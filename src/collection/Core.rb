#===============================================================================
# General gameplay collection. No achievement definitions or evaluation.
#===============================================================================
require "digest/sha2"
require "thread"

module Ironmon
  module GameplayCollection
    SCHEMA_VERSION = 1
    FLUSH_SECONDS = 1.0
    RETRY_SECONDS = 2.0
    BATCH_BYTES = 262_144
    BATCH_EVENTS = 256
    PROFILE_DIRECTORY = "collection-v1"
    @capture_suspended = ENV["IRONMON_GAMEPLAY_COLLECTION_TEST_MODE"] == "suspended"

    class JsonReader
      def initialize(bytes)
        @bytes = bytes.dup.force_encoding(Encoding::UTF_8)
        raise "Invalid UTF-8 in collection JSON." if !@bytes.valid_encoding?
        @offset = 0
      end

      def parse
        result = value(0)
        whitespace
        raise "Trailing input in collection JSON." if @offset != @bytes.bytesize
        return result
      end

      def whitespace
        @offset += 1 while [9, 10, 13, 32].include?(@bytes.getbyte(@offset))
      end

      def take(byte)
        whitespace
        return false if @bytes.getbyte(@offset) != byte
        @offset += 1
        return true
      end

      def value(depth)
        raise "Collection JSON nesting is too deep." if depth > 128
        whitespace
        byte = @bytes.getbyte(@offset)
        return string if byte == 34
        if byte == 123
          @offset += 1
          result = {}
          return result if take(125)
          loop do
            whitespace
            raise "Collection JSON object key is not a string." if @bytes.getbyte(@offset) != 34
            key = string
            raise "Duplicate collection JSON field." if result.key?(key)
            raise "Collection JSON object is missing a colon." if !take(58)
            result[key] = value(depth + 1)
            return result if take(125)
            raise "Collection JSON object is missing a comma." if !take(44)
          end
        end
        if byte == 91
          @offset += 1
          result = []
          return result if take(93)
          loop do
            result << value(depth + 1)
            return result if take(93)
            raise "Collection JSON array is missing a comma." if !take(44)
          end
        end
        { "true" => true, "false" => false, "null" => nil }.each do |literal, parsed|
          if @bytes.byteslice(@offset, literal.bytesize) == literal
            @offset += literal.bytesize
            return parsed
          end
        end
        start = @offset
        @offset += 1 while @bytes.getbyte(@offset) && "-+0123456789.eE".include?(@bytes.getbyte(@offset).chr)
        number = @bytes.byteslice(start, @offset - start)
        raise "Invalid collection JSON value." if !/\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z/.match?(number)
        return Integer(number, 10) if !number.include?(".") && !number.match?(/[eE]/)
        result = Float(number)
        raise "Nonfinite collection JSON number." if !result.finite?
        return result
      end

      def string
        @offset += 1
        result = "".b
        loop do
          byte = @bytes.getbyte(@offset)
          raise "Unterminated collection JSON string." if !byte
          @offset += 1
          if byte == 34
            result.force_encoding(Encoding::UTF_8)
            raise "Invalid UTF-8 collection JSON string." if !result.valid_encoding?
            return result
          end
          if byte == 92
            escaped = @bytes.getbyte(@offset)
            @offset += 1
            if escaped == 117
              point = unicode_point
              if point >= 0xD800 && point <= 0xDBFF
                raise "Missing collection JSON surrogate pair." if @bytes.byteslice(@offset, 2) != "\\u"
                @offset += 2
                low = unicode_point
                raise "Invalid collection JSON surrogate pair." if low < 0xDC00 || low > 0xDFFF
                point = 0x10000 + ((point - 0xD800) << 10) + low - 0xDC00
              elsif point >= 0xDC00 && point <= 0xDFFF
                raise "Unexpected collection JSON low surrogate."
              end
              result << [point].pack("U").b
            else
              replacement = { 34 => 34, 92 => 92, 47 => 47, 98 => 8, 102 => 12, 110 => 10, 114 => 13, 116 => 9 }[escaped]
              raise "Invalid collection JSON escape." if !replacement
              result << replacement.chr(Encoding::BINARY)
            end
          else
            raise "Unescaped collection JSON control character." if byte < 32
            result << byte.chr(Encoding::BINARY)
          end
        end
      end

      def unicode_point
        digits = @bytes.byteslice(@offset, 4)
        raise "Invalid collection JSON unicode escape." if !digits || !/\A[0-9a-fA-F]{4}\z/.match?(digits)
        @offset += 4
        return digits.to_i(16)
      end
    end

    class << self
      attr_accessor :directory_override, :capture_suspended
      attr_reader :last_error, :attempt_id, :branch_id
    end

    def self.uuid
      return Random.urandom(16).unpack("H*")[0]
    end

    def self.clock
      return Process.clock_gettime(Process::CLOCK_MONOTONIC)
    end

    def self.now_us
      return (clock * 1_000_000).to_i.to_s
    end

    def self.directory
      return @directory_override if @directory_override
      local = ENV["LOCALAPPDATA"]
      raise "The gameplay collection needs a local application data directory." if !local || local.empty?
      return File.join(local, "IronmonTracker", PROFILE_DIRECTORY, "producer")
    end

    def self.enabled?
      return false if @disabled || @capture_suspended || !Ironmon.active? || !$Trainer
      return true
    end

    def self.journal
      return @journal if @journal
      @journal = Journal.new(directory)
      emit("collection.context_checkpoint", @journal.recovery_context)
      return @journal
    end

    def self.context_stack
      return Thread.current[:ironmon_gameplay_collection_contexts] ||= []
    end

    def self.current_context
      context = {
        "session_id" => @journal ? @journal.stream_id : nil,
        "save_lineage_id" => @save_lineage_id,
        "attempt_id" => @attempt_id, "branch_id" => @branch_id
      }
      context_stack.each do |entry|
        next if entry[:branch_id] != @branch_id
        context.merge!(entry[:context])
      end
      return context
    end

    def self.with_context(context)
      pushed = false
      begin
        entry = stringify(context)
        stack = context_stack
        stack << { :branch_id => @branch_id, :context => entry }
        pushed = true
      rescue StandardError => error
        fail_collection(error)
      end
      begin
        return yield
      ensure
        stack.pop if pushed
      end
    end

    def self.stringify(value)
      if value.is_a?(Hash)
        result = {}
        value.each { |key, child| result[key.to_s] = stringify(child) }
        return result
      end
      return value.map { |child| stringify(child) } if value.is_a?(Array)
      return value.to_s if value.is_a?(Symbol)
      return value if value.nil? || value == true || value == false || value.is_a?(String) || value.is_a?(Integer)
      return value if value.is_a?(Float) && value.finite?
      return { "coverage" => "unsupported", "runtime_class" => value.class.name }
    end

    def self.encode(value)
      return encode_json_value(value).encode(Encoding::UTF_8)
    end

    JSON_STRING_ESCAPES = {
      "\b" => "\\b", "\t" => "\\t", "\n" => "\\n", "\f" => "\\f", "\r" => "\\r",
      '"' => '\\"', "\\" => "\\\\"
    }.freeze

    def self.encode_json_string(value)
      text = value.to_s
      native_utf8 = text.encoding == Encoding::UTF_8 || text.encoding == Encoding::US_ASCII
      ascii_bytes = text.encoding.ascii_compatible? && text.ascii_only?
      return Ironmon.tracker_json_string(text) if !native_utf8 && !ascii_bytes
      escaped = text.gsub(/["\\\x00-\x1f]/) do |character|
        JSON_STRING_ESCAPES[character] || sprintf("\\u%04x", character.ord)
      end
      return '"' + escaped + '"'
    end

    def self.encode_json_value(value)
      case value
      when Hash
        normalized = {}
        value.each { |key, child| normalized[key.to_s] = child }
        return "{" + normalized.map { |key, child| encode_json_string(key) + ":" + encode_json_value(child) }.join(",") + "}"
      when Array
        return "[" + value.map { |child| encode_json_value(child) }.join(",") + "]"
      when String, Symbol
        return encode_json_string(value)
      when Integer, TrueClass, FalseClass
        return value.to_s
      when NilClass
        return "null"
      when Float
        return value.to_s if value.finite?
      end
      return encode_json_value({ "coverage" => "unsupported", "runtime_class" => value.class.name })
    end

    def self.decode(bytes)
      return JsonReader.new(bytes).parse
    end

    def self.next_id(kind)
      @identity_counter = (@identity_counter || 0) + 1
      return "#{journal.stream_id}:#{kind}:#{@identity_counter}"
    end

    def self.pokemon_instance_id(pokemon)
      return nil if !pokemon
      epoch = journal.epoch_id
      if pokemon.instance_variable_get(:@ironmon_collection_epoch_id) != epoch
        pokemon.instance_variable_set(:@ironmon_collection_epoch_id, epoch)
        pokemon.instance_variable_set(:@ironmon_collection_instance_id, next_id("pokemon"))
      end
      return pokemon.instance_variable_get(:@ironmon_collection_instance_id)
    end

    def self.catalogue_ref(namespace, native_id)
      return nil if native_id.nil?
      return native_id.is_a?(Symbol) ? native_id.to_s : native_id
    end

    def self.emit(type, payload = {}, context = {})
      return nil if !enabled? && !type.to_s.start_with?("collection.")
      payload = NativeEvidence.enrich(type, payload) if defined?(NativeEvidence) && NativeEvidence.respond_to?(:enrich)
      started = clock
      inherited = current_context.merge(stringify(context))
      captured = stringify(payload)
      captured["event_id"] ||= next_id("event")
      captured["tracking_epoch_id"] ||= journal.epoch_id
      captured["attempt_instance_id"] ||= inherited["attempt_id"]
      captured["battle_instance_id"] ||= inherited["battle_instance_id"] || inherited["battle_id"]
      captured["cause_event_id"] ||= inherited["cause_event_id"]
      event = {
        "type" => type.to_s, "attempt_id" => inherited["attempt_id"],
        "branch_id" => inherited["branch_id"], "battle_id" => inherited["battle_instance_id"] || inherited["battle_id"],
        "occurred_at_us" => now_us, "context" => inherited,
        "payload" => captured
      }
      sequence = journal.append(event)
      NativeEvidence.observe_fact(type, captured, sequence) if defined?(NativeEvidence) && NativeEvidence.respond_to?(:observe_fact)
      @capture_count = (@capture_count || 0) + 1
      @capture_seconds = (@capture_seconds || 0.0) + clock - started
      return sequence
    rescue StandardError => error
      fail_collection(error)
      return nil
    end

    def self.open_group(kind, context = {})
      return nil if !enabled?
      id = next_id("group")
      @group_contexts ||= {}
      @group_contexts[id] = current_context.merge(stringify(context))
      sequence = emit("collection.effect_group_opened", { "effect_group_id" => id, "kind" => kind.to_s, "parent_effect_group_id" => current_context["effect_group_id"] }, context)
      return id if sequence
      @group_contexts.delete(id)
      return nil
    rescue StandardError => error
      @group_contexts.delete(id) if @group_contexts && id
      fail_collection(error)
      return nil
    end

    def self.close_group(id, outcome = {}, completion_kind = "completed")
      return if !id
      return if !@group_contexts || !@group_contexts.key?(id)
      pending = Thread.current[:ironmon_collection_field_item_results]
      deferred = pending && pending.find { |entry| entry[:group] == id }
      if deferred
        deferred[:closure] = [outcome, completion_kind]
        return
      end
      context = @group_contexts.delete(id)
      emit("collection.effect_group_closed", {
        "effect_group_id" => id, "last_member_sequence" => journal.sequence.to_s,
        "completion_kind" => completion_kind.to_s, "outcome" => outcome
      }, context)
      ItemIdentity.close_group(id) if defined?(ItemIdentity)
      SourceContext.close_group(id) if defined?(SourceContext)
    rescue StandardError => error
      fail_collection(error)
    end

    def self.with_group(kind, context = {})
      group = open_group(kind, context)
      completed = false
      result = with_context(context.merge("effect_group_id" => group)) { yield(group) }
      completed = true
      return result
    ensure
      close_group(group, {}, completed ? "completed" : "interrupted") if group
    end

    def self.flush
      journal.fence if @journal
    rescue StandardError => error
      fail_collection(error)
    end

    def self.fail_collection(error)
      @last_error = "#{error.class}: #{error.message}"
      begin
        echoln "Ironmon gameplay collection: #{@last_error}" if @last_reported_error != @last_error && respond_to?(:echoln, true)
      rescue StandardError
        # An optional diagnostic sink cannot turn a collection failure into a game failure.
      end
      @last_reported_error = @last_error
      @disabled = true
    end

    def self.metrics
      return {
        "schema_version" => SCHEMA_VERSION, "status" => @last_error ? "error" : "active",
        "capture_events" => @capture_count || 0, "capture_seconds" => @capture_seconds || 0.0,
        "last_error" => @last_error, "journal" => @journal ? @journal.metrics : nil
      }
    end

    def self.close_at_process_exit
      return if !@journal
      begin
        World.leave_loaded("process_exit") if defined?(World) && World.respond_to?(:leave_loaded)
      rescue StandardError => error
        fail_collection(error)
      ensure
        begin
          emit("collection.stream_closed", { "retirement_kind" => "closed", "reason" => "process_exit" })
          @journal.close
        rescue StandardError => error
          fail_collection(error)
        end
      end
    end

    if !@shutdown_registered
      @shutdown_registered = true
      at_exit { Ironmon::GameplayCollection.close_at_process_exit }
    end
  end
end
