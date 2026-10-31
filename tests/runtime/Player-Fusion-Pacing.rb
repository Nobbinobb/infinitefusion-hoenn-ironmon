module IronmonPlayerFusionPacingTests
  def self.assert(condition, message)
    raise "Player-fusion pacing validation failed: #{message}" if !condition
  end

  def self.now
    return Process.clock_gettime(Process::CLOCK_MONOTONIC)
  end

  def self.with_runtime
    methods = [:player_fusion_preparation_uptime, :pivot_state, :player_fusion_generator_version]
    originals = methods.map { |name| [name, Ironmon.method(name)] }.to_h
    variables = [:player_fusion_mapper, :player_fusion_mapper_seed, :player_fusion_mapper_state_id,
      :player_fusion_mapper_version, :player_fusion_preparation_mapper, :player_fusion_preparation_seed,
      :player_fusion_preparation_state_id, :player_fusion_preparation_version,
      :player_fusion_preparation_fiber, :player_fusion_preparation_last_work_at]
    saved = variables.map { |name| [name, Ironmon.instance_variable_get("@#{name}")] }.to_h
    variables.each { |name| Ironmon.instance_variable_set("@#{name}", nil) }
    state = { :time => 100.0, :pivot => Ironmon::PivotState.new }
    Ironmon.define_singleton_method(:player_fusion_preparation_uptime) { state[:time] }
    Ironmon.define_singleton_method(:pivot_state) { state[:pivot] }
    Ironmon.define_singleton_method(:player_fusion_generator_version) { Ironmon::PlayerFusionMapper::SCHEMA_VERSION }
    yield state
  ensure
    originals.each { |name, callable| Ironmon.define_singleton_method(name, callable) } if originals
    saved.each { |name, value| Ironmon.instance_variable_set("@#{name}", value) } if saved
  end

  def self.install_test_fiber(state, duration = Ironmon::PLAYER_FUSION_PREPARATION_BUDGET_SECONDS)
    mapper = Object.new
    mapper.define_singleton_method(:preparation_stage) { :fusion_pool }
    mapper.define_singleton_method(:finish_cooperative_preparation) { self }
    fiber = Fiber.new do
      20.times do
        state[:calls] = state.fetch(:calls, 0) + 1
        state[:time] += duration
        Fiber.yield
      end
    end
    Ironmon.instance_variable_set(:@player_fusion_preparation_mapper, mapper)
    Ironmon.instance_variable_set(:@player_fusion_preparation_fiber, fiber)
    Ironmon.instance_variable_set(:@player_fusion_preparation_seed, $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0)
    Ironmon.instance_variable_set(:@player_fusion_preparation_state_id, state[:pivot].object_id)
    Ironmon.instance_variable_set(:@player_fusion_preparation_version, Ironmon::PlayerFusionMapper::SCHEMA_VERSION)
    return mapper
  end

  def self.test_wall_clock_background
    with_runtime do |state|
      mapper = install_test_fiber(state)
      1_000.times { Ironmon.advance_player_fusion_pairing }
      assert(state[:calls] == 1, "a thousand accelerated logical updates share one background allowance")
      state[:time] += 0.016
      Ironmon.advance_player_fusion_pairing
      assert(state[:calls] == 1, "background work waits a full interval after finishing its slice")
      state[:time] += 0.001
      Ironmon.advance_player_fusion_pairing
      assert(state[:calls] == 2, "background work resumes at its next wall-clock opportunity")
      assert(Ironmon.player_fusion_mapper.equal?(mapper) && state[:calls] == 20 &&
        !Ironmon.instance_variable_get(:@player_fusion_preparation_fiber),
        "an explicit gameplay request completes and installs preparation immediately despite its background cooldown")
      assert(state[:pivot].fusion_mappings.empty? && state[:pivot].discovered_fusion_mappings.empty?,
        "background preparation does not create gameplay discoveries")
    end
    with_runtime do |state|
      install_test_fiber(state, 0.2)
      1_000.times { Ironmon.advance_player_fusion_pairing }
      assert(state[:calls] == 1, "an indivisible overrun cannot immediately repeat background work")
      state[:pivot] = Ironmon::PivotState.new
      state[:time] += 0.02
      Ironmon.advance_player_fusion_pairing
      assert(state[:calls] == 1 && !Ironmon.instance_variable_get(:@player_fusion_preparation_fiber) &&
        !Ironmon.instance_variable_get(:@player_fusion_preparation_last_work_at),
        "a changed save context cancels stale work and clears its scheduler clock")
    end
  end

  def self.test_native_mapper
    started = now
    pool = Ironmon.custom_fusion_pool_numbers
    pool_ms = (now - started) * 1000
    seed = ENV.fetch("IRONMON_PLAYER_FUSION_SEED", "1187411801").to_i
    stat_generator = Ironmon::BaseStatGenerator.new(seed, Ironmon.base_stat_source_fingerprint)
    small_pool = 8.times.map { |index| pool[index * pool.length / 8] }
    baseline = Ironmon::PlayerFusionMapper.new(seed, small_pool, {}, {}, stat_generator)
    baseline.prepare
    mapper = Ironmon::PlayerFusionMapper.new(seed, small_pool, {}, {}, stat_generator,
      Ironmon::PlayerFusionMapper::SCHEMA_VERSION, proc { Fiber.yield })
    fiber = Fiber.new { mapper.prepare }
    resumes = 0
    while fiber.alive?
      fiber.resume
      resumes += 1
      assert(resumes < 100_000, "cooperative native pairing eventually finishes")
    end
    assert(mapper.instance_variable_get(:@fusion_pairs) == baseline.instance_variable_get(:@fusion_pairs) &&
      mapper.instance_variable_get(:@paired_result_ids) == baseline.instance_variable_get(:@paired_result_ids) &&
      mapper.pairing_bst_limit == baseline.pairing_bst_limit,
      "cooperative preparation preserves every native pairing and strength-rule decision")
    cold_stats = Ironmon::BaseStatGenerator.new(seed, Ironmon.base_stat_source_fingerprint)
    cold = Ironmon::PlayerFusionMapper.new(seed, pool, {}, {}, cold_stats,
      Ironmon::PlayerFusionMapper::SCHEMA_VERSION, proc { Fiber.yield })
    cold_fiber = Fiber.new { cold.prepare }
    started = now
    maximum = 0.0
    maximum_stage = nil
    stages = {}
    cold_resumes = 0
    while cold_fiber.alive?
      chunk_started = now
      cold_fiber.resume
      elapsed = now - chunk_started
      if elapsed > maximum
        maximum = elapsed
        maximum_stage = cold.preparation_stage.to_s
      end
      stages[cold.preparation_stage.to_s] = true
      cold_resumes += 1
      assert(now - started < 45, "the actual full-pool cold startup path completes every preparation phase")
    end
    install_started = now
    assert(cold.finish_cooperative_preparation.equal?(cold) && cold.pairing_prepared?,
      "the full cold mapper completes and installs its prepared result")
    install_ms = (now - install_started) * 1000
    assert(cold.instance_variable_get(:@paired_result_ids).length == pool.length,
      "the completed cold table covers every custom fusion exactly once")
    assert(maximum < 0.1, "no individual cold native pairing resume freezes gameplay (#{maximum}s, #{maximum_stage})")
    assert(stages["fusion_pool"] && stages["maximum_range"] && stages["strength_order"] && stages["strength_pairing"],
      "the actual full native pool audits all cold startup preparation phases")
    return { "seed" => seed, "identities" => pool.length, "pool_initialization_ms" => pool_ms,
      "exact_small_pool_resumes" => resumes, "cold_resumes" => cold_resumes,
      "cold_seconds" => now - started, "maximum_resume_ms" => maximum * 1000,
      "installation_ms" => install_ms,
      "maximum_resume_stage" => maximum_stage, "stages" => stages.keys,
      "background_budget_ms" => Ironmon::PLAYER_FUSION_PREPARATION_BUDGET_SECONDS * 1000,
      "background_interval_ms" => Ironmon::PLAYER_FUSION_PREPARATION_INTERVAL_SECONDS * 1000 }
  end

  def self.run
    test_wall_clock_background
    profile_id = ENV["IRONMON_PLAYER_FUSION_PROFILE"]
    result = profile_id ? Ironmon.with_generation_profile(profile_id) { test_native_mapper } : test_native_mapper
    result["generation_profile_id"] = profile_id if profile_id
    output = $ironmon_speed_up_acceptance_output.to_s
    File.binwrite(File.join(output, "player-fusion-pacing.json"), Ironmon.tracker_json_generate(result)) if !output.empty?
    puts "Player-fusion pacing: bounded background work, reset cancellation, foreground completion and native pairing passed"
    puts Ironmon.tracker_json_generate(result)
  end
end

IronmonPlayerFusionPacingTests.run
