#===============================================================================
# Ironmon pre-starter checkpoint and F7 reset flow
#===============================================================================

module Ironmon
  CHECKPOINT_BASENAME = "IronmonCheckpoint"

  def self.ensure_checkpoint_id
    return if !$PokemonGlobal
    if !$PokemonGlobal.ironmon_checkpoint_id
      $PokemonGlobal.ironmon_checkpoint_id = rand(2_147_483_647)
    end
  end

  def self.checkpoint_slot_name(slot)
    return "Unsaved" if !slot || slot.to_s.empty?
    name = slot.to_s.gsub(/[^0-9A-Za-z_-]+/, "_")
    name.gsub!(/^_+|_+$/, "")
    return name.empty? ? "Unsaved" : name
  end

  def self.checkpoint_path_for_slot(slot)
    filename = "#{CHECKPOINT_BASENAME}_#{checkpoint_slot_name(slot)}.rxdata"
    return File.join(SaveData::SAVE_DIR, filename)
  end

  def self.legacy_checkpoint_path
    return nil if !$PokemonGlobal || !$PokemonGlobal.ironmon_checkpoint_id
    filename = "#{CHECKPOINT_BASENAME}_#{$PokemonGlobal.ironmon_checkpoint_id}.rxdata"
    return File.join(SaveData::SAVE_DIR, filename)
  end

  def self.checkpoint_path
    slot = $Trainer ? $Trainer.save_slot : nil
    return checkpoint_path_for_slot(slot)
  end

  def self.existing_checkpoint_path(slot = nil)
    path = checkpoint_path_for_slot(slot)
    return path if File.file?(path)
    legacy_path = legacy_checkpoint_path
    return legacy_path if legacy_path && File.file?(legacy_path)
    return nil
  end

  def self.replace_checkpoint_file(path)
    temporary_path = "#{path}.tmp"
    begin
      SaveData.save_to_file(temporary_path)
      File.delete(path) if File.file?(path)
      File.rename(temporary_path, path)
    ensure
      File.delete(temporary_path) if File.file?(temporary_path)
    end
  end

  def self.copy_checkpoint_file(source, destination)
    return true if source == destination
    temporary_path = "#{destination}.tmp"
    begin
      File.open(source, "rb") do |input|
        File.open(temporary_path, "wb") { |output| output.write(input.read) }
      end
      File.delete(destination) if File.file?(destination)
      File.rename(temporary_path, destination)
      return true
    ensure
      File.delete(temporary_path) if File.file?(temporary_path)
    end
  end

  def self.migrate_checkpoint_to_slot(slot, previous_slot = nil)
    return if !slot
    source = existing_checkpoint_path(previous_slot)
    return if !source
    destination = checkpoint_path_for_slot(slot)
    return if source == destination
    begin
      copy_checkpoint_file(source, destination)
      pending_path = checkpoint_path_for_slot(nil)
      legacy_path = legacy_checkpoint_path
      if source == pending_path || (legacy_path && source == legacy_path)
        File.delete(source) if File.file?(source)
      end
      echoln "Ironmon checkpoint assigned to save slot #{slot}: #{destination}"
    rescue Exception => e
      echoln "Ironmon checkpoint could not be assigned to save slot #{slot}: #{e.message}"
    end
  end

  def self.capture_checkpoint
    return if !active?
    ensure_checkpoint_id
    path = checkpoint_path
    return if !path
    begin
      replace_checkpoint_file(path)
      echoln "Ironmon checkpoint created: #{path}"
    rescue Exception => e
      echoln "Ironmon checkpoint could not be created: #{e.message}"
    end
  end

  def self.finish_pending_reset
    return if !@reset_in_progress
    return finish_seed_import if @seed_import_in_progress
    transaction = @reset_transaction
    begin
      raise "the reset transaction is unavailable" if !transaction
      generated = apply_preset(:f7_reset, nil, false)
      raise generation_error_message if !generated
      save_slot = transaction["save_slot"]
      if save_slot
        $Trainer.save_slot = save_slot
        raise "the new attempt could not be saved" if !Game.save(save_slot)
      end
      @reset_notice = transaction["automatic"] ? :automatic_success : :success
      complete_reset_tracker_transition(transaction)
      return true
    rescue Exception => e
      return fail_checkpoint_reset_transaction(e)
    ensure
      @reset_save_slot = nil
      @reset_transaction = nil
      @reset_in_progress = false
    end
  end

  def self.complete_reset_tracker_transition(transaction)
    recipe = transaction["completed_recipe"]
    if recipe
      begin
        tracker_connection.send_event(
          "run_completed", recipe, transaction["source_run_id"],
          transaction["source_sequence"] + 1
        )
      rescue Exception => e
        echoln "Ironmon reset completion could not be published: #{e.message}"
      end
    end
    begin
      start_tracker_run
    rescue Exception => e
      echoln "Ironmon reset tracker start failed safely: #{e.message}"
    end
  end

  def self.restore_live_save_snapshot(save_data)
    SaveData.mark_values_as_unloaded
    Game.load(save_data)
    return nil
  rescue Exception => e
    return e.message.to_s
  end

  def self.fail_checkpoint_reset_transaction(error)
    transaction = @reset_transaction
    rollback_error = if transaction
                       @seed_to_avoid = transaction["seed_to_avoid"]
                       restore_live_save_snapshot(transaction["rollback_data"])
                     else
                       "the reset transaction is unavailable"
                     end
    @reset_notice = {
      :type => :reset_failed,
      :message => error.message.to_s,
      :rollback_error => rollback_error
    }
    return false
  end

  def self.fail_pending_reset(error)
    if @seed_import_in_progress && respond_to?(:fail_seed_import_transaction)
      return fail_seed_import_transaction(error)
    end
    return fail_checkpoint_reset_transaction(error)
  ensure
    @reset_save_slot = nil
    @reset_transaction = nil if !@seed_import_in_progress
    @reset_in_progress = false
  end

  def self.with_checkpoint_reset_load
    @checkpoint_reset_loading = true
    return yield
  ensure
    @checkpoint_reset_loading = false
  end

  def self.checkpoint_reset_loading?
    return @checkpoint_reset_loading == true
  end

  def self.show_pending_reset_notice
    return false if !@reset_notice
    notice = @reset_notice
    @reset_notice = nil
    if notice.is_a?(Hash) && notice[:type] == :seed_import_failed
      message = if notice[:rollback_error]
                  _INTL(
                    "The seeded run could not be imported, and restoring the current attempt also failed. {1} Rollback: {2}",
                    notice[:message], notice[:rollback_error]
                  )
                else
                  _INTL(
                    "The seeded run could not be imported. The current attempt is still active. {1}",
                    notice[:message]
                  )
                end
      pbMessage(message)
    elsif notice.is_a?(Hash) && notice[:type] == :reset_failed
      message = if notice[:rollback_error]
                  _INTL(
                    "The Ironmon reset failed, and restoring the previous attempt also failed. {1} Rollback: {2}",
                    notice[:message], notice[:rollback_error]
                  )
                else
                  _INTL(
                    "The Ironmon reset failed. The previous attempt was restored. {1}",
                    notice[:message]
                  )
                end
      pbMessage(message)
    elsif notice == :seed_import_success
      number = current_attempt_number
      pbMessage(_INTL(
        "Ironmon attempt {1} was created from the shared seed. Choose your starter.",
        number
      ))
    elsif notice == :automatic_success
      return true
    else
      number = current_attempt_number
      pbMessage(_INTL("Ironmon attempt {1} has been generated. Choose your starter.", number))
    end
    return true
  end

  def self.handle_reset_hotkey
    return false if @reset_in_progress
    keyboard_requested = Input.trigger?(RESET_KEY)
    tracker_requested = @tracker_reset_requested == true
    return false if !active? || (!keyboard_requested && !tracker_requested)
    return false if !$game_temp || $game_temp.message_window_showing
    return false if pbMapInterpreterRunning?
    return false if !$game_player || $game_player.moving?

    @tracker_reset_requested = false
    warning = _INTL("Restart this Ironmon run from the starter selection with a new randomization? The current run will be replaced.")
    return true if !pbConfirmMessage(warning)

    start_checkpoint_reset(false)
    return true
  end

  def self.request_tracker_reset
    return false if !active? || @reset_in_progress || @tracker_seed_import_pending
    @tracker_reset_requested = true
    return true
  end

  def self.start_checkpoint_reset(automatic)
    return false if @reset_in_progress

    slot = $Trainer ? $Trainer.save_slot : nil
    path = existing_checkpoint_path(slot)
    if !path
      pbMessage(_INTL("No Ironmon starter checkpoint exists yet. Start one new Ironmon run and reach the starter selection once to create it."))
      return false
    end

    begin
      checkpoint_data = SaveData.read_from_file(path)
    rescue Exception => e
      pbMessage(_INTL("The Ironmon checkpoint could not be loaded: {1}", e.message))
      return false
    end

    rollback_data = Marshal.load(Marshal.dump(SaveData.compile_save_hash))
    seed_to_avoid = @seed_to_avoid
    begin
      completion = stage_run_completion(:abandoned)
      configuration_snapshot = Ironmon.configuration_snapshot
      remember_current_seed_for_reset
      @reset_transaction = {
        "rollback_data" => rollback_data,
        "completed_recipe" => completion["completed_recipe"],
        "source_run_id" => completion["source_run_id"],
        "source_sequence" => completion["source_sequence"],
        "save_slot" => slot,
        "automatic" => automatic,
        "seed_to_avoid" => seed_to_avoid
      }
      @reset_in_progress = true
      @reset_save_slot = slot
      $scene = IronmonCheckpointLoadScene.new(
        checkpoint_data, configuration_snapshot,
        completion["ledger_snapshot"]
      )
      return true
    rescue Exception => e
      @seed_to_avoid = seed_to_avoid
      rollback_error = restore_live_save_snapshot(rollback_data)
      message = if rollback_error
                  _INTL(
                    "The Ironmon reset could not start, and restoring the previous attempt also failed. {1} Rollback: {2}",
                    e.message, rollback_error
                  )
                else
                  _INTL(
                    "The Ironmon reset could not start. The previous attempt was restored. {1}",
                    e.message
                  )
                end
      pbMessage(message)
      @reset_transaction = nil
      @reset_save_slot = nil
      @reset_in_progress = false
      return false
    end
  end
end

Ironmon.register_game_save_hook(
  :checkpoint,
  proc { |_slot, _auto, _safe| $Trainer ? $Trainer.save_slot : nil },
  proc do |slot, auto, _safe, result, previous_slot|
    if result && !auto && Ironmon.active?
      saved_slot = $Trainer ? $Trainer.save_slot : slot
      Ironmon.migrate_checkpoint_to_slot(saved_slot, previous_slot)
    end
  end
)

class IronmonCheckpointLoadScene
  def initialize(save_data, configuration_snapshot, ledger_snapshot)
    @save_data = save_data
    @configuration_snapshot = configuration_snapshot
    @ledger_snapshot = ledger_snapshot
  end

  def main
    begin
      SaveData.mark_values_as_unloaded
      Ironmon.with_checkpoint_reset_load { Game.load(@save_data) }
      Ironmon.configuration = @configuration_snapshot
      Ironmon.restore_run_ledger(@ledger_snapshot)
    rescue Exception => e
      Ironmon.fail_pending_reset(e)
    end
  end
end
