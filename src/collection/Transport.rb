#===============================================================================
# Bounded transport of the durable outbox. No game fact waits for tracker I/O.
#===============================================================================
module Ironmon
  module GameplayCollection
    TRANSPORT_CHUNK_BYTES = 131_072
    TRANSPORT_MAXIMUM_BATCH_BYTES = 16_777_216

    def self.transport_tick(connection)
      return if !@journal || !connection.session_ready? || !connection.collection_supported?
      now = clock
      return if !connection.output_drained?
      if @transfer && @transfer["acknowledged"]
        return if !journal.acknowledged?(@transfer["stream_id"], @transfer["last_sequence"])
        @transfer = nil
      end
      if !@status_sent_at || now - @status_sent_at >= 10.0
        connection.send_event("collection_status", metrics.merge("transport_error" => @transport_error))
        @status_sent_at = now
        return
      end
      if @transfer && now < @transfer["next_send_at"]
        return
      end
      if !@transfer
        batch = prepared_transfer(connection)
        return if !batch
        raw = batch["json"]
        if raw.bytesize > TRANSPORT_MAXIMUM_BATCH_BYTES
          @transport_error = "A collection batch exceeds the supported 16 MiB transfer size; its source remains pending."
          return
        end
        @transfer = batch.merge("index" => 0, "next_send_at" => 0.0)
      elsif @transfer["waiting"]
        # A lost fragment or acknowledgement restarts the bounded transfer. Input
        # receipts in SQLite make a retry of a committed batch harmless.
        @transfer["index"] = 0
      end
      transfer = @transfer
      raw = transfer["json"]
      if transfer["compressed_json"] && connection.collection_compression_supported?
        connection.send_collection_json("collection_compressed_batch", transfer["compressed_json"])
      elsif raw.bytesize <= BATCH_BYTES + 4096
        connection.send_collection_json("collection_batch", raw)
      else
        index = transfer["index"]
        count = (raw.bytesize + TRANSPORT_CHUNK_BYTES - 1) / TRANSPORT_CHUNK_BYTES
        bytes = raw.byteslice(index * TRANSPORT_CHUNK_BYTES, TRANSPORT_CHUNK_BYTES)
        connection.send_event("collection_chunk", {
          "transfer_id" => transfer["sha256"], "sha256" => transfer["sha256"],
          "index" => index, "count" => count, "total_bytes" => raw.bytesize,
          "data" => [bytes].pack("m0")
        })
      end
      transfer["waiting"] = true
      transfer["next_send_at"] = now + RETRY_SECONDS
    rescue StandardError => error
      @transport_error = "#{error.class}: #{error.message}"
    end

    def self.prepared_transfer(connection)
      @transfer_preparation ||= journal.start_batch
      return nil if !@transfer_preparation
      batch = journal.advance_batch(@transfer_preparation)
      if @transfer_preparation["cancelled"]
        @transfer_preparation = nil
        return nil
      end
      return nil if !batch
      @transfer_preparation = nil
      raw = batch["json"]
      raise "A collection batch exceeds the supported transfer size." if raw.bytesize > TRANSPORT_MAXIMUM_BATCH_BYTES
      batch["sha256"] = Digest::SHA256.hexdigest(raw)
      if connection.collection_compression_supported?
        compressed = Zlib::Deflate.deflate(raw, 1)
        # Only generated decimal, hex and base64 enter this envelope.
        wrapped = '{"encoding_version":1,"uncompressed_bytes":' + raw.bytesize.to_s + ',"sha256":"' + batch["sha256"] + '","data":"' + [compressed].pack("m0") + '"}'
        batch["compressed_json"] = wrapped if wrapped.bytesize < raw.bytesize && wrapped.bytesize <= BATCH_BYTES + 4096
      end
      return batch
    end

    def self.receive_transport(event, payload)
      return if !@journal
      if event == "collection_acknowledged"
        if journal.acknowledge(payload)
          @transfer["acknowledged"] = true if @transfer && @transfer["stream_id"] == payload["stream_id"] && Integer(payload["durable_prefix"]) >= Integer(@transfer["last_sequence"])
          @transport_error = nil
        end
      elsif event == "collection_chunk_acknowledged"
        return if !@transfer || payload["transfer_id"] != @transfer["sha256"] || payload["index"] != @transfer["index"]
        @transfer["index"] += 1
        @transfer["waiting"] = false
        @transfer["next_send_at"] = 0.0
      elsif event == "collection_rejected"
        @transport_error = payload["message"].to_s
        @transfer = nil
        @retry_after = clock + 10.0
      end
    rescue StandardError => error
      fail_collection(error)
    end

    module ConnectionHooks
      def achievement_notifications_supported?
        return @achievement_protocol_version == 3
      end
      def achievement_overlay_supported?
        return @achievement_overlay_version == 1
      end
      def collection_supported?
        return @collection_protocol_version == SCHEMA_VERSION
      end

      def collection_compression_supported?
        return @collection_compression_version == 1
      end

      def send_collection_json(event, raw)
        return false if !connected?
        message = event_message(event, nil)
        encoded = GameplayCollection.encode(message)
        # event_message writes payload last, but replace its null by key rather
        # than assuming the surrounding envelope's ordering.
        encoded = encoded.sub(/"payload":null(?=[,}])/) { '"payload":' + raw }
        raise "A collection transfer exceeds tracker framing." if encoded.bytesize > Ironmon::TRACKER_MAXIMUM_MESSAGE_BYTES
        @output_buffer << encoded << "\n"
        return true
      end

      def update
        result = super
        if !GameplayCollection.instance_variable_get(:@retry_after) || GameplayCollection.clock >= GameplayCollection.instance_variable_get(:@retry_after)
          GameplayCollection.transport_tick(self)
        end
        return result
      end

      private

      def game_handshake_message
        message = super
        message["payload"]["achievement_protocol_version"] = 3
        message["payload"]["achievement_overlay_version"] = 1
        return message
      end

      def handle_message(message)
        event = message["event"].to_s
        if message["type"] == "event" && event == "achievement_overlay_ready"
          validate_envelope(message)
          Ironmon::Achievements.overlay_ready(message["payload"] || {})
          return
        end
        if message["type"] == "event" && event == "achievement_unlocks"
          validate_envelope(message)
          Ironmon::Achievements.receive(message["payload"] || {})
          return
        end
        if message["type"] == "event" && ["collection_acknowledged", "collection_chunk_acknowledged", "collection_rejected"].include?(event)
          validate_envelope(message)
          GameplayCollection.receive_transport(event, message["payload"] || {})
          return
        end
        result = super
        if event == "tracker_connected"
          @collection_protocol_version = (message["payload"] || {})["collection_protocol_version"]
          @collection_compression_version = (message["payload"] || {})["collection_compression_version"]
          @achievement_protocol_version = (message["payload"] || {})["achievement_protocol_version"]
          @achievement_overlay_version = (message["payload"] || {})["achievement_overlay_version"]
        end
        return result
      end

      def disconnect(error = nil)
        @achievement_protocol_version = nil
        @achievement_overlay_version = nil
        @collection_protocol_version = nil
        @collection_compression_version = nil
        GameplayCollection.instance_variable_set(:@transfer, nil)
        GameplayCollection.instance_variable_set(:@transfer_preparation, nil)
        return super
      end
    end
  end
end

Ironmon::TrackerConnection.prepend(Ironmon::GameplayCollection::ConnectionHooks)
