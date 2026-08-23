#===============================================================================
# Ironmon map-scene integration
#===============================================================================

class Scene_Map
  alias ironmon_original_create_spritesets createSpritesets
  def createSpritesets
    Ironmon.pause_tracker_obtainability_for_map
    ironmon_original_create_spritesets
    Ironmon.finish_pending_reset
    Ironmon.refresh_pending_static_events
  end

  alias ironmon_original_update update
  def update
    return if Ironmon.show_pending_reset_notice
    return if Ironmon.handle_seed_import
    return if Ironmon.handle_reset_hotkey
    if Ironmon.failed_run_locked? && pbMapInterpreterRunning?
      return ironmon_original_update
    end
    return if Ironmon.handle_failed_run_state
    if Ironmon.active?
      Ironmon.enforce_party_limit
      pending = Ironmon.pivot_state.pending_pivot
      if pending
        Ironmon.resolve_pending_pivot(pending[:acquisition_id])
        return
      end
    end
    result = ironmon_original_update
    Ironmon.mark_tracker_obtainability_map_ready(self)
    return result
  end
end

class Game_Player
  alias ironmon_failure_original_update_command_new update_command_new
  def update_command_new
    return if Ironmon.failed_run_locked?
    return ironmon_failure_original_update_command_new
  end

  alias ironmon_failure_original_update_event_triggering update_event_triggering
  def update_event_triggering
    return if Ironmon.failed_run_locked?
    return ironmon_failure_original_update_event_triggering
  end
end
