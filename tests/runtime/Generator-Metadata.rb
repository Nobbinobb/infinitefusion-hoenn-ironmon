module IronmonGeneratorMetadataRuntimeTests
  OUTPUT_PATH = $ironmon_generator_metadata_test_output_path.to_s

  def self.assert(condition, message)
    raise "Generator-metadata test failed: #{message}" if !condition
  end

  def self.test_shared_metadata_primitives
    original_global = $PokemonGlobal
    $PokemonGlobal = PokemonGlobalMetadata.new
    Ironmon.record_generator_metadata({
      :ironmon_ability_generator_version => 3,
      :ironmon_ability_pool_size => 250,
      :ironmon_ability_pool_fingerprint => "pool-a"
    })
    assert(
      $PokemonGlobal.ironmon_ability_generator_version == 3 &&
        $PokemonGlobal.ironmon_ability_pool_size == 250 &&
        $PokemonGlobal.ironmon_ability_pool_fingerprint == "pool-a",
      "metadata recording writes every declared field"
    )
    checks = [
      ["schema", 3, 3],
      ["size", 250, 250],
      ["catalog", "pool-a", "pool-b"]
    ]
    assert(
      Ironmon.generator_metadata_mismatch(checks) == "catalog" &&
        !Ironmon.generator_metadata_matches?(checks),
      "exact comparison reports the first incompatible field"
    )
  ensure
    $PokemonGlobal = original_global
  end

  def self.test_missing_generator_metadata_is_rejected
    original_global = $PokemonGlobal
    original_switches = $game_switches
    switches_snapshot = Marshal.dump($game_switches) if $game_switches
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    checks = [
      [proc { Ironmon.ensure_base_stat_randomization },
       Ironmon::BaseStatRandomizationError],
      [proc { Ironmon.ensure_move_access_randomization },
       Ironmon::MoveAccessRandomizationError],
      [proc { Ironmon.ensure_evolution_randomization },
       Ironmon::EvolutionRandomizationError],
      [proc { Ironmon.ensure_item_randomization },
       Ironmon::ItemRandomizationError]
    ]
    checks.each do |operation, error_class|
      error = nil
      begin
        operation.call
      rescue Exception => exception
        error = exception
      end
      assert(
        error && error.is_a?(error_class) &&
          error.message.include?("incompatible"),
        "missing generator metadata reports incompatibility: " +
          (error ? "#{error.class}: #{error.message}" : "no error")
      )
    end
  ensure
    Ironmon.suspend_base_stat_randomization
    Ironmon.suspend_move_access_randomization
    Ironmon.suspend_evolution_randomization
    Ironmon.suspend_item_randomization
    $PokemonGlobal = original_global
    if original_switches && switches_snapshot
      original_switches.replace(Marshal.load(switches_snapshot))
    end
    $game_switches = original_switches
  end

  def self.test_partial_metadata_is_not_absent
    original_global = $PokemonGlobal
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_base_stat_generator_version =
      Ironmon::BaseStatGenerator::SCHEMA_VERSION
    begin
      Ironmon.ensure_base_stat_randomization
      assert(false, "partial base-stat metadata should be rejected")
    rescue Ironmon::BaseStatRandomizationError => error
      assert(
        error.message.include?("incompatible"),
        "partial metadata reports incompatibility instead of legacy absence"
      )
    end
  ensure
    Ironmon.suspend_base_stat_randomization
    $PokemonGlobal = original_global
  end

  def self.test_evolution_source_ownership
    generator_location = Ironmon::NormalEvolutionGenerator.instance_method(
      :graph
    ).source_location
    metadata_location = Ironmon.method(:evolution_metadata_values).source_location
    readiness_location = Ironmon.method(
      :ensure_evolution_randomization
    ).source_location
    assert(
      generator_location &&
        File.basename(generator_location[0]) ==
          "003_Evolution_Randomization_Generation.rb",
      "normal evolution graph generation stays in the generator source"
    )
    assert(
      metadata_location && readiness_location &&
        File.basename(metadata_location[0]) ==
          "003_Evolution_Randomization_Readiness.rb" &&
        File.basename(readiness_location[0]) ==
          "003_Evolution_Randomization_Readiness.rb",
      "evolution metadata and readiness share their lifecycle source"
    )
    assert(
      Ironmon::EVOLUTION_METADATA_FIELDS.frozen?,
      "evolution metadata field ownership remains immutable"
    )
  end

  def self.test_ability_source_ownership
    generator_location = Ironmon::AbilityGenerator.instance_method(
      :slots_for
    ).source_location
    readiness_location = Ironmon.method(:allowed_ability_pool).source_location
    resolution_location = Ironmon.method(
      :generated_normal_abilities
    ).source_location
    runtime_location = GameData::Species.instance_method(:abilities).source_location
    assert(
      generator_location &&
        File.basename(generator_location[0]) ==
          "003_Ability_Randomization.rb",
      "ability generation stays in the generator source"
    )
    assert(
      readiness_location &&
        File.basename(readiness_location[0]) ==
          "003_Ability_Randomization_Readiness.rb",
      "ability pool metadata stays in the readiness source"
    )
    assert(
      resolution_location &&
        File.basename(resolution_location[0]) ==
          "003_Ability_Randomization_Resolution.rb",
      "ability slot resolution stays in the resolution source"
    )
    assert(
      runtime_location &&
        File.basename(runtime_location[0]) ==
          "003_Ability_Randomization_Runtime_Integration.rb",
      "ability engine patches stay in the runtime source"
    )
  end

  def self.test_species_source_ownership
    generator_location = Ironmon::SpeciesGenerator.instance_method(
      :map
    ).source_location
    pools_location = Ironmon.method(:species_generator).source_location
    wild_location = Ironmon.method(:wild_species_for).source_location
    trainer_location = Ironmon.method(:trainer_species_for).source_location
    readiness_location = Ironmon.method(:prepare_species_mappings).source_location
    assert(
      generator_location &&
        File.basename(generator_location[0]) == "003_Species_Generators.rb",
      "species mapping stays in the deterministic generator source"
    )
    assert(
      pools_location &&
        File.basename(pools_location[0]) ==
          "003_Species_Generators_1_Pools.rb",
      "species pool construction stays in the pool source"
    )
    assert(
      wild_location && trainer_location &&
        File.basename(wild_location[0]) ==
          "003_Species_Generators_2_Mapping.rb" &&
        File.basename(trainer_location[0]) ==
          "003_Species_Generators_2_Mapping.rb",
      "wild and trainer mapping share their runtime mapping source"
    )
    assert(
      readiness_location &&
        File.basename(readiness_location[0]) ==
          "003_Species_Generators_3_Readiness.rb",
      "species mapping lifecycle stays in the readiness source"
    )
  end

  def self.test_saved_runs_are_not_migrated
    migration_methods = [
      :begin_saved_run_migration,
      :confirm_saved_run_migration,
      :saved_run_migration_issues,
      :saved_run_migration_approved?
    ]
    assert(
      migration_methods.none? { |method_name| Ironmon.respond_to?(method_name) },
      "saved runs retain their pinned generation metadata without migration"
    )
  end

  def self.run
    test_shared_metadata_primitives
    test_missing_generator_metadata_is_rejected
    test_partial_metadata_is_not_absent
    test_evolution_source_ownership
    test_ability_source_ownership
    test_species_source_ownership
    test_saved_runs_are_not_migrated
    File.binwrite(OUTPUT_PATH, "generator-metadata tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonGeneratorMetadataRuntimeTests.run
