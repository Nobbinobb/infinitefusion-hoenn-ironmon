module IronmonSpeedUpInteractionTests
  MOVEMENT_SECONDS = 0.20
  MENU_SECONDS = 0.95

  def self.assert(condition, message)
    raise "Speed-up interaction failed: #{message}" if !condition
  end

  def self.now
    return Process.clock_gettime(Process::CLOCK_MONOTONIC)
  end

  def self.controls(pressed)
    Ironmon.receive_tracker_controls({ "enabled" => true,
      "managed" => Ironmon::TRACKER_CONTROL_KEYS.keys, "pressed" => pressed })
  end

  def self.tick(pressed = [])
    controls(pressed)
    Graphics.update
    Input.update
    yield if block_given?
  end

  def self.speed(multiplier)
    tick
    $GameSpeed = SPEEDUP_STAGES.index(multiplier)
    assert(!$GameSpeed.nil?, "the requested #{multiplier}x stage exists")
    tick
    assert(Ironmon::SpeedUp.multiplier == multiplier, "the selected #{multiplier}x stage is active")
    Graphics.frame_reset
  end

  def self.profile
    result = Ironmon::ToggleSpeed.default_configuration
    Ironmon::ToggleSpeed::LANES.each do |lane|
      result["lanes"][lane] << { "id" => "interaction-#{lane}-fast",
        "nodes" => [{ "id" => "interaction-#{lane}-10", "value" => 10 }] }
    end
    result["links"] << { "id" => "interaction-fast",
      "overworld" => "interaction-overworld-10", "battle" => "interaction-battle-10" }
    return result
  end

  def self.map_scene
    $MapFactory = PokemonMapFactory.new(10)
    $game_map.events.clear
    $game_player.moveto(1, $game_map.height / 2)
    $game_player.refresh
    $game_player.through = true
    $game_system.encounter_disabled = true
    $PokemonEncounters = PokemonEncounters.new
    $PokemonEncounters.setup($game_map.map_id)
    $scene = Scene_Map.new
    $scene.createSpritesets
    return $scene
  end

  def self.test_movement(scene)
    results = [1, 10].map do |multiplier|
      speed(multiplier)
      $game_player.moveto(1, $game_map.height / 2)
      12.times { tick { scene.update } }
      start = [$game_player.x, $game_player.y]
      started = now
      ticks = 0
      while now - started < MOVEMENT_SECONDS
        tick(["right"]) { scene.update }
        ticks += 1
      end
      elapsed = now - started
      released = [$game_player.x, $game_player.y]
      tick { scene.update }
      assert(Input.dir4 == 0, "forwarded direction releases immediately at #{multiplier}x")
      settling_ticks = 0
      while $game_player.moving? && settling_ticks < 64
        tick { scene.update }
        settling_ticks += 1
      end
      assert(!$game_player.moving?, "the in-progress tile finishes after release at #{multiplier}x")
      assert([$game_player.x, $game_player.y] == released,
        "releasing direction cannot start another tile at #{multiplier}x")
      48.times { tick { scene.update } }
      assert([$game_player.x, $game_player.y] == released && !$game_player.moving?,
        "the player stays stopped across another 48 logical ticks at #{multiplier}x")
      distance = released[0] - start[0]
      assert(distance > 0 && released[1] == start[1],
        "forwarded right input moves the actual player at #{multiplier}x")
      { "multiplier" => multiplier, "held_seconds" => elapsed,
        "logical_updates" => ticks, "tiles_moved" => distance,
        "settling_updates" => settling_ticks, "stayed_stopped_updates" => 48 }
    end
    assert(results[1]["tiles_moved"] > results[0]["tiles_moved"],
      "a real 10x movement gesture travels farther than the same 1x gesture")
    return results
  end

  def self.capture(filename)
    output = $ironmon_speed_up_acceptance_output.to_s
    return if output.empty?
    bitmap = Graphics.snap_to_bitmap
    bitmap.save_to_png(File.join(output, filename))
  ensure
    bitmap.dispose if bitmap
  end

  def self.test_menu
    previous_menu = $game_temp.in_menu
    $game_temp.in_menu = true
    window = Window_CommandPokemonEx.new(Array.new(12) { |index| "Choice #{index + 1}" })
    window.width = 250
    window.height = [window.height, Graphics.height - 32].min
    window.x = (Graphics.width - window.width) / 2
    window.y = 16
    window.z = 99999
    window.active = true
    results = [1, 10].map do |multiplier|
      speed(multiplier)
      window.index = 0
      started = now
      changes = []
      previous = window.index
      while now - started < MENU_SECONDS
        tick(["down"]) { window.update }
        if window.index != previous
          changes << { "index" => window.index, "seconds" => now - started }
          previous = window.index
        end
      end
      elapsed = now - started
      selected = window.index
      assert(selected.between?(3, 5),
        "a held direction navigates a normal number of menu entries at #{multiplier}x")
      assert(changes.length >= 2 && changes[1]["seconds"].between?(0.50, 0.72),
        "the real command window waits before auto-repeat at #{multiplier}x")
      24.times { tick { window.update } }
      assert(window.index == selected,
        "the real command window stops scrolling on release at #{multiplier}x")
      capture("interaction-menu-#{multiplier}x.png")
      { "multiplier" => multiplier, "held_seconds" => elapsed,
        "selected_index" => selected, "changes" => changes,
        "stayed_stopped_updates" => 24 }
    end
    assert((results[0]["selected_index"] - results[1]["selected_index"]).abs <= 1,
      "normal command-window repeat cadence stays consistent between 1x and 10x")
    return results
  ensure
    window.dispose if window
    $game_temp.in_menu = previous_menu
  end

  def self.run
    original_profile = Ironmon::ToggleSpeed.configuration
    original_settings = [$PokemonSystem.speedup, $GameSpeed, $CanToggle]
    original_globals = [$scene, $MapFactory, $game_map, $game_player,
      $PokemonEncounters, $game_temp, $PokemonTemp, $game_system,
      $PokemonGlobal, $Trainer, $game_switches, $game_variables, $game_screen]
    $game_temp = Game_Temp.new
    $PokemonTemp = PokemonTemp.new
    $game_player = Game_Player.new
    $game_system = Game_System.new
    $game_screen = Game_Screen.new
    $PokemonGlobal = Marshal.load(Marshal.dump($PokemonGlobal))
    $Trainer = Marshal.load(Marshal.dump($Trainer))
    $game_switches = Marshal.load(Marshal.dump($game_switches))
    $game_variables = Marshal.load(Marshal.dump($game_variables))
    $PokemonSystem.speedup = 1
    $CanToggle = true
    Ironmon::ToggleSpeed.configure(profile)
    tick
    scene = map_scene
    movement = test_movement(scene)
    menu = test_menu
    output = $ironmon_speed_up_acceptance_output.to_s
    File.binwrite(File.join(output, "interaction.json"), JSON.generate({
      "movement" => movement, "command_window" => menu })) if !output.empty?
    puts "Speed-up interaction: actual held movement, release and stop, and normal command-window repeat at 1x/10x passed"
  ensure
    scene.dispose if scene
    $scene, $MapFactory, $game_map, $game_player,
      $PokemonEncounters, $game_temp, $PokemonTemp, $game_system,
      $PokemonGlobal, $Trainer, $game_switches, $game_variables, $game_screen = original_globals if original_globals
    Ironmon.clear_tracker_controls
    Ironmon::ToggleSpeed.configure(original_profile) if original_profile
    if original_settings
      $PokemonSystem.speedup, original_stage, $CanToggle = original_settings
      tick
      $GameSpeed = original_stage
      tick
    end
    Ironmon.clear_tracker_controls
  end
end

IronmonSpeedUpInteractionTests.run
