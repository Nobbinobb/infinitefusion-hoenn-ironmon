#===============================================================================
# Ironmon tracker engine and event integration
#===============================================================================

Ironmon.register_game_load_hook(
  :tracker, nil,
  proc { |_save_data, _result| Ironmon.refresh_tracker_run_after_load }
)

Ironmon.register_graphics_update_hook(
  :tracker,
  proc do
    Ironmon.update_tracker_connection
    Ironmon.update_tracker_player
    Ironmon.update_tracker_enemies
    interrupt_result = Ironmon.tracker_battle_item_interrupt_result
    throw :ironmon_tracker_battle_item, interrupt_result if
      !interrupt_result.nil?
  end
)

module IronmonTrackerBattleSceneItemHooks
  def pbCommandMenu(idxBattler, firstAction)
    Ironmon.begin_tracker_battle_command(@battle, idxBattler, firstAction, 1)
    return catch(:ironmon_tracker_battle_item) { super }
  ensure
    Ironmon.end_tracker_battle_command
  end

  def pbFightMenu(idxBattler, megaEvoPossible = false)
    Ironmon.begin_tracker_battle_command(@battle, idxBattler, false, -1)
    return catch(:ironmon_tracker_battle_item) { super }
  ensure
    Ironmon.end_tracker_battle_command
  end

  def pbItemMenu(idxBattler, firstAction)
    Ironmon.begin_tracker_battle_command(
      @battle, idxBattler, firstAction, -1, false
    )
    return super
  ensure
    Ironmon.end_tracker_battle_command
  end

  def pbChooseTarget(idxBattler, target_data, visibleSprites = nil)
    return super if !Ironmon.tracker_battle_item_menu_active?
    return catch(:ironmon_tracker_battle_item) do
      Ironmon.with_tracker_battle_item_interrupt { super }
    end
  end
end

PokeBattle_Scene.prepend(IronmonTrackerBattleSceneItemHooks)

module IronmonTrackerBagSceneItemHooks
  def pbChooseItem
    result = catch(:ironmon_tracker_battle_item) do
      Ironmon.with_tracker_battle_item_interrupt { super }
    end
    return nil if Ironmon.tracker_battle_item_pending?
    return result
  end

  def pbShowCommands(helptext, commands, index = 0)
    return catch(:ironmon_tracker_battle_item) do
      Ironmon.with_tracker_battle_item_interrupt { super }
    end
  end
end

PokemonBag_Scene.prepend(IronmonTrackerBagSceneItemHooks)

module IronmonTrackerPartySceneItemHooks
  def pbChoosePokemon(switching = false, initialsel = -1, canswitch = 0)
    return catch(:ironmon_tracker_battle_item) do
      Ironmon.with_tracker_battle_item_interrupt { super }
    end
  end

  def pbShowCommands(helptext, commands, index = 0)
    return catch(:ironmon_tracker_battle_item) do
      Ironmon.with_tracker_battle_item_interrupt { super }
    end
  end
end

PokemonParty_Scene.prepend(IronmonTrackerPartySceneItemHooks)

module IronmonTrackerBattleHooks
  def pbStartBattle
    Ironmon.start_tracker_battle(self)
    return super
  ensure
    Ironmon.end_tracker_battle if Ironmon.tracker_battle_id
  end

  def pbSendOut(send_outs, start_battle = false)
    result = super
    send_outs.each do |entry|
      index = entry[0]
      next if !pbOwnedByPlayer?(index)
      Ironmon.tracker_player_sent_out(@battlers[index])
      break
    end
    send_outs.each do |entry|
      index = entry[0]
      next if index.even?
      Ironmon.tracker_enemy_sent_out(@battlers[index])
    end
    return result
  end

  def pbOnActiveAll
    result = super
    @battlers.each do |battler|
      next if !battler || battler.index.even?
      Ironmon.tracker_enemy_sent_out(battler)
    end
    return result
  end

  def pbFightMenu(idxBattler)
    battler = @battlers[idxBattler]
    Ironmon.tracker_player_move_menu_opened(battler) if battler && pbOwnedByPlayer?(idxBattler)
    return super
  end

  def pbPartyMenu(idxBattler)
    Ironmon.begin_tracker_battle_command(self, idxBattler, false, -1, false)
    return super
  ensure
    Ironmon.end_tracker_battle_command
  end

  def pbItemMenu(idxBattler, firstAction)
    selection = Ironmon.consume_tracker_battle_item
    return super if !selection
    return false if !@internalBattle || !pbOwnedByPlayer?(idxBattler)
    item = GameData::Item.try_get(selection["item"])
    return false if !item || !$PokemonBag || $PokemonBag.pbQuantity(item.id) <= 0
    battler = @battlers[idxBattler]
    return false if !battler || battler.fainted?
    use_type = item.battle_use
    target_index = battler.pokemonIndex
    target_battler = battler
    target_pokemon = battler.pokemon
    if [4, 9].include?(use_type)
      requested_position = selection["target_position"]
      target_battler = @battlers[requested_position] if requested_position.is_a?(Integer)
      if !target_battler || target_battler.fainted? || !target_battler.opposes?(idxBattler)
        target_battler = nil
        eachOtherSideBattler(idxBattler) { |candidate| target_battler ||= candidate }
      end
      return false if !target_battler
      target_index = target_battler.index
      target_pokemon = target_battler.pokemon
    elsif [5, 10].include?(use_type)
      target_index = idxBattler
    end
    if [1, 2, 3, 6, 7, 8].include?(use_type)
      return false if !pbCanUseItemOnPokemon?(item.id, target_pokemon, target_battler, @scene)
    end
    move_index = selection["move_index"] || -1
    return false if !ItemHandlers.triggerCanUseInBattle(
      item.id, target_pokemon, target_battler, move_index, firstAction,
      self, @scene
    )
    return pbRegisterItem(idxBattler, item.id, target_index, move_index)
  end

  def pbShowAbilitySplash(battler, delay = false, logTrigger = true, abilityName = nil)
    Ironmon.tracker_enemy_ability_revealed(battler) if battler && battler.index.odd?
    return super
  end
end

PokeBattle_Battle.prepend(IronmonTrackerBattleHooks)

module IronmonTrackerBattlerHooks
  def pbUseMove(choice, special_usage = false)
    result = super
    if !@lastMoveFailed && @lastMoveUsed && @index.odd? && !special_usage
      Ironmon.tracker_enemy_move_if_changed(self)
    end
    return result
  end
end

PokeBattle_Battler.prepend(IronmonTrackerBattlerHooks)
