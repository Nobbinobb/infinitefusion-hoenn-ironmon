module IronmonGenerationProfileRuntimeTests
  OUTPUT_PATH = $ironmon_generation_profile_test_output_path.to_s
  FIXTURE_PATH = $ironmon_generation_profile_fixture_path.to_s
  POOL_FIXTURE_PATH = $ironmon_custom_fusion_component_fixture_path.to_s

  def self.assert(condition, message)
    raise "Generation-profile test failed: #{message}" if !condition
  end

  def self.assert_lookup_error(expected_code)
    yield
    raise "Generation-profile test failed: #{expected_code} was not raised"
  rescue Ironmon::TrackerLookupError => exception
    assert(
      exception.code == expected_code,
      "lookup failure is reported as #{expected_code}"
    )
  end

  def self.test_installed_profile
    Ironmon.reset_current_generation_profile_cache
    profile = Ironmon.current_generation_profile
    assert(
      profile["profile_id"] == Ironmon.current_generation_profile_id,
      "the installed profile validates to its declared identity"
    )
    assert(
      profile["manifest"]["components"].map { |entry| entry["name"] } ==
        Ironmon::GENERATION_PROFILE_COMPONENT_PATHS.keys.sort,
      "every runtime source component is installed and validated"
    )
    stored = Ironmon.persist_current_generation_profile
    assert(
      stored["profile_id"] == profile["profile_id"] &&
        File.directory?(stored["root"]),
      "the installed package is retained under its content identity"
    )
    component_names = Ironmon::GENERATION_PROFILE_COMPONENT_FILENAMES.values
    assert(
      component_names.all? { |name| File.file?(File.join(stored["root"], name)) } &&
        File.file?(File.join(
          stored["root"], Ironmon::GENERATION_PROFILE_MANIFEST_FILENAME
        )),
      "the retained package contains every immutable source component"
    )
    Ironmon.with_generation_profile(profile["profile_id"]) do
      assert(
        Ironmon.generation_profile_context?(profile["profile_id"]),
        "an archived request selects its pinned profile context"
      )
      component = Ironmon::CustomFusionComponent.identities(File.binread(
        Ironmon.generation_profile_component_path("custom_fusion_pool")
      ))
      assert(
        Ironmon.custom_fusion_pool == component &&
          Ironmon.custom_fusion_pool_info[:fingerprint] ==
            Ironmon.species_pool_fingerprint(component),
        "archived custom fusion lookup reads the pinned packed component"
      )
    end
    assert(
      !Ironmon.generation_profile_context?,
      "profile selection is restored after archived work"
    )
  end

  def self.test_run_profile_pinning
    original_global = $PokemonGlobal
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_run_ledger = Ironmon.default_run_ledger
    attempt = Ironmon.begin_run_attempt(12345)
    profile_id = Ironmon.current_generation_profile_id
    assert(
      attempt["generation_profile_id"] == profile_id &&
        $PokemonGlobal.ironmon_generation_profile_id == profile_id &&
        Ironmon.run_generation_profile_id == profile_id,
      "a new attempt pins the installed profile in save-owned metadata"
    )
    area_recipe = Ironmon.tracker_active_area_recipe(attempt)
    assert(
      area_recipe["generation_profile_id"] == profile_id,
      "the active area recipe carries the pinned profile identity"
    )

    legacy = Ironmon.normalize_run_attempt({
      "attempt_number" => 1,
      "run_id" => "legacy-run",
      "seed" => 42,
      "result" => "active",
      "active_seconds" => 0.0,
      "statistics" => nil
    })
    assert(
      !legacy.key?("generation_profile_id"),
      "normalization does not backfill a legacy attempt"
    )
    $PokemonGlobal.ironmon_run_ledger["current_attempt"] = legacy
    begin
      Ironmon.pinned_generation_profile_id
      raise "legacy attempt unexpectedly inherited the current profile"
    rescue Ironmon::GenerationProfileUnavailable
    end
  ensure
    Ironmon.deactivate_run_generation_profile
    $PokemonGlobal = original_global
  end

  def self.test_active_run_profile_lifecycle
    current_id = "1" * 64
    pinned_id = "2" * 64
    archived_id = "3" * 64
    events = []
    singleton = class << Ironmon; self; end
    methods = [
      :current_generation_profile_id, :generation_profile,
      :activate_generation_profile_data, :restore_generation_profile_data
    ]
    methods.each do |method_name|
      singleton.send(
        :alias_method,
        "generation_profile_lifecycle_original_#{method_name}",
        method_name
      )
    end
    singleton.send(:define_method, :current_generation_profile_id) do
      current_id
    end
    singleton.send(:define_method, :generation_profile) do |profile_id|
      { "profile_id" => profile_id }
    end
    singleton.send(:define_method, :activate_generation_profile_data) do |profile_id|
      events << [:activate, profile_id]
      { :profile_id => profile_id }
    end
    singleton.send(:define_method, :restore_generation_profile_data) do |snapshot|
      events << [:restore, snapshot[:profile_id]] if snapshot
    end

    original_global = $PokemonGlobal
    global = PokemonGlobalMetadata.new
    global.ironmon_mode = true
    global.ironmon_run_ledger = Ironmon.default_run_ledger
    global.ironmon_run_ledger["current_attempt"] = {
      "attempt_number" => 1,
      "run_id" => "pinned-active-run",
      "generation_profile_id" => pinned_id,
      "seed" => 123,
      "result" => "active",
      "active_seconds" => 0.0,
      "statistics" => nil
    }
    profile_id = Ironmon.prepare_generation_profile_load({
      :global_metadata => global
    })
    $PokemonGlobal = global
    assert(
      profile_id == pinned_id &&
        Ironmon.run_generation_profile_id == pinned_id &&
        Ironmon.active_generation_profile_id == pinned_id &&
        events == [[:activate, pinned_id]],
      "loading an active run activates its pinned historical profile"
    )
    Ironmon.finish_generation_profile_load(profile_id)
    Ironmon.with_generation_profile(archived_id) do
      assert(
        Ironmon.active_generation_profile_id == archived_id,
        "an archive request can temporarily select another profile"
      )
    end
    assert(
      Ironmon.active_generation_profile_id == pinned_id &&
        events[-2, 2] == [[:activate, archived_id], [:restore, archived_id]],
      "archive work restores the active run's pinned profile"
    )
    Ironmon.with_generation_profile(current_id) do
      assert(
        Ironmon.active_generation_profile_id == current_id,
        "the installed profile can be selected temporarily from an older run"
      )
    end
    assert(
      Ironmon.active_generation_profile_id == pinned_id &&
        events[-2, 2] == [[:activate, current_id], [:restore, current_id]],
      "temporary installed-profile work also restores the active run"
    )
    Ironmon.instance_variable_set(:@checkpoint_reset_loading, true)
    reset_profile_id = Ironmon.prepare_generation_profile_load({
      :global_metadata => global
    })
    assert(
      !reset_profile_id &&
      !Ironmon.run_generation_profile_id &&
        Ironmon.active_generation_profile_id == current_id &&
        events.last == [:restore, pinned_id],
      "checkpoint reset returns to the installed profile before generation"
    )
  ensure
    Ironmon.instance_variable_set(:@checkpoint_reset_loading, false)
    $PokemonGlobal = original_global
    Ironmon.instance_variable_set(:@active_generation_profile_id, nil)
    Ironmon.instance_variable_set(:@run_generation_profile_id, nil)
    Ironmon.instance_variable_set(:@run_generation_profile_data_snapshot, nil)
    methods.each do |method_name|
      singleton.send(:remove_method, method_name)
      singleton.send(
        :alias_method,
        method_name,
        "generation_profile_lifecycle_original_#{method_name}"
      )
      singleton.send(
        :remove_method,
        "generation_profile_lifecycle_original_#{method_name}"
      )
    end
  end

  def self.test_profile_base_catalog
    profile_id = Ironmon.current_generation_profile_id
    expected_move_access_metadata = Ironmon.move_access_metadata_values
    data = Ironmon.generation_profile_game_data(profile_id)
    species = data[GameData::Species]
    abilities = data[GameData::Ability]
    moves = data[GameData::Move]
    items = data[GameData::Item]
    assert(
      species[1].id_number == 1 && species[species[1].id].equal?(species[1]),
      "the base catalog reconstructs stable species identifiers"
    )
    assert(
      !abilities.empty? && !moves.empty? && !items.empty?,
      "the base catalog reconstructs generator source records"
    )
    assert(
      !data[GameData::Trainer].empty? &&
        !data[GameData::Encounter].empty?,
      "the base catalog reconstructs trainer and encounter records"
    )
    original_species_data = GameData::Species::DATA.dup
    original_move_data = GameData::Move::DATA.dup
    snapshot = Ironmon.activate_generation_profile_data(profile_id)
    assert(
      GameData::Species::DATA.key?(1) &&
        GameData::Species::DATA.key?(species[1].id),
      "the reconstructed species catalog retains both identifier forms"
    )
    profile_species = GameData::Species::DATA[1]
    assert(
      profile_species.id_number == 1 &&
        profile_species.base_stats == species[1].base_stats,
      "the reconstructed catalog can become the authoritative lookup source"
    )
    assert(
      GameData::Move::DATA.keys.map(&:class).tally ==
        original_move_data.keys.map(&:class).tally,
      "profile reconstruction preserves each data class's native key shape"
    )
    assert(
      Ironmon.move_access_metadata_values == expected_move_access_metadata,
      "profile activation preserves deterministic move-access metadata"
    )
  ensure
    Ironmon.restore_generation_profile_data(snapshot) if snapshot
    assert(
      GameData::Species::DATA == original_species_data,
      "the live game catalog is restored after archived lookup"
    ) if original_species_data
  end

  def self.test_archived_profile_rejection
    base_recipe = {
      "schema_version" => 1,
      "run_id" => "archived-run",
      "result" => "lost"
    }
    assert_lookup_error("invalid_recipe") do
      Ironmon.tracker_validate_completed_recipe(
        base_recipe, "archived-run"
      )
    end
    unavailable = base_recipe.merge(
      "generation_profile_id" => "0" * 64
    )
    assert_lookup_error("generation_profile_unavailable") do
      Ironmon.tracker_validate_completed_recipe(
        unavailable, "archived-run"
      )
    end
  end

  def self.run
    fixture = JSON.parse(File.binread(FIXTURE_PATH))
    manifest = fixture["manifest"] || fixture[:manifest]
    expected_json = fixture["canonical_json"] || fixture[:canonical_json]
    expected_id = fixture["profile_id"] || fixture[:profile_id]
    canonical_json = Ironmon::GenerationProfile.canonical_json(
      Ironmon::GenerationProfile.normalize(manifest)
    )
    assert(
      canonical_json == expected_json,
      "Ruby canonical JSON matches the shared golden vector"
    )
    assert(
      Ironmon::GenerationProfile.fingerprint(manifest) == expected_id,
      "Ruby profile identity matches the shared golden vector"
    )
    reordered = {
      "components" => Ironmon::GenerationProfile.value_for(
        manifest, "components"
      ).reverse,
      "algorithms" => Ironmon::GenerationProfile.value_for(
        manifest, "algorithms"
      ).reverse,
      "schema_version" => Ironmon::GenerationProfile.value_for(
        manifest, "schema_version"
      )
    }
    assert(
      Ironmon::GenerationProfile.fingerprint(reordered) == expected_id,
      "descriptor input order does not affect profile identity"
    )
    invalid = Marshal.load(Marshal.dump(manifest))
    Ironmon::GenerationProfile.value_for(invalid, "algorithms").pop
    begin
      Ironmon::GenerationProfile.fingerprint(invalid)
      raise "missing algorithm family was accepted"
    rescue ArgumentError
    end
    pool_fixture = JSON.parse(File.binread(POOL_FIXTURE_PATH))
    normal_species_count = Ironmon::GenerationProfile.value_for(
      pool_fixture, "normal_species_count"
    )
    identities = Ironmon::GenerationProfile.value_for(
      pool_fixture, "identities"
    )
    expected_bytes = Ironmon::GenerationProfile.value_for(
      pool_fixture, "encoded_base64"
    ).unpack("m0")[0]
    encoded = Ironmon::CustomFusionComponent.encode(
      normal_species_count, identities
    )
    assert(
      encoded == expected_bytes,
      "Ruby custom-fusion packing matches the shared golden vector"
    )
    assert(
      Ironmon::CustomFusionComponent.identities(encoded).map(&:to_s) ==
        identities,
      "custom-fusion membership round-trips in deterministic order"
    )
    assert(
      Ironmon::CustomFusionComponent.include?(encoded, 4, 1) &&
        !Ironmon::CustomFusionComponent.include?(encoded, 1, 1),
      "custom-fusion membership queries use the packed bitset"
    )
    test_installed_profile
    test_profile_base_catalog
    test_run_profile_pinning
    test_active_run_profile_lifecycle
    test_archived_profile_rejection
    File.binwrite(OUTPUT_PATH, "generation-profile tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonGenerationProfileRuntimeTests.run
