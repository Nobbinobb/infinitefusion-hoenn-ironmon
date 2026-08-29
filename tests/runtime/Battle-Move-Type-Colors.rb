module IronmonBattleMoveTypeColorRuntimeTests
  OUTPUT_PATH = $ironmon_battle_move_type_color_test_output_path.to_s

  def self.assert(condition, message)
    raise "Battle move type color runtime test failed: #{message}" if !condition
  end

  def self.with_ironmon_active(active)
    singleton = class << Ironmon; self; end
    singleton.send(
      :alias_method, :battle_move_type_color_original_active,
      :active?
    )
    singleton.send(:define_method, :active?) { active }
    return yield
  ensure
    if singleton && singleton.method_defined?(
      :battle_move_type_color_original_active
    )
      singleton.send(
        :alias_method, :active?, :battle_move_type_color_original_active
      )
      singleton.send(:remove_method, :battle_move_type_color_original_active)
    end
  end

  def self.assert_color(bitmap, row, expected, description)
    color = bitmap.get_pixel(20, (row * 46) + 24)
    assert(
      [color.red, color.green, color.blue] == expected,
      description
    )
  end

  def self.test_private_assets
    assert(
      GameData::Type.get(:STEEL).id_number == 8,
      "the generated Steel row matches the game's type numbering"
    )
    assert(
      GameData::Move.get(:IRONHEAD).type == :STEEL,
      "Iron Head selects the generated Steel row"
    )
    light = Bitmap.new("#{Ironmon::BATTLE_MOVE_BUTTON_PATH}.png")
    dark = Bitmap.new("#{Ironmon::BATTLE_MOVE_BUTTON_DARK_PATH}.png")
    begin
      assert_color(light, 0, [159, 161, 159], "light Normal uses the tracker palette")
      assert_color(light, 8, [111, 143, 173], "light Steel uses the tracker palette")
      assert_color(light, 10, [230, 40, 41], "light Fire uses the tracker palette")
      assert_color(light, 16, [104, 88, 208], "light Dragon stays distinct from Water")
      assert_color(dark, 2, [129, 185, 239], "dark Flying uses the tracker palette")
      assert_color(dark, 18, [239, 112, 239], "dark Fairy uses the tracker palette")
    ensure
      light.dispose
      dark.dispose
    end
  end

  def self.test_scope
    with_ironmon_active(false) do
      assert(
        Ironmon.battle_move_button_path(false).nil?,
        "normal saves retain the standard light battle move sheet"
      )
      assert(
        Ironmon.battle_move_button_path(true).nil?,
        "normal saves retain the standard dark battle move sheet"
      )
    end
    with_ironmon_active(true) do
      assert(
        Ironmon.battle_move_button_path(false) ==
          Ironmon::BATTLE_MOVE_BUTTON_PATH,
        "Ironmon saves select the private light battle move sheet"
      )
      assert(
        Ironmon.battle_move_button_path(true) ==
          Ironmon::BATTLE_MOVE_BUTTON_DARK_PATH,
        "Ironmon saves select the private dark battle move sheet"
      )
    end
  end

  def self.test_fight_menu_override
    viewport = Viewport.new(0, 0, Graphics.width, Graphics.height)
    display = nil
    with_ironmon_active(true) do
      display = FightMenuDisplay.new(viewport, 1)
      bitmap = display.instance_variable_get(:@buttonBitmap).bitmap
      assert_color(
        bitmap,
        13,
        [250, 192, 0],
        "the live Ironmon fight menu uses the private Electric row"
      )
      buttons = display.instance_variable_get(:@buttons)
      buttons.each do |button|
        assert(
          button.src_rect.width == bitmap.width / 2,
          "the private sheet preserves the native move button width"
        )
        assert(
          button.src_rect.height == BattleMenuBase::BUTTON_HEIGHT,
          "the private sheet preserves the native move button height"
        )
      end
    end
  ensure
    display.dispose if display
    viewport.dispose if viewport && !viewport.disposed?
  end

  def self.run
    test_private_assets
    test_scope
    test_fight_menu_override
    File.binwrite(
      OUTPUT_PATH,
      "battle move type color runtime tests passed\n"
    )
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n" +
        "#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonBattleMoveTypeColorRuntimeTests.run
