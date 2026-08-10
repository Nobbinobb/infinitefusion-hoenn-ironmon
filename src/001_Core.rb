#===============================================================================
# Ironmon core
#===============================================================================

module Ironmon
  VERSION = "0.6.2"
  SUPPORTED_GAME_VERSIONS = ["6.8.0"].freeze
  RESET_KEY = Input::F7
  FULL_RANDOM_BST_RANGE = 999
  TRAINER_CUSTOM_SPRITES_SWITCH = 600

  @skip_next_mode_selection = false
  @reset_in_progress = false
  @static_refresh_pending = false
  @reset_save_slot = nil
  @reset_notice = nil
  @seed_to_avoid = nil

  def self.active?
    return false if !$PokemonGlobal
    is_active = $PokemonGlobal.ironmon_mode == true
    configuration if is_active
    return is_active
  end

  def self.mode_available?
    return true if Settings::KANTO
    return $Trainer && $Trainer.new_game_plus_unlocked
  end

  def self.compatible?
    return false if !defined?(Settings::GAME_VERSION_NUMBER)
    return SUPPORTED_GAME_VERSIONS.include?(Settings::GAME_VERSION_NUMBER)
  end

  def self.check_compatibility
    return true if compatible?
    game_version = if defined?(Settings::GAME_VERSION_NUMBER)
                     Settings::GAME_VERSION_NUMBER
                   else
                     "unknown"
                   end
    echoln "Ironmon #{VERSION} warning: game version #{game_version} is not supported. Supported version: #{SUPPORTED_GAME_VERSIONS.join(', ')}."
    return false
  end

  def self.consume_mode_selection_skip
    return false if !@skip_next_mode_selection
    @skip_next_mode_selection = false
    return true
  end
end

class PokemonGlobalMetadata
  attr_accessor :ironmon_mode
  attr_accessor :ironmon_seed
  attr_accessor :ironmon_checkpoint_id
  attr_accessor :ironmon_configuration
  attr_accessor :ironmon_custom_fusion_pool_version
  attr_accessor :ironmon_custom_fusion_pool_size
  attr_accessor :ironmon_custom_fusion_pool_fingerprint
  attr_accessor :ironmon_species_generator_version
  attr_accessor :ironmon_ability_generator_version
  attr_accessor :ironmon_ability_pool_size
  attr_accessor :ironmon_ability_pool_fingerprint
  attr_accessor :ironmon_base_stat_generator_version
  attr_accessor :ironmon_base_stat_source_fingerprint
  attr_accessor :ironmon_evolution_generator_version
  attr_accessor :ironmon_evolution_rules_version
  attr_accessor :ironmon_evolution_source_fingerprint
  attr_accessor :ironmon_evolution_taxonomy_fingerprint
  attr_accessor :ironmon_evolution_method_fingerprint
  attr_accessor :ironmon_evolution_target_fingerprint
  attr_accessor :ironmon_evolution_base_stat_generator_version
  attr_accessor :ironmon_evolution_base_stat_source_fingerprint
  attr_accessor :ironmon_evolution_fusion_generator_version
  attr_accessor :ironmon_evolution_fusion_rules_version
  attr_accessor :ironmon_evolution_fusion_target_pool_version
  attr_accessor :ironmon_evolution_fusion_target_pool_size
  attr_accessor :ironmon_evolution_fusion_target_pool_fingerprint
  attr_accessor :ironmon_move_access_generator_version
  attr_accessor :ironmon_move_pool_size
  attr_accessor :ironmon_move_pool_fingerprint
  attr_accessor :ironmon_move_contextual_restriction_fingerprint
  attr_accessor :ironmon_move_source_fingerprint
  attr_accessor :ironmon_egg_move_source_fingerprint
  attr_accessor :ironmon_tm_roster_size
  attr_accessor :ironmon_tm_roster_fingerprint
  attr_accessor :ironmon_tm_source_fingerprint
  attr_accessor :ironmon_tr_roster_size
  attr_accessor :ironmon_tr_roster_fingerprint
  attr_accessor :ironmon_tr_source_fingerprint
  attr_accessor :ironmon_tutor_catalog_size
  attr_accessor :ironmon_tutor_catalog_fingerprint
  attr_accessor :ironmon_tutor_source_fingerprint
  attr_accessor :ironmon_fusion_tutor_regular_catalog_size
  attr_accessor :ironmon_fusion_tutor_legendary_catalog_size
  attr_accessor :ironmon_fusion_tutor_catalog_fingerprint
  attr_accessor :ironmon_fusion_tutor_source_fingerprint
  attr_accessor :ironmon_wild_species_map
  attr_accessor :ironmon_trainer_species_map
  attr_accessor :ironmon_gym_leader_teams
  attr_accessor :ironmon_pivot_state
  attr_accessor :ironmon_run_id
  attr_accessor :ironmon_tracker_sequence
  attr_accessor :ironmon_run_result
  attr_accessor :ironmon_move_access_metrics
  attr_accessor :ironmon_evolution_metrics
end

class Pokemon
  attr_accessor :ironmon_fusion_origin
  attr_accessor :ironmon_transformation_right
  attr_accessor :ironmon_party_exclusion
end

Ironmon.check_compatibility
