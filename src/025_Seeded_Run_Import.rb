#===============================================================================
# Transactional tracker-owned seeded-run import
#===============================================================================

module Ironmon
  SEED_IMPORT_MINIMUM_SEED = 0
  SEED_IMPORT_MAXIMUM_SEED = 2_147_483_646
  SEED_IMPORT_MAXIMUM_TOKEN_ID_CHARACTERS = 128
  SEED_IMPORT_FINGERPRINT_PATTERN = /\A[0-9a-f]{64}\z/
  SEED_IMPORT_PAYLOAD_KEYS = [
    "token_id", "seed", "game_version", "ironmon_version", "data_mode",
    "configuration", "compatibility_fingerprint"
  ].freeze
  SEED_IMPORT_CONFIGURATION_KEYS = [
    "schema_version", "wild_policy", "trainer_policy", "unfusion_setting",
    "automatic_reset"
  ].freeze

  def self.tracker_seeded_run_export(envelope_run_id)
    attempt = current_run_attempt
    if !active? || !attempt || attempt["result"] != "active"
      raise TrackerLookupError.new(
        "seed_export_unavailable",
        "No active Ironmon attempt is available to export."
      )
    end
    if envelope_run_id.to_s != ensure_tracker_run_id.to_s
      raise TrackerLookupError.new(
        "seed_export_stale",
        "The seeded-run export targets a different active attempt."
      )
    end
    if @reset_in_progress || @tracker_reset_requested ||
       @tracker_seed_import_pending
      raise TrackerLookupError.new(
        "seed_export_transition",
        "A run transition is already pending."
      )
    end
    return tracker_run_reproduction_recipe
  end

  def self.request_tracker_seed_import(payload, envelope_run_id)
    token_id = payload.is_a?(Hash) ? payload["token_id"].to_s : ""
    error = validate_seed_import_request(payload, envelope_run_id)
    return seed_import_status("rejected", token_id, error) if error

    slot = $Trainer ? $Trainer.save_slot : nil
    path = existing_checkpoint_path(slot)
    return seed_import_status(
      "rejected", token_id,
      "No Ironmon starter checkpoint is available for this save slot."
    ) if !path

    begin
      checkpoint_data = SaveData.read_from_file(path)
    rescue Exception => e
      return seed_import_status(
        "rejected", token_id,
        "The Ironmon starter checkpoint could not be read: #{e.message}"
      )
    end

    @tracker_seed_import_pending = {
      "token_id" => token_id,
      "seed" => payload["seed"],
      "configuration" => Marshal.load(Marshal.dump(payload["configuration"])),
      "compatibility_fingerprint" => payload["compatibility_fingerprint"],
      "checkpoint_data" => checkpoint_data,
      "source_run_id" => ensure_tracker_run_id
    }
    return seed_import_status(
      "accepted", token_id,
      "Seeded-run import was accepted for guarded queueing."
    )
  end

  def self.validate_seed_import_request(payload, envelope_run_id)
    return "Seeded-run import payload is missing." if !payload.is_a?(Hash)
    return "Seeded-run import payload has unsupported fields." if
      !payload.keys.all? { |key| key.is_a?(String) } ||
      payload.keys.sort != SEED_IMPORT_PAYLOAD_KEYS.sort
    return "No active Ironmon attempt is available to replace." if
      !active? || !current_run_attempt ||
      current_run_attempt["result"] != "active"
    return "The seeded-run request targets a different active attempt." if
      envelope_run_id.to_s != ensure_tracker_run_id.to_s
    return "Another run transition is already pending." if
      @reset_in_progress || @tracker_reset_requested ||
      @tracker_seed_import_pending

    token_id = payload["token_id"]
    return "Seeded-run token identity is invalid." if
      !token_id.is_a?(String) || token_id.strip.empty? ||
      token_id.length > SEED_IMPORT_MAXIMUM_TOKEN_ID_CHARACTERS
    seed = payload["seed"]
    return "Seeded-run seed is outside the supported range." if
      !seed.is_a?(Integer) || seed < SEED_IMPORT_MINIMUM_SEED ||
      seed > SEED_IMPORT_MAXIMUM_SEED
    return "Seeded-run game version is incompatible." if
      payload["game_version"] != tracker_game_version
    return "Seeded-run Ironmon version is incompatible." if
      payload["ironmon_version"] != VERSION
    return "Seeded-run game-data mode is incompatible." if
      payload["data_mode"] != tracker_data_mode

    configuration_error = validate_seed_import_configuration(
      payload["configuration"]
    )
    return configuration_error if configuration_error
    fingerprint = payload["compatibility_fingerprint"]
    return "Seeded-run compatibility fingerprint is invalid." if
      !fingerprint.is_a?(String) ||
      fingerprint !~ SEED_IMPORT_FINGERPRINT_PATTERN
    begin
      return "Seeded-run installation compatibility does not match." if
        tracker_compatibility_fingerprint != fingerprint
    rescue Exception => e
      return "Installed seeded-run compatibility could not be verified: #{e.message}"
    end
    return nil
  end

  def self.validate_seed_import_configuration(configuration)
    return "Seeded-run configuration is missing." if
      !configuration.is_a?(Hash)
    return "Seeded-run configuration has unsupported fields." if
      !configuration.keys.all? { |key| key.is_a?(String) } ||
      configuration.keys.sort != SEED_IMPORT_CONFIGURATION_KEYS.sort
    return "Seeded-run configuration schema is unsupported." if
      configuration["schema_version"] != Configuration::SCHEMA_VERSION
    return "Seeded-run wild policy is unsupported." if
      !Configuration::POLICY_IDS.include?(
        configuration["wild_policy"].to_s.to_sym
      )
    return "Seeded-run trainer policy is unsupported." if
      !Configuration::POLICY_IDS.include?(
        configuration["trainer_policy"].to_s.to_sym
      )
    return "Seeded-run unfusion setting is unsupported." if
      !Configuration::UNFUSION_SETTING_IDS.include?(
        configuration["unfusion_setting"].to_s.to_sym
      )
    return "Seeded-run automatic-reset value is invalid." if
      ![true, false].include?(configuration["automatic_reset"])
    return nil
  end

  def self.handle_seed_import
    return false if !@tracker_seed_import_pending || @reset_in_progress
    return false if !$game_temp || $game_temp.message_window_showing
    return false if pbMapInterpreterRunning?
    return false if !$game_player || $game_player.moving?
    return false if tracker_battle_id
    start_seed_import
    return true
  end

  def self.start_seed_import
    pending = @tracker_seed_import_pending
    return false if !pending || @reset_in_progress
    rollback_data = Marshal.load(Marshal.dump(SaveData.compile_save_hash))
    begin
      completion = stage_run_completion(:abandoned)
      raise "the active attempt ledger is unavailable" if
        !completion["completed"]
      completed_recipe = completion["completed_recipe"]
      ledger_snapshot = completion["ledger_snapshot"]
      @seed_import_transaction = pending.merge({
        "rollback_data" => rollback_data,
        "completed_recipe" => completed_recipe,
        "ledger_snapshot" => ledger_snapshot,
        "source_sequence" => completion["source_sequence"],
        "save_slot" => ($Trainer ? $Trainer.save_slot : nil)
      })
      @tracker_seed_import_pending = nil
      @seed_import_in_progress = true
      @reset_in_progress = true
      @reset_save_slot = @seed_import_transaction["save_slot"]
      $scene = IronmonCheckpointLoadScene.new(
        pending["checkpoint_data"], pending["configuration"], ledger_snapshot
      )
      return true
    rescue Exception => e
      @tracker_seed_import_pending = nil
      rollback_error = rollback_seed_import(rollback_data)
      message = "Seeded-run import could not start: #{e.message}"
      message += "; restoring the active attempt also failed: #{rollback_error}" if
        rollback_error
      begin
        send_seed_import_status(
          pending["token_id"], "failed", message, pending["source_run_id"]
        )
      ensure
        @seed_import_transaction = nil
        @seed_import_in_progress = false
        @reset_save_slot = nil
        @reset_in_progress = false
      end
      return false
    end
  end

  def self.stage_seed_import_ledger(snapshot, completed_recipe)
    return stage_run_ledger_completion(
      snapshot, :abandoned, completed_recipe
    )
  end

  def self.finish_seed_import
    transaction = @seed_import_transaction
    return false if !transaction
    begin
      Ironmon.configuration = transaction["configuration"]
      generated = apply_preset(
        :seed_import, transaction["seed"], false,
        transaction["compatibility_fingerprint"]
      )
      raise(@seed_import_error_message || generation_error_message) if !generated
      $PokemonGlobal.ironmon_tracker_sequence = 0 if $PokemonGlobal
      if transaction["save_slot"]
        $Trainer.save_slot = transaction["save_slot"]
        raise "the imported run could not be saved" if
          !Game.save(transaction["save_slot"])
      end

      completed_recipe = transaction["completed_recipe"]
      if completed_recipe
        tracker_connection.send_event(
          "run_completed", completed_recipe, transaction["source_run_id"],
          transaction["source_sequence"] + 1
        )
      end
      start_tracker_run
      send_seed_import_status(
        transaction["token_id"], "started",
        "The imported seeded run has started."
      )
      @reset_notice = :seed_import_success
      return true
    rescue Exception => e
      return fail_seed_import_transaction(e)
    ensure
      @seed_import_transaction = nil
      @seed_import_in_progress = false
      @reset_save_slot = nil
      @reset_in_progress = false
    end
  end

  def self.rollback_seed_import(save_data)
    return restore_live_save_snapshot(save_data)
  end

  def self.fail_seed_import_transaction(error)
    transaction = @seed_import_transaction
    rollback_error = if transaction
                       rollback_seed_import(transaction["rollback_data"])
                     else
                       "the seeded-run transaction is unavailable"
                     end
    if transaction
      message = if rollback_error
                  "Seeded-run import failed, and restoring the active attempt " +
                    "also failed: #{error.message}; rollback: #{rollback_error}"
                else
                  "Seeded-run import failed without replacing the active " +
                    "attempt: #{error.message}"
                end
      send_seed_import_status(
        transaction["token_id"], "failed", message,
        transaction["source_run_id"]
      )
    end
    @reset_notice = {
      :type => :seed_import_failed,
      :message => error.message.to_s,
      :rollback_error => rollback_error
    }
    @seed_import_transaction = nil
    @seed_import_in_progress = false
    @reset_save_slot = nil
    @reset_in_progress = false
    return false
  end

  def self.send_seed_import_status(token_id, status, message, run_id = nil)
    tracker_connection.send_event("seeded_run_import_status", {
      "token_id" => token_id,
      "status" => status,
      "message" => message
    }, run_id)
    return true
  rescue Exception => e
    echoln "Ironmon seeded-run status failed safely: #{e.message}"
    return false
  end

  def self.seed_import_status(status, token_id, message)
    return {
      "token_id" => token_id,
      "status" => status,
      "message" => message
    }
  end
end
