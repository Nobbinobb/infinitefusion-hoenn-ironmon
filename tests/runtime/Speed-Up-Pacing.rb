module IronmonSpeedUpPacingTests
  SAMPLE_SECONDS = 0.75
  LOGICAL_WORK_SECONDS = 0.0001
  WORK_SPIKE_SECONDS = 0.020

  # Models a blocking display swap and the native limiter's ability to omit a
  # draw when behind. It does not change the computer's actual display mode.
  class DisplayClock
    attr_reader :time, :draws, :skips, :resets

    def initialize(refresh)
      @refresh = refresh.to_f
      @time = 1_000.0
      @draws = 0
      @skips = 0
      @resets = 0
      reset
    end

    def advance(seconds)
      @time += seconds
    end

    def reset
      @deadline = @time
      @last_swap = @time
      @resets += 1
    end

    def render
      interval = 1.0 / Ironmon::SpeedUp::NATIVE_RATE.call
      @deadline += interval
      if Graphics.frameskip && @time - @deadline > interval
        @skips += 1
      else
        ready = [@time, @deadline].max
        boundary = ((ready * @refresh - 0.000001).ceil / @refresh)
        @time = [boundary, @last_swap + 1.0 / @refresh].max
        @last_swap = @time
        @draws += 1
      end
      Ironmon::SpeedClock.set_raw_frame_count(Ironmon::SpeedClock.raw_frame_count + 1)
    end
  end

  def self.assert(condition, message)
    raise "Speed-up pacing failed: #{message}" if !condition
  end

  def self.now
    return Process.clock_gettime(Process::CLOCK_MONOTONIC)
  end

  def self.controls
    Ironmon.receive_tracker_controls({ "enabled" => true,
      "managed" => Ironmon::TRACKER_CONTROL_KEYS.keys, "pressed" => [] })
    Input.update
  end

  def self.tick
    Graphics.update
    controls
  end

  def self.profile
    result = Ironmon::ToggleSpeed.default_configuration
    [6, 10].each do |value|
      Ironmon::ToggleSpeed::LANES.each do |lane|
        result["lanes"][lane] << { "id" => "pacing-#{lane}-group-#{value}",
          "nodes" => [{ "id" => "pacing-#{lane}-#{value}", "value" => value }] }
      end
      result["links"] << { "id" => "pacing-#{value}",
        "overworld" => "pacing-overworld-#{value}", "battle" => "pacing-battle-#{value}" }
    end
    return result
  end

  def self.speed(multiplier)
    tick
    $GameSpeed = SPEEDUP_STAGES.index(multiplier)
    assert(!$GameSpeed.nil?, "the requested #{multiplier}x stage exists")
    tick
    Ironmon::SpeedUp.reset_engine
  end

  def self.percentile(values, fraction)
    return 0.0 if values.empty?
    ordered = values.sort
    return ordered[[(ordered.length * fraction).ceil - 1, 0].max]
  end

  def self.metrics(values)
    return { "p50_ms" => percentile(values, 0.50) * 1000,
      "p95_ms" => percentile(values, 0.95) * 1000,
      "p99_ms" => percentile(values, 0.99) * 1000,
      "maximum_ms" => values.empty? ? 0.0 : values.max * 1000 }
  end

  def self.native_frame
    started = now
    result = yield
    @native_durations << now - started if @native_durations
    return result
  end

  def self.measure(multiplier, limit)
    speed(multiplier)
    8.times { tick }
    Graphics.frame_reset
    @native_durations = []
    durations = []
    gaps = []
    spike_after = nil
    allocated_before = GC.stat(:total_allocated_objects)
    collections_before = GC.stat(:count)
    started = now
    previous = started
    while now - started < SAMPLE_SECONDS
      began = now
      work_until = began + LOGICAL_WORK_SECONDS
      work_until += WORK_SPIKE_SECONDS if !spike_after && began - started >= SAMPLE_SECONDS / 2
      if work_until - began > WORK_SPIKE_SECONDS
        spike_after = durations.length
      end
      1 while now < work_until
      tick
      finished = now
      durations << finished - began
      gaps << finished - previous
      previous = finished
    end
    elapsed = now - started
    frames = durations.length
    actual_multiplier = frames / elapsed / Graphics.frame_rate
    assert(actual_multiplier.between?(multiplier * 0.35, multiplier * 1.5),
      "native throughput remains bounded at #{multiplier}x (#{actual_multiplier})")
    assert(@native_durations.length > 0, "the native engine receives updates at #{multiplier}x")
    assert(spike_after, "the pacing window exercised a 20ms work spike")
    return { "variant" => "native-budget-#{limit}", "native_update_limit" => limit,
      "multiplier" => multiplier, "seconds" => elapsed,
      "logical_updates" => frames, "measured_multiplier" => actual_multiplier,
      "native_updates" => @native_durations.length,
      "native_average_frame_rate" => Graphics.average_frame_rate,
      "logical_update_timing" => metrics(durations), "native_update_timing" => metrics(@native_durations),
      "frame_gap_timing" => metrics(gaps), "spike_after_update" => spike_after,
      "allocated_objects" => GC.stat(:total_allocated_objects) - allocated_before,
      "garbage_collections" => GC.stat(:count) - collections_before }
  ensure
    @native_durations = nil
  end

  def self.test_simulated_60hz
    service = Ironmon::SpeedUp
    original_now = service.method(:now)
    native_owner = Graphics.singleton_class
    original_native = Graphics.method(:update_KGC_SpecialTransition)
    original_native = original_native.super_method until original_native.owner == native_owner
    original_reset = Graphics.method(:frame_reset)
    original_reset = original_reset.super_method until original_reset.owner == native_owner
    original_native = original_native.unbind
    original_reset = original_reset.unbind
    clock = DisplayClock.new(60)
    service.define_singleton_method(:now) { clock.time }
    native_owner.send(:define_method, :update_KGC_SpecialTransition) { clock.render }
    native_owner.send(:define_method, :frame_reset) { clock.reset }
    results = []
    [60, 120].each do |limit|
      @limit = limit
      [1, 2, 3, 6, 10].each do |multiplier|
        speed(multiplier)
        12.times { clock.advance(LOGICAL_WORK_SECONDS); tick }
        began = clock.time
        before = Graphics.frame_count
        before_draws = clock.draws
        before_skips = clock.skips
        steps = 0
        while clock.time - began < 1.0
          clock.advance(LOGICAL_WORK_SECONDS)
          tick
          steps += 1
        end
        duration = clock.time - began
        observed = steps / duration / Graphics.frame_rate
        assert(observed.between?(multiplier * 0.90, multiplier * 1.10),
          "a simulated 60Hz display preserves #{multiplier}x gameplay with native budget #{limit} (#{observed})")
        assert(Graphics.frame_count - before == steps,
          "simulated blocking swaps preserve every logical update")
        draws = clock.draws - before_draws
        native_skips = clock.skips - before_skips
        assert(draws / duration <= 61,
          "the display model never presents faster than its 60Hz refresh")
        resets_before = clock.resets
        clock.advance(0.2)
        recovery_began = clock.time
        40.times { clock.advance(LOGICAL_WORK_SECONDS); tick }
        recovery = clock.time - recovery_began
        expected_recovery = 40.0 / (Graphics.frame_rate * multiplier)
        assert(clock.resets > resets_before, "a 200ms work stall resets pacing debt")
        assert(recovery.between?(expected_recovery * 0.8, expected_recovery + 0.04),
          "resuming after a 200ms stall stays paced at #{multiplier}x (#{recovery})")
        results << { "native_update_limit" => limit, "multiplier" => multiplier,
          "simulated_refresh_hz" => 60, "seconds" => duration,
          "logical_updates" => steps, "measured_multiplier" => observed,
          "draws" => draws, "native_skips" => native_skips,
          "recovery_seconds_for_40_updates" => recovery }
      end
    end
    return results
  ensure
    service.define_singleton_method(:now, original_now) if original_now
    native_owner.send(:define_method, :update_KGC_SpecialTransition, original_native) if original_native
    native_owner.send(:define_method, :frame_reset, original_reset) if original_reset
    service.reset_engine if service
  end

  def self.run
    original_profile = Ironmon::ToggleSpeed.configuration
    original_settings = [$PokemonSystem.speedup, $GameSpeed, $CanToggle, $game_temp.in_battle]
    service = Ironmon::SpeedUp
    original_limit = service.method(:native_update_limit)
    service.define_singleton_method(:native_update_limit) { IronmonSpeedUpPacingTests.limit }
    original_native = Graphics.method(:update_KGC_SpecialTransition)
    original_native = original_native.super_method until original_native.owner == Graphics.singleton_class
    original_native = original_native.unbind
    Graphics.singleton_class.send(:define_method, :update_KGC_SpecialTransition) do
      IronmonSpeedUpPacingTests.native_frame { original_native.bind_call(self) }
    end
    $PokemonSystem.speedup = 1
    $CanToggle = true
    $game_temp.in_battle = false
    Ironmon::ToggleSpeed.configure(profile)
    tick
    simulated = test_simulated_60hz
    results = [120, 60].flat_map do |limit|
      @limit = limit
      service.reset_engine
      [1, 2, 3, 6, 10].map { |multiplier| measure(multiplier, limit) }
    end
    output = $ironmon_speed_up_acceptance_output.to_s
    File.binwrite(File.join(output, "pacing.json"), JSON.generate({
      "fixture" => "Actual bundled renderer with one injected 20ms logic stall; display configuration unchanged",
      "simulated_60hz_display" => simulated, "measurements" => results })) if !output.empty?
    puts "Speed-up pacing: simulated 60Hz swaps, native 60/120 budgets, 20/200ms stalls, throughput, and allocation measurements passed"
  ensure
    @native_durations = nil
    Graphics.singleton_class.send(:define_method, :update_KGC_SpecialTransition, original_native) if original_native
    service.define_singleton_method(:native_update_limit, original_limit) if original_limit
    service.reset_engine if service
    Ironmon.clear_tracker_controls
    Ironmon::ToggleSpeed.configure(original_profile) if original_profile
    if original_settings
      $PokemonSystem.speedup, original_stage, $CanToggle, $game_temp.in_battle = original_settings
      tick
      $GameSpeed = original_stage
      tick
    end
    Ironmon.clear_tracker_controls
  end

  def self.limit
    return @limit || 60
  end
end

IronmonSpeedUpPacingTests.run
