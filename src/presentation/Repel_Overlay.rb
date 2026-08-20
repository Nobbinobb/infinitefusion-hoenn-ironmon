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
    return false if !active?
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
  WIDTH = 132
  HEIGHT = 21
  SCREEN_MARGIN = 4

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

  def update
    return if @disposed
    @sprite.visible = Ironmon.repel_overlay_visible?
    return if !@sprite.visible
    @sprite.update
    update_position
    text = Ironmon.repel_overlay_text
    return if text == @last_text
    draw(text)
    @last_text = text
  end

  def update_position
    @sprite.x = Graphics.width - WIDTH - SCREEN_MARGIN
    @sprite.y = SCREEN_MARGIN
  end

  def draw(text)
    bitmap = @sprite.bitmap
    bitmap.clear
    bitmap.fill_rect(1, 1, WIDTH - 1, HEIGHT - 1, Color.new(0, 0, 0, 112))
    bitmap.fill_rect(0, 0, WIDTH - 1, HEIGHT - 1, Color.new(24, 30, 38, 224))
    bitmap.fill_rect(0, 0, 3, HEIGHT - 1, Color.new(108, 220, 142))
    bitmap.font.name = MessageConfig.pbTryFonts("Arial")
    bitmap.font.size = 16
    bitmap.font.bold = false
    bitmap.font.shadow = false if bitmap.font.respond_to?("shadow")
    bitmap.font.color = Color.new(248, 250, 252)
    bitmap.draw_text(4, 2, WIDTH - 5, 17, text, 1)
  end
end

module IronmonRepelOverlaySpritesetHooks
  def initialize
    super
    @ironmon_repel_overlay = IronmonRepelOverlay.new(Spriteset_Map.viewport)
  end

  def update
    super
    @ironmon_repel_overlay.update if @ironmon_repel_overlay
  end

  def dispose
    @ironmon_repel_overlay.dispose if @ironmon_repel_overlay
    @ironmon_repel_overlay = nil
    super
  end
end

Spriteset_Global.prepend(IronmonRepelOverlaySpritesetHooks)
