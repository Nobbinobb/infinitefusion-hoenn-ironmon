#===============================================================================
# Ironmon completed-run encounter and trainer occurrence lookup
#===============================================================================

module Ironmon
  def self.tracker_lookup_wild_occurrences(target, recipe)
    if recipe["species_generator_version"] ==
       SpeciesGenerator::LEGACY_SCHEMA_VERSION
      return tracker_lookup_legacy_wild_occurrences(target, recipe)
    end
    return [] if !SpeciesGenerator::SLOT_SCHEMA_VERSIONS.include?(
      recipe["species_generator_version"]
    )
    configuration_value = Configuration.from(recipe["configuration"])
    generator = if tracker_loaded_recipe?(recipe)
                  species_generator(:wild)
                 else
                  SpeciesGenerator.new(
                    recipe["seed"], :wild, configuration_value.wild_policy,
                    normal_species_pool, custom_fusion_pool, {},
                    recipe["species_generator_version"]
                  )
                end
    modes = if recipe["data_mode"] == "remix" &&
               defined?(GameData::EncounterModern)
              [GameData::EncounterModern]
            else
              [GameData::Encounter]
            end
    occurrences = []
    modes.each do |mode|
      mode.each do |data|
        data.types.each do |encounter_type, entries|
          total = entries.inject(0) { |sum, entry| sum + entry[0].to_i }
          entries.each_with_index do |entry, slot|
            context = [:table, mode.name, data.map, data.version,
                       encounter_type, slot]
            mapped = generator.map(entry[1], context)
            next if mapped != target.id
            source = GameData::Species.get(entry[1])
            chance = total > 0 ? (entry[0].to_f * 100.0 / total).round(2) : nil
            occurrences << {
              "map_id" => data.map,
              "route_name" => pbGetMapNameFromId(data.map),
              "mode" => mode == GameData::Encounter ? "Classic" : "Remix",
              "encounter_version" => data.version,
              "encounter_type" => encounter_type.to_s,
              "slot" => slot + 1,
              "minimum_level" => scaled_level(entry[2]),
              "maximum_level" => scaled_level(entry[3] || entry[2]),
              "source_species_id" => "#{source.id}:0",
              "source_species_name" => source.name,
              "chance_percent" => chance,
              "chance_is_conditional" => false
            }
          end
          if configuration_value.wild_policy ==
             Configuration::POLICY_NORMAL_ONLY
            tracker_lookup_wild_fusion_occurrences(
              occurrences, target, recipe, generator, mode, data,
              encounter_type, entries, total
            )
          end
        end
      end
    end
    return occurrences.sort_by do |entry|
      [entry["route_name"], entry["encounter_type"], entry["slot"]]
    end
  end

  def self.tracker_lookup_legacy_wild_occurrences(target, recipe)
    return [] if !tracker_loaded_recipe?(recipe)
    mapping = $PokemonGlobal.ironmon_wild_species_map
    return [] if !mapping.is_a?(Hash)
    mode = if recipe["data_mode"] == "remix" &&
              defined?(GameData::EncounterModern)
             GameData::EncounterModern
           else
             GameData::Encounter
           end
    occurrences = []
    mode.each do |data|
      data.types.each do |encounter_type, entries|
        total = entries.inject(0) { |sum, entry| sum + entry[0].to_i }
        entries.each_with_index do |entry, slot|
          source = GameData::Species.get(entry[1])
          next if mapping[source.id_number].to_i != target.id_number
          chance = total > 0 ? (entry[0].to_f * 100.0 / total).round(2) : nil
          occurrences << {
            "map_id" => data.map,
            "route_name" => pbGetMapNameFromId(data.map),
            "mode" => mode == GameData::Encounter ? "Classic" : "Remix",
            "encounter_version" => data.version,
            "encounter_type" => encounter_type.to_s,
            "slot" => slot + 1,
            "minimum_level" => scaled_level(entry[2]),
            "maximum_level" => scaled_level(entry[3] || entry[2]),
            "source_species_id" => "#{source.id}:0",
            "source_species_name" => source.name,
            "chance_percent" => chance,
            "chance_is_conditional" => false
          }
        end
      end
    end
    return occurrences.sort_by do |entry|
      [entry["route_name"], entry["encounter_type"], entry["slot"]]
    end
  end

  def self.tracker_lookup_wild_fusion_occurrences(
    occurrences, target, recipe, generator, mode, data, encounter_type,
    entries, total
  )
    return if target.id_number <= NB_POKEMON || total <= 0
    mapper = tracker_post_run_fusion_mapper(recipe)
    table = tracker_lookup_wild_fusion_table(
      recipe, generator, mapper, mode, data, encounter_type, entries
    )
    return if !table[:target_ids][target.id_number]
    mapped_entries = table[:mapped_entries]
    mapped_entries.each do |body_entry, body, body_slot|
      mapped_entries.each do |head_entry, head, head_slot|
        next if mapper.species_number(body, head) != target.id_number
        body_source = GameData::Species.get(body_entry[1])
        head_source = GameData::Species.get(head_entry[1])
        chance = body_entry[0].to_f * head_entry[0].to_f * 100.0 /
          (total * total)
        occurrences << {
          "map_id" => data.map,
          "route_name" => pbGetMapNameFromId(data.map),
          "mode" => mode == GameData::Encounter ? "Classic" : "Remix",
          "encounter_version" => data.version,
          "encounter_type" => encounter_type.to_s,
          "slot" => body_slot + 1,
          "secondary_slot" => head_slot + 1,
          "minimum_level" => scaled_level(body_entry[2]),
          "maximum_level" => scaled_level(body_entry[3] || body_entry[2]),
          "source_species_id" => "#{body_source.id}:0",
          "source_species_name" => body_source.name,
          "secondary_source_species_id" => "#{head_source.id}:0",
          "secondary_source_species_name" => head_source.name,
          "chance_percent" => chance.round(2),
          "chance_is_conditional" => true
        }
      end
    end
  rescue PlayerFusionMappingError => e
    echoln "Ironmon tracker skipped wild fusion occurrences: #{e.message}"
  end

  def self.tracker_lookup_wild_fusion_table(
    recipe, generator, mapper, mode, data, encounter_type, entries
  )
    key = [recipe["run_id"], mode.name, data.map, data.version,
           encounter_type]
    cached = tracker_wild_fusion_tables[key]
    return cached if cached
    mapped_entries = entries.each_with_index.map do |entry, slot|
      context = [:table, mode.name, data.map, data.version,
                 encounter_type, slot]
      [entry, generator.map(entry[1], context), slot]
    end
    target_ids = {}
    mapped_entries.each do |_body_entry, body, _body_slot|
      mapped_entries.each do |_head_entry, head, _head_slot|
        target_ids[mapper.species_number(body, head)] = true
      end
    end
    table = {
      :mapped_entries => mapped_entries.freeze,
      :target_ids => target_ids.freeze
    }.freeze
    tracker_wild_fusion_tables[key] = table
    return table
  end

  def self.tracker_lookup_trainer_occurrences(target, recipe)
    if recipe["species_generator_version"] ==
       SpeciesGenerator::LEGACY_SCHEMA_VERSION
      return tracker_lookup_legacy_trainer_occurrences(target, recipe)
    end
    return [] if !SpeciesGenerator::SLOT_SCHEMA_VERSIONS.include?(
      recipe["species_generator_version"]
    )
    configuration_value = Configuration.from(recipe["configuration"])
    generator = if tracker_loaded_recipe?(recipe)
                  species_generator(:trainer)
                 else
                  SpeciesGenerator.new(
                    recipe["seed"], :trainer,
                    configuration_value.trainer_policy,
                    normal_species_pool, custom_fusion_pool, {},
                    recipe["species_generator_version"]
                  )
                end
    occurrences = []
    tracker_trainer_data_mode(recipe).list_all.each do |_trainer_id, trainer|
      trainer.pokemon.each_with_index do |pokemon, slot|
        source = GameData::Species.get(pokemon[:species])
        mapped = generator.map(source.id, [:pbs, trainer.id, slot])
        next if mapped != target.id
        trainer_type = GameData::TrainerType.try_get(trainer.trainer_type)
        occurrence = {
          "trainer_id" => tracker_lookup_trainer_id(trainer),
          "trainer_name" => trainer.name,
          "trainer_type" => trainer_type ? trainer_type.name :
            trainer.trainer_type.to_s,
          "slot" => slot + 1,
          "level" => scaled_level(pokemon[:level]),
          "source_species_id" => "#{source.id}:0",
          "source_species_name" => source.name
        }
        tracker_append_trainer_locations(occurrences, occurrence, trainer)
      end
    end
    occurrences.uniq! do |entry|
      [entry["trainer_id"], entry["slot"], entry["source_species_id"],
       entry["map_id"]]
    end
    return occurrences.sort_by do |entry|
      [entry["trainer_type"], entry["trainer_name"], entry["slot"]]
    end
  end

  def self.tracker_lookup_legacy_trainer_occurrences(target, recipe)
    return [] if !tracker_loaded_recipe?(recipe)
    mapping = $PokemonGlobal.ironmon_trainer_species_map
    return [] if !mapping.is_a?(Hash)
    occurrences = []
    tracker_trainer_data_mode(recipe).list_all.each do |_trainer_id, trainer|
      trainer.pokemon.each_with_index do |pokemon, slot|
        source = GameData::Species.get(pokemon[:species])
        next if mapping[source.id_number].to_i != target.id_number
        trainer_type = GameData::TrainerType.try_get(trainer.trainer_type)
        occurrence = {
          "trainer_id" => tracker_lookup_trainer_id(trainer),
          "trainer_name" => trainer.name,
          "trainer_type" => trainer_type ? trainer_type.name :
            trainer.trainer_type.to_s,
          "slot" => slot + 1,
          "level" => scaled_level(pokemon[:level]),
          "source_species_id" => "#{source.id}:0",
          "source_species_name" => source.name
        }
        tracker_append_trainer_locations(occurrences, occurrence, trainer)
      end
    end
    occurrences.uniq! do |entry|
      [entry["trainer_id"], entry["slot"], entry["source_species_id"],
       entry["map_id"]]
    end
    return occurrences.sort_by do |entry|
      [entry["trainer_type"], entry["trainer_name"], entry["slot"]]
    end
  end

  def self.tracker_loaded_recipe?(recipe)
    return false if !$PokemonGlobal
    return recipe["active_run"] == true &&
      recipe["run_id"] == $PokemonGlobal.ironmon_run_id
  end

  def self.tracker_lookup_trainer_id(trainer)
    components = trainer.id.is_a?(Array) ? trainer.id : [trainer.id]
    return components.map { |component| component.to_s }.join(":")
  end

  def self.tracker_append_trainer_locations(occurrences, occurrence, trainer)
    locations = tracker_lookup_trainer_locations(trainer)
    if locations.empty?
      occurrences << occurrence
      return
    end
    locations.each do |location|
      occurrences << occurrence.merge(location)
    end
  end

  def self.tracker_lookup_trainer_locations(trainer)
    components = trainer.id.is_a?(Array) ? trainer.id : [trainer.id]
    key = "#{components[0]}\0#{components[1]}"
    return tracker_trainer_location_index[key] || []
  end

  def self.tracker_trainer_location_index
    return @tracker_trainer_location_index if @tracker_trainer_location_index
    index = {}
    pattern = /pbTrainerBattle\(\s*:([A-Za-z0-9_]+)\s*,\s*["']([^"']+)["']/
    Dir.glob(File.join("Data", "Map[0-9][0-9][0-9].rxdata")).each do |path|
      map_id = File.basename(path)[/\d+/].to_i
      File.open(path, "rb") do |file|
        file.read.scan(pattern).each do |trainer_type, trainer_name|
          route_name = pbGetMapNameFromId(map_id)
          next if route_name.to_s.match?(/\Aquest_/i)
          key = "#{trainer_type}\0#{trainer_name}"
          index[key] ||= {}
          index[key][map_id] = {
            "map_id" => map_id,
            "route_name" => route_name
          }
        end
      end
    end
    @tracker_trainer_location_index = {}
    index.each do |key, locations|
      @tracker_trainer_location_index[key] = locations.values
    end
    return @tracker_trainer_location_index
  rescue Exception => e
    echoln "Ironmon tracker could not index trainer locations: #{e.message}"
    @tracker_trainer_location_index = {}
    return @tracker_trainer_location_index
  end

  def self.tracker_data_mode
    return "remix" if $game_switches &&
      $game_switches[SWITCH_MODERN_MODE]
    return "expert" if $game_switches &&
      $game_switches[SWITCH_EXPERT_MODE]
    return "classic"
  end

  def self.tracker_trainer_data_mode(recipe)
    return GameData::TrainerModern if recipe["data_mode"] == "remix" &&
      defined?(GameData::TrainerModern)
    return GameData::TrainerExpert if recipe["data_mode"] == "expert" &&
      defined?(GameData::TrainerExpert)
    return GameData::Trainer
  end
end
