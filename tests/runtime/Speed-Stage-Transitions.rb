module IronmonSpeedStageTransitionTests
  def self.assert(condition, message)
    raise "Speed-stage transition validation failed: #{message}" if !condition
  end

  def self.now
    return Process.clock_gettime(Process::CLOCK_MONOTONIC)
  end

  def self.controls(pressed = [])
    Ironmon.receive_tracker_controls({ "enabled" => true,
      "managed" => Ironmon::TRACKER_CONTROL_KEYS.keys, "pressed" => pressed })
    Input.update
  end

  def self.tick(pressed = [])
    controls(pressed)
    Graphics.update
  end

  def self.profile
    result = { "lanes" => {}, "links" => [] }
    ["overworld", "battle"].each do |lane|
      result["lanes"][lane] = [
        { "id" => "#{lane}-normal", "nodes" => [{ "id" => "#{lane}-one", "value" => 1 }] },
        { "id" => "#{lane}-fast", "nodes" => [2, 3, 6, 10, 6, 3, 2].each_with_index.map do |value, index|
          { "id" => "#{lane}-#{index}", "value" => value }
        end }
      ]
    end
    result["links"] << { "id" => "fast", "overworld" => "overworld-0", "battle" => "battle-0" }
    return result
  end

  def self.set_speed(multiplier)
    controls
    Graphics.update
    $GameSpeed = SPEEDUP_STAGES.index(multiplier)
    Graphics.update
    Ironmon::SpeedUp.reset_engine
  end

  def self.measure(name)
    @native_sets.clear
    @resets = 0
    timings = []
    started = now
    yield timings
    elapsed = now - started
    ordered = timings.sort
    return { "case" => name, "seconds" => elapsed,
      "native_rate_sets" => @native_sets.length, "frame_resets" => @resets,
      "native_rates" => @native_sets.dup, "updates" => timings.length,
      "p95_ms" => ordered.empty? ? 0 : ordered[(ordered.length * 0.95).floor] * 1000,
      "maximum_ms" => ordered.empty? ? 0 : ordered.last * 1000 }
  end

  def self.test_same_native_budget
    set_speed(3)
    result = measure("rapid 3x/6x/10x stage changes") do |timings|
      12.times do
        [6, 10, 3, 10, 6, 3].each do |multiplier|
          $GameSpeed = SPEEDUP_STAGES.index(multiplier)
          started = now
          tick
          timings << now - started
          assert(Ironmon::SpeedUp.multiplier == multiplier, "logical speed changes immediately")
        end
      end
    end
    if ENV["IRONMON_STAGE_BASELINE"] != "1"
      assert(result["native_rate_sets"] == 0 && result["frame_resets"] == 0,
        "stages sharing the 120Hz native budget retain limiter and frame-phase progress")
    end
    return result
  end

  def self.test_toggle_burst
    set_speed(1)
    observed = []
    expected = []
    result = measure("48 physical-style toggle edges") do |timings|
      48.times do |index|
        expected << (index + 1) % SPEEDUP_STAGES.length
        started = now
        tick(["speed"])
        observed << $GameSpeed
        tick
        timings << now - started
      end
    end
    assert(observed == expected, "every toggle edge retains its exact cycle position")
    assert(result["native_rate_sets"] < observed.length / 2,
      "rapid toggle bursts coalesce redundant native rate changes") if ENV["IRONMON_STAGE_BASELINE"] != "1"
    result["observed_stages"] = observed
    return result
  end

  def self.test_settling_and_boundaries
    service = Ironmon::SpeedUp
    original_now = service.method(:now)
    stamp = 1_000.0
    service.define_singleton_method(:now) { stamp }
    set_speed(1)
    4.times do
      tick(["speed"])
      tick
      stamp += 0.01
    end
    assert(service.multiplier == 10 && $GameSpeed == 4,
      "rapid input selects the final logical stage without waiting")
    before_settle = service::NATIVE_RATE.call
    assert(before_settle < 120, "a rapid upward native change waits for the quiet period")
    deadline = service.instance_variable_get(:@coalesce_until)
    stamp = deadline - 0.001
    tick
    assert(service::NATIVE_RATE.call == before_settle, "the native budget is stable during the quiet period")
    stamp = deadline + 0.001
    tick
    assert(service::NATIVE_RATE.call == 120 && service.multiplier == 10,
      "the final native budget applies after 80ms of quiet input")

    stamp += 0.01
    tick(["speed"])
    tick
    chosen = $GameSpeed
    Ironmon::MoveLearningProtection.protect do
      tick
      assert(service.multiplier == 1 && service::NATIVE_RATE.call == 40,
        "move protection immediately restores true normal speed during a burst")
    end
    tick
    assert($GameSpeed == chosen && service.multiplier == 6 && service::NATIVE_RATE.call == 120,
      "leaving protection immediately restores the selected speed")
    Ironmon::SpeedTransitions.with_visual_speed do
      assert(service.multiplier == 3 && service::NATIVE_RATE.call == 120,
        "visual transitions apply their own pacing immediately")
    end
    assert(service.multiplier == 6 && $GameSpeed == chosen,
      "visual transitions preserve the exact selected stage")
    $GameSpeed = 0
    tick
    assert(service.multiplier == 1 && service::NATIVE_RATE.call == 40,
      "normal speed applies immediately even during a rapid burst")
    return { "case" => "settling and immediate safety boundaries", "quiet_milliseconds" => 80,
      "logical_stage_immediate" => true, "protected_and_visual_boundaries_immediate" => true }
  ensure
    service.define_singleton_method(:now, original_now) if original_now
    service.reset_engine if service
  end

  def self.run
    service = Ironmon::SpeedUp
    original_profile = Ironmon::ToggleSpeed.configuration
    original_state = [$PokemonSystem.speedup, $GameSpeed, $CanToggle, $game_temp.in_battle]
    original_set_rate = service.const_get(:SET_NATIVE_RATE)
    @native_sets = []
    @resets = 0
    service.send(:remove_const, :SET_NATIVE_RATE)
    service.const_set(:SET_NATIVE_RATE, proc do |value|
      @native_sets << value
      original_set_rate.call(value)
    end)
    owner = Graphics.singleton_class
    original_reset = Graphics.method(:frame_reset)
    original_reset = original_reset.super_method until original_reset.owner == owner
    original_reset = original_reset.unbind
    owner.send(:define_method, :frame_reset) do
      IronmonSpeedStageTransitionTests.instance_variable_set(:@resets,
        IronmonSpeedStageTransitionTests.instance_variable_get(:@resets) + 1)
      original_reset.bind_call(self)
    end
    $PokemonSystem.speedup = 1
    $CanToggle = true
    $game_temp.in_battle = false
    Ironmon::ToggleSpeed.configure(profile)
    tick
    results = [test_same_native_budget, test_toggle_burst]
    results << test_settling_and_boundaries if ENV["IRONMON_STAGE_BASELINE"] != "1"
    output = $ironmon_speed_up_acceptance_output.to_s
    File.binwrite(File.join(output, "speed-stage-transitions.json"), Ironmon.tracker_json_generate({
      "baseline" => ENV["IRONMON_STAGE_BASELINE"] == "1", "measurements" => results })) if !output.empty?
    puts "Speed-stage transitions: immediate stages, exact toggle edges, and native limiter/phase continuity passed"
  ensure
    if original_set_rate
      service.send(:remove_const, :SET_NATIVE_RATE)
      service.const_set(:SET_NATIVE_RATE, original_set_rate)
    end
    owner.send(:define_method, :frame_reset, original_reset) if owner && original_reset
    Ironmon.clear_tracker_controls
    Ironmon::ToggleSpeed.configure(original_profile) if original_profile
    if original_state
      $PokemonSystem.speedup, original_stage, $CanToggle, $game_temp.in_battle = original_state
      tick
      $GameSpeed = original_stage
      tick
    end
    Ironmon.clear_tracker_controls
  end
end

IronmonSpeedStageTransitionTests.run
