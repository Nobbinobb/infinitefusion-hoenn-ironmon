#===============================================================================
# Ironmon battle move type colors
#===============================================================================

module Ironmon
  BATTLE_MOVE_BUTTON_FOLDER = "Data/Ironmon/graphics/Battle"
  BATTLE_MOVE_BUTTON_PATH = "#{BATTLE_MOVE_BUTTON_FOLDER}/cursor_fight"
  BATTLE_MOVE_BUTTON_DARK_PATH =
    "#{BATTLE_MOVE_BUTTON_FOLDER}/cursor_fight_dark"

  def self.battle_move_button_path(dark_mode)
    return nil if !active?
    path = dark_mode ? BATTLE_MOVE_BUTTON_DARK_PATH : BATTLE_MOVE_BUTTON_PATH
    return nil if !pbResolveBitmap(path)
    return path
  end
end

module IronmonBattleMoveTypeColorDisplayHooks
  def initialize(viewport, z)
    super
    return if !FightMenuDisplay::USE_GRAPHICS
    button_path = Ironmon.battle_move_button_path(isDarkMode)
    return if !button_path
    original_bitmap = @buttonBitmap
    @buttonBitmap = AnimatedBitmap.new(button_path)
    @buttons.each do |button|
      button.bitmap = @buttonBitmap.bitmap
      button.src_rect.width = @buttonBitmap.width / 2
      button.src_rect.height = BattleMenuBase::BUTTON_HEIGHT
    end
    original_bitmap.dispose
  end
end

FightMenuDisplay.prepend(IronmonBattleMoveTypeColorDisplayHooks)
