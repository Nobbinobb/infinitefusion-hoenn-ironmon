#===============================================================================
# Ironmon tracker request routing and diagnostic authorization
#===============================================================================

module Ironmon
  TRACKER_MAXIMUM_ERROR_MESSAGE_CHARACTERS = 2_000
  TRACKER_DISCOVERY_RETRY_SECONDS = 1.0
  TRACKER_MAXIMUM_DIAGNOSTIC_CAPABILITIES = 128
  TRACKER_MAXIMUM_DIAGNOSTIC_CAPABILITY_CHARACTERS = 128

  def self.tracker_information_diagnostic_capability(section)
    return "pokemon.abilities" if section.to_s == "abilities"
    return "pokemon.base_stats" if section.to_s == "stats"
    return "pokemon.move_access" if section.to_s == "moves"
    return "evolution.results" if section.to_s == "evolutions"
    return "pokemon.overview"
  end

  def self.tracker_information_diagnostic_capabilities(section)
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
    return [tracker_information_diagnostic_capability(section)]
  end

  class TrackerConnection
    private

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
        Ironmon.reset_tracker_obtainability_worker_failures
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
      elsif message["command"] == "export_seeded_run"
        payload = Ironmon.tracker_seeded_run_export(message["run_id"])
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "import_seeded_run"
        payload = Ironmon.request_tracker_seed_import(
          message["payload"], message["run_id"]
        )
        queue_message(success_response(request_id, payload, message["run_id"]))
        if payload["status"] == "accepted"
          send_event("seeded_run_import_status", {
            "token_id" => payload["token_id"],
            "status" => "queued",
            "message" => "Seeded run is waiting for a safe map boundary."
          }, message["run_id"])
        end
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
      elsif message["command"] == "pokemon_obtainability"
        payload = Ironmon.tracker_obtainability(
          message["payload"], message["run_id"]
        )
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "evolution_candidate_search"
        payload = Ironmon.tracker_evolution_candidate_search(
          message["payload"], message["run_id"]
        )
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "evolution_predecessor_search"
        payload = Ironmon.tracker_evolution_predecessor_search(
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
          Ironmon.tracker_information_diagnostic_capabilities(
            inspection_payload["section"]
          )
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
        Ironmon.tracker_debug_search_trace("queue_started")
        queue_message(success_response(request_id, payload, message["run_id"]))
        Ironmon.tracker_debug_search_trace("queue_ready")
        Ironmon.finish_tracker_debug_search_trace
      elsif message["command"] == "debug_pokemon_lookup"
        section = (message["payload"] || {})["section"].to_s
        require_diagnostic_capabilities([
          "pokemon.all_active",
          Ironmon.tracker_information_diagnostic_capability(section)
        ])
        payload = Ironmon.tracker_debug_pokemon_lookup(message["payload"])
        queue_message(success_response(request_id, payload, message["run_id"]))
      elsif message["command"] == "debug_pokemon_obtainability"
        require_diagnostic_capabilities(
          Ironmon::TRACKER_OBTAINABILITY_DIAGNOSTIC_CAPABILITIES
        )
        payload = Ironmon.tracker_debug_obtainability(message["payload"])
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
      elsif message["command"] == "debug_evolution_predecessor_search"
        require_diagnostic_capabilities([
          pokemon_source_diagnostic_capability(message["payload"] || {}),
          "evolution.results"
        ])
        payload = Ironmon.tracker_debug_evolution_predecessor_search(
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
  end
end
