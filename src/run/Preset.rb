#===============================================================================
# Ironmon deterministic randomization preset
#===============================================================================

module Ironmon
  PRESET_TRANSACTION_ERROR_VARIABLES = [
    :@preset_generation_error_message,
    :@custom_fusion_pool_error_message,
    :@ability_randomization_error_message,
    :@base_stat_randomization_error_message,
    :@evolution_randomization_error_message,
    :@move_access_randomization_error_message,
    :@item_randomization_error_message,
    :@species_generation_error_message,
    :@seed_import_error_message
  ].freeze

  # The starting map is constructed before its mode is selected. Refresh any
  # static encounters that were therefore created before Ironmon became active.
  def self.randomize_loaded_static_events
    return if !$game_map || !$game_map.events
    if !$scene || !$scene.respond_to?(:spritesets) || !$scene.spritesets
      @static_refresh_pending = true
      return
    end
    @static_refresh_pending = false
    $game_map.events.each_value do |event|
      next if !event.is_a?(StaticOverworldPokemonEvent)
      next if event.instance_variable_get(:@ironmon_randomized_species)
      terrain = event.instance_variable_get(:@terrain) || :Land
      event.setup_pokemon(event.species, event.level, terrain,
                          event.behavior_roaming, event.behavior_noticed)
    end
  end

  def self.refresh_pending_static_events
    randomize_loaded_static_events if @static_refresh_pending
  end

  def self.refresh_loaded_wild_encounter_table
    return if !$PokemonEncounters || !$game_map
    $PokemonEncounters.setup($game_map.map_id)
  end

  def self.preset_transaction_snapshot
    runtime_state = {}
    instance_variables.each do |name|
      runtime_state[name] = instance_variable_get(name)
    end
    return {
      :pokemon_global => [$PokemonGlobal, Marshal.dump($PokemonGlobal)],
      :game_switches => [$game_switches, Marshal.dump($game_switches)],
      :game_variables => [$game_variables, Marshal.dump($game_variables)],
      :trainer => [$Trainer, Marshal.dump($Trainer)],
      :pokemon_system => [$PokemonSystem, Marshal.dump($PokemonSystem)],
      :runtime_state => runtime_state
    }
  end

  def self.preset_transaction_error_state
    result = {}
    PRESET_TRANSACTION_ERROR_VARIABLES.each do |name|
      result[name] = instance_variable_get(name) if
        instance_variable_defined?(name)
    end
    return result
  end

  def self.restore_preset_object(entry)
    target = entry[0]
    restored = Marshal.load(entry[1])
    return restored if !target
    if target.is_a?(Array) || target.is_a?(Hash)
      target.replace(restored)
      return target
    end
    (target.instance_variables - restored.instance_variables).each do |name|
      target.remove_instance_variable(name)
    end
    restored.instance_variables.each do |name|
      target.instance_variable_set(
        name, restored.instance_variable_get(name)
      )
    end
    return target
  end

  def self.restore_preset_transaction(snapshot, error_state)
    $PokemonGlobal = restore_preset_object(snapshot[:pokemon_global])
    $game_switches = restore_preset_object(snapshot[:game_switches])
    $game_variables = restore_preset_object(snapshot[:game_variables])
    $Trainer = restore_preset_object(snapshot[:trainer])
    $PokemonSystem = restore_preset_object(snapshot[:pokemon_system])
    runtime_state = snapshot[:runtime_state]
    (instance_variables - runtime_state.keys).each do |name|
      remove_instance_variable(name)
    end
    runtime_state.each do |name, value|
      instance_variable_set(name, value)
    end
    PRESET_TRANSACTION_ERROR_VARIABLES.each do |name|
      remove_instance_variable(name) if instance_variable_defined?(name)
    end
    error_state.each do |name, value|
      instance_variable_set(name, value)
    end
    return false
  end

  def self.rollback_preset_transaction(snapshot)
    errors = preset_transaction_error_state
    return restore_preset_transaction(snapshot, errors)
  end

  def self.apply_preset(context = :new_run, seed_override = nil, start_tracker = true, expected_compatibility_fingerprint = nil)
    return false if !$PokemonGlobal || !$game_switches || !$game_variables
    transaction = preset_transaction_snapshot
    @preset_generation_error_message = nil
    @ability_randomization_error_message = nil
    @base_stat_randomization_error_message = nil
    @evolution_randomization_error_message = nil
    @move_access_randomization_error_message = nil
    @item_randomization_error_message = nil
    @species_generation_error_message = nil
    @seed_import_error_message = nil if context == :seed_import
    return rollback_preset_transaction(transaction) if
      !prepare_custom_fusion_pool

    $PokemonGlobal.ironmon_mode = true
    configuration
    $PokemonGlobal.ironmon_seed = if seed_override.nil?
                                    generate_run_seed
                                  else
                                    seed_override
                                  end
    $PokemonGlobal.ironmon_gym_leader_teams = {}
    reset_pivot_state
    prepare_player_fusion_pairing
    ensure_checkpoint_id
    record_custom_fusion_pool_metadata
    return rollback_preset_transaction(transaction) if
      !prepare_ability_randomization
    return rollback_preset_transaction(transaction) if
      !prepare_base_stat_randomization
    return rollback_preset_transaction(transaction) if
      !prepare_evolution_randomization
    return rollback_preset_transaction(transaction) if
      !prepare_move_access_randomization
    return rollback_preset_transaction(transaction) if
      !prepare_item_randomization
    return rollback_preset_transaction(transaction) if
      !prepare_species_mappings
    if expected_compatibility_fingerprint &&
       tracker_compatibility_fingerprint != expected_compatibility_fingerprint
      @seed_import_error_message = _INTL(
        "This seed token is not compatible with the installed Ironmon data."
      )
      return rollback_preset_transaction(transaction)
    end

    $game_switches[SWITCH_RANDOMIZED_AT_LEAST_ONCE] = true
    $game_switches[SWITCH_RANDOMIZED_MODE_INTRO] = false
    $game_switches[SWITCH_RANDOM_WILD] = true
    $game_switches[SWITCH_RANDOM_TRAINERS] = true
    $game_switches[SWITCH_RANDOM_ITEMS_GENERAL] = false
    enforce_difficulty_settings

    $game_variables[VAR_RANDOMIZER_WILD_POKE_BST] = FULL_RANDOM_BST_RANGE
    $game_switches[SWITCH_RANDOM_WILD_AREA] = false
    # Ironmon maps each encounter at its final creation path. Leaving the
    # built-in global map enabled would map ordinary encounters twice.
    $game_switches[SWITCH_WILD_RANDOM_GLOBAL] = false
    $game_switches[SWITCH_RANDOM_WILD_TO_FUSION] = false
    $game_switches[SWITCH_RANDOM_WILD_ONLY_CUSTOMS] = false
    $game_switches[SWITCH_RANDOM_WILD_LEGENDARIES] = true
    $game_switches[SWITCH_RANDOM_STARTERS] = true
    $game_switches[SWITCH_RANDOM_STARTER_FIRST_STAGE] = false
    $game_switches[SWITCH_RANDOM_STATIC_ENCOUNTERS] = true
    $game_switches[SWITCH_RANDOM_GIFT_POKEMON] = true

    $game_variables[VAR_RANDOMIZER_TRAINER_BST] = FULL_RANDOM_BST_RANGE
    $game_switches[TRAINER_CUSTOM_SPRITES_SWITCH] = false
    $game_switches[SWITCH_RANDOM_TRAINER_LEGENDARIES] = true
    $game_switches[SWITCH_RANDOMIZE_GYMS_SEPARATELY] = false
    $game_switches[SWITCH_RANDOMIZED_GYM_TYPES] = false
    $game_switches[SWITCH_RANDOM_GYM_CUSTOMS] = false
    $game_switches[SWITCH_GYM_RANDOM_EACH_BATTLE] = false
    $game_switches[SWITCH_RANDOM_GYM_PERSIST_TEAMS] = true
    pbSet(VAR_CURRENT_GYM_TYPE, -1)

    $game_switches[SWITCH_RANDOM_ITEMS] = false
    $game_switches[SWITCH_RANDOM_FOUND_ITEMS] = false
    $game_switches[SWITCH_RANDOM_GIVEN_ITEMS] = false
    $game_switches[SWITCH_RANDOM_ITEMS_MAPPED] = false
    $game_switches[SWITCH_RANDOM_ITEMS_DYNAMIC] = false
    $game_switches[SWITCH_RANDOM_TMS] = false
    $game_switches[SWITCH_RANDOM_FOUND_TMS] = false
    $game_switches[SWITCH_RANDOM_GIVEN_TMS] = false
    $game_switches[SWITCH_RANDOM_SHOP_ITEMS] = false
    $game_switches[SWITCH_RANDOM_HELD_ITEMS] = false
    return rollback_preset_transaction(transaction) if
      !begin_run_attempt($PokemonGlobal.ironmon_seed)
  rescue Exception => e
    @preset_generation_error_message = _INTL(
      "Ironmon could not apply its randomization preset: {1}", e.message
    )
    echoln @preset_generation_error_message
    return rollback_preset_transaction(transaction) if transaction
    return false
  else
    refresh_loaded_wild_encounter_table
    randomize_loaded_static_events
    start_tracker_run if start_tracker
    log_run_diagnostics(context)
    return true
  end
end
