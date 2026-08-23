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
      Ironmon.method(:reset_tracker_post_run_cache),
      "019_Tracker_Post_Run_5_Fusion_Caches_And_Runtime.rb",
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
      Ironmon.method(:tracker_validate_debug_context),
      "020_Tracker_Debug_2_Context_And_Authorization.rb",
      "diagnostic authorization"
    )
    assert_source(
      Ironmon.method(:tracker_debug_resolve_pokemon),
      "020_Tracker_Debug_3_Pokemon_Snapshots.rb",
      "diagnostic Pokemon snapshots"
    )
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
