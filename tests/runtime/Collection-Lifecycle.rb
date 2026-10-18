module IronmonCollectionLifecycleRuntimeTests
  OUTPUT_PATH = $ironmon_collection_lifecycle_test_output_path.to_s
  TestTrainer = Struct.new(:party, :save_slot) do
    def badge_count
      return 0
    end

    def badges
      return []
    end
  end

  class MemoryJournal
    attr_reader :epoch_id

    def initialize
      @epoch_id = "33333333333333333333333333333333"
      @contexts = {}
    end

    def runtime_context(key)
      return Marshal.load(Marshal.dump(@contexts[key]))
    end

    def bind_runtime_context(key, value)
      @contexts[key] = Marshal.load(Marshal.dump(value))
    end

    def fence
      return nil
    end
  end

  def self.assert(value, message)
    raise "Gameplay collection lifecycle runtime test failed: #{message}" if !value
  end

  def self.metadata
    value = PokemonGlobalMetadata.new
    value.ironmon_mode = true
    ledger = Ironmon.default_run_ledger
    ledger["current_attempt"] = {
      "run_id" => "run-seed-99", "attempt_number" => 1, "seed" => 99, "result" => "active"
    }
    value.ironmon_run_ledger = ledger
    return value
  end

  def self.run
    collection = Ironmon::GameplayCollection
    world = collection::World
    original_globals = [$Trainer, $PokemonGlobal, $game_map, $PokemonTemp, $PokemonSystem, $game_switches]
    original_world = world.instance_variables.to_h { |name| [name, world.instance_variable_get(name)] }
    original_collection = [collection.attempt_id, collection.branch_id, collection.instance_variable_get(:@save_lineage_id)]
    lifecycle_names = [:@reset_in_progress, :@reset_transaction, :@reset_save_slot, :@seed_import_in_progress, :@seed_import_transaction, :@reset_notice, :@seed_to_avoid]
    original_ironmon = lifecycle_names.to_h { |name| [name, Ironmon.instance_variable_get(name)] }
    overrides = []
    replace = proc do |object, name, implementation|
      overrides << [object.singleton_class, name, object.method(name)]
      object.singleton_class.define_method(name, implementation)
    end
    journal = MemoryJournal.new
    facts = []
    counter = 0
    replace.call(collection, :enabled?, proc { true })
    replace.call(collection, :journal, proc { journal })
    replace.call(collection, :flush, proc { journal.fence })
    replace.call(collection, :next_id, proc { |kind| counter += 1; "lifecycle:#{kind}:#{counter}" })
    replace.call(collection, :emit, proc do |type, payload = {}, context = {}|
      facts << { "type" => type, "attempt_id" => collection.attempt_id, "payload" => payload, "context" => context }
      facts.length
    end)
    replace.call(collection::Ownership, :baseline, proc { |*_args| nil })
    replace.call(collection::Ownership, :poll, proc { |*_args| nil })
    world.instance_variables.each { |name| world.remove_instance_variable(name) }
    $Trainer = TestTrainer.new([], "CollectionFixture")
    $PokemonGlobal = metadata
    $game_map = nil
    $PokemonTemp = nil
    $PokemonSystem = nil
    $game_switches = Game_Switches.new
    begin
      world.new_attempt("same_seed_one")
      first_id = collection.attempt_id
      first_token = $PokemonGlobal.instance_variable_get(:@ironmon_collection_attempt_association)["token"]
      first_save = Marshal.dump($PokemonGlobal)
      world.new_attempt("same_seed_two")
      second_id = collection.attempt_id
      second_token = $PokemonGlobal.instance_variable_get(:@ironmon_collection_attempt_association)["token"]
      assert(first_id != second_id && first_token != second_token, "actual repeated-seed attempts have distinct external identities and saved association tokens")
      starts = facts.count { |fact| fact["type"] == "attempt.started" }
      $PokemonGlobal = Marshal.load(first_save)
      restored = world.bind_attempt(false, "load")
      assert(restored == first_id, "loading the older same-seed save restores its own attempt, not the newer seed alias")
      assert(facts.count { |fact| fact["type"] == "attempt.started" } == starts, "ordinary load does not earn an attempt start")
      original_binding = journal.runtime_context("attempt_bindings")[first_token]
      [{ "run_id" => "run-seed-100" }, { "attempt_number" => 2 }].each do |changed_identity|
        $PokemonGlobal = Marshal.load(first_save)
        $PokemonGlobal.ironmon_run_ledger["current_attempt"].merge!(changed_identity)
        rebound = world.bind_attempt(false, "tracking_recovered")
        rebound_token = $PokemonGlobal.instance_variable_get(:@ironmon_collection_attempt_association)["token"]
        assert(rebound != first_id && rebound_token != first_token, "a stale token from untracked resets cannot reuse an earlier native attempt")
        assert(journal.runtime_context("attempt_bindings")[first_token] == original_binding, "recovery preserves the earlier attempt binding without replacing its native identity")
        assert(facts.last["type"] == "collection.attempt_context_initialized", "the newly observed native attempt receives a technical anchor")
        assert(facts.count { |fact| fact["type"] == "attempt.started" } == starts, "recovery never fabricates an unobserved attempt start")
      end
      $PokemonGlobal = Marshal.load(first_save)
      assert(world.bind_attempt(false, "load") == first_id, "the original saved attempt still resolves after stale-token recovery")
      $PokemonGlobal = metadata
      world.bind_attempt(false, "old_save")
      assert(facts.last["type"] == "collection.attempt_context_initialized", "an old save without a token creates a technical anchor")
      assert(facts.count { |fact| fact["type"] == "attempt.started" } == starts, "first observation of an old save never grants start progress")
      $PokemonGlobal.repel = 10
      $PokemonGlobal.tempRepel = true
      assert(world.movement_protection["source_kind"] == "radar_temporary", "native temporary radar repel is not credited as an item Repel")
      $PokemonGlobal.tempRepel = false
      assert(world.movement_protection["source_kind"] == "standard_repel", "actual non-temporary Repel counter retains its own classification")
      parent = Object.new
      parent_id = collection.pokemon_instance_id(parent)
      ordinary_copy = parent.clone
      collection::Ownership.assign_retained_identity({ :candidate => parent }, ordinary_copy, :swap, nil)
      assert(collection.pokemon_instance_id(ordinary_copy) == parent_id, "an ordinary captured candidate clone preserves its individual identity")
      transformed_copy = parent.clone
      collection::Ownership.assign_retained_identity({ :candidate => parent }, transformed_copy, :reverse_and_take, nil)
      assert(collection.pokemon_instance_id(transformed_copy) != parent_id, "a genuinely new transformed result cannot inherit a parent entity ID")
      collection::Ownership.assign_retained_identity({ :candidate => parent }, parent, :reverse_and_take, nil)
      assert(collection.pokemon_instance_id(parent) == parent_id, "a same-object persistent transformation preserves its individual identity")
      world.new_attempt("failure_source")
      source_id = collection.attempt_id
      source_metadata = Marshal.dump($PokemonGlobal)
      source_token = $PokemonGlobal.instance_variable_get(:@ironmon_collection_attempt_association)["token"]
      transaction = {
        "save_slot" => "CollectionFixture", "source_run_id" => "run-seed-99", "seed_to_avoid" => 99,
        "completed_recipe" => { "statistics" => { "result" => "abandoned" } }, "rollback_data" => {}
      }
      Ironmon.instance_variable_set(:@reset_in_progress, true)
      Ironmon.instance_variable_set(:@seed_import_in_progress, false)
      Ironmon.instance_variable_set(:@reset_transaction, transaction)
      world.stage_transition("manual_reset", transaction)
      staged_token = nil
      saves = 0
      replace.call(Ironmon, :apply_preset, proc do |*_args|
        $PokemonGlobal = IronmonCollectionLifecycleRuntimeTests.metadata
        world.new_attempt("staged_failure")
        true
      end)
      replace.call(Game, :save, proc do |*_args|
        saves += 1
        staged_token = $PokemonGlobal.instance_variable_get(:@ironmon_collection_attempt_association)["token"]
        false
      end)
      replace.call(Ironmon, :restore_live_save_snapshot, proc do |*_args|
        $PokemonGlobal = Marshal.load(source_metadata)
        nil
      end)
      starts = facts.count { |fact| fact["type"] == "attempt.started" }
      result = Ironmon.finish_pending_reset
      assert(result == false && saves == 1, "actual native finish_pending_reset observes its failed Game.save")
      assert(staged_token && staged_token != source_token, "the staged save already carries a new opaque association")
      assert(collection.attempt_id == source_id, "failed reset preserves the source attempt identity")
      assert(facts.count { |fact| fact["type"] == "attempt.started" } == starts, "a failed save cannot grant an irreversible attempt-start event")
      assert(facts.any? { |fact| fact["type"] == "attempt.transition_resolved" && fact["payload"]["outcome"] == "failed" }, "failed reset emits an explicit failed transition")
      successful = { "completed_recipe" => { "statistics" => { "result" => "abandoned" } } }
      world.stage_transition("manual_reset", successful)
      $PokemonGlobal = metadata
      world.new_attempt("native_generation_stage")
      world.commit_transition(successful)
      transition = facts.reverse.find { |fact| fact["type"] == "attempt.transition_resolved" && fact["payload"]["outcome"] == "success" }
      assert(transition && transition["payload"]["kind"] == "manual_reset", "a successful reset retains its actual transition kind rather than the generation-stage label")
      assert(collection.attempt_id != source_id, "successful reset commits a distinct destination attempt")
      assert(!journal.runtime_context("attempt_bindings").key?(staged_token), "failed destination never becomes a committed external binding")
      File.binwrite(OUTPUT_PATH, "gameplay collection lifecycle runtime tests passed\n")
    ensure
      overrides.reverse_each { |singleton, name, original| singleton.define_method(name, original) }
      world.instance_variables.each { |name| world.remove_instance_variable(name) }
      original_world.each { |name, value| world.instance_variable_set(name, value) }
      original_ironmon.each { |name, value| Ironmon.instance_variable_set(name, value) }
      collection.instance_variable_set(:@attempt_id, original_collection[0])
      collection.instance_variable_set(:@branch_id, original_collection[1])
      collection.instance_variable_set(:@save_lineage_id, original_collection[2])
      $Trainer, $PokemonGlobal, $game_map, $PokemonTemp, $PokemonSystem, $game_switches = original_globals
    end
  rescue Exception => error
    File.binwrite(OUTPUT_PATH, "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}\n")
    raise
  end
end

IronmonCollectionLifecycleRuntimeTests.run
