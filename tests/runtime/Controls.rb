module IronmonControlsRuntimeTests
  OUTPUT_PATH = $ironmon_controls_test_output_path.to_s

  def self.assert(condition, message)
    raise "Controls runtime test failed: #{message}" if !condition
  end

  def self.state(pressed, managed = Ironmon::TRACKER_CONTROL_KEYS.keys)
    Ironmon.receive_tracker_controls({
      "enabled" => true, "managed" => managed, "pressed" => pressed
    })
  end

  def self.run
    original_temp = $game_temp
    native_input = Input.singleton_class
    native_methods = {}
    { :press? => true, :trigger? => true, :repeat? => true,
      :release? => true, :time? => 9.0 }.each do |query, result|
      original = Input.method(query).super_method
      native_methods[query] = original
      native_input.define_method(query) do |key|
        [Input::C, Input::B, Input::LEFT, Input::F9].include?(key) ? result : original.call(key)
      end
    end
    singleton = Ironmon.singleton_class
    singleton.alias_method(:controls_test_original_uptime, :tracker_uptime_seconds)
    singleton.define_method(:tracker_uptime_seconds) { 100.0 }
    Ironmon.clear_tracker_controls
    assert(!Ironmon.tracker_controls_active?, "native input starts in ownership")
    assert(Input.press?(Input::C) && Input.trigger?(Input::B), "test backend supplies held native confirm and cancel")

    state([])
    Input.update
    assert(!Input.press?(Input::C), "managed native confirm is suppressed")
    assert(!Input.trigger?(Input::B) && !Input.repeat?(Input::C), "native triggers and repeats are suppressed")
    assert(!Input.release?(Input::C) && Input.time?(Input::C) == 0, "native release and duration are suppressed")
    assert(Input.dir4 == 0 && Input.dir8 == 0, "native held direction cannot leak through the override")
    assert(Input.press?(Input::F9), "unmanaged engine input retains native ownership")
    state(["confirm", "right"])
    Input.update
    assert(Input.press?(Input::C), "remapped confirm is held")
    assert(Input.trigger?(Input::C), "confirm triggers once on the leading edge")
    assert(Input.repeat?(Input::C), "initial confirm repeats")
    assert(Input.dir4 == 6 && Input.dir8 == 6, "right direction reaches movement")
    Input.update
    assert(!Input.trigger?(Input::C), "held input does not retrigger")
    assert(!Input.repeat?(Input::C), "held input waits before repeating")
    22.times { Input.update }
    assert(Input.repeat?(Input::C), "held menu input repeats after its delay")
    assert(Input.time?(Input::C) > 0, "held input has a duration")
    state([])
    Input.update
    assert(!Input.press?(Input::C) && Input.release?(Input::C), "release clears held input")

    state(["confirm"])
    state([])
    Input.update
    assert(Input.trigger?(Input::C), "a short press survives multiple network snapshots")
    Input.update
    assert(!Input.press?(Input::C), "a buffered short press releases next frame")
    state(["left", "right", "up", "down"])
    Input.update
    assert(Input.dir4 == 0 && Input.dir8 == 0, "opposed directions cancel")
    state(["up", "right"])
    Input.update
    assert(Input.dir8 == 9, "diagonal input reaches the engine")
    state(["reset"])
    Input.update
    assert(Input.trigger?(Ironmon::RESET_KEY), "custom reset reaches the existing F7 guard")

    state(["interact", "menu"])
    Input.update
    assert(!Input.press?(Input::C) && !Input.press?(Input::B), "field controls do not confirm or cancel menus")
    Ironmon.with_tracker_control_context(:field) do
      assert(Input.trigger?(Input::C), "interact uses the field confirm alias")
      assert(Input.trigger?(Input::B), "pause menu uses the field cancel alias")
      Ironmon.with_tracker_control_context(:menu) do
        assert(!Input.press?(Input::C) && !Input.press?(Input::B), "nested menus use menu bindings")
      end
      assert(Input.press?(Input::C), "leaving a nested menu restores field context")
    end
    state(["confirm", "cancel"])
    Input.update
    Ironmon.with_tracker_control_context(:field) do
      assert(!Input.press?(Input::C) && !Input.press?(Input::B), "menu bindings do not interact or open the pause menu")
    end
    assert(Input.trigger?(Input::C) && Input.trigger?(Input::B), "menu confirm and cancel remain independent")
    $game_temp = Game_Temp.new
    Ironmon.with_tracker_control_context(:field) do
      $game_temp.in_menu = true
      assert(Input.press?(Input::C) && Input.press?(Input::B), "a field menu uses menu controls")
      $game_temp.in_menu = false
      $game_temp.message_window_showing = true
      assert(Input.press?(Input::C) && Input.press?(Input::B), "a field dialog uses menu controls")
      $game_temp.message_window_showing = false
      $game_temp.in_battle = true
      assert(Input.press?(Input::C) && Input.press?(Input::B), "a battle opened from the field uses menu controls")
    end
    $game_temp = original_temp
    Ironmon.with_battle_cancel_run_hotkey do
      assert(!Input.press?(Input::B), "menu cancel does not activate Battle Run")
      assert(Input.press?(Input::C), "battle confirmation retains menu confirm")
    end
    state(["battle_run"])
    Input.update
    assert(!Input.press?(Input::B), "Battle Run does not cancel ordinary menus")
    Ironmon.with_battle_cancel_run_hotkey do
      assert(Input.trigger?(Input::B), "Battle Run reaches the guarded wild-battle shortcut")
    end
    begin
      Ironmon.with_tracker_control_context(:field) { raise "context test" }
    rescue RuntimeError
      assert(Ironmon.instance_variable_get(:@tracker_control_context).nil?, "exceptions restore the previous input context")
    end

    Ironmon::TRACKER_CONTROL_KEYS.keys.grep(/\Aquick_item_/).each do |action|
      state([])
      Input.update
      state([action])
      Input.update
      assert(Ironmon.tracker_control_action_triggered?(action), "quick-item action reaches its first input frame")
      assert(!Input.trigger?(Input::C) && Input.dir4 == 0, "quick-item input cannot confirm or move the native menu")
      Input.update
      assert(!Ironmon.tracker_control_action_triggered?(action), "held quick confirm and directions do not retrigger immediately")
      22.times { Input.update }
      assert(Ironmon.tracker_control_action_triggered?(action, true), "quick-item navigation supports delayed repeat")
      assert(!Ironmon.tracker_control_action_triggered?(action), "quick-item confirmation never repeats")
    end

    Ironmon.receive_tracker_controls({ "enabled" => false })
    assert(!Ironmon.tracker_controls_active?, "tracker close restores native ownership")
    assert(Input.press?(Input::C) && Input.trigger?(Input::B), "closing the tracker restores held native bindings")
    assert(Input.press?(Input::C) == Input.method(:press?).super_method.call(Input::C), "native input is delegated after release")
    state(["confirm"])
    singleton.define_method(:tracker_uptime_seconds) { 102.0 }
    assert(!Ironmon.tracker_controls_active?, "crashed tracker lease expires")
    Input.update
    assert(Ironmon.tracker_control_frames(Input::C) == 0, "timeout clears held actions")

    singleton.define_method(:tracker_uptime_seconds) { 100.0 }
    state(["confirm"])
    connection = Ironmon::TrackerConnection.new
    connection.send(:disconnect)
    assert(!Ironmon.tracker_controls_active?, "disconnect immediately restores controls")
    Ironmon.receive_tracker_controls({ "enabled" => true, "managed" => ["unknown"], "pressed" => [] })
    assert(!Ironmon.tracker_controls_active?, "invalid actions cannot seize input")
    state(["confirm"], ["reset"])
    Input.update
    assert(!Ironmon.tracker_control_managed?(Input::C), "unmanaged actions retain native input")
    assert(!Input.press?(Input::F7), "pressed input must also be managed")
    File.binwrite(OUTPUT_PATH, "PASS: bundled runtime input override, triggers, repeat, release, buffered taps, movement, separate field/menu/battle bindings, nested contexts, reset routing, disconnect, timeout, and native fallback.\n")
  ensure
    $game_temp = original_temp if defined?(original_temp)
    Ironmon.clear_tracker_controls
    native_methods.each { |query, original| native_input.define_method(query, original) } if native_methods
    if singleton && singleton.method_defined?(:controls_test_original_uptime)
      singleton.alias_method(:tracker_uptime_seconds, :controls_test_original_uptime)
      singleton.remove_method(:controls_test_original_uptime)
    end
  end
end

IronmonControlsRuntimeTests.run
