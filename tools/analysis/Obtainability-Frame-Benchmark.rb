module IronmonObtainabilityFrameBenchmark
  module Checkpoints
    def cooperative_checkpoint
      now = Ironmon.tracker_uptime_seconds
      gap = (now - @benchmark_checkpoint_at) * 1000.0
      if gap > $ironmon_frame_benchmark_budget + 1.0
        @benchmark_gaps << {
          "phase" => @phase.to_s, "milliseconds" => gap.round(3),
          "stack" => caller(1, 6)
        }
      end
      super
      @benchmark_checkpoint_at = Ironmon.tracker_uptime_seconds
    end
  end

  def self.recipe
    metadata = Ironmon.evolution_metadata_values
    result = {
      "run_id" => "frame-benchmark", "seed" => $ironmon_frame_benchmark_seed,
      "generation_profile_id" => Ironmon.current_generation_profile_id,
      "configuration" => Ironmon::Configuration.new.to_h,
      "data_mode" => "classic",
      "species_generator_version" => Ironmon::SpeciesGenerator::SCHEMA_VERSION,
      "base_stat_source_fingerprint" => Ironmon.base_stat_source_fingerprint,
      "player_fusion_generator_version" => Ironmon::PlayerFusionMapper::SCHEMA_VERSION,
      "item_generator" => { "rules_version" => Ironmon::ItemSlotGenerator::POOL_RULES_VERSION },
      "item_mappings" => {}, "tm_mappings" => {}
    }
    metadata.each do |key, value|
      name = key.to_s.sub(/\Aironmon_/, "")
      name = name.sub(/\Aevolution_fusion_/, "fusion_evolution_")
      result[name] = value
    end
    return result
  end

  def self.run
    timings = {}
    operation_timings = {}
    operation_errors = []
    [
      [Ironmon::SpeciesGenerator, [:map]],
      [GameData::FusedSpecies, [:initialize]],
      [Ironmon::FusionPredecessorIndex, [:initialize]],
      [Ironmon.singleton_class, [:tracker_lookup_species_available?, :custom_fusion_pool]]
    ].each do |owner, names|
      hooks = Module.new
      names.each do |name|
        label = "#{owner}##{name}"
        hooks.send(:define_method, name) do |*args|
          started = Ironmon.tracker_uptime_seconds
          result = super(*args)
          elapsed = (Ironmon.tracker_uptime_seconds - started) * 1000.0
          record = operation_timings[label] ||= { "calls" => 0, "total" => 0.0, "max" => 0.0 }
          record["calls"] += 1
          record["total"] += elapsed
          record["max"] = [record["max"], elapsed].max
          result
        rescue Exception => error
          diagnostic = { "operation" => label, "error" => error.message }
          if is_a?(Ironmon::FusionPredecessorIndex) &&
             Ironmon.custom_fusion_pool_service.respond_to?(:packed_pool_membership)
            packed = args[0]["packed_pool_membership"]
            expected = Ironmon.custom_fusion_pool_service.packed_pool_membership
            diagnostic["cached_encoding"] = packed.encoding.to_s
            diagnostic["pool_encoding"] = expected.encoding.to_s
            diagnostic["identical_bytes"] = packed.b == expected.b
          end
          operation_errors << diagnostic
          raise
        end
      end
      owner.prepend(hooks)
    end
    timed_methods = Module.new
    [:prepare_generators, :prepare_encounter_tables, :process_encounter_table,
     :advance_derived_encounter_source_work, :build_resource_supply,
     :prepare_material_pairs, :species_generator, :fusion_closure_job_id,
     :tracker_custom_fusion_pool_membership].each do |name|
      timed_methods.send(:define_method, name) do |*args|
        started = Ironmon.tracker_uptime_seconds
        result = super(*args)
        elapsed = (Ironmon.tracker_uptime_seconds - started) * 1000.0
        record = timings[name.to_s] ||= { "calls" => 0, "total" => 0.0, "max" => 0.0 }
        record["calls"] += 1
        record["total"] += elapsed
        record["max"] = [record["max"], elapsed].max
        result
      end
    end
    configured_recipe = recipe
    # A real run has constructed these pools before offering its starters.
    Ironmon.normal_species_pool
    Ironmon.custom_fusion_pool
    pbGetMapNameFromId(1)
    GameData::Species.get(1).name
    service = Ironmon::TrackerObtainabilityService.new(configured_recipe, true)
    service.configure_material_pair_preparation(true)
    service.extend(Checkpoints)
    service.extend(timed_methods)
    gaps = []
    service.instance_variable_set(:@benchmark_gaps, gaps)
    slices = []
    GC::Profiler.enable
    deadline = Ironmon.tracker_uptime_seconds + 120.0
    until service.instance_variable_get(:@phase) == :player_fusions
      phase = service.instance_variable_get(:@phase).to_s
      started = Ironmon.tracker_uptime_seconds
      gc_started = GC::Profiler.total_time
      service.instance_variable_set(:@benchmark_checkpoint_at, started)
      service.advance_for_milliseconds($ironmon_frame_benchmark_budget)
      elapsed = (Ironmon.tracker_uptime_seconds - started) * 1000.0
      slices << {
        "phase" => phase, "milliseconds" => elapsed.round(3),
        "gc_milliseconds" => ((GC::Profiler.total_time - gc_started) * 1000.0).round(3)
      }
      raise "Frame benchmark timed out" if Ironmon.tracker_uptime_seconds > deadline
    end
    started = Ironmon.tracker_uptime_seconds
    snapshot = service.snapshot
    snapshot_ms = (Ironmon.tracker_uptime_seconds - started) * 1000.0
    proof_fingerprint = Digest::SHA256.hexdigest(JSON.generate([
      snapshot["fusion_closure_work"], service.send(:tracker_base_proof_snapshot),
      service.send(:tracker_resource_supply_snapshot),
      service.instance_variable_get(:@possible_evolution_edge_numbers).keys.sort
    ]))
    durations = slices.map { |slice| slice["milliseconds"] }.sort
    report = {
      "seed" => $ironmon_frame_benchmark_seed,
      "generation_profile_id" => configured_recipe["generation_profile_id"],
      "proof_fingerprint" => proof_fingerprint,
      "scope" => "Game-owned preparation to the first native job; warmed pools and text; no tracker transport, native worker, or rendered-frame timing",
      "budget_milliseconds" => $ironmon_frame_benchmark_budget,
      "slice_count" => slices.length,
      "total_work_milliseconds" => durations.inject(0.0, :+).round(3),
      "max_slice_milliseconds" => durations.last,
      "p95_slice_milliseconds" => durations[(durations.length * 0.95).floor],
      "slices_over_16_67_milliseconds" => durations.count { |value| value > 16.67 },
      "snapshot_milliseconds" => snapshot_ms.round(3),
      "method_milliseconds" => timings,
      "operation_milliseconds" => operation_timings,
      "operation_errors" => operation_errors,
      "slowest_slices" => slices.sort_by { |slice| -slice["milliseconds"] }.first(15),
      "checkpoint_gaps" => gaps.sort_by { |gap| -gap["milliseconds"] }.first(20),
      "result" => snapshot.reject { |key, _value| key == "fusion_closure_work" }
    }
    report["native_result_probe"] = native_result_probe(configured_recipe)
    File.binwrite($ironmon_frame_benchmark_output, JSON.generate(report))
  end

  def self.native_result_probe(configured_recipe)
    service = Ironmon::TrackerObtainabilityService.new(configured_recipe, true)
    service.instance_variable_set(:@phase, :player_fusions)
    service.instance_variable_set(:@material_pairs_prepared, true)
    service.instance_variable_set(:@fusion_closure_job_id, "decoder-probe")
    maximum = (NB_POKEMON * NB_POKEMON) + NB_POKEMON
    result = {
      "job_id" => "decoder-probe", "direct_encounter_fusion_ids" => [],
      "obtainable_fusion_words" => Array.new((maximum >> 5) + 1, 0),
      "packed_executable_evolution_edges" => ["\x01" * 100_000].pack("m0"),
      "obtainable_count" => 0
    }
    deferred = service.respond_to?(:queue_fusion_closure_result)
    started = Ironmon.tracker_uptime_seconds
    if deferred
      service.queue_fusion_closure_result(result)
    else
      service.apply_fusion_closure_result(result)
    end
    request_ms = (Ironmon.tracker_uptime_seconds - started) * 1000.0
    slices = []
    until service.complete?
      started = Ironmon.tracker_uptime_seconds
      service.advance_for_milliseconds($ironmon_frame_benchmark_budget)
      slices << (Ironmon.tracker_uptime_seconds - started) * 1000.0
    end
    edges = service.instance_variable_get(:@tracker_executable_evolution_edges)
    raise "Decoder probe changed the edge index" if edges != (1..100_000).to_a
    return {
      "edge_count" => edges.length, "deferred" => deferred,
      "request_milliseconds" => request_ms.round(3),
      "slice_count" => slices.length,
      "max_slice_milliseconds" => slices.empty? ? 0 : slices.max.round(3)
    }
  end
end

Ironmon.with_generation_profile(Ironmon.current_generation_profile_id) do
  IronmonObtainabilityFrameBenchmark.run
end
