module IronmonSpeedUpLossTests
  def self.now
    return Process.clock_gettime(Process::CLOCK_MONOTONIC)
  end

  def self.assert(condition, message)
    raise "Speed-up loss validation failed: #{message}" if !condition
  end

  def self.profile
    result = Ironmon::ToggleSpeed.default_configuration
    Ironmon::ToggleSpeed::LANES.each do |lane|
      result["lanes"][lane] << { "id" => "loss-#{lane}-fast",
        "nodes" => [{ "id" => "loss-#{lane}-10", "value" => 10 }] }
    end
    result["links"] << { "id" => "loss-fast", "overworld" => "loss-overworld-10", "battle" => "loss-battle-10" }
    return result
  end

  def self.prepare_attempt(seed)
    $PokemonGlobal.ironmon_starter_species = [:BULBASAUR, :CHARMANDER, :SQUIRTLE]
    $PokemonGlobal.ironmon_rival_lab_decision = nil
    $game_switches[Ironmon::EARLY_GAME_SWITCH_FIRST_RIVAL_WON] = false
    $game_variables[VAR_HOENN_CHOSEN_STARTER_INDEX] = 0
    $game_variables[VAR_TRAINER_GENDER] = 0
    Ironmon.begin_run_attempt(seed)
    Ironmon::GameplayCollection::World.tick
    Ironmon::GameplayCollection.flush
  end

  def self.measure_loss(multiplier, seed, delayed = false)
    prepare_attempt(seed)
    $GameSpeed = SPEEDUP_STAGES.index(multiplier)
    Graphics.update
    journal = Ironmon::GameplayCollection.journal
    original_commit = journal.method(:commit_records)
    if delayed
      journal.define_singleton_method(:commit_records) do |written|
        sleep(0.15)
        original_commit.call(written)
      end
    end
    phases = []
    watched = [:complete_run, :publish_run_completion, :tracker_completed_run_recipe,
      :tracker_current_story_trainer_previews, :complete, :poll, :flush, :fence]
    stacks = Hash.new { |hash, key| hash[key] = [] }
    trace = TracePoint.new(:call, :return) do |event|
      next if !watched.include?(event.method_id)
      key = [Thread.current.object_id, event.defined_class, event.method_id]
      if event.event == :call
        stacks[key] << now
      elsif (started = stacks[key].pop)
        phases << { "owner" => event.defined_class.to_s, "method" => event.method_id.to_s,
          "milliseconds" => (now - started) * 1000 }
      end
    end
    before = Graphics.frame_count
    started = now
    completed = trace.enable { Ironmon.complete_run(:lost) }
    elapsed = now - started
    assert(completed && Ironmon.failed_run_locked?, "a real synthetic attempt completed as a loss")
    assert(Ironmon::GameplayCollection.enabled?, "collection stayed enabled through completion")
    result = { "multiplier" => multiplier, "milliseconds" => elapsed * 1000,
      "injected_writer_delay_ms" => delayed ? 150 : 0,
      "graphics_updates_during_completion" => Graphics.frame_count - before,
      "phases" => phases, "journal" => journal.metrics }
    if delayed
      assert(elapsed >= 0.12, "a durable loss currently waits for the deliberately delayed writer")
      assert(Graphics.frame_count == before, "synchronous journal durability currently blocks graphics during a loss")
    end
    return result
  ensure
    trace.disable if trace
    journal.singleton_class.send(:remove_method, :commit_records) if delayed && journal
  end

  def self.input_allocations
    service = Ironmon::ResponsiveInput
    original = service.method(:update)
    original_suspended = Ironmon::GameplayCollection.capture_suspended
    Ironmon::GameplayCollection.capture_suspended = true
    Ironmon.clear_tracker_controls
    GC.start
    before = GC.stat(:total_allocated_objects)
    started = now
    1000.times { Input.update }
    with_service = { "objects" => GC.stat(:total_allocated_objects) - before,
      "milliseconds" => (now - started) * 1000 }
    service.define_singleton_method(:update) { }
    GC.start
    before = GC.stat(:total_allocated_objects)
    started = now
    1000.times { Input.update }
    without_service = { "objects" => GC.stat(:total_allocated_objects) - before,
      "milliseconds" => (now - started) * 1000 }
    return { "updates" => 1000, "with_responsive_service" => with_service,
      "without_responsive_service" => without_service }
  ensure
    service.define_singleton_method(:update, original) if original
    Ironmon::GameplayCollection.capture_suspended = original_suspended
  end

  def self.run
    collection = Ironmon::GameplayCollection
    output = $ironmon_speed_up_acceptance_output
    assert(output && System.data_directory.start_with?(output), "loss acceptance owns its user-data location")
    assert(!collection.instance_variable_get(:@journal), "no preexisting collection journal can be replaced")
    original_suspended = collection.capture_suspended
    original_directory = collection.instance_variable_get(:@directory_override)
    original_profile = Ironmon::ToggleSpeed.configuration
    original_mode = $PokemonSystem.speedup
    saved = Marshal.load(Marshal.dump(SaveData.compile_save_hash))
    collection.instance_variable_set(:@directory_override, File.join(output, "loss-collection"))
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new
    Ironmon.activate_run_generation_profile(Ironmon.current_generation_profile_id)
    $Trainer.party = [Pokemon.new(:BULBASAUR, 10, nil, false)]
    collection.capture_suspended = false
    $PokemonSystem.speedup = 1
    Ironmon::ToggleSpeed.configure(profile)
    Graphics.update
    results = [measure_loss(1, 123456), measure_loss(10, 123457), measure_loss(10, 123458, true)]
    allocations = input_allocations
    report = { "completion" => results, "input_allocations" => allocations }
    File.binwrite(File.join(output, "loss-and-input-costs.json"), JSON.generate(report))
    puts "Loss completion: enabled isolated collection, real completion timing, bounded delayed writer, and input allocation comparison passed"
  ensure
    collection.capture_suspended = true if collection
    journal = collection.instance_variable_get(:@journal) if collection
    journal.close if journal
    collection.instance_variable_set(:@journal, nil) if collection
    collection.instance_variable_set(:@directory_override, original_directory) if collection
    collection.capture_suspended = original_suspended if collection
    Ironmon::ToggleSpeed.configure(original_profile) if original_profile
    $PokemonSystem.speedup = original_mode if original_mode
    if saved
      SaveData.mark_values_as_unloaded
      SaveData.load_all_values(saved)
    end
  end
end

IronmonSpeedUpLossTests.run
