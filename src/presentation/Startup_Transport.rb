module Ironmon
  module StartupTransport
    MAXIMUM_QUEUED_MESSAGES = 64
    MAXIMUM_QUEUED_BYTES = TRACKER_MAXIMUM_MESSAGE_BYTES
    MAXIMUM_READ_BYTES = TRACKER_OUTPUT_WRITE_BYTES * 2
    READ_BYTES = 16_384

    def self.active?
      return defined?(Ironmon::StartupPreparation) && Ironmon::StartupPreparation.active?
    end

    module Connection
      def send_startup_preparation_progress(payload)
        return false if !connected?
        queue_message({ "schema_version" => TRACKER_SCHEMA_VERSION,
          "type" => "event", "event" => "startup_preparation_progress",
          "run_id" => "", "battle_id" => nil, "sequence" => 0,
          "sent_at" => Ironmon.tracker_timestamp, "payload" => payload })
        flush_output
        return connected?
      rescue Exception => error
        disconnect(error)
        return false
      end

      def update
        return update_loading if StartupTransport.active?
        replay_startup_messages
        return update_loading if StartupTransport.active?
        return super
      rescue Exception => error
        disconnect(error)
      end

      def update_loading
        Ironmon.clear_tracker_controls
        return false if !connected?
        flush_output
        return false if !connected?
        read_input
        flush_output if connected?
        return connected?
      rescue Exception => error
        disconnect(error)
        return false
      end

      private

      def handle_message(message)
        return super if !StartupTransport.active?
        validate_envelope(message)
        if message["type"] == "event"
          event = message["event"]
          if event == "controls_state"
            Ironmon.clear_tracker_controls
            return
          elsif event == "tracker_heartbeat"
            payload = message["payload"]
            @last_heartbeat = Ironmon.tracker_uptime_seconds if payload.is_a?(Hash) &&
              payload["session_id"] == Ironmon.game_session_id
            return
          elsif event == "tracker_connected"
            enqueue_startup_message(message)
            payload = message["payload"]
            if payload.is_a?(Hash) && payload["management_protocol_version"] == GAME_MANAGEMENT_PROTOCOL &&
                payload["session_id"] == Ironmon.game_session_id && payload["game_root"] == File.expand_path(".")
              @session_ready = true
              @last_heartbeat = Ironmon.tracker_uptime_seconds
            end
            return
          end
        end
        enqueue_startup_message(message)
      end

      def enqueue_startup_message(message)
        @startup_messages ||= []
        @startup_message_bytes ||= 0
        bytes = Ironmon.tracker_json_generate(message).bytesize
        if @startup_messages.length >= StartupTransport::MAXIMUM_QUEUED_MESSAGES ||
            bytes > StartupTransport::MAXIMUM_QUEUED_BYTES - @startup_message_bytes
          raise "Ironmon tracker startup loading queue exceeded its message or byte limit; reconnect before retrying pending messages."
        end
        @startup_messages << [message, bytes]
        @startup_message_bytes += bytes
      end

      def replay_startup_messages
        while connected? && !StartupTransport.active? && @startup_messages && !@startup_messages.empty?
          message, bytes = @startup_messages.shift
          @startup_message_bytes -= bytes
          handle_message(message)
        end
      end

      def read_input
        return super if !StartupTransport.active?
        bytes = 0
        while connected? && bytes < StartupTransport::MAXIMUM_READ_BYTES
          return if !IO.select([@socket], nil, nil, 0)
          data = @socket.recv([StartupTransport::READ_BYTES, StartupTransport::MAXIMUM_READ_BYTES - bytes].min)
          if data.empty?
            disconnect
            return
          end
          bytes += data.bytesize
          @input_buffer << data
          raise "Ironmon tracker input exceeded the framing limit." if @input_buffer.bytesize > TRACKER_MAXIMUM_MESSAGE_BYTES
          process_input_lines
        end
      rescue Errno::ECONNRESET, Errno::ECONNABORTED, IOError
        disconnect
      end

      def connection_established
        clear_startup_messages
        return super
      end

      def disconnect(error = nil)
        clear_startup_messages
        return super
      end

      def clear_startup_messages
        @startup_messages = []
        @startup_message_bytes = 0
      end
    end
  end
end

Ironmon::TrackerConnection.prepend(Ironmon::StartupTransport::Connection)
