module IronmonGenerationBaseCatalogExporter
  SCHEMA_VERSION = 1
  OUTPUT_PATH = $ironmon_generation_base_catalog_output_path.to_s
  DESCRIPTOR_PATH = $ironmon_generation_base_catalog_descriptor_path.to_s
  COMPONENT_NAME = "base_catalog"

  def self.identity(value)
    return value ? value.to_s : nil
  end

  def self.records(data_class)
    result = []
    data_class.each { |entry| result << yield(entry) }
    return result.sort_by do |entry|
      [entry["id_number"].to_i, entry["id"].to_s]
    end
  end

  def self.types
    return records(GameData::Type) do |type|
      {
        "id" => identity(type.id),
        "id_number" => type.id_number,
        "name" => type.real_name,
        "special" => type.special_type,
        "pseudo" => type.pseudo_type,
        "weaknesses" => type.weaknesses,
        "resistances" => type.resistances,
        "immunities" => type.immunities
      }
    end
  end

  def self.abilities
    return records(GameData::Ability) do |ability|
      {
        "id" => identity(ability.id),
        "id_number" => ability.id_number,
        "name" => ability.real_name,
        "description" => ability.real_description
      }
    end
  end

  def self.moves
    return records(GameData::Move) do |move|
      {
        "id" => identity(move.id),
        "id_number" => move.id_number,
        "name" => move.real_name,
        "function_code" => move.function_code,
        "base_damage" => move.base_damage,
        "type" => identity(move.type),
        "category" => move.category,
        "accuracy" => move.accuracy,
        "total_pp" => move.total_pp,
        "effect_chance" => move.effect_chance,
        "target" => identity(move.target),
        "priority" => move.priority,
        "flags" => move.flags,
        "description" => move.real_description
      }
    end
  end

  def self.items
    return records(GameData::Item) do |item|
      {
        "id" => identity(item.id),
        "id_number" => item.id_number,
        "name" => item.real_name,
        "plural_name" => item.real_name_plural,
        "pocket" => item.pocket,
        "price" => item.price,
        "description" => item.real_description,
        "field_use" => item.field_use,
        "battle_use" => item.battle_use,
        "type" => item.type,
        "move" => identity(item.move)
      }
    end
  end

  def self.species
    return (1..NB_POKEMON).map do |id_number|
      species = GameData::Species.get(id_number)
      {
        "id" => identity(species.id),
        "id_number" => species.id_number,
        "species" => identity(species.species),
        "form" => species.form,
        "name" => species.real_name,
        "form_name" => species.real_form_name,
        "category" => species.real_category,
        "pokedex_entry" => species.real_pokedex_entry,
        "pokedex_form" => species.pokedex_form,
        "type1" => identity(species.type1),
        "type2" => identity(species.type2),
        "base_stats" => species.base_stats,
        "evs" => species.evs,
        "base_exp" => species.base_exp,
        "growth_rate" => identity(species.growth_rate),
        "gender_ratio" => identity(species.gender_ratio),
        "catch_rate" => species.catch_rate,
        "happiness" => species.happiness,
        "moves" => species.moves,
        "tutor_moves" => species.tutor_moves,
        "egg_moves" => species.egg_moves,
        "abilities" => species.abilities,
        "hidden_abilities" => species.hidden_abilities,
        "wild_items" => [
          species.wild_item_common,
          species.wild_item_uncommon,
          species.wild_item_rare
        ],
        "egg_groups" => species.egg_groups,
        "hatch_steps" => species.hatch_steps,
        "incense" => identity(species.incense),
        "evolutions" => species.evolutions,
        "height" => species.height,
        "weight" => species.weight,
        "color" => identity(species.color),
        "shape" => identity(species.shape),
        "habitat" => identity(species.habitat),
        "generation" => species.generation,
        "mega_stone" => identity(species.mega_stone),
        "mega_move" => identity(species.mega_move),
        "unmega_form" => species.unmega_form,
        "mega_message" => species.mega_message
      }
    end
  end

  def self.evolution_methods
    return records(GameData::Evolution) do |method|
      {
        "id" => identity(method.id),
        "id_number" => 0,
        "name" => method.real_name,
        "parameter_type" => method.parameter ? method.parameter.to_s : nil,
        "minimum_level" => method.minimum_level,
        "level_up" => !method.level_up_proc.nil?,
        "use_item" => !method.use_item_proc.nil?,
        "trade" => !method.on_trade_proc.nil?,
        "after_evolution" => !method.after_evolution_proc.nil?
      }
    end
  end

  def self.fusion_name_parts
    return (1..NB_POKEMON).map do |id_number|
      dex_number = GameData::NAT_DEX_MAPPING[id_number] || id_number
      parts = GameData::SPLIT_NAMES[dex_number]
      {
        "id_number" => id_number,
        "prefix" => parts ? parts[0] : nil,
        "suffix" => parts ? parts[1] : nil
      }
    end
  end

  def self.trainer_types
    return records(GameData::TrainerType) do |type|
      {
        "id" => identity(type.id),
        "id_number" => type.id_number,
        "name" => type.real_name,
        "base_money" => type.base_money,
        "battle_bgm" => type.battle_BGM,
        "victory_me" => type.victory_ME,
        "intro_me" => type.intro_ME,
        "gender" => type.gender,
        "skill_level" => type.skill_level,
        "skill_code" => type.skill_code
      }
    end
  end

  def self.trainers(data_class)
    return records(data_class) do |trainer|
      {
        "id" => trainer.id,
        "id_number" => trainer.id_number,
        "trainer_type" => identity(trainer.trainer_type),
        "name" => trainer.real_name,
        "version" => trainer.version,
        "items" => trainer.items,
        "pokemon" => trainer.pokemon
      }
    end
  end

  def self.encounters(data_class)
    result = []
    data_class.each do |encounter|
      result << {
        "id" => identity(encounter.id),
        "map" => encounter.map,
        "version" => encounter.version,
        "step_chances" => encounter.step_chances,
        "types" => encounter.types
      }
    end
    return result.sort_by { |entry| [entry["map"], entry["version"]] }
  end

  def self.optional_catalog(class_name)
    data_class = GameData.const_get(class_name) if GameData.const_defined?(class_name)
    return [] if !data_class
    return yield(data_class)
  end

  def self.document
    species_records = species
    trainer_catalogs = {
      "classic" => trainers(GameData::Trainer),
      "remix" => optional_catalog(:TrainerModern) { |klass| trainers(klass) },
      "expert" => optional_catalog(:TrainerExpert) { |klass| trainers(klass) }
    }
    encounter_catalogs = {
      "classic" => encounters(GameData::Encounter),
      "remix" => optional_catalog(:EncounterModern) { |klass| encounters(klass) },
      "randomized" => optional_catalog(:EncounterRandom) { |klass| encounters(klass) }
    }
    return {
      "schema_version" => SCHEMA_VERSION,
      "game_version" => Settings::GAME_VERSION_NUMBER.to_s,
      "scope" => "immutable_game_data_without_custom_fusion_pool",
      "normal_species_count" => NB_POKEMON,
      "types" => types,
      "abilities" => abilities,
      "moves" => moves,
      "items" => items,
      "species" => species_records,
      "fusion_name_parts" => fusion_name_parts,
      "evolution_methods" => evolution_methods,
      "trainer_types" => trainer_types,
      "trainers" => trainer_catalogs,
      "encounters" => encounter_catalogs,
      "audit" => {
        "species_are_normal_only" => species_records.all? do |entry|
          entry["id_number"] >= 1 && entry["id_number"] <= NB_POKEMON
        end,
        "species_ids_are_contiguous" => species_records.map do |entry|
          entry["id_number"]
        end == (1..NB_POKEMON).to_a,
        "trainer_counts" => trainer_catalogs.transform_values(&:length),
        "encounter_counts" => encounter_catalogs.transform_values(&:length)
      }
    }
  end

  def self.run
    output = Ironmon::GenerationProfile.canonical_json(document) + "\n"
    temporary_path = "#{OUTPUT_PATH}.tmp"
    File.binwrite(temporary_path, output)
    File.delete(OUTPUT_PATH) if File.file?(OUTPUT_PATH)
    File.rename(temporary_path, OUTPUT_PATH)
    descriptor = Ironmon::GenerationProfile.component(
      COMPONENT_NAME, SCHEMA_VERSION, OUTPUT_PATH
    )
    File.binwrite(
      DESCRIPTOR_PATH,
      Ironmon::GenerationProfile.canonical_json(descriptor) + "\n"
    )
  end
end

IronmonGenerationBaseCatalogExporter.run
