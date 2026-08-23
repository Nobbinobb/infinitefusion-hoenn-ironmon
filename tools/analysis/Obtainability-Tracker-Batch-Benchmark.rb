module IronmonObtainabilityTrackerBatchBenchmark
  OUTPUT_PATH = $ironmon_obtainability_tracker_batch_output_path.to_s
  MAPPING_PATH = $ironmon_obtainability_tracker_batch_mapping_path.to_s
  MODE = $ironmon_obtainability_tracker_batch_mode.to_s
  SEED = $ironmon_obtainability_tracker_batch_seed.to_i
  BATCH_SIZE = 8_192
  GRAPH_EDGE_COUNT = 160

  def self.elapsed_milliseconds(started_at)
    return ((Time.now - started_at) * 1000.0).round(3)
  end

  def self.protocol_value(value)
    if value.is_a?(Array)
      return value.map { |entry| protocol_value(entry) }
    end
    if value.is_a?(Hash)
      result = {}
      value.each do |key, entry|
        result[key.to_s] = protocol_value(entry)
      end
      return result
    end
    return value
  end

  def self.recipe
    metadata = Ironmon.evolution_metadata_values
    return {
      "run_id" => "tracker-batch-benchmark-#{SEED}",
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

  def self.mapping_entries(service)
    mapper = service.instance_variable_get(:@fusion_mapper)
    materials = service.instance_variable_get(:@material_ids)
    result = []
    materials.each_with_index do |first, first_index|
      (first_index...materials.length).each do |second_index|
        second = materials[second_index]
        mapped = mapper.species_pair(first, second)
        result << {
          "first_material_id" => GameData::Species.get(first).id_number,
          "second_material_id" => GameData::Species.get(second).id_number,
          "first_result_id" => GameData::Species.get(mapped[0]).id_number,
          "second_result_id" => GameData::Species.get(mapped[1]).id_number
        }
      end
    end
    return result
  end

  def self.graph_edge_keys(service, entries)
    generator = service.instance_variable_get(:@fusion_generator)
    result = {}
    entries.each do |entry|
      [entry["first_result_id"], entry["second_result_id"]].each do |number|
        source = GameData::Species.get(number)
        generator.branches_for(source).each do |branch|
          target = GameData::Species.get(branch[:target_id])
          result["#{source.id}:0>#{target.id}:0"] = true
          return result.keys if result.length >= GRAPH_EDGE_COUNT
        end
      end
    end
    return result.keys
  end

  def self.prepare_mapping
    service = Ironmon::TrackerObtainabilityService.new(recipe, true)
    while !service.instance_variable_get(:@material_pairs_prepared)
      service.send(:advance_work_unit)
    end
    work = service.snapshot["fusion_mapping_work"]
    raise "the service did not expose tracker mapping work" if !work

    reference_started = Time.now
    entries = mapping_entries(service)
    reference_elapsed = elapsed_milliseconds(reference_started)
    raise "the mapping entry count is inconsistent" if
      entries.length != work["total_pairs"]
    edge_keys = graph_edge_keys(service, entries)
    raise "the mapping fixture did not produce enough graph edges" if
      edge_keys.length < GRAPH_EDGE_COUNT
    File.open(MAPPING_PATH, "wb") do |file|
      file.write(JSON.generate({
        "job_id" => work["job_id"],
        "total_pairs" => work["total_pairs"],
        "material_species" => work["material_ids"].length,
        "graph_edge_keys" => edge_keys,
        "fixture_mapping_preparation_milliseconds" => reference_elapsed
      }) + "\n")
      entries.each_slice(BATCH_SIZE) do |pairs|
        packed_pairs = pairs.flat_map do |entry|
          [
            entry["first_material_id"], entry["second_material_id"],
            entry["first_result_id"], entry["second_result_id"]
          ]
        end
        file.write(JSON.generate(packed_pairs) + "\n")
      end
    end
  end

  def self.apply_mapping
    total_started = Time.now
    service = Ironmon::TrackerObtainabilityService.new(recipe, true)
    setup_started = Time.now
    while !service.instance_variable_get(:@material_pairs_prepared)
      service.send(:advance_work_unit)
    end
    setup_elapsed = elapsed_milliseconds(setup_started)
    work = service.snapshot["fusion_mapping_work"]
    raise "the service did not expose tracker mapping work" if !work

    mapping_file = File.open(MAPPING_PATH, "rb")
    metadata = protocol_value(JSON.parse(mapping_file.gets))
    if metadata["total_pairs"].to_i != work["total_pairs"] ||
       metadata["material_species"].to_i != work["material_ids"].length
      raise "the prepared tracker mapping does not match the cold service " +
        "(prepared_materials=#{metadata["material_species"]}, " +
        "cold_materials=#{work["material_ids"].length}, " +
        "prepared_pairs=#{metadata["total_pairs"]}, " +
        "cold_pairs=#{work["total_pairs"]})"
    end
    edge_keys = metadata["graph_edge_keys"]
    graph_registration_started = Time.now
    progress = service.snapshot(nil, [], edge_keys)
    graph_registration_elapsed = elapsed_milliseconds(
      graph_registration_started
    )

    batch_elapsed = []
    batch_parse_elapsed = []
    background_slice_elapsed = []
    mapping_file.each_line.with_index do |line, batch_index|
      parse_started = Time.now
      packed_pairs = protocol_value(JSON.parse(line))
      batch_parse_elapsed << elapsed_milliseconds(parse_started)
      batch_started = Time.now
      service.apply_fusion_mapping_batch({
        "job_id" => work["job_id"],
        "offset" => progress["processed_pairs"],
        "packed_pairs" => packed_pairs
      }, batch_index > 0)
      while !service.instance_variable_get(
        :@precomputed_fusion_mapping_entries
      ).empty?
        slice_started = Time.now
        service.advance_for_milliseconds(batch_index == 0 ? 4.0 : 250.0)
        background_slice_elapsed << elapsed_milliseconds(slice_started) if
          batch_index == 0
      end
      progress = service.snapshot(nil, [], edge_keys)
      batch_elapsed << elapsed_milliseconds(batch_started)
    end
    mapping_file.close
    application_elapsed = batch_elapsed.inject(0.0, :+).round(3)
    parse_elapsed = batch_parse_elapsed.inject(0.0, :+).round(3)

    completion_started = Time.now
    completion_by_phase = Hash.new(0.0)
    completion_poll_elapsed = 0.0
    completion_polls = 0
    until service.complete?
      phase = service.instance_variable_get(:@phase).to_s
      unit_started = Time.now
      service.advance_for_milliseconds(250.0)
      completion_by_phase[phase] += elapsed_milliseconds(unit_started)
      poll_started = Time.now
      service.snapshot(nil, [], edge_keys)
      completion_poll_elapsed += elapsed_milliseconds(poll_started)
      completion_polls += 1
    end
    completion_elapsed = elapsed_milliseconds(completion_started)
    snapshot = service.snapshot
    report = {
      "schema_version" => 4,
      "cold_game_process" => true,
      "mapping_protocol_snapshots" => batch_elapsed.length + 1,
      "seed" => SEED,
      "material_species" => work["material_ids"].length,
      "unordered_pairs" => work["total_pairs"],
      "batch_size" => BATCH_SIZE,
      "batch_count" => batch_elapsed.length,
      "graph_edge_count" => edge_keys.length,
      "setup_milliseconds" => setup_elapsed,
      "graph_registration_milliseconds" => graph_registration_elapsed,
      "fixture_mapping_preparation_milliseconds" =>
        metadata["fixture_mapping_preparation_milliseconds"],
      "batch_payload_parse_milliseconds" => parse_elapsed,
      "batch_application_milliseconds" => application_elapsed,
      "average_batch_application_milliseconds" =>
        (application_elapsed / batch_elapsed.length).round(3),
      "maximum_batch_application_milliseconds" => batch_elapsed.max,
      "background_slice_average_milliseconds" =>
        (background_slice_elapsed.inject(0.0, :+) /
         background_slice_elapsed.length).round(3),
      "background_slice_maximum_milliseconds" =>
        background_slice_elapsed.max,
      "background_slices_measured" => background_slice_elapsed.length,
      "completion_milliseconds" => completion_elapsed,
      "completion_poll_milliseconds" => completion_poll_elapsed.round(3),
      "completion_polls" => completion_polls,
      "completion_phase_milliseconds" => completion_by_phase.transform_values do |value|
        value.round(3)
      end,
      "obtainable_species" => snapshot["obtainable_count"],
      "total_milliseconds_excluding_reference_mapping" =>
        (setup_elapsed + graph_registration_elapsed + parse_elapsed +
         application_elapsed + completion_elapsed).round(3),
      "wall_milliseconds" => elapsed_milliseconds(total_started)
    }
    temporary_path = "#{OUTPUT_PATH}.tmp"
    File.binwrite(temporary_path, JSON.generate(report) + "\n")
    File.delete(OUTPUT_PATH) if File.file?(OUTPUT_PATH)
    File.rename(temporary_path, OUTPUT_PATH)
  end

  def self.run
    return prepare_mapping if MODE == "prepare"
    return apply_mapping if MODE == "apply"
    raise "unknown tracker-batch benchmark mode: #{MODE}"
  end
end

IronmonObtainabilityTrackerBatchBenchmark.run
