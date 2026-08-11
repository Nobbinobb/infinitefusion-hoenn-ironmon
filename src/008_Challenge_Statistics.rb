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

  def self.record_item_healing(requested, actual)
    statistics = attempt_statistics
    return if !statistics || !@statistics_item_context
    requested = requested.to_i
    actual = actual.to_i
    return if requested <= 0 || actual < 0
    statistics["total_item_healing"] += actual
    waste = requested - actual
    statistics["wasted_item_healing"] += waste if waste > 0
  end

  def self.with_statistics_item_context(item, source)
    previous = @statistics_item_context
    @statistics_item_context = [item.to_s, source.to_s]
    return yield
  ensure
    @statistics_item_context = previous
  end

  def self.record_consumed_item(item, source, quantity = 1)
    statistics = attempt_statistics
    return if !statistics || current_run_attempt["result"] != "active"
    quantity = quantity.to_i
    return if quantity <= 0
    item_id = GameData::Item.get(item).id.to_s
    source_name = source.to_s
    counts = statistics["items_by_source"][source_name]
    return if !counts
    counts[item_id] = counts.fetch(item_id, 0) + quantity
    statistics["items_used"] += quantity
  end

  def self.bag_quantity(bag, item)
    return 0 if !bag
    return bag.pbQuantity(item).to_i
  rescue Exception
    return 0
  end

  def self.battle_item_consumed?(item)
    use_type = GameData::Item.get(item).battle_use
    return use_type != 0 && !(use_type >= 6 && use_type <= 10)
  end

  def self.register_statistics_battler_item(battler)
    return if !battler
    battle = attempt_statistics ? attempt_statistics["active_battle"] : nil
    battle_id = battle ? battle["id"] : nil
    pokemon = battler.pokemon
    marker = pokemon.instance_variable_get(:@ironmon_item_provenance_battle_id)
    owned = if marker == battle_id
              pokemon.instance_variable_get(
                :@ironmon_item_player_owned
              ) == true
            else
              !battler.opposes?
            end
    battler.instance_variable_set(:@ironmon_item_player_owned, owned)
    pokemon.instance_variable_set(:@ironmon_item_player_owned, owned)
    pokemon.instance_variable_set(:@ironmon_item_provenance_battle_id, battle_id)
  end

  def self.statistics_player_owned_item?(battler)
    return battler && battler.instance_variable_get(
      :@ironmon_item_player_owned
    ) == true
  end

  def self.reset_statistics_item_provenance
    @statistics_displaced_items = Hash.new(0)
    @statistics_expected_item_changes = {}
  end

  def self.prepare_statistics_item_assignment(battler, value)
    return if !battler || !attempt_statistics
    @statistics_displaced_items ||= Hash.new(0)
    @statistics_expected_item_changes ||= {}
    current = battler.item_id
    new_item = GameData::Item.try_get(value)
    new_id = new_item ? new_item.id : nil
    return if current == new_id
    expected = @statistics_expected_item_changes.delete(battler.object_id)
    current_owned = statistics_player_owned_item?(battler)
    if current && current_owned && expected != current
      @statistics_displaced_items[current.to_s] += 1
    end
    new_owned = false
    if new_id
      battle = battler.instance_variable_get(:@battle)
      candidates = battle ? battle.battlers : []
      source = candidates.find do |other|
        other && other != battler && other.item_id == new_id &&
          statistics_player_owned_item?(other)
      end
      if source
        new_owned = true
        source.instance_variable_set(:@ironmon_item_player_owned, false)
        source.pokemon.instance_variable_set(
          :@ironmon_item_player_owned, false
        )
        @statistics_expected_item_changes[source.object_id] = new_id
      elsif @statistics_displaced_items[new_id.to_s] > 0
        new_owned = true
        @statistics_displaced_items[new_id.to_s] -= 1
      end
    end
    battler.instance_variable_set(:@ironmon_item_player_owned, new_owned)
    battle = attempt_statistics["active_battle"]
    battler.pokemon.instance_variable_set(
      :@ironmon_item_player_owned, new_owned
    )
    battler.pokemon.instance_variable_set(
      :@ironmon_item_provenance_battle_id, battle ? battle["id"] : nil
    )
  rescue Exception => e
    echoln "Ironmon item provenance update failed safely: #{e.message}"
  end

  def self.with_owned_held_item_context(item, battler)
    if statistics_player_owned_item?(battler) && !battler.opposes?
      return with_statistics_item_context(item, :Held) { yield }
    end
    return yield
  end
end

alias ironmon_statistics_original_item_restore_hp pbItemRestoreHP
def pbItemRestoreHP(pokemon, restore_hp)
  actual = ironmon_statistics_original_item_restore_hp(pokemon, restore_hp)
  if $Trainer && $Trainer.party.include?(pokemon)
    Ironmon.record_item_healing(restore_hp, actual)
  end
  return actual
end

alias ironmon_statistics_original_use_item pbUseItem
def pbUseItem(bag, item, bagscene = nil)
  before = Ironmon.bag_quantity(bag, item)
  result = Ironmon.with_statistics_item_context(item, :Bag) do
    ironmon_statistics_original_use_item(bag, item, bagscene)
  end
  consumed = before - Ironmon.bag_quantity(bag, item)
  Ironmon.record_consumed_item(item, :Bag, consumed) if consumed > 0
  return result
end

alias ironmon_statistics_original_use_item_on_pokemon pbUseItemOnPokemon
def pbUseItemOnPokemon(item, pokemon, scene)
  before = Ironmon.bag_quantity($PokemonBag, item)
  result = Ironmon.with_statistics_item_context(item, :Bag) do
    ironmon_statistics_original_use_item_on_pokemon(item, pokemon, scene)
  end
  consumed = before - Ironmon.bag_quantity($PokemonBag, item)
  Ironmon.record_consumed_item(item, :Bag, consumed) if consumed > 0
  return result
end

module IronmonStatisticsBattleHooks
  def pbUseItemOnPokemon(item, idx_party, user_battler)
    return ironmon_statistics_use_battle_item(item, user_battler) { super }
  end

  def pbUseItemOnBattler(item, idx_party, user_battler)
    return ironmon_statistics_use_battle_item(item, user_battler) { super }
  end

  def pbUseItemInBattle(item, idx_battler, user_battler)
    return ironmon_statistics_use_battle_item(item, user_battler) { super }
  end

  def pbUsePokeBallInBattle(item, idx_battler, user_battler)
    return ironmon_statistics_use_battle_item(item, user_battler) { super }
  end

  private

  def ironmon_statistics_use_battle_item(item, user_battler)
    choice = @choices[user_battler.index]
    result = Ironmon.with_statistics_item_context(item, :Bag) { yield }
    if pbOwnedByPlayer?(user_battler.index) && choice[1].nil? &&
       Ironmon.battle_item_consumed?(item)
      Ironmon.record_consumed_item(item, :Bag)
    end
    return result
  end
end

PokeBattle_Battle.prepend(IronmonStatisticsBattleHooks)

module IronmonStatisticsBattlerHooks
  def item=(value)
    Ironmon.prepare_statistics_item_assignment(self, value)
    return super
  end

  def pbRecoverHP(amount, anim = true, any_anim = true)
    actual = super
    Ironmon.record_item_healing(amount, actual) if !opposes?
    return actual
  end

  def pbItemHPHealCheck(item_to_use = nil, fling = false)
    item = item_to_use || self.item
    return Ironmon.with_owned_held_item_context(item, self) { super }
  end

  def pbConsumeItem(recoverable = true, symbiosis = true, belch = true)
    item = self.item
    owned = Ironmon.statistics_player_owned_item?(self)
    result = super
    Ironmon.record_consumed_item(item, :Held) if item && owned
    return result
  end

  def pbFaint(show_message = true)
    if fainted? && !@fainted && opposes?
      Ironmon.record_trainer_pokemon_defeated(self)
    end
    return super
  end
end

PokeBattle_Battler.prepend(IronmonStatisticsBattlerHooks)

module IronmonStatisticsBattleHandlerHooks
  def triggerEORHealingItem(item, battler, battle)
    return Ironmon.with_owned_held_item_context(item, battler) { super }
  end

  def triggerUserItemAfterMoveUse(item, user, targets, move, num_hits, battle)
    return Ironmon.with_owned_held_item_context(item, user) { super }
  end
end

BattleHandlers.singleton_class.prepend(IronmonStatisticsBattleHandlerHooks)

module IronmonStatisticsBugBiteHooks
  def pbEffectAfterAllHits(user, target)
    item = target.item
    owned = Ironmon.statistics_player_owned_item?(target)
    result = if item && owned
      Ironmon.with_statistics_item_context(item, :Held) { super }
    else
      super
    end
    if item && owned && !target.item
      Ironmon.record_consumed_item(item, :Held)
    end
    return result
  end
end

PokeBattle_Move_0F4.prepend(IronmonStatisticsBugBiteHooks)

Events.onStartBattle += proc do |_sender|
  Ironmon.begin_statistics_battle
end

Events.onEndBattle += proc do |_sender, _event|
  Ironmon.complete_statistics_battle
end

module Graphics
  class << self
    alias ironmon_statistics_original_update update
    def update
      ironmon_statistics_original_update
      Ironmon.refresh_attempt_progress_statistics
    end
  end
end
