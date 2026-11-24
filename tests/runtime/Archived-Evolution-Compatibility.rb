module IronmonArchivedEvolutionCompatibilityTests
  def self.assert(condition, message)
    raise "Archived evolution compatibility failed: #{message}" if !condition
  end

  def self.run
    registry = GameData::Evolution::DATA
    registration = registry[:ReversersLevel]
    original_global = $PokemonGlobal
    cache_fields = [:@evolution_catalog, :@evolution_generator, :@evolution_generator_key,
      :@evolution_randomization_ready, :@fusion_evolution_generator, :@fusion_evolution_generator_key,
      :@tracker_evolution_generators, :@tracker_fusion_evolution_generators]
    saved_caches = cache_fields.map { |field| [field, Ironmon.instance_variable_get(field)] }.to_h

    registry.delete(:ReversersLevel)
    previous = Ironmon::EvolutionCatalog.new
    registry[:ReversersLevel] = registration || GameData::Evolution.new({ :id => :ReversersLevel, :parameter => Integer })
    current = Ironmon::EvolutionCatalog.new
    legacy = current.for_method_fingerprint(previous.method_fingerprint)
    assert(current.method_fingerprint != previous.method_fingerprint && legacy,
      "the extra unused registration must resolve to the exact previous catalog")
    assert(legacy.method_catalog == previous.method_catalog && legacy.branch_catalog == previous.branch_catalog,
      "the previous methods and effective branches are reproduced")
    assert(current.for_method_fingerprint("invalid").nil?, "an unrelated fingerprint is rejected")

    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_seed = 1187411801
    Ironmon.instance_variable_set(:@evolution_catalog, current)
    pool_info = Ironmon.custom_fusion_pool_info
    old_values = Ironmon.evolution_metadata_values(previous, pool_info)
    Ironmon.record_generator_metadata(old_values)
    assert(Ironmon.ensure_evolution_randomization, "the existing attempt resumes")

    seed = $PokemonGlobal.ironmon_seed
    stat_fingerprint = Ironmon.base_stat_source_fingerprint
    baseline_normal = Ironmon::NormalEvolutionGenerator.new(seed, previous, Ironmon::BaseStatGenerator::SCHEMA_VERSION, stat_fingerprint)
    baseline_fusion = Ironmon::FusionEvolutionGenerator.new(seed, previous, Ironmon.custom_fusion_pool,
      pool_info, Ironmon.base_stat_generator)
    assert(Ironmon.evolution_generator.graph_fingerprint == baseline_normal.graph_fingerprint,
      "the resumed run keeps its exact normal evolution graph")
    assert(Ironmon.fusion_evolution_generator.send(:deterministic_value, "compatibility") ==
      baseline_fusion.send(:deterministic_value, "compatibility"),
      "the resumed run keeps its original fusion evolution seed input")
    assert($PokemonGlobal.ironmon_evolution_method_fingerprint == previous.method_fingerprint,
      "resuming leaves the saved identity intact")

    Ironmon.instance_variable_set(:@tracker_evolution_generators, nil)
    Ironmon.instance_variable_set(:@tracker_fusion_evolution_generators, nil)
    recipe = old_values.transform_keys do |field|
      field.to_s.sub(/\Aironmon_evolution_fusion_/, "fusion_evolution_").delete_prefix("ironmon_")
    end.merge("seed" => seed)
    assert(Ironmon.tracker_validate_evolution_recipe(recipe), "the archived recipe accepts the exact previous fingerprint")
    assert(Ironmon.tracker_normal_evolution_generator(recipe).graph_fingerprint == baseline_normal.graph_fingerprint &&
      Ironmon.tracker_fusion_evolution_generator(recipe).send(:deterministic_value, "compatibility") ==
        baseline_fusion.send(:deterministic_value, "compatibility"),
      "archived tracker lookups preserve both original generators")

    Ironmon.record_generator_metadata(Ironmon.evolution_metadata_values(current, pool_info))
    assert(Ironmon.evolution_generator.catalog.method_fingerprint == current.method_fingerprint &&
      Ironmon.fusion_evolution_generator.catalog.method_fingerprint == current.method_fingerprint,
      "a new attempt uses the current registry")
  ensure
    if registration
      registry[:ReversersLevel] = registration
    else
      registry.delete(:ReversersLevel)
    end
    $PokemonGlobal = original_global
    saved_caches.each { |field, value| Ironmon.instance_variable_set(field, value) } if saved_caches
  end
end

IronmonArchivedEvolutionCompatibilityTests.run
