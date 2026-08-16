#===============================================================================
# Ironmon public area lookup
#===============================================================================

module Ironmon
  AREA_CATALOG_PATH = File.join("Data", "Ironmon", "area_catalog.dat")

  class AreaCatalogError < StandardError
  end

  def self.tracker_area_catalog_document
    return @tracker_area_catalog_document if @tracker_area_catalog_document
    document = File.open(AREA_CATALOG_PATH, "rb") do |file|
      Marshal.load(file)
    end
    tracker_validate_area_catalog(document)
    @tracker_area_catalog_document = document
    return @tracker_area_catalog_document
  rescue Exception => e
    raise AreaCatalogError,
      "The bundled Ironmon area catalog is unavailable: #{e.message}"
  end

  def self.tracker_area_catalog
    return tracker_area_catalog_document["areas"]
  end

  def self.tracker_area_event_entry_id(category, map_id, event_id)
    @tracker_area_event_entry_indexes ||= {}
    index = @tracker_area_event_entry_indexes[category]
    if !index
      index = {}
      tracker_area_catalog.each do |area|
        entries = area[category] || []
        entries.each do |entry|
          key = [entry["map_id"].to_i, entry["event_id"].to_i]
          index[key] = entry["entry_id"]
        end
      end
      @tracker_area_event_entry_indexes[category] = index
    end
    return index[[map_id.to_i, event_id.to_i]]
  end

  def self.tracker_area_hidden_items(map_id)
    @tracker_area_hidden_item_indexes ||= {}
    return @tracker_area_hidden_item_indexes[map_id.to_i] if
      @tracker_area_hidden_item_indexes.key?(map_id.to_i)
    entries = []
    tracker_area_catalog.each do |area|
      next if !area["map_ids"].include?(map_id.to_i)
      entries = area["items"].select do |entry|
        entry["map_id"].to_i == map_id.to_i && entry["hidden"] == true
      end
      break
    end
    @tracker_area_hidden_item_indexes[map_id.to_i] = entries
    return entries
  end

  def self.tracker_validate_area_catalog(document)
    if !document.is_a?(Hash) || document["schema_version"] != 1 ||
       !document["areas"].is_a?(Array)
      raise "unsupported area catalog schema"
    end
    area_ids = {}
    entry_ids = {}
    document["areas"].each do |area|
      if !area.is_a?(Hash) || area["area_id"].to_s.empty? ||
         area["name"].to_s.empty? || !area["map_ids"].is_a?(Array) ||
         !area["trainers"].is_a?(Array) || !area["items"].is_a?(Array)
        raise "an area catalog entry is malformed"
      end
      raise "duplicate area identifier" if area_ids[area["area_id"]]
      area_ids[area["area_id"]] = true
      area["map_ids"].each do |map_id|
        raise "invalid area map identifier" if map_id.to_i < 1
      end
      (area["trainers"] + area["items"]).each do |entry|
        entry_id = entry.is_a?(Hash) ? entry["entry_id"].to_s : ""
        raise "an area content entry is malformed" if entry_id.empty?
        raise "duplicate area content identifier" if entry_ids[entry_id]
        entry_ids[entry_id] = true
      end
    end
    return true
  end

  def self.tracker_area_lookup_summary(payload, envelope_run_id)
    recipe, archived = tracker_area_lookup_context(payload, envelope_run_id)
    category = (payload || {})["category"].to_s
    if !AREA_DISCOVERY_CATEGORIES.include?(category)
      raise TrackerLookupError.new(
        "invalid_area_category", "The selected area category is invalid."
      )
    end
    areas = tracker_area_catalog.map do |area|
      encounter_entries = tracker_area_encounter_metadata(area, recipe)
      trainer_defeated = if category == "trainer" && !archived
                           area["trainers"].count do |entry|
                             tracker_area_event_completed?(entry)
                           end
                         else
                           0
                         end
      items_collected = if category == "item" && !archived
                          area["items"].count do |entry|
                            tracker_area_event_completed?(entry)
                          end
                        else
                          0
                        end
      {
        "area_id" => area["area_id"],
        "name" => area["name"],
        "map_ids" => area["map_ids"],
        "trainer_total" => category == "trainer" ?
          area["trainers"].length : 0,
        "trainer_defeated" => trainer_defeated,
        "encounter_total" => category == "encounter" ?
          encounter_entries.length : 0,
        "encountered" => 0,
        "item_total" => category == "item" ? area["items"].length : 0,
        "items_collected" => items_collected
      }
    end
    return {
      "schema_version" => tracker_area_catalog_document["schema_version"],
      "revision" => 0,
      "areas" => areas
    }
  end

  def self.tracker_area_lookup_detail(payload, envelope_run_id, debug)
    payload ||= {}
    recipe, archived = tracker_area_lookup_context(payload, envelope_run_id)
    area_id = payload["area_id"].to_s
    area = tracker_area_catalog.find do |candidate|
      candidate["area_id"] == area_id
    end
    if !area
      raise TrackerLookupError.new("area_not_found", "The selected area is unavailable.")
    end
    category = payload["category"].to_s
    discovery_keys = tracker_validate_area_discovery_keys(
      area, category, recipe, payload["discovery_keys"]
    )
    full_details = debug || archived
    result = {
      "area_id" => area["area_id"],
      "name" => area["name"],
      "category" => category,
      "revision" => 0,
      "trainers" => [],
      "encounters" => [],
      "items" => []
    }
    case category
    when "trainer"
      result["trainers"] = tracker_area_trainer_entries(
        area, recipe, discovery_keys, full_details, archived
      )
    when "encounter"
      result["encounters"] = tracker_area_encounter_entries(
        area, recipe, discovery_keys, full_details
      )
    when "item"
      result["items"] = tracker_area_item_entries(
        area, recipe, discovery_keys, full_details, archived
      )
    else
      raise TrackerLookupError.new(
        "invalid_area_category", "The selected area category is invalid."
      )
    end
    return result
  end

  def self.tracker_area_lookup_context(payload, envelope_run_id)
    payload ||= {}
    archived = payload["recipe"]
    if archived.is_a?(Hash)
      recipe = tracker_validate_completed_recipe(archived, envelope_run_id)
      return [recipe, true]
    end
    attempt = current_run_attempt
    if !attempt || attempt["run_id"].to_s.empty?
      raise TrackerLookupError.new("run_unavailable", "No Ironmon run is available.")
    end
    if envelope_run_id && envelope_run_id != attempt["run_id"]
      raise TrackerLookupError.new(
        "run_mismatch", "The request and active run identify different runs."
      )
    end
    return [tracker_active_area_recipe(attempt), false]
  end

  def self.tracker_active_area_recipe(attempt)
    return {
      "active_run" => true,
      "run_id" => attempt["run_id"],
      "seed" => attempt["seed"],
      "result" => attempt["result"],
      "configuration" => configuration_snapshot,
      "data_mode" => tracker_data_mode,
      "item_generator" => item_generator_recipe,
      "item_mappings" => tracker_item_mapping_recipe(
        $PokemonGlobal.randomItemsHash
      ),
      "tm_mappings" => tracker_item_mapping_recipe(
        $PokemonGlobal.randomTMsHash
      ),
      "species_generator_version" =>
        $PokemonGlobal.ironmon_species_generator_version
    }
  end

  def self.tracker_validate_area_discovery_keys(area, category, recipe, values)
    keys = values.is_a?(Array) ? values.map { |value| value.to_s }.uniq : []
    valid = case category
            when "trainer"
              area["trainers"].map { |entry| entry["entry_id"] }
            when "encounter"
              tracker_area_encounter_metadata(area, recipe).
                map { |entry| entry["entry_id"] }
            when "item"
              area["items"].map { |entry| entry["entry_id"] }
            else
              raise TrackerLookupError.new(
                "invalid_area_category",
                "The selected area category is invalid."
              )
            end
    invalid = keys - valid
    if !invalid.empty?
      raise TrackerLookupError.new(
        "invalid_area_discovery",
        "A discovery key does not belong to the requested area and category."
      )
    end
    return keys.each_with_object({}) { |key, result| result[key] = true }
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
                                          full_details)
    generator = tracker_area_species_generator(recipe, :wild)
    return tracker_area_encounter_metadata(area, recipe).map do |entry|
      encountered = discoveries.key?(entry["entry_id"])
      revealed = full_details || encountered
      mapped = generator.map(
        entry["source_species"],
        [:table, entry["mode_name"], entry["map_id"], entry["version"],
         entry["encounter_type"].to_sym, entry["context_slot"]]
      )
      species = GameData::Species.get(mapped)
      result = {
        "entry_id" => entry["entry_id"],
        "map_id" => entry["map_id"],
        "version" => entry["version"],
        "encounter_type" => entry["encounter_type"],
        "slot" => entry["slot"],
        "probability_percent" => entry["probability_percent"],
        "minimum_level" => entry["minimum_level"],
        "maximum_level" => entry["maximum_level"],
        "encountered" => encountered,
        "details_revealed" => revealed,
        "independent_fusion" => species.id_number > NB_POKEMON
      }
      if revealed
        result["species_id"] = "#{species.id}:0"
        result["species_name"] = species.name
        result["sprite_path"] = tracker_lookup_sprite_path(species)
      end
      result
    end
  end

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

  def self.tracker_area_item_entries(area, recipe, discoveries, full_details,
                                     archived)
    return area["items"].map do |entry|
      collected = discoveries.key?(entry["entry_id"]) ||
        (!archived && tracker_area_event_completed?(entry))
      revealed = full_details || collected
      item_ids = revealed ? tracker_area_resolved_item_ids(entry, recipe) : []
      {
        "entry_id" => entry["entry_id"],
        "map_id" => entry["map_id"],
        "x" => entry["x"],
        "y" => entry["y"],
        "kind" => entry["kind"],
        "hidden" => entry["hidden"],
        "collected" => collected,
        "details_revealed" => revealed,
        "items" => item_ids.map do |item_id|
          item = GameData::Item.try_get(item_id.to_sym)
          {
            "item_id" => item_id,
            "item_name" => item ? item.name : item_id
          }
        end
      }
    end
  end

  def self.tracker_area_resolved_item_ids(entry, recipe)
    item_generator = recipe["item_generator"]
    slot_id = "map:#{entry["map_id"]}|event:#{entry["event_id"]}"
    generator = nil
    if item_generator.is_a?(Hash)
      rules = item_generator["rules_version"]
      generator = ItemSlotGenerator.new(
        recipe["seed"], item_ground_pool(rules), item_tm_pool(rules)
      )
    end
    return entry["authored_item_ids"].map do |item_id|
      replacement = hm_replacement_item(item_id) if
        respond_to?(:hm_replacement_item)
      item = if replacement
               GameData::Item.get(replacement)
             elsif generator
               GameData::Item.get(
                 resolve_ground_reward(item_id, slot_id, generator)
               )
             else
               source = GameData::Item.get(item_id)
               mappings = source.is_TM? ? recipe["tm_mappings"] :
                 recipe["item_mappings"]
               mapped = mappings[item_id.to_s] if mappings.is_a?(Hash)
               GameData::Item.get(mapped || item_id)
             end
      item.id.to_s
    end.uniq
  end

  def self.tracker_area_event_completed?(entry)
    map_id = entry["map_id"].to_i
    event_id = entry["event_id"].to_i
    return false if map_id < 1 || event_id < 1
    if $game_self_switches
      return true if ["A", "B", "C", "D"].any? do |switch|
        $game_self_switches[[map_id, event_id, switch]] == true
      end
    end
    return false
  rescue Exception
    return false
  end
end

Ironmon.tracker_area_catalog_document
