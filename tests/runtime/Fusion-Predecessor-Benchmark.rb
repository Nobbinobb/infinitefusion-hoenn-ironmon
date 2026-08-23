module IronmonFusionPredecessorBenchmark
  OUTPUT_PATH = $ironmon_fusion_predecessor_benchmark_output_path.to_s
  SEED = 1689
  SOURCE_IDENTITIES = [
    :B1H4,
    :B7H10,
    :B25H133,
    :B63H92,
    :B147H246,
    :B280H374,
    :B506H10,
    :B559H568
  ].freeze
  TARGET_IDENTITIES = [
    :B510H15,
    :B183H501,
    :B300H101,
    :B370H286,
    :B393H370,
    :B106H24,
    :B14H289,
    :B12H222,
    :B483H148,
    :B192H289,
    :B407H256,
    :B14H242
  ].freeze
  PAGE_LIMITS = [8, 12].freeze
  FORWARD_ITERATIONS = 3

  def self.assert(condition, message)
    raise "Fusion predecessor benchmark failed: #{message}" if !condition
  end

  def self.generator(catalog, pool, pool_info)
    return Ironmon::FusionEvolutionGenerator.new(
      SEED, catalog, pool, pool_info,
      Ironmon::BaseStatGenerator.new(
        SEED, Ironmon.base_stat_source_fingerprint
      )
    )
  end

  def self.percentile(values, percentile)
    sorted = values.sort
    index = ((sorted.length * percentile) / 100.0).ceil - 1
    index = 0 if index < 0
    return sorted[index]
  end

  def self.elapsed_milliseconds(started_at)
    return ((Time.now - started_at) * 1000).round(3)
  end

  def self.benchmark_reset(catalog)
    values = []
    FORWARD_ITERATIONS.times do
      started_at = Time.now
      normal = Ironmon::NormalEvolutionGenerator.new(
        SEED, catalog, Ironmon::BaseStatGenerator::SCHEMA_VERSION,
        Ironmon.base_stat_source_fingerprint
      )
      normal.graph
      values << elapsed_milliseconds(started_at)
    end
    return values
  end

  def self.benchmark_normal_lookup(catalog)
    normal = Ironmon::NormalEvolutionGenerator.new(
      SEED, catalog, Ironmon::BaseStatGenerator::SCHEMA_VERSION,
      Ironmon.base_stat_source_fingerprint
    )
    source = catalog.branch_catalog[0][:source]
    normal.graph
    started_at = Time.now
    1_000.times { normal.branches_for(source) }
    return elapsed_milliseconds(started_at) / 1_000.0
  end

  def self.benchmark_forward_source(catalog, pool, pool_info, source)
    cold_values = []
    cached_values = []
    branches = nil
    FORWARD_ITERATIONS.times do
      resolver = generator(catalog, pool, pool_info)
      started_at = Time.now
      generated = resolver.branches_for(source)
      cold_values << elapsed_milliseconds(started_at)
      branches ||= generated
      started_at = Time.now
      1_000.times { resolver.branches_for(source) }
      cached_values << elapsed_milliseconds(started_at) / 1_000.0
    end
    return {
      :source => source,
      :branch_count => branches.length,
      :fallback_count => branches.count { |branch| branch[:fallback] },
      :upward_count => branches.count { |branch| branch[:upward_expansion] },
      :cold_values => cold_values,
      :cached_values => cached_values
    }
  end

  def self.timing_summary_line(name, values)
    return [
      "timing",
      "name=#{name}",
      "count=#{values.length}",
      "min_ms=#{values.min.round(3)}",
      "median_ms=#{percentile(values, 50).round(3)}",
      "max_ms=#{values.max.round(3)}",
      "average_ms=#{(values.inject(0.0) { |sum, value| sum + value } / values.length).round(3)}"
    ].join(" ")
  end

  def self.forward_line(record)
    return [
      "forward",
      "source=#{record[:source]}",
      "branches=#{record[:branch_count]}",
      "fallback=#{record[:fallback_count]}",
      "upward=#{record[:upward_count]}",
      "cold_median_ms=#{percentile(record[:cold_values], 50).round(3)}",
      "cold_max_ms=#{record[:cold_values].max.round(3)}",
      "cached_median_ms=#{percentile(record[:cached_values], 50).round(6)}"
    ].join(" ")
  end

  def self.benchmark_first_page(resolver, sample, mode, page_limit)
    started_at = Time.now
    page = resolver.predecessor_page_for(sample[:target_id], 0, page_limit)
    elapsed = ((Time.now - started_at) * 1000).round
    assert(page[:branches].length <= page_limit,
           "#{mode} #{sample[:target_id]} exceeded its page limit")
    assert(page[:branches].all? do |branch|
             branch[:target_id] == sample[:target_id]
           end,
           "#{mode} #{sample[:target_id]} returned an inexact predecessor")
    diagnostics = page[:diagnostics]
    case page[:continuation]
    when :available
      assert(diagnostics[:predecessor_count] > page_limit,
             "#{mode} #{sample[:target_id]} lacks its available result")
    when :unknown
      assert(page[:branches].length == page_limit,
             "#{mode} #{sample[:target_id]} stopped before filling its page")
      assert(!diagnostics[:complete],
             "#{mode} #{sample[:target_id]} left completion unresolved after finishing")
    when :complete
      assert(diagnostics[:complete],
             "#{mode} #{sample[:target_id]} stopped without proving completion")
    else
      assert(false,
             "#{mode} #{sample[:target_id]} returned an invalid continuation")
    end
    return diagnostics.merge(
      :mode => mode,
      :page_limit => page_limit,
      :returned_count => page[:branches].length,
      :continuation => page[:continuation],
      :elapsed_milliseconds => elapsed
    )
  end

  def self.samples(_catalog, _pool, _pool_info)
    return TARGET_IDENTITIES.map do |identity|
      species = GameData::Species.get(identity)
      { :target_id => species.id }
    end
  end

  def self.summary(mode, records)
    elapsed = records.map { |record| record[:elapsed_milliseconds] }
    result = {
      :mode => mode,
      :count => records.length,
      :minimum => elapsed.min,
      :median => percentile(elapsed, 50),
      :p95 => percentile(elapsed, 95),
      :maximum => elapsed.max,
      :average => (elapsed.inject(0) { |sum, value| sum + value } /
        elapsed.length.to_f).round
    }
    phase_names = [
      :structural_lookup_milliseconds,
      :bst_filter_milliseconds,
      :seed_index_build_milliseconds,
      :seeded_ranking_milliseconds,
      :assignment_verification_milliseconds,
      :verification_candidate_gathering_milliseconds,
      :verification_candidate_ranking_milliseconds,
      :verification_assignment_solver_milliseconds,
      :verification_other_milliseconds
    ]
    phase_names.each do |phase|
      result[phase] = percentile(
        records.map { |record| record[phase] }, 50
      )
    end
    return result
  end

  def self.record_line(record)
    return [
      "target=#{record[:target]}",
      "mode=#{record[:mode]}",
      "bucket=#{record[:target_bucket]}",
      "types=#{record[:target_types].join('+')}",
      "bst=#{record[:target_bst]}",
      "branches=#{record[:compatible_component_branch_count]}",
      "eligible=#{record[:eligible_source_count]}",
      "verified=#{record[:verified_source_count]}",
      "predecessors=#{record[:predecessor_count]}",
      "page_limit=#{record[:page_limit] || 0}",
      "returned=#{record[:returned_count] || record[:predecessor_count]}",
      "continuation=#{record[:continuation]}",
      "scanned=#{record[:scanned_source_count]}",
      "structural_ms=#{record[:structural_lookup_milliseconds]}",
      "bst_ms=#{record[:bst_filter_milliseconds]}",
      "index_ms=#{record[:seed_index_build_milliseconds]}",
      "ranking_ms=#{record[:seeded_ranking_milliseconds]}",
      "assignment_ms=#{record[:assignment_verification_milliseconds]}",
      "gathering_ms=#{record[:verification_candidate_gathering_milliseconds]}",
      "candidate_ranking_ms=#{record[:verification_candidate_ranking_milliseconds]}",
      "solver_ms=#{record[:verification_assignment_solver_milliseconds]}",
      "other_verification_ms=#{record[:verification_other_milliseconds]}",
      "elapsed_ms=#{record[:elapsed_milliseconds]}"
    ].join(" ")
  end

  def self.summary_line(summary)
    return [
      "summary",
      "mode=#{summary[:mode]}",
      "count=#{summary[:count]}",
      "min_ms=#{summary[:minimum]}",
      "median_ms=#{summary[:median]}",
      "p95_ms=#{summary[:p95]}",
      "max_ms=#{summary[:maximum]}",
      "average_ms=#{summary[:average]}",
      "median_structural_ms=#{summary[:structural_lookup_milliseconds]}",
      "median_bst_ms=#{summary[:bst_filter_milliseconds]}",
      "median_index_ms=#{summary[:seed_index_build_milliseconds]}",
      "median_ranking_ms=#{summary[:seeded_ranking_milliseconds]}",
      "median_assignment_ms=#{summary[:assignment_verification_milliseconds]}",
      "median_gathering_ms=#{summary[:verification_candidate_gathering_milliseconds]}",
      "median_candidate_ranking_ms=#{summary[:verification_candidate_ranking_milliseconds]}",
      "median_solver_ms=#{summary[:verification_assignment_solver_milliseconds]}",
      "median_other_verification_ms=#{summary[:verification_other_milliseconds]}"
    ].join(" ")
  end

  def self.run
    original_game_temp = $game_temp
    begin
      $game_temp = Game_Temp.new
      Game.load_sprites_list_caches
      Ironmon.reset_custom_fusion_pool_cache
      catalog = Ironmon.evolution_catalog
      pool = Ironmon.custom_fusion_pool
      pool_info = Ironmon.custom_fusion_pool_info
      selected_samples = samples(catalog, pool, pool_info)
      reset_values = benchmark_reset(catalog)
      normal_lookup_value = benchmark_normal_lookup(catalog)
      forward_records = SOURCE_IDENTITIES.map do |source|
        benchmark_forward_source(catalog, pool, pool_info, source)
      end
      records_by_mode = {}
      PAGE_LIMITS.each do |page_limit|
        isolated_mode = "page_#{page_limit}_isolated".to_sym
        records_by_mode[isolated_mode] = selected_samples.map do |sample|
          benchmark_first_page(
            generator(catalog, pool, pool_info), sample, isolated_mode,
            page_limit
          )
        end
        sequential_mode = "page_#{page_limit}_sequential".to_sym
        shared_resolver = generator(catalog, pool, pool_info)
        records_by_mode[sequential_mode] = selected_samples.map do |sample|
          benchmark_first_page(
            shared_resolver, sample, sequential_mode, page_limit
          )
        end
      end
      output = ["fusion predecessor benchmark passed"]
      output << "fusion_rules=#{Ironmon::FusionEvolutionGenerator::RULES_VERSION}"
      output << timing_summary_line(:reset_ready, reset_values)
      output << "timing name=normal_cached_lookup count=1000 average_ms=#{normal_lookup_value.round(6)}"
      forward_records.each { |record| output << forward_line(record) }
      output << timing_summary_line(
        :fusion_forward_cold,
        forward_records.flat_map { |record| record[:cold_values] }
      )
      output << timing_summary_line(
        :fusion_forward_cached,
        forward_records.flat_map { |record| record[:cached_values] }
      )
      records_by_mode.each_value do |records|
        records.each { |record| output << record_line(record) }
      end
      records_by_mode.each do |mode, records|
        output << summary_line(summary(mode, records))
      end
      File.binwrite(OUTPUT_PATH, output.join("\n") + "\n")
    ensure
      Ironmon.reset_custom_fusion_pool_cache
      $game_temp = original_game_temp
      Ironmon.reset_custom_fusion_pool_cache
    end
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

if $ironmon_run_fusion_predecessor_benchmark
  IronmonFusionPredecessorBenchmark.run
end
