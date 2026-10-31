module IronmonSwitchOutMoveRuntimeTests
  MOVES = [:VOLTSWITCH, :UTURN].freeze

  def self.assert(condition, message)
    @assertions = (@assertions || 0) + 1
    raise "Switch-out move validation failed: #{message}" if !condition
  end

  def self.pokemon(species, level, move)
    individual = Pokemon.new(species, level, nil, false)
    individual.ability = :RUNAWAY
    individual.item = nil
    individual.moves.replace([Pokemon::Move.new(move), Pokemon::Move.new(:SPLASH)])
    individual.calc_stats
    return individual
  end

  def self.sprite_state(sprite)
    return { "visible" => sprite.visible, "opacity" => sprite.opacity,
      "x" => sprite.x, "y" => sprite.y, "ox" => sprite.ox, "oy" => sprite.oy,
      "bitmap_alive" => !!(sprite.bitmap && !sprite.bitmap.disposed?) }
  end

  def self.visible?(state)
    return state["visible"] && state["opacity"] > 0 && state["bitmap_alive"]
  end

  def self.fixture(move_id, bench, target_species = :TAUROS, actor_index = 0)
    actor = pokemon(:JOLTEON, 35, move_id)
    defender = pokemon(target_species, 80, :SPLASH)
    party = [actor]
    if bench
      replacement = pokemon(:BULBASAUR, 35, :SPLASH)
      replacement.hp = 0 if bench == :fainted
      replacement.steps_to_hatch = 1 if bench == :egg
      party << replacement
    end
    player_party, opponent_party = actor_index == 0 ? [party, [defender]] : [[defender], party]
    trainer = Player.new("Switch-out acceptance", :POKEMONTRAINER_Red)
    trainer.party = player_party
    $Trainer = trainer
    scene = PokeBattle_Scene.new
    battle = PokeBattle_Battle.new(scene, player_party, opponent_party, trainer, nil)
    battle.internalBattle = false
    battle.expGain = false
    battle.showAnims = true
    battle.backdrop = "indoora"
    battle.define_singleton_method(:pbOwnedByPlayer?) { |index| index.even? }
    random = Random.new(19811)
    battle.define_singleton_method(:pbRandom) { |maximum| random.rand(maximum) }
    [player_party.first, opponent_party.first].each_with_index do |individual, index|
      battle.positions[index] = PokeBattle_ActivePosition.new
      battler = PokeBattle_Battler.new(battle, index)
      battle.battlers[index] = battler
      battler.pbInitialize(individual, 0)
      battler.define_singleton_method(:checkChallengesAfterTurn) { nil }
      battle.successStates[index] = PokeBattle_SuccessState.new
      battle.choices[index] = [:UseMove, 0, battler.moves[0], 1 - index, 0]
    end
    battle.pbCalculatePriority(true)
    scene.define_singleton_method(:pbBattleIntroAnimation) { }
    scene.define_singleton_method(:pbCreateTrainerBackSprite) { |*_args| }
    scene.define_singleton_method(:pbDisplayMessage) { |_message, *_args| }
    scene.define_singleton_method(:pbDisplayPausedMessage) { |_message, *_args| }
    scene.pbStartBattle(battle)
    battle.battlers.each_with_index do |battler, index|
      next if !battler
      scene.pbChangePokemon(index, battler.pokemon)
      sprite = scene.sprites["pokemon_#{index}"]
      sprite.visible = true
      sprite.opacity = 255
      sprite.tone = Tone.new(0, 0, 0)
      scene.sprites["dataBox_#{index}"].visible = true
    end
    events = { "animations" => [], "choices" => 0, "recalls" => 0 }
    scene.define_singleton_method(:pbAnimationCore) do |animation, user, target, opposing = false|
      event = { "name" => animation.name, "user_index" => user.index,
        "opposing_animation" => opposing }
      events["animations"] << event
      result = super(animation, user, target, opposing)
      event["native_user_after"] = IronmonSwitchOutMoveRuntimeTests.sprite_state(
        sprites["pokemon_#{actor_index}"])
      result
    end
    battle.define_singleton_method(:pbGetReplacementPokemonIndex) do |index, *_args|
      events["choices"] += 1
      IronmonSwitchOutMoveRuntimeTests.assert(pbCanSwitchLax?(index, 1),
        "the positive-control replacement is natively eligible")
      1
    end
    battle.define_singleton_method(:pbRecallAndReplace) do |*args|
      events["recalls"] += 1
      super(*args)
    end
    return [battle, battle.battlers[actor_index], battle.battlers[1 - actor_index], scene, events]
  end

  def self.set_speed(multiplier)
    Ironmon.clear_tracker_controls
    Input.update
    Graphics.update
    $GameSpeed = SPEEDUP_STAGES.index(multiplier)
    assert(!$GameSpeed.nil?, "the requested #{multiplier}x stage exists")
    Graphics.update
    Ironmon::SpeedUp.reset_engine
    assert(Ironmon::SpeedUp.multiplier == multiplier, "the actual runtime uses #{multiplier}x")
  end

  def self.test_case(move_id, name, bench = nil, multiplier = 1, actor_index = 0, original = false)
    target_species = name == "type immunity" ? :SANDSLASH : :TAUROS
    battle, user, target, scene, events = fixture(move_id, bench, target_species, actor_index)
    set_speed(multiplier)
    target.ability = :WONDERGUARD if name == "ability immunity"
    target.hp = 1 if name == "target KO"
    if name == "user recoil KO"
      user.hp = 1
      user.item = :LIFEORB
    end
    if name == "miss"
      user.stages[:ACCURACY] = -6
      battle.define_singleton_method(:pbRandom) { |maximum| maximum - 1 }
    end
    if original
      animation = user.moves[0].method(:pbShowAnimation)
      assert(animation.owner == Ironmon::SwitchOutMoves::MoveAnimation,
        "the regression bypasses only the new animation wrapper")
      animation = animation.super_method
      user.moves[0].define_singleton_method(:pbShowAnimation) { |*args| animation.call(*args) }
    end
    original_pokemon = user.pokemon
    initial = sprite_state(scene.sprites["pokemon_#{actor_index}"])
    before = target.hp
    user.pbUseMove(battle.choices[actor_index])
    damage = before - target.hp
    after = sprite_state(scene.sprites["pokemon_#{actor_index}"])
    blocked = ["miss", "type immunity", "ability immunity"].include?(name)
    assert(blocked ? damage == 0 : damage > 0, "#{move_id} #{name} preserves native damage")
    if bench == :valid
      assert(events["choices"] == 1 && events["recalls"] == 1,
        "#{move_id} with a healthy bench chooses and really recalls once")
      assert(user.pokemonIndex == 1 && !user.pokemon.equal?(original_pokemon),
        "#{move_id} sends the actual replacement into battle")
      assert(visible?(after), "the actual #{move_id} replacement sprite is visible")
    else
      assert(events["choices"] == 0 && events["recalls"] == 0,
        "#{move_id} #{name} never prompts or recalls without an eligible replacement")
      assert(user.pokemon.equal?(original_pokemon) && user.pokemonIndex == 0,
        "#{move_id} #{name} keeps the same active Pokemon")
      if name == "user recoil KO"
        assert(user.fainted? && !after["visible"],
          "#{move_id} recoil still faints and hides its user")
      elsif !original
        assert(visible?(after), "#{move_id} #{name} leaves the living user visible")
        ["x", "y", "ox", "oy"].each do |property|
          assert(after[property] == initial[property],
            "#{move_id} #{name} restores native #{property}")
        end
        scene.pbFrameUpdate
        Graphics.update
        assert(visible?(sprite_state(scene.sprites["pokemon_#{actor_index}"])),
          "#{move_id} #{name} remains visible after another real scene update")
      end
    end
    if !blocked
      assert(!events["animations"].empty?, "#{move_id} plays its real native animation")
      assert(events["animations"].all? { |event| !event["name"].to_s.empty? },
        "the native animation player receives a named animation")
    end
    if name == "target KO"
      assert(target.fainted? && !scene.sprites["pokemon_#{target.index}"].visible,
        "#{move_id} preserves the target's genuine faint animation")
    end
    return { "move" => move_id.to_s, "case" => name, "multiplier" => multiplier,
      "actor_index" => actor_index, "unpatched" => original, "damage" => damage,
      "user_after" => after, "choices" => events["choices"], "recalls" => events["recalls"],
      "animations" => events["animations"] }
  ensure
    if scene
      scene.pbDisposeSprites if scene.sprites
      viewport = scene.viewport
      viewport.dispose if viewport && !viewport.disposed?
    end
  end

  def self.run
    globals = [$Trainer, $PokemonGlobal, $game_temp, $PokemonSystem.speedup, $GameSpeed]
    configuration = Ironmon::ToggleSpeed.configuration
    tracker_signatures = Ironmon.instance_variable_get(:@tracker_enemy_move_signatures)
    Ironmon.instance_variable_set(:@tracker_enemy_move_signatures, {})
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = false
    $game_temp = Game_Temp.new
    $game_temp.in_battle = true
    $PokemonSystem.speedup = 1
    profile = Ironmon::ToggleSpeed.default_configuration
    Ironmon::ToggleSpeed::LANES.each do |lane|
      profile["lanes"][lane] << { "id" => "switch-#{lane}-fast",
        "nodes" => [{ "id" => "switch-#{lane}-10", "value" => 10 }] }
    end
    profile["links"] << { "id" => "switch-fast",
      "overworld" => "switch-overworld-10", "battle" => "switch-battle-10" }
    Ironmon::ToggleSpeed.configure(profile)
    results = MOVES.map { |move| test_case(move, "original solo", nil, 1, 0, true) }
    assert(results.any? { |result| !visible?(result["user_after"]) },
      "an actual named native animation reproduces the original stranded invisible user")
    MOVES.each do |move|
      results << test_case(move, "solo")
      results << test_case(move, "fainted bench", :fainted)
      results << test_case(move, "egg bench", :egg)
      results << test_case(move, "valid bench", :valid)
      results << test_case(move, "miss")
      results << test_case(move, "target KO")
      results << test_case(move, "user recoil KO")
      results << test_case(move, "fast solo", nil, 10)
      results << test_case(move, "opponent solo", nil, 10, 1)
    end
    results << test_case(:VOLTSWITCH, "type immunity")
    results << test_case(:UTURN, "ability immunity")
    output = $ironmon_speed_up_acceptance_output.to_s
    if !output.empty?
      File.binwrite(File.join(output, "switch-out-moves.json"),
        Ironmon.tracker_json_generate({ "assertions" => @assertions, "cases" => results }))
    end
    puts "Switch-out moves: real native animations, original failure, damage, eligibility, switching, immunity, miss, fainting, both sides, and 1x/10x passed (#{@assertions} assertions)"
  ensure
    if globals
      $Trainer, $PokemonGlobal, $game_temp, $PokemonSystem.speedup, $GameSpeed = globals
      Ironmon::ToggleSpeed.configure(configuration) if configuration
      Ironmon.instance_variable_set(:@tracker_enemy_move_signatures, tracker_signatures)
      Ironmon.clear_tracker_controls
      Input.update
      Ironmon::SpeedUp.reset_engine
    end
  end
end

IronmonSwitchOutMoveRuntimeTests.run
