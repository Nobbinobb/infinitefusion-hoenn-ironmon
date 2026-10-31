module IronmonSpeedClockRuntimeTests
  def self.assert(condition, message)
    raise "Speed clock validation failed: #{message}" if !condition
  end

  def self.tick(multiplier, frozen = false)
    Ironmon::SpeedClock.begin_tick
    Ironmon::SpeedClock.set_raw_frame_count(Ironmon::SpeedClock.raw_frame_count + 1) if !frozen
    Ironmon::SpeedClock.advance(multiplier, frozen)
  end

  def self.test_counters
    Graphics.frame_count = 400
    100.times { tick(10) }
    assert(Graphics.frame_count == 500, "simulation frames advance at accelerated speed")
    assert(Ironmon::SpeedClock.playtime_frame_count == 410, "playtime advances at normal nominal speed")
    300.times { tick(3) }
    assert(Ironmon::SpeedClock.playtime_frame_count == 510, "fractional multipliers do not lose whole frames to rounding")
    before = Graphics.frame_count
    20.times { tick(10, true) }
    assert(Graphics.frame_count == before && Ironmon::SpeedClock.playtime_frame_count == 510,
      "frozen frames advance neither simulation nor playtime")
    Ironmon::SpeedClock.with_playtime_frame_count do
      assert(Graphics.frame_count == 510, "time displays read the unaccelerated clock")
      tick(1)
      assert(Graphics.frame_count == before + 1, "animated messages leave the drawing clock scope")
    end
    assert(Graphics.frame_count == before + 1, "drawing restores the simulation counter")
    begin
      Ironmon::SpeedClock.with_playtime_frame_count { raise "clock display interruption" }
    rescue RuntimeError => error
      raise if error.message != "clock display interruption"
    end
    assert(Graphics.frame_count == before + 1, "display exceptions restore the simulation counter")
    Ironmon::SpeedClock.begin_tick
    Ironmon::SpeedClock.cancel_tick
    assert(!Ironmon::SpeedClock.displaying_playtime?, "interrupted ticks release their clock scope")
  end

  def self.test_save_roundtrip
    Graphics.frame_count = 1_000
    100.times { tick(10) }
    saved = SaveData.compile_save_hash
    assert(saved[:frame_count] == 1_010 && saved[:ironmon_logical_frame_count] == 1_100,
      "save slots retain real playtime and preserve logical timestamps separately")
    assert(saved[:ironmon_speed_clock_version] == 1 && SaveData.valid?(saved),
      "clock additions leave the native save schema valid")
    Graphics.frame_count = 0
    SaveData.mark_values_as_unloaded
    SaveData.load_all_values(saved)
    assert(Graphics.frame_count == 1_100 && Ironmon::SpeedClock.playtime_frame_count == 1_010,
      "native in-memory loading restores both clocks")
    tick(10)
    SaveData.load_all_values(saved)
    assert(Graphics.frame_count == 1_101, "already-loaded save values do not rewind the live clocks")
    legacy = saved.reject do |key, _value|
      [Ironmon::SpeedClock::LOGICAL_SAVE_KEY, Ironmon::SpeedClock::VERSION_SAVE_KEY].include?(key)
    end
    assert(SaveData.valid?(legacy), "old saves require no new fields")
    SaveData.mark_values_as_unloaded
    SaveData.load_all_values(legacy)
    assert(Graphics.frame_count == 1_010 && Ironmon::SpeedClock.playtime_frame_count == 1_010,
      "legacy saves initialize matching clocks without a migration")
    future = saved.merge(Ironmon::SpeedClock::VERSION_SAVE_KEY => 99)
    SaveData.mark_values_as_unloaded
    SaveData.load_all_values(future)
    assert(Graphics.frame_count == 1_010, "unrecognized optional clock versions use the native saved clock")
  end

  def self.test_native_wait
    original_mode = $PokemonSystem.speedup
    original_speed = $GameSpeed
    $PokemonSystem.speedup = 1
    Ironmon::ToggleSpeed.before_update
    $GameSpeed = 2
    Graphics.update
    Graphics.frame_count = 0
    Graphics.wait(6)
    expected = (6 / Ironmon::SpeedClock.native_scale).round
    assert(Ironmon::SpeedClock.playtime_frame_count == expected,
      "native waits outside the Ruby game loop contribute at the actual native limiter rate")
    before = Graphics.frame_count
    tick(3)
    assert(Graphics.frame_count == before + 1, "native waits preserve subsequent logical timing")
  ensure
    $PokemonSystem.speedup = original_mode
    $GameSpeed = original_speed
    Graphics.update
  end

  def self.run
    saved = SaveData.compile_save_hash
    test_counters
    test_save_roundtrip
    test_native_wait
    output = $ironmon_speed_up_acceptance_output.to_s
    File.binwrite(File.join(output, "speed-clock.txt"), "PASS: logical timing, playtime, native waits, old and new save loading\n") if !output.empty?
  ensure
    if saved
      SaveData.mark_values_as_unloaded
      SaveData.load_all_values(saved)
    end
  end
end

IronmonSpeedClockRuntimeTests.run
