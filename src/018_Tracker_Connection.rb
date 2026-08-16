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
  TRACKER_MAXIMUM_ERROR_MESSAGE_CHARACTERS = 2_000
  TRACKER_STATE_INTERVAL_SECONDS = 0.1
  TRACKER_DISCOVERY_RETRY_SECONDS = 1.0
  TRACKER_MAXIMUM_DIAGNOSTIC_CAPABILITIES = 128
  TRACKER_MAXIMUM_DIAGNOSTIC_CAPABILITY_CHARACTERS = 128
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
  TRACKER_FIXED_HEALING = {
    :POTION => 20,
    :BERRYJUICE => 20,
    :SWEETHEART => 20,
    :SUPERPOTION => 50,
    :FRESHWATER => 50,
    :ENERGYPOWDER => 50,
    :SODAPOP => 60,
    :LEMONADE => 80,
    :MOOMOOMILK => 100,
    :ORANBERRY => 10,
    :HYPERPOTION => 200,
    :ENERGYROOT => 200
  }
  TRACKER_STATUS_ITEMS = [
    :ANTIDOTE, :AWAKENING, :ASPEARBERRY, :BIGMALASADA, :BLUEFLUTE,
    :BURNHEAL, :CASTELIACONE, :CHERIBERRY, :CHESTOBERRY, :FULLHEAL,
    :HEALPOWDER, :ICEHEAL, :LAVACOOKIE, :LUMIOSEGALETTE, :OLDGATEAU,
    :LUMBERRY, :PARALYZEHEAL, :PARLYZHEAL, :PECHABERRY, :PERSIMBERRY, :RAWSTBERRY,
    :REDFLUTE, :SHALOURSABLE, :YELLOWFLUTE
  ]
  TRACKER_PP_RESTORE_ITEMS = [
    :ELIXIR, :ETHER, :LEPPABERRY, :MAXELIXIR, :MAXETHER
  ]
  TRACKER_COMBAT_STAT_ITEM_PREFIXES = [
    "DIREHIT", "GUARDSPEC", "XACCURACY", "XATTACK", "XDEFEND",
    "XDEFENSE", "XSPATK", "XSPECIAL", "XSPDEF", "XSPEED"
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

    def send_event(event_name, payload)
      return if @state != :connected
      queue_message(event_message(event_name, payload))
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

    def event_message(event_name, payload)
      return {
        "schema_version" => TRACKER_SCHEMA_VERSION,
        "type" => "event",
        "event" => event_name,
        "run_id" => Ironmon.ensure_tracker_run_id,
        "battle_id" => Ironmon.tracker_battle_id,
        "sequence" => Ironmon.next_tracker_sequence,
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

    def handle_message(message)
      validate_envelope(message)
      if message["type"] == "event" && message["event"] == "tracker_connected"
        payload = message["payload"] || {}
        @debug_requested = payload["debug_requested"] == true
        @diagnostic_capabilities = normalize_diagnostic_capabilities(
          payload["diagnostic_capabilities"]
        )
        @auto_select_starter = payload["auto_select_starter"] == true
        @maximum_starter_base_stat_total = Ironmon.valid_starter_bst_ceiling(
          payload["maximum_starter_base_stat_total"]
        )
        @favorite_species_ids = normalize_favorite_species_ids(
          payload["favorite_species_ids"]
        )
      elsif message["type"] == "event" &&
            message["event"] == "diagnostic_access_changed"
        payload = message["payload"] || {}
        @diagnostic_capabilities = normalize_diagnostic_capabilities(
          payload["diagnostic_capabilities"]
        )
      elsif message["type"] == "event" &&
            message["event"] == "area_discovery_acknowledged"
        acknowledge_area_discovery(message)
      elsif message["type"] == "request"
        handle_request(message)
      end
    end

    def acknowledge_area_discovery(message)
      payload = message["payload"] || {}
      package_id = payload["package_id"].to_s
      return if package_id.empty?
      pending = @pending_area_discoveries[package_id]
      return if !pending
      return if message["run_id"].to_s != Ironmon.ensure_tracker_run_id.to_s
      @pending_area_discoveries.delete(package_id)
    end

    def resend_area_discoveries
      now = Ironmon.tracker_uptime_seconds
      @pending_area_discoveries.keys.each do |package_id|
        pending = @pending_area_discoveries[package_id]
        next if !pending || now < pending["next_send_at"]
        send_pending_area_discovery(package_id)
      end
    end

    def send_pending_area_discovery(package_id)
      pending = @pending_area_discoveries[package_id]
      return false if !pending || !connected?
      queue_message(event_message("area_discovery", pending["payload"]))
      pending["next_send_at"] = Ironmon.tracker_uptime_seconds +
        TRACKER_DISCOVERY_RETRY_SECONDS
      return true
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
      elsif message["command"] == "update_settings"
        payload = message["payload"] || {}
        @auto_select_starter = payload["auto_select_starter"] == true
        @maximum_starter_base_stat_total = Ironmon.valid_starter_bst_ceiling(
          payload["maximum_starter_base_stat_total"]
        )
        @favorite_species_ids = normalize_favorite_species_ids(
          payload["favorite_species_ids"]
        )
        queue_message(success_response(
          request_id, {
            "auto_select_starter" => @auto_select_starter,
            "maximum_starter_base_stat_total" =>
              @maximum_starter_base_stat_total,
            "favorite_species_ids" => @favorite_species_ids
          },
          message["run_id"]
        ))
      elsif message["command"] == "reset_run"
        accepted = Ironmon.request_tracker_reset
        queue_message(success_response(
          request_id, { "accepted" => accepted }, message["run_id"]
        ))
      elsif message["command"] == "use_battle_item"
        payload = Ironmon.request_tracker_battle_item(
          message["payload"], message["battle_id"]
        )
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "favorite_pokemon_search"
        payload = message["payload"] || {}
        payload["normal_only"] = true
        payload = Ironmon.tracker_pokemon_search_for_recipe(payload, nil)
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "area_lookup_summary"
        payload = Ironmon.tracker_area_lookup_summary(
          message["payload"], message["run_id"]
        )
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "area_lookup_detail"
        category = (message["payload"] || {})["category"].to_s
        payload = Ironmon.tracker_area_lookup_detail(
          message["payload"], message["run_id"],
          diagnostic_capability?(area_diagnostic_capability(category))
        )
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "pokemon_search"
        payload = Ironmon.tracker_pokemon_search(message["payload"], message["run_id"])
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "pokemon_lookup"
        payload = Ironmon.tracker_pokemon_lookup(message["payload"], message["run_id"])
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "evolution_candidate_search"
        payload = Ironmon.tracker_evolution_candidate_search(
          message["payload"], message["run_id"]
        )
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "fusion_material_search"
        payload = Ironmon.tracker_fusion_material_search(
          message["payload"], message["run_id"]
        )
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "wild_occurrence_search"
        payload = Ironmon.tracker_wild_occurrence_search(
          message["payload"], message["run_id"]
        )
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "trainer_occurrence_search"
        payload = Ironmon.tracker_trainer_occurrence_search(
          message["payload"], message["run_id"]
        )
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "fusion_preview"
        payload = Ironmon.tracker_fusion_preview(message["payload"], message["run_id"])
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "debug_inspect_pokemon"
        inspection_payload = message["payload"] || {}
        require_diagnostic_capabilities([
          inspection_diagnostic_capability(inspection_payload)
        ])
        require_any_diagnostic_capability(
          pokemon_information_capabilities(inspection_payload["section"])
        )
        payload = Ironmon.tracker_debug_inspect_pokemon(message["payload"])
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "debug_run_diagnostics"
        require_any_diagnostic_capability([
          "run.configuration", "run.seed", "run.generator_manifests",
          "evolution.generator_details"
        ])
        payload = Ironmon.tracker_debug_run_diagnostics
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "debug_pokemon_search"
        require_diagnostic_capabilities(["pokemon.all_active"])
        payload = Ironmon.tracker_debug_pokemon_search(message["payload"])
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "debug_pokemon_lookup"
        section = (message["payload"] || {})["section"].to_s
        require_diagnostic_capabilities([
          "pokemon.all_active", pokemon_information_capability(section)
        ])
        payload = Ironmon.tracker_debug_pokemon_lookup(message["payload"])
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "debug_evolution_candidate_search"
        require_diagnostic_capabilities([
          pokemon_source_diagnostic_capability(message["payload"] || {}),
          "evolution.candidates"
        ])
        payload = Ironmon.tracker_debug_evolution_candidate_search(
          message["payload"]
        )
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "debug_fusion_material_search"
        require_diagnostic_capabilities([
          pokemon_source_diagnostic_capability(message["payload"] || {}),
          "fusion.material_pairs"
        ])
        payload = Ironmon.tracker_debug_fusion_material_search(
          message["payload"]
        )
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "debug_wild_occurrence_search"
        require_diagnostic_capabilities([
          pokemon_source_diagnostic_capability(message["payload"] || {}),
          "world.wild_encounters"
        ])
        payload = Ironmon.tracker_debug_wild_occurrence_search(message["payload"])
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "debug_trainer_occurrence_search"
        require_diagnostic_capabilities([
          pokemon_source_diagnostic_capability(message["payload"] || {}),
          "world.trainer_parties"
        ])
        payload = Ironmon.tracker_debug_trainer_occurrence_search(message["payload"])
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "debug_fusion_preview"
        require_diagnostic_capabilities(["fusion.preview_results"])
        payload = Ironmon.tracker_debug_fusion_preview(message["payload"])
        queue_message(success_response(request_id, payload, message["run_id"]))
      else
        queue_message(error_response(request_id, "unknown_command", "The game does not support this tracker command."))
      end
    rescue Ironmon::TrackerLookupError, Ironmon::TrackerDebugError => e
      queue_message(error_response(request_id, e.code, e.message, message["run_id"]))
    rescue Exception => e
      queue_message(error_response(request_id, "lookup_failed", e.message, message["run_id"]))
    end

    def normalize_favorite_species_ids(values)
      return [] if !values.is_a?(Array)
      identifiers = values.map { |value| value.to_s.split(":", 2)[0].upcase }
      return identifiers.reject { |value| value.empty? }.
        map { |value| "#{value}:0" }.uniq
    end

    def normalize_diagnostic_capabilities(values)
      return [] if !values.is_a?(Array)
      return [] if values.length > TRACKER_MAXIMUM_DIAGNOSTIC_CAPABILITIES
      normalized = []
      values.each do |value|
        return [] if !value.is_a?(String) || value.empty?
        return [] if value.length > TRACKER_MAXIMUM_DIAGNOSTIC_CAPABILITY_CHARACTERS
        return [] if !TRACKER_DIAGNOSTIC_CAPABILITIES.include?(value)
        return [] if normalized.include?(value)
        normalized << value
      end
      return normalized
    end

    def require_diagnostic_capabilities(capabilities)
      return true if diagnostic_capabilities?(*capabilities)
      raise Ironmon::TrackerDebugError.new(
        "debug_forbidden",
        "The tracker has not granted every required diagnostic capability."
      )
    end

    def require_any_diagnostic_capability(capabilities)
      return true if any_diagnostic_capability?(*capabilities)
      raise Ironmon::TrackerDebugError.new(
        "debug_forbidden",
        "The tracker has not granted access to any requested diagnostic section."
      )
    end

    def inspection_diagnostic_capability(payload)
      target = payload["target"].to_s
      return "pokemon.current_player" if target == "player"
      return "pokemon.current_enemies" if target == "enemy"
      raise Ironmon::TrackerDebugError.new(
        "invalid_target",
        "Only the represented current player or current enemy may be inspected."
      )
    end

    def pokemon_source_diagnostic_capability(payload)
      return "pokemon.all_active" if payload["target"].to_s.empty?
      return inspection_diagnostic_capability(payload)
    end

    def pokemon_information_capabilities(section)
      if section.to_s == "overview" || section.to_s.empty?
        return [
          "pokemon.overview", "world.wild_encounters",
          "world.trainer_parties", "fusion.material_pairs",
          "fusion.preview_results"
        ]
      end
      if section.to_s == "evolutions"
        return ["evolution.results", "evolution.candidates"]
      end
      return [pokemon_information_capability(section)]
    end

    def pokemon_information_capability(section)
      return "pokemon.abilities" if section.to_s == "abilities"
      return "pokemon.base_stats" if section.to_s == "stats"
      return "pokemon.move_access" if section.to_s == "moves"
      return "evolution.results" if section.to_s == "evolutions"
      return "pokemon.overview"
    end

    def area_diagnostic_capability(category)
      return "world.trainer_parties" if category == "trainer"
      return "world.wild_encounters" if category == "encounter"
      return "world.items"
    end

    def debug_authorized?
      return $DEBUG == true && @debug_requested == true
    end

    def success_response(request_id, payload, run_id = nil)
      return {
        "schema_version" => TRACKER_SCHEMA_VERSION,
        "type" => "response",
        "request_id" => request_id,
        "run_id" => run_id || Ironmon.ensure_tracker_run_id,
        "battle_id" => Ironmon.tracker_battle_id,
        "sent_at" => Ironmon.tracker_timestamp,
        "success" => true,
        "payload" => payload
      }
    end

    def error_response(request_id, code, message, run_id = nil)
      error_message = message.to_s
      suffix = "... [truncated]"
      if error_message.length > TRACKER_MAXIMUM_ERROR_MESSAGE_CHARACTERS
        retained = TRACKER_MAXIMUM_ERROR_MESSAGE_CHARACTERS - suffix.length
        error_message = error_message[0, retained] + suffix
      end
      return {
        "schema_version" => TRACKER_SCHEMA_VERSION,
        "type" => "response",
        "request_id" => request_id,
        "run_id" => run_id || Ironmon.ensure_tracker_run_id,
        "battle_id" => Ironmon.tracker_battle_id,
        "sent_at" => Ironmon.tracker_timestamp,
        "success" => false,
        "error" => { "code" => code, "message" => error_message },
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
    attempt = current_run_attempt if respond_to?(:current_run_attempt)
    $PokemonGlobal.ironmon_run_id = if attempt && !attempt["run_id"].empty?
                                      attempt["run_id"]
                                    else
                                      new_tracker_run_id
                                    end
    $PokemonGlobal.ironmon_tracker_sequence = 0
    $PokemonGlobal.ironmon_run_result = nil
    tracker_connection.reset_area_discoveries
    reset_move_access_metrics if respond_to?(:reset_move_access_metrics)
    reset_evolution_metrics if respond_to?(:reset_evolution_metrics)
    @tracker_battle = nil
    @tracker_battle_id = nil
    @tracker_player_battler = nil
    @tracker_player_pokemon = nil
    @tracker_player_json = nil
    @tracker_move_menu_pokemon_id = nil
    @tracker_enemy_battlers = {}
    @tracker_enemy_json = {}
    @tracker_enemy_move_signatures = {}
    @tracker_enemy_abilities = {}
    @tracker_next_enemy_state_at = 0.0
    @tracker_pending_battle_item = nil
    @tracker_battle_command = nil
    @tracker_starter_selection = nil
    tracker_connection.send_run_started
  end

  def self.refresh_tracker_run_after_load
    return if checkpoint_reset_loading?
    tracker_connection.send_run_started
  end

  def self.start_tracker_battle(battle)
    return if !active?
    @tracker_battle = battle
    uptime = (tracker_uptime_seconds * 1000).to_i
    @tracker_battle_id = "battle-#{ensure_tracker_run_id}-#{uptime}"
    @tracker_player_battler = nil
    @tracker_player_json = nil
    @tracker_move_menu_pokemon_id = nil
    @tracker_enemy_battlers = {}
    @tracker_enemy_json = {}
    @tracker_enemy_move_signatures = {}
    @tracker_enemy_abilities = {}
    @tracker_next_enemy_state_at = 0.0
    @tracker_next_state_at = 0.0
    @tracker_pending_battle_item = nil
    @tracker_battle_command = nil
    tracker_connection.send_event("battle_started", tracker_battle_snapshot)
  end

  def self.end_tracker_battle
    return if !@tracker_battle_id
    tracker_connection.send_event("battle_ended", tracker_battle_snapshot)
    @tracker_battle = nil
    @tracker_battle_id = nil
    @tracker_player_battler = nil
    @tracker_move_menu_pokemon_id = nil
    @tracker_enemy_battlers = {}
    @tracker_enemy_json = {}
    @tracker_enemy_move_signatures = {}
    @tracker_enemy_abilities = {}
    @tracker_pending_battle_item = nil
    @tracker_battle_command = nil
  end

  def self.tracker_player_sent_out(battler)
    return if !active? || !@tracker_battle_id || !battler
    register_statistics_battler_item(battler) if
      respond_to?(:register_statistics_battler_item)
    @tracker_player_battler = battler
    @tracker_player_pokemon = battler.pokemon
    record_move_access_encounter(battler.pokemon, battler.level, "player") if
      respond_to?(:record_move_access_encounter)
    @tracker_move_menu_pokemon_id = nil
    snapshot = tracker_player_snapshot
    @tracker_player_json = tracker_json_generate(snapshot)
    tracker_connection.send_event("player_sent_out", snapshot)
  end

  def self.tracker_player_move_menu_opened(battler)
    return if !active? || !@tracker_battle_id || !battler || !battler.pokemon
    pokemon_id = battler.pokemon.personalID.to_s
    return if @tracker_move_menu_pokemon_id == pokemon_id
    @tracker_move_menu_pokemon_id = pokemon_id
    tracker_connection.send_event("player_move_menu_opened", { "pokemon_id" => pokemon_id })
  end

  def self.update_tracker_player
    synchronize_tracker_player_from_party if !@tracker_battle_id
    return if !@tracker_player_pokemon
    return if @tracker_battle_id && !@tracker_player_battler
    now = tracker_uptime_seconds
    return if @tracker_next_state_at && now < @tracker_next_state_at
    @tracker_next_state_at = now + TRACKER_STATE_INTERVAL_SECONDS
    snapshot = tracker_player_snapshot
    snapshot_json = tracker_json_generate(snapshot)
    return if snapshot_json == @tracker_player_json
    @tracker_player_json = snapshot_json
    tracker_connection.send_event("player_state_changed", snapshot)
  rescue Exception => e
    echoln "Ironmon tracker player update failed safely: #{e.message}"
  end

  def self.synchronize_tracker_player_from_party
    return if !$Trainer || !$Trainer.party
    party_pokemon = nil
    if @tracker_player_pokemon
      pokemon_id = @tracker_player_pokemon.personalID
      party_pokemon = $Trainer.party.find do |pokemon|
        pokemon.personalID == pokemon_id
      end
    end

    party_pokemon ||= $Trainer.party.find do |pokemon|
      !pokemon.respond_to?(:egg?) || !pokemon.egg?
    end
    @tracker_player_pokemon = party_pokemon
  end

  def self.tracker_enemy_sent_out(battler)
    return if !active? || !@tracker_battle_id || !battler
    register_statistics_battler_item(battler) if
      respond_to?(:register_statistics_battler_item)
    record_trainer_species_encounter(battler) if
      respond_to?(:record_trainer_species_encounter)
    current = @tracker_enemy_battlers[battler.index]
    return if current && current.pokemon.equal?(battler.pokemon)
    @tracker_enemy_battlers[battler.index] = battler
    record_move_access_encounter(battler.pokemon, battler.level, "enemy") if
      respond_to?(:record_move_access_encounter)
    @tracker_enemy_move_signatures.delete(battler.index)
    @tracker_enemy_abilities.delete(battler.index)
    snapshot = tracker_enemy_snapshot(battler)
    @tracker_enemy_json[battler.index] = tracker_json_generate(snapshot)
    tracker_connection.send_event("enemy_sent_out", snapshot)
  end

  def self.update_tracker_enemies
    return if !@tracker_battle_id || !@tracker_enemy_battlers
    now = tracker_uptime_seconds
    return if @tracker_next_enemy_state_at && now < @tracker_next_enemy_state_at
    @tracker_next_enemy_state_at = now + TRACKER_STATE_INTERVAL_SECONDS
    @tracker_enemy_battlers.each do |position, battler|
      tracker_enemy_move_if_changed(battler)
      snapshot = tracker_enemy_snapshot(battler)
      snapshot_json = tracker_json_generate(snapshot)
      next if snapshot_json == @tracker_enemy_json[position]
      @tracker_enemy_json[position] = snapshot_json
      tracker_connection.send_event("enemy_state_changed", snapshot)
    end
  rescue Exception => e
    echoln "Ironmon tracker enemy update failed safely: #{e.message}"
  end

  def self.tracker_enemy_move_if_changed(battler)
    return if !battler || !battler.lastMoveUsed || battler.lastMoveFailed
    battle_move = battler.moves.find { |move| move.id == battler.lastMoveUsed }
    current_pp = battle_move ? battle_move.pp : nil
    signature = [tracker_enemy_id(battler.pokemon), battler.lastMoveUsed, current_pp]
    return if @tracker_enemy_move_signatures[battler.index] == signature
    @tracker_enemy_move_signatures[battler.index] = signature
    tracker_enemy_move_used(battler, battler.lastMoveUsed)
  end

  def self.tracker_enemy_move_used(battler, move_id)
    return if !active? || !@tracker_battle_id || !battler || !move_id
    battle_move = battler.moves.find { |move| move.id == move_id }
    pp_after_use = battle_move && battle_move.pp >= 0 ? battle_move.pp : nil
    move = tracker_observed_move(battler.pokemon, move_id, "enemy_use", pp_after_use)
    payload = {
      "enemy_id" => tracker_enemy_id(battler.pokemon),
      "species_id" => tracker_species_id(battler.pokemon),
      "enemy_level" => battler.level,
      "move" => move
    }
    tracker_connection.send_event("enemy_move_used", payload)
  rescue Exception => e
    echoln "Ironmon tracker enemy move failed safely: #{e.message}"
  end

  def self.tracker_enemy_ability_revealed(battler)
    return if !active? || !@tracker_battle_id || !battler || !battler.ability
    ability = tracker_ability_snapshot(battler.ability)
    return if @tracker_enemy_abilities[battler.index] == ability
    @tracker_enemy_abilities[battler.index] = ability
    payload = {
      "enemy_id" => tracker_enemy_id(battler.pokemon),
      "species_id" => tracker_species_id(battler.pokemon),
      "ability" => ability
    }
    tracker_connection.send_event("enemy_ability_revealed", payload)
  rescue Exception => e
    echoln "Ironmon tracker enemy ability failed safely: #{e.message}"
  end

  def self.ensure_tracker_run_id
    return nil if !active? || !$PokemonGlobal
    if !$PokemonGlobal.ironmon_run_id || $PokemonGlobal.ironmon_run_id.empty?
      $PokemonGlobal.ironmon_run_id = new_tracker_run_id
    end
    return $PokemonGlobal.ironmon_run_id
  end

  def self.new_tracker_run_id
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : nil
    return "run-seed-#{seed}" if seed
    timestamp = Time.now.utc.strftime("%Y%m%dT%H%M%S")
    uptime = (tracker_uptime_seconds * 1000).to_i
    return "run-#{timestamp}-#{Process.pid}-#{uptime}"
  end

  def self.next_tracker_sequence
    return 0 if !$PokemonGlobal
    current = $PokemonGlobal.ironmon_tracker_sequence || 0
    $PokemonGlobal.ironmon_tracker_sequence = current + 1
    return $PokemonGlobal.ironmon_tracker_sequence
  end

  def self.tracker_current_state
    payload = {
      "ironmon_active" => active?,
      "run_id" => ensure_tracker_run_id,
      "battle_id" => tracker_battle_id,
      "sequence" => $PokemonGlobal ? ($PokemonGlobal.ironmon_tracker_sequence || 0) : 0,
      "battle" => tracker_battle_snapshot,
      "player" => @tracker_player_pokemon ? tracker_player_snapshot : nil,
      "enemies" => tracker_enemy_snapshots,
      "starter_selection" => if respond_to?(:tracker_starter_selection_snapshot)
                               tracker_starter_selection_snapshot
                             else
                               nil
                             end,
      "attempt_statistics" => if respond_to?(:tracker_attempt_statistics)
                                tracker_attempt_statistics(current_run_attempt)
                              else
                                nil
                              end,
      "completed_run" => tracker_recoverable_completed_run_recipe
    }
    coverage = tracker_type_coverage_context
    payload["type_coverage"] = coverage if coverage
    return payload
  end

  def self.tracker_type_coverage_context
    return nil if !active?
    normal_pool = normal_species_pool
    fusion_pool = custom_fusion_pool_info
    return {
      "trainer_policy" => configuration.trainer_policy.to_s,
      "normal_pool_size" => normal_pool.length,
      "normal_pool_fingerprint" => species_pool_fingerprint(normal_pool),
      "fusion_pool_schema_version" => fusion_pool[:schema_version],
      "fusion_pool_size" => fusion_pool[:size],
      "fusion_pool_fingerprint" => fusion_pool[:fingerprint]
    }
  end

  def self.tracker_battle_id
    return @tracker_battle_id
  end

  def self.tracker_battle_snapshot
    return nil if !@tracker_battle_id
    return { "battle_id" => @tracker_battle_id }
  end

  def self.tracker_player_snapshot
    pokemon = @tracker_player_pokemon
    species = pokemon.species_data
    nature = pokemon.nature
    ability = pokemon.ability
    held_item = pokemon.item
    return {
      "pokemon_id" => pokemon.personalID.to_s,
      "species_id" => tracker_species_id(pokemon),
      "nickname" => pokemon.name,
      "species_name" => species.name,
      "gender" => tracker_gender(pokemon),
      "sprite_path" => tracker_sprite_path(pokemon),
      "level" => pokemon.level,
      "current_hp" => pokemon.hp,
      "maximum_hp" => pokemon.totalhp,
      "status" => pokemon.status.to_s,
      "confused" => tracker_player_confused?,
      "types" => pokemon.types.map { |type| type.to_s },
      "ability" => ability ? ability.name : "None",
      "ability_details" => ability ? tracker_ability_snapshot(ability) : nil,
      "held_item" => held_item ? held_item.name : nil,
      "attack" => pokemon.attack,
      "defense" => pokemon.defense,
      "special_attack" => pokemon.spatk,
      "special_defense" => pokemon.spdef,
      "speed" => pokemon.speed,
      "stat_stages" => tracker_battler_stat_stages(@tracker_player_battler),
      "base_stat_total" => pokemon.baseStats.values.inject(0) { |sum, value| sum + value },
      "nature" => nature ? nature.name : nil,
      "nature_adjustments" => tracker_nature_adjustments(pokemon),
      "moves" => pokemon.moves.map { |move| tracker_move_snapshot(move) },
      "level_up_moves" => tracker_player_level_up_moves(pokemon),
      "learnset_progress" => tracker_learnset_progress(pokemon),
      "evolutions" => tracker_evolutions(pokemon),
      "healing" => tracker_healing_snapshot(pokemon.totalhp)
    }
  end

  def self.tracker_gender(pokemon)
    return "male" if pokemon.male?
    return "female" if pokemon.female?
    return "genderless"
  end

  def self.tracker_enemy_snapshots
    return [] if !@tracker_enemy_battlers
    return @tracker_enemy_battlers.keys.sort.map { |position| tracker_enemy_snapshot(@tracker_enemy_battlers[position]) }
  end

  def self.tracker_enemy_snapshot(battler)
    pokemon = battler.pokemon
    species = pokemon.species_data
    snapshot = {
      "enemy_id" => tracker_enemy_id(pokemon),
      "position" => battler.index,
      "species_id" => tracker_species_id(pokemon),
      "species_name" => species.name,
      "sprite_path" => tracker_sprite_path(pokemon),
      "level" => battler.level,
      "types" => pokemon.types.map { |type| type.to_s },
      "base_stat_total" => pokemon.baseStats.values.inject(0) { |sum, value| sum + value },
      "stat_stages" => tracker_battler_stat_stages(battler),
      "last_move" => tracker_enemy_last_move(battler),
      "last_ability" => @tracker_enemy_abilities[battler.index]
    }
    if catch_assistance_battle?(@tracker_battle)
      snapshot["catch_chance_percent"] = poke_ball_catch_chance_percent(
        pokemon, battler, @tracker_battle.caughtOffGuard
      )
    end
    return snapshot
  end

  def self.tracker_enemy_last_move(battler)
    move_id = battler.lastMoveUsed
    move_id = battler.movesUsed.last if !move_id && battler.movesUsed && !battler.movesUsed.empty?
    return nil if !move_id
    battle_move = battler.moves.find { |move| move.id == move_id }
    pp_after_use = battle_move ? battle_move.pp : nil
    return tracker_observed_move(battler.pokemon, move_id, "enemy_use", pp_after_use)
  end

  def self.tracker_battler_stat_stages(battler)
    stages = battler && battler.respond_to?(:stages) ? battler.stages : {}
    return {
      "attack" => stages[:ATTACK].to_i,
      "defense" => stages[:DEFENSE].to_i,
      "special_attack" => stages[:SPECIAL_ATTACK].to_i,
      "special_defense" => stages[:SPECIAL_DEFENSE].to_i,
      "speed" => stages[:SPEED].to_i
    }
  end

  def self.tracker_player_level_up_moves(pokemon)
    return pokemon.moves.map do |move|
      tracker_observed_move(pokemon, move.id, "player_initial", nil)
    end.select { |move| move["source"] == "level_up" }
  end

  def self.tracker_observed_move(pokemon, move_id, origin, pp_after_use)
    move_data = GameData::Move.get(move_id)
    learned_level = 0
    learn_order = 0
    found = false
    pokemon.getMoveList.each_with_index do |entry, index|
      listed_move = GameData::Move.try_get(entry[1])
      next if !listed_move || listed_move.id != move_data.id || entry[0] > pokemon.level
      learned_level = entry[0]
      learn_order = index
      found = true
    end
    source = found ? "level_up" : "unknown"
    return {
      "id" => move_data.id.to_s,
      "name" => move_data.name,
      "learned_level" => learned_level,
      "learn_order" => learn_order,
      "source" => source,
      "origin" => origin,
      "type" => move_data.type.to_s,
      "category" => tracker_move_category(move_data),
      "description" => move_data.description,
      "power" => move_data.base_damage || 0,
      "accuracy" => move_data.accuracy || 0,
      "total_pp" => move_data.total_pp,
      "pp_after_use" => pp_after_use
    }
  end

  def self.tracker_species_id(pokemon)
    return "#{pokemon.species_data.id}:#{pokemon.form}"
  end

  def self.tracker_enemy_id(pokemon)
    return "enemy-#{pokemon.personalID}"
  end

  def self.tracker_player_confused?
    return false if !@tracker_battle_id || !@tracker_player_battler
    confusion = @tracker_player_battler.effects[PBEffects::Confusion]
    return !!(confusion && confusion > 0)
  end

  def self.tracker_move_snapshot(move)
    return {
      "id" => move.id.to_s,
      "name" => move.name,
      "type" => move.type.to_s,
      "category" => tracker_move_category(GameData::Move.get(move.id)),
      "description" => GameData::Move.get(move.id).description,
      "current_pp" => move.pp,
      "total_pp" => move.total_pp,
      "power" => move.base_damage || 0,
      "accuracy" => move.accuracy || 0
    }
  end

  def self.tracker_move_category(move)
    return "status" if move.base_damage == 0
    return "physical" if move.physical?
    return "special" if move.special?
    return "unknown"
  end

  def self.tracker_ability_snapshot(ability)
    return {
      "id" => ability.id.to_s,
      "name" => ability.name,
      "description" => ability.description
    }
  end

  def self.tracker_nature_adjustments(pokemon)
    adjustments = {
      "attack" => "neutral",
      "defense" => "neutral",
      "special_attack" => "neutral",
      "special_defense" => "neutral",
      "speed" => "neutral"
    }
    pokemon.nature_for_stats.stat_changes.each do |change|
      key = change[0].to_s.downcase
      adjustments[key] = change[1] > 0 ? "increased" : "decreased"
    end
    return adjustments
  end

  def self.tracker_learnset_progress(pokemon)
    learnset = pokemon.getMoveList
    move_ids = learnset.map { |entry| GameData::Move.get(entry[1]).id }.uniq
    learned_ids = (pokemon.learned_moves || []).map { |move| GameData::Move.get(move).id }.uniq
    next_entry = learnset.select { |entry| entry[0] > pokemon.level }.min_by { |entry| entry[0] }
    return {
      "learned_moves" => (move_ids & learned_ids).length,
      "maximum_moves" => move_ids.length,
      "next_move_level" => next_entry ? next_entry[0] : nil
    }
  end

  def self.tracker_evolutions(pokemon)
    evolutions = pokemon.species_data.get_evolutions(true).map do |evolution|
      tracker_evolution_snapshot(evolution[1], evolution[2])
    end
    return evolutions.sort_by { |evolution| evolution["requirement"] }
  end

  def self.tracker_evolution_snapshot(method, parameter)
    evolution = GameData::Evolution.get(method)
    method_name = method.to_s.gsub(/([a-z])([A-Z])/, '\\1 \\2')
    result = {
      "kind" => "other",
      "requirement" => method_name
    }
    if evolution.parameter == :Item
      item = GameData::Item.get(parameter)
      result["kind"] = "item"
      result["item_id"] = item.id.to_s
      result["item_name"] = item.name
      result["requirement"] = item.name
    elsif evolution.parameter == Integer && evolution.minimum_level == 0
      condition = method_name.sub(/^Level\s*/, "")
      result["kind"] = "level"
      result["level"] = parameter
      result["requirement"] = condition.empty? ? "Level #{parameter}" : "Level #{parameter} (#{condition})"
    end
    return result
  end

  def self.tracker_healing_snapshot(maximum_hp)
    item_count = 0
    potential_hp = 0
    if $PokemonBag
      $PokemonBag.pockets.each do |pocket|
        next if !pocket
        pocket.each do |entry|
          item = entry[0]
          quantity = entry[1]
          healing = tracker_item_healing(item, maximum_hp)
          next if healing <= 0
          item_count += quantity
          potential_hp += healing * quantity
        end
      end
    end
    percentage = maximum_hp > 0 ? (potential_hp * 100.0 / maximum_hp).round(1) : 0.0
    return {
      "item_count" => item_count,
      "potential_hp" => potential_hp,
      "percentage" => percentage,
      "items" => tracker_battle_items(maximum_hp)
    }
  end

  def self.tracker_battle_items(maximum_hp)
    return [] if !$PokemonBag
    items = []
    $PokemonBag.pockets.each do |pocket|
      next if !pocket
      pocket.each do |entry|
        item = GameData::Item.try_get(entry[0])
        next if !item || item.battle_use <= 0 || entry[1].to_i <= 0
        items.push({
          "id" => item.id.to_s,
          "name" => item.name,
          "description" => item.description,
          "quantity" => entry[1],
          "category" => tracker_battle_item_category(item, maximum_hp),
          "requires_move" => [2, 7].include?(item.battle_use)
        })
      end
    end
    return items.sort_by { |item| [item["category"], item["name"], item["id"]] }
  end

  def self.tracker_battle_item_category(item, maximum_hp)
    return "healing" if tracker_item_healing(item.id, maximum_hp) > 0
    return "pp_restore" if TRACKER_PP_RESTORE_ITEMS.include?(item.id)
    return "status" if item.id == :RAGECANDYBAR &&
      Settings::RAGE_CANDY_BAR_CURES_STATUS_PROBLEMS
    return "status" if TRACKER_STATUS_ITEMS.include?(item.id)
    item_id = item.id.to_s
    return "combat_stat" if TRACKER_COMBAT_STAT_ITEM_PREFIXES.any? do |prefix|
      item_id.start_with?(prefix)
    end
    return "other"
  end

  def self.begin_tracker_battle_command(
    battle, battler_index, first_action, interrupt_result,
    interruptible = true
  )
    return if battle != @tracker_battle
    @tracker_battle_command = {
      "battler_index" => battler_index,
      "first_action" => first_action,
      "interrupt_result" => interrupt_result,
      "interrupt_depth" => interruptible ? 1 : 0
    }
  end

  def self.end_tracker_battle_command
    @tracker_battle_command = nil
  end

  def self.with_tracker_battle_item_interrupt
    return yield if !@tracker_battle_command
    @tracker_battle_command["interrupt_depth"] += 1
    return yield
  ensure
    if @tracker_battle_command
      @tracker_battle_command["interrupt_depth"] -= 1
    end
  end

  def self.request_tracker_battle_item(payload, battle_id)
    return tracker_battle_item_result(false, "No active battle is available.") if
      !@tracker_battle_id || battle_id.to_s != @tracker_battle_id.to_s
    return tracker_battle_item_result(false, "Choose an item while a battle action menu is open.") if
      !@tracker_battle_command ||
      @tracker_battle_command["interrupt_depth"] <= 0
    return tracker_battle_item_result(false, "Another tracker item is already selected.") if
      @tracker_pending_battle_item
    item_id = (payload || {})["item_id"].to_s
    item = GameData::Item.try_get(item_id.to_sym)
    return tracker_battle_item_result(false, "That item is not available.") if
      !item || item.battle_use <= 0 || !$PokemonBag || $PokemonBag.pbQuantity(item.id) <= 0
    move_index = (payload || {})["move_index"]
    if [2, 7].include?(item.battle_use)
      moves = @tracker_player_pokemon ? @tracker_player_pokemon.moves : []
      return tracker_battle_item_result(false, "Choose a move for that PP item.") if
        !move_index.is_a?(Integer) || move_index < 0 || move_index >= moves.length
    else
      move_index = -1
    end
    target_position = (payload || {})["target_position"]
    @tracker_pending_battle_item = {
      "item" => item.id,
      "move_index" => move_index,
      "target_position" => target_position
    }
    return tracker_battle_item_result(true, "#{item.name} was selected for this turn.")
  end

  def self.tracker_battle_item_result(accepted, message)
    return { "accepted" => accepted, "message" => message }
  end

  def self.tracker_battle_item_interrupt?
    return !!(
      @tracker_battle_command && @tracker_pending_battle_item &&
      @tracker_battle_command["interrupt_depth"] > 0
    )
  end

  def self.tracker_battle_item_menu_active?
    return !!@tracker_battle_command
  end

  def self.tracker_battle_item_pending?
    return !!@tracker_pending_battle_item
  end

  def self.tracker_battle_item_interrupt_result
    return nil if !tracker_battle_item_interrupt?
    return @tracker_battle_command["interrupt_result"]
  end

  def self.consume_tracker_battle_item
    item = @tracker_pending_battle_item
    @tracker_pending_battle_item = nil
    return item
  end

  def self.tracker_item_healing(item, maximum_hp)
    return maximum_hp if [:MAXPOTION, :FULLRESTORE].include?(item)
    return maximum_hp / 4 if item == :SITRUSBERRY
    return 20 if item == :RAGECANDYBAR && !Settings::RAGE_CANDY_BAR_CURES_STATUS_PROBLEMS
    return TRACKER_FIXED_HEALING[item] || 0
  end

  def self.tracker_sprite_path(pokemon, preferred_sprite = nil)
    pif_sprite = preferred_sprite || pokemon.pif_sprite
    if !pif_sprite
      loader = BattleSpriteLoader.new
      pif_sprite = loader.get_pif_sprite_from_species(pokemon.species)
    end
    path = BattleSpriteLoader.new.check_for_local_sprite(pif_sprite)
    return path ? path.tr("\\", "/") : nil
  rescue Exception
    return nil
  end

  def self.tracker_game_version
    return Settings::GAME_VERSION_NUMBER if defined?(Settings::GAME_VERSION_NUMBER)
    return "unknown"
  end

  def self.tracker_timestamp
    return Time.now.utc.strftime("%Y-%m-%dT%H:%M:%S.%LZ")
  end
end


module Game
  class << self
    alias ironmon_tracker_original_load load
    def load(save_data)
      result = ironmon_tracker_original_load(save_data)
      Ironmon.refresh_tracker_run_after_load
      return result
    end
  end
end

module Graphics
  class << self
    alias ironmon_tracker_original_update update

    def update
      ironmon_tracker_original_update
      Ironmon.update_tracker_connection
      Ironmon.update_tracker_player
      Ironmon.update_tracker_enemies
      interrupt_result = Ironmon.tracker_battle_item_interrupt_result
      throw :ironmon_tracker_battle_item, interrupt_result if !interrupt_result.nil?
    end
  end
end

module IronmonTrackerBattleSceneItemHooks
  def pbCommandMenu(idxBattler, firstAction)
    Ironmon.begin_tracker_battle_command(@battle, idxBattler, firstAction, 1)
    return catch(:ironmon_tracker_battle_item) { super }
  ensure
    Ironmon.end_tracker_battle_command
  end

  def pbFightMenu(idxBattler, megaEvoPossible = false)
    Ironmon.begin_tracker_battle_command(@battle, idxBattler, false, -1)
    return catch(:ironmon_tracker_battle_item) { super }
  ensure
    Ironmon.end_tracker_battle_command
  end

  def pbItemMenu(idxBattler, firstAction)
    Ironmon.begin_tracker_battle_command(
      @battle, idxBattler, firstAction, -1, false
    )
    return super
  ensure
    Ironmon.end_tracker_battle_command
  end

  def pbChooseTarget(idxBattler, target_data, visibleSprites = nil)
    return super if !Ironmon.tracker_battle_item_menu_active?
    return catch(:ironmon_tracker_battle_item) do
      Ironmon.with_tracker_battle_item_interrupt { super }
    end
  end
end

PokeBattle_Scene.prepend(IronmonTrackerBattleSceneItemHooks)

module IronmonTrackerBagSceneItemHooks
  def pbChooseItem
    result = catch(:ironmon_tracker_battle_item) do
      Ironmon.with_tracker_battle_item_interrupt { super }
    end
    return nil if Ironmon.tracker_battle_item_pending?
    return result
  end

  def pbShowCommands(helptext, commands, index = 0)
    return catch(:ironmon_tracker_battle_item) do
      Ironmon.with_tracker_battle_item_interrupt { super }
    end
  end
end

PokemonBag_Scene.prepend(IronmonTrackerBagSceneItemHooks)

module IronmonTrackerPartySceneItemHooks
  def pbChoosePokemon(switching = false, initialsel = -1, canswitch = 0)
    return catch(:ironmon_tracker_battle_item) do
      Ironmon.with_tracker_battle_item_interrupt { super }
    end
  end

  def pbShowCommands(helptext, commands, index = 0)
    return catch(:ironmon_tracker_battle_item) do
      Ironmon.with_tracker_battle_item_interrupt { super }
    end
  end
end

PokemonParty_Scene.prepend(IronmonTrackerPartySceneItemHooks)

module IronmonTrackerBattleHooks
  def pbStartBattle
    Ironmon.start_tracker_battle(self)
    return super
  ensure
    Ironmon.end_tracker_battle if Ironmon.tracker_battle_id
  end

  def pbSendOut(send_outs, start_battle = false)
    result = super
    send_outs.each do |entry|
      index = entry[0]
      next if !pbOwnedByPlayer?(index)
      Ironmon.tracker_player_sent_out(@battlers[index])
      break
    end
    send_outs.each do |entry|
      index = entry[0]
      next if index.even?
      Ironmon.tracker_enemy_sent_out(@battlers[index])
    end
    return result
  end

  def pbOnActiveAll
    result = super
    @battlers.each do |battler|
      next if !battler || battler.index.even?
      Ironmon.tracker_enemy_sent_out(battler)
    end
    return result
  end

  def pbFightMenu(idxBattler)
    battler = @battlers[idxBattler]
    Ironmon.tracker_player_move_menu_opened(battler) if battler && pbOwnedByPlayer?(idxBattler)
    return super
  end

  def pbPartyMenu(idxBattler)
    Ironmon.begin_tracker_battle_command(self, idxBattler, false, -1, false)
    return super
  ensure
    Ironmon.end_tracker_battle_command
  end

  def pbItemMenu(idxBattler, firstAction)
    selection = Ironmon.consume_tracker_battle_item
    return super if !selection
    return false if !@internalBattle || !pbOwnedByPlayer?(idxBattler)
    item = GameData::Item.try_get(selection["item"])
    return false if !item || !$PokemonBag || $PokemonBag.pbQuantity(item.id) <= 0
    battler = @battlers[idxBattler]
    return false if !battler || battler.fainted?
    use_type = item.battle_use
    target_index = battler.pokemonIndex
    target_battler = battler
    target_pokemon = battler.pokemon
    if [4, 9].include?(use_type)
      requested_position = selection["target_position"]
      target_battler = @battlers[requested_position] if requested_position.is_a?(Integer)
      if !target_battler || target_battler.fainted? || !target_battler.opposes?(idxBattler)
        target_battler = nil
        eachOtherSideBattler(idxBattler) { |candidate| target_battler ||= candidate }
      end
      return false if !target_battler
      target_index = target_battler.index
      target_pokemon = target_battler.pokemon
    elsif [5, 10].include?(use_type)
      target_index = idxBattler
    end
    if [1, 2, 3, 6, 7, 8].include?(use_type)
      return false if !pbCanUseItemOnPokemon?(item.id, target_pokemon, target_battler, @scene)
    end
    move_index = selection["move_index"] || -1
    return false if !ItemHandlers.triggerCanUseInBattle(
      item.id, target_pokemon, target_battler, move_index, firstAction,
      self, @scene
    )
    return pbRegisterItem(idxBattler, item.id, target_index, move_index)
  end

  def pbShowAbilitySplash(battler, delay = false, logTrigger = true, abilityName = nil)
    Ironmon.tracker_enemy_ability_revealed(battler) if battler && battler.index.odd?
    return super
  end
end

PokeBattle_Battle.prepend(IronmonTrackerBattleHooks)

module IronmonTrackerBattlerHooks
  def pbUseMove(choice, special_usage = false)
    result = super
    if !@lastMoveFailed && @lastMoveUsed
      if @index.odd? && !special_usage
        Ironmon.tracker_enemy_move_if_changed(self)
      end
      Ironmon.record_move_access_use(
        @pokemon, @lastMoveUsed, @index.even? ? "player" : "enemy",
        special_usage
      ) if Ironmon.respond_to?(:record_move_access_use)
    end
    return result
  end
end

PokeBattle_Battler.prepend(IronmonTrackerBattlerHooks)
