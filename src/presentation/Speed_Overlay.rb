class IronmonSpeedOverlay
  WIDTH = Ironmon::OverlayStyle::STATUS_WIDTH
  HEIGHT = Ironmon::OverlayStyle::STATUS_HEIGHT
  RAIL_X = 48
  RAIL_LENGTH = 85
  MULTIPLIER_X = 5
  MULTIPLIER_WIDTH = 26
  DIVIDER_X = 37
  attr_reader :sprite

  def initialize(viewport)
    @sprite = Sprite.new(viewport)
    @sprite.bitmap = Bitmap.new(WIDTH, HEIGHT)
    @sprite.visible = false
  end

  def disposed?
    return @sprite.disposed?
  end

  def dispose
    return if disposed?
    @sprite.bitmap.dispose
    @sprite.dispose
  end

  def update(state)
    @sprite.x = Graphics.width - WIDTH - Ironmon::OverlayStyle::MARGIN
    @sprite.y = Ironmon::OverlayStyle::MARGIN
    @sprite.visible = !!(state && state[0] > 1)
    return if !@sprite.visible || state == @last_state
    draw(*state)
    @last_state = state
  end

  def draw(multiplier, index, total)
    bitmap = @sprite.bitmap
    bitmap.clear
    Ironmon::OverlayStyle.draw_panel(bitmap)
    Ironmon::OverlayStyle.set_font(bitmap, 16)
    bitmap.draw_text(MULTIPLIER_X, 5, MULTIPLIER_WIDTH, 16, "#{multiplier}×", 2)
    Ironmon::OverlayStyle.draw_divider(bitmap, DIVIDER_X)
    position = (RAIL_LENGTH * index.to_f / [total - 1, 1].max).round
    bitmap.fill_rect(RAIL_X, 12, RAIL_LENGTH, 2, Color.new(145, 160, 174, 115))
    bitmap.fill_rect(RAIL_X, 12, position, 2, Color.new(248, 250, 252, 140)) if position > 0
    marker_x = RAIL_X + position
    if index == total - 1
      bitmap.fill_rect(marker_x - 1, 6, 3, 14, Color.new(248, 250, 252))
    else
      Ironmon::OverlayStyle.fill_rounded_rect(bitmap, marker_x - 4, 9, 8, 8, 4, Color.new(248, 250, 252))
    end
  end
end

module Ironmon
  module StatusOverlays
    def self.visible_scene?
      return Ironmon.active? && $scene.is_a?(Scene_Map)
    end

    def self.update
      if !visible_scene?
        dispose
        return
      end
      state = Ironmon.feature_enabled?("speed_indicator") ? Ironmon::ToggleSpeed.overlay_state : nil
      speed_visible = state && state[0] > 1
      if !speed_visible && !Ironmon.repel_overlay_visible?
        dispose
        return
      end
      if !@viewport || @viewport.disposed?
        @viewport = Viewport.new(0, 0, Graphics.width, Graphics.height)
        @viewport.z = 100_000
        @speed = IronmonSpeedOverlay.new(@viewport)
        @repel = IronmonRepelOverlay.new(@viewport)
      end
      @viewport.rect.set(0, 0, Graphics.width, Graphics.height)
      @speed.update(state)
      @repel.update(speed_visible)
    end

    def self.dispose
      @speed.dispose if @speed && !@speed.disposed?
      @repel.dispose if @repel && !@repel.disposed?
      @viewport.dispose if @viewport && !@viewport.disposed?
      @speed = nil; @repel = nil; @viewport = nil
    end
  end
end

Ironmon.register_graphics_update_hook(:status_overlays, proc { Ironmon::StatusOverlays.update })
