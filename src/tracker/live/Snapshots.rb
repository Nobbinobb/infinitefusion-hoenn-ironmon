#===============================================================================
# Ironmon tracker current-state and live snapshot construction
#===============================================================================

module Ironmon
  TRACKER_GYM_BADGE_COUNT = 8
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
      "badges" => tracker_badge_snapshot,
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

  def self.tracker_badge_snapshot
    return nil if !$Trainer
    return Array.new(TRACKER_GYM_BADGE_COUNT) { |index| !!$Trainer.badges[index] }
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
      "generation_profile_id" => pinned_generation_profile_id,
      "normal_species_count" => NB_POKEMON,
      "base_stat_generator_version" =>
        $PokemonGlobal.ironmon_base_stat_generator_version,
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
      "target_pool_fingerprint" => metadata[10],
      "packed_custom_fusion_pool" =>
        [fusion_predecessor_index.packed_pool_membership].pack("m0")
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
      "fusion_pool_schema_version" => generation_profile_algorithm_version(
        "custom_fusion_eligibility"
      ),
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
    battler = @tracker_player_battler if @tracker_battle_id
    transformed = !!(battler && battler.effects[PBEffects::Transform])
    transform_identity = transformed ?
      battler.instance_variable_get(:@ironmon_tracker_transform_identity) : nil
    species = pokemon.species_data
    nature = pokemon.nature
    ability = battler ? battler.ability : pokemon.ability
    held_item = battler ? battler.item : pokemon.item
    current_moves = battler ? battler.moves : pokemon.moves
    displayed_species_id = transform_identity ?
      transform_identity["species_id"] : tracker_species_id(pokemon)
    displayed_species_name = transform_identity ?
      transform_identity["species_name"] : species.name
    displayed_fusion = transform_identity ?
      transform_identity["fusion"] : pokemon.isFusion? || pokemon.isTripleFusion?
    displayed_sprite_path = transform_identity ?
      transform_identity["sprite_path"] : tracker_sprite_path(pokemon)
    displayed_base_stat_total = transform_identity ?
      transform_identity["base_stat_total"] :
      pokemon.baseStats.values.inject(0) { |sum, value| sum + value }
    return {
      "pokemon_id" => pokemon.personalID.to_s,
      "species_id" => displayed_species_id,
      "original_species_id" => tracker_species_id(pokemon),
      "nickname" => pokemon.name,
      "species_name" => displayed_species_name,
      "fusion" => displayed_fusion,
      "transformed" => transformed,
      "gender" => tracker_gender(pokemon),
      "sprite_path" => displayed_sprite_path,
      "level" => pokemon.level,
      "current_hp" => pokemon.hp,
      "maximum_hp" => pokemon.totalhp,
      "status" => pokemon.status.to_s,
      "confused" => tracker_player_confused?,
      "types" => tracker_active_types(pokemon, battler),
      "defensive_overview" => tracker_defense_snapshot(pokemon, battler),
      "ability" => ability ? ability.name : "None",
      "ability_details" => ability ? tracker_ability_snapshot(ability) : nil,
      "stored_ability_details" => pokemon.ability ?
        tracker_ability_snapshot(pokemon.ability) : nil,
      "copied_ability_details" => transform_identity ?
        transform_identity["ability_details"] : nil,
      "held_item" => held_item ? held_item.name : nil,
      "held_item_description" => held_item ? held_item.description : nil,
      "attack" => battler ? battler.attack : pokemon.attack,
      "defense" => battler ? battler.defense : pokemon.defense,
      "special_attack" => battler ? battler.spatk : pokemon.spatk,
      "special_defense" => battler ? battler.spdef : pokemon.spdef,
      "speed" => battler ? battler.speed : pokemon.speed,
      "stat_stages" => tracker_battler_stat_stages(battler),
      "base_stat_total" => displayed_base_stat_total,
      "nature" => nature ? nature.name : nil,
      "nature_adjustments" => transformed ?
        tracker_neutral_nature_adjustments : tracker_nature_adjustments(pokemon),
      "moves" => current_moves.map { |move| tracker_move_snapshot(move, battler) },
      "stored_moves" => battler ?
        pokemon.moves.map { |move| tracker_move_snapshot(move, nil) } : [],
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
    summary = tracker_learnset_display_summary(pokemon)
    return pokemon.moves.map do |move|
      move_id = GameData::Move.get(move.id).id
      provenance = summary[:provenance][move_id]
      next if !provenance
      tracker_observed_move(
        pokemon, move_id, "player_initial", nil, nil, false, provenance
      )
    end.compact
  end

  def self.tracker_observed_move(pokemon, move_id, origin, pp_after_use,
                                 battler = nil, enemy = false, provenance = nil)
    move_data = GameData::Move.get(move_id)
    pokemon_move = Pokemon::Move.new(move_data.id)
    pokemon_move.pp = pp_after_use if pp_after_use
    presentation = tracker_move_power_presentation(
      pokemon_move, pokemon, battler, enemy
    )
    learned_level = provenance ? provenance[0] : 0
    learn_order = provenance ? provenance[1] : 0
    found = !provenance.nil?
    if !provenance
      pokemon.getMoveList.each_with_index do |entry, index|
        listed_move = GameData::Move.try_get(entry[1])
        next if !listed_move || listed_move.id != move_data.id || entry[0] > pokemon.level
        learned_level = entry[0]
        learn_order = index
        found = true
      end
    end
    source = found ? "level_up" : "unknown"
    result = {
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
    if presentation.equal?(DEFERRED_MOVE_POWER_PRESENTATION)
      result.delete("power_presentation")
      result["power_presentation_deferred"] = true
    end
    return result
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

  def self.tracker_move_snapshot(move, battler = @tracker_player_battler)
    move_data = GameData::Move.get(move.id)
    presentation_move = if move.is_a?(Pokemon::Move)
                          move
                        else
                          Pokemon::Move.new(move.id).tap { |entry| entry.pp = move.pp }
                        end
    presentation = tracker_move_power_presentation(
      presentation_move, @tracker_player_pokemon, battler, false
    )
    result = {
      "id" => move_data.id.to_s,
      "name" => move_data.name,
      "type" => presentation && presentation["type"] ?
        presentation["type"] : move_data.type.to_s,
      "category" => tracker_move_category(move_data),
      "description" => move_data.description,
      "current_pp" => move.pp,
      "total_pp" => move.total_pp,
      "power" => move_data.base_damage || 0,
      "accuracy" => move_data.accuracy || 0,
      "power_presentation" => presentation
    }
    if presentation.equal?(DEFERRED_MOVE_POWER_PRESENTATION)
      result.delete("power_presentation")
      result["power_presentation_deferred"] = true
    end
    return result
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

  def self.tracker_neutral_nature_adjustments
    return {
      "attack" => "neutral",
      "defense" => "neutral",
      "special_attack" => "neutral",
      "special_defense" => "neutral",
      "speed" => "neutral"
    }
  end

  def self.tracker_record_transform(battler, target)
    return if !battler || battler.index.odd? || !target || !target.pokemon
    pokemon = target.pokemon
    battler.instance_variable_set(:@ironmon_tracker_transform_identity, {
      "species_id" => tracker_species_id(pokemon),
      "species_name" => pokemon.species_data.name,
      "fusion" => pokemon.isFusion? || pokemon.isTripleFusion?,
      "sprite_path" => tracker_sprite_path(pokemon),
      "ability_details" => target.ability ?
        tracker_ability_snapshot(target.ability) : nil,
      "base_stat_total" => pokemon.baseStats.values.inject(0) do |sum, value|
        sum + value
      end
    })
  rescue Exception => error
    echoln "Ironmon tracker transform identity failed safely: #{error.message}"
  end

  def self.tracker_learnset_progress(pokemon)
    summary = tracker_learnset_display_summary(pokemon)
    GameData::Move.get(summary[:invalid_moves][0]) if !summary[:invalid_moves].empty?
    learned = pokemon.learned_moves || []
    if !summary[:progress] || summary[:learned] != learned ||
       !tracker_live_move_records_current?(summary[:learned_records])
      records = learned.map do |move|
        data = GameData::Move.get(move)
        [tracker_copy_live_summary(move), data, data.id]
      end
      learned_ids = records.map { |record| record[2] }.uniq
      summary[:learned] = tracker_copy_live_summary(learned)
      summary[:learned_records] = records
      summary[:progress] = {
        "learned_moves" => learned_ids.count { |id| summary[:move_ids][id] },
        "maximum_moves" => summary[:move_ids].length,
        "next_move_level" => summary[:next_level]
      }
    end
    return summary[:progress].dup
  end

  def self.tracker_live_move_records_current?(records)
    return records.all? do |id, previous, canonical_id|
      current = if id.is_a?(GameData::Move)
                  id
                else
                  key = id.is_a?(String) ? id.to_sym : id
                  GameData::Move::DATA[key] || GameData::Move.try_get(id)
                end
      current.equal?(previous) && (!current || current.id == canonical_id)
    end
  end

  def self.tracker_live_summary_context(pokemon)
    return [
      tracker_live_summary_species(pokemon), pokemon.form,
      $PokemonGlobal,
      $PokemonGlobal && $PokemonGlobal.ironmon_seed,
      $PokemonGlobal && $PokemonGlobal.ironmon_mode,
      @current_generation_profile,
      @active_generation_profile_id, @run_generation_profile_id,
      @move_access_randomization_ready, @move_access_generator,
      @allowed_level_up_move_pool,
      @move_access_source_entries,
      @evolution_randomization_ready, @evolution_generator,
      @fusion_evolution_generator, @evolution_catalog,
      @base_stat_source_fingerprint, @base_stat_generator,
      @custom_fusion_pool_service,
      @profile_custom_fusion_pool_services,
      @fusion_evolution_target_catalog,
      $PokemonSystem && $PokemonSystem.language
    ]
  end

  def self.tracker_live_summary_species(pokemon)
    identity = pokemon.species
    registered = GameData::Species::DATA[identity]
    cached = @tracker_live_summary_species
    if cached && cached[:identity] == identity &&
       cached[:registered].equal?(registered) &&
       (!cached[:body_id] ||
        (cached[:body].equal?(GameData::Species::DATA[cached[:body_id]]) &&
         cached[:head].equal?(GameData::Species::DATA[cached[:head_id]])))
      return cached[:species]
    end
    species = pokemon.species_data
    cached = { :identity => identity, :registered => registered, :species => species }
    if species.is_a?(GameData::FusedSpecies)
      cached[:body_id] = species.body_pokemon.id
      cached[:head_id] = species.head_pokemon.id
      cached[:body] = GameData::Species::DATA[cached[:body_id]]
      cached[:head] = GameData::Species::DATA[cached[:head_id]]
    end
    @tracker_live_summary_species = cached
    return species
  end

  def self.tracker_learnset_display_summary(pokemon)
    context = tracker_live_summary_context(pokemon)
    cached = @tracker_learnset_display_summary
    species = tracker_live_summary_species(pokemon)
    generated = move_access_randomization_active? &&
      (normal_move_access_species?(species) || fusion_move_access_species?(species))
    source = pokemon.getMoveList if !generated
    if !cached || cached[:context] != context ||
       (source && cached[:source] != source) ||
       !tracker_live_move_records_current?(cached[:records])
      source ||= pokemon.getMoveList
      entries = []
      move_ids = {}
      records = []
      invalid_moves = []
      source.each_with_index do |entry, index|
        move = GameData::Move.try_get(entry[1])
        invalid_moves << entry[1] if !move
        records << [tracker_copy_live_summary(entry[1]), move, move && move.id]
        entries << [entry[0], move && move.id, index]
        move_ids[move.id] = true if move
      end
      # Keep one species only, and capture generator identities after lazy setup.
      cached = {
        :context => tracker_live_summary_context(pokemon),
        :source => tracker_copy_live_summary(source),
        :entries => entries, :move_ids => move_ids, :records => records,
        :invalid_moves => tracker_copy_live_summary(invalid_moves)
      }
      @tracker_learnset_display_summary = cached
    end
    if cached[:level] != pokemon.level
      provenance = {}
      next_level = nil
      cached[:entries].each do |level, move_id, index|
        if level <= pokemon.level
          provenance[move_id] = [level, index] if move_id
        elsif !next_level || level < next_level
          next_level = level
        end
      end
      cached[:level] = pokemon.level
      cached[:provenance] = provenance
      cached[:next_level] = next_level
      cached[:progress] = nil
    end
    return cached
  end

  def self.tracker_copy_live_summary(value)
    return value.map { |entry| tracker_copy_live_summary(entry) } if
      value.is_a?(Array)
    if value.is_a?(Hash)
      return value.each_with_object({}) do |(key, entry), copy|
        copy[key] = tracker_copy_live_summary(entry)
      end
    end
    return value.dup if value.is_a?(String)
    return value
  end

  private_class_method :tracker_live_move_records_current?,
                       :tracker_live_summary_context,
                       :tracker_live_summary_species,
                       :tracker_learnset_display_summary,
                       :tracker_copy_live_summary

  def self.tracker_evolutions(pokemon)
    context = tracker_live_summary_context(pokemon)
    cached = @tracker_evolution_display_summary
    species = tracker_live_summary_species(pokemon)
    generated = evolution_randomization_active? &&
      generated_evolution_runtime_species?(species)
    source = pokemon.species_data.get_evolutions(true) if
      !generated
    if !cached || cached[:context] != context ||
       (source && cached[:source] != source)
      methods = if generated
                  branches = if fusion_evolution_runtime_species?(
                                  species
                                )
                               generated_fusion_evolution_branches_for(
                                 species
                               )
                             else
                               generated_normal_evolution_branches_for(
                                 species
                               )
                             end
                  branches.flat_map { |branch| branch[:effective_methods] }
                else
                  (source || pokemon.species_data.get_evolutions(true)).map do |evolution|
                    {
                      :method => evolution[1],
                      :parameter => evolution[2]
                    }
                  end
                end
      cached = {
        :context => tracker_live_summary_context(pokemon),
        :source => tracker_copy_live_summary(source),
        :methods => tracker_copy_live_summary(methods)
      }
      @tracker_evolution_display_summary = cached
    end
    # Labels and method definitions can change without replacing the generators.
    methods = cached[:methods]
    evolutions = methods.map do |method|
      tracker_evolution_snapshot(method[:method], method[:parameter])
    end
    evolutions.sort_by! { |evolution| evolution["requirement"] }
    return evolutions
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
      result["item_name"] = item.name.dup
      result["requirement"] = item.name.dup
    elsif evolution.parameter == Integer && evolution.minimum_level == 0
      condition = method_name.sub(/^Level\s*/, "")
      result["kind"] = "level"
      result["level"] = parameter
      result["requirement"] = condition.empty? ? "Level #{parameter}" : "Level #{parameter} (#{condition})"
    end
    return result
  end

  def self.tracker_healing_snapshot(maximum_hp)
    cache = tracker_inventory_display_cache(maximum_hp)
    item_count = 0
    potential_hp = 0
    if cache
      cache[:rows].each do |row|
        next if row[7] <= 0
        quantity = row[1][1]
        item_count += quantity
        potential_hp += row[7] * quantity
      end
    end
    percentage = maximum_hp > 0 ? (potential_hp * 100.0 / maximum_hp).round(1) : 0.0
    return {
      "item_count" => item_count,
      "potential_hp" => potential_hp,
      "percentage" => percentage,
      "items" => tracker_inventory_display_items(cache)
    }
  end

  def self.tracker_battle_items(maximum_hp)
    return tracker_inventory_display_items(
      tracker_inventory_display_cache(maximum_hp)
    )
  end

  def self.tracker_inventory_display_items(cache)
    return [] if !cache
    items = []
    cache[:items].each do |row|
      quantity = row[1][1]
      next if quantity.to_i <= 0
      template = row[8]
      items.push({
        "id" => template["id"].dup,
        "name" => tracker_inventory_display_copy(template["name"]),
        "description" => tracker_inventory_display_copy(template["description"]),
        "quantity" => tracker_inventory_display_copy(quantity),
        "category" => template["category"].dup,
        "requires_move" => template["requires_move"]
      })
    end
    return items
  end

  def self.tracker_inventory_display_copy(value)
    return value.is_a?(String) ? value.dup : value
  end

  def self.tracker_inventory_display_cache(maximum_hp)
    if !$PokemonBag
      @tracker_inventory_display_cache = nil
      return nil
    end
    pockets = $PokemonBag.pockets
    cache = @tracker_inventory_display_cache
    if tracker_inventory_display_cache_valid?(cache, pockets)
      tracker_inventory_display_refresh_healing(cache, maximum_hp) if
        cache[:maximum_hp] != maximum_hp ||
        cache[:maximum_hp].class != maximum_hp.class
      return cache
    end
    rows = []
    items = []
    pockets.each do |pocket|
      next if !pocket
      pocket.each do |entry|
        item = GameData::Item.try_get(entry[0])
        battle_item = item && item.battle_use > 0
        # Keep quantities live: they do not change names, categories or sorting.
        # Exact source/metadata comparison also catches direct external edits.
        row = [tracker_inventory_display_copy(entry[0]), entry, item,
               item && tracker_inventory_display_copy(item.id),
               item && item.battle_use,
               battle_item && tracker_inventory_display_copy(item.name),
               battle_item && tracker_inventory_display_copy(item.description),
               tracker_item_healing(entry[0], maximum_hp)]
        rows.push(row)
        next if !battle_item
        row[8] = {
          "id" => item.id.to_s,
          "name" => row[5], "description" => row[6],
          "category" => tracker_battle_item_category(item, maximum_hp),
          "requires_move" => item.battle_use == 2 || item.battle_use == 7
        }
        items.push(row)
      end
    end
    @tracker_inventory_display_cache = {
      :bag => $PokemonBag, :source => pockets, :pockets => pockets.dup,
      :maximum_hp => maximum_hp, :rows => rows,
      :items => items.sort_by { |row| [row[8]["category"], row[5], row[8]["id"]] },
      :active_profile => tracker_inventory_display_copy(@active_generation_profile_id),
      :run_profile => tracker_inventory_display_copy(@run_generation_profile_id),
      :installed_profile => @current_generation_profile,
      :language => $PokemonSystem && $PokemonSystem.language,
      :rage_candy_bar => Settings::RAGE_CANDY_BAR_CURES_STATUS_PROBLEMS,
      :fixed_healing => TRACKER_FIXED_HEALING.dup,
      :pp_items => TRACKER_PP_RESTORE_ITEMS.dup,
      :status_items => TRACKER_STATUS_ITEMS.dup,
      :combat_prefixes => TRACKER_COMBAT_STAT_ITEM_PREFIXES.map(&:dup)
    }
    return @tracker_inventory_display_cache
  end

  def self.tracker_inventory_display_refresh_healing(cache, maximum_hp)
    changed_category = false
    cache[:rows].each do |row|
      row[7] = tracker_item_healing(row[0], maximum_hp)
      next if !row[8]
      healing = tracker_item_healing(row[3], maximum_hp)
      category = row[8]["category"]
      if healing > 0
        next if category == "healing"
        row[8]["category"] = "healing"
      elsif category == "healing"
        row[8]["category"] = tracker_battle_item_category(row[2], maximum_hp)
      else
        next
      end
      changed_category = true
    end
    if changed_category
      cache[:items].sort_by! { |row| [row[8]["category"], row[5], row[8]["id"]] }
    end
    cache[:maximum_hp] = maximum_hp
  end

  def self.tracker_inventory_display_cache_valid?(cache, pockets)
    return false if !cache || !cache[:bag].equal?($PokemonBag) ||
      !cache[:source].equal?(pockets) ||
      cache[:active_profile] != @active_generation_profile_id ||
      cache[:run_profile] != @run_generation_profile_id ||
      !cache[:installed_profile].equal?(@current_generation_profile) ||
      cache[:language] != ($PokemonSystem && $PokemonSystem.language) ||
      cache[:rage_candy_bar] != Settings::RAGE_CANDY_BAR_CURES_STATUS_PROBLEMS ||
      cache[:fixed_healing] != TRACKER_FIXED_HEALING ||
      cache[:pp_items] != TRACKER_PP_RESTORE_ITEMS ||
      cache[:status_items] != TRACKER_STATUS_ITEMS ||
      cache[:combat_prefixes] != TRACKER_COMBAT_STAT_ITEM_PREFIXES ||
      cache[:pockets].length != pockets.length
    index = 0
    pockets.each_with_index do |pocket, pocket_index|
      return false if !cache[:pockets][pocket_index].equal?(pocket)
      next if !pocket
      pocket.each do |entry|
        row = cache[:rows][index]
        return false if !row || row[0] != entry[0] || !row[1].equal?(entry)
        item = GameData::Item.try_get(entry[0])
        return false if !row[2].equal?(item)
        return false if item && (row[3] != item.id || row[4] != item.battle_use)
        return false if row[8] && (row[5] != item.name || row[6] != item.description)
        index += 1
      end
    end
    return index == cache[:rows].length
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

  private_class_method :tracker_inventory_display_items,
    :tracker_inventory_display_copy, :tracker_inventory_display_cache,
    :tracker_inventory_display_refresh_healing,
    :tracker_inventory_display_cache_valid?
end
