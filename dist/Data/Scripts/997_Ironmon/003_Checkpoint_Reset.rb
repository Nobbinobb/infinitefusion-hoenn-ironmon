#===============================================================================
# Ironmon pre-starter checkpoint and F7 reset flow
#===============================================================================

module Ironmon
  def self.ensure_checkpoint_id
    return if !$PokemonGlobal
    if !$PokemonGlobal.ironmon_checkpoint_id
      $PokemonGlobal.ironmon_checkpoint_id = rand(2_147_483_647)
    end
  end

  def self.checkpoint_path
    return nil if !$PokemonGlobal || !$PokemonGlobal.ironmon_checkpoint_id
    filename = "IronmonCheckpoint_#{$PokemonGlobal.ironmon_checkpoint_id}.rxdata"
    return File.join(SaveData::SAVE_DIR, filename)
  end

  def self.capture_checkpoint
    return if !active?
    ensure_checkpoint_id
    path = checkpoint_path
    return if !path
    begin
      SaveData.save_to_file(path)
      echoln "Ironmon checkpoint created: #{path}"
    rescue Exception => e
      echoln "Ironmon checkpoint could not be created: #{e.message}"
    end
  end

  def self.finish_pending_reset
    return if !@reset_in_progress
    begin
      apply_preset
      if @reset_save_slot
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

  def self.show_pending_reset_notice
    return false if !@reset_notice
    notice = @reset_notice
    @reset_notice = nil
    if notice == :save_failed
      pbMessage(_INTL("The run restarted, but the save slot could not be updated. Please save manually."))
    else
      pbMessage(_INTL("A new Ironmon run has been generated. Choose your starter."))
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

    path = checkpoint_path
    if !path || !File.file?(path)
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
    remember_current_seed_for_reset
    @reset_in_progress = true
    @reset_save_slot = $Trainer.save_slot
    $scene = IronmonCheckpointLoadScene.new(checkpoint_data,
                                            configuration_snapshot)
    return true
  end
end

class IronmonCheckpointLoadScene
  def initialize(save_data, configuration_snapshot)
    @save_data = save_data
    @configuration_snapshot = configuration_snapshot
  end

  def main
    SaveData.mark_values_as_unloaded
    Game.load(@save_data)
    Ironmon.configuration = @configuration_snapshot
  end
end
