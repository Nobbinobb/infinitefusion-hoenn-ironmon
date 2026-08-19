#===============================================================================
# Ironmon item provenance and healing statistics
#===============================================================================

module Ironmon
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
