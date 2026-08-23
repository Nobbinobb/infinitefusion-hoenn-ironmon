module IronmonObtainabilityPrototypeBenchmark
  OUTPUT_PATH = $ironmon_obtainability_prototype_benchmark_output_path.to_s
  PROGRESS_PATH = "#{OUTPUT_PATH}.progress"
  SEED = $ironmon_obtainability_prototype_benchmark_seed.to_i
  SKIP_BACKGROUND =
    $ironmon_obtainability_prototype_benchmark_skip_background == true

  def self.elapsed_milliseconds(started_at)
    return ((Time.now - started_at) * 1000.0).round(3)
  end

  def self.recipe
    metadata = Ironmon.evolution_metadata_values
    return {
      "run_id" => "prototype-benchmark-#{SEED}",
      "seed" => SEED,
      "configuration" => Ironmon::Configuration.new.to_h,
      "data_mode" => "classic",
      "species_generator_version" => Ironmon::SpeciesGenerator::SCHEMA_VERSION,
      "base_stat_source_fingerprint" => Ironmon.base_stat_source_fingerprint,
      "player_fusion_generator_version" =>
        Ironmon::PlayerFusionMapper::SCHEMA_VERSION,
      "evolution_generator_version" =>
        metadata[:ironmon_evolution_generator_version],
      "evolution_rules_version" =>
        metadata[:ironmon_evolution_rules_version],
      "evolution_source_fingerprint" =>
        metadata[:ironmon_evolution_source_fingerprint],
      "evolution_taxonomy_fingerprint" =>
        metadata[:ironmon_evolution_taxonomy_fingerprint],
      "evolution_method_fingerprint" =>
        metadata[:ironmon_evolution_method_fingerprint],
      "evolution_target_fingerprint" =>
        metadata[:ironmon_evolution_target_fingerprint],
      "evolution_base_stat_generator_version" =>
        metadata[:ironmon_evolution_base_stat_generator_version],
      "evolution_base_stat_source_fingerprint" =>
        metadata[:ironmon_evolution_base_stat_source_fingerprint],
      "fusion_evolution_generator_version" =>
        metadata[:ironmon_evolution_fusion_generator_version],
      "fusion_evolution_rules_version" =>
        metadata[:ironmon_evolution_fusion_rules_version],
      "fusion_evolution_target_pool_version" =>
        metadata[:ironmon_evolution_fusion_target_pool_version],
      "fusion_evolution_target_pool_size" =>
        metadata[:ironmon_evolution_fusion_target_pool_size],
      "fusion_evolution_target_pool_fingerprint" =>
        metadata[:ironmon_evolution_fusion_target_pool_fingerprint],
      "item_generator" => {
        "rules_version" => Ironmon::ItemSlotGenerator::POOL_RULES_VERSION
      },
      "item_mappings" => {},
      "tm_mappings" => {}
    }
  end

  def self.write_progress(snapshot, request_count)
    File.binwrite(
      PROGRESS_PATH,
      "phase=#{snapshot["phase"]} requests=#{request_count} " +
      "pairs=#{snapshot["processed_pairs"]}/#{snapshot["total_pairs"]}\n"
    )
  end

  def self.run
    total_started = Time.now
    recipe_started = Time.now
    benchmark_recipe = recipe
    recipe_elapsed = elapsed_milliseconds(recipe_started)
    initialization_started = Time.now
    service = Ironmon::TrackerObtainabilityService.new(benchmark_recipe, true)
    service.disable_tracker_mapping_worker
    initialization_elapsed = elapsed_milliseconds(initialization_started)
    initial = service.snapshot
    request_count = 0
    first_batch_elapsed = 0.0
    request_elapsed = []
    request_details = []
    calculation_started = Time.now
    snapshot = initial
    while !snapshot["complete"]
      batch_started = Time.now
      phase_before = snapshot["phase"]
      pairs_before = snapshot["processed_pairs"]
      service.advance_for_milliseconds(
        Ironmon::TrackerObtainabilityService::FOREGROUND_MILLISECONDS
      )
      snapshot = service.snapshot
      batch_elapsed = elapsed_milliseconds(batch_started)
      first_batch_elapsed = batch_elapsed if request_count == 0
      request_elapsed << batch_elapsed
      request_details << {
        "milliseconds" => batch_elapsed,
        "phase_before" => phase_before,
        "phase_after" => snapshot["phase"],
        "processed_pair_delta" => snapshot["processed_pairs"] - pairs_before
      }
      request_count += 1
      write_progress(snapshot, request_count) if request_count % 10 == 0 ||
        snapshot["complete"]
    end
    calculation_elapsed = elapsed_milliseconds(calculation_started)
    total_elapsed = elapsed_milliseconds(total_started)
    background_slice_elapsed = []
    background_slice_details = []
    background_elapsed = nil
    background_service = nil
    if !SKIP_BACKGROUND
      Ironmon.reset_tracker_post_run_cache
      background_service = Ironmon::TrackerObtainabilityService.new(
        benchmark_recipe, true
      )
      background_service.disable_tracker_mapping_worker
      background_started = Time.now
      while !background_service.complete? &&
            background_service.background_advance_allowed?
        slice_started = Time.now
        phase_before = background_service.instance_variable_get(:@phase).to_s
        background_service.advance_for_milliseconds(
          Ironmon::TrackerObtainabilityService::BACKGROUND_MILLISECONDS
        )
        slice_elapsed = elapsed_milliseconds(slice_started)
        background_slice_elapsed << slice_elapsed
        background_slice_details << [
          slice_elapsed, phase_before,
          background_service.instance_variable_get(:@phase).to_s
        ]
      end
      background_elapsed = elapsed_milliseconds(background_started)
    end
    maximum_background_slice = background_slice_details.max_by do |entry|
      entry[0]
    end
    pair_count = snapshot["total_pairs"]
    sorted_request_elapsed = request_elapsed.sort
    maximum_request_detail = request_details.max_by do |entry|
      entry["milliseconds"]
    end
    percentile_index = ((sorted_request_elapsed.length - 1) * 0.95).round
    phase_requests = {}
    request_details.each do |detail|
      phase = detail["phase_before"]
      phase_requests[phase] ||= {
        "requests" => 0,
        "milliseconds" => 0.0,
        "processed_pairs" => 0
      }
      phase_requests[phase]["requests"] += 1
      phase_requests[phase]["milliseconds"] += detail["milliseconds"]
      phase_requests[phase]["processed_pairs"] +=
        detail["processed_pair_delta"]
    end
    phase_requests.each_value do |entry|
      entry["milliseconds"] = entry["milliseconds"].round(3)
    end
    report = {
      "schema_version" => 1,
      "seed" => SEED,
      "scenario" => "classic|mixed|random_component",
      "material_species" => service.instance_variable_get(:@material_ids).length,
      "unordered_pairs" => pair_count,
      "requests" => request_count,
      "foreground_phases" => phase_requests,
      "initial_obtainable_species" => initial["obtainable_count"],
      "complete_obtainable_species" => snapshot["obtainable_count"],
      "recipe_preparation_milliseconds" => recipe_elapsed,
      "initialization_milliseconds" => initialization_elapsed,
      "first_batch_milliseconds" => first_batch_elapsed,
      "maximum_request_milliseconds" => request_elapsed.max,
      "maximum_request_phase_before" =>
        maximum_request_detail["phase_before"],
      "maximum_request_phase_after" => maximum_request_detail["phase_after"],
      "maximum_request_processed_pair_delta" =>
        maximum_request_detail["processed_pair_delta"],
      "p95_request_milliseconds" => sorted_request_elapsed[percentile_index],
      "average_request_milliseconds" =>
        (request_elapsed.inject(0.0, :+) / request_elapsed.length).round(3),
      "background_slice_budget_milliseconds" =>
        Ironmon::TrackerObtainabilityService::BACKGROUND_MILLISECONDS,
      "background_slice_average_milliseconds" =>
        background_slice_elapsed.empty? ? nil :
          (background_slice_elapsed.inject(0.0, :+) /
           background_slice_elapsed.length).round(3),
      "background_slice_maximum_milliseconds" =>
        background_slice_elapsed.max,
      "background_slice_maximum_phase_before" =>
        maximum_background_slice ? maximum_background_slice[1] : nil,
      "background_slice_maximum_phase_after" =>
        maximum_background_slice ? maximum_background_slice[2] : nil,
      "background_slices" => background_slice_elapsed.length,
      "background_precalculation_milliseconds" => background_elapsed,
      "background_precalculation_processed_pairs" =>
        background_service ?
          background_service.instance_variable_get(:@pair_index) : nil,
      "background_precalculation_phase" =>
        background_service ?
          background_service.instance_variable_get(:@phase).to_s : nil,
      "calculation_milliseconds" => calculation_elapsed,
      "total_milliseconds" => total_elapsed,
      "average_calculation_milliseconds_per_pair" =>
        (calculation_elapsed / pair_count).round(6),
      "unresolved_source_count" => snapshot["unresolved_source_count"],
      "unresolved_resource_count" => snapshot["unresolved_resource_count"]
    }
    temporary_path = "#{OUTPUT_PATH}.tmp"
    File.binwrite(temporary_path, JSON.generate(report) + "\n")
    File.delete(OUTPUT_PATH) if File.file?(OUTPUT_PATH)
    File.rename(temporary_path, OUTPUT_PATH)
  ensure
    File.delete(PROGRESS_PATH) if File.file?(PROGRESS_PATH)
  end
end

IronmonObtainabilityPrototypeBenchmark.run
