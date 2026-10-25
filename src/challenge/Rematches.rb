#===============================================================================
# Trainer rematches during an active challenge
#===============================================================================

module Ironmon
  REMATCH_BLOCK_TAG = :ironmon_rematch_blocked
  REMATCH_BLOCKED_MESSAGE =
    "I'd really like to battle you again, but I heard you're taking on a " \
    "challenge right now. I'll wait until you've finished."

  def self.rematch_battles_blocked?
    return active?
  end

  def self.block_trainer_rematch
    pbMessage(_INTL(REMATCH_BLOCKED_MESSAGE))
    throw REMATCH_BLOCK_TAG, true
  end

  def self.reject_direct_trainer_rematch
    pbMessage(_INTL(REMATCH_BLOCKED_MESSAGE))
    $PokemonGlobal.nextBattleBack = nil if $PokemonGlobal
  end
end

alias ironmon_rematches_original_generate_trainer_rematch generateTrainerRematch
def generateTrainerRematch(trainer, allow_double = true)
  if Ironmon.rematch_battles_blocked?
    Ironmon.reject_direct_trainer_rematch
    return [trainer, false]
  end
  return ironmon_rematches_original_generate_trainer_rematch(trainer, allow_double)
end

alias ironmon_rematches_original_rival_rematch rivalRematch
def rivalRematch(loseDialog)
  if Ironmon.rematch_battles_blocked?
    Ironmon.reject_direct_trainer_rematch
    return false
  end
  return ironmon_rematches_original_rival_rematch(loseDialog)
end

alias ironmon_rematches_original_do_post_battle_action doPostBattleAction
def doPostBattleAction(actionType, trainer, double_allowed = true)
  if actionType == :BATTLE && Ironmon.rematch_battles_blocked?
    Ironmon.block_trainer_rematch
  end
  return ironmon_rematches_original_do_post_battle_action(
    actionType, trainer, double_allowed
  )
end

alias ironmon_rematches_original_post_battle_actions_menu postBattleActionsMenu
def postBattleActionsMenu(trainer = nil, event_id = nil)
  blocked = catch(Ironmon::REMATCH_BLOCK_TAG) do
    return ironmon_rematches_original_post_battle_actions_menu(
      trainer, event_id
    )
  end
  $PokemonGlobal.nextBattleBack = nil if $PokemonGlobal
  return nil if blocked
end
