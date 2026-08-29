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
    transport_server = TCPServer.new(Ironmon::TRACKER_HOST, 0)
    transport_client = TCPSocket.new(
      Ironmon::TRACKER_HOST, transport_server.addr[1]
    )
    transport_peer = transport_server.accept
    begin
      transport_connection = Ironmon::TrackerConnection.new
      transport_connection.instance_variable_set(:@socket, transport_client)
      transport_output = "x" * (Ironmon::TRACKER_OUTPUT_WRITE_BYTES + 1)
      transport_connection.instance_variable_set(
        :@output_buffer, transport_output
      )
      offered_output_bytes = nil
      transport_client.define_singleton_method(:write_nonblock) do |output, exception: true|
        offered_output_bytes = output.bytesize
        :wait_writable
      end
      transport_connection.send(:flush_output)
      assert(
        offered_output_bytes == Ironmon::TRACKER_OUTPUT_WRITE_BYTES &&
          transport_connection.instance_variable_get(:@output_buffer) ==
            transport_output,
        "tracker transport never blocks the game thread on a full socket"
      )
      transport_client.define_singleton_method(:write_nonblock) do |output, exception: true|
        output.bytesize / 2
      end
      transport_connection.send(:flush_output)
      assert(
        transport_connection.instance_variable_get(:@output_buffer).bytesize ==
          transport_output.bytesize - Ironmon::TRACKER_OUTPUT_WRITE_BYTES / 2,
        "tracker transport preserves output after a partial nonblocking write"
      )
    ensure
      transport_client.close
      transport_peer.close
      transport_server.close
    end
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
      Ironmon.method(:tracker_active_fusion_assignment_recipe),
      "018_Tracker_Snapshots.rb",
      "tracker early fusion evolution recipe"
    )
    assert_source(
      Ironmon.method(:begin_tracker_battle_command),
      "018_Tracker_User_Commands.rb",
      "tracker user commands"
    )
    assert_source(
      Ironmon.method(:tracker_resolved_sprite_path),
      "023_Sprite_Performance_Fixes.rb",
      "tracker sprite materialization"
    )
    original_live_game_temp = $game_temp
    original_live_pokemon_temp = $PokemonTemp
    begin
      $game_temp = Game_Temp.new
      $PokemonTemp = PokemonTemp.new
      live_pokemon = Pokemon.new(:B1H2, 5)
      live_pokemon.pif_sprite = nil
      first_live_sprite = Ironmon.tracker_live_pif_sprite(live_pokemon)
      second_live_sprite = Ironmon.tracker_live_pif_sprite(live_pokemon)
      assert(
        first_live_sprite && first_live_sprite.equal?(second_live_sprite) &&
          live_pokemon.pif_sprite.equal?(first_live_sprite),
        "tracker snapshots retain one sprite variant after a live fusion"
      )
      stale_live_sprite = PIFSprite.new(:BASE, 1, nil, "")
      live_pokemon.pif_sprite = stale_live_sprite
      repaired_live_sprite = Ironmon.tracker_live_pif_sprite(live_pokemon)
      assert(
        !repaired_live_sprite.equal?(stale_live_sprite) &&
          repaired_live_sprite.species == live_pokemon.species_data.species &&
          live_pokemon.pif_sprite.equal?(repaired_live_sprite),
        "tracker snapshots replace a sprite left stale by a species change"
      )
    ensure
      $game_temp = original_live_game_temp
      $PokemonTemp = original_live_pokemon_temp
    end
    assert_source(
      Ironmon.method(:with_tracker_starter_sprite),
      "023_Tracker_Starter_Selection.rb",
      "stable tracker starter sprite selection"
    )
    starter_sprite = PIFSprite.new(:CUSTOM, 1, 2, "a")
    starter_sprite_key = [1, 2]
    original_pokemon_system = $PokemonSystem
    $PokemonSystem = PokemonSystem.new if !$PokemonSystem
    $PokemonSystem.alt_sprite_substitutions ||= {}
    substitutions = $PokemonSystem.alt_sprite_substitutions
    previous_selection_present = Ironmon.instance_variable_defined?(
      :@tracker_starter_selection
    )
    previous_selection = Ironmon.instance_variable_get(
      :@tracker_starter_selection
    )
    previous_substitution_present = substitutions.key?(starter_sprite_key)
    previous_substitution = substitutions[starter_sprite_key]
    begin
      Ironmon.instance_variable_set(
        :@tracker_starter_selection,
        { :sprites => [starter_sprite] }
      )
      yielded_sprite = Ironmon.with_tracker_starter_sprite(0) do
        substitutions[starter_sprite_key]
      end
      assert(
        yielded_sprite.equal?(starter_sprite),
        "starter graphics reuse the tracker-selected sprite variant"
      )
      assert(
        substitutions.key?(starter_sprite_key) ==
          previous_substitution_present &&
          substitutions[starter_sprite_key].equal?(previous_substitution),
        "temporary starter sprite substitution restores prior game state"
      )
    ensure
      if previous_selection_present
        Ironmon.instance_variable_set(
          :@tracker_starter_selection, previous_selection
        )
      else
        Ironmon.remove_instance_variable(:@tracker_starter_selection) if
          Ironmon.instance_variable_defined?(:@tracker_starter_selection)
      end
      if previous_substitution_present
        substitutions[starter_sprite_key] = previous_substitution
      else
        substitutions.delete(starter_sprite_key)
      end
      $PokemonSystem = original_pokemon_system
    end
    manual_sprite = PIFSprite.new(:AUTOGEN, 1, 1, "")
    manual_sprite.local_path = Settings::DEFAULT_SPRITE_PATH
    manual_sprite_path = Ironmon.tracker_resolved_sprite_path(manual_sprite)
    assert(
      manual_sprite_path && File.file?(manual_sprite_path),
      "tracker sprites preserve an existing game-local individual image"
    )
    materialized_sprite = PIFSprite.new(:AUTOGEN, 1, 0, "")
    materialized_sprite.local_path =
      "Graphics/CustomBattlers/local_sprites/indexed/missing.png"
    test_sprite_cache_path = Ironmon.tracker_materialized_sprite_path(
      materialized_sprite
    )
    File.delete(test_sprite_cache_path) if File.file?(test_sprite_cache_path)
    begin
      materialized_sprite_path = Ironmon.tracker_resolved_sprite_path(
        materialized_sprite
      )
      assert(
        materialized_sprite_path == test_sprite_cache_path &&
          File.file?(materialized_sprite_path),
        "tracker sprites recover from a stale local path through the game loader"
      )
    ensure
      File.delete(test_sprite_cache_path) if File.file?(test_sprite_cache_path)
      cache_folder = Ironmon::TRACKER_SPRITE_CACHE_FOLDER
      Dir.rmdir(cache_folder) if
        Dir.exist?(cache_folder) && Dir.children(cache_folder).empty?
    end
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
    source_catalog = Ironmon.tracker_obtainability_source_catalog
    assert(
      source_catalog["schema_version"] == 1 &&
        !source_catalog["fingerprint"].to_s.empty? &&
        !source_catalog["sources"].empty? &&
        !source_catalog["resources"].empty?,
      "the generated semantic obtainability source catalog is loadable"
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
      Ironmon.method(:tracker_fusion_evolution_targets_for_assignments),
      "019_Tracker_Post_Run_3_Evolution_Lookup.rb",
      "native fusion evolution target formatting"
    )
    assert_source(
      Ironmon.method(:tracker_fusion_evolution_candidates_for_assignments),
      "019_Tracker_Post_Run_3_Evolution_Lookup.rb",
      "native fusion evolution candidate formatting"
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
      Ironmon::TrackerObtainabilityService::BACKGROUND_MILLISECONDS == 6.0 &&
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
    assert(
      obtainability.send(
        :decode_tracker_executable_edges, "AYEBqgE="
      ) == [1, 130, 300],
      "obtainability decodes the tracker's compact delta-varint edge index"
    )
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
      background_terminal_snapshot["background_complete"] == false &&
        background_terminal_snapshot["complete"] == false,
      "authored source adapters remain inside bounded background calculation"
    )
    failed_obtainability = Ironmon::TrackerObtainabilityService.new(
      obtainability_recipe, true
    )
    failed_obtainability.instance_variable_set(:@phase, :failed)
    failed_obtainability.instance_variable_set(
      :@failure_message, "Deliberate obtainability adapter failure."
    )
    failure = begin
      failed_obtainability.snapshot
      nil
    rescue Ironmon::TrackerLookupError => error
      error
    end
    assert(
      failure && failure.code == "obtainability_incomplete",
      "an unclassified acquisition adapter fails explicitly instead of " +
        "leaving Pokemon permanently calculating"
    )
    first_archive_recipe = obtainability_recipe.merge(
      "run_id" => "tracker-structure-archive-a"
    )
    second_archive_recipe = obtainability_recipe.merge(
      "run_id" => "tracker-structure-archive-b"
    )
    first_archive_service =
      Ironmon.tracker_obtainability_service(first_archive_recipe)
    second_archive_service =
      Ironmon.tracker_obtainability_service(second_archive_recipe)
    assert(
      !first_archive_service.equal?(second_archive_service) &&
        Ironmon.tracker_obtainability_services[first_archive_recipe["run_id"]]
          .equal?(first_archive_service) &&
        Ironmon.tracker_obtainability_services[second_archive_recipe["run_id"]]
          .equal?(second_archive_service),
      "archived obtainability services are isolated by completed-run identity"
    )
    Ironmon.tracker_obtainability_services.delete(first_archive_recipe["run_id"])
    Ironmon.tracker_obtainability_services.delete(second_archive_recipe["run_id"])
    integrated_obtainability =
      Ironmon::TrackerObtainabilityService.new(obtainability_recipe)
    initial_obtainability = integrated_obtainability.snapshot
    closure_work = initial_obtainability["fusion_closure_work"]
    assert(
      !initial_obtainability["complete"] &&
        !initial_obtainability["background_complete"] &&
        initial_obtainability["total_pairs"] > 0 &&
        initial_obtainability["processed_pairs"] == 0 &&
        initial_obtainability["obtainable_count"] > 0 &&
        initial_obtainability["unresolved_source_count"] == 0 &&
        initial_obtainability["unresolved_resource_count"] == 0 &&
        closure_work.is_a?(Hash) &&
        closure_work["direct_pair_offsets"].is_a?(Array) &&
        closure_work["reversible_fusion_ids"].is_a?(Array) &&
        closure_work["source_catalog_fingerprint"] ==
          source_catalog["fingerprint"],
      "obtainability classifies every audited source and resource before " +
        "handing the direct-result index to the parallel tracker worker"
    )
    maximum_fusion_number = (NB_POKEMON * NB_POKEMON) + NB_POKEMON
    integrated_obtainability.apply_fusion_closure_result({
      "job_id" => initial_obtainability["fusion_closure_work"]["job_id"],
      "obtainable_fusion_words" =>
        Array.new((maximum_fusion_number >> 5) + 1, 0),
      "packed_executable_evolution_edges" => "",
      "obtainable_count" => integrated_obtainability.instance_variable_get(
        :@plans
      ).count { |_identity, plans| !plans.empty? }
    })
    lazy_evolution_entry = integrated_obtainability.instance_variable_get(
      :@plans
    ).find do |_identity, plans|
      plans.any? do |plan|
        witness = plan[:bulk_witness]
        witness && witness[0] == :evolution
      end
    end
    lazy_evolution_plan = lazy_evolution_entry[1].find do |plan|
      witness = plan[:bulk_witness]
      witness && witness[0] == :evolution
    end if lazy_evolution_entry
    lazy_evolution_path = integrated_obtainability.send(
      :materialize_plan_path, lazy_evolution_plan
    ) if lazy_evolution_plan
    assert(
      lazy_evolution_path &&
        lazy_evolution_path.any? { |step| step.start_with?("Evolve into ") },
      "deferred evolution witnesses materialize a readable proof path on demand"
    )
    fusion_literal = integrated_obtainability.send(
      :literal_species, "fusionOf(:MAGCARGO,:GRAVELER)"
    )
    assert(
      fusion_literal == GameData::Species.get(
        fusionOf(:MAGCARGO, :GRAVELER)
      ).id,
      "authored fusionOf acquisition expressions preserve their Head/Body " +
        "orientation"
    )
    fossil_page = load_data("Data/Map048.rxdata").events[87].pages[1]
    fossil_scripts = integrated_obtainability.send(
      :script_chunks, fossil_page.list
    ).map { |_index, script| script }
    assert(
      fossil_scripts.any? { |script| script.include?("pbAddToParty") } &&
        fossil_scripts.any? { |script| script.include?("pbAddPokemon") },
      "authored acquisition calls embedded in event conditions are parsed"
    )
    passive_target = Ironmon.custom_fusion_pool.find do |identity|
      plans = integrated_obtainability.instance_variable_get(:@plans)
      !plans.key?(identity) || plans[identity].empty?
    end
    passive_snapshot = integrated_obtainability.passive_target_snapshot(
      passive_target
    )
    assert(
      passive_snapshot["status"] == "unobtainable" &&
        integrated_obtainability.snapshot["phase"] == "complete" &&
        integrated_obtainability.snapshot["background_complete"],
      "completed run-wide closure answers a passive Pokemon-card status " +
        "without queuing target-specific work"
    )
    obtainability_services = Ironmon.tracker_obtainability_services
    previous_integrated_service = obtainability_services[
      obtainability_recipe["run_id"]
    ]
    obtainability_services[obtainability_recipe["run_id"]] =
      integrated_obtainability
    Ironmon.instance_variable_set(:@tracker_search_indexes, nil)
    Ironmon.instance_variable_set(:@tracker_search_result_cache, nil)
    cold_search_started = System.uptime
    cold_search_response = Ironmon.tracker_pokemon_search_for_recipe(
      {
        "query" => "beevee",
        "offset" => 0,
        "limit" => 20,
        "normal_only" => false
      },
      obtainability_recipe
    )
    cold_search_seconds =
      (System.uptime - cold_search_started).to_f / 1_000_000.0
    @cold_fusion_search_milliseconds = (cold_search_seconds * 1_000).round
    assert(
      cold_search_seconds < 5.0,
      "a cold full-pool Pokemon search completes without blocking tracker " +
        "transport: #{cold_search_seconds.round(3)} seconds"
    )
    cold_search_matches = cold_search_response["matches"]
    cold_search_matches_valid = cold_search_matches.all? do |match|
      match["species_name"].downcase.include?("beevee") &&
        !match["obtainability_status"].to_s.empty?
    end
    assert(
      cold_search_response["total"].to_i > 0 &&
        !cold_search_matches.empty? &&
        cold_search_matches.length <= 20 &&
        cold_search_matches_valid,
      "the exact cold beevee search returns annotated, paged fusion rows"
    )
    search_species = GameData::Species.get(Ironmon.normal_species_pool.first)
    search_response = Ironmon.tracker_pokemon_search_for_recipe(
      {
        "query" => search_species.name,
        "offset" => 0,
        "limit" => 50
      },
      obtainability_recipe
    )
    search_match = search_response["matches"].find do |match|
      match["species_id"] == "#{search_species.id}:0"
    end
    expected_search_status = integrated_obtainability.passive_target_snapshot(
      search_species
    )["status"]
    assert(
      search_match &&
        search_match["obtainability_status"] == expected_search_status,
      "Pokemon search attaches the shared request-scoped obtainability state"
    )
    hidden_search_response = Ironmon.tracker_pokemon_search_for_recipe(
      {
        "query" => search_species.name,
        "offset" => 0,
        "limit" => 50
      },
      obtainability_recipe,
      { :obtainability => false }
    )
    hidden_search_omits_status =
      hidden_search_response["matches"].all? do |match|
        !match.key?("obtainability_status")
      end
    assert(
      hidden_search_omits_status,
      "Pokemon search omits passive obtainability outside its authorized " +
        "information domain"
    )
    relation_snapshot = Ironmon.tracker_lookup_relation(
      search_species, "Test relation", obtainability_recipe, true
    )
    assert(
      relation_snapshot["obtainability_status"] == expected_search_status,
      "reusable Pokemon relation cards receive the same passive status"
    )
    unresolved_sources = integrated_obtainability.instance_variable_get(
      :@unresolved_sources
    )
    direct_caught_fusions = integrated_obtainability.instance_variable_get(
      :@direct_caught_fusions
    )
    caught_fusion_identity = direct_caught_fusions.keys.first
    caught_fusion = GameData::Species.get(caught_fusion_identity) if
      caught_fusion_identity
    caught_components = caught_fusion ? [
      caught_fusion.body_pokemon.id, caught_fusion.head_pokemon.id
    ] : []
    component_plans = integrated_obtainability.instance_variable_get(:@plans)
    assert(
      !caught_fusion_identity.nil? && unresolved_sources.empty? &&
        caught_components.all? do |identity|
          component_plans[identity].any? do |plan|
            plan[:reason] == "Caught-fusion random-component unfusion"
          end
        end,
      "random-component unfusion records both acquisition-order outcomes as " +
        "exclusive proof alternatives"
    )
    material_ids = integrated_obtainability.instance_variable_get(:@material_ids)
    first_material = material_ids[0]
    second_material = material_ids[1]
    mapping_probe = Ironmon.tracker_obtainability_fusion_mapper(
      obtainability_recipe
    )
    mapped_results = mapping_probe.species_pair(first_material, second_material)
    mapped_result = GameData::Species.get(mapped_results[0])
    first_material_id = GameData::Species.get(first_material).id_number
    second_material_id = GameData::Species.get(second_material).id_number
    material_page = Ironmon.tracker_fusion_material_search_for_recipe(
      {
        "species_id" => "#{mapped_result.id}:0",
        "offset" => 0,
        "limit" => 10,
        "material_assignments" => [
          {
            "body_id" => first_material_id,
            "head_id" => second_material_id
          }
        ],
        "material_assignment_total" => 1
      },
      obtainability_recipe,
      false
    )
    assert(
      material_page["total"] == 1 && material_page["matches"].length == 1,
      "tracker-generated fusion-material pages bypass the game-wide reverse " +
        "index"
    )
    missing_material_page_error = nil
    missing_material_page_started = System.uptime
    begin
      Ironmon.tracker_fusion_material_search_for_recipe(
        {
          "species_id" => "#{mapped_result.id}:0",
          "offset" => 0,
          "limit" => 10
        },
        obtainability_recipe,
        false
      )
    rescue Ironmon::TrackerLookupError => error
      missing_material_page_error = error
    end
    missing_material_page_seconds =
      (System.uptime - missing_material_page_started).to_f / 1_000_000.0
    assert(
      missing_material_page_error &&
        missing_material_page_error.code ==
          "fusion_material_assignments_required" &&
        missing_material_page_seconds < 1.0,
      "a missing tracker material page fails immediately instead of building " +
        "the reverse index on the game thread"
    )
    pair_proven = integrated_obtainability.prove_player_fusion_pair(
      first_material, second_material, mapped_result
    )
    assert(
      pair_proven &&
        integrated_obtainability.passive_target_snapshot(mapped_result)[
          "status"
        ] == "obtainable" &&
        integrated_obtainability.snapshot["processed_pairs"] ==
          integrated_obtainability.snapshot["total_pairs"],
      "a displayed craft result is proven from only its selected material " +
        "pair without advancing the global pair scan"
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
      :@possible_evolution_edge_numbers
    ).keys
    possible_edge_source = GameData::Species.get(
      possible_evolution_edges.first >> 20
    )
    possible_edge_target = GameData::Species.get(
      possible_evolution_edges.first & ((1 << 20) - 1)
    )
    graph_target = Ironmon.tracker_lookup_evolution_targets(
      possible_edge_source, obtainability_recipe
    ).values.flatten.find do |target|
      target["species_id"] == "#{possible_edge_target.id}:0"
    end
    graph_edge_key = graph_target ?
      "#{possible_edge_source.id}:0>#{graph_target["species_id"]}" : ""
    integrated_obtainability.instance_variable_set(:@phase, :complete)
    integrated_obtainability.snapshot(
      nil, [], [graph_edge_key, "unproven:0>edge:0"]
    )
    100.times do
      break if integrated_obtainability.complete?
      integrated_obtainability.send(:advance_work_unit)
    end
    complete_edge_snapshot = integrated_obtainability.snapshot(
      nil, [], [graph_edge_key, "unproven:0>edge:0"]
    )
    assert(
      !possible_evolution_edges.empty? &&
        graph_target && complete_edge_snapshot["complete"] &&
        graph_target.key?("obtainability_status") &&
        complete_edge_snapshot["obtainable_evolution_edge_keys"] == [
          graph_edge_key
        ],
      "completed obtainability preserves real graph-formatted executable " +
        "routes (internal=#{possible_evolution_edges.first.inspect}, " +
        "graph=#{graph_edge_key.inspect}, target=#{graph_target.inspect}, " +
        "returned=#{complete_edge_snapshot["obtainable_evolution_edge_keys"].inspect})"
    )
    repeated_edge_snapshot = integrated_obtainability.snapshot(
      nil, [], [graph_edge_key]
    )
    assert(
      repeated_edge_snapshot["obtainable_evolution_edge_keys"] == [
        graph_edge_key
      ] && integrated_obtainability.snapshot["phase"] == "complete",
      "repeated graph polls read the immutable closure without registering " +
        "request-specific work"
    )
    if previous_integrated_service
      obtainability_services[obtainability_recipe["run_id"]] =
        previous_integrated_service
    else
      obtainability_services.delete(obtainability_recipe["run_id"])
    end
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
      active_services = Ironmon.tracker_obtainability_services
      previous_active_service = active_services[active_recipe["run_id"]]
      active_services[active_recipe["run_id"]] = integrated_obtainability
      live_connection = Ironmon.tracker_connection
      previous_debug = $DEBUG
      previous_debug_requested = live_connection.instance_variable_get(
        :@debug_requested
      )
      previous_output_buffer = live_connection.instance_variable_get(
        :@output_buffer
      )
      begin
        $DEBUG = true
        live_connection.instance_variable_set(:@debug_requested, true)
        live_connection.instance_variable_set(:@output_buffer, "")
        Ironmon.instance_variable_set(:@tracker_search_indexes, nil)
        Ironmon.instance_variable_set(:@tracker_search_result_cache, nil)
        live_search_started = System.uptime
        live_connection.send(
          :handle_request,
          {
            "schema_version" => Ironmon::TRACKER_SCHEMA_VERSION,
            "type" => "request",
            "command" => "debug_pokemon_search",
            "request_id" => "runtime-live-beevee-search",
            "run_id" => active_recipe["run_id"],
            "payload" => {
              "query" => "beevee",
              "offset" => 0,
              "limit" => 20,
              "normal_only" => false
            }
          }
        )
        live_search_seconds =
          (System.uptime - live_search_started).to_f / 1_000_000.0
        serialized_response = live_connection.instance_variable_get(
          :@output_buffer
        )
        assert(
          live_search_seconds < 5.0 &&
            serialized_response.include?("\"success\":true") &&
            serialized_response.include?("Beevee") &&
            serialized_response.include?("\"obtainability_status\":"),
          "the live debug request route builds, annotates, and serializes the " +
            "exact Beevee search without blocking"
        )
      ensure
        $DEBUG = previous_debug
        live_connection.instance_variable_set(
          :@debug_requested, previous_debug_requested
        )
        live_connection.instance_variable_set(
          :@output_buffer, previous_output_buffer
        )
        Ironmon.finish_tracker_debug_search_trace
        File.delete(Ironmon::TRACKER_DEBUG_SEARCH_TRACE_PATH) if
          File.file?(Ironmon::TRACKER_DEBUG_SEARCH_TRACE_PATH)
        if previous_active_service
          active_services[active_recipe["run_id"]] = previous_active_service
        else
          active_services.delete(active_recipe["run_id"])
        end
      end
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
        active_service.foreground_requested? &&
          active_service.background_requested? &&
          active_service.game_work_priority_requested?,
        "diagnostic obtainability requests renew foreground and background " +
          "calculation priority"
      )
      assert(
        Ironmon.tracker_obtainability_game_work_pending?,
        "active game-side obtainability work pauses global fusion pairing"
      )
      active_service.instance_variable_set(:@phase, :player_fusions)
      assert(
        !active_service.foreground_requested? &&
          !active_service.background_requested? &&
          !active_service.scheduled_advance_allowed? &&
          active_service.background_advance_allowed? &&
          active_service.game_work_priority_requested?,
        "tracker mapping waits without burning a game-thread work slice"
      )
      assert(
        Ironmon.tracker_obtainability_game_work_pending?,
        "tracker-side fusion closure retains priority over global fusion pairing"
      )
      active_service.instance_variable_set(:@foreground_until, 0.0)
      active_service.instance_variable_set(:@background_until, 0.0)
      active_service.instance_variable_set(
        :@player_fusion_until,
        Ironmon.tracker_uptime_seconds +
          Ironmon::TrackerObtainabilityService::PLAYER_FUSION_LEASE_SECONDS
      )
      assert(
        Ironmon.tracker_obtainability_game_work_pending?,
        "an in-flight tracker closure keeps global fusion pairing paused after " +
          "the request lease expires"
      )
      active_service.instance_variable_set(:@player_fusion_until, 0.0)
      assert(
        !Ironmon.tracker_obtainability_game_work_pending?,
        "expired tracker closure work lets global fusion pairing resume"
      )
      waiting_started = Process.clock_gettime(Process::CLOCK_MONOTONIC)
      active_service.advance_for_milliseconds(
        Ironmon::TrackerObtainabilityService::FOREGROUND_MILLISECONDS
      )
      waiting_elapsed = Process.clock_gettime(Process::CLOCK_MONOTONIC) -
        waiting_started
      assert(
        waiting_elapsed < 0.05,
        "tracker mapping returns immediately instead of consuming its foreground budget"
      )
      active_service.instance_variable_set(:@phase, :prepare_generators)
      active_service.send(:advance_work_unit)
      assert(
        !active_service.instance_variable_defined?(:@fusion_mapper),
        "obtainability defers seeded fusion mapping entirely to the parallel " +
          "tracker closure"
      )
      assert(
        !active_service.instance_variable_defined?(:@fusion_generator),
        "tracker-owned closure does not retain an obsolete Ruby fusion-evolution generator"
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
    File.binwrite(
      OUTPUT_PATH,
      "tracker structure tests passed\n" +
        "cold_fusion_search_milliseconds=" +
        @cold_fusion_search_milliseconds.to_i.to_s + "\n"
    )
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonTrackerStructureRuntimeTests.run
