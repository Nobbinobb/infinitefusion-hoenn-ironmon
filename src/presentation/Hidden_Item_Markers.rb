#===============================================================================
# Ironmon visible markers for authored hidden items
#===============================================================================

module Ironmon
  def self.hidden_area_item_marker_visible?(event, entry_id)
    return false if !active? || !event || !event.active?
    components = entry_id.to_s.split(":", 3)
    return false if components.length == 3 && $game_self_switches &&
      $game_self_switches[[components[1].to_i, components[2].to_i, "A"]]
    return true
  end
end

class IronmonHiddenItemMarker
  def initialize(event, entry_id, viewport)
    @event = event
    @entry_id = entry_id
    @sprite = Sprite.new(viewport)
    @sprite.bitmap = Bitmap.new(32, 32)
    @sprite.ox = 16
    @sprite.oy = 34
    draw_marker
    @disposed = false
    update
  end

  def draw_marker
    bitmap = @sprite.bitmap
    bitmap.clear
    bitmap.font.name = "Arial"
    bitmap.font.size = 28
    bitmap.font.bold = true
    bitmap.font.color = Color.new(24, 24, 24, 220)
    bitmap.draw_text(1, 1, 32, 32, "!", 1)
    bitmap.font.color = Color.new(255, 224, 64)
    bitmap.draw_text(0, 0, 32, 32, "!", 1)
  end

  def disposed?
    return @disposed
  end

  def dispose
    return if @disposed
    @sprite.bitmap.dispose if @sprite.bitmap && !@sprite.bitmap.disposed?
    @sprite.dispose if !@sprite.disposed?
    @event = nil
    @disposed = true
  end

  def update
    return if @disposed || !@event
    @sprite.visible = Ironmon.hidden_area_item_marker_visible?(
      @event, @entry_id
    )
    return if !@sprite.visible
    @sprite.update
    if Object.const_defined?(:ScreenPosHelper)
      @sprite.x = ScreenPosHelper.pbScreenX(@event)
      @sprite.y = ScreenPosHelper.pbScreenY(@event)
      @sprite.zoom_x = ScreenPosHelper.pbScreenZoomX(@event)
    else
      @sprite.x = @event.screen_x
      @sprite.y = @event.screen_y
      @sprite.zoom_x = 1.0
    end
    @sprite.zoom_y = @sprite.zoom_x
    @sprite.z = @event.screen_z(32) + 10
    @sprite.opacity = 208 + ((Graphics.frame_count / 8) % 4) * 12
    pbDayNightTint(@sprite)
  end
end

Events.onSpritesetCreate += proc do |_sender, event|
  spriteset = event[0]
  viewport = event[1]
  map = spriteset.map
  Ironmon.tracker_area_hidden_items(map.map_id).each do |entry|
    map_event = map.events[entry["event_id"].to_i]
    next if !map_event
    spriteset.addUserSprite(
      IronmonHiddenItemMarker.new(map_event, entry["entry_id"], viewport)
    )
  end
end
