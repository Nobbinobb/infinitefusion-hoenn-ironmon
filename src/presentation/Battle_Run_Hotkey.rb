#===============================================================================
# Ironmon wild-battle Run shortcut
#===============================================================================

module Ironmon
  def self.battle_cancel_run_hotkey?(battle, mode)
    return false if !active? || !battle || battle.trainerBattle?
    return mode == 0
  end

  def self.with_battle_cancel_run_hotkey
    previous = @battle_cancel_run_hotkey_active
    @battle_cancel_run_hotkey_active = true
    return yield
  ensure
    @battle_cancel_run_hotkey_active = previous
  end

  def self.battle_cancel_run_hotkey_active?
    return @battle_cancel_run_hotkey_active == true
  end
end

module IronmonBattleRunHotkeySceneHooks
  def pbCommandMenuEx(idxBattler, texts, mode = 0)
    return super if !Ironmon.battle_cancel_run_hotkey?(@battle, mode)
    return Ironmon.with_battle_cancel_run_hotkey do
      result = super(idxBattler, texts, 1)
      result == -1 ? 3 : result
    end
  end
end

PokeBattle_Scene.prepend(IronmonBattleRunHotkeySceneHooks)

module IronmonBattleRunHotkeyDisplayHooks
  def setIndexAndMode(index, mode)
    if mode == 1 && Ironmon.battle_cancel_run_hotkey_active?
      mode = 0
    end
    return super(index, mode)
  end
end

CommandMenuDisplay.prepend(IronmonBattleRunHotkeyDisplayHooks)
