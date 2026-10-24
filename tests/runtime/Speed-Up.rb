module IronmonSpeedUpRuntimeTests
  OUTPUT = $ironmon_speed_up_output
  UPDATES = 360

  def self.assert(condition, message)
    raise "Speed-up validation failed: #{message}" if !condition
  end

  def self.now
    return System.uptime.to_f / 1_000_000.0
  end

  def self.controls(pressed)
    Ironmon.receive_tracker_controls({ "enabled" => true,
      "managed" => Ironmon::TRACKER_CONTROL_KEYS.keys, "pressed" => pressed })
    Input.update
  end

  def self.test_cycle
    Ironmon::ToggleSpeed.reset
    Graphics.update
    assert(SPEEDUP_STAGES == [1, 2, 3], "unchanged default cycle")
    $PokemonSystem.speedup = 1
    $GameSpeed = 0
    $frame = 0
    observed = [SPEEDUP_STAGES[$GameSpeed]]
    3.times do
      controls(["speed"])
      Graphics.update
      observed << SPEEDUP_STAGES[$GameSpeed]
      controls([])
      Graphics.update
    end
    assert(observed == [1, 2, 3, 1], "default speed input wraps after 3x")
    controls(["speed"])
    Graphics.update
    held_stage = $GameSpeed
    12.times { controls(["speed"]); Graphics.update }
    assert($GameSpeed == held_stage, "holding toggle does not cycle repeatedly")
    controls([])
    Graphics.update
    pbDisallowSpeedup
    controls(["speed"])
    Graphics.update
    assert($GameSpeed == held_stage, "existing toggle lock is preserved")
    controls([])
    pbAllowSpeedup
    $PokemonSystem.speedup = 0
    $PokemonSystem.speedup_speed = 3
    $PokemonSystem.speedup_speed_battles = 5
    [false, true].each do |in_battle|
      $game_temp.in_battle = in_battle
      controls(["speed"])
      $frame = 0
      before = @native_updates
      expected = in_battle ? 6 : 4
      24.times { Graphics.update; controls(["speed"]) }
      assert(@native_updates - before == 24 / expected, "hold-mode settings retain their original pacing")
      controls([])
    end
    $PokemonSystem.speedup = 1
    $game_temp.in_battle = false
    return observed
  end

  def self.test_profiles
    profile = Ironmon::ToggleSpeed.default_configuration
    ow = profile["lanes"]["overworld"]
    bt = profile["lanes"]["battle"]
    ow[1]["nodes"] << { "id" => "ow-five", "value" => 5 }
    ow[1]["nodes"] << { "id" => "ow-two-again", "value" => 2 }
    bt[1]["nodes"] << { "id" => "bt-eight", "value" => 8 }
    profile["links"][0]["battle"] = "bt-eight"
    Ironmon::ToggleSpeed.configure(profile)
    $game_temp.in_battle = false
    controls([]); Graphics.update
    $GameSpeed = 2; Graphics.update
    assert(SPEEDUP_STAGES == [1, 2, 5, 2, 3], "duplicate and arbitrary positions retained")
    $game_temp.in_battle = true; Graphics.update
    assert(SPEEDUP_STAGES[$GameSpeed] == 8, "first visit uses selected entry, not rank or fastest")
    $GameSpeed = 1; Graphics.update
    $game_temp.in_battle = false; Graphics.update
    assert($GameSpeed == 2 && SPEEDUP_STAGES[$GameSpeed] == 5, "return restores exact member")
    $GameSpeed = 3; Graphics.update
    $game_temp.in_battle = true; Graphics.update
    assert(SPEEDUP_STAGES[$GameSpeed] == 2, "battle group independently remembers its member")
    $game_temp.in_battle = false; Graphics.update
    assert($GameSpeed == 3, "duplicate-valued positions preserve their identity")
    $GameSpeed = 4; Graphics.update
    $game_temp.in_battle = true; Graphics.update
    assert(SPEEDUP_STAGES[$GameSpeed] == 3, "other group has its own connection")
    $GameSpeed = 1; Graphics.update
    $game_temp.in_battle = false; Graphics.update
    assert($GameSpeed == 3, "other group memory survives visiting another pair")
    $GameSpeed = 0; Graphics.update
    $game_temp.in_battle = true; Graphics.update
    assert($GameSpeed == 0, "normal always maps to normal")
    saved = Ironmon::ToggleSpeed.configuration
    invalid = Marshal.load(Marshal.dump(saved)); invalid["links"].pop
    begin
      Ironmon::ToggleSpeed.configure(invalid)
      raise "Incomplete configuration was accepted"
    rescue ArgumentError
      assert(Ironmon::ToggleSpeed.configuration == saved, "rejection preserves committed profile")
    end
    saved["lanes"]["overworld"][1]["nodes"][0]["value"] = 10
    assert(Ironmon::ToggleSpeed.configuration != saved, "profile callers cannot mutate live state")
    $game_temp.in_battle = false
    benchmark = Ironmon::ToggleSpeed.default_configuration
    [6, 10].each do |value|
      ["overworld", "battle"].each do |lane|
        prefix = lane == "overworld" ? "ow" : "bt"
        benchmark["lanes"][lane] << { "id" => "#{prefix}-group-#{value}", "nodes" => [{ "id" => "#{prefix}-#{value}", "value" => value }] }
      end
      benchmark["links"] << { "id" => "matching-#{value}", "overworld" => "ow-#{value}", "battle" => "bt-#{value}" }
    end
    Ironmon::ToggleSpeed.configure(benchmark)
    Graphics.update
    controls(["speed"]); Graphics.update
    controls([]); Graphics.update
    assert(SPEEDUP_STAGES == [1, 2, 3, 6, 10], "custom profile applies including 6x and 10x")
    5.times { controls(["speed"]); Graphics.update; controls([]); Graphics.update }
    return "PASS"
  end

  def self.test_settings_transport
    connection = Ironmon::TrackerConnection.allocate
    messages = []
    connection.define_singleton_method(:queue_message) { |message| messages << message }
    profile = Ironmon::ToggleSpeed.configuration
    request = { "request_id" => "speed-settings", "command" => "update_settings", "payload" => { "toggle_speed" => profile } }
    connection.send(:handle_request, request)
    assert(messages.last["success"] && messages.last["payload"]["toggle_speed"] == profile, "settings route acknowledges accepted profile")
    request["payload"] = { "automatic_reset" => false }
    connection.send(:handle_request, request)
    assert(messages.last["success"] && Ironmon::ToggleSpeed.configuration == profile, "unrelated settings retain custom profile")
    incomplete = Marshal.load(Marshal.dump(profile)); incomplete["links"].pop
    request["payload"] = { "automatic_reset" => true, "toggle_speed" => incomplete }
    connection.send(:handle_request, request)
    assert(messages.last["success"] == false, "settings route rejects incomplete mapping")
    assert(Ironmon::ToggleSpeed.configuration == profile && connection.instance_variable_get(:@automatic_reset) == false, "rejected mapping leaves all live settings unchanged")
    return "PASS"
  end

  def self.measure(scene_name)
    controls([]); Graphics.update
    results = []
    2.times do |repeat|
      SPEEDUP_STAGES.each_with_index do |multiplier, index|
        $GameSpeed = index
        $frame = 0
        12.times { controls([]); yield }
        $frame = 0
        Graphics.frame_reset
        before = @native_updates
        durations = []
        started = now
        UPDATES.times do
          tick = now
          controls([])
          yield
          durations << (now - tick) * 1000
        end
        elapsed = now - started
        rendered = @native_updates - before
        assert(rendered == UPDATES / multiplier, "#{scene_name}: native rendering cadence at #{multiplier}x")
        ordered = durations.sort
        result = { "scene" => scene_name, "repeat" => repeat + 1,
          "target_multiplier" => multiplier, "updates" => UPDATES,
          "seconds" => elapsed.round(4), "updates_per_second" => (UPDATES / elapsed).round(2),
          "native_updates" => rendered, "p95_update_ms" => ordered[(ordered.length * 0.95).floor].round(3),
          "maximum_update_ms" => ordered.last.round(3) }
        results << result
        File.binwrite(File.join(OUTPUT, "progress.json"), Ironmon.tracker_json_generate(result))
      end
    end
    capture("#{scene_name}-6x.png")
    return results
  end

  def self.capture(filename)
    image = Graphics.snap_to_bitmap
    image.save_to_png(File.join(OUTPUT, filename))
    image.dispose
  end

  def self.map_scene
    $MapFactory = PokemonMapFactory.new(10)
    $game_player.moveto($game_map.width / 2, $game_map.height / 2)
    $game_player.refresh
    $PokemonEncounters = PokemonEncounters.new
    $PokemonEncounters.setup($game_map.map_id)
    $scene = Scene_Map.new
    $scene.createSpritesets
    return $scene
  end

  def self.battle_scene
    $game_temp.in_battle = true
    pokemon = Pokemon.new(:BULBASAUR, 20, nil, false)
    enemy = Pokemon.new(:PIDGEY, 20, nil, false)
    pokemon.moves.replace([Pokemon::Move.new(:TACKLE), Pokemon::Move.new(:GROWL)])
    $Trainer = Player.new("Speed-up test", :POKEMONTRAINER_Red)
    $Trainer.party = [pokemon]
    scene = PokeBattle_Scene.new
    battle = PokeBattle_Battle.new(scene, [pokemon], [enemy], $Trainer, nil)
    battle.backdrop = "indoora"
    [pokemon, enemy].each_with_index do |individual, index|
      battle.positions[index] = PokeBattle_ActivePosition.new
      battler = PokeBattle_Battler.new(battle, index)
      battle.battlers[index] = battler
      battler.pbInitialize(individual, 0)
      battle.choices[index] = [:None, 0, nil, -1]
    end
    scene.define_singleton_method(:pbBattleIntroAnimation) { }
    scene.define_singleton_method(:pbCreateTrainerBackSprite) { |*_args| }
    scene.pbStartBattle(battle)
    scene.pbChangePokemon(0, pokemon)
    scene.sprites["pokemon_0"].visible = true
    scene.sprites["pokemon_0"].tone = Tone.new(0, 0, 0)
    scene.sprites["dataBox_0"].visible = true
    scene.sprites["dataBox_1"].visible = true
    scene.pbShowWindow(PokeBattle_Scene::COMMAND_BOX)
    scene.sprites["commandWindow"].setTexts(["What will Bulbasaur do?", "Fight", "Bag", "Pokemon", "Run"])
    scene.sprites["commandWindow"].setIndexAndMode(0, 0)
    return scene
  end

  def self.test_map_movement(map)
    $GameSpeed = 3
    $frame = 0
    controls([])
    start_x = $game_player.x
    start_y = $game_player.y
    original_through = $game_player.through
    $game_player.through = true
    $game_system.encounter_disabled = true
    $game_player.move_right
    48.times { Graphics.update; controls([]); map.update }
    assert($game_player.x == start_x + 1 && $game_player.y == start_y && !$game_player.moving?,
      "native map movement completes at 6x")
    $game_player.moveto(start_x, start_y)
    return "PASS"
  ensure
    $game_player.through = original_through
  end

  def self.overlay_profile(count)
    profile = { "lanes" => {}, "links" => [] }
    ["overworld", "battle"].each do |lane|
      profile["lanes"][lane] = [
        { "id" => "#{lane}-normal", "nodes" => [{ "id" => "#{lane}-one", "value" => 1 }] },
        { "id" => "#{lane}-fast", "nodes" => Array.new(count - 1) do |index|
          { "id" => "#{lane}-#{index}", "value" => 2 + index % 9 }
        end }
      ]
    end
    profile["links"] << { "id" => "fast", "overworld" => "overworld-0", "battle" => "battle-0" }
    return profile
  end

  def self.test_overlays
    original_profile = Ironmon::ToggleSpeed.configuration
    original_mode = $PokemonGlobal.ironmon_mode
    original_repel = $PokemonGlobal.repel
    original_temporary = $PokemonGlobal.tempRepel
    original_speed = $GameSpeed
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.repel = 120
    $PokemonGlobal.tempRepel = false
    controls([])
    Ironmon::ToggleSpeed.configure(Ironmon::ToggleSpeed.default_configuration)
    Graphics.update
    $GameSpeed = 1; Graphics.update
    speed = Ironmon::StatusOverlays.instance_variable_get(:@speed)
    repel = Ironmon::StatusOverlays.instance_variable_get(:@repel)
    assert(speed && speed.sprite.visible && repel.sprite.visible, "production graphics hook creates both status overlays")
    assert(speed.sprite.x == 358 && speed.sprite.y == 8 && repel.sprite.x == 358 && repel.sprite.y == 38, "approved fixed top-right layout and matching panel widths")
    assert(speed.sprite.viewport.z > 99_999, "speed stays above the native battle viewport")
    [speed.sprite.bitmap, repel.sprite.bitmap].each do |bitmap|
      assert(bitmap.width == 146 && bitmap.height == 26, "matching native status dimensions")
      assert(bitmap.get_pixel(100, 3).alpha == 191, "background alone uses 75 percent alpha")
      assert(bitmap.get_pixel(0, 0).alpha == 0, "rounded panel corners remain transparent")
    end
    capture("overlay-overworld-default.png")
    $PokemonGlobal.repel = 0; Graphics.update
    assert(!repel.sprite.visible && speed.sprite.y == 8, "Repel expiry never moves speed")
    $PokemonGlobal.repel = 120
    $game_temp.in_battle = true; Graphics.update
    assert(!repel.sprite.visible && speed.sprite.y == 8, "battle hides Repel without moving speed")
    $game_temp.in_battle = false; Graphics.update
    assert(repel.sprite.visible && repel.sprite.y == 38, "overworld restores Repel below speed")
    $GameSpeed = 0; Graphics.update
    assert(!speed.sprite.visible && repel.sprite.y == 8, "normal speed hides speed and lets Repel occupy the top slot")
    $PokemonSystem.speedup = 0; Graphics.update
    assert(!speed.sprite.visible, "hold mode never shows the toggle overlay")
    $PokemonSystem.speedup = 1
    [2, 3, 10, 25, 128].each do |count|
      Ironmon::ToggleSpeed.configure(overlay_profile(count)); Graphics.update
      (1...count).each do |index|
        $GameSpeed = index; Graphics.update
        current = Ironmon::ToggleSpeed.overlay_state
        assert(current[1] == index && current[2] == count, "exact position retained across repeated values in #{count}-position cycle")
        bitmap = speed.sprite.bitmap
        end_stop = bitmap.get_pixel(133, 6)
        assert((end_stop.alpha == 255) == (index == count - 1), "only final position has the tall end stop, including dense cycles")
        assert(speed.sprite.x == 358 && speed.sprite.y == 8, "position and value never change speed placement")
      end
      if count == 10
        $GameSpeed = 8; Graphics.update
        symbol = speed.sprite.bitmap.raw_data
        capture("overlay-overworld-9x.png")
        $GameSpeed = 9; Graphics.update
        after = speed.sprite.bitmap.raw_data
        capture("overlay-overworld-10x.png")
        bitmap = speed.sprite.bitmap
        symbol_right = IronmonSpeedOverlay::MULTIPLIER_X + IronmonSpeedOverlay::MULTIPLIER_WIDTH
        symbol_x = symbol_right - bitmap.text_size("×").width
        (5...21).each do |y|
          ((symbol_x + 1)...symbol_right).each do |x|
            offset = (y * 146 + x) * 4
            assert(symbol[offset, 4] == after[offset, 4], "multiplier symbol does not shift between 9x and 10x")
          end
        end
      elsif count == 25
        $GameSpeed = 23; Graphics.update
        capture("overlay-overworld-before-last.png")
        $GameSpeed = 24; Graphics.update
        capture("overlay-overworld-last.png")
      end
    end
    previous = speed.sprite.bitmap.raw_data
    20.times { Graphics.update }
    assert(speed.sprite.bitmap.raw_data == previous, "unchanged state leaves its cached bitmap intact")
    $PokemonGlobal.ironmon_mode = false; Graphics.update
    assert(speed.disposed? && repel.disposed? && !Ironmon::StatusOverlays.instance_variable_get(:@viewport), "leaving Ironmon releases native overlay resources")
    return "PASS"
  ensure
    Ironmon::StatusOverlays.dispose
    $PokemonGlobal.ironmon_mode = original_mode
    $PokemonGlobal.repel = original_repel
    $PokemonGlobal.tempRepel = original_temporary
    $PokemonSystem.speedup = 1
    $game_temp.in_battle = false
    Ironmon::ToggleSpeed.configure(original_profile)
    Graphics.update
    $GameSpeed = original_speed
    Graphics.update
  end

  def self.capture_battle_overlay
    original_mode = $PokemonGlobal.ironmon_mode
    original_repel = $PokemonGlobal.repel
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.repel = 120
    $GameSpeed = 4; Graphics.update
    speed = Ironmon::StatusOverlays.instance_variable_get(:@speed)
    repel = Ironmon::StatusOverlays.instance_variable_get(:@repel)
    assert(speed.sprite.visible && !repel.sprite.visible, "native battle renders speed alone")
    capture("overlay-battle-10x.png")
  ensure
    $PokemonGlobal.ironmon_mode = original_mode
    $PokemonGlobal.repel = original_repel
    Ironmon::StatusOverlays.dispose
  end

  def self.run
    Game.initialize
    SaveData.load_new_game_values
    $PokemonSystem.speedup = 1
    Graphics.resize_screen(Settings::SCREEN_WIDTH, Settings::SCREEN_HEIGHT)
    Graphics.singleton_class.class_eval do
      alias speed_up_validation_native_update fast_forward_update
      define_method(:fast_forward_update) do
        IronmonSpeedUpRuntimeTests.native_update
        speed_up_validation_native_update
      end
    end
    @native_updates = 0
    cycle = test_cycle
    profiles = test_profiles
    transport = test_settings_transport
    map = map_scene
    overlays = test_overlays
    if $ironmon_speed_up_interactive
      interactive_map(map)
      movement = test_map_movement(map)
      map.dispose
      battle = battle_scene
      interactive_battle(battle)
      behavior = test_battle_behavior(battle)
      behavior["native_map_movement_at_6x"] = movement
      File.binwrite(File.join(OUTPUT, "interaction-behavior.json"), Ironmon.tracker_json_generate(behavior))
      interactive_report = { "observed_states" => @interactive_events,
        "overworld_speeds_observed" => @interactive_events.select { |event| event["scene"] == "Overworld" }.map { |event| event["speed"] }.uniq,
        "battle_speeds_observed" => @interactive_events.select { |event| event["scene"] == "Battle" }.map { |event| event["speed"] }.uniq }
      File.binwrite(File.join(OUTPUT, "interactive.json"), Ironmon.tracker_json_generate(interactive_report))
      battle.pbDisposeSprites
      battle.viewport.dispose
      return
    end
    movement = test_map_movement(map)
    results = measure("overworld") { Graphics.update; map.update }
    map.dispose
    battle = battle_scene
    capture_battle_overlay
    results.concat(measure("battle") { battle.pbUpdate(battle.sprites["commandWindow"]) })
    behavior = test_battle_behavior(battle)
    report = { "status" => "PASS", "toggle_cycle" => cycle,
      "profiles_and_transitions" => profiles, "settings_transport" => transport,
      "speed_and_repel_overlays" => overlays,
      "hold_mode_unchanged" => true, "toggle_hold_and_lock_passed" => true,
      "native_map_movement_at_6x" => movement,
      "fixture" => "Disposable map 10 and native battle scene; Ironmon hooks loaded; no player saves or live tracker connection",
      "measurements" => results, "battle_behavior" => behavior }
    File.binwrite(File.join(OUTPUT, "result.json"), Ironmon.tracker_json_generate(report))
    battle.pbDisposeSprites
    battle.viewport.dispose
  end

  def self.native_update
    @native_updates += 1
  end

  def self.test_battle_behavior(scene)
    $GameSpeed = 3
    $frame = 0
    frames = 0
    scene.define_singleton_method(:pbInputUpdate) do
      frames += 1
      raise "Battle command input timed out" if frames > 30
      pressed = { 2 => ["right"], 4 => ["down"], 6 => ["confirm"] }.fetch(frames, [])
      Ironmon.receive_tracker_controls({ "enabled" => true,
        "managed" => Ironmon::TRACKER_CONTROL_KEYS.keys, "pressed" => pressed })
      super()
    end
    scene.sprites["commandWindow"].setIndexAndMode(0, 0)
    scene.setLastCommandIndex(0, 0)
    command = scene.pbCommandMenu(0, true)
    assert(command == 3, "native battle command navigation and confirmation at 6x")
    scene.singleton_class.remove_method(:pbInputUpdate)
    controls([])
    battle = scene.instance_variable_get(:@battle)
    animations = [2, 3].map do |index|
      $GameSpeed = index
      $frame = 0
      Graphics.frame_reset
      before = @native_updates
      started = now
      scene.pbAnimation(:TACKLE, battle.battlers[0], battle.battlers[1])
      duration = now - started
      rendered = @native_updates - before
      assert(rendered > 0, "native Tackle animation played at #{SPEEDUP_STAGES[index]}x")
      { "multiplier" => SPEEDUP_STAGES[index], "seconds" => duration.round(4),
        "native_updates" => rendered }
    end
    return { "native_command_navigation_at_6x" => "PASS", "tackle_animations" => animations }
  end

  def self.interactive_status(label, scene)
    state = { "scene" => scene, "speed" => SPEEDUP_STAGES[$GameSpeed],
      "x" => $game_player.x, "y" => $game_player.y }
    if !@interactive_events || @interactive_events.last != state
      @interactive_events ||= []
      @interactive_events << state
      File.binwrite(File.join(OUTPUT, "interactive-progress.json"), Ironmon.tracker_json_generate(state))
      label.text = "#{scene}: #{state['speed']}x | Speed key: cycle | F6: next"
    end
  end

  def self.interactive_map(map)
    Ironmon.clear_tracker_controls
    $GameSpeed = 0
    $frame = 0
    label = Window_UnformattedTextPokemon.newWithSize("", 0, 0, Graphics.width, 64)
    label.z = 999999
    deadline = now + 120
    loop do
      Graphics.update
      Input.update
      interactive_status(label, "Overworld")
      break if Input.trigger?(Input::F6) || now >= deadline
      map.update
    end
    capture("interactive-overworld.png")
  ensure
    label.dispose if label
  end

  def self.interactive_battle(battle)
    label = Window_UnformattedTextPokemon.newWithSize("", 0, 0, Graphics.width, 64)
    label.z = 999999
    deadline = now + 120
    battle.define_singleton_method(:pbInputUpdate) do
      super()
      IronmonSpeedUpRuntimeTests.interactive_status(label, "Battle")
      if Input.trigger?(Input::F6) || IronmonSpeedUpRuntimeTests.now >= deadline
        throw :speed_up_interactive_done
      end
    end
    catch(:speed_up_interactive_done) { battle.pbCommandMenu(0, true) }
    battle.singleton_class.remove_method(:pbInputUpdate)
    capture("interactive-battle.png")
  ensure
    label.dispose if label
  end
end

Ironmon::TrackerConnection.class_eval do
  def update
    return
  end
end

module Game
  def self.save(*_args)
    raise "Saving is disabled in the speed-up validation session"
  end
end

module Ironmon
  def self.wait_for_tracker_session
    return
  end
end

IronmonSpeedUpRuntimeTests.run
