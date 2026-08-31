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
    catalog_areas = tracker_area_catalog
    if !archived && $game_map
      current_map_id = $game_map.map_id.to_i
      current_areas, other_areas = catalog_areas.partition do |area|
        area["map_ids"].include?(current_map_id)
      end
      catalog_areas = current_areas + other_areas
    end
    areas = catalog_areas.map do |area|
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
          tracker_area_encounter_total(encounter_entries, recipe) : 0,
        "encountered" => 0,
        "item_total" => category == "item" ? area["items"].length : 0,
        "items_collected" => items_collected
      }
    end
    return {
      "schema_version" => tracker_area_catalog_document["schema_version"],
      "revision" => 0,
      "overworld_encounters" => recipe["overworld_encounters"],
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
    encounter_environment = payload["encounter_environment"]
    encounter_environment = encounter_environment.to_s if
      encounter_environment
    offset = payload["offset"].to_i
    limit = payload["limit"].to_i
    if category == "encounter" &&
       (offset < 0 || limit < 1 || limit > 50)
      raise TrackerLookupError.new(
        "invalid_area_page", "The selected encounter page is invalid."
      )
    end
    discovery_keys = tracker_validate_area_discovery_keys(
      area, category, recipe, payload["discovery_keys"]
    )
    full_details = debug || archived
    result = {
      "area_id" => area["area_id"],
      "name" => area["name"],
      "category" => category,
      "overworld_encounters" => recipe["overworld_encounters"],
      "revision" => 0,
      "offset" => category == "encounter" ? offset : 0,
      "limit" => category == "encounter" ? limit : 0,
      "total_count" => 0,
      "encounter_environment" => category == "encounter" ?
        encounter_environment : nil,
      "encounter_environments" => [],
      "trainers" => [],
      "encounters" => [],
      "encounter_fusions" => [],
      "pending" => false,
      "items" => []
    }
    case category
    when "trainer"
      result["trainers"] = tracker_area_trainer_entries(
        area, recipe, discovery_keys, full_details, archived
      )
      result["total_count"] = result["trainers"].length
    when "encounter"
      metadata = tracker_area_encounter_metadata(area, recipe)
      environments = tracker_area_encounter_environment_index(
        metadata, recipe["overworld_encounters"]
      )
      result["encounter_environments"] = environments
      if encounter_environment
        selected = environments.find do |entry|
          entry["key"] == encounter_environment
        end
        if !selected
          raise TrackerLookupError.new(
            "invalid_area_environment",
            "The selected encounter environment is invalid."
          )
        end
        slots = if encounter_environment == "cross"
                  []
                else
                  metadata.select do |entry|
                    tracker_area_encounter_environment(
                      entry["encounter_type"]
                    ) == encounter_environment
                  end
                end
        source_metadata = if encounter_environment == "cross"
                            metadata.select do |entry|
                              overworld_encounter_environment?(
                                entry["encounter_type"]
                              )
                            end
                          else
                            slots
                          end
        sources = tracker_area_normal_encounter_sources(source_metadata, recipe)
        fusion_count = tracker_area_encounter_fusion_count(
          sources, encounter_environment == "cross",
          encounter_environment == "cross" ? nil : encounter_environment,
          recipe["overworld_encounters"]
        )
        result["total_count"] = slots.length + fusion_count
        slot_page = slots.slice(offset, limit) || []
        result["encounters"] = tracker_area_encounter_payloads(
          slot_page, recipe, discovery_keys, full_details
        )
        fusion_offset = [offset - slots.length, 0].max
        fusion_limit = limit - slot_page.length
        if fusion_limit > 0
          descriptors = tracker_area_encounter_fusion_descriptors(
            sources, encounter_environment == "cross", fusion_offset,
            fusion_limit,
            encounter_environment == "cross" ? nil : encounter_environment,
            recipe["overworld_encounters"]
          )
          fusions = tracker_area_encounter_fusion_entries(
            descriptors, recipe, discovery_keys,
            payload["use_native_fusion_mapping"] == true ?
              (payload["fusion_results"] || []) : nil,
            full_details
          )
          result["pending"] = fusions.nil?
          result["encounter_fusions"] = fusions || []
          if fusions.nil? && payload["use_native_fusion_mapping"] == true
            revealed = tracker_area_revealed_fusion_descriptors(
              descriptors, discovery_keys, full_details
            )
            result["required_fusion_materials"] =
              tracker_area_encounter_fusion_materials(revealed).uniq.map do |pair|
                { "body_id" => pair[0], "head_id" => pair[1] }
              end
          end
        end
      else
        result["total_count"] = tracker_area_encounter_total(metadata, recipe)
      end
    when "item"
      result["items"] = tracker_area_item_entries(
        area, recipe, discovery_keys, full_details, archived
      )
      result["total_count"] = result["items"].length
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
      "overworld_encounters" => !!($PokemonSystem &&
        $PokemonSystem.overworld_encounters),
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
        $PokemonGlobal.ironmon_species_generator_version,
      "player_fusion_generator_version" => PlayerFusionMapper::SCHEMA_VERSION,
      "base_stat_source_fingerprint" => base_stat_source_fingerprint
    }
  end

  def self.tracker_validate_area_discovery_keys(area, category, recipe, values)
    keys = values.is_a?(Array) ? values.map { |value| value.to_s }.uniq : []
    valid = case category
            when "trainer"
              area["trainers"].map { |entry| entry["entry_id"] }
            when "encounter"
              authored = tracker_area_encounter_metadata(area, recipe).
                map { |entry| entry["entry_id"] }
              fusions = keys.select do |key|
                tracker_valid_area_encounter_fusion_key?(area, recipe, key)
              end
              authored + fusions
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

  def self.tracker_valid_area_encounter_fusion_key?(area, recipe, key)
    parsed = tracker_area_encounter_fusion_key(key)
    return false if !parsed || !area["map_ids"].include?(parsed["map_id"])
    metadata = tracker_area_encounter_metadata(area, recipe)
    first = tracker_area_encounter_fusion_source(
      metadata, parsed["map_id"], parsed["first_version"],
      parsed["first_type"], parsed["first_slot"]
    )
    second = tracker_area_encounter_fusion_source(
      metadata, parsed["map_id"], parsed["second_version"],
      parsed["second_type"], parsed["second_slot"]
    )
    return false if !first || !second
    same = first["version"] == second["version"] &&
      first["encounter_type"] == second["encounter_type"]
    return false if parsed["origin"].end_with?("same") != same
    if parsed["origin"] == "standard_cross"
      return false if !overworld_encounter_environment?(
        first["encounter_type"]
      ) || !overworld_encounter_environment?(second["encounter_type"])
    end
    return true
  end
end
