module IronmonResponsiveInputRuntimeTests
  def self.assert(condition, message)
    raise "Responsive input validation failed: #{message}" if !condition
  end

  def self.controls(pressed, managed = Ironmon::TRACKER_CONTROL_KEYS.keys)
    Ironmon.receive_tracker_controls({ "enabled" => true,
      "managed" => managed, "pressed" => pressed })
    Input.update
  end

  def self.at(time)
    @time = time
    yield
  end

  def self.time
    return @time
  end

  def self.test_native_repeat
    service = Ironmon::ResponsiveInput
    service.reset
    @native_keys[Input::DOWN] = true
    at(100.0) { controls([], []) }
    assert(Input.repeat?(Input::DOWN), "native key repeats once when first pressed")
    assert(Input.repeat?(Input::DOWN), "repeat queries are stable within an input update")
    at(100.399) { controls([], []) }
    assert(!Input.repeat?(Input::DOWN), "native repeat waits for 400ms of real time")
    at(100.401) { controls([], []) }
    assert(Input.repeat?(Input::DOWN), "native repeat begins after its normal delay")
    at(100.499) { controls([], []) }
    assert(!Input.repeat?(Input::DOWN), "native repeat interval does not follow accelerated ticks")
    at(100.502) { controls([], []) }
    assert(Input.repeat?(Input::DOWN), "native repeat interval remains 100ms")
    at(103.0) { controls([], []) }
    assert(Input.repeat?(Input::DOWN), "a stalled frame produces one repeat")
    controls([], [])
    assert(!Input.repeat?(Input::DOWN), "a stalled frame never creates a catch-up burst")
    @native_keys.clear
    Input.update
    assert(!Input.repeat?(Input::DOWN) && service.held_seconds(Input::DOWN) == 0,
      "releasing native input clears repeat and duration")
  end

  def self.test_tracker_repeat
    service = Ironmon::ResponsiveInput
    service.reset
    at(200.0) { controls(["down", "quick_item_right"]) }
    assert(Input.press?(Input::DOWN) && Input.trigger?(Input::DOWN),
      "tracker press and trigger retain their native semantics")
    assert(Input.dir4 == 2, "movement direction remains available immediately")
    assert(Input.repeat?(Input::DOWN), "tracker direction has its initial repeat")
    assert(Ironmon.tracker_control_action_triggered?("quick_item_right", true),
      "quick-item navigation has its initial repeat")
    240.times { controls(["down", "quick_item_right"]) }
    assert(!Input.repeat?(Input::DOWN) && Input.press?(Input::DOWN),
      "hundreds of accelerated ticks preserve held movement without early menu repeat")
    assert(!Ironmon.tracker_control_action_triggered?("quick_item_right", true),
      "quick-item navigation also ignores accelerated tick counts")
    at(200.599) { controls(["down", "quick_item_right"]) }
    assert(!Input.repeat?(Input::DOWN), "tracker menu repeat waits for 600ms")
    at(200.601) { controls(["down", "quick_item_right"]) }
    assert(Input.repeat?(Input::DOWN) &&
      Ironmon.tracker_control_action_triggered?("quick_item_right", true),
      "tracker menu and quick-item repeats start together after 600ms")
    assert(!Ironmon.tracker_control_action_triggered?("quick_item_right"),
      "one-shot quick-item actions never acquire repeats")
    assert(Input.time?(Input::DOWN).between?(600, 602), "tracker hold duration uses real milliseconds")
    at(200.749) { controls(["down", "quick_item_right"]) }
    assert(!Input.repeat?(Input::DOWN), "tracker interval remains 150ms")
    at(200.752) { controls(["down", "quick_item_right"]) }
    assert(Input.repeat?(Input::DOWN), "tracker next repeat arrives after 150ms")
    controls([])
    assert(!Input.press?(Input::DOWN) && Input.release?(Input::DOWN) &&
      !Input.repeat?(Input::DOWN), "tracker release clears held input immediately")
    assert(Input.time?(Input::DOWN) == 0, "released tracker input has no duration")
  end

  def self.test_context_and_lease
    service = Ironmon::ResponsiveInput
    service.reset
    at(300.0) { controls(["confirm"], ["confirm", "interact"]) }
    assert(Input.repeat?(Input::C), "menu confirmation uses its own tracker action")
    Ironmon.with_tracker_control_context(:field) do
      assert(!Input.repeat?(Input::C), "field interaction never borrows held menu confirmation")
    end
    controls(["interact"], ["confirm", "interact"])
    Ironmon.with_tracker_control_context(:field) do
      assert(Input.repeat?(Input::C), "field action is tracked independently of menu confirmation")
    end
    assert(!Input.repeat?(Input::C), "menu confirmation never borrows held field interaction")
    Ironmon.instance_variable_set(:@tracker_control_deadline, Ironmon.tracker_uptime_seconds - 1)
    Input.update
    assert(!Ironmon.tracker_controls_active? && !Input.repeat?(Input::C),
      "expired tracker ownership cannot leave a stuck repeat")
    at(300.1) { controls(["confirm"]) }
    assert(Input.trigger?(Input::C) && Input.repeat?(Input::C),
      "a new lease starts a fresh input gesture")
    Ironmon.clear_tracker_controls
    controls(["confirm"])
    assert(Ironmon.tracker_control_action_triggered?("confirm"),
      "reacquiring input between updates starts a new gesture")
  end

  def self.test_normal_speed_compatibility
    service = Ironmon::ResponsiveInput
    service.reset
    service.define_singleton_method(:accelerated?) { false }
    at(400.0) { controls(["down", "quick_item_right"]) }
    23.times { controls(["down", "quick_item_right"]) }
    assert(Input.repeat?(Input::DOWN), "normal-speed tracker repeat retains its existing frame cadence")
    assert(Ironmon.tracker_control_action_triggered?("quick_item_right", true),
      "normal-speed quick-item navigation retains its existing cadence")
    assert(Input.time?(Input::DOWN) == 24 * 1000.0 / Graphics.frame_rate,
      "normal-speed tracker duration retains its existing contract")
  end

  def self.test_steady_state_allocations
    service = Ironmon::ResponsiveInput
    service.reset
    @native_keys[Input::DOWN] = true
    at(450.0) { 2.times { controls(["down", "quick_item_right"]) } }
    10.times { service.update }
    # GC's allocation counter covers all Ruby threads. Drain queued diagnostic
    # encoding from earlier scene tests before measuring this service alone.
    frames = Ironmon.instance_variable_get(:@tracker_control_frames)
    assert(frames["down"].to_i > 1 && frames["quick_item_right"].to_i > 1,
      "allocation measurement uses held input after its first frame")
    updates = 1_000
    quick_action = "quick_item_right"
    before = GC.stat(:total_allocated_objects)
    updates.times do
      service.update
      service.repeat?(Input::DOWN)
      service.held_seconds(Input::DOWN)
      service.quick_action_triggered?(quick_action, true)
    end
    allocated = GC.stat(:total_allocated_objects) - before
    assert(allocated < 50,
      "steady input updates and queries avoid per-key garbage (#{allocated} objects / #{updates} updates)")
    puts "Responsive input steady-state allocations: #{allocated} objects / #{updates} updates"
    @native_keys.clear
    controls([])
  end

  def self.run
    service = Ironmon::ResponsiveInput
    original_now = service.method(:now)
    original_accelerated = service.method(:accelerated?)
    original_native_press = service.instance_variable_get(:@native_press)
    original_states = service.instance_variable_get(:@states)
    original_sequence = service.sequence
    original_map = $game_map
    original_context = Ironmon.instance_variable_get(:@tracker_control_context)
    original_flags = [$game_temp.in_menu, $game_temp.in_battle, $game_temp.message_window_showing]
    $game_map = nil
    $game_temp.in_menu = $game_temp.in_battle = $game_temp.message_window_showing = false
    @native_keys = {}
    service.define_singleton_method(:now) { IronmonResponsiveInputRuntimeTests.time }
    service.define_singleton_method(:accelerated?) { true }
    service.instance_variable_set(:@native_press, proc { |key| @native_keys[key] == true })
    test_native_repeat
    test_tracker_repeat
    test_context_and_lease
    test_normal_speed_compatibility
    test_steady_state_allocations
    puts "Responsive input: native/tracker repeat timing, stable queries, movement, quick items, lease expiry, context mapping, and normal-speed compatibility passed"
  ensure
    service.define_singleton_method(:now, original_now)
    service.define_singleton_method(:accelerated?, original_accelerated)
    service.instance_variable_set(:@native_press, original_native_press)
    service.instance_variable_set(:@states, original_states)
    service.instance_variable_set(:@sequence, original_sequence)
    $game_map = original_map
    $game_temp.in_menu, $game_temp.in_battle, $game_temp.message_window_showing = original_flags
    Ironmon.instance_variable_set(:@tracker_control_context, original_context)
    Ironmon.clear_tracker_controls
  end
end

IronmonResponsiveInputRuntimeTests.run
