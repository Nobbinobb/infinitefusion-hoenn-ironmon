# Fixed vectors were recorded by the native coefficient experiments before the
# production v3 implementation, including both unchanged historical formulas.
module IronmonFusionStatVersionRuntimeTests
  ROWS = [
    [281872251, 102287, [65,47,69,83,18,38], [44,67,185,57,39,148],
      [50,53,107,65,32,74], [48,50,80,59,32,74], [49,51,85,60,32,74]],
    [281872251, 51513, [86,150,23,83,104,54], [153,18,180,197,112,20],
      [130,106,75,158,108,42], [130,136,54,173,110,42], [130,129,62,167,110,42]],
    [281872251, 28641, [60,26,77,75,99,113], [70,47,65,53,28,15],
      [66,32,72,60,51,80], [66,29,72,54,41,102], [66,30,72,56,45,98]],
    [605873817, 133921, [99,100,24,37,107,133], [40,6,64,41,57,28],
      [59,68,37,39,73,97], [59,81,37,39,61,121], [59,76,37,39,64,117]],
    [605873817, 155667, [76,79,129,14,104,113], [59,22,71,57,24,67],
      [64,59,109,42,50,97], [62,73,109,52,50,103], [63,70,109,50,50,100]],
    [605873817, 209283, [76,72,50,107,120,47], [40,31,82,134,133,10],
      [51,58,60,124,128,34], [51,58,56,130,130,43], [51,58,57,129,129,40]]
  ].freeze

  def self.assert(condition, message)
    @assertions = (@assertions || 0) + 1
    raise "Fusion stat version test failed: #{message}" unless condition
  end

  def self.stats(values)
    Hash[Ironmon::BaseStatGenerator::STAT_ORDER.zip(values)]
  end

  def self.profile_for_version(profile, version)
    manifest = Marshal.load(Marshal.dump(profile.fetch("manifest")))
    manifest.fetch("algorithms").find { |entry| entry["name"] == "base_stats" }["version"] = version
    identity = Ironmon::GenerationProfile.fingerprint(manifest)
    return { "profile_id" => identity, "manifest" => manifest,
      "root" => profile.fetch("root") }.freeze
  end

  def self.test_vectors
    ROWS.each do |seed, identity, body, head, *versions|
      species = GameData::Species.get(identity)
      [1, 2, 3].each do |version|
        generator = Ironmon::BaseStatGenerator.new(seed,
          Ironmon.base_stat_source_fingerprint(version), version)
        expected = stats(versions.fetch(version - 1))
        assert(generator.fuse(stats(body), stats(head), identity) == expected,
          "version #{version} retains the recorded fusion #{identity} at seed #{seed}")
        assert(generator.stats_for(species.body_pokemon) == stats(body) &&
          generator.stats_for(species.head_pokemon) == stats(head),
          "all formula versions preserve the recorded normal vectors")
        assert(generator.fusion_stats_for(species) == expected,
          "species lookup uses version #{version} with the same displayed components")
        assert(generator.fuse(stats(body), stats(head)) == stats(versions.first),
          "unseeded native source blending remains two-thirds for every version")
      end
    end
  end

  def self.test_saved_profile_readiness
    previous_global = $PokemonGlobal
    profiles = Ironmon.instance_variable_get(:@generation_profiles)
    installed = Ironmon.current_generation_profile
    variants = [1, 2, 3].map { |version| profile_for_version(installed, version) }
    Ironmon.instance_variable_set(:@generation_profiles, (profiles || {}).merge(
      variants.each_with_object({}) { |profile, result| result[profile["profile_id"]] = profile }))
    seed, identity, _body, _head, *vectors = ROWS.first
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_seed = seed
    baseline_version = Ironmon.base_stat_formula_version
    baseline_fingerprint = Ironmon.base_stat_source_fingerprint
    [1, 2, 3].each do |version|
      profile = variants.fetch(version - 1)
      Ironmon.with_generation_profile(profile["profile_id"]) do
        $PokemonGlobal.ironmon_generation_profile_id = profile["profile_id"]
        $PokemonGlobal.ironmon_base_stat_generator_version = 1
        $PokemonGlobal.ironmon_base_stat_source_fingerprint = Ironmon.base_stat_source_fingerprint(version)
        assert(Ironmon.base_stat_formula_version == version,
          "a saved profile selects its pinned formula #{version}")
        assert(Ironmon.ensure_base_stat_randomization,
          "existing metadata remains accepted for formula #{version}")
        species = GameData::Species.get(identity)
        assert(species.base_stats == stats(vectors.fetch(version - 1)),
          "gameplay stat access retains the pinned formula #{version}")
        conflicting = version == 3 ? 2 : 3
        $PokemonGlobal.ironmon_base_stat_source_fingerprint = Ironmon.base_stat_source_fingerprint(conflicting)
        begin
          Ironmon.ensure_base_stat_randomization
          assert(false, "cross-version metadata was unexpectedly accepted")
        rescue Ironmon::BaseStatRandomizationError => error
          assert(error.message.include?("incompatible"),
            "incompatible metadata is rejected without silently changing the formula")
        end
      end
      assert(Ironmon.base_stat_formula_version == baseline_version &&
        Ironmon.base_stat_source_fingerprint == baseline_fingerprint,
        "leaving archived formula #{version} restores the active calculation")
    end
    Ironmon.with_generation_profile(variants.last["profile_id"]) do
      assert(Ironmon.prepare_base_stat_randomization,
        "a new version-3 profile prepares successfully")
      assert($PokemonGlobal.ironmon_base_stat_generator_version == 1 &&
        $PokemonGlobal.ironmon_base_stat_source_fingerprint == Ironmon.base_stat_source_fingerprint(3),
        "new formula metadata retains the normal generator schema and records its versioned fingerprint")
      assert(Ironmon.base_stat_generator.fusion_stats_for(GameData::Species.get(identity)) == stats(vectors.last),
        "new runs receive the selected 85/75/two-thirds calculation")
    end
  ensure
    Ironmon.instance_variable_set(:@generation_profiles, profiles)
    $PokemonGlobal = previous_global
    Ironmon.suspend_base_stat_randomization
  end

  def self.run
    @assertions = 0
    assert(Ironmon::GenerationProfile::CURRENT_ALGORITHM_VERSIONS.fetch("base_stats") == 3,
      "new generated profiles select stat formula 3")
    assert(Ironmon::BaseStatGenerator::SCHEMA_VERSION == 1,
      "the normal stat generator schema is unchanged")
    assert(Ironmon.base_stat_source_fingerprint(1) == "1e25ae036f296389" &&
      Ironmon.base_stat_source_fingerprint(2) == "2d3777878d24e250",
      "historical catalog fingerprints stay exactly stable")
    assert(Ironmon.base_stat_source_fingerprint(3) == "7c8f8b9b602bdcdb",
      "new formula metadata matches the measured version-3 source fingerprint")
    assert([1, 2, 3].map { |version| Ironmon.base_stat_source_fingerprint(version) }.uniq.length == 3,
      "all formula versions have distinct source fingerprints")
    test_vectors
    test_saved_profile_readiness
    return @assertions
  end
end

IronmonFusionStatVersionRuntimeTests.run
