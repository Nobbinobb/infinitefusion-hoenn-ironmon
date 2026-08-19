#===============================================================================
# Ironmon tracker area trainer reconstruction
#===============================================================================

module Ironmon
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
          {
            "slot" => slot + 1,
            "species_id" => "#{pokemon["species"].id}:0",
            "species_name" => pokemon["species"].name,
            "level" => pokemon["level"],
            "sprite_path" => tracker_lookup_sprite_path(pokemon["species"])
          }
        end
      end
      result
    end
  end

  def self.tracker_area_trainer_party(trainer, recipe, generator)
    return [] if !trainer
    party = trainer.pokemon.each_with_index.map do |pokemon, slot|
      mapped = tracker_area_trainer_slot_species(
        trainer, pokemon[:species], slot, recipe, generator
      )
      {
        "species" => GameData::Species.get(mapped),
        "level" => scaled_level(pokemon[:level])
      }
    end
    return party if !gym_leader?(trainer) || party.length >= 6

    leader_level = party.map { |pokemon| pokemon["level"] }.max || 1
    trainer_name = trainer.name.to_s
    while party.length < 6
      slot = party.length
      source = gym_leader_source_species_for(
        recipe["seed"], trainer.trainer_type, trainer_name, slot
      )
      mapped = generator.map(
        source,
        [:gym_addition, trainer.trainer_type, trainer_name, slot]
      )
      party << {
        "species" => GameData::Species.get(mapped),
        "level" => leader_level
      }
    end
    return party
  end

  def self.tracker_area_trainer_slot_species(trainer, source, slot, recipe,
                                             generator)
    if !tracker_loaded_recipe?(recipe)
      return generator.map(source, [:pbs, trainer.id, slot])
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
