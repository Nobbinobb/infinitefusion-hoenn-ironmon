module IronmonEvolutionUpwardExpansionRuntimeTests
  OUTPUT_PATH = $ironmon_evolution_upward_expansion_test_output_path.to_s

  def self.assert(condition, message)
    raise "Evolution upward-expansion test failed: #{message}" if !condition
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
        assert(generator.validate_source(identity),
               "#{identity} seed #{seed} receives a valid assignment")
        branches = generator.branches_for(identity)
        assert(branches.length == 3,
               "#{identity} retains its three conceptual branches")
        assert(branches.map { |branch| branch[:target] }.uniq.length == 3,
               "#{identity} receives three distinct targets")
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
      end
      File.binwrite(OUTPUT_PATH, "evolution upward-expansion tests passed\n")
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
