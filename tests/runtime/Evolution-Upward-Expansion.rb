module IronmonEvolutionUpwardExpansionRuntimeTests
  OUTPUT_PATH = $ironmon_evolution_upward_expansion_test_output_path.to_s

  def self.assert(condition, message)
    raise "Evolution upward-expansion test failed: #{message}" if !condition
  end

  def self.assert_lazy_shuffle(generator)
    candidates = 5.times.map do |index|
      {
        :identity => "candidate-#{index}",
        :family_ids => [index]
      }
    end
    counts = Array.new(candidates.length, 0)
    10_000.times do |sample|
      priority = generator.send(
        :deterministic_state, "shuffle_distribution", sample
      )
      ordered = generator.send(
        :deterministic_candidate_prefix, candidates, priority,
        candidates.length
      )
      assert(ordered.uniq.length == candidates.length,
             "lazy shuffle produces a complete permutation")
      counts[candidates.index(ordered[0])] += 1
    end
    counts.each do |count|
      assert(count >= 1_700 && count <= 2_300,
             "lazy shuffle first-position distribution remains uniform")
    end
    priority = generator.send(:deterministic_state, "shuffle_filter")
    complete = generator.send(
      :deterministic_candidate_prefix, candidates, priority,
      candidates.length
    )
    filtered = generator.send(
      :deterministic_range_prefix, candidates, 0, candidates.length,
      priority, candidates.length - 1, [2]
    )
    assert(filtered == complete.reject { |candidate| candidate[:family_ids] == [2] },
           "family rejection preserves the uniform permutation order")
  end

  def self.assert_fusion_rules_3_upgrade
    original_global = $PokemonGlobal
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_seed = 1_234_567
    Ironmon.record_generator_metadata({
      :ironmon_base_stat_generator_version =>
        Ironmon::BaseStatGenerator::SCHEMA_VERSION,
      :ironmon_base_stat_source_fingerprint =>
        Ironmon.base_stat_source_fingerprint
    })
    metadata = Ironmon.evolution_metadata_values
    metadata[:ironmon_evolution_fusion_rules_version] = 3
    Ironmon.record_generator_metadata(metadata)
    assert(
      Ironmon.legacy_generated_evolution_rules_version == :fusion_rules_3,
      "fusion rules version 3 is recognized as an exact upgrade source"
    )
    assert(
      Ironmon.ensure_evolution_randomization,
      "fusion rules version 3 upgrades without rejecting the save"
    )
    assert(
      $PokemonGlobal.ironmon_evolution_fusion_rules_version ==
        Ironmon::FusionEvolutionGenerator::RULES_VERSION,
      "fusion rules upgrade records the current version"
    )
    assert(
      Ironmon.evolution_randomization_active?,
      "fusion rules upgrade leaves evolution randomization ready"
    )
    recipe = {
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
      "fusion_evolution_rules_version" => 3,
      "fusion_evolution_target_pool_version" =>
        metadata[:ironmon_evolution_fusion_target_pool_version],
      "fusion_evolution_target_pool_size" =>
        metadata[:ironmon_evolution_fusion_target_pool_size],
      "fusion_evolution_target_pool_fingerprint" =>
        metadata[:ironmon_evolution_fusion_target_pool_fingerprint]
    }
    assert(
      Ironmon.tracker_validate_evolution_recipe(recipe),
      "fusion rules version 3 completed recipes upgrade for lookup"
    )
    assert(
      recipe["fusion_evolution_rules_version"] ==
        Ironmon::FusionEvolutionGenerator::RULES_VERSION,
      "completed recipe upgrade records the current fusion rules"
    )
  ensure
    Ironmon.suspend_evolution_randomization
    Ironmon.suspend_base_stat_randomization
    $PokemonGlobal = original_global
  end

  def self.run
    original_game_temp = $game_temp
    reverse_diagnostics = nil
    begin
      $game_temp = Game_Temp.new
      Game.load_sprites_list_caches
      Ironmon.reset_custom_fusion_pool_cache
      catalog = Ironmon.evolution_catalog
      pool = Ironmon.custom_fusion_pool
      pool_info = Ironmon.custom_fusion_pool_info
      pool_index = {}
      pool.each { |species_id| pool_index[species_id] = true }
      expected_graph_levels = {
        [0, 0] => 8, [0, 1] => 0, [1, 1] => 1, [1, 2] => 2,
        [1, 3] => 3, [0, 2] => 4, [2, 2] => 5, [2, 3] => 6,
        [0, 3] => 7, [3, 3] => 8
      }
      assert(Ironmon::EVOLUTION_GRAPH_FUSION_LEVELS == expected_graph_levels,
             "fusion graph levels retain the documented component mapping")
      sample_fusion = GameData::Species.get(pool[0])
      sample_levels = [
        Ironmon.tracker_evolution_component_level(sample_fusion.body_pokemon),
        Ironmon.tracker_evolution_component_level(sample_fusion.head_pokemon)
      ].sort
      assert(
        Ironmon.tracker_evolution_graph_level(sample_fusion) ==
          expected_graph_levels[sample_levels],
        "fusion graph level derives from both native component stages"
      )
      invalid_fusion_identity = nil
      (1..NB_POKEMON).each do |body_id|
        (1..NB_POKEMON).each do |head_id|
          candidate = "B#{body_id}H#{head_id}".to_sym
          next if pool_index[candidate]
          invalid_fusion_identity = candidate if
            GameData::Species.try_get(candidate)
          break if invalid_fusion_identity
        end
        break if invalid_fusion_identity
      end
      assert(invalid_fusion_identity,
             "runtime data exposes a non-pool fusion for boundary testing")
      assert(
        !Ironmon.tracker_lookup_species_available?(
          GameData::Species.get(invalid_fusion_identity)
        ),
        "tracker lookup rejects a fusion outside the custom pool"
      )
      assert_fusion_rules_3_upgrade
      cases = [
        [1689, :B506H10],
        [2895, :B285H506]
      ]
      cases.each do |seed, identity|
        stats = Ironmon::BaseStatGenerator.new(
          seed, Ironmon.base_stat_source_fingerprint
        )
        generator = Ironmon::FusionEvolutionGenerator.new(
          seed, catalog, pool, pool_info, stats
        )
        assert(generator.branches_for(invalid_fusion_identity).empty?,
               "fusion evolution rejects a source outside the custom pool")
        assert_lazy_shuffle(generator) if seed == cases[0][0]
        assert(generator.validate_source(identity),
               "#{identity} seed #{seed} receives a valid assignment")
        branches = generator.branches_for(identity)
        assert(branches.length == 3,
               "#{identity} retains its three conceptual branches")
        assert(branches.map { |branch| branch[:target] }.uniq.length == 3,
               "#{identity} receives three distinct targets")
        target_snapshot = Ironmon.tracker_evolution_target_snapshot(branches[0])
        assert(target_snapshot["stage_level"] ==
                 Ironmon.tracker_evolution_graph_level(
                   GameData::Species.get(branches[0][:target_id])
                 ),
               "generated target snapshots expose their stable graph level")
        assert(target_snapshot["component_side"] ==
                 branches[0][:component_side].to_s,
               "generated target snapshots expose their fusion component")
        expanded = branches.select { |branch| branch[:upward_expansion] }
        assert(expanded.length == 1,
               "#{identity} expands exactly one branch upward")
        assert(expanded[0][:target_bst] >
                 expanded[0][:upward_expansion_floor],
               "#{identity} expansion increases the upper BST boundary")
        replay = Ironmon::FusionEvolutionGenerator.new(
          seed, catalog, pool, pool_info,
          Ironmon::BaseStatGenerator.new(
            seed, Ironmon.base_stat_source_fingerprint
          )
        ).branches_for(identity)
        assert(replay == branches,
               "#{identity} upward rescue is deterministic")
        next if seed != cases[0][0]
        reference_branch = branches.find { |branch| !branch[:upward_expansion] }
        target = reference_branch[:target_id]
        paged_predecessors = []
        continuations = []
        page_offset = 0
        loop do
          page = generator.predecessor_page_for(target, page_offset, 2)
          continuations << page[:continuation]
          assert(page[:branches].length <= 2,
                 "#{target} reverse page respects its requested limit")
          paged_predecessors.concat(page[:branches])
          break if page[:continuation] == :complete
          assert([:available, :unknown].include?(page[:continuation]),
                 "#{target} reverse page exposes a valid continuation")
          assert(page[:next_offset] == page_offset + 2,
                 "#{target} reverse page advances by its requested limit")
          page_offset = page[:next_offset]
        end
        cached_first_page = generator.predecessor_page_for(target, 0, 2)
        predecessors = generator.predecessor_branches_for(target)
        expected_cached_continuation = predecessors.length > 2 ?
          :available : :complete
        assert(
          cached_first_page[:continuation] == expected_cached_continuation,
          "#{target} cached reverse page reports its known continuation"
        )
        assert(paged_predecessors == predecessors,
               "#{target} reverse pages reconstruct the exhaustive result")
        assert(predecessors.any? do |branch|
                 branch[:source] == identity.to_s &&
                   branch[:identity] == reference_branch[:identity]
               end,
               "#{target} reverse lookup includes its generated source")
        assert(predecessors.all? { |branch| branch[:target_id] == target },
               "#{target} reverse lookup contains only exact predecessors")
        assert(predecessors.all? do |branch|
                 branch[:effective_methods] &&
                   !branch[:effective_methods].empty?
               end,
               "#{target} reverse lookup retains a condition for every edge")
        assert(predecessors.all? do |branch|
                 Ironmon.custom_fusion_species?(branch[:source].to_sym)
               end,
               "#{target} reverse lookup contains only custom fusion sources")
        predecessor_snapshot = Ironmon.tracker_evolution_predecessor_snapshot(
          predecessors[0]
        )
        assert(predecessor_snapshot["stage_level"] ==
                 Ironmon.tracker_evolution_graph_level(
                   GameData::Species.get(predecessors[0][:source].to_sym)
                 ),
               "predecessor snapshots expose their stable graph level")
        assert(predecessor_snapshot["component_side"] ==
                 predecessors[0][:component_side].to_s,
               "predecessor snapshots expose their fusion component")
        indexed_source_ids = Ironmon.fusion_predecessor_index.source_ids_for(
          reference_branch[:bucket],
          GameData::Species.get(target).types
        )
        assert(indexed_source_ids.all? do |source_id|
                 body_id = (source_id - 1) / NB_POKEMON
                 head_id = source_id - (body_id * NB_POKEMON)
                 Ironmon.custom_fusion_species?("B#{body_id}H#{head_id}".to_sym)
               end,
               "fusion predecessor index contains only custom fusion sources")
        checked_predecessor = predecessors.find do |branch|
          branch[:source] != identity.to_s
        end
        checked_predecessor ||= predecessors[0]
        direct_generator = Ironmon::FusionEvolutionGenerator.new(
          seed, catalog, pool, pool_info,
          Ironmon::BaseStatGenerator.new(
            seed, Ironmon.base_stat_source_fingerprint
          )
        )
        direct_branch = direct_generator.branches_for(
          checked_predecessor[:source]
        ).find do |branch|
          branch[:identity] == checked_predecessor[:identity]
        end
        assert(direct_branch == checked_predecessor,
               "#{target} shared reverse plan matches direct generation")
        diagnostics = generator.predecessor_lookup_diagnostics(target)
        reverse_diagnostics = diagnostics
        assert(diagnostics[:complete],
               "#{target} exhaustive lookup completes its incremental scan")
        assert(diagnostics[:compatible_component_branch_count] <
                 diagnostics[:catalog_branch_count],
               "#{target} reverse lookup reduces branches by type and stage")
        assert(diagnostics[:verified_source_count] < NB_POKEMON * NB_POKEMON,
               "#{target} reverse lookup avoids exhaustive source generation")
        cached_source_count = generator.cached_source_count
        assert(generator.predecessor_branches_for(target).equal?(predecessors),
               "#{target} reverse lookup result is cached")
        assert(generator.cached_source_count == cached_source_count,
               "#{target} cached lookup does not regenerate sources")
      end
      diagnostic_output = if reverse_diagnostics
                            "target=#{reverse_diagnostics[:target]} " +
                              "branches=#{reverse_diagnostics[:catalog_branch_count]}->" +
                              "#{reverse_diagnostics[:compatible_component_branch_count]} " +
                              "eligible_sources=#{reverse_diagnostics[:eligible_source_count]} " +
                              "standard=#{reverse_diagnostics[:standard_shortlist_count]} " +
                              "rescue_checks=#{reverse_diagnostics[:upward_rescue_check_count]} " +
                              "verified=#{reverse_diagnostics[:verified_source_count]} " +
                              "predecessors=#{reverse_diagnostics[:predecessor_count]} " +
                              "elapsed_ms=#{reverse_diagnostics[:elapsed_milliseconds]}"
                          else
                            "reverse lookup did not run"
                          end
      File.binwrite(
        OUTPUT_PATH,
        "evolution upward-expansion tests passed\n#{diagnostic_output}\n"
      )
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

IronmonEvolutionUpwardExpansionRuntimeTests.run
