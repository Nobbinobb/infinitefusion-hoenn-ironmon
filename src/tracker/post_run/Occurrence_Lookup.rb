#===============================================================================
# Ironmon completed-run encounter and trainer occurrence lookup
#===============================================================================

module Ironmon
  class TrackerWildOccurrenceWork
    WORK_SECONDS = 0.004

    def initialize(target, recipe, materials)
      @results = nil
      @error = nil
      @fiber = Fiber.new do
        checkpoint = proc do
          Fiber.yield if Ironmon.tracker_uptime_seconds >= @deadline
        end
        @results = Ironmon.tracker_lookup_wild_occurrences(
          target, recipe, materials, checkpoint
        )
      end
    end

    def advance
      return if !@fiber || @error
      @deadline = Ironmon.tracker_uptime_seconds + WORK_SECONDS
      @fiber.resume
      @fiber = nil if !@fiber.alive?
    rescue StandardError => error
      @error = error
      @fiber = nil
      echoln "Ironmon wild location preparation failed: #{error.message}"
    end

    def results
      advance
      if @error
        raise TrackerLookupError.new(
          "wild_lookup_failed",
          "Unable to prepare encounter locations: #{@error.message}"
        )
      end
      return @results
    end
  end

  def self.tracker_occurrence_cache_key(recipe, species, kind)
    return [
      recipe["run_id"], recipe["seed"], recipe["active_run"],
      recipe["data_mode"], recipe["configuration"],
      recipe["species_generator_version"],
      recipe["player_fusion_generator_version"],
      recipe["base_stat_source_fingerprint"],
      recipe["overworld_encounters"], species.id, kind
    ]
  end

  def self.tracker_wild_occurrence_work(key, target, recipe, encoded)
    @tracker_wild_occurrence_work ||= {}
    return @tracker_wild_occurrence_work[key] if
      @tracker_wild_occurrence_work.key?(key)
    work = TrackerWildOccurrenceWork.new(
      target, recipe, tracker_decode_occurrence_materials(encoded)
    )
    tracker_store_bounded(@tracker_wild_occurrence_work, key, work, 4)
    return work
  end

  def self.tracker_lookup_wild_occurrences(target, recipe, materials = nil,
                                           checkpoint = nil)
    if recipe["species_generator_version"] ==
       SpeciesGenerator::LEGACY_SCHEMA_VERSION
      return tracker_lookup_legacy_wild_occurrences(target, recipe)
    end
    return [] if !SpeciesGenerator::SLOT_SCHEMA_VERSIONS.include?(
      recipe["species_generator_version"]
    )
    occurrences = []
    sources = []
    tracker_wild_occurrence_sources(recipe, checkpoint).each do |entry, mapped|
      checkpoint.call if checkpoint
      if mapped > 0 && mapped <= NB_POKEMON
        sources << { "metadata" => entry, "species" => GameData::Species.get(mapped).id }
      end
      next if mapped != target.id_number
      source = GameData::Species.get(entry["source_species"])
      occurrences << {
        "entry_id" => entry["entry_id"],
        "map_id" => entry["map_id"],
        "route_name" => pbGetMapNameFromId(entry["map_id"]),
        "mode" => tracker_area_encounter_mode(recipe) == GameData::Encounter ? "Classic" : "Remix",
        "encounter_version" => entry["version"],
        "encounter_type" => entry["encounter_type"],
        "slot" => entry["slot"],
        "minimum_level" => scaled_level(entry["minimum_level"]),
        "maximum_level" => scaled_level(entry["maximum_level"]),
        "source_species_id" => "#{source.id}:0",
        "source_species_name" => source.name,
        "chance_percent" => entry["probability_percent"],
        "chance_is_conditional" => false
      }
    end
    if target.id_number > NB_POKEMON
      tracker_append_derived_wild_occurrences(
        occurrences, target, recipe, sources, materials, checkpoint
      )
    end
    return occurrences.sort_by do |entry|
      [entry["route_name"], entry["encounter_type"], entry["slot"],
       entry["entry_id"].to_s]
    end
  end

  def self.tracker_wild_occurrence_sources(recipe, checkpoint)
    @tracker_wild_occurrence_sources ||= {}
    key = [recipe["run_id"], recipe["seed"], recipe["active_run"],
           recipe["data_mode"], recipe["configuration"],
           recipe["species_generator_version"]]
    return @tracker_wild_occurrence_sources[key] if
      @tracker_wild_occurrence_sources.key?(key)
    generator = tracker_area_species_generator(recipe, :wild)
    entries = tracker_area_encounter_catalog(recipe).values.flatten(1)
    sources = entries.map do |entry|
      checkpoint.call if checkpoint
      mapped = generator.map_number(entry["source_species"], [
        :table, entry["mode_name"], entry["map_id"], entry["version"],
        entry["encounter_type"].to_sym, entry["context_slot"]
      ])
      [entry, mapped]
    end
    tracker_store_bounded(@tracker_wild_occurrence_sources, key, sources, 2)
    return sources
  end

  def self.tracker_decode_occurrence_materials(encoded)
    return nil if encoded.nil?
    expected = (NB_POKEMON * NB_POKEMON + 7) / 8
    if !encoded.is_a?(String) || encoded.length > ((expected + 2) / 3) * 4
      raise TrackerLookupError.new("invalid_query", "The fusion material membership is malformed.")
    end
    bytes = encoded.unpack("m0")[0]
    if bytes.bytesize != expected
      raise TrackerLookupError.new("invalid_query", "The fusion material membership has an invalid size.")
    end
    pairs = []
    bytes.each_byte.with_index do |value, index|
      next if value == 0
      8.times do |bit|
        next if (value & (1 << bit)) == 0
        position = index * 8 + bit
        next if position >= NB_POKEMON * NB_POKEMON
        pairs << [position / NB_POKEMON + 1, position % NB_POKEMON + 1]
      end
    end
    return pairs
  rescue ArgumentError
    raise TrackerLookupError.new("invalid_query", "The fusion material membership is malformed.")
  end

  def self.tracker_append_derived_wild_occurrences(occurrences, target, recipe,
                                                   sources, materials,
                                                   checkpoint)
    by_species = sources.group_by { |source| GameData::Species.get(source["species"]).id_number }
    if materials.nil?
      mapper = nil
      materials = []
      sources.group_by { |source| source["metadata"]["map_id"] }.each_value do |map_sources|
        ids = map_sources.map { |source| GameData::Species.get(source["species"]).id_number }.uniq
        ids.each do |body|
          ids.each do |head|
            checkpoint.call if checkpoint
            next if body == head
            mapper ||= tracker_obtainability_fusion_mapper(recipe, checkpoint)
            materials << [body, head] if mapper.species_number(body, head) == target.id_number
          end
        end
      end
      materials.uniq!
    end
    materials.each do |body, head|
      next if body == head
      (by_species[body] || []).each do |body_source|
        (by_species[head] || []).each do |head_source|
          checkpoint.call if checkpoint
          [[body_source, head_source, false], [head_source, body_source, true]].each do |first, second, overworld|
            next if !recipe["overworld_encounters"].nil? && recipe["overworld_encounters"] != overworld
            cross = !tracker_area_same_encounter_table?(first, second)
            next if !tracker_area_encounter_fusion_pair?(first, second, cross)
            tracker_area_encounter_fusion_origins(first, second, cross, overworld).each do |origin|
              descriptor = { "first" => first, "second" => second, "origin" => origin }
              occurrences << tracker_derived_wild_occurrence(descriptor, recipe)
            end
          end
        end
      end
    end
  end

  def self.tracker_derived_wild_occurrence(descriptor, recipe)
    metadata = tracker_area_encounter_fusion_metadata(descriptor, {})
    first = descriptor["first"]["metadata"]
    second = descriptor["second"]["metadata"]
    first_species = GameData::Species.get(descriptor["first"]["species"])
    second_species = GameData::Species.get(descriptor["second"]["species"])
    return {
      "entry_id" => metadata["entry_id"],
      "map_id" => first["map_id"],
      "route_name" => pbGetMapNameFromId(first["map_id"]),
      "mode" => tracker_area_encounter_mode(recipe) == GameData::Encounter ? "Classic" : "Remix",
      "encounter_version" => first["version"],
      "encounter_type" => first["encounter_type"],
      "slot" => first["slot"],
      "secondary_encounter_type" => second["encounter_type"],
      "secondary_encounter_version" => second["version"],
      "secondary_slot" => second["slot"],
      "minimum_level" => metadata["minimum_level"],
      "maximum_level" => metadata["maximum_level"],
      "source_species_id" => "#{first_species.id}:0",
      "source_species_name" => first_species.name,
      "secondary_source_species_id" => "#{second_species.id}:0",
      "secondary_source_species_name" => second_species.name,
      "origin" => metadata["origin"],
      "fusion_chance_percent" => metadata["fusion_chance_percent"],
      "cross_environment" => metadata["cross_environment"]
    }
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
