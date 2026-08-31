#===============================================================================
# Ironmon transient tracker area discoveries
#===============================================================================

module Ironmon
  AREA_DISCOVERY_CATEGORIES = ["trainer", "encounter", "item"].freeze

  def self.tracker_area_id_for_map(map_id)
    map_id = map_id.to_i
    area = tracker_area_catalog.find do |candidate|
      candidate["map_ids"].include?(map_id)
    end
    return area ? area["area_id"] : nil
  end

  def self.tracker_area_reference_for_entry(category, entry_id)
    category = category.to_s
    entry_id = entry_id.to_s
    return nil if !AREA_DISCOVERY_CATEGORIES.include?(category) ||
                  entry_id.empty?
    if category == "encounter"
      components = entry_id.split(":")
      valid_prefix = ["encounter", "encounter_fusion"].include?(
        components[0]
      )
      area_id = valid_prefix && components.length >= 4 ?
        tracker_area_id_for_map(components[1]) : nil
      return area_id ? [area_id, entry_id] : nil
    end
    catalog_key = category == "trainer" ? "trainers" : "items"
    tracker_area_catalog.each do |area|
      entry = area[catalog_key].find do |candidate|
        candidate["entry_id"] == entry_id
      end
      return [area["area_id"], entry_id] if entry
    end
    return nil
  end

  def self.publish_area_discoveries(category, entry_ids)
    connection = tracker_connection
    return false if !active? || !connection.connected?
    references = entry_ids.compact.uniq.map do |entry_id|
      tracker_area_reference_for_entry(category, entry_id)
    end.compact
    grouped = references.group_by { |reference| reference[0] }
    sent = false
    grouped.each do |area_id, entries|
      keys = entries.map { |reference| reference[1] }
      sent = connection.send_area_discovery(area_id, category, keys) || sent
    end
    return sent
  end

  def self.record_defeated_area_trainer(entry_id)
    return publish_area_discoveries("trainer", [entry_id])
  end

  def self.record_encountered_area_slots(entry_ids)
    return publish_area_discoveries("encounter", entry_ids)
  end

  def self.record_collected_area_item(entry_id)
    return publish_area_discoveries("item", [entry_id])
  end
end
