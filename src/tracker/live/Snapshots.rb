#===============================================================================
# Ironmon tracker current-state and live snapshot construction
#===============================================================================

module Ironmon
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

  def self.tracker_current_state
    payload = {
      "ironmon_active" => active?,
      "overworld_encounters" => !!($PokemonSystem &&
        $PokemonSystem.overworld_encounters),
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
      "completed_run" => tracker_recoverable_completed_run_recipe,
      "active_run_preparation_ready" =>
        tracker_active_run_preparation_ready?
    }
    coverage = tracker_type_coverage_context
    payload["type_coverage"] = coverage if coverage
    assignments = tracker_active_fusion_assignment_recipe
    payload["fusion_assignments"] = assignments if assignments
    return payload
  end

  def self.tracker_active_run_preparation_ready?
    return false if !active? || !$PokemonGlobal
    attempt = current_run_attempt if respond_to?(:current_run_attempt)
    return false if !attempt || attempt["result"] != "active"
    ready_run_id =
      $PokemonGlobal.ironmon_tracker_active_run_preparation_run_id.to_s
    return !ready_run_id.empty? && ready_run_id == attempt["run_id"].to_s
  end

  def self.tracker_active_fusion_assignment_recipe
    return nil if !tracker_active_run_preparation_ready?
    return nil if !active? || !$PokemonGlobal
    return nil if !tracker_connection.diagnostic_capabilities?(
      *TRACKER_OBTAINABILITY_DIAGNOSTIC_CAPABILITIES
    )
    metadata = [
      $PokemonGlobal.ironmon_seed,
      PlayerFusionMapper::SCHEMA_VERSION,
      $PokemonGlobal.ironmon_evolution_fusion_generator_version,
      $PokemonGlobal.ironmon_evolution_fusion_rules_version,
      $PokemonGlobal.ironmon_evolution_source_fingerprint,
      $PokemonGlobal.ironmon_evolution_taxonomy_fingerprint,
      $PokemonGlobal.ironmon_evolution_method_fingerprint,
      $PokemonGlobal.ironmon_evolution_base_stat_source_fingerprint,
      $PokemonGlobal.ironmon_evolution_fusion_target_pool_version,
      $PokemonGlobal.ironmon_evolution_fusion_target_pool_size,
      $PokemonGlobal.ironmon_evolution_fusion_target_pool_fingerprint
    ]
    return nil if metadata.any? { |value| value.nil? }
    return {
      "seed" => metadata[0],
      "player_fusion_generator_version" => metadata[1],
      "generator_version" => metadata[2],
      "rules_version" => metadata[3],
      "source_fingerprint" => metadata[4],
      "taxonomy_fingerprint" => metadata[5],
      "method_fingerprint" => metadata[6],
      "base_stat_source_fingerprint" => metadata[7],
      "target_pool_version" => metadata[8],
      "target_pool_size" => metadata[9],
      "target_pool_fingerprint" => metadata[10]
    }
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
    return {
      "battle_id" => @tracker_battle_id,
      "selected_target_position" => @tracker_selected_target_position
    }
  end

  def self.tracker_active_types(pokemon, battler = nil)
    types = if battler && battler.respond_to?(:pbTypes)
              battler.pbTypes(true)
            else
              pokemon.types
            end
    return types.compact.uniq.map { |type| type.to_s }
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
      "fusion" => pokemon.isFusion? || pokemon.isTripleFusion?,
      "gender" => tracker_gender(pokemon),
      "sprite_path" => tracker_sprite_path(pokemon),
      "level" => pokemon.level,
      "current_hp" => pokemon.hp,
      "maximum_hp" => pokemon.totalhp,
      "status" => pokemon.status.to_s,
      "confused" => tracker_player_confused?,
      "types" => tracker_active_types(pokemon, @tracker_player_battler),
      "defensive_overview" => tracker_defense_snapshot(pokemon, @tracker_player_battler),
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
      "party_index" => battler.pokemonIndex,
      "species_id" => tracker_species_id(pokemon),
      "species_name" => species.name,
      "fusion" => pokemon.isFusion? || pokemon.isTripleFusion?,
      "sprite_path" => tracker_sprite_path(pokemon),
      "level" => battler.level,
      "types" => tracker_active_types(pokemon, battler),
      "defensive_overview" => tracker_defense_snapshot(pokemon, battler, true),
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
    return tracker_observed_move(
      battler.pokemon, move_id, "enemy_use", pp_after_use, battler, true
    )
  end

  def self.tracker_battler_stat_stages(battler)
    stages = battler && battler.respond_to?(:stages) ? battler.stages : {}
    return {
      "attack" => stages[:ATTACK].to_i,
      "defense" => stages[:DEFENSE].to_i,
      "special_attack" => stages[:SPECIAL_ATTACK].to_i,
      "special_defense" => stages[:SPECIAL_DEFENSE].to_i,
      "speed" => stages[:SPEED].to_i,
      "accuracy" => stages[:ACCURACY].to_i,
      "evasion" => stages[:EVASION].to_i
    }
  end

  def self.tracker_player_level_up_moves(pokemon)
    return pokemon.moves.map do |move|
      tracker_observed_move(pokemon, move.id, "player_initial", nil)
    end.select { |move| move["source"] == "level_up" }
  end

  def self.tracker_observed_move(pokemon, move_id, origin, pp_after_use,
                                 battler = nil, enemy = false)
    move_data = GameData::Move.get(move_id)
    pokemon_move = Pokemon::Move.new(move_data.id)
    pokemon_move.pp = pp_after_use if pp_after_use
    presentation = tracker_move_power_presentation(
      pokemon_move, pokemon, battler, enemy
    )
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
      "type" => presentation && presentation["type"] ?
        presentation["type"] : move_data.type.to_s,
      "category" => tracker_move_category(move_data),
      "description" => move_data.description,
      "power" => move_data.base_damage || 0,
      "accuracy" => move_data.accuracy || 0,
      "total_pp" => move_data.total_pp,
      "pp_after_use" => pp_after_use,
      "power_presentation" => presentation
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
    presentation = tracker_move_power_presentation(
      move, @tracker_player_pokemon, @tracker_player_battler, false
    )
    return {
      "id" => move.id.to_s,
      "name" => move.name,
      "type" => presentation && presentation["type"] ?
        presentation["type"] : move.type.to_s,
      "category" => tracker_move_category(GameData::Move.get(move.id)),
      "description" => GameData::Move.get(move.id).description,
      "current_pp" => move.pp,
      "total_pp" => move.total_pp,
      "power" => move.base_damage || 0,
      "accuracy" => move.accuracy || 0,
      "power_presentation" => presentation
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
end
