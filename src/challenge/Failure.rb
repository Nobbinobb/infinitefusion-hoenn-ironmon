#===============================================================================
# Ironmon completed-run battle boundaries
#===============================================================================

module Ironmon
  def self.with_defeat_start_over_suppressed(decision)
    previous = @defeat_start_over_suppressed
    @defeat_start_over_suppressed = active? && [2, 5].include?(decision)
    return yield
  ensure
    @defeat_start_over_suppressed = previous
  end

  def self.defeat_start_over_suppressed?
    return @defeat_start_over_suppressed == true
  end
end

alias ironmon_failure_original_after_battle pbAfterBattle
def pbAfterBattle(decision, canLose)
  return Ironmon.with_defeat_start_over_suppressed(decision) do
    ironmon_failure_original_after_battle(decision, canLose)
  end
end

alias ironmon_failure_original_start_over pbStartOver
def pbStartOver(gameover = false)
  return if Ironmon.defeat_start_over_suppressed?
  return ironmon_failure_original_start_over(gameover)
end

alias ironmon_failure_original_wild_battle_core pbWildBattleCore
def pbWildBattleCore(*args)
  return Ironmon.blocked_battle_result if Ironmon.failed_run_locked?
  return Ironmon.with_statistics_battle_type(:wild) do
    ironmon_failure_original_wild_battle_core(*args)
  end
end

alias ironmon_failure_original_trainer_battle_core pbTrainerBattleCore
def pbTrainerBattleCore(*args)
  return Ironmon.blocked_battle_result if Ironmon.failed_run_locked?
  return Ironmon.with_statistics_battle_type(:trainer) do
    ironmon_failure_original_trainer_battle_core(*args)
  end
end
