#===============================================================================
# Ironmon tracker area encounter reconstruction
#===============================================================================

module Ironmon
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
    return area["map_ids"].flat_map { |map_id| catalog[map_id] }
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
    generator = nil
    metadata = tracker_area_encounter_metadata(area, recipe)
    metadata = metadata.slice(offset, limit) || [] if limit
    return metadata.map do |entry|
      encountered = discoveries.key?(entry["entry_id"])
      revealed = full_details || encountered
      result = {
        "entry_id" => entry["entry_id"],
        "map_id" => entry["map_id"],
        "version" => entry["version"],
        "encounter_type" => entry["encounter_type"],
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
end
