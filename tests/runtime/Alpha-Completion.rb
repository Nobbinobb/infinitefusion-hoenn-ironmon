module IronmonAlphaCompletionRuntimeTests
  class QuietScene
    def method_missing(_name, *_arguments); return nil; end
    def respond_to_missing?(_name, _private = false); return true; end
  end

  class MemoryJournal
    attr_reader :epoch_id
    def initialize; @epoch_id = "22222222222222222222222222222222"; @contexts = {}; end
    def runtime_context(key); return Marshal.load(Marshal.dump(@contexts[key])); end
    def bind_runtime_context(key, value); @contexts[key] = Marshal.load(Marshal.dump(value)); end
    def fence; return nil; end
  end

  class Connection
    attr_reader :sent
    def initialize; @sent = []; end
    def send_event(event, payload); @sent << [event, Marshal.load(Marshal.dump(payload))]; return true; end
  end

  def self.assert(condition, message)
    raise "Alpha completion runtime test failed: #{message}" if !condition
  end

  def self.run(output_root)
    collection = Ironmon::GameplayCollection
    world = collection::World
    original_globals = [$Trainer, $PokemonGlobal, $PokemonSystem, $PokemonTemp, $game_switches, $game_variables, $game_temp, $game_map, $PokemonBag, $scene, $game_system]
    original_ironmon = Ironmon.instance_variables.to_h { |name| [name, Ironmon.instance_variable_get(name)] }
    original_world = world.instance_variables.to_h { |name| [name, world.instance_variable_get(name)] }
    original_collection = collection.instance_variables.to_h { |name| [name, collection.instance_variable_get(name)] }
    overrides = []
    replace = proc do |object, name, implementation|
      overrides << [object, name, object.method(name)]
      object.singleton_class.define_method(name, implementation)
    end
    journal = MemoryJournal.new
    connection = Connection.new
    facts = []
    counter = 0
    replace.call(collection, :enabled?, proc { true })
    replace.call(collection, :journal, proc { journal })
    replace.call(collection, :flush, proc { nil })
    replace.call(collection, :next_id, proc { |_kind| counter += 1; "%032x" % counter })
    replace.call(collection, :emit, proc do |type, payload = {}, context = {}|
      facts << { "sequence" => (facts.length + 1).to_s, "type" => type, "attempt_id" => collection.attempt_id, "branch_id" => collection.branch_id, "battle_id" => context["battle_id"], "occurred_at_us" => "1", "context" => context, "payload" => Marshal.load(Marshal.dump(payload)) }
      facts.length
    end)
    replace.call(collection::Ownership, :baseline, proc { |*_arguments| nil })
    replace.call(collection::Ownership, :poll, proc { |*_arguments| nil })
    replace.call(Ironmon, :tracker_connection, proc { connection })
    replace.call(Ironmon, :tracker_owned_iv_attempt_ids, proc { |*_arguments| [] })
    replace.call(Ironmon, :tracker_run_reproduction_recipe, proc do
      { "seed" => 12345, "game_version" => "6.8.2", "ironmon_version" => Ironmon::VERSION,
        "generation_profile_id" => "0123456789abcdef" * 4,
        "configuration" => { "schema_version" => 1, "wild_policy" => "mixed", "trainer_policy" => "mixed", "unfusion_setting" => "random_component" },
        "species_generator" => { "version" => 1, "pool_fingerprint" => "native-alpha-fixture" },
        "ability_generator" => { "version" => 1, "pool_size" => 100, "pool_fingerprint" => "native-alpha-fixture" },
        "base_stat_generator" => { "version" => 1, "source_fingerprint" => "native-alpha-fixture" },
        "player_fusion_generator" => { "version" => 1, "pool_size" => 100, "pool_fingerprint" => "native-alpha-fixture" } }
    end)
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new
    $PokemonGlobal.ironmon_seed = 12345
    $PokemonSystem = PokemonSystem.new
    $PokemonTemp = PokemonTemp.new
    $game_switches = Game_Switches.new
    $game_variables = Game_Variables.new
    $game_temp = Game_Temp.new
    $game_map = nil
    $PokemonBag = nil
    trainer_type = GameData::TrainerType.keys.first
    $Trainer = Player.new("Alpha runtime", trainer_type)
    $Trainer.save_slot = "AlphaRuntimeFixture"
    pokemon = Pokemon.new(:BULBASAUR, 20, $Trainer)
    opponent = Pokemon.new(:PIKACHU, 20, nil, false)
    $Trainer.party = [pokemon]
    ledger = Ironmon.default_run_ledger
    ledger["attempts_started"] = 1
    ledger["current_attempt"] = { "run_id" => "alpha-native-run", "attempt_number" => 1, "seed" => 12345, "result" => "active", "active_seconds" => 10.0, "statistics" => Ironmon.default_attempt_statistics }
    $PokemonGlobal.ironmon_run_ledger = ledger
    $PokemonGlobal.ironmon_run_id = "alpha-native-run"
    world.instance_variables.each { |name| world.remove_instance_variable(name) }
    world.new_attempt("alpha_runtime")
    $Trainer.badges[0] = true
    $Trainer.badges[1] = true
    Ironmon.with_statistics_battle_type(:trainer) { Ironmon.begin_statistics_battle }
    native_trainer = NPCTrainer.new("A deliberately different display name", :LEADER_Wattson)
    native_trainer.party = [opponent]
    battle = PokeBattle_Battle.new(QuietScene.new, [pokemon], [opponent], [$Trainer], [native_trainer])
    battle.internalBattle = true
    battle.instance_variable_set(:@decision, 1)
    battle.instance_variable_set(:@usedInBattle, [[false], [false]])
    battle.instance_variable_set(:@initialItems, [[nil], [nil]])
    battle.define_singleton_method(:pbGainCosmeticsMoney) { 0 }
    battle.define_singleton_method(:pbGainMoney) { |_money| nil }
    battle.define_singleton_method(:pbDisplayPaused) { |_message| nil }
    battle.define_singleton_method(:pbRecordAndStoreCaughtPokemon) { nil }
    collection::Combat.admit(battle)
    $game_temp.in_battle = true
    result = battle.pbEndOfBattle
    assert(result == 1 && Ironmon.current_run_attempt["result"] == "won", "the real native Wattson victory completes the run")
    assert(ledger["attempts_won"] == 1 && $PokemonGlobal.ironmon_run_result == "won", "normal win counters and run state agree")
    assert(facts.count { |fact| fact["type"] == "attempt.completed" } == 1, "one authoritative completion is emitted")
    assert(facts.index { |fact| fact["type"] == "battle.ended" } < facts.index { |fact| fact["type"] == "attempt.completed" }, "final battle statistics are committed before the attempt closes")
    recipe = connection.sent.find { |event, _payload| event == "run_completed" }&.last&.fetch("recipe")
    assert(recipe && recipe["result"] == "won" && recipe["statistics"]["attempts_won"] == 1, "the normal completed-run recipe contains the win")
    assert(recipe["statistics"]["battles_completed"] == 1, "the final trainer battle is counted before completing the run")
    assert(!Ironmon.complete_first_alpha_battle(battle, 1) && !Ironmon.complete_run(:lost) && !Ironmon.complete_run(:abandoned), "duplicate victory and later reset/loss cannot replace a win")
    completion = facts.find { |fact| fact["type"] == "attempt.completed" }
    assert(completion["payload"]["completion_milestone"] == Ironmon::FIRST_ALPHA_COMPLETION_MILESTONE, "the achievement receives authoritative eligibility")
    $scene = Scene_Map.allocate
    interpreter = Struct.new(:running?).new(true)
    $game_system = Struct.new(:map_interpreter).new(interpreter)
    saves = 0
    save_path = File.join(output_root, "completed-metadata.dat")
    replace.call(Game, :save, proc do |_slot, _auto = false, safe: true|
      saves += 1
      File.binwrite(save_path, Marshal.dump($PokemonGlobal))
      true
    end)
    assert(!Ironmon.save_first_alpha_completion && saves == 0, "completion saving waits for battle cleanup")
    $game_temp.in_battle = false
    $Trainer.badges[2] = true
    assert(!Ironmon.save_first_alpha_completion && saves == 0, "completion saving waits for gym reward dialogue")
    interpreter[0] = false
    assert(Ironmon.save_first_alpha_completion && saves == 1, "completion is saved once at the safe overworld boundary")
    recipe = connection.sent.reverse.find { |event, _payload| event == "run_completed" }.last.fetch("recipe")
    assert(recipe["statistics"]["badges_earned"] == 3 && ledger["last_completed_attempt"]["statistics"]["badges_earned"] == 3, "post-battle gym rewards reach both won-run history and the saved ledger")
    assert(!Ironmon.save_first_alpha_completion && saves == 1, "later frames never repeat the completion save")
    $PokemonGlobal = Marshal.load(File.binread(save_path))
    Ironmon.restore_run_ledger(Ironmon.run_ledger_snapshot)
    assert(Ironmon.current_run_attempt["result"] == "won" && Ironmon.run_ledger["attempts_won"] == 1, "normalization and disk reload retain the won attempt")
    assert(!Ironmon.complete_run(:lost) && !Ironmon.complete_run(:abandoned), "reloaded wins remain terminal")
    batch = { "schema_version" => 1, "profile_id" => "11111111111111111111111111111111", "epoch_id" => journal.epoch_id, "stream_id" => "33333333333333333333333333333333", "events" => facts }
    File.binwrite(File.join(output_root, "alpha-completion-native.json"), JSON.generate({ "batch" => batch, "recipe" => recipe }))
    ledger = Ironmon.default_run_ledger
    ledger["current_attempt"] = { "run_id" => "not-completed", "attempt_number" => 2, "seed" => 77, "result" => "active", "active_seconds" => 0.0, "statistics" => Ironmon.default_attempt_statistics }
    $PokemonGlobal.ironmon_run_ledger = ledger
    assert(!Ironmon.complete_first_alpha_battle(battle, 2), "losing against Wattson never wins")
    native_trainer.trainer_type = :LEADER_Roxanne
    assert(!Ironmon.complete_first_alpha_battle(battle, 1), "other trainer classes never complete the Alpha endpoint")
    native_trainer.trainer_type = :LEADER_Wattson
    battle.internalBattle = false
    assert(!Ironmon.complete_first_alpha_battle(battle, 1), "external/simulated fights cannot qualify")
    battle.internalBattle = true
    replace.call(Ironmon, :first_alpha_completion_enabled?, proc { false })
    assert(!Ironmon.complete_first_alpha_battle(battle, 1), "retiring the endpoint leaves later content runs active")
    assert(Ironmon.current_run_attempt["result"] == "active", "nonqualifying events leave the run untouched")
    return { "status" => "passed", "native_wattson_victory" => true, "normal_won_recipe" => true, "normal_win_counter" => true, "duplicate_terminal_guard" => true, "safe_save_and_reload" => true, "retired_endpoint" => true }
  ensure
    collection::Combat.release_battle(battle, true) if battle && collection
    overrides.reverse_each { |object, name, method| object.singleton_class.define_method(name, method) } if overrides
    [[Ironmon, original_ironmon], [world, original_world], [collection, original_collection]].each do |object, saved|
      next if !object || !saved
      object.instance_variables.each { |name| object.remove_instance_variable(name) if !saved.key?(name) }
      saved.each { |name, value| object.instance_variable_set(name, value) }
    end
    $Trainer, $PokemonGlobal, $PokemonSystem, $PokemonTemp, $game_switches, $game_variables, $game_temp, $game_map, $PokemonBag, $scene, $game_system = original_globals if original_globals
  end
end
