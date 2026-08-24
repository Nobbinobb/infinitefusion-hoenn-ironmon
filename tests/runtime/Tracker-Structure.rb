module IronmonTrackerStructureRuntimeTests
  OUTPUT_PATH = $ironmon_tracker_structure_test_output_path.to_s

  def self.assert(condition, message)
    raise "Tracker structure test failed: #{message}" if !condition
  end

  def self.assert_source(callable, expected_file, description)
    location = callable.source_location
    assert(location, "#{description} exposes its source location")
    assert(
      File.basename(location[0]) == expected_file,
      "#{description} is defined by #{expected_file}"
    )
  end

  def self.run
    assert_source(
      Ironmon::TrackerConnection.instance_method(:update),
      "018_Tracker_Connection.rb",
      "tracker transport"
    )
    assert_source(
      Ironmon::TrackerConnection.instance_method(:handle_request),
      "018_Tracker_Connection_Requests.rb",
      "tracker request routing"
    )
    assert_source(
      Ironmon.method(:tracker_information_diagnostic_capabilities),
      "018_Tracker_Connection_Requests.rb",
      "tracker diagnostic information policy"
    )
    assert(
      Ironmon.tracker_information_diagnostic_capability("abilities") ==
        "pokemon.abilities",
      "tracker diagnostic information policy maps ability inspection"
    )
    assert(
      Ironmon.tracker_information_diagnostic_capabilities("evolutions") ==
        ["evolution.results", "evolution.candidates"],
      "tracker diagnostic information policy maps evolution inspection"
    )
    assert(
      Ironmon::TrackerConnection.private_instance_methods.include?(
        :handle_request
      ),
      "tracker request routing remains private"
    )
    assert_source(
      Ironmon.method(:start_tracker_run),
      "018_Tracker_Lifecycle.rb",
      "tracker lifecycle"
    )
    assert_source(
      Ironmon.method(:tracker_current_state),
      "018_Tracker_Snapshots.rb",
      "tracker snapshots"
    )
    assert_source(
      Ironmon.method(:begin_tracker_battle_command),
      "018_Tracker_User_Commands.rb",
      "tracker user commands"
    )
    assert_source(
      IronmonTrackerBattleHooks.instance_method(:pbStartBattle),
      "018_Tracker_Z_Runtime_Integration.rb",
      "tracker engine integration"
    )
    assert_source(
      Ironmon.method(:tracker_area_catalog),
      "010_Tracker_Area_Lookup.rb",
      "tracker area catalog"
    )
    assert_source(
      Ironmon.method(:tracker_area_lookup_summary),
      "010_Tracker_Area_Lookup_1_Requests.rb",
      "tracker area requests"
    )
    assert_source(
      Ironmon.method(:tracker_area_encounter_entries),
      "010_Tracker_Area_Lookup_2_Encounters.rb",
      "tracker area encounters"
    )
    assert_source(
      Ironmon.method(:tracker_area_trainer_entries),
      "010_Tracker_Area_Lookup_3_Trainers.rb",
      "tracker area trainers"
    )
    assert_source(
      Ironmon.method(:tracker_area_item_entries),
      "010_Tracker_Area_Lookup_4_Items.rb",
      "tracker area items"
    )
    assert_source(
      Ironmon.method(:complete_tracker_run),
      "019_Tracker_Post_Run.rb",
      "completed-run recipes"
    )
    assert_source(
      Ironmon.method(:tracker_validate_completed_recipe),
      "019_Tracker_Post_Run_1_Requests.rb",
      "completed-run request validation"
    )
    assert_source(
      Ironmon.method(:tracker_lookup_move_access),
      "019_Tracker_Post_Run_2_Pokemon_Lookup.rb",
      "completed-run Pokemon reconstruction"
    )
    assert_source(
      Ironmon.method(:tracker_evolution_candidates_for),
      "019_Tracker_Post_Run_3_Evolution_Lookup.rb",
      "completed-run evolution reconstruction"
    )
    assert_source(
      Ironmon.method(:tracker_lookup_evolution_predecessor_page),
      "019_Tracker_Post_Run_3_Evolution_Lookup.rb",
      "paged completed-run evolution predecessors"
    )
    assert_source(
      Ironmon.method(:tracker_lookup_wild_occurrences),
      "019_Tracker_Post_Run_4_Occurrence_Lookup.rb",
      "completed-run occurrence lookup"
    )
    assert_source(
      Ironmon.method(:tracker_obtainability),
      "019_Tracker_Post_Run_5_Obtainability.rb",
      "incremental completed-run obtainability"
    )
    assert(
      Ironmon::TrackerObtainabilityService::BACKGROUND_MILLISECONDS == 4.0 &&
        Ironmon::TrackerObtainabilityService::FOREGROUND_MILLISECONDS >
          Ironmon::TrackerObtainabilityService::BACKGROUND_MILLISECONDS,
      "obtainability preserves its background frame budget and faster foreground mode"
    )
    assert_source(
      Ironmon.method(:update_tracker_obtainability),
      "019_Tracker_Post_Run_5_Obtainability.rb",
      "obtainability cooperative scheduler"
    )
    assert_source(
      Ironmon.method(:tracker_obtainability_runtime_ready?),
      "019_Tracker_Post_Run_5_Obtainability.rb",
      "obtainability startup gate"
    )
    assert(
      Scene_Map.instance_method(:update).source_location[0].end_with?(
        "006_Scene_Hooks.rb"
      ),
      "map updates own the obtainability startup-ready signal"
    )
    ready_scene = Object.new
    Ironmon.mark_tracker_obtainability_map_ready(ready_scene)
    assert(
      Ironmon.instance_variable_get(
        :@tracker_obtainability_ready_scene
      ).equal?(ready_scene),
      "obtainability can be armed only for the map scene that finished updating"
    )
    Ironmon.pause_tracker_obtainability_for_map
    assert(
      Ironmon.instance_variable_get(
        :@tracker_obtainability_ready_scene
      ).nil?,
      "spriteset creation pauses obtainability until the new map has updated"
    )
    original_obtainability_services = Ironmon.instance_variable_get(
      :@tracker_obtainability_services
    )
    blocked_foreground_service = Object.new
    blocked_foreground_service.instance_variable_set(:@advance_calls, 0)
    def blocked_foreground_service.foreground_requested?
      return true
    end
    def blocked_foreground_service.foreground_deadline
      return 1.0
    end
    def blocked_foreground_service.advance_for_milliseconds(_milliseconds)
      @advance_calls += 1
    end
    Ironmon.instance_variable_set(
      :@tracker_obtainability_services,
      { "blocked-startup" => blocked_foreground_service }
    )
    Ironmon.update_tracker_obtainability
    assert(
      blocked_foreground_service.instance_variable_get(:@advance_calls) == 0,
      "foreground obtainability cannot consume startup frames before map readiness"
    )
    Ironmon.instance_variable_set(
      :@tracker_obtainability_services, original_obtainability_services
    )
    acquisition_adapters =
      Ironmon::TrackerObtainabilityService::SCRIPTED_ACQUISITION_METHODS +
      Ironmon::TrackerObtainabilityService::DEFERRED_ACQUISITION_METHODS
    assert(
      acquisition_adapters.uniq.length == acquisition_adapters.length,
      "obtainability acquisition adapters have unique ownership"
    )
    obtainability = Ironmon::TrackerObtainabilityService.allocate
    obtainability.instance_variable_set(
      :@plans, Hash.new { |hash, key| hash[key] = [] }
    )
    obtainability.instance_variable_set(:@fusion_evolution_queue, [])
    obtainability.instance_variable_set(:@queued_fusion_evolutions, {})
    expensive_plan = {
      :items => { :MOONSTONE => 2 },
      :constraints => { :starter => 0 },
      :source_uses => { "starter:0" => 1 },
      :reason => "expensive",
      :path => ["expensive"]
    }
    cheap_plan = {
      :items => { :MOONSTONE => 1 },
      :constraints => { :starter => 0 },
      :source_uses => { "starter:0" => 1 },
      :reason => "cheap",
      :path => ["cheap"]
    }
    assert(
      obtainability.send(:add_plan, :BULBASAUR, expensive_plan),
      "obtainability accepts an initial witness plan"
    )
    assert(
      obtainability.send(:add_plan, :BULBASAUR, cheap_plan),
      "obtainability accepts a resource-cheaper witness plan"
    )
    assert(
      obtainability.instance_variable_get(:@plans)[:BULBASAUR] == [cheap_plan],
      "obtainability removes resource-dominated witness plans"
    )
    assert(
      !obtainability.send(
        :compatible_constraints?, { :starter => 0 }, { :starter => 1 }
      ),
      "obtainability cannot combine mutually exclusive starter choices"
    )
    obtainability.instance_variable_set(:@resource_supply, { :MOONSTONE => 1 })
    assert(
      obtainability.send(
        :within_supply?,
        obtainability.send(
          :merge_counts, { :MOONSTONE => 1 }, { :MOONSTONE => 1 }
        )
      ) == false,
      "obtainability enforces cumulative evolution-item quantities"
    )
    original_game_temp = $game_temp
    $game_temp = Game_Temp.new
    Game.load_sprites_list_caches
    Ironmon.reset_custom_fusion_pool_cache
    evolution_metadata = Ironmon.evolution_metadata_values
    obtainability_recipe = {
      "run_id" => "runtime-obtainability",
      "seed" => 1_187_411_801,
      "configuration" => Ironmon::Configuration.new.to_h,
      "data_mode" => "classic",
      "species_generator_version" => Ironmon::SpeciesGenerator::SCHEMA_VERSION,
      "base_stat_source_fingerprint" => Ironmon.base_stat_source_fingerprint,
      "player_fusion_generator_version" =>
        Ironmon::PlayerFusionMapper::SCHEMA_VERSION,
      "evolution_generator_version" =>
        evolution_metadata[:ironmon_evolution_generator_version],
      "evolution_rules_version" =>
        evolution_metadata[:ironmon_evolution_rules_version],
      "evolution_source_fingerprint" =>
        evolution_metadata[:ironmon_evolution_source_fingerprint],
      "evolution_taxonomy_fingerprint" =>
        evolution_metadata[:ironmon_evolution_taxonomy_fingerprint],
      "evolution_method_fingerprint" =>
        evolution_metadata[:ironmon_evolution_method_fingerprint],
      "evolution_target_fingerprint" =>
        evolution_metadata[:ironmon_evolution_target_fingerprint],
      "evolution_base_stat_generator_version" =>
        evolution_metadata[:ironmon_evolution_base_stat_generator_version],
      "evolution_base_stat_source_fingerprint" =>
        evolution_metadata[:ironmon_evolution_base_stat_source_fingerprint],
      "fusion_evolution_generator_version" =>
        evolution_metadata[:ironmon_evolution_fusion_generator_version],
      "fusion_evolution_rules_version" =>
        evolution_metadata[:ironmon_evolution_fusion_rules_version],
      "fusion_evolution_target_pool_version" =>
        evolution_metadata[:ironmon_evolution_fusion_target_pool_version],
      "fusion_evolution_target_pool_size" =>
        evolution_metadata[:ironmon_evolution_fusion_target_pool_size],
      "fusion_evolution_target_pool_fingerprint" =>
        evolution_metadata[:ironmon_evolution_fusion_target_pool_fingerprint],
      "item_generator" => {
        "rules_version" => Ironmon::ItemSlotGenerator::POOL_RULES_VERSION
      },
      "item_mappings" => {},
      "tm_mappings" => {}
    }
    deferred_obtainability = Ironmon::TrackerObtainabilityService.new(
      obtainability_recipe, true
    )
    assert(
      deferred_obtainability.instance_variable_get(:@configuration).nil? &&
        deferred_obtainability.snapshot["phase"] == "prepare_generators",
      "startup requests defer obtainability configuration and generator setup"
    )
    deferred_obtainability.instance_variable_set(:@phase, :authored_sources)
    background_terminal_snapshot = deferred_obtainability.snapshot
    assert(
      background_terminal_snapshot["background_complete"] == true &&
        background_terminal_snapshot["complete"] == false,
      "foreground-only authored sources terminate background polling"
    )
    integrated_obtainability =
      Ironmon::TrackerObtainabilityService.new(obtainability_recipe)
    initial_obtainability = integrated_obtainability.snapshot
    assert(
      initial_obtainability["total_pairs"] > 0 &&
        initial_obtainability["obtainable_count"] > 0 &&
        initial_obtainability["fusion_mapping_mode"] ==
          "waiting_for_tracker",
      "obtainability initializes deterministic run sources and material work"
    )
    passive_target = Ironmon.custom_fusion_pool.find do |identity|
      plans = integrated_obtainability.instance_variable_get(:@plans)
      !plans.key?(identity) || plans[identity].empty?
    end
    passive_snapshot = integrated_obtainability.passive_target_snapshot(
      passive_target
    )
    assert(
      passive_snapshot["status"] == "calculating" &&
        !integrated_obtainability.instance_variable_get(
          :@full_fusion_closure_requested
        ),
      "passive Pokemon-card status does not start the complete fusion " +
        "evolution fallback needed only by an explicit target check"
    )
    unresolved_sources = integrated_obtainability.instance_variable_get(
      :@unresolved_sources
    )
    assert(
      unresolved_sources.include?(
        "caught-fusion random component acquisition order"
      ),
      "obtainability does not expose both random-unfusion components as proven"
    )
    integrated_obtainability.disable_tracker_mapping_worker
    integrated_obtainability.send(:advance_work_unit)
    warmed_obtainability = integrated_obtainability.snapshot
    assert(
      warmed_obtainability["processed_pairs"] == 0,
      "obtainability isolates fusion-evolution warm-up from material work"
    )
    integrated_obtainability.send(:advance_work_unit)
    advanced_obtainability = integrated_obtainability.snapshot
    assert(
      advanced_obtainability["processed_pairs"] == 1,
      "obtainability advances one player-fusion mapping work unit"
    )
    integrated_obtainability.instance_variable_set(
      :@tracker_mapping_worker_disabled, false
    )
    mapping_work = integrated_obtainability.snapshot["fusion_mapping_work"]
    pair_first = integrated_obtainability.instance_variable_get(:@pair_first)
    pair_second = integrated_obtainability.instance_variable_get(:@pair_second)
    material_ids = integrated_obtainability.instance_variable_get(:@material_ids)
    first_material = material_ids[pair_first]
    second_material = material_ids[pair_second]
    mapped_results = integrated_obtainability.instance_variable_get(
      :@fusion_mapper
    ).species_pair(first_material, second_material)
    mapped_result_number = GameData::Species.get(mapped_results[0]).id_number
    assert(
      Ironmon.fusion_species_identity(mapped_result_number) ==
        mapped_results[0] &&
        Ironmon.custom_fusion_species?(mapped_result_number),
      "tracker mapping validates numeric custom-fusion identities without " +
        "requiring a materialized fusion object"
    )
    mapped_entry = [
      GameData::Species.get(first_material).id_number,
      GameData::Species.get(second_material).id_number,
      mapped_result_number,
      GameData::Species.get(mapped_results[1]).id_number
    ]
    mapped_entry_offset = mapping_work["processed_pairs"]
    integrated_obtainability.apply_fusion_mapping_batch({
      "job_id" => mapping_work["job_id"],
      "offset" => mapped_entry_offset,
      "packed_pairs" => mapped_entry
    })
    integrated_obtainability.send(:advance_work_unit)
    assert(
      integrated_obtainability.snapshot["processed_pairs"] == 2,
      "obtainability validates and applies an ordered tracker mapping batch"
    )
    next_first_index = integrated_obtainability.instance_variable_get(
      :@pair_first
    )
    next_second_index = integrated_obtainability.instance_variable_get(
      :@pair_second
    )
    next_first_material = material_ids[next_first_index]
    next_second_material = material_ids[next_second_index]
    next_mapped_results = integrated_obtainability.instance_variable_get(
      :@fusion_mapper
    ).species_pair(next_first_material, next_second_material)
    next_mapped_entry = [
      GameData::Species.get(next_first_material).id_number,
      GameData::Species.get(next_second_material).id_number,
      GameData::Species.get(next_mapped_results[0]).id_number,
      GameData::Species.get(next_mapped_results[1]).id_number
    ]
    integrated_obtainability.apply_fusion_mapping_batch({
      "job_id" => mapping_work["job_id"],
      "offset" => mapped_entry_offset,
      "packed_pairs" => mapped_entry + next_mapped_entry
    }, true)
    overlap_snapshot = integrated_obtainability.snapshot
    assert(
      overlap_snapshot["processed_pairs"] == mapped_entry_offset + 2,
      "obtainability resumes the unapplied suffix of an overlapping " +
        "background/foreground tracker batch"
    )
    integrated_obtainability.apply_fusion_mapping_batch({
      "job_id" => mapping_work["job_id"],
      "offset" => mapped_entry_offset,
      "packed_pairs" => mapped_entry
    }, true)
    assert(
      integrated_obtainability.snapshot["processed_pairs"] ==
        overlap_snapshot["processed_pairs"],
      "obtainability safely ignores a fully applied in-flight tracker batch"
    )
    obtainable_plan_count = integrated_obtainability.instance_variable_get(
      :@plans
    ).count { |_identity, plans| !plans.empty? }
    assert(
      integrated_obtainability.snapshot["obtainable_count"] ==
        obtainable_plan_count,
      "obtainability maintains its progress count without rescanning every " +
        "species for each tracker snapshot"
    )
    dead_fiber = Fiber.new {}
    dead_fiber.resume
    integrated_obtainability.instance_variable_set(:@work_fiber, dead_fiber)
    integrated_obtainability.disable_tracker_mapping_worker
    integrated_obtainability.advance_for_milliseconds(1.0)
    replacement_fiber = integrated_obtainability.instance_variable_get(
      :@work_fiber
    )
    assert(
      replacement_fiber.nil? || !replacement_fiber.equal?(dead_fiber),
      "obtainability replaces a terminated cooperative worker instead of " +
        "remaining permanently calculating"
    )
    possible_evolution_edges = integrated_obtainability.instance_variable_get(
      :@possible_evolution_edges
    ).keys
    possible_edge_parts = possible_evolution_edges.first.to_s.split(">", 2)
    possible_edge_source = GameData::Species.get(
      possible_edge_parts[0].split(":", 2)[0].to_sym
    )
    graph_target = Ironmon.tracker_lookup_evolution_targets(
      possible_edge_source, obtainability_recipe
    ).values.flatten.find do |target|
      target["species_id"] == possible_edge_parts[1]
    end
    graph_edge_key = graph_target ?
      "#{possible_edge_source.id}:0>#{graph_target["species_id"]}" : ""
    integrated_obtainability.instance_variable_set(:@phase, :complete)
    complete_edge_snapshot = integrated_obtainability.snapshot(
      nil, [], [graph_edge_key, "unproven:0>edge:0"]
    )
    assert(
      !possible_evolution_edges.empty? &&
        graph_target && complete_edge_snapshot["complete"] &&
        complete_edge_snapshot["obtainable_evolution_edge_keys"] == [
          graph_edge_key
        ],
      "completed obtainability preserves real graph-formatted executable " +
        "routes (internal=#{possible_evolution_edges.first.inspect}, " +
        "graph=#{graph_edge_key.inspect}, target=#{graph_target.inspect}, " +
        "returned=#{complete_edge_snapshot["obtainable_evolution_edge_keys"].inspect})"
    )
    registered_edge_count = integrated_obtainability.instance_variable_get(
      :@requested_evolution_edge_keys
    ).length
    integrated_obtainability.snapshot(nil, [], [graph_edge_key])
    assert(
      integrated_obtainability.instance_variable_get(
        :@requested_evolution_edge_keys
      ).length == registered_edge_count,
      "repeated graph polls skip already registered evolution edges before " +
        "resolving their fused species"
    )
    assert_source(
      Ironmon.method(:reset_tracker_post_run_cache),
      "019_Tracker_Post_Run_6_Fusion_Caches_And_Runtime.rb",
      "completed-run fusion and cache utilities"
    )
    assert_source(
      Ironmon.method(:tracker_debug_inspect_pokemon),
      "020_Tracker_Debug.rb",
      "diagnostic current-Pokemon inspection"
    )
    assert_source(
      Ironmon.method(:tracker_debug_pokemon_search),
      "020_Tracker_Debug_1_Requests.rb",
      "diagnostic lookup requests"
    )
    assert_source(
      Ironmon.method(:tracker_debug_evolution_predecessor_search),
      "020_Tracker_Debug_1_Requests.rb",
      "diagnostic evolution predecessor requests"
    )
    assert_source(
      Ironmon.method(:tracker_debug_obtainability),
      "020_Tracker_Debug_1_Requests.rb",
      "diagnostic active-run obtainability"
    )
    assert(
      Ironmon::TRACKER_OBTAINABILITY_DIAGNOSTIC_CAPABILITIES.all? do |capability|
        Ironmon::TRACKER_DIAGNOSTIC_CAPABILITIES.include?(capability)
      end,
      "active-run obtainability uses only negotiated diagnostic capabilities"
    )
    original_global = $PokemonGlobal
    begin
      $PokemonGlobal = PokemonGlobalMetadata.new
      $PokemonGlobal.ironmon_mode = true
      $PokemonGlobal.ironmon_seed = obtainability_recipe["seed"]
      $PokemonGlobal.ironmon_run_id = "runtime-active-obtainability"
      active_recipe = Ironmon.tracker_debug_active_recipe
      assert(
        active_recipe["active_run"] == true &&
          Ironmon.tracker_loaded_recipe?(active_recipe),
        "diagnostic obtainability identifies the currently loaded run"
      )
      assert(
        Ironmon.tracker_normal_evolution_generator(active_recipe).equal?(
          Ironmon.evolution_generator
        ) &&
          Ironmon.tracker_fusion_evolution_generator(active_recipe).equal?(
            Ironmon.fusion_evolution_generator
        ),
        "diagnostic obtainability reuses the loaded evolution generators"
      )
      Ironmon.mark_tracker_obtainability_map_ready(Object.new)
      Ironmon.reset_tracker_post_run_cache
      assert(
        Ironmon.instance_variable_get(
          :@tracker_obtainability_ready_scene
        ).nil?,
        "run transitions clear the obtainability map-ready signal"
      )
      active_obtainability = Ironmon.tracker_obtainability_for_recipe(
        { "foreground" => true }, active_recipe
      )
      active_service = Ironmon.tracker_obtainability_services[
        active_recipe["run_id"]
      ]
      assert(
        active_obtainability["processed_pairs"] == 0 &&
          active_obtainability["obtainable_species_ids"].empty? &&
          active_obtainability["obtainable_evolution_edge_keys"].empty?,
        "diagnostic obtainability accepts an omitted optional species without returning the full catalog"
      )
      assert(
        active_service.foreground_requested?,
        "diagnostic obtainability requests renew foreground calculation priority"
      )
      active_service.send(:advance_work_unit)
      assert(
        !active_service.instance_variable_get(:@fusion_mapper).equal?(
          Ironmon.player_fusion_mapper
        ),
        "obtainability keeps speculative fusion mappings out of live gameplay state"
      )
      assert(
        !active_service.instance_variable_get(:@fusion_generator).equal?(
          Ironmon.fusion_evolution_generator
        ),
        "obtainability keeps cooperative fusion generation out of live gameplay caches"
      )
    ensure
      $PokemonGlobal = original_global
    end
    assert_source(
      Ironmon.method(:tracker_validate_debug_context),
      "020_Tracker_Debug_2_Context_And_Authorization.rb",
      "diagnostic authorization"
    )
    assert_source(
      Ironmon.method(:tracker_debug_resolve_pokemon),
      "020_Tracker_Debug_3_Pokemon_Snapshots.rb",
      "diagnostic Pokemon snapshots"
    )
    $game_temp = original_game_temp
    File.binwrite(OUTPUT_PATH, "tracker structure tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonTrackerStructureRuntimeTests.run
