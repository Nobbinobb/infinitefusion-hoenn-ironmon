#===============================================================================
# Ironmon game-owned run lifecycle and per-save attempt ledger
#===============================================================================

module Ironmon
  RUN_LEDGER_SCHEMA_VERSION = 1
  RUN_RESULTS = ["active", "lost", "won", "abandoned"].freeze
  RUN_COMPLETED_RESULTS = ["lost", "won", "abandoned"].freeze
  RUN_UPTIME_UNITS_PER_SECOND = 1_000_000.0

  def self.default_run_ledger
    return {
      "schema_version" => RUN_LEDGER_SCHEMA_VERSION,
      "next_attempt_number" => 1,
      "attempts_started" => 0,
      "attempts_lost" => 0,
      "attempts_won" => 0,
      "attempts_abandoned" => 0,
      "current_attempt" => nil,
      "last_completed_attempt" => nil,
      "last_completed_recipe" => nil
    }
  end

  def self.run_ledger
    return default_run_ledger if !$PokemonGlobal
    stored = $PokemonGlobal.ironmon_run_ledger
    if stored.is_a?(Hash) &&
       stored["schema_version"] == RUN_LEDGER_SCHEMA_VERSION
      return stored
    end
    normalized = normalize_run_ledger(stored)
    $PokemonGlobal.ironmon_run_ledger = normalized
    return normalized
  end

  def self.normalize_run_ledger(value)
    ledger = default_run_ledger
    return ledger if !value.is_a?(Hash)
    ["next_attempt_number", "attempts_started", "attempts_lost",
     "attempts_won", "attempts_abandoned"].each do |key|
      number = value[key].to_i
      number = 0 if number < 0
      ledger[key] = number
    end
    ledger["next_attempt_number"] = 1 if ledger["next_attempt_number"] < 1
    ledger["current_attempt"] = normalize_run_attempt(value["current_attempt"])
    ledger["last_completed_attempt"] =
      normalize_run_attempt(value["last_completed_attempt"], false)
    ledger["last_completed_recipe"] = value["last_completed_recipe"] if
      value["last_completed_recipe"].is_a?(Hash)
    return ledger
  end

  def self.normalize_run_attempt(value, allow_active = true)
    return nil if !value.is_a?(Hash)
    result = value["result"].to_s
    allowed = allow_active ? RUN_RESULTS : RUN_COMPLETED_RESULTS
    return nil if !allowed.include?(result)
    number = value["attempt_number"].to_i
    return nil if number < 1
    seconds = value["active_seconds"].to_f
    seconds = 0.0 if seconds < 0.0
    return {
      "attempt_number" => number,
      "run_id" => value["run_id"].to_s,
      "seed" => value["seed"].to_i,
      "result" => result,
      "active_seconds" => seconds,
      "statistics" => if respond_to?(:normalize_attempt_statistics)
                        normalize_attempt_statistics(value["statistics"])
                      else
                        value["statistics"]
                      end
    }
  end

  def self.current_run_attempt
    return nil if !$PokemonGlobal
    return run_ledger["current_attempt"]
  end

  def self.current_attempt_number
    attempt = current_run_attempt
    return attempt ? attempt["attempt_number"] : nil
  end

  def self.run_ledger_snapshot
    tick_active_run_duration
    return Marshal.load(Marshal.dump(run_ledger))
  end

  def self.restore_run_ledger(snapshot)
    return if !$PokemonGlobal
    $PokemonGlobal.ironmon_run_ledger = normalize_run_ledger(snapshot)
    attempt = current_run_attempt
    if attempt
      $PokemonGlobal.ironmon_run_id = attempt["run_id"]
      result = attempt["result"]
      $PokemonGlobal.ironmon_run_result = result == "active" ? nil : result
    end
    resume_run_duration
  end

  def self.begin_run_attempt(seed)
    return nil if !$PokemonGlobal
    ledger = run_ledger
    return nil if ledger["current_attempt"] &&
                  ledger["current_attempt"]["result"] == "active"
    number = ledger["next_attempt_number"]
    $PokemonGlobal.ironmon_seed = seed
    $PokemonGlobal.ironmon_run_id = new_tracker_run_id
    attempt = {
      "attempt_number" => number,
      "run_id" => $PokemonGlobal.ironmon_run_id,
      "seed" => seed.to_i,
      "result" => "active",
      "active_seconds" => 0.0,
      "statistics" => if respond_to?(:default_attempt_statistics)
                        default_attempt_statistics
                      else
                        nil
                      end
    }
    ledger["current_attempt"] = attempt
    ledger["next_attempt_number"] = number + 1
    ledger["attempts_started"] += 1
    $PokemonGlobal.ironmon_run_result = nil
    reset_failed_run_runtime_state
    resume_run_duration
    return attempt
  end

  def self.complete_run(result)
    return false if !$PokemonGlobal
    result_name = result.to_s
    return false if !RUN_COMPLETED_RESULTS.include?(result_name)
    attempt = current_run_attempt
    return false if !attempt || attempt["result"] != "active"
    tick_active_run_duration
    finalize_attempt_statistics if respond_to?(:finalize_attempt_statistics)
    attempt["result"] = result_name
    ledger = run_ledger
    ledger["attempts_#{result_name}"] += 1
    ledger["last_completed_attempt"] = Marshal.load(Marshal.dump(attempt))
    $PokemonGlobal.ironmon_run_result = result_name
    @run_duration_anchor = nil
    @run_duration_identity = nil
    publish_run_completion if respond_to?(:publish_run_completion)
    return true
  end

  def self.failed_run_locked?
    return false if !active?
    attempt = current_run_attempt
    return attempt && attempt["result"] == "lost"
  end

  def self.reset_failed_run_runtime_state
    @failed_run_notice_id = nil
    @automatic_reset_attempted_id = nil
  end

  def self.handle_failed_run_state
    return false if !failed_run_locked?
    attempt = current_run_attempt
    run_id = attempt["run_id"]
    if configuration.automatic_reset &&
       @automatic_reset_attempted_id != run_id
      @automatic_reset_attempted_id = run_id
      start_checkpoint_reset(true)
      return true
    end
    if @failed_run_notice_id != run_id
      @failed_run_notice_id = run_id
      pbMessage(_INTL("This Ironmon attempt has ended. Press F7 to begin a new attempt."))
    end
    return true
  end

  def self.block_failed_run_action
    return false if !failed_run_locked?
    pbMessage(_INTL("This Ironmon attempt has ended. Press F7 to begin a new attempt."))
    return true
  end

  def self.blocked_battle_result
    outcome_variable = $PokemonTemp.battleRules["outcomeVar"] || 1
    pbSet(outcome_variable, 5)
    $PokemonTemp.clearBattleRules
    block_failed_run_action
    return 5
  end

  def self.run_uptime_seconds
    return System.uptime.to_f / RUN_UPTIME_UNITS_PER_SECOND
  end

  def self.resume_run_duration
    attempt = current_run_attempt
    if attempt && attempt["result"] == "active"
      @run_duration_identity = attempt["run_id"]
      @run_duration_anchor = run_uptime_seconds
    else
      @run_duration_identity = nil
      @run_duration_anchor = nil
    end
  end

  def self.tick_active_run_duration
    attempt = current_run_attempt
    if !attempt || attempt["result"] != "active"
      @run_duration_identity = nil
      @run_duration_anchor = nil
      return
    end
    now = run_uptime_seconds
    if @run_duration_identity != attempt["run_id"] || !@run_duration_anchor
      @run_duration_identity = attempt["run_id"]
      @run_duration_anchor = now
      return
    end
    elapsed = now - @run_duration_anchor
    attempt["active_seconds"] += elapsed if elapsed > 0.0
    @run_duration_anchor = now
  end
end

module Game
  class << self
    alias ironmon_run_lifecycle_original_save save
    def save(slot = nil, auto = false, safe: false)
      Ironmon.tick_active_run_duration
      return ironmon_run_lifecycle_original_save(slot, auto, safe: safe)
    end

    alias ironmon_run_lifecycle_original_load load
    def load(save_data)
      result = ironmon_run_lifecycle_original_load(save_data)
      Ironmon.run_ledger if $PokemonGlobal
      Ironmon.resume_run_duration
      return result
    end
  end
end

module Graphics
  class << self
    alias ironmon_run_lifecycle_original_update update
    def update
      ironmon_run_lifecycle_original_update
      Ironmon.tick_active_run_duration
    end
  end
end
