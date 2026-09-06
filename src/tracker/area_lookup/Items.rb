#===============================================================================
# Ironmon tracker area item reconstruction
#===============================================================================

module Ironmon
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
            "item_name" => item ? item.name : item_id,
            "category" => tracker_area_item_category(item)
          }
        end
      }
    end
  end

  def self.tracker_area_item_category(item)
    return nil if !item
    category = item_result_category(item)
    return category == :excluded || category == :uniform ?
      "general_utility" : category.to_s
  end

  def self.tracker_area_resolved_item_ids(entry, recipe)
    item_generator = recipe["item_generator"]
    slot_id = "map:#{entry["map_id"]}|event:#{entry["event_id"]}"
    generator = nil
    if item_generator.is_a?(Hash)
      rules = item_generator["rules_version"]
      generator = build_item_slot_generator(recipe["seed"], rules)
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
