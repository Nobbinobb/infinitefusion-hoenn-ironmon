module IronmonSpeedTransitionsTests
  def self.assert(condition, message)
    raise "Speed transition validation failed: #{message}" if !condition
  end

  def self.now
    return Process.clock_gettime(Process::CLOCK_MONOTONIC)
  end

  def self.profile
    result = Ironmon::ToggleSpeed.default_configuration
    Ironmon::ToggleSpeed::LANES.each do |lane|
      result["lanes"][lane] << { "id" => "transition-#{lane}-fast",
        "nodes" => [{ "id" => "transition-#{lane}-10", "value" => 10 }] }
    end
    result["links"] << { "id" => "transition-fast",
      "overworld" => "transition-overworld-10", "battle" => "transition-battle-10" }
    return result
  end

  def self.set_speed(multiplier)
    Ironmon.clear_tracker_controls
    Input.update
    Graphics.update
    $GameSpeed = SPEEDUP_STAGES.index(multiplier)
    Graphics.update
    Ironmon::SpeedUp.reset_engine
  end

  def self.measure(name, multiplier, limit)
    set_speed(multiplier)
    Graphics.brightness = 255
    Graphics.frame_reset
    @native_updates = 0
    before = Ironmon::SpeedClock.raw_frame_count
    started = now
    yield
    elapsed = now - started
    assert(!Ironmon::SpeedUp.frozen, "#{name} releases frozen graphics")
    if defined?(Ironmon::SpeedTransitions) && name == "coded SnakeSquares"
      expected_speed = multiplier > 3 ? 3 : multiplier
      expected_native = (21 * [40 * expected_speed, limit].min.to_f / (40 * expected_speed)).ceil
      assert(@native_updates == expected_native,
        "the coded wipe retains every visual step at its limited speed")
      expected = 20.0 / (40 * expected_speed)
      assert(elapsed.between?(expected * 0.7, expected + 0.15),
        "the coded wipe finishes at a readable accelerated duration")
    elsif defined?(Ironmon::SpeedTransitions) && name == "native fadeout"
      expected_speed = multiplier > 3 ? 3 : multiplier
      expected = 20.0 / (40 * expected_speed)
      assert(elapsed.between?(expected * 0.7, expected + 0.15),
        "the native fade duration follows the same visual speed at either native budget")
    end
    return { "operation" => name, "multiplier" => multiplier,
      "native_limit" => limit, "milliseconds" => elapsed * 1000,
      "native_updates" => @native_updates,
      "native_average_frame_rate" => Graphics.average_frame_rate,
      "logical_frames" => Ironmon::SpeedClock.raw_frame_count - before }
  end

  def self.test_scopes
    set_speed(10)
    selected = $GameSpeed
    Ironmon::SpeedTransitions.with_visual_speed do
      assert(Ironmon::SpeedUp.multiplier == 3, "visual scope limits acceleration")
      Ironmon::SpeedTransitions.with_visual_speed do
        assert(Ironmon::SpeedUp.multiplier == 3, "nested visual scopes keep the same limit")
      end
      Ironmon::SpeedTransitions.without_visual_speed do
        assert(Ironmon::SpeedUp.multiplier == 10, "the battle body receives the selected gameplay speed")
      end
      assert(Ironmon::SpeedUp.multiplier == 3, "leaving the battle body restores visual speed")
    end
    assert(Ironmon::SpeedUp.multiplier == 10 && $GameSpeed == selected,
      "visual scopes restore the exact selected stage")
    begin
      Ironmon::SpeedTransitions.with_visual_speed { raise "test transition interruption" }
    rescue RuntimeError => error
      raise if error.message != "test transition interruption"
    end
    assert(!Ironmon::SpeedTransitions.active? && Ironmon::SpeedUp.multiplier == 10,
      "an interrupted visual scope restores gameplay speed")
    before_playtime = Ironmon::SpeedClock.playtime_frame_count
    Ironmon::MoveLearningProtection.protect { Graphics.fadeout(4) }
    assert(Ironmon::SpeedClock.playtime_frame_count - before_playtime == 4,
      "native fades entering a protected 1x scope retain normal playtime accounting")
    Ironmon::SpeedUp.refresh_rate
    Graphics.brightness = 255

    original_hold = [$PokemonSystem.speedup_speed, $PokemonSystem.speedup_speed_battles]
    $PokemonSystem.speedup = 0
    $PokemonSystem.speedup_speed = 9
    $PokemonSystem.speedup_speed_battles = 5
    [false, true].each do |battle|
      $game_temp.in_battle = battle
      Ironmon.receive_tracker_controls({ "enabled" => true,
        "managed" => Ironmon::TRACKER_CONTROL_KEYS.keys, "pressed" => ["speed"] })
      Input.update
      Graphics.update
      expected = battle ? 6 : 10
      assert(Ironmon::SpeedUp.multiplier == expected, "hold sliders select their independent speed")
      Ironmon::SpeedTransitions.with_visual_speed do
        assert(Ironmon::SpeedUp.multiplier == 3, "visual speed is limited in hold mode and either context")
      end
      assert(Ironmon::SpeedUp.multiplier == expected, "the hold multiplier restores after the visual scope")
    end
  ensure
    $PokemonSystem.speedup_speed, $PokemonSystem.speedup_speed_battles = original_hold if original_hold
    Ironmon.clear_tracker_controls
    Input.update
    $PokemonSystem.speedup = 1
    $game_temp.in_battle = false
    Graphics.update
  end

  def self.battle_scene
    pokemon = Pokemon.new(:BULBASAUR, 20, nil, false)
    enemy = Pokemon.new(:PIDGEY, 20, nil, false)
    player = Player.new("Transition acceptance", :POKEMONTRAINER_Red)
    player.party = [pokemon]
    scene = PokeBattle_Scene.new
    battle = PokeBattle_Battle.new(scene, [pokemon], [enemy], player, nil)
    battle.backdrop = "indoora"
    [pokemon, enemy].each_with_index do |individual, index|
      battle.positions[index] = PokeBattle_ActivePosition.new
      battler = PokeBattle_Battler.new(battle, index)
      battle.battlers[index] = battler
      battler.pbInitialize(individual, 0)
      battle.choices[index] = [:None, 0, nil, -1]
    end
    scene.define_singleton_method(:pbCreateTrainerBackSprite) { |*_args| }
    intro_speeds = []
    scene.define_singleton_method(:pbGraphicsUpdate) do
      intro_speeds << Ironmon::SpeedUp.multiplier
      super()
    end
    setup_started = now
    scene.define_singleton_method(:pbInitSprites) do
      began = IronmonSpeedTransitionsTests.now
      result = super()
      @transition_setup_seconds = IronmonSpeedTransitionsTests.now - began
      return result
    end
    scene.pbStartBattle(battle)
    total = now - setup_started
    assert(!intro_speeds.empty? && intro_speeds.all? { |value| value == 3 },
      "the real wild battle intro receives limited visual speed")
    assert(Ironmon::SpeedUp.multiplier == 10, "the real battle scene restores selected gameplay speed")
    return { "construction_ms" => scene.instance_variable_get(:@transition_setup_seconds) * 1000,
      "total_ms" => total * 1000, "intro_updates" => intro_speeds.length,
      "intro_speeds" => intro_speeds.uniq }
  ensure
    scene.pbDisposeSprites if scene && scene.sprites
    viewport = scene.viewport if scene
    viewport.dispose if viewport && !viewport.disposed?
  end

  def self.test_encounter
    original_globals = [$scene, $MapFactory, $game_map, $PokemonEncounters]
    $MapFactory = PokemonMapFactory.new(10)
    $PokemonEncounters = PokemonEncounters.new
    $PokemonEncounters.setup($game_map.map_id)
    $scene = nil
    set_speed(10)
    observed = []
    callback = proc { observed << [Ironmon::SpeedTransitions.active?, Ironmon::SpeedUp.multiplier] }
    original_hooks = Ironmon.instance_variable_get(:@graphics_update_hooks)
    Ironmon.instance_variable_set(:@graphics_update_hooks, [{ :name => :transition_acceptance, :callback => callback }])
    setup = nil
    selected = $GameSpeed
    pbBattleAnimation(RPG::AudioFile.new("art_gallery", 0, 100), 0, []) do
      assert(!Ironmon::SpeedTransitions.active? && Ironmon::SpeedUp.multiplier == 10,
        "the actual yielded encounter body runs at the selected 10x speed")
      setup = battle_scene
      assert($GameSpeed == selected, "battle intro does not alter the selected stage")
    end
    assert(observed.any? { |state| state == [true, 3] }, "the actual encounter flash and return fade use visual speed")
    assert(!Ironmon::SpeedTransitions.active? && Ironmon::SpeedUp.multiplier == 10,
      "encounter completion restores gameplay speed and leaves visual scope")
    return setup
  ensure
    Ironmon.instance_variable_set(:@graphics_update_hooks, original_hooks) if original_hooks
    $scene, $MapFactory, $game_map, $PokemonEncounters = original_globals if original_globals
    Audio.bgm_stop
  end

  def self.run
    original_profile = Ironmon::ToggleSpeed.configuration
    original_state = [$PokemonSystem.speedup, $GameSpeed, $CanToggle, $game_temp.in_battle]
    service = Ironmon::SpeedUp
    original_limit = service.method(:native_update_limit)
    original_native = Graphics.method(:update_KGC_SpecialTransition)
    original_native = original_native.super_method until original_native.owner == Graphics.singleton_class
    original_native = original_native.unbind
    Graphics.singleton_class.send(:define_method, :update_KGC_SpecialTransition) do
      IronmonSpeedTransitionsTests.native_update
      original_native.bind_call(self)
    end
    viewport = Viewport.new(0, 0, Graphics.width, Graphics.height)
    sprite = BitmapSprite.new(Graphics.width, Graphics.height, viewport)
    sprite.bitmap.fill_rect(0, 0, Graphics.width, Graphics.height, Color.new(35, 110, 145))
    $PokemonSystem.speedup = 1
    $CanToggle = true
    $game_temp.in_battle = false
    Ironmon::ToggleSpeed.configure(profile)
    Graphics.update
    results = [60, 120].flat_map do |limit|
      service.define_singleton_method(:native_update_limit) { limit }
      [1, 3, 10].flat_map do |multiplier|
        [
          measure("coded SnakeSquares", multiplier, limit) do
            Graphics.freeze
            Graphics.transition(20, "SnakeSquares")
          end,
          measure("native fadeout", multiplier, limit) { Graphics.fadeout(20) },
          measure("native wait", multiplier, limit) { Graphics.wait(20) }
        ]
      end
    end
    if defined?(Ironmon::SpeedTransitions)
      test_scopes
      service.define_singleton_method(:native_update_limit) { 120 }
      encounter = test_encounter
    end
    output = $ironmon_speed_up_acceptance_output.to_s
    File.binwrite(File.join(output, "transitions.json"), Ironmon.tracker_json_generate({
      "measurements" => results, "encounter" => encounter })) if !output.empty?
    puts "Speed transitions: coded encounter wipes, native fades, real encounter and wild intro, nested/interrupted scopes, and hold settings passed"
  ensure
    Graphics.brightness = 255
    sprite.dispose if sprite && !sprite.disposed?
    viewport.dispose if viewport && !viewport.disposed?
    Graphics.singleton_class.send(:define_method, :update_KGC_SpecialTransition, original_native) if original_native
    service.define_singleton_method(:native_update_limit, original_limit) if original_limit
    Ironmon::ToggleSpeed.configure(original_profile) if original_profile
    if original_state
      $PokemonSystem.speedup, original_stage, $CanToggle, $game_temp.in_battle = original_state
      Graphics.update
      $GameSpeed = original_stage
      Graphics.update
    end
    Ironmon.clear_tracker_controls
  end

  def self.native_update
    @native_updates = @native_updates.to_i + 1
  end
end

IronmonSpeedTransitionsTests.run
