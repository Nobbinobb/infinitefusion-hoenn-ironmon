module IronmonBattleRunHotkeyRuntimeTests
  OUTPUT_PATH = $ironmon_battle_run_hotkey_test_output_path.to_s

  TestBattle = Struct.new(:trainer_battle) do
    def trainerBattle?
      return trainer_battle
    end
  end

  def self.assert(condition, message)
    raise "Battle Run hotkey runtime test failed: #{message}" if !condition
  end

  def self.with_active_ironmon
    singleton = class << Ironmon; self; end
    singleton.send(
      :alias_method, :battle_run_hotkey_original_active,
      :active?
    )
    singleton.send(:define_method, :active?) { true }
    return yield
  ensure
    if singleton && singleton.method_defined?(
      :battle_run_hotkey_original_active
    )
      singleton.send(
        :alias_method, :active?, :battle_run_hotkey_original_active
      )
      singleton.send(:remove_method, :battle_run_hotkey_original_active)
    end
  end

  def self.test_scope
    with_active_ironmon do
      wild = TestBattle.new(false)
      trainer = TestBattle.new(true)
      assert(
        Ironmon.battle_cancel_run_hotkey?(wild, 0),
        "the Run shortcut applies to the first wild command menu"
      )
      assert(
        !Ironmon.battle_cancel_run_hotkey?(wild, 1),
        "a later battler's Cancel command keeps its normal behavior"
      )
      assert(
        !Ironmon.battle_cancel_run_hotkey?(trainer, 0),
        "trainer battles do not receive the Run shortcut"
      )
    end
  end

  def self.test_command_result
    base = Class.new do
      attr_reader :received_mode

      def initialize(battle, result)
        @battle = battle
        @result = result
      end

      def pbCommandMenuEx(_idxBattler, _texts, mode = 0)
        @received_mode = mode
        return @result
      end
    end
    base.prepend(IronmonBattleRunHotkeySceneHooks)
    with_active_ironmon do
      cancelled = base.new(TestBattle.new(false), -1)
      assert(
        cancelled.pbCommandMenuEx(0, [], 0) == 3 &&
          cancelled.received_mode == 1,
        "Cancel is converted to the native Run command"
      )
      selected = base.new(TestBattle.new(false), 2)
      assert(
        selected.pbCommandMenuEx(0, [], 0) == 2,
        "ordinary command selection remains unchanged"
      )
    end
  end

  def self.test_run_graphics
    base = Class.new do
      attr_reader :received_mode

      def setIndexAndMode(_index, mode)
        @received_mode = mode
      end
    end
    base.prepend(IronmonBattleRunHotkeyDisplayHooks)
    display = base.new
    Ironmon.with_battle_cancel_run_hotkey do
      display.setIndexAndMode(0, 1)
    end
    assert(
      display.received_mode == 0,
      "the shortcut retains the ordinary Run button graphic"
    )
  end

  def self.run
    test_scope
    test_command_result
    test_run_graphics
    File.binwrite(OUTPUT_PATH, "battle Run hotkey runtime tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonBattleRunHotkeyRuntimeTests.run
