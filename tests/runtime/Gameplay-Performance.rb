module IronmonGameplayPerformanceTests
  SUMMARY_FIELDS = [:@summary_started, :@summary_gc_count, :@summary_gc_time,
    :@summary_frames, :@summary_slow_hooks, :@summary_hook_seconds,
    :@summary_max_hook, :@summary_max_gap, :@last_frame_stamp].freeze

  def self.assert(condition, message)
    raise "Gameplay performance validation failed: #{message}" if !condition
  end

  def self.rows
    service = Ironmon::SpeedDiagnostics
    assert(service.flush, "owned diagnostic writes finish before inspection")
    return [] if !File.file?(service.path)
    return File.readlines(service.path).map { |line| Ironmon::GameplayCollection.decode(line) }
  end

  def self.test_fast_path
    service = Ironmon::SpeedDiagnostics
    before_worker = service.instance_variable_get(:@worker)
    before = GC.stat(:total_allocated_objects)
    10_000.times { service.record_work(:graphics_hook, 0.001) }
    allocated = GC.stat(:total_allocated_objects) - before
    assert(allocated < 50, "ordinary work avoids per-frame diagnostic allocations")
    assert(service.instance_variable_get(:@worker).equal?(before_worker), "ordinary work starts no writer")
    assert(!File.file?(service.path), "ordinary work creates no log file")
  end

  def self.test_work_budget
    service = Ironmon::SpeedDiagnostics
    @time = 100.0
    details = { "operation" => "obtainability", "phase" => :normal_graph,
      "budget_ms" => 2.0, "processed" => 3, "pokemon" => "PRIVATE" }
    assert(service.record_work(:calculation_chunk, 0.004, details), "a small calculation budget can expose a four millisecond overrun")
    10.times { assert(!service.record_work(:calculation_chunk, 0.030, details), "repeated budget overruns are rate limited") }
    @time += 1.01
    assert(service.record_work(:calculation_chunk, 0.020, details), "later budget overruns resume after the reporting interval")
    row = rows.last
    assert(row["kind"] == "budget" && row["suppressed_events"] == 10 && row["maximum_duration_ms"] == 30.0,
      "budget logs summarize the suppressed work and its maximum duration")
    assert(row["phase"] == "normal_graph" && row["processed"] == 3 && !row.key?("pokemon"),
      "calculation logs retain phase/count data without Pokemon details")
    assert(service.record_work(:calculation_chunk, 0.150, details), "a genuine stall bypasses budget rate limiting")
    assert(rows.last["kind"] == "stall", "long work remains distinguishable from budget overruns")
  end

  def self.test_context_and_source
    service = Ironmon::SpeedDiagnostics
    @time = 200.0
    assert(service.record_context(:speed_selection, { "previous_multiplier" => 1, "multiplier" => 2, "stage_index" => 1 }),
      "speed changes produce compact context records")
    5.times { assert(!service.record_context(:speed_selection, { "multiplier" => 3 }), "rapid contexts are coalesced") }
    @time += 0.26
    assert(service.record_context(:speed_selection, { "multiplier" => 10 }), "the current speed is recorded after the context interval")
    row = rows.last
    assert(row["kind"] == "context" && row["multiplier"] == 10 && row["suppressed_events"] == 5,
      "context records distinguish rapid transitions from frame stalls")
    source = eval("Ironmon::SpeedDiagnostics.caller_source", binding, "023_Speed_Up.rb", 162)
    assert(source && source.start_with?("Gameplay-Performance.rb:"), "installed wrapper names do not hide the calling script")
    assert(!source.include?("/") && !source.include?("\\"), "source attribution excludes absolute paths")
  end

  def self.test_player_fusion_phase_budget
    service = Ironmon::SpeedDiagnostics
    @time = 250.0
    details = { "operation" => "player_fusion_background", "phase" => :strength_order,
      "processed" => 32, "budget_ms" => 1.0, "pokemon" => "PRIVATE" }
    assert(!service.record_work(:calculation_chunk, 0.0008, details), "player-fusion work inside its one millisecond budget stays quiet")
    assert(service.record_work(:calculation_chunk, 0.0012, details), "a one millisecond background overrun is visible below the normal sixteen millisecond threshold")
    20.times { assert(!service.record_work(:calculation_chunk, 0.0015, details), "repeated player-fusion overruns are coalesced") }
    assert(service.record_work(:calculation_chunk, 0.0013, details.merge("phase" => :strength_pairing, "processed" => 64)),
      "a new player-fusion stage retains an independent reporting budget")
    row = rows.last
    assert(row["phase"] == "strength_pairing" && row["processed"] == 64 && row["budget_ms"] == 1.0,
      "startup work records its exact mapper stage, progress and background budget")
    @time += 1.01
    assert(service.record_work(:calculation_chunk, 0.0014, details), "player-fusion stage reporting resumes after one second")
    row = rows.last
    assert(row["suppressed_events"] == 20 && row["maximum_duration_ms"] == 1.5,
      "repeated sub-frame player-fusion work reports bounded counts and its slowest sample")
    service::PLAYER_FUSION_PHASES.each_with_index do |phase, index|
      @time += 1.01
      assert(service.record_work(:calculation_prepare, 0.020,
        { "operation" => "player_fusion_schedule", "phase" => phase, "processed" => index }),
        "every fixed player-fusion stage is accepted")
      assert(rows.last["phase"] == phase, "player-fusion stage metadata survives bounded serialization")
    end
    @time += 1.01
    assert(service.record_work(:calculation_prepare, 0.020,
      { "operation" => "player_fusion_schedule", "phase" => "private_species_name", "pokemon" => "PRIVATE" }),
      "unknown metadata does not prevent optional diagnostics")
    row = rows.last
    assert(!row.key?("phase") && !row.key?("pokemon"), "unrecognized stages and Pokemon payloads are excluded")
  end

  def self.test_frame_summary
    service = Ironmon::SpeedDiagnostics
    SUMMARY_FIELDS.each { |field| service.instance_variable_set(field, nil) }
    @time = 300.0
    service.note_frame
    service.note_hook(0.030)
    @time += 2.5
    service.note_frame
    service.note_hook(0.002)
    @time += 2.6
    service.note_frame
    row = rows.last
    assert(row["kind"] == "summary" && row["logical_updates"] == 3,
      "healthy play has a sparse five-second frame baseline")
    assert(row["slow_hooks"] == 1 && row["hook_work_ms"].between?(31.9, 32.1) && row["maximum_hook_ms"] == 30.0,
      "frame summaries retain callback load and its largest sample")
    assert(row["maximum_gap_ms"].between?(2599, 2601) && row["gc_count_delta"] >= 0 && row["gc_time_ms"] >= 0,
      "frame summaries include gaps and garbage collection deltas")
  end

  def self.test_real_hook_and_measurement
    service = Ironmon::SpeedDiagnostics
    @time = 400.0
    Ironmon.instance_variable_set(:@graphics_update_hooks, [
      { :name => :performance_budget_hook, :callback => proc { sleep(0.025); :preserved } },
      { :name => :performance_second_hook, :callback => proc { sleep(0.025); :preserved } }
    ])
    assert(Ironmon.run_graphics_update_hooks == :preserved, "hook instrumentation preserves callback results")
    hooks = rows.select { |row| row["component"] == "graphics_hook" }
    assert(hooks.map { |row| row["hook"] }.last(2) == ["performance_budget_hook", "performance_second_hook"],
      "different slow callbacks retain independent rate limits within the same frame")
    row = hooks.last
    assert(row["kind"] == "budget", "an actual twenty-five millisecond callback is identified below the old stall threshold")
    assert(row["gc_count_delta"] >= 0 && row["gc_time_ms"] >= 0, "measured hooks expose garbage collection separately")
    result = service.measure_work(:calculation_prepare, { "operation" => "prepare_material_pairs" }) do
      @time += 0.025
      :calculation_result
    end
    assert(result == :calculation_result, "calculation instrumentation preserves return values")
    begin
      service.measure_work(:calculation_prepare, { "operation" => "prepare_generators" }) do
        @time += 0.025
        raise "expected performance test interruption"
      end
      raise "The calculation interruption was swallowed"
    rescue RuntimeError => error
      assert(error.message == "expected performance test interruption", "instrumented work preserves its original exception")
    end
  end

  def self.run
    service = Ironmon::SpeedDiagnostics
    original_directory = service.method(:directory)
    original_now = service.method(:now)
    original_hooks = Ironmon.instance_variable_get(:@graphics_update_hooks)
    original_rate_limits = service.instance_variable_get(:@rate_limits)
    original_summary = SUMMARY_FIELDS.map { |field| [field, service.instance_variable_get(field)] }
    assert(service.flush, "earlier diagnostic work finishes before the owned fixture")
    output = File.join($ironmon_speed_up_acceptance_output, "gameplay-performance")
    service.define_singleton_method(:directory) { output }
    service.define_singleton_method(:now) { IronmonGameplayPerformanceTests.time }
    service.instance_variable_set(:@rate_limits, {})
    test_fast_path
    test_work_budget
    test_context_and_source
    test_player_fusion_phase_budget
    test_frame_summary
    test_real_hook_and_measurement
    assert(rows.all? { |row| !row.values.include?("PRIVATE") }, "performance records omit private payloads")
    puts "Gameplay performance: work budgets, rate limiting, context changes, frame/GC summaries, source attribution, callback timing, and preserved outcomes passed"
  ensure
    service.close if service
    if service
      service.define_singleton_method(:directory, original_directory) if original_directory
      service.define_singleton_method(:now, original_now) if original_now
      service.instance_variable_set(:@rate_limits, original_rate_limits) if original_rate_limits
      original_summary.each { |field, value| service.instance_variable_set(field, value) } if original_summary
      service.instance_variable_set(:@worker, nil)
      service.instance_variable_set(:@closed, false)
    end
    Ironmon.instance_variable_set(:@graphics_update_hooks, original_hooks) if original_hooks
  end

  def self.time
    return @time || 100.0
  end
end

IronmonGameplayPerformanceTests.run
