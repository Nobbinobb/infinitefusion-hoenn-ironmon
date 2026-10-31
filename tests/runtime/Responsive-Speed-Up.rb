module IronmonResponsiveSpeedUpTests
  def self.assert(condition, message)
    raise "Responsive speed-up failed: #{message}" if !condition
  end

  def self.now
    return Process.clock_gettime(Process::CLOCK_MONOTONIC)
  end

  def self.controls(pressed = [])
    Ironmon.receive_tracker_controls({ "enabled" => true,
      "managed" => Ironmon::TRACKER_CONTROL_KEYS.keys, "pressed" => pressed })
    Input.update
  end

  def self.speed(multiplier)
    $PokemonSystem.speedup = 1
    controls
    Graphics.update
    $GameSpeed = SPEEDUP_STAGES.index(multiplier)
    Graphics.update
    Graphics.frame_reset
  end

  def self.test_toggle_edge
    speed(1)
    controls(["speed"])
    Graphics.update
    stage = $GameSpeed
    6.times { Graphics.update }
    assert(stage == 1 && $GameSpeed == stage,
      "one input snapshot changes speed exactly once despite repeated graphics updates")
    controls
    Graphics.update
  end

  def self.test_clocks
    [1, 2, 3, 6, 10].each do |multiplier|
      speed(multiplier)
      count = Graphics.frame_count
      60.times { Graphics.update; controls }
      assert(Graphics.frame_count - count == 60,
        "every logical tick advances animations at #{multiplier}x")
      assert(Graphics.frame_rate == 40, "game timers retain their base frame rate")
      assert(Ironmon::SpeedUp::NATIVE_RATE.call == [40 * multiplier, Ironmon::SpeedUp.native_update_limit].min,
        "the native limiter stays within the presentation budget")
      assert((Graphics.delta_s - 0.025).abs < 0.00001,
        "delta-driven animations share logical time") if multiplier > 1
    end
  end

  def self.test_transitions
    speed(10)
    Graphics.freeze
    count = Graphics.frame_count
    12.times { Graphics.update; controls }
    assert(Graphics.frame_count == count, "frozen graphics do not advance the logical counter")
    Graphics.transition(8, "fadetoblack")
    assert(!Ironmon::SpeedUp.frozen, "custom transition releases frozen graphics")
    count = Graphics.frame_count
    12.times { Graphics.update; controls }
    assert(Graphics.frame_count - count == 12, "logical updates resume after a transition")
    Graphics.freeze
    Graphics.transition(8, "missing-transition-test-image")
    assert(!Ironmon::SpeedUp.frozen, "native transition fallback releases frozen graphics")
    Graphics.fadeout(4)
    Graphics.fadein(4)
    Graphics.wait(4)
  end

  def self.test_stall
    speed(10)
    sleep(0.15)
    started = now
    40.times { Graphics.update; controls }
    elapsed = now - started
    assert(elapsed >= 0.06, "a stalled game resumes paced updates without a catch-up burst")
  end

  def self.test_music
    results = []
    Audio.bgm_play("Audio/BGM/art_gallery.ogg", 0, 100)
    [1, 3, 10].each do |multiplier|
      speed(multiplier)
      started = now
      position = Audio.bgm_pos
      frames = 0
      while now - started < 1.2
        Graphics.update
        controls
        frames += 1
      end
      elapsed = now - started
      music = Audio.bgm_pos - position
      assert((music - elapsed).abs < 0.18,
        "music advances in real seconds at #{multiplier}x (#{music}/#{elapsed})")
      actual = frames / elapsed / Graphics.frame_rate
      assert(actual.between?(multiplier * 0.75, multiplier * 1.25),
        "logical update throughput follows #{multiplier}x (#{actual})")
      results << { "multiplier" => multiplier, "seconds" => elapsed,
        "music_seconds" => music, "logical_updates" => frames,
        "measured_multiplier" => actual }
    end
    File.binwrite(File.join($ironmon_speed_up_output, "music-and-pacing.json"), JSON.generate(results))
  ensure
    Audio.bgm_stop
  end

  def self.test_engine_reset
    speed(3)
    Graphics.freeze
    Graphics.__reset__
    Graphics.update
    assert(!Ironmon::SpeedUp.frozen && Ironmon::SpeedUp::NATIVE_RATE.call == Ironmon::SpeedUp.native_update_limit,
      "native graphics reset restores the selected limiter and releases frozen state")
    count = Graphics.frame_count
    12.times { Graphics.update; controls }
    assert(Graphics.frame_count - count == 12, "graphics continue updating after a native reset")
  end

  def self.run
    original_profile = Ironmon::ToggleSpeed.configuration
    original_system = [$PokemonSystem.speedup, $GameSpeed, $game_temp.in_battle]
    profile = Ironmon::ToggleSpeed.default_configuration
    [6, 10].each do |value|
      Ironmon::ToggleSpeed::LANES.each do |lane|
        profile["lanes"][lane] << { "id" => "#{lane}-group-#{value}",
          "nodes" => [{ "id" => "#{lane}-#{value}", "value" => value }] }
      end
      profile["links"] << { "id" => "matching-#{value}",
        "overworld" => "overworld-#{value}", "battle" => "battle-#{value}" }
    end
    $game_temp.in_battle = false
    $CanToggle = true
    Ironmon::ToggleSpeed.configure(profile)
    Graphics.update
    test_toggle_edge
    test_clocks
    test_transitions
    test_stall
    test_music
    test_engine_reset
    puts "Responsive speed-up: input edges, logical clocks, native limiter, freeze/transitions/fades/waits, stall recovery, and real-time music passed"
  ensure
    controls
    Ironmon::ToggleSpeed.configure(original_profile)
    $PokemonSystem.speedup, $GameSpeed, $game_temp.in_battle = original_system
    Graphics.update
    Ironmon.clear_tracker_controls
  end
end

IronmonResponsiveSpeedUpTests.run
