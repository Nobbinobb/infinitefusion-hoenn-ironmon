#===============================================================================
# Ironmon tracker connection
#===============================================================================

require "socket"

module Ironmon
  TRACKER_HOST = "127.0.0.1"
  TRACKER_PORT = 38_521
  TRACKER_SCHEMA_VERSION = 1
  TRACKER_RECONNECT_SECONDS = 2.0
  TRACKER_MAXIMUM_MESSAGE_BYTES = 1_048_576

  class TrackerConnection
    def initialize
      @socket = nil
      @connect_thread = nil
      @pending_socket = nil
      @connect_error = nil
      @state = :disconnected
      @next_attempt_at = 0.0
      @input_buffer = ""
      @output_buffer = ""
      @last_error = nil
      @last_error_at = 0.0
      @debug_requested = false
    end

    def update
      if @state == :disconnected
        begin_connect if System.uptime >= @next_attempt_at
      elsif @state == :connecting
        finish_connect
      elsif @state == :connected
        flush_output
        read_input
        flush_output
      end
    rescue Exception => e
      disconnect(e)
    end

    def send_run_started
      return if @state != :connected
      queue_message(event_message("run_started", Ironmon.tracker_current_state))
    end

    private

    def begin_connect
      @pending_socket = nil
      @connect_error = nil
      @connect_thread = Thread.new do
        begin
          @pending_socket = TCPSocket.new(TRACKER_HOST, TRACKER_PORT)
        rescue Exception => e
          @connect_error = e
        end
      end
      @state = :connecting
    end

    def finish_connect
      return if @connect_thread.alive?
      @connect_thread.join
      if @connect_error
        disconnect(@connect_error)
        return
      end
      @socket = @pending_socket
      if !@socket
        disconnect(RuntimeError.new("Ironmon tracker connection did not return a socket."))
        return
      end
      connection_established
    end

    def connection_established
      @state = :connected
      @input_buffer = ""
      @output_buffer = ""
      @last_error = nil
      @connect_thread = nil
      @pending_socket = nil
      @connect_error = nil
      queue_message(game_handshake_message)
    end

    def game_handshake_message
      run_id = Ironmon.ensure_tracker_run_id
      payload = {
        "game_version" => Ironmon.tracker_game_version,
        "ironmon_version" => Ironmon::VERSION,
        "ironmon_active" => Ironmon.active?,
        "debug_available" => ($DEBUG == true),
        "game_root" => File.expand_path("."),
        "run_id" => run_id,
        "battle_id" => nil
      }
      return event_message("game_connected", payload)
    end

    def event_message(event_name, payload)
      return {
        "schema_version" => TRACKER_SCHEMA_VERSION,
        "type" => "event",
        "event" => event_name,
        "run_id" => Ironmon.ensure_tracker_run_id,
        "battle_id" => nil,
        "sequence" => Ironmon.next_tracker_sequence,
        "sent_at" => Ironmon.tracker_timestamp,
        "payload" => payload
      }
    end

    def queue_message(message)
      @output_buffer << JSON.generate(message) << "\n"
    end

    def flush_output
      return if @output_buffer.empty?
      return if !IO.select(nil, [@socket], nil, 0)
      written = @socket.write(@output_buffer)
      @output_buffer = @output_buffer.byteslice(written, @output_buffer.bytesize) || ""
    rescue Errno::ECONNRESET, Errno::EPIPE, IOError
      disconnect
    end

    def read_input
      loop do
        return if !IO.select([@socket], nil, nil, 0)
        data = @socket.recv(16_384)
        if data.empty?
          disconnect
          return
        end
        @input_buffer << data
        if @input_buffer.bytesize > TRACKER_MAXIMUM_MESSAGE_BYTES
          raise "Ironmon tracker input exceeded the framing limit."
        end
        process_input_lines
      end
    rescue Errno::ECONNRESET, Errno::ECONNABORTED, IOError
      disconnect
    end

    def process_input_lines
      while (newline = @input_buffer.index("\n"))
        line = @input_buffer.slice!(0, newline + 1).strip
        next if line.empty?
        handle_message(normalize_json(JSON.parse(line)))
      end
    end

    def normalize_json(value)
      if value.is_a?(Hash)
        normalized = {}
        value.each do |key, child|
          normalized[key.to_s] = normalize_json(child)
        end
        return normalized
      end
      return value.map { |child| normalize_json(child) } if value.is_a?(Array)
      return value
    end

    def handle_message(message)
      validate_envelope(message)
      if message["type"] == "event" && message["event"] == "tracker_connected"
        payload = message["payload"] || {}
        @debug_requested = payload["debug_requested"] == true
      elsif message["type"] == "request"
        handle_request(message)
      end
    end

    def validate_envelope(message)
      if message["schema_version"] != TRACKER_SCHEMA_VERSION
        raise "Unsupported tracker schema version."
      end
      if !["event", "request", "response"].include?(message["type"])
        raise "Invalid tracker message type."
      end
      raise "Tracker message payload is missing." if !message.key?("payload")
    end

    def handle_request(message)
      request_id = message["request_id"]
      raise "Tracker request_id is missing." if !request_id.is_a?(String) || request_id.empty?
      if message["command"] == "current_state"
        queue_message(success_response(request_id, Ironmon.tracker_current_state))
      else
        queue_message(error_response(request_id, "unknown_command", "The game does not support this tracker command."))
      end
    end

    def success_response(request_id, payload)
      return {
        "schema_version" => TRACKER_SCHEMA_VERSION,
        "type" => "response",
        "request_id" => request_id,
        "run_id" => Ironmon.ensure_tracker_run_id,
        "battle_id" => nil,
        "sent_at" => Ironmon.tracker_timestamp,
        "success" => true,
        "payload" => payload
      }
    end

    def error_response(request_id, code, message)
      return {
        "schema_version" => TRACKER_SCHEMA_VERSION,
        "type" => "response",
        "request_id" => request_id,
        "run_id" => Ironmon.ensure_tracker_run_id,
        "battle_id" => nil,
        "sent_at" => Ironmon.tracker_timestamp,
        "success" => false,
        "error" => { "code" => code, "message" => message },
        "payload" => {}
      }
    end

    def disconnect(error = nil)
      was_connected = @state == :connected
      begin
        @connect_thread.kill if @connect_thread && @connect_thread.alive?
        @socket.close if @socket && !@socket.closed?
        @pending_socket.close if @pending_socket && !@pending_socket.closed?
      rescue Exception
      end
      @socket = nil
      @connect_thread = nil
      @pending_socket = nil
      @connect_error = nil
      @state = :disconnected
      @input_buffer = ""
      @output_buffer = ""
      @next_attempt_at = System.uptime + TRACKER_RECONNECT_SECONDS
      log_error(error) if error
      echoln "Ironmon tracker disconnected." if was_connected && !error
    end

    def log_error(error)
      message = "#{error.class}: #{error.message}"
      now = System.uptime
      return if message == @last_error && now - @last_error_at < 30.0
      @last_error = message
      @last_error_at = now
      echoln "Ironmon tracker connection error: #{message}"
    end
  end

  def self.tracker_connection
    @tracker_connection ||= TrackerConnection.new
    return @tracker_connection
  end

  def self.update_tracker_connection
    tracker_connection.update
  rescue Exception => e
    echoln "Ironmon tracker update failed safely: #{e.message}"
  end

  def self.start_tracker_run
    return if !$PokemonGlobal
    $PokemonGlobal.ironmon_run_id = new_tracker_run_id
    $PokemonGlobal.ironmon_tracker_sequence = 0
    tracker_connection.send_run_started
  end

  def self.ensure_tracker_run_id
    return nil if !active? || !$PokemonGlobal
    if !$PokemonGlobal.ironmon_run_id || $PokemonGlobal.ironmon_run_id.empty?
      $PokemonGlobal.ironmon_run_id = new_tracker_run_id
    end
    return $PokemonGlobal.ironmon_run_id
  end

  def self.new_tracker_run_id
    timestamp = Time.now.utc.strftime("%Y%m%dT%H%M%S")
    uptime = (System.uptime * 1000).to_i
    return "run-#{timestamp}-#{Process.pid}-#{uptime}"
  end

  def self.next_tracker_sequence
    return 0 if !$PokemonGlobal
    current = $PokemonGlobal.ironmon_tracker_sequence || 0
    $PokemonGlobal.ironmon_tracker_sequence = current + 1
    return $PokemonGlobal.ironmon_tracker_sequence
  end

  def self.tracker_current_state
    return {
      "ironmon_active" => active?,
      "run_id" => ensure_tracker_run_id,
      "battle_id" => nil,
      "sequence" => $PokemonGlobal ? ($PokemonGlobal.ironmon_tracker_sequence || 0) : 0
    }
  end

  def self.tracker_game_version
    return Settings::GAME_VERSION_NUMBER if defined?(Settings::GAME_VERSION_NUMBER)
    return "unknown"
  end

  def self.tracker_timestamp
    return Time.now.utc.strftime("%Y-%m-%dT%H:%M:%S.%LZ")
  end
end

module Graphics
  class << self
    alias ironmon_tracker_original_update update

    def update
      ironmon_tracker_original_update
      Ironmon.update_tracker_connection
    end
  end
end
