#===============================================================================
# Ironmon public area lookup
#===============================================================================

module Ironmon
  AREA_CATALOG_PATH = File.join("Data", "Ironmon", "area_catalog.dat")
  OBTAINABILITY_SOURCE_CATALOG_PATH = File.join(
    "Data", "Ironmon", "obtainability_source_catalog.json"
  )

  class AreaCatalogError < StandardError
  end

  class ObtainabilitySourceCatalogError < StandardError
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

  def self.tracker_obtainability_source_catalog
    return @tracker_obtainability_source_catalog if
      @tracker_obtainability_source_catalog
    document = File.open(OBTAINABILITY_SOURCE_CATALOG_PATH, "rb") do |file|
      tracker_stringify_catalog_keys(JSON.parse(file.read))
    end
    tracker_validate_obtainability_source_catalog(document)
    @tracker_obtainability_source_catalog = document
    return @tracker_obtainability_source_catalog
  rescue Exception => e
    raise ObtainabilitySourceCatalogError,
      "The bundled obtainability source catalog is unavailable: #{e.message}"
  end

  def self.tracker_stringify_catalog_keys(value)
    if value.is_a?(Hash)
      result = {}
      value.each do |key, entry|
        result[key.to_s] = tracker_stringify_catalog_keys(entry)
      end
      return result
    end
    return value.map { |entry| tracker_stringify_catalog_keys(entry) } if
      value.is_a?(Array)
    return value
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
    if !document.is_a?(Hash) || document["schema_version"].to_i != 1 ||
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

  def self.tracker_validate_obtainability_source_catalog(document)
    if !document.is_a?(Hash) || document["schema_version"] != 1 ||
       document["fingerprint"].to_s.empty? ||
       !document["sources"].is_a?(Array) ||
       !document["resources"].is_a?(Array)
      raise "unsupported obtainability source catalog schema"
    end
    document["sources"].each do |entry|
      if !entry.is_a?(Hash) || entry["species_id"].to_i < 1 ||
         !["none", "wild"].include?(entry["mapping_kind"].to_s) ||
         !entry["mapping_context"].is_a?(Array) ||
         entry["reason"].to_s.empty? || entry["detail"].to_s.empty?
        raise "an obtainability source catalog entry is malformed"
      end
    end
    document["resources"].each do |entry|
      if !entry.is_a?(Hash) || !entry["item_ids"].is_a?(Array) ||
         entry["item_ids"].empty? || entry["quantity"].to_i < 1 ||
         entry["slot_id"].to_s.empty?
        raise "an obtainability resource catalog entry is malformed"
      end
    end
    return true
  end
end
