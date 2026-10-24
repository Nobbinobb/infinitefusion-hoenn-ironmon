module Ironmon
  FIRST_ALPHA_COMPLETION_ENABLED = true
  FIRST_ALPHA_COMPLETION_MILESTONE = "first_alpha_wattson"
  FIRST_ALPHA_FINAL_TRAINER_TYPE = :LEADER_Wattson

  def self.first_alpha_completion_enabled?
    return FIRST_ALPHA_COMPLETION_ENABLED
  end

  def self.complete_first_alpha_battle(battle, decision)
    return false if !first_alpha_completion_enabled? || !active? || !compatible?
    return false if Settings::GAME_ID != :IF_HOENN || decision != 1
    return false if !battle || !battle.internalBattle || !battle.trainerBattle?
    return false if battle.respond_to?(:debug) && battle.debug
    return false if defined?(PokeBattle_BattlePlayerModule) && battle.is_a?(PokeBattle_BattlePlayerModule)
    opponents = battle.opponent
    return false if !opponents.is_a?(Array) || opponents.length != 1
    return false if boss_trainer_type(opponents[0]) != FIRST_ALPHA_FINAL_TRAINER_TYPE
    attempt = current_run_attempt
    return false if !attempt || attempt["result"] != "active"
    complete_statistics_battle if respond_to?(:complete_statistics_battle)
    attempt["completion_milestone"] = FIRST_ALPHA_COMPLETION_MILESTONE
    attempt["completion_trainer_class_ids"] = [FIRST_ALPHA_FINAL_TRAINER_TYPE.to_s]
    return false if !complete_run(:won)
    @first_alpha_completion_save_run_id = attempt["run_id"]
    return true
  end

  def self.save_first_alpha_completion
    started_save = false
    pending = @first_alpha_completion_save_run_id
    return false if !pending || !$Trainer || !$PokemonGlobal || !$Trainer.save_slot || @saving_first_alpha_completion
    attempt = current_run_attempt
    if !attempt || attempt["run_id"] != pending || attempt["result"] != "won"
      @first_alpha_completion_save_run_id = nil
      return false
    end
    return false if !$scene || !$scene.is_a?(Scene_Map)
    return false if $game_temp && $game_temp.in_battle
    interpreter = $game_system && $game_system.map_interpreter
    return false if interpreter && interpreter.running?
    now = run_uptime_seconds
    return false if @first_alpha_completion_retry_at && now < @first_alpha_completion_retry_at
    @first_alpha_completion_retry_at = now + 1.0
    @saving_first_alpha_completion = true
    started_save = true
    finalize_first_alpha_rewards(attempt)
    return false if !Game.save($Trainer.save_slot, false, safe: true)
    @first_alpha_completion_save_run_id = nil
    return true
  rescue StandardError => error
    echoln "Ironmon could not save the completed Alpha run: #{error.message}"
    return false
  ensure
    @saving_first_alpha_completion = false if started_save
  end

  def self.finalize_first_alpha_rewards(attempt)
    statistics = attempt["statistics"]
    return if !statistics.is_a?(Hash)
    badges = [$Trainer.badge_count - statistics["starting_badges"].to_i, 0].max
    return if badges <= statistics["badges_earned"].to_i
    statistics["badges_earned"] = badges
    run_ledger["last_completed_attempt"] = Marshal.load(Marshal.dump(attempt))
    publish_run_completion
  end
end

module IronmonAlphaCompletionBattleHooks
  def pbEndOfBattle(*args)
    decision = @decision
    result = super
    Ironmon.complete_first_alpha_battle(self, decision)
    return result
  end
end

# The manifest loads this wrapper last so all battle facts commit before run completion.
PokeBattle_Battle.prepend(IronmonAlphaCompletionBattleHooks)

Ironmon.register_graphics_update_hook(:first_alpha_completion, proc { Ironmon.save_first_alpha_completion })
