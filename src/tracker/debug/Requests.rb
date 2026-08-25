#===============================================================================
# Ironmon diagnostic tracker lookup requests
#===============================================================================

module Ironmon
  def self.tracker_debug_pokemon_search(payload)
    tracker_validate_debug_context(["pokemon.all_active"])
    return tracker_pokemon_search_for_recipe(
      payload || {}, tracker_debug_active_recipe,
      tracker_debug_lookup_visibility
    )
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
    result["species_id"] = tracker_species_id(
      tracker_debug_resolve_pokemon(result)
    )
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
