module IronmonCollectionCombatRuntimeTests
  OUTPUT_PATH = $ironmon_collection_combat_test_output_path.to_s
  TestPokemon = Struct.new(:hp, :status, :statusCount, :item)

  class QuietScene
    def method_missing(_name, *_args)
      return nil
    end

    def respond_to_missing?(_name, _private = false)
      return true
    end
  end

  def self.assert(condition, message)
    raise "Gameplay collection combat runtime test failed: #{message}" if !condition
  end

  def self.fixture_battle
    battle = PokeBattle_Battle.allocate
    battle.instance_variable_set(:@scene, QuietScene.new)
    battle.instance_variable_set(:@field, PokeBattle_ActiveField.new)
    battle.instance_variable_set(:@sides, [PokeBattle_ActiveSide.new, PokeBattle_ActiveSide.new])
    battle.instance_variable_set(:@positions, [PokeBattle_ActivePosition.new, PokeBattle_ActivePosition.new])
    battle.instance_variable_set(:@battlers, [])
    battle.instance_variable_set(:@choices, [])
    battle.instance_variable_set(:@sideSizes, [1, 1])
    battle.instance_variable_set(:@player, [])
    battle.instance_variable_set(:@opponent, [])
    battle.instance_variable_set(:@internalBattle, true)
    battle.instance_variable_set(:@debug, false)
    battle.instance_variable_set(:@turnCount, 0)
    battle.define_singleton_method(:pbOwnedByPlayer?) { |index| index.even? }
    battle.define_singleton_method(:trainerBattle?) { false }
    battle.define_singleton_method(:wildBattle?) { true }
    battle.define_singleton_method(:pbCommonAnimation) { |*_args| nil }
    battle.define_singleton_method(:pbDisplay) { |*_args| nil }
    return battle
  end

  def self.fixture_battler(battle, index)
    battler = PokeBattle_Battler.allocate
    battler.instance_variable_set(:@battle, battle)
    battler.instance_variable_set(:@index, index)
    battler.instance_variable_set(:@pokemonIndex, 0)
    battler.instance_variable_set(:@pokemon, TestPokemon.new(100, :NONE, 0, nil))
    battler.instance_variable_set(:@hp, 100)
    battler.instance_variable_set(:@totalhp, 100)
    battler.instance_variable_set(:@level, 5)
    battler.instance_variable_set(:@species, :BULBASAUR)
    battler.instance_variable_set(:@form, 0)
    battler.instance_variable_set(:@type1, :NORMAL)
    battler.instance_variable_set(:@type2, :NORMAL)
    battler.instance_variable_set(:@status, :NONE)
    battler.instance_variable_set(:@statusCount, 0)
    battler.instance_variable_set(:@fainted, false)
    battler.instance_variable_set(:@ability_id, nil)
    battler.instance_variable_set(:@item_id, nil)
    battler.instance_variable_set(:@dummy, false)
    battler.instance_variable_set(:@moves, [])
    battler.instance_variable_set(:@damageState, PokeBattle_DamageState.new)
    battler.instance_variable_set(:@effects, Array.new(120, 0))
    Ironmon::GameplayCollection::Combat::EFFECT_DECLARATIONS.each do |scope, symbol, _index, kind, _duration, _maxima, _governing|
      next if scope != "battler" || !PBEffects.const_defined?(symbol, false)
      battler.effects[PBEffects.const_get(symbol)] = false if kind == "boolean"
      battler.effects[PBEffects.const_get(symbol)] = -1 if ["battler_index", "position_index", "party_index", "side_index"].include?(kind)
    end
    battler.effects[PBEffects::Substitute] = 0
    battler.effects[PBEffects::Transform] = false
    battler.effects[PBEffects::BurnUp] = false
    battler.effects[PBEffects::GastroAcid] = false
    battler.instance_variable_set(:@stages, { :ATTACK => 0, :SPEED => 0 })
    battler.define_singleton_method(:pbThis) { |*_args| "Test Pokemon" }
    battler.define_singleton_method(:checkHPRelatedFormChange) { |*_args| nil }
    battler.define_singleton_method(:pbCheckFormOnStatusChange) { nil }
    battler.define_singleton_method(:abilityActive?) { |*_args| false }
    battler.define_singleton_method(:pbAbilityStatusCureCheck) { nil }
    battler.define_singleton_method(:pbItemStatusCureCheck) { |*_args| nil }
    battle.battlers[index] = battler
    battle.choices[index] = [:None, 0, nil, -1, 0]
    Ironmon::GameplayCollection::Combat.entered(battler)
    return battler
  end

  def self.fixture_move(battle)
    move = PokeBattle_Move.allocate
    move.instance_variable_set(:@battle, battle)
    move.instance_variable_set(:@id, :TACKLE)
    move.instance_variable_set(:@type, :NORMAL)
    move.instance_variable_set(:@calcType, :NORMAL)
    move.instance_variable_set(:@category, 0)
    move.instance_variable_set(:@function, "000")
    move.instance_variable_set(:@priority, 0)
    move.instance_variable_set(:@pp, 10)
    move.instance_variable_set(:@total_pp, 35)
    move.define_singleton_method(:nonLethal?) { |*_args| false }
    return move
  end

  def self.events(type)
    return @events.select { |event| event["type"] == type }
  end

  def self.native_tackle(enabled, scenario = "damage")
    collection = Ironmon::GameplayCollection
    original_globals = [$game_temp, $PokemonTemp, $PokemonSystem, $PokemonGlobal, $game_switches, $game_variables, $PokemonBag]
    $game_temp = Game_Temp.new
    $game_temp.in_battle = true
    $PokemonTemp = PokemonTemp.new
    $PokemonSystem = PokemonSystem.new
    $PokemonGlobal = PokemonGlobalMetadata.new
    $game_switches = Game_Switches.new
    $game_variables = Game_Variables.new
    $PokemonBag = nil
    collection.singleton_class.define_method(:enabled?) { enabled }
    pokemon = Pokemon.new(:BULBASAUR, 40, nil, false)
    opponent = Pokemon.new(scenario == "immune" ? :GASTLY : :SNORLAX, 80, nil, false)
    [pokemon, opponent].each do |individual|
      individual.iv.each_key { |key| individual.iv[key] = 0 }
      individual.ev.each_key { |key| individual.ev[key] = 0 }
      individual.nature = :HARDY
      individual.ability = :RUNAWAY
      individual.item = nil
      individual.calc_stats
    end
    pokemon.moves.replace([Pokemon::Move.new(scenario == "status_failed" ? :THUNDERWAVE : :TACKLE)])
    opponent.status = :PARALYSIS if scenario == "status_failed"
    opponent.moves.replace([Pokemon::Move.new(:SPLASH)])
    battle = PokeBattle_Battle.new(QuietScene.new, [pokemon], [opponent], nil, nil)
    battle.internalBattle = false
    battle.expGain = false
    battle.define_singleton_method(:pbOwnedByPlayer?) { |index| index.even? }
    random = Random.new(19_811)
    calls = []
    battle.define_singleton_method(:pbRandom) do |maximum|
      result = scenario == "miss" && maximum == 100 ? 99 : random.rand(maximum)
      calls << [maximum, result]
      result
    end
    battle.positions[0] = PokeBattle_ActivePosition.new
    battle.positions[1] = PokeBattle_ActivePosition.new
    collection::Combat.admit(battle) if enabled
    player = PokeBattle_Battler.new(battle, 0)
    enemy = PokeBattle_Battler.new(battle, 1)
    battle.battlers[0], battle.battlers[1] = player, enemy
    player.pbInitialize(pokemon, 0)
    enemy.pbInitialize(opponent, 0)
    player.moves[0].instance_variable_set(:@accuracy, 1) if scenario == "miss"
    player.define_singleton_method(:checkChallengesAfterTurn) { nil }
    battle.successStates[0] = PokeBattle_SuccessState.new
    battle.successStates[1] = PokeBattle_SuccessState.new
    battle.choices[0] = [:UseMove, 0, player.moves[0], 1, 0]
    battle.choices[1] = [:UseMove, 0, enemy.moves[0], 0, 0]
    battle.pbCalculatePriority(true)
    starting = enemy.hp
    event_start = @events.length
    player.pbUseMove(battle.choices[0])
    captured = @events[event_start..-1]
    return { "loss" => starting - enemy.hp, "pp" => player.moves[0].pp, "rng" => calls, "events" => captured }
  ensure
    collection::Combat.release_battle(battle, true) if enabled && battle
    collection.singleton_class.define_method(:enabled?) { true } if collection
    $game_temp, $PokemonTemp, $PokemonSystem, $PokemonGlobal, $game_switches, $game_variables, $PokemonBag = original_globals if original_globals
  end

  def self.run
    collection = Ironmon::GameplayCollection
    singleton = collection.singleton_class
    originals = {}
    [:enabled?, :emit, :next_id, :pokemon_instance_id].each do |method_name|
      originals[method_name] = collection.method(method_name)
    end
    @events = []
    counter = 0
    identifiers = {}
    sink = @events
    singleton.define_method(:enabled?) { true }
    singleton.define_method(:next_id) do |kind|
      counter += 1
      "test:#{kind}:#{counter}"
    end
    singleton.define_method(:pokemon_instance_id) do |pokemon|
      identifiers[pokemon.object_id] ||= next_id("pokemon")
    end
    singleton.define_method(:emit) do |type, payload = {}, context = {}|
      sink << { "type" => type, "payload" => payload, "context" => current_context.merge(context) }
      sink.length
    end
    combat = collection::Combat
    previous_states = combat.instance_variable_get(:@states)
    previous_battle = combat.instance_variable_get(:@current_battle)
    battle = fixture_battle
    begin
      combat.admit(battle)
      player = fixture_battler(battle, 0)
      target = fixture_battler(battle, 1)
      move = fixture_move(battle)
      player.moves << move
      context = { "source_kind" => "move", "source_id" => "TACKLE", "source_actor" => combat.actor_ref(player) }
      combat.with_context(context, battle) do
        actual = target.pbReduceHP(140, false, true, false)
        assert(actual == 100 && target.hp == 0, "native capped damage result is preserved")
      end
      loss = events("hp.loss_applied").last["payload"]
      assert(loss["actual_hp_lost"] == 100 && loss["damage_requested"] == 140, "requested and actual real HP are distinct")
      target.instance_variable_set(:@hp, 40)
      target.pokemon.hp = 40
      actual = target.pbRecoverHP(100, false, false)
      heal = events("hp.healing_resolved").last["payload"]
      assert(actual == 60 && heal["actual_restored_hp"] == 60 && heal["capacity_excess_hp"] == 40, "native healing and excess are exact")
      count = events("hp.loss_applied").length
      target.effects[PBEffects::Substitute] = 30
      target.damageState.substitute = true
      target.damageState.calcDamage = 90
      combat.with_context(context, battle) do
        move.pbReduceDamage(player, target)
        move.pbInflictHPDamage(target)
      end
      assert(target.hp == 100 && target.effects[PBEffects::Substitute] == 0, "native substitute absorbs its own clipped damage")
      assert(events("hp.loss_applied").length == count, "substitute damage is not counted as real HP loss")
      substitute = events("substitute.changed").last["payload"]
      assert(substitute["actual_substitute_hp_lost"] == 30, "substitute loss is retained separately")
      target.damageState.reset
      target.damageState.calcDamage = 170
      combat.with_context(context, battle) do
        move.pbReduceDamage(player, target)
        move.pbInflictHPDamage(target)
      end
      calculated = events("hit.damage_calculated").last["payload"]
      assert(calculated["calculated_damage"] == 170 && target.hp == 0, "rolled overkill remains separate from actual HP")
      target.instance_variable_set(:@hp, 100)
      target.pokemon.hp = 100
      target.define_singleton_method(:pbItemStatusCureCheck) do |*_args|
        combat.with_context({ "source_kind" => "item", "source_id" => "CHERIBERRY" }, battle) { self.status = :NONE }
      end
      combat.with_context(context, battle) { target.pbInflictStatus(:PARALYSIS, 0, "Test status", player) }
      applied = events("status.major_application_resolved").last
      cured = events("status.major_cure_resolved").last
      assert(applied && cured && target.status == :NONE, "application and immediate cure are both collected")
      assert(@events.index(applied) < @events.index(cured), "status application precedes immediate recovery")
      assert(applied["payload"]["status_episode_id"] == cured["payload"]["status_episode_id"], "cure retains its actual status episode")
      combat.with_context(context, battle) { move.pp = 7 }
      pp = events("pp.expenditure_resolved").last["payload"]
      assert(pp["actual_pp_lost"] == 3 && pp["move_slot"] == 0, "actual finite move PP delta is retained")
      combat.with_context(context, battle) { target.stages[:ATTACK] = 2 }
      stage = events("stat_stage.changed").last["payload"]
      assert(stage["delta"] == 2, "direct stage writes are observed")
      known = combat.effect_registry[["battler", PBEffects::Attract]]
      assert(!known["maxima_eligible"] && known["value_kind"] == "battler_index", "actor indexes cannot become numeric quantity maxima")
      bookkeeping_count = events("volatile.application_resolved").length
      target.effects[PBEffects::ProtectRate] = 3
      bookkeeping = events("field.effect_changed").last["payload"]
      assert(bookkeeping["effect_metadata"]["semantic_role"] == "engine_state", "ProtectRate remains explicit engine state")
      assert(events("volatile.application_resolved").length == bookkeeping_count, "bookkeeping writes never invent volatile applications")
      target.effects[1_000] = 9
      unknown = events("field.effect_changed").last["payload"]["effect_metadata"]
      assert(unknown["value_kind"] == "unsupported" && !unknown["maxima_eligible"], "unknown effect indexes stay explicit and ineligible for maxima")
      first = combat.actor_ref(target)
      target.instance_variable_set(:@pokemon, TestPokemon.new(100, :NONE, 0, nil))
      second = combat.actor_ref(target)
      assert(first["actor_id"] != second["actor_id"] && second["slot_epoch"] > first["slot_epoch"], "reused slots cannot merge different individuals")
      baseline = native_tackle(false)
      captured = native_tackle(true)
      assert(baseline["loss"] > 0 && baseline["loss"] == captured["loss"], "actual native Tackle dispatch keeps identical damage with collection enabled")
      assert(baseline["pp"] == captured["pp"] && baseline["rng"] == captured["rng"], "native dispatch consumes identical PP and identical RNG calls/results")
      types = captured["events"].map { |event| event["type"] }
      ["action.started", "move.attempted", "move.executed", "move.targets_resolved", "hit.damage_calculated", "hp.loss_applied", "hit.resolved", "move.finished", "action.finished"].each do |type|
        assert(types.include?(type), "native Tackle dispatch emits #{type}")
      end
      actual_hit = captured["events"].find { |event| event["type"] == "hit.resolved" }["payload"]
      assert(actual_hit["application_snapshot"]["actual_pool_hp_lost"] == captured["loss"], "native hit summary reconciles actual committed damage")
      ["miss", "immune", "status_failed"].each do |scenario|
        sample = native_tackle(true, scenario)
        finished = sample["events"].find { |event| event["type"] == "move.finished" }
        assert(sample["loss"] == 0 && finished, "native #{scenario} resolves without target damage")
        assert(finished["payload"]["result"] == "executed_no_effect", "native #{scenario} cannot earn a successful-move count from bookkeeping")
        if scenario == "miss"
          assert(sample["events"].any? { |event| event["type"] == "move.accuracy_resolved" && event["payload"]["passed"] == false }, "forced native accuracy miss is actually observed")
        elsif scenario == "immune"
          assert(sample["events"].any? { |event| event["type"] == "move.target_gate_resolved" && event["payload"]["gate_result"] == "type_immune" }, "native Ghost immunity is actually observed")
        end
      end
      File.binwrite(OUTPUT_PATH, "gameplay collection combat runtime tests passed\n")
    ensure
      originals.each { |method_name, implementation| singleton.define_method(method_name, implementation) }
      combat.instance_variable_set(:@states, previous_states)
      combat.instance_variable_set(:@current_battle, previous_battle)
    end
  rescue Exception => error
    File.binwrite(OUTPUT_PATH, "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}\n")
    raise
  end
end

IronmonCollectionCombatRuntimeTests.run
