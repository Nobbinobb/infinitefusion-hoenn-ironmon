module IronmonCalculationPacingTests
  def self.assert(condition, message)
    raise "Calculation pacing validation failed: #{message}" if !condition
  end

  def self.now
    return Process.clock_gettime(Process::CLOCK_MONOTONIC)
  end

  def self.with_scheduler(real_time = false)
    methods = [:tracker_uptime_seconds, :tracker_opening_loss_reset_pending?,
      :tracker_archive_lookup_menu_ready?, :tracker_obtainability_session_ready?,
      :tracker_obtainability_runtime_ready?, :tracker_obtainability_background_safe?,
      :tracker_battle_id, :with_generation_profile]
    originals = methods.map { |name| [name, Ironmon.method(name)] }.to_h
    variables = [:@tracker_obtainability_services, :@tracker_obtainability_last_work_at]
    saved_variables = variables.map { |name| [name, Ironmon.instance_variable_get(name)] }.to_h
    state = { :time => 100.0, :menu => false, :pending => false, :profile => nil }
    Ironmon.define_singleton_method(:tracker_uptime_seconds) { state[:time] } if !real_time
    Ironmon.define_singleton_method(:tracker_opening_loss_reset_pending?) { state[:pending] }
    Ironmon.define_singleton_method(:tracker_archive_lookup_menu_ready?) { state[:menu] }
    Ironmon.define_singleton_method(:tracker_obtainability_session_ready?) { true }
    Ironmon.define_singleton_method(:tracker_obtainability_runtime_ready?) { true }
    Ironmon.define_singleton_method(:tracker_obtainability_background_safe?) { true }
    Ironmon.define_singleton_method(:tracker_battle_id) { nil }
    if !real_time
      Ironmon.define_singleton_method(:with_generation_profile) do |profile, &block|
        previous = state[:profile]
        state[:profile] = profile
        begin
          block.call
        ensure
          state[:profile] = previous
        end
      end
    end
    Ironmon.instance_variable_set(:@tracker_obtainability_services, {})
    Ironmon.instance_variable_set(:@tracker_obtainability_last_work_at, nil)
    yield state
  ensure
    originals.each { |name, callable| Ironmon.define_singleton_method(name, callable) } if originals
    saved_variables.each { |name, value| Ironmon.instance_variable_set(name, value) } if saved_variables
  end

  def self.test_wall_clock_cadence
    with_scheduler do |state|
      budgets = []
      recipe = { "run_id" => "calculation-cadence", "active_run" => true,
        "generation_profile_id" => "calculation-profile" }
      service = Ironmon.tracker_obtainability_service(recipe)
      service.define_singleton_method(:advance_for_milliseconds) do |budget|
        IronmonCalculationPacingTests.assert(state[:profile] == "calculation-profile",
          "a work slice uses its own run generation profile")
        budgets << budget
        state[:time] += budget / 1000.0
      end
      service.request_foreground
      1_000.times { Ironmon.update_tracker_obtainability }
      assert(budgets == [Ironmon::TrackerObtainabilityService::LIVE_FOREGROUND_MILLISECONDS],
        "a thousand fast logical updates cannot multiply the live calculation budget")
      assert(state[:profile].nil?, "the generation profile is restored between game updates")
      state[:time] += 0.004
      Ironmon.update_tracker_obtainability
      assert(budgets.length == 1, "the next work slice waits until its wall-clock cadence")
      state[:time] += 0.001
      Ironmon.update_tracker_obtainability
      assert(budgets.length == 2, "preparation continues at the next available cadence")
      state[:time] += 0.02
      state[:pending] = true
      Ironmon.update_tracker_obtainability
      assert(budgets.length == 2, "a pending reset cannot advance an archived calculation")
      state[:pending] = false
      state[:time] += 3.0
      service.request_background
      Ironmon.update_tracker_obtainability
      assert(budgets.last == Ironmon::TrackerObtainabilityService::LIVE_BACKGROUND_MILLISECONDS,
        "unrequested live preparation keeps the smaller background slice")
      archived = recipe.merge("active_run" => false, "result" => "lost")
      assert(Ironmon.tracker_obtainability_service(archived).equal?(service) && service.archived?,
        "archiving an unfinished run retains its existing calculation progress")
      state[:menu] = true
      state[:time] += 0.02
      service.request_foreground
      Ironmon.update_tracker_obtainability
      assert(budgets.last == Ironmon::TrackerObtainabilityService::LIVE_FOREGROUND_MILLISECONDS,
        "explicit title-menu preparation receives the same foreground priority")
      service.instance_variable_set(:@phase, :complete)
      assert(Ironmon.tracker_obtainability_service(archived).equal?(service),
        "archiving a finished run reuses its completed result")
    end
  end

  def self.test_resumable_budget
    service = Ironmon::TrackerObtainabilityService.new({}, true)
    completed = []
    service.define_singleton_method(:advance_work_unit) do
      deadline = IronmonCalculationPacingTests.now + 0.0002
      1 + 1 while IronmonCalculationPacingTests.now < deadline
      completed << completed.length
      @phase = :complete if completed.length == 400
    end
    timings = []
    until service.complete?
      started = now
      service.advance_for_milliseconds(Ironmon::TrackerObtainabilityService::LIVE_FOREGROUND_MILLISECONDS)
      timings << now - started
      assert(timings.length < 1_000, "sliced work must eventually finish")
    end
    assert(timings.length >= 5 && completed == (0...400).to_a,
      "short live slices resume the same Fiber and preserve every work unit exactly once")
    return { "chunks" => timings.length, "maximum_chunk_ms" => timings.max * 1000,
      "total_work_ms" => timings.sum * 1000 }
  end

  def self.test_overrun_recovery
    with_scheduler do |state|
      calls = 0
      service = Ironmon.tracker_obtainability_service({ "run_id" => "calculation-overrun",
        "active_run" => true, "generation_profile_id" => "calculation-profile" })
      service.define_singleton_method(:advance_for_milliseconds) do |_budget|
        calls += 1
        state[:time] += 0.05
      end
      service.request_foreground
      1_000.times { Ironmon.update_tracker_obtainability }
      assert(calls == 1, "an indivisible native overrun cannot trigger immediate catch-up work")
      state[:time] += 0.016
      Ironmon.update_tracker_obtainability
      assert(calls == 1, "a native overrun leaves a complete game-update interval after finishing")
      state[:time] += 0.001
      Ironmon.update_tracker_obtainability
      assert(calls == 2, "foreground preparation resumes after recovering from a native overrun")
    end
  end

  def self.recipe
    if ENV["IRONMON_CALCULATION_RECIPE_PATH"]
      raw = Ironmon.tracker_stringify_catalog_keys(JSON.parse(
        Ironmon.generation_profile_runtime_json(File.binread(ENV["IRONMON_CALCULATION_RECIPE_PATH"]))))
      return Ironmon.tracker_flatten_completed_recipe(raw)
    end
    result = { "run_id" => "calculation-native-proof", "result" => "lost",
      "seed" => 1_187_411_801, "configuration" => Ironmon::Configuration.new.to_h,
      "generation_profile_id" => Ironmon.current_generation_profile_id,
      "data_mode" => "classic", "item_mappings" => {}, "tm_mappings" => {},
      "species_generator_version" => Ironmon::SpeciesGenerator::SCHEMA_VERSION,
      "base_stat_source_fingerprint" => Ironmon.base_stat_source_fingerprint,
      "player_fusion_generator_version" => Ironmon::PlayerFusionMapper::SCHEMA_VERSION,
      "item_generator" => { "rules_version" => Ironmon::ItemSlotGenerator::POOL_RULES_VERSION } }
    Ironmon.evolution_metadata_values.each do |key, value|
      result[key.to_s.sub(/\Aironmon_/, "")] = value
    end
    return result
  end

  def self.prepare(service, budget)
    service.configure_material_pair_preparation(true)
    timings = []
    started = now
    while service.scheduled_advance_allowed?
      phase = service.instance_variable_get(:@phase).to_s
      chunk_started = now
      Ironmon.with_generation_profile(service.generation_profile_id) do
        service.advance_for_milliseconds(budget)
      end
      timings << { "phase" => phase, "milliseconds" => (now - chunk_started) * 1000 }
      assert(now - started < 90, "native source preparation finishes without a tracker worker")
    end
    return { "chunks" => timings.length, "seconds" => now - started,
      "maximum_chunk_ms" => timings.map { |entry| entry["milliseconds"] }.max,
      "slow_chunks" => timings.select { |entry| entry["milliseconds"] >= 16 } }
  end

  def self.test_native_sources
    Game.load_sprites_list_caches
    current_recipe = recipe
    message_started = now
    pbGetMapNameFromId(10)
    message_initialization_ms = (now - message_started) * 1000
    sliced = Ironmon::TrackerObtainabilityService.new(current_recipe, true)
    original_credits = Ironmon.method(:sprite_credit_catalog)
    Ironmon.define_singleton_method(:sprite_credit_catalog) do |*_arguments|
      raise "Compact obtainability proofs must not scan the sprite-credit CSV"
    end
    loader_timings = []
    if ENV["IRONMON_CALCULATION_TRACE"] == "1"
      traced_methods = [:pool, :build_pool, :identities, :decode, :prepare_generators,
        :prepare_encounter_tables, :process_encounter_table, :species_generator,
        :map_number, :map_id, :select_pool, :species_number, :normalize_species_id,
        :add_direct_source, :add_plan, :pbGetMapNameFromId, :get, :load, :each,
        :initialize, :parse,
        :tracker_area_catalog_document, :tracker_validate_area_catalog,
        :custom_fusion_pool_service, :tracker_normal_evolution_generator,
        :evolution_generator_for, :normal_species_pool, :evolution_catalog,
        :generation_profile_game_data, :generation_profile, :current_generation_profile,
        :fnv1a_64_fingerprint, :fnv1a_64_entries].map { |name| [name, true] }.to_h
      stacks = {}
      trace = TracePoint.new(:call, :return) do |event|
        next if !traced_methods[event.method_id]
        stack = (stacks[Fiber.current.object_id] ||= [])
        if event.event == :call
          stack << [event.defined_class.to_s, event.method_id.to_s, now]
        elsif !stack.empty?
          owner, method, began = stack.pop
          elapsed = now - began
          loader_timings << { "owner" => owner, "method" => method,
            "milliseconds" => elapsed * 1000 } if elapsed >= 0.016
        end
      end
      trace.enable
    end
    begin
      sliced_measurement = prepare(sliced, Ironmon::TrackerObtainabilityService::LIVE_FOREGROUND_MILLISECONDS)
    ensure
      trace.disable if trace
      Ironmon.define_singleton_method(:sprite_credit_catalog, original_credits)
    end
    uninterrupted = Ironmon::TrackerObtainabilityService.new(current_recipe, true)
    original_measurement = prepare(uninterrupted, Ironmon::TrackerObtainabilityService::FOREGROUND_MILLISECONDS)
    assert(sliced.send(:tracker_base_proof_snapshot) == uninterrupted.send(:tracker_base_proof_snapshot) &&
      sliced.send(:tracker_resource_supply_snapshot) == uninterrupted.send(:tracker_resource_supply_snapshot) &&
      sliced.instance_variable_get(:@direct_fusion_pair_offsets) == uninterrupted.instance_variable_get(:@direct_fusion_pair_offsets),
      "live slicing preserves all native encounter, resource, evolution and direct fusion proofs")
    return { "live_slices" => sliced_measurement, "previous_slices" => original_measurement,
      "runtime_message_initialization_ms" => message_initialization_ms,
      "loader_timings" => loader_timings }
  end

  def self.scheduled_source_preparation(current_recipe, foreground, update_rate)
    variables = [:@profile_custom_fusion_pool_services, :@tracker_area_catalog_documents,
      :@tracker_obtainability_source_catalogs, :@tracker_area_encounter_catalogs,
      :@tracker_evolution_generators]
    saved = variables.map { |variable| [variable, Ironmon.instance_variable_get(variable)] }.to_h
    variables.each { |variable| Ironmon.instance_variable_set(variable, {}) }
    with_scheduler(true) do |_state|
      service = Ironmon.tracker_obtainability_service(current_recipe)
      service.configure_material_pair_preparation(true)
      chunks = []
      original_advance = service.method(:advance_for_milliseconds)
      service.define_singleton_method(:advance_for_milliseconds) do |budget|
        began = IronmonCalculationPacingTests.now
        original_advance.call(budget)
        chunks << { "budget_ms" => budget, "started" => began,
          "milliseconds" => (IronmonCalculationPacingTests.now - began) * 1000 }
      end
      started = now
      next_tick = started
      next_request = started
      while service.scheduled_advance_allowed?
        current = now
        if current >= next_request
          service.request_background
          service.request_foreground if foreground
          next_request = current + 0.25
        end
        if current >= next_tick
          Ironmon.update_tracker_obtainability
          next_tick = current + 1.0 / update_rate
        else
          sleep([next_tick - current, 0.001].min)
        end
        assert(now - started < 45, "wall-clock scheduled native preparation finishes")
      end
      elapsed = now - started
      expected_budget = foreground ? Ironmon::TrackerObtainabilityService::LIVE_FOREGROUND_MILLISECONDS :
        Ironmon::TrackerObtainabilityService::LIVE_BACKGROUND_MILLISECONDS
      assert(chunks.all? { |entry| entry["budget_ms"] == expected_budget },
        "only explicit requests promote native preparation to foreground priority")
      assert(service.instance_variable_get(:@phase) == :player_fusions,
        "scheduled source preparation reaches the same tracker-worker handoff")
      gaps = chunks.each_cons(2).map { |first, second| second["started"] - first["started"] }
      assert(gaps.all? { |seconds| seconds >= Ironmon::TrackerObtainabilityService::WORK_INTERVAL_SECONDS - 0.00001 },
        "high logical update rates cannot multiply the wall-clock preparation allowance")
      assert(elapsed < 5.0, "explicit foreground source preparation remains fast (#{elapsed}s)") if foreground
      return { :proofs => service.send(:tracker_base_proof_snapshot),
        :resources => service.send(:tracker_resource_supply_snapshot),
        :direct_pairs => service.instance_variable_get(:@direct_fusion_pair_offsets),
        :measurement => { "foreground" => foreground, "logical_updates_per_second" => update_rate,
          "seconds" => elapsed, "chunks" => chunks.length,
          "work_ms" => chunks.sum { |entry| entry["milliseconds"] },
          "maximum_chunk_ms" => chunks.map { |entry| entry["milliseconds"] }.max,
          "minimum_start_gap_ms" => gaps.min.to_f * 1000,
          "budget_ms" => expected_budget } }
    end
  ensure
    saved.each { |variable, value| Ironmon.instance_variable_set(variable, value) } if saved
  end

  def self.test_scheduled_native_sources
    current_recipe = recipe
    foreground = scheduled_source_preparation(current_recipe, true, 400)
    normal_speed = scheduled_source_preparation(current_recipe, true, 40)
    background = scheduled_source_preparation(current_recipe, false, 400)
    [normal_speed, background].each do |result|
      assert(result[:proofs] == foreground[:proofs] && result[:resources] == foreground[:resources] &&
        result[:direct_pairs] == foreground[:direct_pairs],
        "foreground priority and logical update rate preserve every native proof and material pair")
    end
    assert(background[:measurement]["seconds"] > foreground[:measurement]["seconds"] * 2,
      "passive background requests retain their smaller allowance")
    return { "foreground_fast_game" => foreground[:measurement],
      "foreground_normal_game" => normal_speed[:measurement], "background_fast_game" => background[:measurement],
      "seed" => current_recipe["seed"] }
  end

  def self.test_pool_publication
    profile_id = Ironmon.current_generation_profile_id
    service = Ironmon::ProfileCustomFusionPool.new(
      Ironmon.generation_profile_component_path("custom_fusion_pool", profile_id))
    result = nil
    checkpoints = 0
    worker = Fiber.new do
      result = service.pool(proc do
        checkpoints += 1
        Fiber.yield
      end)
    end
    worker.resume
    assert(worker.alive? && !result && !service.instance_variable_get(:@pool) &&
      !service.instance_variable_get(:@accepted_by_number) && !service.instance_variable_get(:@numbers) &&
      !service.instance_variable_get(:@packed_pool_membership) && !service.fingerprint,
      "a cold pool yields before publishing any identity, membership index or fingerprint")
    worker.resume while worker.alive?
    Ironmon.with_generation_profile(profile_id) do
      expected = Ironmon.custom_fusion_pool_service
      assert(result == expected.pool && service.numbers == expected.numbers &&
        service.packed_pool_membership == expected.packed_pool_membership &&
        service.fingerprint == expected.fingerprint && result.frozen?,
        "resuming a cold profile pool preserves exact ordering, membership and fingerprint")
    end
    assert(service.pool(proc { raise "A warm pool must not rebuild" }).equal?(result),
      "completed immutable pool data is reused without decoding or rebuilding")
    assert(Ironmon.species_pool_fingerprint(result) == service.fingerprint,
      "the cooperative fingerprint retains the established seed hashing contract")
    return { "checkpoints" => checkpoints, "identities" => result.length }
  end

  def self.cold_closure_handoff(current_recipe, baseline)
    profile_id = current_recipe["generation_profile_id"]
    variables = [:@generation_profile_base_catalogs, :@tracker_search_fusion_name_parts]
    saved = variables.map { |variable| [variable, Ironmon.instance_variable_get(variable)] }.to_h
    variables.each { |variable| Ironmon.instance_variable_set(variable, {}) }
    original_names = Ironmon.method(:tracker_search_fusion_name_parts)
    original_parser = HTTPLite::JSON.method(:parse)
    native_parses = []
    HTTPLite::JSON.define_singleton_method(:parse) do |bytes|
      began = IronmonCalculationPacingTests.now
      result = original_parser.call(bytes)
      native_parses << (IronmonCalculationPacingTests.now - began) * 1000
      result
    end
    if baseline
      Ironmon.define_singleton_method(:tracker_search_fusion_name_parts) do |*_arguments|
        original_names.call
      end
    end
    service = Ironmon::TrackerObtainabilityService.new(current_recipe, true)
    Ironmon.with_generation_profile(profile_id) { service.send(:prepare_generators) }
    number = Ironmon.with_generation_profile(profile_id) { Ironmon.custom_fusion_pool_service.numbers.first }
    service.instance_variable_set(:@phase, :player_fusions)
    service.instance_variable_set(:@material_pairs_prepared, true)
    service.instance_variable_set(:@fusion_closure_job_id, "cold-closure-handoff")
    service.instance_variable_set(:@direct_fusion_pair_offsets, [0])
    original_unit = service.method(:advance_work_unit)
    service.define_singleton_method(:advance_work_unit) do
      original_unit.call
      Fiber.yield if @derived_caught_fusion_index > 0
    end
    service.queue_fusion_closure_result({ "job_id" => "cold-closure-handoff",
      "direct_encounter_fusion_ids" => [number] })
    timings = []
    slow_chunks = []
    first_processed = nil
    until service.instance_variable_get(:@derived_caught_fusion_index) > 0
      started = now
      Ironmon.with_generation_profile(profile_id) { service.advance_for_milliseconds(1.0) }
      timings << (now - started) * 1000
      slow_chunks << { "milliseconds" => timings.last } if timings.last >= 16
      first_processed ||= service.instance_variable_get(:@derived_caught_fusion_index) if timings.length == 1
      assert(timings.length < 5_000, "a cold closure handoff resumes to completion")
      if !baseline && !Ironmon.instance_variable_get(:@generation_profile_base_catalogs)[profile_id]
        assert(!Ironmon.instance_variable_get(:@tracker_search_fusion_name_parts)[profile_id],
          "a partial base catalog cannot publish a partial fusion-name cache")
      end
    end
    assert(first_processed == 0, "the cold name loader yields before processing a derived fusion") if !baseline
    return { :measurement => { "chunks" => timings.length, "maximum_chunk_ms" => timings.max,
      "total_ms" => timings.sum, "native_parse_ms" => native_parses, "slow_chunks" => slow_chunks },
      :proofs => service.send(:tracker_base_proof_snapshot),
      :plans => service.instance_variable_get(:@plans),
      :catalog => Ironmon.instance_variable_get(:@generation_profile_base_catalogs)[profile_id] }
  ensure
    Ironmon.define_singleton_method(:tracker_search_fusion_name_parts, original_names) if original_names
    HTTPLite::JSON.define_singleton_method(:parse, original_parser) if original_parser
    saved.each { |variable, value| Ironmon.instance_variable_set(variable, value) } if saved
  end

  def self.test_closure_handoff
    current_recipe = recipe
    previous = cold_closure_handoff(current_recipe, true)
    sliced = cold_closure_handoff(current_recipe, false)
    assert(previous[:proofs] == sliced[:proofs] && previous[:plans] == sliced[:plans],
      "slicing the cold closure handoff preserves every derived-fusion and unfusion proof")
    assert(typed_tree_equal?(previous[:catalog], sliced[:catalog]),
      "native profile parsing preserves every catalog string byte and numeric type")
    ["{\"text\":\"before\\u0000after\"}", "{\"value\":1.0}",
     "{\"value\":1e2}", "{\"value\":9007199254740993}"].each do |unsafe|
      assert(!Ironmon.generation_profile_native_json_safe?(unsafe),
        "native profile parsing preserves NUL text, floating-point values and large integers through its original parser")
    end
    assert(Ironmon.generation_profile_native_json_safe?("{\"text\":\"1.5 and 1e2\",\"value\":42}"),
      "numeric-looking text does not disable safe native parsing")
    assert(sliced[:measurement]["maximum_chunk_ms"] < 100,
      "the cold fusion-name handoff retains only a bounded native parser operation (#{sliced[:measurement]["maximum_chunk_ms"]})")
    service = Ironmon::TrackerObtainabilityService.new(current_recipe, true)
    previous_edges = [7]
    previous_words = [9]
    service.instance_variable_set(:@tracker_executable_evolution_edges, previous_edges)
    service.instance_variable_set(:@tracker_obtainable_fusion_words, previous_words)
    begin
      service.send(:apply_tracker_closure_summary, { "obtainable_fusion_words" =>
        Array.new((((NB_POKEMON * NB_POKEMON) + NB_POKEMON) >> 5) + 1, 0),
        "packed_executable_evolution_edges" => "", "obtainable_count" => -1 })
      raise "An invalid native summary was accepted"
    rescue Ironmon::TrackerLookupError
    end
    assert(service.instance_variable_get(:@tracker_executable_evolution_edges).equal?(previous_edges) &&
      service.instance_variable_get(:@tracker_obtainable_fusion_words).equal?(previous_words),
      "invalid closure metadata cannot publish replacement indexes")
    return { "unsliced_name_loader" => previous[:measurement], "sliced_name_loader" => sliced[:measurement],
      "seed" => current_recipe["seed"] }
  end

  def self.typed_tree_equal?(first, second)
    return false if first.class != second.class
    if first.is_a?(Hash)
      return first.keys == second.keys && first.all? do |key, value|
        typed_tree_equal?(value, second[key])
      end
    elsif first.is_a?(Array)
      return first.length == second.length && first.each_index.all? do |index|
        typed_tree_equal?(first[index], second[index])
      end
    elsif first.is_a?(String)
      return first.bytes == second.bytes
    end
    return first.eql?(second)
  end

  def self.run
    test_wall_clock_cadence
    test_overrun_recovery
    results = { "resumable_budget" => test_resumable_budget, "native_sources" => test_native_sources,
      "pool_publication" => test_pool_publication, "closure_handoff" => test_closure_handoff,
      "scheduled_native_sources" => test_scheduled_native_sources }
    output = $ironmon_speed_up_acceptance_output.to_s
    File.binwrite(File.join(output, "calculation-pacing.json"), Ironmon.tracker_json_generate(results)) if !output.empty?
    puts "Calculation pacing: wall-clock cadence, reset eligibility, archive reuse and exact native proofs passed"
    puts Ironmon.tracker_json_generate(results)
  end
end

IronmonCalculationPacingTests.run
