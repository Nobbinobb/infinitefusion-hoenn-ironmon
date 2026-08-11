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
    begin
      generated = apply_preset(:f7_reset)
      if !generated
        @reset_notice = :generation_failed
      elsif @reset_save_slot
        $Trainer.save_slot = @reset_save_slot
        saved = Game.save(@reset_save_slot)
        @reset_notice = saved ? :success : :save_failed
      else
        @reset_notice = :success
      end
    ensure
      @reset_save_slot = nil
      @reset_in_progress = false
    end
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
    if notice == :generation_failed
      pbMessage(generation_error_message)
    elsif notice == :save_failed
      pbMessage(_INTL("The run restarted, but the save slot could not be updated. Please save manually."))
    else
      number = current_attempt_number
      pbMessage(_INTL("Ironmon attempt {1} has been generated. Choose your starter.", number))
    end
    return true
  end

  def self.handle_reset_hotkey
    return false if @reset_in_progress
    return false if !active? || !Input.trigger?(RESET_KEY)
    return false if !$game_temp || $game_temp.message_window_showing
    return false if pbMapInterpreterRunning?
    return false if !$game_player || $game_player.moving?

    warning = _INTL("Restart this Ironmon run from the starter selection with a new randomization? The current run will be replaced.")
    return true if !pbConfirmMessage(warning)

    slot = $Trainer ? $Trainer.save_slot : nil
    path = existing_checkpoint_path(slot)
    if !path
      pbMessage(_INTL("No Ironmon starter checkpoint exists yet. Start one new Ironmon run and reach the starter selection once to create it."))
      return true
    end

    begin
      checkpoint_data = SaveData.read_from_file(path)
    rescue Exception => e
      pbMessage(_INTL("The Ironmon checkpoint could not be loaded: {1}", e.message))
      return true
    end

    configuration_snapshot = Ironmon.configuration_snapshot
    Ironmon.complete_run(:abandoned)
    ledger_snapshot = Ironmon.run_ledger_snapshot
    remember_current_seed_for_reset
    @reset_in_progress = true
    @reset_save_slot = $Trainer.save_slot
    $scene = IronmonCheckpointLoadScene.new(
      checkpoint_data, configuration_snapshot, ledger_snapshot
    )
    return true
  end
end

module Game
  class << self
    alias ironmon_checkpoint_original_save save
    def save(slot = nil, auto = false, safe: false)
      previous_slot = $Trainer ? $Trainer.save_slot : nil
      result = ironmon_checkpoint_original_save(slot, auto, safe: safe)
      if result && !auto && Ironmon.active?
        saved_slot = $Trainer ? $Trainer.save_slot : slot
        Ironmon.migrate_checkpoint_to_slot(saved_slot, previous_slot)
      end
      return result
    end
  end
end

class IronmonCheckpointLoadScene
  def initialize(save_data, configuration_snapshot, ledger_snapshot)
    @save_data = save_data
    @configuration_snapshot = configuration_snapshot
    @ledger_snapshot = ledger_snapshot
  end

  def main
    SaveData.mark_values_as_unloaded
    Ironmon.with_checkpoint_reset_load { Game.load(@save_data) }
    Ironmon.configuration = @configuration_snapshot
    Ironmon.restore_run_ledger(@ledger_snapshot)
  end
end
