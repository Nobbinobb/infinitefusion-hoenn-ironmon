#===============================================================================
# Ironmon Repel duration overlay
#===============================================================================

module Ironmon
  def self.repel_steps_remaining
    return 0 if !$PokemonGlobal
    steps = $PokemonGlobal.repel.to_i
    return steps > 0 ? steps : 0
  end

  def self.repel_overlay_visible?
    return false if !active? || !feature_enabled?("repel_counter")
    return false if $game_temp && $game_temp.in_battle
    return false if ($PokemonGlobal.tempRepel rescue false)
    return repel_steps_remaining > 0
  end

  def self.repel_overlay_text
    steps = repel_steps_remaining
    unit = steps == 1 ? _INTL("step") : _INTL("steps")
    return _INTL("Repel: {1} {2}", steps, unit)
  end
end

class IronmonRepelOverlay
  WIDTH = Ironmon::OverlayStyle::STATUS_WIDTH
  HEIGHT = Ironmon::OverlayStyle::STATUS_HEIGHT
  SCREEN_MARGIN = Ironmon::OverlayStyle::MARGIN
  attr_reader :sprite

  def initialize(viewport)
    @sprite = Sprite.new(viewport)
    @sprite.bitmap = Bitmap.new(WIDTH, HEIGHT)
    @sprite.z = 99_999
    @last_text = nil
    @disposed = false
    update_position
    update
  end

  def disposed?
    return @disposed
  end

  def dispose
    return if @disposed
    @sprite.bitmap.dispose if @sprite.bitmap && !@sprite.bitmap.disposed?
    @sprite.dispose if !@sprite.disposed?
    @disposed = true
  end

  def update(speed_visible = false)
    return if @disposed
    @sprite.visible = Ironmon.repel_overlay_visible?
    return if !@sprite.visible
    @sprite.update
    update_position(speed_visible)
    text = Ironmon.repel_overlay_text
    return if text == @last_text
    draw(text)
    @last_text = text
  end

  def update_position(speed_visible = false)
    @sprite.x = Graphics.width - WIDTH - SCREEN_MARGIN
    @sprite.y = SCREEN_MARGIN + (speed_visible ? HEIGHT + Ironmon::OverlayStyle::STATUS_GAP : 0)
  end

  def draw(text)
    bitmap = @sprite.bitmap
    bitmap.clear
    Ironmon::OverlayStyle.draw_panel(bitmap)
    Ironmon::OverlayStyle.set_font(bitmap, 16)
    label = _INTL("Repel")
    duration = text.sub(/\A.*?:\s*/, "")
    label_width = bitmap.text_size(label).width
    duration_width = bitmap.text_size(duration).width
    x = (WIDTH - label_width - duration_width - 17) / 2
    bitmap.draw_text(x, 5, label_width, 16, label, 0)
    Ironmon::OverlayStyle.draw_divider(bitmap, x + label_width + 8)
    bitmap.draw_text(x + label_width + 17, 5, duration_width, 16, duration, 0)
  end
end
