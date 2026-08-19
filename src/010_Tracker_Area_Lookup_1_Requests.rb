#===============================================================================
# Ironmon tracker area lookup requests and discovery validation
#===============================================================================

module Ironmon
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
end
