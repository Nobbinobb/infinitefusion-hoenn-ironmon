#===============================================================================
# Ironmon challenge statistics engine and event integration
#===============================================================================

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

Ironmon.register_graphics_update_hook(
  :challenge_statistics,
  proc { Ironmon.refresh_attempt_progress_statistics }
)
