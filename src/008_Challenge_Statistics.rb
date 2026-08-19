#===============================================================================
# Ironmon authoritative per-attempt challenge statistics
#===============================================================================

module Ironmon
  ATTEMPT_STATISTICS_SCHEMA_VERSION = 1

  def self.default_attempt_statistics
    starting_badges = $Trainer ? $Trainer.badge_count : 0
    return {
      "schema_version" => ATTEMPT_STATISTICS_SCHEMA_VERSION,
      "battles_completed" => 0,
      "next_battle_number" => 1,
      "active_battle" => nil,
      "highest_player_level" => 0,
      "starting_badges" => starting_badges,
      "badges_earned" => 0,
      "total_item_healing" => 0,
      "wasted_item_healing" => 0,
      "items_used" => 0,
      "items_by_source" => { "Bag" => {}, "Held" => {} },
      "trainer_species_counts" => {},
      "trainer_species_names" => {},
      "trainer_species_distinct" => 0,
      "trainer_species_most_encountered" => [],
      "trainer_defeated_count" => 0,
      "trainer_defeated_bst_total" => 0,
      "trainer_defeated_bst_average" => nil,
      "trainer_defeated_bst_minimum" => nil,
      "trainer_defeated_bst_minimum_species" => [],
      "trainer_defeated_bst_maximum" => nil,
      "trainer_defeated_bst_maximum_species" => []
    }
  end

  def self.normalize_attempt_statistics(value)
    statistics = default_attempt_statistics
    return statistics if !value.is_a?(Hash)
    numeric_keys = [
      "battles_completed", "next_battle_number", "highest_player_level",
      "starting_badges", "badges_earned", "total_item_healing",
      "wasted_item_healing", "items_used", "trainer_species_distinct",
      "trainer_defeated_count", "trainer_defeated_bst_total"
    ]
    numeric_keys.each do |key|
      number = value[key].to_i
      statistics[key] = number < 0 ? 0 : number
    end
    statistics["next_battle_number"] = 1 if
      statistics["next_battle_number"] < 1
    statistics["active_battle"] = normalize_statistics_battle(
      value["active_battle"]
    )
    statistics["items_by_source"] = normalize_item_source_counts(
      value["items_by_source"]
    )
    statistics["trainer_species_counts"] = normalize_count_hash(
      value["trainer_species_counts"]
    )
    statistics["trainer_species_names"] = normalize_string_hash(
      value["trainer_species_names"]
    )
    statistics["trainer_species_most_encountered"] = normalize_string_array(
      value["trainer_species_most_encountered"]
    )
    ["minimum", "maximum"].each do |boundary|
      bst_key = "trainer_defeated_bst_#{boundary}"
      species_key = "#{bst_key}_species"
      statistics[bst_key] = value[bst_key].nil? ? nil : value[bst_key].to_i
      statistics[species_key] = normalize_string_array(value[species_key])
    end
    update_derived_attempt_statistics(statistics)
    return statistics
  end

  def self.normalize_statistics_battle(value)
    return nil if !value.is_a?(Hash)
    type = value["type"].to_s
    return nil if !["wild", "trainer"].include?(type)
    return {
      "id" => value["id"].to_s,
      "type" => type,
      "trainer_seen_ids" => normalize_boolean_hash(value["trainer_seen_ids"]),
      "trainer_defeated_ids" => normalize_boolean_hash(
        value["trainer_defeated_ids"]
      )
    }
  end

  def self.normalize_item_source_counts(value)
    value = {} if !value.is_a?(Hash)
    return {
      "Bag" => normalize_count_hash(value["Bag"]),
      "Held" => normalize_count_hash(value["Held"])
    }
  end

  def self.normalize_count_hash(value)
    result = {}
    return result if !value.is_a?(Hash)
    value.each do |key, count|
      count = count.to_i
      result[key.to_s] = count if count > 0
    end
    return result
  end

  def self.normalize_boolean_hash(value)
    result = {}
    return result if !value.is_a?(Hash)
    value.each { |key, present| result[key.to_s] = true if present == true }
    return result
  end

  def self.normalize_string_hash(value)
    result = {}
    return result if !value.is_a?(Hash)
    value.each do |key, text|
      key = key.to_s
      text = text.to_s
      result[key] = text if !key.empty? && !text.empty?
    end
    return result
  end

  def self.normalize_string_array(value)
    return [] if !value.is_a?(Array)
    return value.map(&:to_s).uniq.sort
  end

  def self.attempt_statistics
    attempt = current_run_attempt
    return nil if !attempt
    if !attempt["statistics"].is_a?(Hash) ||
       attempt["statistics"]["schema_version"] !=
         ATTEMPT_STATISTICS_SCHEMA_VERSION ||
       !attempt["statistics"]["trainer_species_names"].is_a?(Hash)
      attempt["statistics"] = normalize_attempt_statistics(
        attempt["statistics"]
      )
    end
    return attempt["statistics"]
  end
end
