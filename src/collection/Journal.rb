#===============================================================================
# Game-owned retry journal. Only unacknowledged input remains on disk.
#===============================================================================
require "zlib"

module Ironmon
  module GameplayCollection
    class Journal
      SEGMENT_BYTES = 1_048_576
      HEADER_BYTES = 36
      MAX_RECORD_BYTES = 16_777_216
      COMPRESSED_RECORD_MAGIC = "\xFFCJ1".b.freeze
      attr_reader :profile_id, :epoch_id, :stream_id, :recovery_context

      def initialize(directory, compress: true)
        @compress_records = compress
        @raw_written_bytes = 0
        @compression_seconds = 0.0
        @compressed_records = 0
        @directory = File.expand_path(directory)
        mkdir(@directory)
        @lock_file = File.open(File.join(@directory, "producer.lock"), "a+b")
        raise "Another game owns the gameplay collection producer." if !@lock_file.flock(File::LOCK_EX | File::LOCK_NB)
        @mutex = Mutex.new
        @manifest_mutex = Mutex.new
        @read_mutex = Mutex.new
        @changed = ConditionVariable.new
        @queue = []
        @acknowledgements = []
        @indexes = {}
        @segments = {}
        @error = nil
        @pending_incarnation_id = nil
        @closed = false
        @flush_count = 0
        @flush_seconds = 0.0
        @written_bytes = 0
        @state = read_state
        @profile_id = @state["profile_id"]
        @epoch_id = @state["epoch_id"]
        @state["streams"].each { |stream| recover(stream) }
        @published_acknowledgements = {}
        @state["streams"].each { |stream| @published_acknowledgements[stream["stream_id"]] = Integer(stream["acknowledged_sequence"]) }
        @state["streams"].reject! { |stream| Integer(stream["durable_sequence"]) == 0 }
        predecessor = @state["streams"].last
        @recovery_context = {
          "retirement_kind" => predecessor ? (predecessor["closed"] ? "closed" : "recovered_interrupted") : "none",
          "predecessor_stream_id" => predecessor ? predecessor["stream_id"] : nil,
          "predecessor_final_sequence" => predecessor ? predecessor["durable_sequence"] : nil,
          "predecessor_acknowledged_sequence" => predecessor ? predecessor["acknowledged_sequence"] : nil
        }
        predecessor["closed"] = true if predecessor
        @stream_id = GameplayCollection.uuid
        @current = {
          "stream_id" => @stream_id, "predecessor_stream_id" => predecessor ? predecessor["stream_id"] : nil,
          "predecessor_final_sequence" => predecessor ? predecessor["durable_sequence"] : nil,
          "durable_sequence" => "0", "acknowledged_sequence" => "0", "closed" => false
        }
        @state["streams"] << @current
        @indexes[@stream_id] = {}
        @sequence = 0
        @durable_sequence = 0
        @requested_fence = 0
        @segment_number = 0
        @segment_file = nil
        write_state
        @worker = Thread.new { writer_loop }
      rescue Exception
        @lock_file.close if @lock_file && !@lock_file.closed?
        raise
      end

      def mkdir(path)
        return if File.directory?(path)
        parent = File.dirname(path)
        mkdir(parent) if parent != path && !File.directory?(parent)
        Dir.mkdir(path) if !File.directory?(path)
      end

      def read_state
        paths = ["binding-a.json", "binding-b.json"].map { |name| File.join(@directory, name) }
        existing = paths.select { |path| File.file?(path) }
        candidates = existing.filter_map do |path|
          begin
            envelope = GameplayCollection.decode(File.binread(path))
            payload = envelope["payload"]
            next if !payload.is_a?(String) || Digest::SHA256.hexdigest(payload) != envelope["sha256"]
            state = GameplayCollection.decode(payload)
            next if state["schema_version"] != SCHEMA_VERSION || !state["streams"].is_a?(Array)
            next if ![state["profile_id"], state["epoch_id"]].all? { |id| id.is_a?(String) && /\A[0-9a-f]{32}\z/.match?(id) }
            state
          rescue StandardError
            nil
          end
        end
        raise "The gameplay collection binding is damaged; it was not reset." if !existing.empty? && candidates.empty?
        return candidates.max_by { |state| state["generation"].to_i } if !candidates.empty?
        return {
          "schema_version" => SCHEMA_VERSION, "generation" => 0,
          "profile_id" => GameplayCollection.uuid, "epoch_id" => GameplayCollection.uuid,
          "store_incarnation_id" => nil, "streams" => []
        }
      end

      def state_snapshot
        @state["generation"] += 1
        payload = GameplayCollection.encode(@state)
        bytes = GameplayCollection.encode({ "payload" => payload, "sha256" => Digest::SHA256.hexdigest(payload) })
        target = File.join(@directory, @state["generation"].odd? ? "binding-a.json" : "binding-b.json")
        return [target, bytes]
      end

      def write_state(snapshot = nil)
        target, bytes = snapshot || state_snapshot
        temporary = File.join(@directory, "binding-writing.tmp")
        File.open(temporary, "wb") { |file| file.write(bytes); file.flush; file.fsync }
        File.delete(target) if File.file?(target)
        File.rename(temporary, target)
      end

      def stream_directory(id)
        raise "Invalid gameplay collection stream." if !/\A[0-9a-f]{32}\z/.match?(id)
        return File.join(@directory, "streams", id)
      end

      def recover(stream)
        id = stream["stream_id"]
        directory = stream_directory(id)
        @indexes[id] = {}
        acknowledged = Integer(stream["acknowledged_sequence"])
        durable = Integer(stream["durable_sequence"])
        maximum = acknowledged
        files = File.directory?(directory) ? Dir.children(directory).grep(/\Asegment-.*\.journal\z/).sort.map { |name| File.join(directory, name) } : []
        files.each_with_index do |path, file_index|
          valid_end = 0
          File.open(path, "r+b") do |file|
            loop do
              offset = file.pos
              header = file.read(HEADER_BYTES)
              break if !header || header.empty?
              if header.bytesize != HEADER_BYTES
                raise "Damaged sealed collection journal." if file_index != files.length - 1 || stream["closed"]
                file.truncate(valid_end)
                break
              end
              length = header.byteslice(0, 4).unpack("N")[0]
              if length == 0 || length > MAX_RECORD_BYTES
                raise "Damaged committed collection journal framing." if file_index != files.length - 1 || stream["closed"] || maximum < durable
                file.truncate(valid_end)
                break
              end
              payload = file.read(length)
              valid = payload && payload.bytesize == length && Digest::SHA256.digest(payload) == header.byteslice(4, 32)
              if !valid
                raise "Damaged committed collection journal." if file_index != files.length - 1 || stream["closed"] || maximum < durable
                file.truncate(valid_end)
                break
              end
              raw = decode_record(payload)
              event = GameplayCollection.decode(raw)
              sequence = Integer(event.fetch("sequence"))
              raise "Collection journal sequence is invalid." if sequence <= 0 || sequence > 18_446_744_073_709_551_615
              raise "Collection journal has a sequence gap." if sequence > acknowledged && sequence != maximum + 1
              maximum = [maximum, sequence].max
              @indexes[id][sequence] = [path, offset + HEADER_BYTES, length, raw.bytesize] if sequence > acknowledged
              (@segments[path] ||= { "stream_id" => id, "last_sequence" => 0 })["last_sequence"] = sequence
              valid_end = file.pos
            end
          end
        end
        raise "Acknowledged collection metadata exceeds retained input." if maximum < durable
        stream["durable_sequence"] = maximum.to_s
      end

      def sequence
        return @mutex.synchronize { @sequence }
      end

      def runtime_context(key)
        @mutex.synchronize do
          contexts = @state["runtime_contexts"] || {}
          value = contexts[key.to_s]
          return value ? GameplayCollection.decode(GameplayCollection.encode(value)) : nil
        end
      end

      def bind_runtime_context(key, value)
        @manifest_mutex.synchronize do
          snapshot = @mutex.synchronize do
            raise @error if @error
            @state["runtime_contexts"] ||= {}
            @state["runtime_contexts"][key.to_s] = GameplayCollection.stringify(value)
            state_snapshot
          end
          write_state(snapshot)
        end
      end

      def append(event)
        @mutex.synchronize do
          raise @error if @error
          raise "The gameplay collection journal is closed." if @closed
          sequence = @sequence + 1
          raise "Gameplay collection sequence exhausted." if sequence > 18_446_744_073_709_551_615
          event["sequence"] = sequence.to_s
          event["payload"]["sequence"] = sequence.to_s
          payload = GameplayCollection.encode(event)
          raise "A gameplay collection fact exceeds the 16 MiB journal record limit." if payload.bytesize > MAX_RECORD_BYTES
          @queue << [sequence, payload]
          @sequence = sequence
          @changed.signal
          return sequence.to_s
        end
      end

      def fence
        @mutex.synchronize do
          raise @error if @error
          @requested_fence = @sequence
          @changed.signal
          @changed.wait(@mutex, 0.1) while @durable_sequence < @requested_fence && !@error
          raise @error if @error
        end
      end

      def start_batch
        @read_mutex.synchronize do
          @mutex.synchronize do
            raise @error if @error
            stream = @state["streams"].find { |entry| Integer(entry["acknowledged_sequence"]) < Integer(entry["durable_sequence"]) }
            return nil if !stream
            first = Integer(stream["acknowledged_sequence"]) + 1
            limit = Integer(stream["durable_sequence"])
            indexes = []
            bytes = 0
            first.upto(limit) do |sequence|
              indexed = @indexes.fetch(stream["stream_id"]).fetch(sequence)
              break if !indexes.empty? && (bytes + indexed[3] > BATCH_BYTES || indexes.length >= BATCH_EVENTS)
              indexes << [sequence, indexed.dup]
              bytes += indexed[3]
            end
            return { "stream" => stream.dup, "indexes" => indexes, "records" => [], "buffers" => {}, "position" => 0 }
          end
        end
      end

      def advance_batch(prepared, max_records: 8, budget_ms: 1.0, blocking: false)
        if blocking
          @read_mutex.lock
        else
          return nil if !@read_mutex.try_lock
        end
        begin
          stream = prepared["stream"]
          indexes = prepared["indexes"]
          stale = @mutex.synchronize do
            raise @error if @error
            current = @state["streams"].find { |entry| entry["stream_id"] == stream["stream_id"] }
            !current || Integer(current["acknowledged_sequence"]) >= indexes[0][0]
          end
          if stale
            prepared["cancelled"] = true
            return nil
          end
          started = GameplayCollection.clock
          count = 0
          while prepared["position"] < indexes.length && count < max_records
            indexed = indexes[prepared["position"]][1]
            buffer = prepared["buffers"][indexed[0]]
            if !buffer
              same_file = indexes.select { |entry| entry[1][0] == indexed[0] }
              first = same_file.first[1]
              last = same_file.last[1]
              length = last[1] + last[2] - first[1]
              bytes = File.open(indexed[0], "rb") { |file| file.seek(first[1]); file.read(length) }
              raise "A pending collection journal range is truncated." if !bytes || bytes.bytesize != length
              buffer = [first[1], bytes]
              prepared["buffers"][indexed[0]] = buffer
            end
            raw = buffer[1].byteslice(indexed[1] - buffer[0], indexed[2])
            raise "A pending collection journal record is truncated." if !raw || raw.bytesize != indexed[2]
            raw = decode_record(raw)
            raw.force_encoding(Encoding::UTF_8)
            raise "A pending collection journal record is not valid UTF-8." if !raw.valid_encoding?
            prepared["records"] << raw
            prepared["position"] += 1
            count += 1
            break if budget_ms && (GameplayCollection.clock - started) * 1000 >= budget_ms
          end
          return nil if prepared["position"] < indexes.length
          header = {
            "schema_version" => SCHEMA_VERSION, "profile_id" => @profile_id, "epoch_id" => @epoch_id,
            "stream_id" => stream["stream_id"], "predecessor_stream_id" => stream["predecessor_stream_id"],
            "predecessor_final_sequence" => stream["predecessor_final_sequence"]
          }
          encoded = GameplayCollection.encode(header)
          body = encoded.byteslice(0, encoded.bytesize - 1) + ',"events":[' + prepared["records"].join(",") + "]}"
          return { "json" => body, "stream_id" => stream["stream_id"], "last_sequence" => indexes[-1][0].to_s, "events" => indexes.length }
        ensure
          @read_mutex.unlock
        end
      end

      def next_batch
        loop do
          prepared = start_batch
          return nil if !prepared
          batch = advance_batch(prepared, max_records: BATCH_EVENTS, budget_ms: nil, blocking: true)
          return batch if batch
        end
      end

      def acknowledge(payload)
        @mutex.synchronize do
          return false if payload["profile_id"] != @profile_id || payload["epoch_id"] != @epoch_id
          incarnation = payload["store_incarnation_id"]
          raise "Invalid collection store acknowledgement." if !incarnation.is_a?(String) || !/\A[0-9a-f]{32}\z/.match?(incarnation)
          expected = @state["store_incarnation_id"] || @pending_incarnation_id
          raise "The collection database incarnation changed; pending input was retained." if expected && expected != incarnation
          stream = @state["streams"].find { |entry| entry["stream_id"] == payload["stream_id"] }
          return false if !stream
          sequence = Integer(payload.fetch("durable_prefix"))
          return false if sequence < Integer(stream["acknowledged_sequence"]) || sequence > Integer(stream["durable_sequence"])
          @pending_incarnation_id ||= incarnation
          @acknowledgements << [stream["stream_id"], sequence, incarnation]
          @changed.signal
          return true
        end
      end

      def acknowledged?(stream_id, prefix)
        @mutex.synchronize do
          raise @error if @error
          return @published_acknowledgements.fetch(stream_id, 0) >= Integer(prefix)
        end
      end

      def open_segment
        @segment_file.flush if @segment_file
        @segment_file.fsync if @segment_file
        @segment_file.close if @segment_file
        @segment_number += 1
        directory = stream_directory(@stream_id)
        mkdir(directory)
        @segment_path = File.join(directory, sprintf("segment-%08d.journal", @segment_number))
        @segment_file = File.open(@segment_path, "ab")
      end

      def write_records(records)
        records.each_with_index do |(sequence, raw), ordinal|
          payload = encode_record(raw)
          open_segment if !@segment_file || @segment_file.pos + payload.bytesize + HEADER_BYTES > SEGMENT_BYTES
          offset = @segment_file.pos
          @segment_file.write([payload.bytesize].pack("N") + Digest::SHA256.digest(payload) + payload.b)
          @mutex.synchronize do
            @indexes[@stream_id][sequence] = [@segment_path, offset + HEADER_BYTES, payload.bytesize, raw.bytesize]
            @segments[@segment_path] = { "stream_id" => @stream_id, "last_sequence" => sequence }
          end
          @written_bytes += payload.bytesize + HEADER_BYTES
          @raw_written_bytes += raw.bytesize + HEADER_BYTES
          Thread.pass if ordinal % 8 == 7
        end
      end

      def encode_record(raw)
        return raw if !@compress_records
        started = GameplayCollection.clock
        compressed = Zlib::Deflate.deflate(raw, 1)
        @compression_seconds += GameplayCollection.clock - started
        return raw if compressed.bytesize + 8 >= raw.bytesize
        @compressed_records += 1
        return COMPRESSED_RECORD_MAGIC + [raw.bytesize].pack("N") + compressed
      end

      def decode_record(payload)
        return payload if !payload.start_with?(COMPRESSED_RECORD_MAGIC)
        raise "Truncated compressed collection record." if payload.bytesize <= 8
        expected = payload.byteslice(4, 4).unpack("N")[0]
        raise "Invalid compressed collection record size." if expected < 1 || expected > MAX_RECORD_BYTES
        source = payload.byteslice(8, payload.bytesize - 8)
        result = "".b
        inflater = Zlib::Inflate.new
        begin
          inflater.inflate(source) do |chunk|
            raise "Compressed collection record exceeds its declared size." if result.bytesize + chunk.bytesize > expected
            result << chunk
          end
          raise "Incomplete compressed collection record." if !inflater.finished? || inflater.total_in != source.bytesize || result.bytesize != expected
        ensure
          inflater.close
        end
        return result
      end

      def commit_records(last_sequence)
        started = GameplayCollection.clock
        @segment_file.flush if @segment_file
        @segment_file.fsync if @segment_file
        @manifest_mutex.synchronize do
          snapshot = @mutex.synchronize do
            @current["durable_sequence"] = last_sequence.to_s
            state_snapshot
          end
          write_state(snapshot)
          @mutex.synchronize do
            @durable_sequence = last_sequence
            @flush_count += 1
            @flush_seconds += GameplayCollection.clock - started
            @changed.broadcast
          end
        end
      end

      def apply_acknowledgements(acknowledgements)
        return if acknowledgements.empty?
        @manifest_mutex.synchronize do
          snapshot = @mutex.synchronize do
            acknowledgements.each do |id, sequence, incarnation|
              expected = @state["store_incarnation_id"] || @pending_incarnation_id
              raise "The collection database incarnation changed; pending input was retained." if expected && expected != incarnation
              stream = @state["streams"].find { |entry| entry["stream_id"] == id }
              next if !stream
              @state["store_incarnation_id"] ||= incarnation
              next if sequence <= Integer(stream["acknowledged_sequence"])
              stream["acknowledged_sequence"] = sequence.to_s
            end
            state_snapshot
          end
          write_state(snapshot)
          @mutex.synchronize do
            acknowledgements.each do |id, sequence, _incarnation|
              @published_acknowledgements[id] = [@published_acknowledgements.fetch(id, 0), sequence].max
            end
          end
          @read_mutex.synchronize do
            deletions = @mutex.synchronize do
              @state["streams"].each do |stream|
                acknowledged = Integer(stream["acknowledged_sequence"])
                @indexes[stream["stream_id"]].delete_if { |sequence, _index| sequence <= acknowledged }
              end
              @segments.keys.select do |path|
                segment = @segments[path]
                stream = @state["streams"].find { |entry| entry["stream_id"] == segment["stream_id"] }
                segment["last_sequence"] <= Integer(stream["acknowledged_sequence"])
              end
            end
            deletions.each do |path|
              if path == @segment_path
                @segment_file.close if @segment_file && !@segment_file.closed?
                @segment_file = nil
                @segment_path = nil
              end
              File.delete(path) if File.file?(path)
              @mutex.synchronize { @segments.delete(path) }
            end
          end
        end
      end

      def writer_loop
        last_flush = GameplayCollection.clock
        written = 0
        loop do
          records, acknowledgements, stopping, requested = @mutex.synchronize do
            @changed.wait(@mutex, 0.05) if @queue.empty? && @acknowledgements.empty? && !@closed
            result = [@queue, @acknowledgements, @closed, @requested_fence]
            @queue = []
            @acknowledgements = []
            result
          end
          write_records(records)
          written = records[-1][0] if !records.empty?
          now = GameplayCollection.clock
          if written > @durable_sequence && (stopping || requested > @durable_sequence || now - last_flush >= FLUSH_SECONDS)
            commit_records(written)
            last_flush = now
          end
          apply_acknowledgements(acknowledgements)
          break if stopping && records.empty?
        end
      rescue StandardError => error
        @mutex.synchronize { @error = error; @changed.broadcast }
      ensure
        @segment_file.close if @segment_file && !@segment_file.closed?
      end

      def metrics
        @mutex.synchronize do
          return {
            "profile_id" => @profile_id, "epoch_id" => @epoch_id, "stream_id" => @stream_id,
            "sequence" => @sequence.to_s, "durable_sequence" => @durable_sequence.to_s,
            "queued_events" => @queue.length, "pending_events" => @indexes.values.sum(&:length),
            "journal_bytes_written" => @written_bytes, "flushes" => @flush_count,
            "raw_journal_bytes" => @raw_written_bytes, "compressed_records" => @compressed_records,
            "compression_seconds" => @compression_seconds,
            "flush_seconds" => @flush_seconds, "error" => @error ? @error.message : nil
          }
        end
      end

      def close
        return if @lock_file.closed?
        begin
          fence
          @mutex.synchronize { @closed = true; @changed.broadcast }
          @worker.join
          raise @error if @error
          @manifest_mutex.synchronize do
            snapshot = @mutex.synchronize do
              @current["closed"] = true
              state_snapshot
            end
            write_state(snapshot)
          end
        ensure
          @mutex.synchronize { @closed = true; @changed.broadcast }
          @worker.join if @worker && @worker.alive?
          @lock_file.flock(File::LOCK_UN)
          @lock_file.close
        end
      end
    end
  end
end
