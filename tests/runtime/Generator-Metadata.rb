module IronmonGeneratorMetadataRuntimeTests
  OUTPUT_PATH = $ironmon_generator_metadata_test_output_path.to_s

  def self.assert(condition, message)
    raise "Generator-metadata test failed: #{message}" if !condition
  end

  def self.test_shared_metadata_primitives
    original_global = $PokemonGlobal
    $PokemonGlobal = PokemonGlobalMetadata.new
    fields = [
      :ironmon_ability_generator_version,
      :ironmon_ability_pool_size,
      :ironmon_ability_pool_fingerprint
    ]
    assert(
      Ironmon.generator_metadata_absent?(fields),
      "new metadata fields are recognized as absent"
    )
    Ironmon.record_generator_metadata({
      :ironmon_ability_generator_version => 3,
      :ironmon_ability_pool_size => 250,
      :ironmon_ability_pool_fingerprint => "pool-a"
    })
    assert(
      Ironmon.generator_metadata_values(fields) == [3, 250, "pool-a"],
      "metadata recording and ordered reads use the declared fields"
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
    assert(
      Ironmon.generator_metadata_compatible_when_present?([
        ["missing", nil, "new"], ["same", "kept", "kept"]
      ]),
      "migration comparison accepts missing legacy fields"
    )
    assert(
      !Ironmon.generator_metadata_compatible_when_present?([
        ["changed", "old", "new"]
      ]),
      "migration comparison rejects conflicting legacy fields"
    )
  ensure
    $PokemonGlobal = original_global
  end

  def self.test_pre_generator_legacy_policies
    original_global = $PokemonGlobal
    original_switches = $game_switches
    switches_snapshot = Marshal.dump($game_switches) if $game_switches
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    assert(
      Ironmon.ensure_base_stat_randomization &&
        !Ironmon.base_stat_randomization_active?,
      "pre-generator saves retain native base stats"
    )
    assert(
      Ironmon.ensure_move_access_randomization &&
        !Ironmon.move_access_randomization_active?,
      "pre-generator saves retain native move access"
    )
    assert(
      Ironmon.ensure_evolution_randomization &&
        !Ironmon.evolution_randomization_active?,
      "pre-generator saves retain native evolutions"
    )
    assert(
      Ironmon.ensure_item_randomization &&
        !Ironmon.item_randomization_active?,
      "pre-generator saves retain native item behavior"
    )
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

  def self.test_saved_run_migration
    original_global = $PokemonGlobal
    original_game_temp = $game_temp
    original_pool_service = Ironmon.instance_variable_get(
      :@custom_fusion_pool_service
    )
    original_approval = Ironmon.instance_variable_get(
      :@approved_saved_run_migration
    )
    singleton = class << Ironmon; self; end
    original_confirmation = singleton.instance_method(
      :confirm_saved_run_migration
    )
    metadata = PokemonGlobalMetadata.new
    metadata.ironmon_mode = true
    metadata.ironmon_seed = 918_273_645
    $game_temp = Game_Temp.new
    Game.load_sprites_list_caches
    Ironmon.reset_custom_fusion_pool_cache
    $PokemonGlobal = metadata
    assert(
      Ironmon.prepare_base_stat_randomization,
      "migration fixture prepares base-stat metadata"
    )
    prepared_evolutions = Ironmon.prepare_evolution_randomization
    assert(
      prepared_evolutions,
      "migration fixture prepares evolution metadata: " +
        Ironmon.evolution_randomization_error_message.to_s
    )
    Ironmon.record_custom_fusion_pool_metadata
    save_data = {
      :game_version => "6.8.0",
      :global_metadata => metadata
    }
    assert(
      Ironmon.saved_run_migration_issues(save_data).empty?,
      "current saved-run metadata needs no migration"
    )

    metadata.ironmon_custom_fusion_pool_size += 1
    metadata.ironmon_custom_fusion_pool_fingerprint = "old-fusion-pool"
    metadata.ironmon_evolution_source_fingerprint = "old-evolutions"
    before_detection = [
      metadata.ironmon_custom_fusion_pool_size,
      metadata.ironmon_custom_fusion_pool_fingerprint,
      metadata.ironmon_evolution_source_fingerprint
    ]
    issues = Ironmon.saved_run_migration_issues(save_data)
    assert(
      issues == [:custom_fusion_pool, :evolution_randomization],
      "saved-run inspection reports all incompatible catalogs once"
    )
    sprite_message = Ironmon.saved_run_migration_message(
      save_data.merge(:game_version => Settings::GAME_VERSION_NUMBER),
      [:custom_fusion_pool]
    )
    assert(
      sprite_message.include?("game version is still") &&
        sprite_message.include?("CUSTOM_SPRITES") &&
        sprite_message.include?("Sprite_Credits.csv") &&
        !sprite_message.include?("incompatible with Infinite Fusion"),
      "same-version sprite-pool migration names its actual cause"
    )
    assert(
      before_detection == [
        metadata.ironmon_custom_fusion_pool_size,
        metadata.ironmon_custom_fusion_pool_fingerprint,
        metadata.ironmon_evolution_source_fingerprint
      ],
      "saved-run inspection does not mutate declined save data"
    )
    assert(
      $PokemonGlobal.equal?(metadata),
      "saved-run inspection restores the caller's global metadata"
    )

    singleton.send(:define_method, :confirm_saved_run_migration) do |_data, _issues|
      false
    end
    begin
      Ironmon.begin_saved_run_migration(save_data)
      assert(false, "declined saved-run migration stops loading")
    rescue Ironmon::SavedRunMigrationDeclined
      assert(
        !Ironmon.saved_run_migration_approved?(:evolution_randomization),
        "declined migration grants no compatibility override"
      )
    end
    assert(
      before_detection == [
        metadata.ironmon_custom_fusion_pool_size,
        metadata.ironmon_custom_fusion_pool_fingerprint,
        metadata.ironmon_evolution_source_fingerprint
      ],
      "declining migration leaves the save metadata unchanged"
    )

    singleton.send(:define_method, :confirm_saved_run_migration) do |_data, _issues|
      true
    end
    assert(
      Ironmon.begin_saved_run_migration(save_data) &&
        Ironmon.saved_run_migration_approved?(:custom_fusion_pool) &&
        Ironmon.saved_run_migration_approved?(:evolution_randomization),
      "one accepted prompt approves every reported migration system"
    )
    assert(
      Ironmon.ensure_evolution_randomization,
      "approved evolution migration refreshes incompatible metadata"
    )
    Ironmon.record_custom_fusion_pool_metadata
    assert(
      Ironmon.current_evolution_randomization? &&
        Ironmon.saved_run_migration_issues(save_data).empty?,
      "approved migration produces current runnable metadata"
    )
  ensure
    singleton.send(
      :define_method, :confirm_saved_run_migration, original_confirmation
    ) if singleton && original_confirmation
    Ironmon.finish_saved_run_migration
    Ironmon.instance_variable_set(
      :@approved_saved_run_migration, original_approval
    )
    Ironmon.suspend_evolution_randomization
    Ironmon.suspend_base_stat_randomization
    Ironmon.instance_variable_set(
      :@custom_fusion_pool_service, original_pool_service
    )
    $game_temp = original_game_temp
    $PokemonGlobal = original_global
  end

  def self.test_saved_run_migration_source_ownership
    game_load_location = Game.method(:load).source_location
    load_screen_location = PokemonLoadScreen.instance_method(
      :pbStartLoadScreen
    ).source_location
    assert(
      game_load_location && load_screen_location &&
        File.basename(game_load_location[0]) ==
          "003_Z_Saved_Run_Migration.rb" &&
        File.basename(load_screen_location[0]) ==
          "003_Z_Saved_Run_Migration.rb",
      "saved-run migration owns load approval and clean decline handling"
    )
  end

  def self.test_saved_run_migration_decline_returns_to_load_screen
    load_screen = PokemonLoadScreen
    original_start = load_screen.instance_method(
      :ironmon_saved_run_migration_original_start
    )
    calls = 0
    load_screen.send(
      :define_method, :ironmon_saved_run_migration_original_start
    ) do
      calls += 1
      raise Ironmon::SavedRunMigrationDeclined if calls == 1
      :reopened
    end
    screen = load_screen.allocate
    result = screen.pbStartLoadScreen
    assert(
      result == :reopened && calls == 2 &&
        screen.instance_variable_get(:@scene).is_a?(PokemonLoad_Scene),
      "declining migration rebuilds the save-selection scene"
    )
  ensure
    load_screen.send(
      :define_method, :ironmon_saved_run_migration_original_start,
      original_start
    ) if load_screen && original_start
  end

  def self.run
    test_shared_metadata_primitives
    test_pre_generator_legacy_policies
    test_partial_metadata_is_not_absent
    test_evolution_source_ownership
    test_ability_source_ownership
    test_species_source_ownership
    test_saved_run_migration
    test_saved_run_migration_source_ownership
    test_saved_run_migration_decline_returns_to_load_screen
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
