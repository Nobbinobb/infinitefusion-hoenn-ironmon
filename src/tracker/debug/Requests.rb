#===============================================================================
# Ironmon diagnostic tracker lookup requests
#===============================================================================

module Ironmon
  TRACKER_DEBUG_SEARCH_TRACE_PATH = if defined?(SaveData::SAVE_DIR) &&
                                       SaveData::SAVE_DIR
                                      File.join(
                                        SaveData::SAVE_DIR,
                                        "debug_pokemon_search_trace.log"
                                      )
                                    else
                                      "debug_pokemon_search_trace.log"
                                    end

  def self.tracker_debug_search_trace(stage)
    if stage == "received"
      @tracker_debug_search_trace_started_at = tracker_uptime_seconds
      mode = "wb"
    else
      return if !@tracker_debug_search_trace_started_at
      mode = "ab"
    end
    elapsed = tracker_uptime_seconds - @tracker_debug_search_trace_started_at
    File.open(TRACKER_DEBUG_SEARCH_TRACE_PATH, mode) do |file|
      file.write("#{(elapsed * 1_000).round}|#{stage}\n")
    end
  rescue Exception
  end

  def self.finish_tracker_debug_search_trace
    @tracker_debug_search_trace_started_at = nil
  end

  def self.tracker_debug_pokemon_search(payload)
    tracker_debug_search_trace("received")
    tracker_validate_debug_context(["pokemon.all_active"])
    tracker_debug_search_trace("authorized")
    recipe = tracker_debug_active_recipe
    tracker_debug_search_trace("recipe_ready")
    visibility = tracker_debug_lookup_visibility
    tracker_debug_search_trace("visibility_ready")
    result = tracker_pokemon_search_for_recipe(
      payload || {}, recipe, visibility
    )
    tracker_debug_search_trace("search_ready")
    return result
  rescue Exception
    tracker_debug_search_trace("failed")
    raise
  end

  def self.tracker_debug_pokemon_lookup(payload)
    section = (payload || {})["section"].to_s
    tracker_validate_debug_context([
      "pokemon.all_active", tracker_information_diagnostic_capability(section)
    ])
    return tracker_pokemon_lookup_for_recipe(
      payload || {}, tracker_debug_active_recipe,
      tracker_debug_lookup_visibility
    )
  end

  def self.tracker_debug_obtainability(payload)
    tracker_validate_debug_context(
      TRACKER_OBTAINABILITY_DIAGNOSTIC_CAPABILITIES
    )
    return tracker_obtainability_for_recipe(
      payload || {}, tracker_debug_active_recipe
    )
  end

  def self.tracker_debug_lookup_visibility
    return {
      :overview => tracker_connection.diagnostic_capability?(
        "pokemon.overview"
      ),
      :wild => tracker_connection.diagnostic_capability?(
        "world.wild_encounters"
      ),
      :trainer => tracker_connection.diagnostic_capability?(
        "world.trainer_parties"
      ),
      :materials => tracker_connection.diagnostic_capability?(
        "fusion.material_pairs"
      ),
      :evolution_generator => tracker_connection.diagnostic_capability?(
        "evolution.generator_details"
      ),
      :evolution_results => tracker_connection.diagnostic_capability?(
        "evolution.results"
      ),
      :obtainability => tracker_connection.diagnostic_capabilities?(
        *TRACKER_OBTAINABILITY_DIAGNOSTIC_CAPABILITIES
      )
    }
  end

  def self.tracker_debug_evolution_candidate_search(payload)
    tracker_validate_debug_context(["evolution.candidates"])
    payload = tracker_debug_secure_species_payload(payload)
    return tracker_evolution_candidate_search_for_recipe(
      payload || {}, tracker_debug_active_recipe,
      tracker_debug_lookup_visibility[:obtainability]
    )
  end

  def self.tracker_debug_evolution_predecessor_search(payload)
    tracker_validate_debug_context(["evolution.results"])
    payload = tracker_debug_secure_species_payload(payload)
    return tracker_evolution_predecessor_search_for_recipe(
      payload || {}, tracker_debug_active_recipe,
      tracker_debug_lookup_visibility[:obtainability]
    )
  end

  def self.tracker_debug_fusion_material_search(payload)
    tracker_validate_debug_context(["fusion.material_pairs"])
    payload = tracker_debug_secure_species_payload(payload)
    return tracker_fusion_material_search_for_recipe(
      payload || {}, tracker_debug_active_recipe,
      tracker_debug_lookup_visibility[:obtainability]
    )
  end

  def self.tracker_debug_wild_occurrence_search(payload)
    tracker_validate_debug_context(["world.wild_encounters"])
    payload = tracker_debug_secure_species_payload(payload)
    return tracker_occurrence_search_for_recipe(
      payload || {}, tracker_debug_active_recipe, :wild
    )
  end

  def self.tracker_debug_trainer_occurrence_search(payload)
    tracker_validate_debug_context(["world.trainer_parties"])
    payload = tracker_debug_secure_species_payload(payload)
    return tracker_occurrence_search_for_recipe(
      payload || {}, tracker_debug_active_recipe, :trainer
    )
  end

  def self.tracker_debug_fusion_preview(payload)
    tracker_validate_debug_context(["fusion.preview_results"])
    return tracker_fusion_preview_for_recipe(
      payload || {}, tracker_debug_active_recipe,
      tracker_debug_lookup_visibility[:obtainability]
    )
  end

  def self.tracker_debug_secure_species_payload(payload)
    result = (payload || {}).dup
    if result["target"].to_s.empty?
      tracker_validate_debug_context(["pokemon.all_active"])
      return result
    end

    availability = tracker_debug_availability_capability(result["target"])
    tracker_validate_debug_context([availability])
    requested_species_id = result["species_id"].to_s
    result["species_id"] = tracker_species_id(
      tracker_debug_resolve_pokemon(result)
    )
    if result["material_assignments"].is_a?(Array)
      result["material_assignment_species_id"] = requested_species_id
    end
    if !result["fusion_material_membership"].nil?
      result["fusion_membership_species_id"] = requested_species_id
    end
    return result
  end

  def self.tracker_debug_availability_capability(target)
    return "pokemon.current_player" if target.to_s == "player"
    return "pokemon.current_enemies" if target.to_s == "enemy"
    raise TrackerDebugError.new(
      "invalid_target",
      "Only the represented current player or current enemy may be inspected."
    )
  end
end
