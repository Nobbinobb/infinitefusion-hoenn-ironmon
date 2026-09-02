#===============================================================================
# Ironmon tracker area encounter reconstruction
#===============================================================================

module Ironmon
  AREA_ENCOUNTER_ENVIRONMENT_ORDER = {
    "grass" => 0, "cave" => 1, "water" => 2,
    "fishing" => 3, "special" => 4
  }.freeze

  AREA_ENCOUNTER_CONDITION_ORDER = {
    "" => 0, "Morning" => 1, "Day" => 2, "Afternoon" => 3,
    "Evening" => 4, "Night" => 5, "Rain" => 6, "Sunny" => 7,
    "Wind" => 8, "Fog" => 9, "Storm" => 10, "Snow" => 11
  }.freeze

  def self.tracker_area_encounter_environment(encounter_type)
    return encounter_environment(encounter_type).to_s
  end

  def self.tracker_area_encounter_condition(encounter_type)
    value = encounter_type.to_s
    prefix = case tracker_area_encounter_environment(value)
             when "grass" then value == "TallGrass" ? value : "Land"
             when "cave" then "Cave"
             when "water" then "Water"
             else ""
             end
    return prefix.empty? ? value : value.sub(/\A#{prefix}/, "")
  end

  def self.tracker_area_encounter_sort_key(entry)
    environment = tracker_area_encounter_environment(entry["encounter_type"])
    condition = tracker_area_encounter_condition(entry["encounter_type"])
    return [
      AREA_ENCOUNTER_ENVIRONMENT_ORDER.fetch(environment, 99),
      AREA_ENCOUNTER_CONDITION_ORDER.fetch(condition, 50),
      entry["encounter_type"], -entry["probability_percent"],
      entry["slot"]
    ]
  end

  def self.tracker_area_encounter_mode(recipe)
    if recipe["data_mode"] == "remix" && defined?(GameData::EncounterModern)
      return GameData::EncounterModern
    end
    return GameData::Encounter
  end

  def self.tracker_area_encounter_catalog(recipe)
    mode = tracker_area_encounter_mode(recipe)
    @tracker_area_encounter_catalogs ||= {}
    return @tracker_area_encounter_catalogs[mode.name] if
      @tracker_area_encounter_catalogs[mode.name]
    catalog = Hash.new { |hash, key| hash[key] = [] }
    encounter_data = []
    mode.each { |data| encounter_data << data }
    if mode != GameData::Encounter
      GameData::Encounter.each do |data|
        encounter_data << data if !mode.get(data.map, data.version)
      end
    end
    encounter_data.each do |data|
      data.types.each do |encounter_type, entries|
        total = entries.inject(0) { |sum, entry| sum + entry[0].to_i }
        entries.each_with_index do |entry, slot|
          probability = total > 0 ? entry[0].to_f * 100.0 / total : 0.0
          catalog[data.map] << {
            "entry_id" => "encounter:#{data.map}:#{data.version}:#{encounter_type}:#{slot + 1}",
            "map_id" => data.map,
            "version" => data.version,
            "encounter_type" => encounter_type.to_s,
            "slot" => slot + 1,
            "context_slot" => slot,
            "weight" => entry[0].to_i,
            "source_species" => entry[1],
            "minimum_level" => entry[2].to_i,
            "maximum_level" => (entry[3] || entry[2]).to_i,
            "probability_percent" => probability.round(2),
            "mode_name" => mode.name
          }
        end
      end
    end
    @tracker_area_encounter_catalogs[mode.name] = catalog
    return catalog
  end

  def self.tracker_area_encounter_metadata(area, recipe)
    catalog = tracker_area_encounter_catalog(recipe)
    return area["map_ids"].flat_map { |map_id| catalog[map_id] }.
      sort_by { |entry| tracker_area_encounter_sort_key(entry) }
  end

  def self.tracker_area_species_generator(recipe, side)
    return species_generator(side) if tracker_loaded_recipe?(recipe)
    configuration_value = Configuration.from(recipe["configuration"])
    policy = side == :wild ? configuration_value.wild_policy :
      configuration_value.trainer_policy
    return SpeciesGenerator.new(
      recipe["seed"], side, policy, normal_species_pool,
      custom_fusion_pool, {}, recipe["species_generator_version"]
    )
  end

  def self.tracker_area_encounter_entries(area, recipe, discoveries,
                                          full_details, offset = 0,
                                          limit = nil)
    metadata = tracker_area_encounter_metadata(area, recipe)
    metadata = metadata.slice(offset, limit) || [] if limit
    return tracker_area_encounter_payloads(
      metadata, recipe, discoveries, full_details
    )
  end

  def self.tracker_area_encounter_payloads(metadata, recipe, discoveries,
                                           full_details)
    generator = nil
    return metadata.map do |entry|
      encountered = discoveries.key?(entry["entry_id"])
      revealed = full_details || encountered
      result = {
        "entry_id" => entry["entry_id"],
        "map_id" => entry["map_id"],
        "version" => entry["version"],
        "encounter_type" => entry["encounter_type"],
        "environment" => tracker_area_encounter_environment(
          entry["encounter_type"]
        ),
        "slot" => entry["slot"],
        "probability_percent" => entry["probability_percent"],
        "minimum_level" => scaled_level(entry["minimum_level"]),
        "maximum_level" => scaled_level(entry["maximum_level"]),
        "encountered" => encountered,
        "details_revealed" => revealed,
        "independent_fusion" => false
      }
      if revealed
        generator ||= tracker_area_species_generator(recipe, :wild)
        mapped = generator.map(
          entry["source_species"],
          [:table, entry["mode_name"], entry["map_id"], entry["version"],
           entry["encounter_type"].to_sym, entry["context_slot"]]
        )
        species = GameData::Species.get(mapped)
        result["species_id"] = "#{species.id}:0"
        result["species_name"] = species.name
        result["sprite_path"] = tracker_lookup_sprite_path(species)
        result["independent_fusion"] = species.id_number > NB_POKEMON
      end
      result
    end
  end

  def self.tracker_area_encounter_fusion_key(value)
    parts = value.to_s.split(":")
    return nil if parts.length != 9 || parts[0] != "encounter_fusion"
    origin = parts[2]
    return nil if !["standard_same", "standard_cross",
                    "overworld_same", "overworld_cross"].include?(origin)
    return {
      "entry_id" => value.to_s,
      "map_id" => parts[1].to_i,
      "origin" => origin,
      "first_version" => parts[3].to_i,
      "first_type" => parts[4],
      "first_slot" => parts[5].to_i,
      "second_version" => parts[6].to_i,
      "second_type" => parts[7],
      "second_slot" => parts[8].to_i
    }
  end

  def self.tracker_area_encounter_fusion_source(metadata, map_id, version,
                                                 encounter_type, slot)
    return metadata.find do |entry|
      entry["map_id"] == map_id && entry["version"] == version &&
        entry["encounter_type"] == encounter_type && entry["slot"] == slot
    end
  end

  def self.tracker_area_mapped_encounter_species(entry, generator)
    return generator.map(
      entry["source_species"],
      [:table, entry["mode_name"], entry["map_id"], entry["version"],
       entry["encounter_type"].to_sym, entry["context_slot"]]
    )
  end

  def self.tracker_area_encounter_fusion_chance(origin)
    return WILD_FUSION_STANDARD_SAME_CHANCE if origin == "standard_same"
    return WILD_FUSION_STANDARD_CROSS_CHANCE if origin == "standard_cross"
    return WILD_FUSION_OVERWORLD_CHANCE
  end

  def self.tracker_area_normal_encounter_sources(metadata, recipe)
    return [] if metadata.empty?
    generator = tracker_area_species_generator(recipe, :wild)
    return metadata.map do |entry|
      number = generator.map_number(
        entry["source_species"],
        [:table, entry["mode_name"], entry["map_id"], entry["version"],
         entry["encounter_type"].to_sym, entry["context_slot"]]
      )
      next if number <= 0 || number > NB_POKEMON
      species = GameData::Species.get(number).id
      { "metadata" => entry, "species" => species }
    end.compact
  end

  def self.tracker_area_derived_fusion_material_pair_codes(metadata, recipe)
    sources = tracker_area_normal_encounter_sources(metadata, recipe)
    return [] if sources.empty?
    tables = Hash.new { |hash, key| hash[key] = {} }
    sources.each do |source|
      entry = source["metadata"]
      number = GameData::Species.get(source["species"]).id_number
      table = [entry["map_id"], entry["version"], entry["encounter_type"]]
      tables[table][number] = true
    end
    pair_codes = {}
    tables.each_value do |numbers|
      numbers.keys.sort.combination(2) do |first, second|
        pair_codes[(first << 10) | second] = true
      end
    end
    tables_by_map = Hash.new { |hash, key| hash[key] = [] }
    tables.each do |table, numbers|
      tables_by_map[table[0]] << [table, numbers.keys.sort]
    end
    tables_by_map.each_value do |map_tables|
      map_tables.each_with_index do |first, first_index|
        ((first_index + 1)...map_tables.length).each do |second_index|
          second = map_tables[second_index]
          overworld_pair = overworld_encounter_environment?(first[0][2]) &&
            overworld_encounter_environment?(second[0][2])
          next if !overworld_pair
          first[1].each do |first_number|
            second[1].each do |second_number|
              next if first_number == second_number
              lower, higher = [first_number, second_number].sort
              pair_codes[(lower << 10) | higher] = true
            end
          end
        end
      end
    end
    return pair_codes.keys.sort
  end

  def self.tracker_area_same_encounter_table?(first, second)
    first = first["metadata"]
    second = second["metadata"]
    return first["map_id"] == second["map_id"] &&
      first["version"] == second["version"] &&
      first["encounter_type"] == second["encounter_type"]
  end

  def self.tracker_area_distinct_normal_sources?(first, second)
    return GameData::Species.get(first["species"]).id !=
      GameData::Species.get(second["species"]).id
  end

  def self.tracker_area_encounter_fusion_origins(first, second, cross,
                                                  overworld = nil)
    first_type = first["metadata"]["encounter_type"]
    second_type = second["metadata"]["encounter_type"]
    if cross
      return [] if !overworld_encounter_environment?(first_type) ||
                   !overworld_encounter_environment?(second_type)
      origins = overworld == false ? [] : ["overworld_cross"]
      origins.unshift("standard_cross") if
        overworld != true &&
        first["metadata"]["version"] == second["metadata"]["version"]
      return origins
    end
    origins = overworld == true ? [] : ["standard_same"]
    origins << "overworld_same" if
      overworld != false && overworld_encounter_environment?(first_type)
    return origins
  end

  def self.tracker_area_encounter_fusion_pair?(first, second, cross,
                                               environment = nil)
    return false if !tracker_area_distinct_normal_sources?(first, second)
    same = tracker_area_same_encounter_table?(first, second)
    return false if cross == same
    first_entry = first["metadata"]
    second_entry = second["metadata"]
    return false if cross && first_entry["map_id"] != second_entry["map_id"]
    if environment && tracker_area_encounter_environment(
      first_entry["encounter_type"]
    ) != environment
      return false
    end
    return !tracker_area_encounter_fusion_origins(
      first, second, cross
    ).empty?
  end

  def self.tracker_area_encounter_fusion_count(sources, cross,
                                               environment = nil,
                                               overworld = nil)
    count = 0
    sources.each do |first|
      sources.each do |second|
        next if !tracker_area_encounter_fusion_pair?(
          first, second, cross, environment
        )
        count += tracker_area_encounter_fusion_origins(
          first, second, cross, overworld
        ).length
      end
    end
    return count
  end

  def self.tracker_area_encounter_total(metadata, recipe)
    sources = tracker_area_normal_encounter_sources(metadata, recipe)
    overworld = recipe["overworld_encounters"]
    return metadata.length +
      tracker_area_encounter_fusion_count(sources, false, nil, overworld) +
      tracker_area_encounter_fusion_count(sources, true, nil, overworld)
  end

  def self.tracker_area_encounter_fusion_descriptors(sources, cross, offset,
                                                     limit,
                                                     environment = nil,
                                                     overworld = nil)
    skipped = 0
    descriptors = []
    sources.each do |first|
      sources.each do |second|
        next if !tracker_area_encounter_fusion_pair?(
          first, second, cross, environment
        )
        tracker_area_encounter_fusion_origins(
          first, second, cross, overworld
        ).each do |origin|
          if skipped < offset
            skipped += 1
            next
          end
          descriptors << {
            "first" => first, "second" => second, "origin" => origin
          }
          return descriptors if descriptors.length >= limit
        end
      end
    end
    return descriptors
  end

  def self.tracker_area_encounter_fusion_entry_id(first, second, origin)
    first = first["metadata"]
    second = second["metadata"]
    return [
      "encounter_fusion", first["map_id"], origin,
      first["version"], first["encounter_type"], first["slot"],
      second["version"], second["encounter_type"], second["slot"]
    ].join(":")
  end

  def self.tracker_area_encounter_fusion_materials(descriptors)
    return descriptors.map do |descriptor|
      first = GameData::Species.get(descriptor["first"]["species"]).id_number
      second = GameData::Species.get(descriptor["second"]["species"]).id_number
      descriptor["origin"].start_with?("overworld") ?
        [second, first] : [first, second]
    end
  end

  def self.tracker_area_encounter_fusion_entries(descriptors, recipe,
                                                 discoveries, native = nil,
                                                 full_details = true)
    return [] if descriptors.empty?
    revealed = tracker_area_revealed_fusion_descriptors(
      descriptors, discoveries, full_details
    )
    results = []
    if !revealed.empty?
      pairs = tracker_area_encounter_fusion_materials(revealed)
      work = tracker_area_fusion_work(recipe)
      results = native ? work.native_results_for(pairs, native) :
        work.results_for(pairs)
      return nil if !results
    end
    result_index = 0
    return descriptors.map do |descriptor|
      row = tracker_area_encounter_fusion_metadata(descriptor, discoveries)
      row["details_revealed"] = tracker_area_encounter_fusion_revealed?(
        descriptor, discoveries, full_details
      )
      if row["details_revealed"]
        row.merge!(results[result_index])
        result_index += 1
      end
      row
    end
  end

  def self.tracker_area_encounter_fusion_revealed?(descriptor, discoveries,
                                                    full_details)
    return true if full_details
    first = descriptor["first"]
    second = descriptor["second"]
    key = tracker_area_encounter_fusion_entry_id(
      first, second, descriptor["origin"]
    )
    return discoveries.key?(key) ||
      (discoveries.key?(first["metadata"]["entry_id"]) &&
       discoveries.key?(second["metadata"]["entry_id"]))
  end

  def self.tracker_area_revealed_fusion_descriptors(descriptors, discoveries,
                                                    full_details)
    return descriptors.select do |descriptor|
      tracker_area_encounter_fusion_revealed?(
        descriptor, discoveries, full_details
      )
    end
  end

  def self.tracker_area_encounter_fusion_metadata(descriptor, discoveries)
    first_source = descriptor["first"]
    second_source = descriptor["second"]
    first = first_source["metadata"]
    second = second_source["metadata"]
    origin = descriptor["origin"]
    key = tracker_area_encounter_fusion_entry_id(
      first_source, second_source, origin
    )
    cross = origin.end_with?("cross")
    minimum_level = if origin.start_with?("overworld")
                      (first["minimum_level"] +
                        second["minimum_level"]) / 2
                    else
                      first["minimum_level"]
                      end
      maximum_level = if origin.start_with?("overworld")
                        (first["maximum_level"] +
                          second["maximum_level"]) / 2
                      else
                        first["maximum_level"]
                      end
      {
        "entry_id" => key,
        "origin" => origin,
        "environment" => tracker_area_encounter_environment(
          first["encounter_type"]
        ),
        "cross_environment" => cross,
        "encountered" => discoveries.key?(key),
        "first_encounter_type" => first["encounter_type"],
        "first_slot" => first["slot"],
        "second_encounter_type" => second["encounter_type"],
        "second_slot" => second["slot"],
        "fusion_chance_percent" => tracker_area_encounter_fusion_chance(
          origin
        ),
        "minimum_level" => scaled_level(minimum_level),
        "maximum_level" => scaled_level(maximum_level)
      }
  end

  def self.tracker_area_encounter_environment_index(metadata, overworld = nil)
    environments = AREA_ENCOUNTER_ENVIRONMENT_ORDER.keys.select do |environment|
      metadata.any? do |entry|
        tracker_area_encounter_environment(entry["encounter_type"]) ==
          environment
      end
    end.map { |environment| { "key" => environment } }
    tables_by_map = Hash.new { |hash, key| hash[key] = [] }
    metadata.each do |entry|
      next if !overworld_encounter_environment?(entry["encounter_type"])
      group = overworld == false ? [entry["map_id"], entry["version"]] : entry["map_id"]
      tables_by_map[group] << [
        entry["version"], entry["encounter_type"]
      ]
    end
    has_cross_tables = tables_by_map.values.any? do |tables|
      tables.uniq.length > 1
    end
    environments << { "key" => "cross" } if has_cross_tables
    return environments
  end
end
