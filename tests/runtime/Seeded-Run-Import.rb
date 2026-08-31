module IronmonSeededRunImportRuntimeTests
  OUTPUT_PATH = $ironmon_seeded_run_import_test_output_path.to_s
  EXPECTED_WORLD_SNAPSHOT_SHA256 =
    "445ae131d085abb29bfeb2fdc44beac05a4588fda4d83ccb6e7a9d117a1190d4"

  def self.assert(condition, message)
    raise "Seeded-run import runtime test failed: #{message}" if !condition
  end

  def self.compatibility_fixture
    return {
      "seed" => 123_456_789,
      "game_version" => "6.7.2",
      "ironmon_version" => "0.7.6",
      "configuration" => {
        "schema_version" => 1,
        "wild_policy" => "mixed",
        "trainer_policy" => "custom_only",
        "unfusion_setting" => "random_component"
      },
      "data_mode" => "classic",
      "species_generator" => {
        "version" => 1,
        "pool_fingerprint" => "species-a"
      },
      "ability_generator" => {
        "version" => 3,
        "pool_size" => 310,
        "pool_fingerprint" => "abilities-a"
      },
      "base_stat_generator" => {
        "version" => 2,
        "source_fingerprint" => "stats-a"
      },
      "evolution_generator" => {
        "version" => 3,
        "rules_version" => 3,
        "source_fingerprint" => "evolutions-a",
        "taxonomy_fingerprint" => "taxonomy-a",
        "method_fingerprint" => "methods-a",
        "target_fingerprint" => "targets-a",
        "base_stat_generator" => {
          "version" => 2,
          "source_fingerprint" => "stats-a"
        },
        "fusion" => {
          "version" => 2,
          "rules_version" => 2,
          "target_pool" => {
            "version" => 1,
            "size" => 174_348,
            "fingerprint" => "fusions-a"
          }
        }
      },
      "move_access_generator" => {
        "version" => 2,
        "pool_fingerprint" => "moves-a",
        "contextual_restriction_fingerprint" => "move-rules-a",
        "level_up_source_fingerprint" => "level-up-a",
        "egg_source_fingerprint" => "egg-a",
        "tm" => {
          "roster_fingerprint" => "tm-roster-a",
          "source_fingerprint" => "tm-source-a"
        },
        "tr" => {
          "roster_fingerprint" => "tr-roster-a",
          "source_fingerprint" => "tr-source-a"
        },
        "tutor" => {
          "catalog_fingerprint" => "tutor-catalog-a",
          "source_fingerprint" => "tutor-source-a"
        },
        "fusion_tutor" => {
          "catalog_fingerprint" => "fusion-tutor-catalog-a",
          "source_fingerprint" => "fusion-tutor-source-a"
        }
      },
      "player_fusion_generator" => {
        "version" => 2,
        "pool_size" => 174_348,
        "pool_fingerprint" => "fusions-a"
      },
      "item_generator" => {
        "version" => 1,
        "rules_version" => 3,
        "ground_pool_size" => 580,
        "ground_total_weight" => 5_253,
        "ground_pool_fingerprint" => "items-a",
        "tm_pool_size" => 124,
        "tm_pool_fingerprint" => "tm-items-a",
        "result_bans" => ["DNAREVERSER", "DNASPLICERS"],
        "result_ban_fingerprint" => "item-bans-a",
        "shop_policy_version" => 1
      }
    }
  end

  def self.with_transaction_stubs(generation_result)
    singleton = class << Ironmon; self; end
    methods = [
      :apply_preset, :rollback_seed_import, :send_seed_import_status,
      :start_tracker_run, :tracker_connection, :configuration=
    ]
    methods.each do |method|
      singleton.send(:alias_method, "seed_import_test_original_#{method}", method)
    end
    Ironmon.instance_variable_set(:@seed_import_test_generation_result, generation_result)
    Ironmon.instance_variable_set(:@seed_import_test_statuses, [])
    Ironmon.instance_variable_set(:@seed_import_test_events, [])
    Ironmon.instance_variable_set(:@seed_import_test_rollback, nil)
    Ironmon.instance_variable_set(:@seed_import_test_started, false)
    singleton.send(:define_method, :apply_preset) do |*arguments|
      @seed_import_test_generation_result
    end
    singleton.send(:define_method, :rollback_seed_import) do |save_data|
      @seed_import_test_rollback = save_data
      nil
    end
    singleton.send(:define_method, :send_seed_import_status) do |*arguments|
      @seed_import_test_statuses << arguments
    end
    singleton.send(:define_method, :start_tracker_run) do
      @seed_import_test_started = true
    end
    singleton.send(:define_method, :configuration=) { |value| value }
    connection = Object.new
    connection.define_singleton_method(:send_event) do |*arguments|
      Ironmon.instance_variable_get(:@seed_import_test_events) << arguments
    end
    singleton.send(:define_method, :tracker_connection) { connection }
    return yield
  ensure
    methods.each do |method|
      original = "seed_import_test_original_#{method}"
      singleton.send(:alias_method, method, original)
      singleton.send(:remove_method, original)
    end
  end

  def self.transaction_fixture
    return {
      "token_id" => "seed-token-42",
      "seed" => 42,
      "configuration" => {
        "schema_version" => Ironmon::Configuration::SCHEMA_VERSION,
        "wild_policy" => "mixed",
        "trainer_policy" => "normal_only",
        "unfusion_setting" => "player_choice",
        "automatic_reset" => false
      },
      "compatibility_fingerprint" => "a" * 64,
      "rollback_data" => { :marker => :live_attempt },
      "completed_recipe" => { "run_id" => "run-source" },
      "source_run_id" => "run-source",
      "source_sequence" => 17,
      "save_slot" => nil
    }
  end

  def self.custom_fusion_pool_fixture
    pool = (1..20).flat_map do |body|
      (1..20).map { |head| "B#{body}H#{head}".to_sym }
    end.freeze
    service = Object.new
    service.define_singleton_method(:pool) { pool }
    service.define_singleton_method(:info) do
      {
        :schema_version => Ironmon::CustomFusionPool::SCHEMA_VERSION,
        :size => pool.length,
        :fingerprint => Ironmon.species_pool_fingerprint(pool),
        :source_entries => pool.length,
        :rejected_entries => 0
      }
    end
    return service
  end

  def self.test_transaction(generation_result)
    with_transaction_stubs(generation_result) do
      transaction = transaction_fixture
      Ironmon.instance_variable_set(:@seed_import_transaction, transaction)
      Ironmon.instance_variable_set(:@seed_import_in_progress, true)
      Ironmon.instance_variable_set(:@reset_in_progress, true)
      result = Ironmon.finish_seed_import
      statuses = Ironmon.instance_variable_get(:@seed_import_test_statuses)
      if generation_result
        assert(result, "successful generation commits")
        assert(
          Ironmon.instance_variable_get(:@seed_import_test_started),
          "successful generation starts the tracker run"
        )
        assert(statuses.last[1] == "started", "success reports started")
        assert(
          !Ironmon.instance_variable_get(:@seed_import_test_rollback),
          "success does not roll back"
        )
        event = Ironmon.instance_variable_get(:@seed_import_test_events).last
        assert(
          event[0] == "run_completed" && event[2] == "run-source" &&
            event[3] == 18,
          "source completion keeps the old run sequence"
        )
      else
        assert(!result, "failed generation does not commit")
        assert(
          Ironmon.instance_variable_get(:@seed_import_test_rollback) ==
            transaction["rollback_data"],
          "failed generation restores the live snapshot"
        )
        assert(statuses.last[1] == "failed", "failure reports failed")
      end
      assert(
        !Ironmon.instance_variable_get(:@seed_import_in_progress) &&
          !Ironmon.instance_variable_get(:@reset_in_progress),
        "transaction flags clear exactly once"
      )
    end
  end

  def self.test_strict_request_shapes
    assert(
      Ironmon.validate_seed_import_request({ :token_id => "symbol" }, nil) ==
        "Seeded-run import payload has unsupported fields.",
      "non-string request keys are rejected without sorting errors"
    )
    configuration = transaction_fixture["configuration"].merge(
      :extension => true
    )
    assert(
      Ironmon.validate_seed_import_configuration(configuration) ==
        "Seeded-run configuration has unsupported fields.",
      "non-string configuration keys are rejected without sorting errors"
    )
    assert(
      (("a" * 64) + "\n") !~ Ironmon::SEED_IMPORT_FINGERPRINT_PATTERN,
      "a fingerprint with trailing data is rejected"
    )
  end

  def self.test_explicit_export
    singleton = class << Ironmon; self; end
    methods = [
      :active?, :current_run_attempt, :ensure_tracker_run_id,
      :tracker_run_reproduction_recipe
    ]
    methods.each do |method|
      singleton.send(:alias_method, "seed_export_test_original_#{method}", method)
    end
    recipe = compatibility_fixture
    singleton.send(:define_method, :active?) { true }
    singleton.send(:define_method, :current_run_attempt) do
      { "result" => "active" }
    end
    singleton.send(:define_method, :ensure_tracker_run_id) { "run-export" }
    singleton.send(:define_method, :tracker_run_reproduction_recipe) { recipe }
    Ironmon.instance_variable_set(:@reset_in_progress, false)
    Ironmon.instance_variable_set(:@tracker_reset_requested, false)
    Ironmon.instance_variable_set(:@tracker_seed_import_pending, nil)
    exported = Ironmon.tracker_seeded_run_export("run-export")
    assert(exported == recipe, "explicit export returns only reproduction inputs")
    begin
      Ironmon.tracker_seeded_run_export("run-stale")
      assert(false, "stale export should fail")
    rescue Ironmon::TrackerLookupError => error
      assert(error.code == "seed_export_stale", "stale export is rejected")
    end
  ensure
    methods.each do |method|
      original = "seed_export_test_original_#{method}"
      singleton.send(:alias_method, method, original)
      singleton.send(:remove_method, original)
    end
  end

  def self.generated_world_snapshot(seed, reverse)
    original_global = $PokemonGlobal
    original_switches = $game_switches
    original_variables = $game_variables
    original_pool_service = Ironmon.instance_variable_get(
      :@custom_fusion_pool_service
    )
    begin
      $PokemonGlobal = PokemonGlobalMetadata.new
      $game_switches = []
      $game_variables = []
      pool_service = custom_fusion_pool_fixture
      Ironmon.instance_variable_set(
        :@custom_fusion_pool_service, pool_service
      )
      Ironmon.configuration = transaction_fixture["configuration"]
      generated_preset = Ironmon.apply_preset(:seed_import, seed, false)
      generation_error = Ironmon.instance_variable_get(
        :@seed_import_error_message
      ) || Ironmon.generation_error_message
      assert(
        generated_preset,
        "the imported preset generates successfully: #{generation_error}"
      )
      preparation = Ironmon.instance_variable_get(
        :@player_fusion_preparation_fiber
      )
      assert(
        preparation && preparation.alive?,
        "preset application defers global player-fusion pairing"
      )
      preparation_steps = 0
      maximum_preparation_slice = 0.0
      while preparation.alive?
        started_at = System.uptime
        Ironmon.advance_player_fusion_pairing
        elapsed = (System.uptime - started_at).to_f / 1_000_000.0
        maximum_preparation_slice = elapsed if
          elapsed > maximum_preparation_slice
        preparation_steps += 1
      end
      assert(
        preparation_steps > 1 && maximum_preparation_slice < 0.25,
        "global player-fusion pairing advances in bounded map-frame slices: " +
          "#{preparation_steps} slices, " +
          "#{maximum_preparation_slice.round(3)} seconds maximum"
      )

      species = [:BULBASAUR, :CHARMANDER, :SQUIRTLE].map do |id|
        GameData::Species.get(id)
      end
      ordered_species = reverse ? species.reverse : species
      contexts = {
        :BULBASAUR => [:starter, 0],
        :CHARMANDER => [:table, "runtime", 1],
        :SQUIRTLE => [:pbs, "runtime", 2]
      }
      generated = {}
      ordered_species.each do |source|
        generated[source.id] = {
          :wild => Ironmon.species_generator(:wild).map(
            source.id, contexts[source.id]
          ),
          :trainer => Ironmon.species_generator(:trainer).map(
            source.id, contexts[source.id]
          ),
          :abilities => Ironmon.ability_generator.slots_for(source),
          :base_stats => Ironmon.base_stat_generator.stats_for(source),
          :moves => Ironmon.move_access_generator.moves_for(source),
          :egg_moves => Ironmon.move_access_generator.egg_moves_for(source),
          :tm_moves => Ironmon.move_access_generator.tm_moves_for(source),
          :tutor_moves => Ironmon.move_access_generator.tutor_moves_for(source),
          :evolutions => Ironmon.evolution_generator.branches_for(source)
        }
      end

      fusion_species = Ironmon.custom_fusion_pool.first(2).map do |id|
        GameData::Species.get(id)
      end
      ordered_fusions = reverse ? fusion_species.reverse : fusion_species
      fusion_evolutions = {}
      ordered_fusions.each do |source|
        fusion_evolutions[source.id] =
          Ironmon.fusion_evolution_generator.branches_for(source)
      end

      pairs = [
        [:BULBASAUR, :CHARMANDER],
        [:SQUIRTLE, :BULBASAUR]
      ]
      ordered_pairs = reverse ? pairs.reverse : pairs
      player_fusions = {}
      ordered_pairs.each do |body, head|
        player_fusions[[body, head]] =
          Ironmon.player_fusion_mapper.species(body, head)
      end

      item_slots = ["map:1|event:2", "map:9|event:4"]
      ordered_items = reverse ? item_slots.reverse : item_slots
      ground_items = {}
      tm_gifts = {}
      ordered_items.each do |slot|
        ground_items[slot] = Ironmon.item_slot_generator.ground_item(slot)
        tm_gifts[slot] = Ironmon.item_slot_generator.tm_gift(slot)
      end

      return {
        :recipe => Ironmon.tracker_run_reproduction_recipe,
        :species => species.to_h { |source| [source.id, generated[source.id]] },
        :fusion_evolutions => fusion_species.to_h do |source|
          [source.id, fusion_evolutions[source.id]]
        end,
        :player_fusions => pairs.to_h do |pair|
          [pair, player_fusions[pair]]
        end,
        :ground_items => item_slots.to_h do |slot|
          [slot, ground_items[slot]]
        end,
        :tm_gifts => item_slots.to_h do |slot|
          [slot, tm_gifts[slot]]
        end,
        :gym_additions => (0..2).map do |slot|
          Ironmon.gym_leader_source_species_for(
            seed, :LEADER_Roxanne, "Roxanne", slot
          )
        end,
        :progression => (0..1).map do |pool_index|
          Ironmon.progression_random_value(:runtime_replay, pool_index)
        end
      }
    ensure
      Ironmon.suspend_ability_randomization
      Ironmon.suspend_base_stat_randomization
      Ironmon.suspend_evolution_randomization
      Ironmon.suspend_move_access_randomization
      Ironmon.suspend_item_randomization
      Ironmon.reset_species_generator_cache
      Ironmon.reset_player_fusion_mapper_cache
      Ironmon.instance_variable_set(
        :@custom_fusion_pool_service, original_pool_service
      )
      $PokemonGlobal = original_global
      $game_switches = original_switches
      $game_variables = original_variables
    end
  end

  def self.test_repeated_import_determinism
    seed = 987_654_321
    forward = generated_world_snapshot(seed, false)
    reverse = generated_world_snapshot(seed, true)
    assert(
      forward == reverse,
      "two imports reproduce every seeded generator across lookup order"
    )
    second_seed = generated_world_snapshot(seed + 1, false)
    assert(
      forward.reject { |key, _value| key == :recipe } !=
        second_seed.reject { |key, _value| key == :recipe },
      "a different imported seed changes the generated world"
    )
    baseline_snapshot = Marshal.load(Marshal.dump(forward))
    baseline_snapshot[:recipe] = baseline_snapshot[:recipe].reject do |key, _value|
      key == "ironmon_version"
    end
    digest = Digest::SHA256.hexdigest(Marshal.dump(baseline_snapshot))
    assert(
      digest == EXPECTED_WORLD_SNAPSHOT_SHA256,
      "the deterministic world matches the refactor baseline: #{digest}"
    )
  end

  def self.test_player_fusion_reverse_materials
    original_game_temp = $game_temp
    begin
      $game_temp = Game_Temp.new
      Game.load_sprites_list_caches
      Ironmon.reset_custom_fusion_pool_cache
      seed = 1_006_359_419
      range_examples = {
        [700, 700] => (700..750),
        [600, 560] => (600..635),
        [600, 559] => (600..635),
        [600, 550] => (592..633),
        [600, 500] => (563..625),
        [600, 200] => (520..610),
        [600, 100] => (520..608),
        [300, 100] => (260..310)
      }
      preview_mapper = Ironmon::PlayerFusionMapper.new(
        seed, Ironmon.custom_fusion_pool, {}, {},
        Ironmon::BaseStatGenerator.new(
          seed, Ironmon.base_stat_source_fingerprint
        )
      )
      range_examples.each do |materials, expected|
        actual = preview_mapper.send(
          :fusion_bst_range, materials[0], materials[1]
        )
        assert(
          actual == expected,
          "schema-5 fusion BST range for #{materials.inspect} is " +
            "#{expected.inspect}: #{actual.inspect}"
        )
      end
      mudkip = GameData::Species.get(:MUDKIP)
      mewtwo = GameData::Species.get(:MEWTWO)
      pairing_started = System.uptime
      forward = preview_mapper.species(mudkip, mewtwo)
      pairing_seconds = (System.uptime - pairing_started).to_f / 1_000_000.0
      @player_fusion_pairing_milliseconds =
        (pairing_seconds * 1_000).round
      reverse = preview_mapper.species(mewtwo, mudkip)
      assert(
        pairing_seconds < 15.0,
        "schema-5 global player-fusion pairing completes in seconds: " +
          "#{pairing_seconds.round(3)} seconds"
      )
      assert(
        preview_mapper.paired_species(forward) == reverse &&
          preview_mapper.paired_species(reverse) == forward,
        "schema-5 player orientations are the same global reverse pair"
      )
      blocked_seed = 1_851_036_422
      blocked_seed_mapper = Ironmon::PlayerFusionMapper.new(
        blocked_seed, Ironmon.custom_fusion_pool, {}, {},
        Ironmon::BaseStatGenerator.new(
          blocked_seed, Ironmon.base_stat_source_fingerprint
        )
      )
      blocked_seed_mapper.prepare
      assert(
        blocked_seed_mapper.paired_species(forward),
        "schema-5 repairs a blocked strength pairing before retrying the seed"
      )
      chained_seed = 116_872_428
      chained_mapper = Ironmon::PlayerFusionMapper.new(
        chained_seed, Ironmon.custom_fusion_pool_numbers, {}, {},
        Ironmon::BaseStatGenerator.new(
          chained_seed, Ironmon.base_stat_source_fingerprint
        )
      )
      chained_mapper.prepare
      pairs = chained_mapper.instance_variable_get(:@fusion_pairs)
      assert(
        pairs.flatten.uniq.length == Ironmon.custom_fusion_pool_numbers.length,
        "strength-chain repair covers every custom fusion exactly once"
      )
      maximum = chained_mapper.send(:maximum_fusion_range_width)
      assert(
        pairs.all? do |first, second|
          chained_mapper.send(:strength_pair_compatible?, first, second, maximum)
        end,
        "strength-chain repair retains BST and disjoint-component constraints"
      )
      broken_mapper = Ironmon::PlayerFusionMapper.new(
        seed, Ironmon.custom_fusion_pool_numbers.first(1), {}, {},
        Ironmon::BaseStatGenerator.new(seed, Ironmon.base_stat_source_fingerprint)
      )
      failure = nil
      2.times do
        begin
          broken_mapper.prepare
          assert(false, "an odd pool must fail preparation")
        rescue Ironmon::PlayerFusionMappingError => error
          assert(!failure || failure.equal?(error),
                 "failed preparation retains its original terminal error")
          failure = error
        end
      end
      assert(
        !broken_mapper.instance_variable_get(:@fusion_pool_ids) &&
          !broken_mapper.instance_variable_get(:@fusion_pairs),
        "failed preparation never publishes a partial usable pool"
      )
      failed_work = Ironmon::TrackerAreaFusionWork.new({})
      failed_work.instance_variable_set(:@attempts, 0)
      def failed_work.build_mapper
        @attempts += 1
        raise Ironmon::PlayerFusionMappingError, "invalid test pool"
      end
      2.times do
        begin
          failed_work.results_for([[:BULBASAUR, :CHARMANDER]])
          assert(false, "failed fusion page must not look complete")
        rescue Ironmon::TrackerLookupError => error
          assert(error.code == "fusion_lookup_failed",
                 "fusion preparation has a terminal lookup error")
        end
      end
      assert(failed_work.instance_variable_get(:@attempts) == 1,
             "opening a failed fusion page does not repeat its work")
      material_range = preview_mapper.send(
        :fusion_bst_range,
        preview_mapper.send(:normal_bst, mudkip),
        preview_mapper.send(:normal_bst, mewtwo)
      )
      [forward, reverse].each do |result|
        result_bst = preview_mapper.send(
          :target_bst, GameData::Species.get(result).id_number
        )
        assert(
          material_range.include?(result_bst),
          "schema-5 player-fusion result stays inside its rolled BST range"
        )
      end
      stat_probe = Ironmon::BaseStatGenerator.new(
        seed, Ironmon.base_stat_source_fingerprint
      )
      sample_step = [Ironmon.custom_fusion_pool.length / 8, 1].max
      Ironmon.custom_fusion_pool.each_with_index do |identity, index|
        next if (index % sample_step) != 0
        species = GameData::Species.get(identity)
        expected_bst = stat_probe.fusion_stats_for(species).values.inject(
          0
        ) { |sum, value| sum + value.to_i }
        expected_types = preview_mapper.send(
          :type_mask, [species.type1, species.type2].compact.uniq
        )
        assert(
          preview_mapper.send(:target_bst, species.id_number) == expected_bst &&
            preview_mapper.send(:target_type_mask, species.id_number) ==
              expected_types,
          "compact fusion pairing metrics match full fused-species data for " +
            identity.to_s
        )
      end
      indexed_mapper = Ironmon::PlayerFusionMapper.new(
        seed, Ironmon.custom_fusion_pool, {}, {},
        Ironmon::BaseStatGenerator.new(
          seed, Ironmon.base_stat_source_fingerprint
        ),
        Ironmon::PlayerFusionMapper::PREVIOUS_SCHEMA_VERSION, nil, true
      )
      indexed_mapper.prepare
      sample_ids = [1, 4, 7, 25, 94, 150, 251, 384, 493, NB_POKEMON]
      sample_ids.each_with_index do |first_id, first_index|
        sample_ids[first_index..-1].each do |second_id|
          pair = [first_id, second_id]
          indexed = indexed_mapper.send(:select_result_ids, pair)
          linear = indexed_mapper.send(:select_result_ids_linear, pair)
          assert(
            indexed == linear,
            "indexed player-fusion selection matches the schema-3 linear " +
              "reference for #{pair.inspect}: #{indexed.inspect} != " +
              linear.inspect
          )
        end
      end
      reverse_mapper = Ironmon::PlayerFusionMapper.new(
        seed, Ironmon.custom_fusion_pool, {}, {},
        Ironmon::BaseStatGenerator.new(
          seed, Ironmon.base_stat_source_fingerprint
        )
      )
      forward_materials = reverse_mapper.material_pairs_for(forward)
      assert(
        forward_materials.include?([mudkip.id_number, mewtwo.id_number]),
        "schema-5 reverse materials include the pair used by its preview"
      )
      assert(
        reverse_mapper.material_pairs_for(reverse).include?(
          [mewtwo.id_number, mudkip.id_number]
        ),
        "schema-5 reverse materials preserve the preview orientation"
      )
    ensure
      Ironmon.reset_custom_fusion_pool_cache
      $game_temp = original_game_temp
      Ironmon.reset_custom_fusion_pool_cache
    end
  end

  def self.test_preset_failure_rollback
    singleton = class << Ironmon; self; end
    original_global = $PokemonGlobal
    original_switches = $game_switches
    original_variables = $game_variables
    original_pool_service = Ironmon.instance_variable_get(
      :@custom_fusion_pool_service
    )
    singleton.send(
      :alias_method, :preset_test_original_prepare_base_stat_randomization,
      :prepare_base_stat_randomization
    )
    begin
      $PokemonGlobal = PokemonGlobalMetadata.new
      $PokemonGlobal.ironmon_mode = false
      $PokemonGlobal.ironmon_seed = 246_810
      $game_switches = []
      $game_variables = []
      $game_switches[15] = true
      $game_variables[16] = "preserved"
      pool_service = custom_fusion_pool_fixture
      Ironmon.instance_variable_set(
        :@custom_fusion_pool_service, pool_service
      )
      Ironmon.configuration = transaction_fixture["configuration"]
      transaction_global = $PokemonGlobal
      transaction_switches = $game_switches
      transaction_variables = $game_variables
      global_before = {}
      $PokemonGlobal.instance_variables.each do |name|
        next if !name.to_s.start_with?("@ironmon_")
        global_before[name] = Marshal.dump(
          $PokemonGlobal.instance_variable_get(name)
        )
      end
      switches_before = Marshal.dump($game_switches)
      variables_before = Marshal.dump($game_variables)
      singleton.send(:define_method, :prepare_base_stat_randomization) do
        @base_stat_randomization_error_message =
          "injected base-stat preparation failure"
        @preset_test_leaked_state = true
        false
      end

      result = Ironmon.apply_preset(:new_run, 135_791, false)
      assert(!result, "preset preparation failure is reported")
      assert(
        $PokemonGlobal.equal?(transaction_global) &&
          $game_switches.equal?(transaction_switches) &&
          $game_variables.equal?(transaction_variables),
        "preset failure preserves restored object identities"
      )
      global_after = {}
      $PokemonGlobal.instance_variables.each do |name|
        next if !name.to_s.start_with?("@ironmon_")
        global_after[name] = Marshal.dump(
          $PokemonGlobal.instance_variable_get(name)
        )
      end
      changed_global_fields = (global_before.keys + global_after.keys).uniq.select do |name|
        global_before[name] != global_after[name]
      end
      assert(
        changed_global_fields.empty?,
        "preset failure restores persistent global state: " +
          changed_global_fields.inspect
      )
      assert(
        Marshal.dump($game_switches) == switches_before,
        "preset failure restores game switches"
      )
      assert(
        Marshal.dump($game_variables) == variables_before,
        "preset failure restores game variables"
      )
      assert(
        !Ironmon.instance_variable_defined?(:@preset_test_leaked_state),
        "preset failure removes transient runtime state"
      )
      assert(
        Ironmon.generation_error_message ==
          "injected base-stat preparation failure",
        "preset failure retains its actionable error"
      )
    ensure
      singleton.send(
        :alias_method, :prepare_base_stat_randomization,
        :preset_test_original_prepare_base_stat_randomization
      )
      singleton.send(
        :remove_method, :preset_test_original_prepare_base_stat_randomization
      )
      Ironmon.suspend_ability_randomization
      Ironmon.suspend_base_stat_randomization
      Ironmon.suspend_evolution_randomization
      Ironmon.suspend_move_access_randomization
      Ironmon.suspend_item_randomization
      Ironmon.reset_species_generator_cache
      Ironmon.reset_player_fusion_mapper_cache
      Ironmon.instance_variable_set(
        :@custom_fusion_pool_service, original_pool_service
      )
      $PokemonGlobal = original_global
      $game_switches = original_switches
      $game_variables = original_variables
    end
  end

  def self.run
    fingerprint = Ironmon.tracker_compatibility_fingerprint(
      compatibility_fixture
    )
    assert(
      fingerprint ==
        "b505ea5986d3b34723f2d7e49b840e20468555b78da861e71335fb960ed0bac4",
      "bundled runtime matches the tracker compatibility golden vector"
    )
    ledger = {
      "current_attempt" => { "result" => "active", "attempt_number" => 4 },
      "attempts_abandoned" => 2,
      "last_completed_attempt" => nil,
      "last_completed_recipe" => nil
    }
    staged = Ironmon.stage_seed_import_ledger(ledger, { "result" => "abandoned" })
    assert(ledger["current_attempt"]["result"] == "active", "ledger staging is nonmutating")
    assert(staged["current_attempt"]["result"] == "abandoned", "staged attempt is abandoned")
    assert(staged["attempts_abandoned"] == 3, "staged abandonment is counted once")
    test_explicit_export
    test_strict_request_shapes
    test_transaction(true)
    test_transaction(false)
    test_preset_failure_rollback
    test_repeated_import_determinism
    test_player_fusion_reverse_materials
    File.binwrite(
      OUTPUT_PATH,
      "seeded-run import runtime tests passed\n" +
        "player_fusion_pairing_milliseconds=" +
        @player_fusion_pairing_milliseconds.to_i.to_s + "\n"
    )
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonSeededRunImportRuntimeTests.run
