#===============================================================================
# Ironmon battle progress statistics aggregation
#===============================================================================

module Ironmon
  def self.with_statistics_battle_type(type)
    previous = @statistics_battle_type
    @statistics_battle_type = type
    return yield
  ensure
    @statistics_battle_type = previous
  end

  def self.begin_statistics_battle
    statistics = attempt_statistics
    return if !statistics || current_run_attempt["result"] != "active"
    return if statistics["active_battle"]
    number = statistics["next_battle_number"]
    statistics["next_battle_number"] = number + 1
    statistics["active_battle"] = {
      "id" => "#{current_run_attempt["run_id"]}:battle-#{number}",
      "type" => (@statistics_battle_type || :wild).to_s,
      "trainer_seen_ids" => {},
      "trainer_defeated_ids" => {}
    }
    reset_statistics_item_provenance
  end

  def self.complete_statistics_battle
    statistics = attempt_statistics
    return false if !statistics || !statistics["active_battle"]
    statistics["battles_completed"] += 1
    statistics["active_battle"] = nil
    refresh_attempt_progress_statistics
    reset_statistics_item_provenance
    return true
  end

  def self.refresh_attempt_progress_statistics
    statistics = attempt_statistics
    return if !statistics || current_run_attempt["result"] != "active"
    usable_party.each do |pokemon|
      level = pokemon.level.to_i
      statistics["highest_player_level"] = level if
        level > statistics["highest_player_level"]
    end
    badge_count = $Trainer ? $Trainer.badge_count : 0
    earned = badge_count - statistics["starting_badges"]
    earned = 0 if earned < 0
    statistics["badges_earned"] = earned if
      earned > statistics["badges_earned"]
  end

  def self.record_trainer_species_encounter(battler)
    statistics = attempt_statistics
    battle = statistics ? statistics["active_battle"] : nil
    return if !battle || battle["type"] != "trainer" || !battler ||
              !battler.opposes?
    pokemon_id = battler.pokemon.personalID.to_s
    return if battle["trainer_seen_ids"][pokemon_id]
    battle["trainer_seen_ids"][pokemon_id] = true
    species = battler.pokemon.species.to_s
    counts = statistics["trainer_species_counts"]
    counts[species] = counts.fetch(species, 0) + 1
    statistics["trainer_species_names"][species] =
      battler.pokemon.species_data.name.to_s
    update_derived_attempt_statistics(statistics)
  end

  def self.record_trainer_pokemon_defeated(battler)
    statistics = attempt_statistics
    battle = statistics ? statistics["active_battle"] : nil
    return if !battle || battle["type"] != "trainer" || !battler ||
              !battler.opposes? || !battler.pokemon
    pokemon_id = battler.pokemon.personalID.to_s
    return if battle["trainer_defeated_ids"][pokemon_id]
    battle["trainer_defeated_ids"][pokemon_id] = true
    species = battler.pokemon.species.to_s
    bst = battler.pokemon.baseStats.values.inject(0) do |sum, value|
      sum + value.to_i
    end
    statistics["trainer_defeated_count"] += 1
    statistics["trainer_defeated_bst_total"] += bst
    update_bst_boundary(statistics, "minimum", bst, species) do |current|
      current.nil? || bst < current
    end
    update_bst_boundary(statistics, "maximum", bst, species) do |current|
      current.nil? || bst > current
    end
    update_derived_attempt_statistics(statistics)
  end

  def self.update_bst_boundary(statistics, boundary, bst, species)
    value_key = "trainer_defeated_bst_#{boundary}"
    species_key = "#{value_key}_species"
    current = statistics[value_key]
    if yield(current)
      statistics[value_key] = bst
      statistics[species_key] = [species]
    elsif current == bst && !statistics[species_key].include?(species)
      statistics[species_key] << species
      statistics[species_key].sort!
    end
  end

  def self.update_derived_attempt_statistics(statistics)
    counts = statistics["trainer_species_counts"]
    statistics["trainer_species_distinct"] = counts.length
    maximum = counts.values.max
    statistics["trainer_species_most_encountered"] = if maximum
      counts.select { |_species, count| count == maximum }.keys.sort
    else
      []
    end
    defeated = statistics["trainer_defeated_count"]
    statistics["trainer_defeated_bst_average"] = if defeated > 0
      statistics["trainer_defeated_bst_total"].to_f / defeated
    else
      nil
    end
  end

  def self.finalize_attempt_statistics
    refresh_attempt_progress_statistics
    statistics = attempt_statistics
    update_derived_attempt_statistics(statistics) if statistics
  end
end
