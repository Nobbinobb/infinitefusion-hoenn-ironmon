#===============================================================================
# Ironmon tracker run and live-battle lifecycle
#===============================================================================

module Ironmon
  TRACKER_STATE_INTERVAL_SECONDS = 0.1

  def self.tracker_connection
    @tracker_connection ||= TrackerConnection.new
    return @tracker_connection
  end

  def self.update_tracker_connection
    tracker_connection.update
    update_tracker_encounter_mode
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
    $PokemonGlobal.ironmon_tracker_active_run_preparation_run_id = nil
    $PokemonGlobal.ironmon_run_result = nil
    tracker_connection.reset_area_discoveries
    @tracker_battle = nil
    @tracker_battle_id = nil
    @tracker_player_battler = nil
    @tracker_player_pokemon = nil
    @tracker_player_json = nil
    @tracker_move_menu_pokemon_id = nil
    @tracker_enemy_battlers = {}
    @tracker_enemy_pokemon_ids = {}
    @tracker_enemy_json = {}
    @tracker_enemy_move_signatures = {}
    @tracker_enemy_abilities = {}
    @tracker_next_enemy_state_at = 0.0
    @tracker_pending_battle_item = nil
    @tracker_battle_command = nil
    @tracker_player_target_selection_active = false
    @tracker_selected_target_position = nil
    @tracker_starter_selection = nil
    tracker_connection.send_run_started
  end

  def self.refresh_tracker_run_after_load
    return if checkpoint_reset_loading?
    tracker_connection.send_run_started
  end

  def self.announce_tracker_active_run_preparation_ready
    return false if !$PokemonGlobal
    $PokemonGlobal.ironmon_tracker_active_run_preparation_run_id =
      ensure_tracker_run_id
    tracker_connection.send_event(
      "active_run_preparation_ready", tracker_current_state
    )
    return true
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
    @tracker_enemy_pokemon_ids = {}
    @tracker_enemy_json = {}
    @tracker_enemy_move_signatures = {}
    @tracker_enemy_abilities = {}
    @tracker_next_enemy_state_at = 0.0
    @tracker_next_state_at = 0.0
    @tracker_pending_battle_item = nil
    @tracker_battle_command = nil
    @tracker_player_target_selection_active = false
    @tracker_selected_target_position = nil
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
    @tracker_enemy_pokemon_ids = {}
    @tracker_enemy_json = {}
    @tracker_enemy_move_signatures = {}
    @tracker_enemy_abilities = {}
    @tracker_pending_battle_item = nil
    @tracker_battle_command = nil
    @tracker_player_target_selection_active = false
    @tracker_selected_target_position = nil
  end

  def self.update_tracker_encounter_mode
    return if !active? || !$PokemonSystem
    mode = !!$PokemonSystem.overworld_encounters
    changed = !@tracker_overworld_encounters.nil? &&
      @tracker_overworld_encounters != mode
    @tracker_overworld_encounters = mode
    tracker_connection.send_event(
      "encounter_mode_changed", tracker_current_state
    ) if changed
  end

  def self.tracker_player_sent_out(battler)
    return if !active? || !@tracker_battle_id || !battler
    register_statistics_battler_item(battler) if
      respond_to?(:register_statistics_battler_item)
    @tracker_player_battler = battler
    @tracker_player_pokemon = battler.pokemon
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
    @tracker_enemy_pokemon_ids ||= {}
    pokemon_id = tracker_enemy_id(battler.pokemon)
    return if @tracker_enemy_pokemon_ids[battler.index] == pokemon_id
    @tracker_move_menu_pokemon_id = nil
    @tracker_enemy_battlers[battler.index] = battler
    @tracker_enemy_pokemon_ids[battler.index] = pokemon_id
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
    move = tracker_observed_move(
      battler.pokemon, move_id, "enemy_use", pp_after_use, battler, true
    )
    payload = {
      "enemy_id" => tracker_enemy_id(battler.pokemon),
      "position" => battler.index,
      "party_index" => battler.pokemonIndex,
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
end
