#===============================================================================
# Ironmon tracker connection
#===============================================================================

require "socket"

module Ironmon
  TRACKER_HOST = "127.0.0.1"
  TRACKER_PORT = 38_521
  TRACKER_SCHEMA_VERSION = 1
  TRACKER_RECONNECT_SECONDS = 2.0
  TRACKER_UPTIME_UNITS_PER_SECOND = 1_000_000.0
  TRACKER_MAXIMUM_MESSAGE_BYTES = 1_048_576
  TRACKER_DIAGNOSTIC_CAPABILITIES = [
    "evolution.candidates",
    "evolution.generator_details",
    "evolution.results",
    "fusion.material_pairs",
    "fusion.preview_results",
    "pokemon.abilities",
    "pokemon.all_active",
    "pokemon.base_stats",
    "pokemon.current_enemies",
    "pokemon.current_player",
    "pokemon.move_access",
    "pokemon.overview",
    "run.configuration",
    "run.generator_manifests",
    "run.seed",
    "world.items",
    "world.trainer_parties",
    "world.wild_encounters"
  ]
  def self.tracker_uptime_seconds
    return System.uptime.to_f / TRACKER_UPTIME_UNITS_PER_SECOND
  end

  def self.tracker_json_generate(value)
    case value
    when Hash
      entries = value.map do |key, child|
        "#{tracker_json_string(key)}:#{tracker_json_generate(child)}"
      end
      return "{#{entries.join(",")}}"
    when Array
      return "[#{value.map { |child| tracker_json_generate(child) }.join(",")}]"
    when String, Symbol
      return tracker_json_string(value)
    when TrueClass, FalseClass
      return value.to_s
    when NilClass
      return "null"
    when Numeric
      return value.to_s
    end
    raise "Unsupported tracker JSON value #{value.class}."
  end

  def self.tracker_json_string(value)
    escaped = value.to_s.each_codepoint.map do |codepoint|
      case codepoint
      when 0x08 then "\\b"
      when 0x09 then "\\t"
      when 0x0A then "\\n"
      when 0x0C then "\\f"
      when 0x0D then "\\r"
      when 0x22 then '\\"'
      when 0x5C then "\\\\"
      else
        codepoint < 0x20 ? sprintf("\\u%04x", codepoint) : codepoint.chr(Encoding::UTF_8)
      end
    end.join
    return "\"#{escaped}\""
  end

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
      @diagnostic_capabilities = []
      @auto_select_starter = false
      @maximum_starter_base_stat_total = nil
      @favorite_species_ids = []
      @pending_area_discoveries = {}
      @area_discovery_sequence = 0
    end

    def update
      if @state == :disconnected
        begin_connect if Ironmon.tracker_uptime_seconds >= @next_attempt_at
      elsif @state == :connecting
        finish_connect
      elsif @state == :connected
        resend_area_discoveries
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

    def send_event(event_name, payload, run_id = nil, sequence = nil)
      return if @state != :connected
      queue_message(event_message(event_name, payload, run_id, sequence))
    end

    def connected?
      return @state == :connected
    end

    def send_area_discovery(area_id, category, entry_keys)
      return false if !connected?
      keys = entry_keys.map { |key| key.to_s }.reject { |key| key.empty? }.uniq
      return false if area_id.to_s.empty? || keys.empty?
      @area_discovery_sequence += 1
      package_id = "#{Ironmon.ensure_tracker_run_id}:area:#{@area_discovery_sequence}"
      payload = {
        "package_id" => package_id,
        "area_id" => area_id.to_s,
        "category" => category.to_s,
        "entry_keys" => keys
      }
      @pending_area_discoveries[package_id] = {
        "payload" => payload,
        "next_send_at" => 0.0
      }
      send_pending_area_discovery(package_id)
      return true
    end

    def reset_area_discoveries
      @pending_area_discoveries = {}
      @area_discovery_sequence = 0
    end

    def auto_select_starter?
      return @auto_select_starter == true
    end

    def favorite_species_ids
      return @favorite_species_ids || []
    end

    def maximum_starter_base_stat_total
      return @maximum_starter_base_stat_total
    end

    def diagnostic_capability?(capability)
      return true if debug_authorized?
      return @diagnostic_capabilities.include?(capability.to_s)
    end

    def diagnostic_capabilities?(*capabilities)
      return capabilities.all? { |capability| diagnostic_capability?(capability) }
    end

    def any_diagnostic_capability?(*capabilities)
      return capabilities.any? { |capability| diagnostic_capability?(capability) }
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
        "supported_diagnostic_capabilities" => TRACKER_DIAGNOSTIC_CAPABILITIES,
        "game_root" => File.expand_path("."),
        "run_id" => run_id,
        "battle_id" => Ironmon.tracker_battle_id
      }
      return event_message("game_connected", payload)
    end

    def event_message(event_name, payload, run_id = nil, sequence = nil)
      return {
        "schema_version" => TRACKER_SCHEMA_VERSION,
        "type" => "event",
        "event" => event_name,
        "run_id" => run_id || Ironmon.ensure_tracker_run_id,
        "battle_id" => Ironmon.tracker_battle_id,
        "sequence" => sequence || Ironmon.next_tracker_sequence,
        "sent_at" => Ironmon.tracker_timestamp,
        "payload" => payload
      }
    end

    def queue_message(message)
      serialized = Ironmon.tracker_json_generate(message)
      if serialized.bytesize > TRACKER_MAXIMUM_MESSAGE_BYTES
        if message["type"] == "response" && message["request_id"]
          oversized_bytes = serialized.bytesize
          message = error_response(
            message["request_id"], "response_too_large",
            "The tracker response required #{oversized_bytes} bytes and was not sent.",
            message["run_id"]
          )
          serialized = Ironmon.tracker_json_generate(message)
        else
          echoln "Ironmon tracker skipped oversized #{message["type"]} message."
          return false
        end
      end
      @output_buffer << serialized << "\n"
      return true
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
      @debug_requested = false
      @diagnostic_capabilities = []
      @next_attempt_at = Ironmon.tracker_uptime_seconds +
        TRACKER_RECONNECT_SECONDS
      log_error(error) if error
      echoln "Ironmon tracker disconnected." if was_connected && !error
    end

    def log_error(error)
      message = "#{error.class}: #{error.message}"
      now = Ironmon.tracker_uptime_seconds
      return if message == @last_error && now - @last_error_at < 30.0
      @last_error = message
      @last_error_at = now
      echoln "Ironmon tracker connection error: #{message}"
    end
  end
end
