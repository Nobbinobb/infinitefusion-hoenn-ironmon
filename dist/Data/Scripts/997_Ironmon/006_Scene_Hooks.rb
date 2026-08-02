#===============================================================================
# Ironmon map-scene integration
#===============================================================================

class Scene_Map
  alias ironmon_original_create_spritesets createSpritesets
  def createSpritesets
    ironmon_original_create_spritesets
    Ironmon.finish_pending_reset
    Ironmon.refresh_pending_static_events
  end

  alias ironmon_original_update update
  def update
    return if Ironmon.show_pending_reset_notice
    return if Ironmon.handle_reset_hotkey
    ironmon_original_update
  end
end
