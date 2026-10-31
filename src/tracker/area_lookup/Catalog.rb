#===============================================================================
# Ironmon public area lookup
#===============================================================================

module Ironmon
  class AreaCatalogError < StandardError
  end

  class ObtainabilitySourceCatalogError < StandardError
  end

  def self.tracker_area_catalog_document(profile_id = active_generation_profile_id, work_checkpoint = nil)
    @tracker_area_catalog_documents ||= {}
    return @tracker_area_catalog_documents[profile_id] if
      @tracker_area_catalog_documents[profile_id]
    path = generation_profile_component_path("area_catalog", profile_id)
    document = File.open(path, "rb") do |file|
      Marshal.load(file)
    end
    tracker_validate_area_catalog(document, work_checkpoint)
    if profile_id != current_generation_profile_id
      current = tracker_area_catalog_document(current_generation_profile_id, work_checkpoint)
      current["areas"].each do |current_area|
        work_checkpoint.call if work_checkpoint
        area = document["areas"].find { |candidate| candidate["name"] == current_area["name"] }
        if area
          area["trainers"] = current_area["trainers"]
          area["map_ids"] = (area["map_ids"] + current_area["map_ids"]).uniq.sort
        elsif !current_area["trainers"].empty?
          document["areas"] << current_area.merge("items" => [])
        end
      end
    end
    @tracker_area_catalog_documents[profile_id] = document
    return document
  rescue Exception => e
    raise AreaCatalogError,
      "The bundled Ironmon area catalog is unavailable: #{e.message}"
  end

  def self.tracker_area_catalog(work_checkpoint = nil)
    return tracker_area_catalog_document(active_generation_profile_id, work_checkpoint)["areas"]
  end

  def self.tracker_area_encounter_table_available?(map_id, encounter_type)
    triggers = tracker_area_catalog_document["encounter_triggers"]
    triggers ||= tracker_area_catalog_document(
      current_generation_profile_id
    )["encounter_triggers"]
    if !triggers
      raise AreaCatalogError, "The bundled Ironmon area catalog is missing encounter triggers."
    end
    methods = triggers[map_id.to_i] || []
    value = encounter_type.to_s
    return true if methods.include?(value)
    return methods.include?("Fishing") if value.end_with?("Rod")
    return methods.include?("Water") if value.start_with?("Water")
    return methods.include?("Cave") if value.start_with?("Cave")
    if value.match?(/\ALand(?:Rain|Sunny|Wind|Fog|Storm|Snow)\z/)
      return methods.any? { |method| method.start_with?("Land") || method == "TallGrass" }
    end
    base = value.sub(/(?:Morning|Day|Afternoon|Evening|Night)\z/, "")
    return methods.include?(base)
  end

  def self.tracker_obtainability_source_catalog(work_checkpoint = nil)
    @tracker_obtainability_source_catalogs ||= {}
    profile_id = active_generation_profile_id
    return @tracker_obtainability_source_catalogs[profile_id] if
      @tracker_obtainability_source_catalogs[profile_id]
    path = generation_profile_component_path("obtainability_sources", profile_id)
    document = File.open(path, "rb") do |file|
      tracker_stringify_catalog_keys(JSON.parse(file.read), work_checkpoint)
    end
    tracker_validate_obtainability_source_catalog(document, work_checkpoint)
    @tracker_obtainability_source_catalogs[profile_id] = document
    return document
  rescue Exception => e
    raise ObtainabilitySourceCatalogError,
      "The bundled obtainability source catalog is unavailable: #{e.message}"
  end

  def self.tracker_stringify_catalog_keys(value, work_checkpoint = nil)
    work_checkpoint.call if work_checkpoint
    if value.is_a?(Hash)
      result = {}
      value.each do |key, entry|
        result[key.to_s] = tracker_stringify_catalog_keys(entry, work_checkpoint)
      end
      return result
    end
    return value.map { |entry| tracker_stringify_catalog_keys(entry, work_checkpoint) } if
      value.is_a?(Array)
    return value
  end

  def self.tracker_area_event_entry_id(category, map_id, event_id, trainer_type = nil, trainer_name = nil)
    @tracker_area_event_entry_indexes ||= {}
    cache_key = [active_generation_profile_id, category]
    index = @tracker_area_event_entry_indexes[cache_key]
    if !index
      index = {}
      tracker_area_catalog.each do |area|
        entries = area[category] || []
        entries.each do |entry|
          event_ids = entry["event_ids"] || [entry["event_id"]]
          event_ids.each do |id|
            key = [entry["map_id"].to_i, id.to_i]
            (index[key] ||= []) << entry
          end
        end
      end
      @tracker_area_event_entry_indexes[cache_key] = index
    end
    entries = index[[map_id.to_i, event_id.to_i]] || []
    selected = entries.find do |entry|
      (!trainer_type || entry["trainer_type"] == trainer_type.to_s) &&
        (!trainer_name || entry["trainer_name"] == trainer_name.to_s || entry["battle_kind"] == "rival")
    end
    return selected && selected["entry_id"]
  end

  def self.tracker_area_hidden_items(map_id)
    @tracker_area_hidden_item_indexes ||= {}
    cache_key = [active_generation_profile_id, map_id.to_i]
    return @tracker_area_hidden_item_indexes[cache_key] if
      @tracker_area_hidden_item_indexes.key?(cache_key)
    entries = []
    tracker_area_catalog.each do |area|
      next if !area["map_ids"].include?(map_id.to_i)
      entries = area["items"].select do |entry|
        entry["map_id"].to_i == map_id.to_i && entry["hidden"] == true
      end
      break
    end
    @tracker_area_hidden_item_indexes[cache_key] = entries
    return entries
  end

  def self.tracker_validate_area_catalog(document, work_checkpoint = nil)
    if !document.is_a?(Hash) || document["schema_version"].to_i != 1 ||
       !document["areas"].is_a?(Array)
      raise "unsupported area catalog schema"
    end
    area_ids = {}
    entry_ids = {}
    document["areas"].each do |area|
      work_checkpoint.call if work_checkpoint
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
        work_checkpoint.call if work_checkpoint
        entry_id = entry.is_a?(Hash) ? entry["entry_id"].to_s : ""
        raise "an area content entry is malformed" if entry_id.empty?
        raise "duplicate area content identifier" if entry_ids[entry_id]
        entry_ids[entry_id] = true
      end
    end
    triggers = document["encounter_triggers"]
    if triggers && (!triggers.is_a?(Hash) || triggers.any? do |map_id, methods|
      !map_id.is_a?(Integer) || map_id < 1 || !methods.is_a?(Array) ||
        methods.any? { |method| !method.is_a?(String) || method.empty? }
    end)
      raise "the area encounter triggers are malformed"
    end
    return true
  end

  def self.tracker_validate_obtainability_source_catalog(document, work_checkpoint = nil)
    if !document.is_a?(Hash) || document["schema_version"] != 1 ||
       document["fingerprint"].to_s.empty? ||
       !document["sources"].is_a?(Array) ||
       !document["resources"].is_a?(Array)
      raise "unsupported obtainability source catalog schema"
    end
    document["sources"].each do |entry|
      work_checkpoint.call if work_checkpoint
      if !entry.is_a?(Hash) || entry["species_id"].to_i < 1 ||
         !["none", "wild"].include?(entry["mapping_kind"].to_s) ||
         !entry["mapping_context"].is_a?(Array) ||
         entry["reason"].to_s.empty? || entry["detail"].to_s.empty?
        raise "an obtainability source catalog entry is malformed"
      end
    end
    document["resources"].each do |entry|
      work_checkpoint.call if work_checkpoint
      if !entry.is_a?(Hash) || !entry["item_ids"].is_a?(Array) ||
         entry["item_ids"].empty? || entry["quantity"].to_i < 1 ||
         entry["slot_id"].to_s.empty?
        raise "an obtainability resource catalog entry is malformed"
      end
    end
    return true
  end
end
