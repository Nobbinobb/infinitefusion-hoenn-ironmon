module IronmonSkyDropRuntimeTests
  class Scene < PokeBattle_DebugSceneNoLogging
    attr_reader :sprites, :messages, :animations

    def initialize
      super
      @sprites = {}
      @messages = []
      @animations = []
      2.times do |index|
        @sprites["pokemon_#{index}"] = Sprite.new
        @sprites["pokemon_#{index}"].visible = true
      end
    end

    def pbDisplayMessage(message, *_args)
      @messages << message
    end

    def pbAnimation(move_id, user, targets, hit = 0)
      @animations << [move_id, hit]
      return if move_id != :SKYDROP
      ([user] + Array(targets)).each do |battler|
        @sprites["pokemon_#{battler.index}"].visible = (hit != 1)
      end
    end

    def method_missing(_name, *_args)
      return nil
    end

    def respond_to_missing?(_name, _private = false)
      return true
    end

    def dispose
      @sprites.each_value { |sprite| sprite.dispose if !sprite.disposed? }
    end
  end

  def self.assert(condition, message)
    raise "Sky Drop validation failed: #{message}" if !condition
  end

  def self.fixture(species = :TAUROS, ability = :RUNAWAY)
    user_pokemon = Pokemon.new(:PIDGEOT, 35, nil, false)
    target_pokemon = Pokemon.new(species, 80, nil, false)
    [user_pokemon, target_pokemon].each do |individual|
      individual.ability = :RUNAWAY
      individual.item = nil
      individual.moves.replace([Pokemon::Move.new(:SKYDROP), Pokemon::Move.new(:SPLASH)])
      individual.calc_stats
    end
    target_pokemon.ability = ability
    scene = Scene.new
    battle = PokeBattle_Battle.new(scene, [user_pokemon], [target_pokemon], nil, nil)
    battle.internalBattle = false
    battle.expGain = false
    battle.define_singleton_method(:pbOwnedByPlayer?) { |index| index.even? }
    random = Random.new(19811)
    battle.define_singleton_method(:pbRandom) { |maximum| random.rand(maximum) }
    [user_pokemon, target_pokemon].each_with_index do |individual, index|
      battle.positions[index] = PokeBattle_ActivePosition.new
      battler = PokeBattle_Battler.new(battle, index)
      battle.battlers[index] = battler
      battler.pbInitialize(individual, 0)
      battler.define_singleton_method(:checkChallengesAfterTurn) { nil }
      battle.successStates[index] = PokeBattle_SuccessState.new
      battle.choices[index] = [:UseMove, 0, battler.moves[0], 1 - index, 0]
    end
    battle.pbCalculatePriority(true)
    scene.pbStartBattle(battle)
    return [battle, battle.battlers[0], battle.battlers[1], scene]
  end

  def self.charge(battle, user, target)
    before = target.hp
    user.pbUseMove(battle.choices[user.index])
    assert(user.inTwoTurnAttack?("0CE"), "the charging turn retains Sky Drop")
    assert(target.effects[PBEffects::SkyDrop] == user.index, "the target is carried for one turn")
    assert(target.hp == before, "the charging turn does not deal damage")
    assert(!target.pbTryUseMove(battle.choices[target.index], target.moves[0], false, false),
      "the carried target cannot act before being released")
    battle.instance_variable_set(:@turnCount, battle.turnCount + 1)
  end

  def self.assert_released(battle, user, target, scene)
    assert(target.effects[PBEffects::SkyDrop] == -1, "the target is no longer carried")
    assert(!user.inTwoTurnAttack?("0CE"), "the user is no longer locked into Sky Drop")
    assert(scene.sprites["pokemon_#{target.index}"].visible, "the released target's sprite is restored") if !target.fainted?
    assert(scene.sprites["pokemon_#{user.index}"].visible, "the carrier's sprite is restored") if !user.fainted?
    target_choice = [:UseMove, 1, target.moves[1], user.index, 0]
    assert(target.pbTryUseMove(target_choice, target.moves[1], false, false),
      "the released target can use its next move") if !target.fainted?
  end

  def self.test_case(name, species = :TAUROS, ability = :RUNAWAY)
    battle, user, target, scene = fixture(species, ability)
    charge(battle, user, target)
    before = target.hp
    yield battle, user, target, scene if block_given?
    user.pbUseMove(battle.choices[user.index]) if name != "cancel" && name != "carrier faint"
    assert_released(battle, user, target, scene)
    loss = before - target.hp
    assert(loss > 0, "an ordinary Sky Drop deals its normal damage") if name == "normal"
    assert(loss == 0, "#{name} does not bypass its damage prevention") if ["Wonder Guard", "Flying immunity", "Protect", "miss", "flinch"].include?(name)
    return { "case" => name, "damage" => loss, "released" => true }
  ensure
    scene.dispose if scene
  end

  def self.test_original_failure
    original_release = Ironmon::SkyDrop.method(:release)
    Ironmon::SkyDrop.define_singleton_method(:release) { |*_args| }
    battle, user, target, scene = fixture(:TAUROS, :WONDERGUARD)
    charge(battle, user, target)
    before = target.hp
    user.pbUseMove(battle.choices[user.index])
    assert(target.hp == before && target.effects[PBEffects::SkyDrop] == user.index,
      "the original immunity path leaves the carried target locked when cleanup is disabled")
    assert(!target.pbTryUseMove(battle.choices[target.index], target.moves[0], false, false),
      "the original stranded target cannot act")
    return { "case" => "original Wonder Guard failure reproduced", "damage" => 0, "stranded" => true }
  ensure
    Ironmon::SkyDrop.define_singleton_method(:release, original_release) if original_release
    scene.dispose if scene
  end

  def self.test_interrupted_charge
    battle, user, target, scene = fixture
    original_hit = user.method(:pbProcessMoveHit)
    user.define_singleton_method(:pbProcessMoveHit) do |*args|
      original_hit.call(*args)
      raise "Sky Drop test interruption"
    end
    begin
      user.pbUseMove(battle.choices[user.index])
      raise "The interrupted charging turn unexpectedly completed"
    rescue RuntimeError => error
      raise if error.message != "Sky Drop test interruption"
    end
    assert_released(battle, user, target, scene)
    return { "case" => "interrupted charging turn", "released" => true }
  ensure
    scene.dispose if scene
  end

  def self.run
    original_globals = [$game_temp, $PokemonGlobal, $PokemonBag]
    $game_temp = Game_Temp.new
    $game_temp.in_battle = true
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = false
    $PokemonBag = nil
    results = []
    results << test_original_failure
    results << test_case("normal")
    results << test_case("Wonder Guard", :TAUROS, :WONDERGUARD)
    results << test_case("Flying immunity", :PIDGEOT)
    results << test_case("Protect") { |_battle, _user, target| target.effects[PBEffects::Protect] = true }
    results << test_case("miss") do |battle, user|
      user.stages[:ACCURACY] = -6
      battle.define_singleton_method(:pbRandom) { |maximum| maximum - 1 }
    end
    results << test_case("flinch") { |_battle, user| user.effects[PBEffects::Flinch] = true }
    results << test_case("cancel") { |_battle, user| user.pbCancelMoves }
    results << test_case("carrier faint") do |_battle, user|
      user.hp = 0
      user.pbInitEffects(false)
    end
    results << test_interrupted_charge
    output = $ironmon_speed_up_acceptance_output.to_s
    File.binwrite(File.join(output, "sky-drop.json"), Ironmon.tracker_json_generate(results)) if !output.empty?
    puts "Sky Drop: native two-turn use, immunity, protection, miss, interruption, release, action recovery, and sprite recovery passed"
  ensure
    $game_temp, $PokemonGlobal, $PokemonBag = original_globals if original_globals
  end
end

IronmonSkyDropRuntimeTests.run
