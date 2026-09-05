#===============================================================================
# Authorized tracker-driven development controls
#===============================================================================

module Ironmon
  TRACKER_DEVELOPMENT_CAPABILITIES = [
    "development.auto_revive",
    "development.change_ability",
    "development.change_moves",
    "development.evolution",
    "development.full_heal",
    "development.give_item",
    "development.level",
    "development.swap_pokemon"
  ]
  TRACKER_DEVELOPMENT_ACTION_CAPABILITIES = {
    "set_auto_revive" => "development.auto_revive",
    "full_heal" => "development.full_heal",
    "set_level" => "development.level",
    "set_ability" => "development.change_ability",
    "set_moves" => "development.change_moves",
    "give_item" => "development.give_item",
    "evolve" => "development.evolution",
    "devolve" => "development.evolution",
    "swap_pokemon" => "development.swap_pokemon"
  }

  def self.tracker_auto_revive_enabled?
    return false if @tracker_auto_revive_enabled != true
    return tracker_connection.diagnostic_capability?(
      "development.auto_revive"
    )
  end

  def self.disable_tracker_auto_revive
    @tracker_auto_revive_enabled = false
  end

  def self.tracker_development_revive_player
    pokemon = tracker_development_player
    return false if !pokemon
    tracker_development_heal(pokemon)
    return true
  end

  def self.tracker_development_state(include_catalogs = true,
                                     include_evolutions = true)
    tracker_validate_any_debug_context(TRACKER_DEVELOPMENT_CAPABILITIES)
    pokemon = tracker_development_player
    result = {
      "auto_revive_enabled" => tracker_auto_revive_enabled?,
      "player" => pokemon ? tracker_development_player_state(pokemon) : nil
    }
    if include_catalogs && tracker_connection.diagnostic_capability?(
         "development.change_ability"
       )
      result["abilities"] = tracker_development_ability_catalog
    end
    if include_catalogs &&
       tracker_connection.diagnostic_capability?("development.change_moves")
      result["moves"] = tracker_development_move_catalog
    end
    if include_catalogs &&
       tracker_connection.diagnostic_capability?("development.give_item")
      result["items"] = tracker_development_item_catalog
    end
    if include_evolutions &&
       tracker_connection.diagnostic_capability?("development.evolution") &&
       pokemon
      result["evolutions"] = tracker_development_evolution_catalog(
        pokemon, true
      )
      result["devolutions"] = tracker_development_evolution_catalog(
        pokemon, false
      )
    end
    return result
  end

  def self.tracker_development_action(payload)
    payload ||= {}
    action = payload["action"].to_s
    capability = TRACKER_DEVELOPMENT_ACTION_CAPABILITIES[action]
    if !capability
      raise TrackerDebugError.new(
        "invalid_development_action", "The requested development action is invalid."
      )
    end
    tracker_validate_debug_context([capability])
    if action == "set_auto_revive"
      @tracker_auto_revive_enabled = payload["enabled"] == true
      return tracker_development_state(false, false)
    end
    tracker_validate_development_mutation_boundary
    pokemon = tracker_development_player
    if !pokemon
      raise TrackerDebugError.new(
        "pokemon_not_found", "The current player Pokemon is not available."
      )
    end
    case action
    when "full_heal"
      tracker_development_heal(pokemon)
    when "set_level"
      tracker_development_set_level(pokemon, payload["level"])
    when "set_ability"
      tracker_development_set_ability(pokemon, payload["ability_id"])
    when "set_moves"
      tracker_development_set_moves(pokemon, payload["move_ids"])
    when "give_item"
      tracker_development_give_item(payload["item_id"], payload["quantity"])
    when "evolve", "devolve"
      tracker_development_change_evolution(
        pokemon, payload["species_id"], action == "evolve"
      )
    when "swap_pokemon"
      pokemon = tracker_development_swap_pokemon(pokemon, payload["species_id"])
    end
    @tracker_player_pokemon = pokemon
    @tracker_player_json = nil
    return tracker_development_state(false, false)
  end

  def self.tracker_validate_development_mutation_boundary
    if tracker_battle_id
      raise TrackerDebugError.new(
        "development_action_unsafe",
        "Pokemon and inventory development actions are available outside battle."
      )
    end
    return true
  end

  def self.tracker_development_player
    return nil if !$Trainer || !$Trainer.party
    return usable_party[0] if respond_to?(:usable_party)
    return $Trainer.party.find { |pokemon| pokemon && !pokemon.egg? }
  end

  def self.tracker_development_player_state(pokemon)
    ability = pokemon.ability
    return {
      "pokemon_id" => pokemon.personalID.to_s,
      "species_id" => tracker_species_id(pokemon),
      "species_name" => pokemon.species_data.name,
      "level" => pokemon.level,
      "ability_id" => ability ? ability.id.to_s : nil,
      "ability_name" => ability ? ability.name : "",
      "move_ids" => pokemon.moves.map { |move| move.id.to_s },
      "move_names" => pokemon.moves.map { |move| move.name }
    }
  end

  def self.tracker_development_ability_catalog
    return @tracker_development_ability_catalog if
      @tracker_development_ability_catalog
    result = []
    GameData::Ability.each do |ability|
      result << { "id" => ability.id.to_s, "name" => ability.name }
    end
    @tracker_development_ability_catalog = result.sort_by do |ability|
      [ability["name"], ability["id"]]
    end
    return @tracker_development_ability_catalog
  end

  def self.tracker_development_move_catalog
    return @tracker_development_move_catalog if @tracker_development_move_catalog
    result = []
    GameData::Move.each do |move|
      result << { "id" => move.id.to_s, "name" => move.name }
    end
    @tracker_development_move_catalog = result.sort_by do |move|
      [move["name"], move["id"]]
    end
    return @tracker_development_move_catalog
  end

  def self.tracker_development_item_catalog
    return @tracker_development_item_catalog if @tracker_development_item_catalog
    result = []
    GameData::Item.each do |item|
      result << { "id" => item.id.to_s, "name" => item.name }
    end
    @tracker_development_item_catalog = result.sort_by do |item|
      [item["name"], item["id"]]
    end
    return @tracker_development_item_catalog
  end

  def self.tracker_development_evolution_catalog(pokemon, forward)
    recipe = tracker_debug_active_recipe
    entries = if forward
                tracker_lookup_evolution_targets(
                  pokemon.species_data, recipe, false
                ).values.flatten
              else
                tracker_development_evolution_predecessors(
                  pokemon.species_data, recipe
                )
              end
    result = entries.map do |entry|
      {
        "id" => entry["species_id"],
        "name" => entry["species_name"]
      }
    end
    return result.uniq { |entry| entry["id"] }.sort_by do |entry|
      [entry["name"], entry["id"]]
    end
  end

  def self.tracker_development_evolution_predecessors(species, recipe)
    result = []
    offset = 0
    loop do
      page = tracker_lookup_evolution_predecessor_page(
        species, recipe, offset, 50, false
      )
      result.concat(page["matches"])
      break if page["continuation"] == "complete"
      next_offset = page["next_offset"]
      break if !next_offset || next_offset <= offset
      offset = next_offset
    end
    return result
  end

  def self.tracker_development_heal(pokemon)
    previous = $PokemonSystem.instance_variable_get(:@no_reviving) if
      $PokemonSystem
    $PokemonSystem.instance_variable_set(:@no_reviving, false) if
      $PokemonSystem
    pokemon.heal
    return pokemon
  ensure
    $PokemonSystem.instance_variable_set(:@no_reviving, previous) if
      $PokemonSystem
  end

  def self.tracker_development_set_level(pokemon, value)
    level = value.to_i
    if level < 1 || level > 100
      raise TrackerDebugError.new(
        "invalid_level", "The level must be between 1 and 100."
      )
    end
    hp_fraction = pokemon.totalhp > 0 ? pokemon.hp.to_f / pokemon.totalhp : 1.0
    fainted = pokemon.hp <= 0
    pokemon.level = level
    pokemon.calc_stats
    hp = fainted ? 0 :
      [[(pokemon.totalhp * hp_fraction).round, 1].max, pokemon.totalhp].min
    pokemon.instance_variable_set(:@hp, hp)
    return pokemon
  end

  def self.tracker_development_set_ability(pokemon, value)
    ability = GameData::Ability.try_get(value.to_s.to_sym)
    if !ability
      raise TrackerDebugError.new(
        "invalid_ability", "The selected ability does not exist."
      )
    end
    pokemon.instance_variable_set(:@ironmon_development_ability_override, true)
    pokemon.instance_variable_set(:@ironmon_development_ability, ability.id)
    return pokemon
  end

  def self.tracker_development_set_moves(pokemon, values)
    if !values.is_a?(Array) || values.empty? || values.length > Pokemon::MAX_MOVES
      raise TrackerDebugError.new(
        "invalid_moves", "Select between one and four moves."
      )
    end
    ids = values.map do |value|
      move = GameData::Move.try_get(value.to_s.to_sym)
      if !move
        raise TrackerDebugError.new(
          "invalid_moves", "One selected move does not exist."
        )
      end
      move.id
    end
    if ids.uniq.length != ids.length
      raise TrackerDebugError.new(
        "invalid_moves", "A Pokemon cannot have the same move twice."
      )
    end
    pokemon.moves = ids.map { |id| Pokemon::Move.new(id) }
    ids.each { |id| pokemon.add_learned_move(id) }
    return pokemon
  end

  def self.tracker_development_give_item(value, quantity)
    item = GameData::Item.try_get(value.to_s.to_sym)
    count = quantity.to_i
    if !item || count < 1 || count > 999
      raise TrackerDebugError.new(
        "invalid_item", "Select an item and a quantity between 1 and 999."
      )
    end
    if !$PokemonBag || !$PokemonBag.pbStoreAllOrNone(item.id, count)
      raise TrackerDebugError.new(
        "bag_full", "The Bag does not have room for that item quantity."
      )
    end
    return true
  end

  def self.tracker_development_species(value)
    key = value.to_s.split(":", 2)[0]
    species = GameData::Species.try_get(key.to_sym)
    if !species || !tracker_lookup_species_available?(species)
      raise TrackerDebugError.new(
        "pokemon_not_found", "The selected Pokemon is not available to this run."
      )
    end
    return species
  end

  def self.tracker_development_direct_targets(species)
    targets = tracker_lookup_evolution_targets(
      species, tracker_debug_active_recipe, false
    )
    return targets.values.flatten.map { |entry| entry["species_id"] }
  end

  def self.tracker_development_change_evolution(pokemon, value, forward)
    target = tracker_development_species(value)
    current = pokemon.species_data
    valid = if forward
              tracker_development_direct_targets(current).include?(
                "#{target.id}:#{target.form}"
              )
            else
              tracker_development_direct_targets(target).include?(
                "#{current.id}:#{current.form}"
              )
            end
    if !valid
      raise TrackerDebugError.new(
        "invalid_evolution",
        "The selected Pokemon is not a direct generated evolution in that direction."
      )
    end
    pokemon.species = target.id
    pokemon.form_simple = target.form if target.form != 0
    normalize_ability_index(pokemon)
    pokemon.calc_stats
    return pokemon
  end

  def self.tracker_development_swap_pokemon(current, value)
    target = tracker_development_species(value)
    replacement = Pokemon.new(target.id, current.level, $Trainer, false)
    replacement.form_simple = target.form if target.form != 0
    replacement.reset_moves
    normalize_ability_index(replacement)
    index = $Trainer.party.index(current)
    if !index
      raise TrackerDebugError.new(
        "pokemon_not_found", "The current player Pokemon is no longer in the party."
      )
    end
    $Trainer.party[index] = replacement
    return replacement
  end
end
