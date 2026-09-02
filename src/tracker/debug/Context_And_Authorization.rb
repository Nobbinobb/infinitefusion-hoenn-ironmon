#===============================================================================
# Ironmon diagnostic tracker context and authorization
#===============================================================================

module Ironmon
  def self.tracker_debug_active_recipe
    return {
      "active_run" => true,
      "overworld_encounters" => !!($PokemonSystem &&
        $PokemonSystem.overworld_encounters),
      "run_id" => ensure_tracker_run_id,
      "seed" => $PokemonGlobal.ironmon_seed || 0,
      "result" => "active_debug",
      "generation_profile_id" => pinned_generation_profile_id,
      "game_version" => tracker_game_version,
      "ironmon_version" => VERSION,
      "configuration" => configuration_snapshot,
      "data_mode" => tracker_data_mode,
      "species_generator_version" => $PokemonGlobal.ironmon_species_generator_version,
      "ability_generator_version" => $PokemonGlobal.ironmon_ability_generator_version,
      "base_stat_generator_version" => $PokemonGlobal.ironmon_base_stat_generator_version,
      "base_stat_source_fingerprint" => $PokemonGlobal.ironmon_base_stat_source_fingerprint,
      "evolution_generator_version" => $PokemonGlobal.ironmon_evolution_generator_version,
      "evolution_rules_version" => $PokemonGlobal.ironmon_evolution_rules_version,
      "evolution_source_fingerprint" => $PokemonGlobal.ironmon_evolution_source_fingerprint,
      "evolution_taxonomy_fingerprint" => $PokemonGlobal.ironmon_evolution_taxonomy_fingerprint,
      "evolution_method_fingerprint" => $PokemonGlobal.ironmon_evolution_method_fingerprint,
      "evolution_target_fingerprint" => $PokemonGlobal.ironmon_evolution_target_fingerprint,
      "evolution_base_stat_generator_version" => $PokemonGlobal.ironmon_evolution_base_stat_generator_version,
      "evolution_base_stat_source_fingerprint" => $PokemonGlobal.ironmon_evolution_base_stat_source_fingerprint,
      "fusion_evolution_generator_version" => $PokemonGlobal.ironmon_evolution_fusion_generator_version,
      "fusion_evolution_rules_version" => $PokemonGlobal.ironmon_evolution_fusion_rules_version,
      "fusion_evolution_target_pool_version" => $PokemonGlobal.ironmon_evolution_fusion_target_pool_version,
      "fusion_evolution_target_pool_size" => $PokemonGlobal.ironmon_evolution_fusion_target_pool_size,
      "fusion_evolution_target_pool_fingerprint" => $PokemonGlobal.ironmon_evolution_fusion_target_pool_fingerprint,
      "move_access_generator_version" => $PokemonGlobal.ironmon_move_access_generator_version,
      "move_pool_fingerprint" => $PokemonGlobal.ironmon_move_pool_fingerprint,
      "move_contextual_restriction_fingerprint" => $PokemonGlobal.ironmon_move_contextual_restriction_fingerprint,
      "move_source_fingerprint" => $PokemonGlobal.ironmon_move_source_fingerprint,
      "egg_move_source_fingerprint" => $PokemonGlobal.ironmon_egg_move_source_fingerprint,
      "tm_roster_fingerprint" => $PokemonGlobal.ironmon_tm_roster_fingerprint,
      "tm_source_fingerprint" => $PokemonGlobal.ironmon_tm_source_fingerprint,
      "tr_roster_fingerprint" => $PokemonGlobal.ironmon_tr_roster_fingerprint,
      "tr_source_fingerprint" => $PokemonGlobal.ironmon_tr_source_fingerprint,
      "tutor_catalog_fingerprint" => $PokemonGlobal.ironmon_tutor_catalog_fingerprint,
      "tutor_source_fingerprint" => $PokemonGlobal.ironmon_tutor_source_fingerprint,
      "fusion_tutor_catalog_fingerprint" => $PokemonGlobal.ironmon_fusion_tutor_catalog_fingerprint,
      "fusion_tutor_source_fingerprint" => $PokemonGlobal.ironmon_fusion_tutor_source_fingerprint,
      "player_fusion_generator_version" => PlayerFusionMapper::SCHEMA_VERSION,
      "item_generator" => item_generator_recipe,
      "item_mappings" => tracker_item_mapping_recipe(
        $PokemonGlobal.randomItemsHash
      ),
      "tm_mappings" => tracker_item_mapping_recipe(
        $PokemonGlobal.randomTMsHash
      ),
      "species_pool_fingerprint" => tracker_species_pool_fingerprint,
      "ability_pool_fingerprint" => $PokemonGlobal.ironmon_ability_pool_fingerprint,
      "fusion_pool_fingerprint" => $PokemonGlobal.ironmon_custom_fusion_pool_fingerprint
    }
  end

  def self.tracker_validate_debug_context(capabilities)
    if !tracker_connection.diagnostic_capabilities?(*capabilities)
      raise TrackerDebugError.new(
        "debug_forbidden",
        "The tracker has not granted every required diagnostic capability."
      )
    end
    if !active? || !$PokemonGlobal
      raise TrackerDebugError.new(
        "ironmon_inactive", "Ironmon must be active for debug inspection."
      )
    end
    return true
  end

  def self.tracker_validate_any_debug_context(capabilities)
    if !tracker_connection.any_diagnostic_capability?(*capabilities)
      raise TrackerDebugError.new(
        "debug_forbidden",
        "The tracker has not granted access to any requested diagnostic section."
      )
    end
    if !active? || !$PokemonGlobal
      raise TrackerDebugError.new(
        "ironmon_inactive", "Ironmon must be active for debug inspection."
      )
    end
    return true
  end

end
