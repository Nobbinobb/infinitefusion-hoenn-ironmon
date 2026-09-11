#===============================================================================
# Ironmon tracker area trainer reconstruction
#===============================================================================

module Ironmon
  TrackerTrainerAbilityContext = Struct.new(:species_data, :personalID)

  def self.tracker_area_trainer_index(recipe)
    mode = tracker_trainer_data_mode(recipe)
    @tracker_area_trainer_indexes ||= {}
    return @tracker_area_trainer_indexes[mode.name] if
      @tracker_area_trainer_indexes[mode.name]
    index = {}
    mode.list_all.each do |_trainer_id, trainer|
      components = trainer.id.is_a?(Array) ? trainer.id : [trainer.id]
      key = [components[0].to_s, components[1].to_s,
             components[2].to_i]
      index[key] = trainer
    end
    @tracker_area_trainer_indexes[mode.name] = index
    return index
  end

  def self.tracker_area_trainer_entries(area, recipe, discoveries,
                                        full_details, archived)
    index = tracker_area_trainer_index(recipe)
    generator = tracker_area_species_generator(recipe, :trainer)
    abilities_revealed = archived || tracker_connection.diagnostic_capabilities?(
      "pokemon.all_active", "pokemon.abilities"
    )
    moves_revealed = archived || tracker_connection.diagnostic_capabilities?(
      "pokemon.all_active", "pokemon.move_access"
    )
    return area["trainers"].map do |entry|
      key = [entry["trainer_type"], entry["trainer_name"],
             entry["party_id"]]
      trainer = index[key]
      defeated = discoveries.key?(entry["entry_id"]) ||
        (!archived && tracker_area_event_completed?(entry))
      revealed = full_details || defeated
      party = tracker_area_trainer_party(trainer, recipe, generator)
      trainer_type = GameData::TrainerType.try_get(entry["trainer_type"].to_sym)
      result = {
        "entry_id" => entry["entry_id"],
        "map_id" => entry["map_id"],
        "trainer_type" => trainer_type ? trainer_type.name :
          entry["trainer_type"],
        "trainer_name" => entry["trainer_name"],
        "party_size" => party.length,
        "defeated" => defeated,
        "details_revealed" => revealed,
        "party" => []
      }
      if revealed
        result["party"] = party.each_with_index.map do |pokemon, slot|
          member = {
            "slot" => slot + 1,
            "species_id" => "#{pokemon["species"].id}:0",
            "species_name" => pokemon["species"].name,
            "level" => pokemon["level"],
            "sprite_path" => tracker_lookup_sprite_path(pokemon["species"]),
            "abilities_revealed" => abilities_revealed,
            "moves_revealed" => moves_revealed
          }
          source = trainer.pokemon[slot] || {}
          member["abilities"] = tracker_area_trainer_abilities(
            pokemon["species"], source, recipe
          ) if abilities_revealed
          member["moves"] = tracker_area_trainer_moves(
            pokemon["species"], pokemon["level"], source, recipe
          ) if moves_revealed
          member
        end
      end
      result
    end
  end

  def self.tracker_area_trainer_abilities(species, source, recipe)
    generator = AbilityGenerator.new(
      recipe["seed"], allowed_ability_pool, ability_pool_fingerprint
    )
    slots = if fusion_ability_species?(species)
              generator.fusion_slots_for(species)
            elsif normal_ability_species?(species)
              generator.slots_for(species)
            else
              { :normal => original_normal_abilities(species),
                :hidden => original_hidden_abilities(species) }
            end
    requested = source[:ability_index]
    candidates = [0, 1].map do |parity|
      pokemon = TrackerTrainerAbilityContext.new(species, parity)
      index = resolved_ability_index(
        pokemon, requested.nil? ? parity : requested, slots
      )
      ability = index >= 2 ? slots[:hidden][index - 2] : nil
      ability || slots[:normal][index] || slots[:normal][0]
    end
    return candidates.compact.uniq.map do |ability|
      tracker_ability_snapshot(GameData::Ability.get(ability))
    end
  end

  def self.tracker_area_trainer_moves(species, level, source, recipe)
    return [] if source[:shadowness]
    randomized = recipe["move_access_generator_version"] ==
      MoveAccessGenerator::SCHEMA_VERSION
    entries = randomized ? generated_level_up_moves_for(
      species, tracker_move_access_generator(recipe)
    ) : original_level_up_moves_for(species)
    moves = entries.select { |entry| entry[0] <= level }.
      map { |entry| entry[1] }.reverse.uniq.reverse.last(Pokemon::MAX_MOVES)
    if !randomized && source[:species] == species.id
      authored = source[:moves]
      authored = source[:moves_hard] if
        source[:moves_hard] && !source[:moves_hard].empty?
      moves = authored.uniq.last(Pokemon::MAX_MOVES) if authored && !authored.empty?
    end
    return moves.map do |id|
      move = GameData::Move.get(id)
      { "id" => move.id.to_s, "name" => move.name,
        "type" => move.type.to_s, "category" => tracker_move_category(move),
        "description" => move.description }
    end
  end

  def self.tracker_area_trainer_party(trainer, recipe, generator)
    return [] if !trainer
    party = trainer.pokemon.each_with_index.map do |pokemon, slot|
      level = scaled_level(pokemon[:level])
      mapped = tracker_area_trainer_slot_species(
        trainer, pokemon[:species], slot, level, recipe, generator
      )
      {
        "species" => GameData::Species.get(mapped),
        "level" => level
      }
    end
    expansion_version = generation_profile_algorithm_version(
      "gym_party_expansion"
    )
    target_size = trainer_party_expansion_target(
      trainer, party.length, expansion_version
    )
    return party if !target_size || party.length >= target_size

    authored_levels = party.map { |pokemon| pokemon["level"] }
    authored_party_size = party.length
    trainer_name = trainer.name.to_s
    while party.length < target_size
      slot = party.length
      addition_index = slot - authored_party_size
      source = boss_trainer_source_species_for(
        recipe["seed"], trainer.trainer_type, trainer_name, slot,
        expansion_version
      )
      level = boss_trainer_addition_level(
        authored_levels, addition_index
      )
      purpose = expansion_version.to_i <= 1 ?
        :gym_addition : :boss_addition
      mapped = generator.map(
        source,
        [purpose, trainer.trainer_type, trainer_name, slot],
        trainer_requires_fully_evolved_species?(level)
      )
      party << {
        "species" => GameData::Species.get(mapped),
        "level" => level
      }
    end
    return party
  end

  def self.tracker_area_trainer_slot_species(trainer, source, slot, level,
                                             recipe, generator)
    if !tracker_loaded_recipe?(recipe)
      return generator.map(
        source, [:pbs, trainer.id, slot],
        trainer_requires_fully_evolved_species?(level)
      )
    end
    species = GameData::Species.get(source).species
    placeholders = [
      Settings::RIVAL_STARTER_PLACEHOLDER_SPECIES,
      Settings::VAR_1_PLACEHOLDER_SPECIES,
      Settings::VAR_2_PLACEHOLDER_SPECIES,
      Settings::VAR_3_PLACEHOLDER_SPECIES
    ]
    if placeholders.include?(species)
      species = trainer.replace_species_with_placeholder(species)
    elsif $game_switches[SWITCH_RANDOM_TRAINERS] &&
          !$game_switches[SWITCH_FIRST_RIVAL_BATTLE]
      species = trainer.replace_species_to_randomized(
        species, trainer.id, slot
      )
    end
    species = trainer.replaceSingleSpeciesModeIfApplicable(species)
    species = reverseFusionSpecies(species) if
      $game_switches[SWITCH_REVERSED_MODE]
    return species
  end
end
