#===============================================================================
# Ironmon completed-run battle boundaries
#===============================================================================

alias ironmon_failure_original_wild_battle_core pbWildBattleCore
def pbWildBattleCore(*args)
  return Ironmon.blocked_battle_result if Ironmon.failed_run_locked?
  return ironmon_failure_original_wild_battle_core(*args)
end

alias ironmon_failure_original_trainer_battle_core pbTrainerBattleCore
def pbTrainerBattleCore(*args)
  return Ironmon.blocked_battle_result if Ironmon.failed_run_locked?
  return ironmon_failure_original_trainer_battle_core(*args)
end
