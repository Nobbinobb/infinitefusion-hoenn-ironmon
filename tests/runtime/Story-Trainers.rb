module IronmonStoryTrainerRuntimeTests
  def self.assert(condition, message)
    raise "Story trainer test failed: #{message}" if !condition
  end

  def self.request_live_party(connection, area, entry, grants)
    connection.instance_variable_set(:@diagnostic_capabilities, grants)
    connection.instance_variable_set(:@output_buffer, "")
    connection.send(:handle_message, {
      "schema_version" => Ironmon::TRACKER_SCHEMA_VERSION, "type" => "request",
      "request_id" => "live-story-lookup", "command" => "area_lookup_detail",
      "run_id" => $PokemonGlobal.ironmon_run_id,
      "payload" => { "area_id" => area["area_id"], "category" => "trainer", "discovery_keys" => [] }
    })
    response = Ironmon.tracker_stringify_catalog_keys(HTTPLite::JSON.parse(connection.instance_variable_get(:@output_buffer).lines.last))
    assert(!response["error"], "live story request succeeds: #{response['error'].inspect}")
    return response["payload"]["trainers"].find { |row| row["entry_id"] == entry["entry_id"] }
  end

  def self.test_live_story_lookup
    $PokemonGlobal.ironmon_run_id = "live-story-lookup"
    ledger = Ironmon.default_run_ledger
    attempt = { "run_id" => $PokemonGlobal.ironmon_run_id, "seed" => $PokemonGlobal.ironmon_seed,
      "generation_profile_id" => $PokemonGlobal.ironmon_generation_profile_id,
      "result" => "active", "attempt_number" => 7, "statistics" => nil }
    ledger["current_attempt"] = attempt
    $PokemonGlobal.ironmon_run_ledger = ledger
    wally = wally_initialize
    wally.currentTeam = [Ironmon.mark_persistent_trainer_species(Pokemon.new(:SQUIRTLE, 10, nil))]
    $game_self_switches[[73, 53, "A"]] = false
    $game_switches[2145] = false
    connection = Ironmon::TrackerConnection.new
    Ironmon.instance_variable_set(:@tracker_connection, connection)
    recipe = Ironmon.tracker_active_area_recipe(attempt)
    assert(!recipe.key?("statistics"), "the real live request has no archive statistics")
    assert(Ironmon.tracker_story_recipe_matches_current_run?(recipe), "the current live recipe can reconstruct story teams without archive statistics")
    ["run_id", "seed", "generation_profile_id"].each do |field|
      assert(!Ironmon.tracker_story_recipe_matches_current_run?(recipe.merge(field => "different")), "live preview rejects a different #{field}")
    end
    assert(!Ironmon.tracker_story_recipe_matches_current_run?(recipe.reject { |key, _| key == "active_run" }), "archive fallback still requires the exact attempt number")
    before = Marshal.dump([$PokemonGlobal, $game_variables, $game_switches, $game_self_switches])
    roles = []
    Ironmon.tracker_area_catalog.each do |area|
      area["trainers"].each do |entry|
        next if !["wally", "rival"].include?(entry["battle_kind"])
        roles << entry["entry_id"]
        hidden = request_live_party(connection, area, entry, [])
        assert(!hidden["details_revealed"] && hidden["party"].empty?, "unfought #{entry['entry_id']} stays hidden without its grant")
        visible = request_live_party(connection, area, entry, ["world.trainer_parties"])
        assert(visible["details_revealed"] && !visible["party"].empty?, "live grant shows unfought #{entry['entry_id']}")
        assert(!visible["defeated"], "preview does not award victory for #{entry['entry_id']}")
        assert(visible["party"].all? { |member| !member.key?("abilities") && !member.key?("moves") }, "world identity access does not grant battle sets")
        detailed = request_live_party(connection, area, entry, ["world.trainer_parties", "pokemon.all_active", "pokemon.abilities", "pokemon.move_access"])
        assert(detailed["party"].all? { |member| member["abilities_revealed"] && member["moves_revealed"] && !member["abilities"].empty? && !member["moves"].empty? }, "full grants include the live story battle set")
      end
    end
    assert(roles.length == 4, "the live path covers all three rival stages and Wally")
    assert(before == Marshal.dump([$PokemonGlobal, $game_variables, $game_switches, $game_self_switches]), "live preview requests leave persistent story state and progress unchanged")
    scripted = 0
    Ironmon.tracker_area_catalog.each do |area|
      area["trainers"].each do |entry|
        next if !["move_tutor", "scaled"].include?(entry["battle_kind"])
        visible = request_live_party(connection, area, entry, ["world.trainer_parties"])
        assert(visible["details_revealed"] && !visible["party"].empty?, "live grant also shows scripted #{entry['entry_id']}")
        scripted += 1
      end
    end
    assert(scripted == 15, "live permission checks include all fifteen tutor and adventurer encounters")
  end

  def self.test_live_fusion_lookup(output)
    globals = [$PokemonGlobal, $game_variables, $game_switches, $game_self_switches]
    original_connection = Ironmon.instance_variable_get(:@tracker_connection)
    readiness = [Ironmon.instance_variable_get(:@base_stat_randomization_ready), Ironmon.instance_variable_get(:@evolution_randomization_ready)]
    original_third_battle = Object.instance_method(:updateRivalTeamForThirdBattle)
    Object.send(:define_method, :updateRivalTeamForThirdBattle) { raise "A second-rival or Wally lookup must not prepare the third rival battle" }
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_seed = 123456
    $PokemonGlobal.ironmon_run_id = "live-fusion-story-lookup"
    $PokemonGlobal.ironmon_generation_profile_id = Ironmon.current_generation_profile_id
    $PokemonGlobal.ironmon_species_generator_version = Ironmon::SpeciesGenerator::SCHEMA_VERSION
    $PokemonGlobal.ironmon_starter_species = [:SQUIRTLE, :B1H4, :B7H1]
    $game_switches, $game_variables, $game_self_switches = [], [], {}
    $game_variables[VAR_HOENN_CHOSEN_STARTER_INDEX] = 0
    $game_variables[VAR_TRAINER_GENDER] = GENDER_MALE
    assert(Ironmon.prepare_base_stat_randomization, "fusion lookup prepares real base stats")
    assert(Ironmon.prepare_evolution_randomization, "fusion lookup prepares real generated evolution metadata")
    ledger = Ironmon.default_run_ledger
    ledger["current_attempt"] = { "run_id" => $PokemonGlobal.ironmon_run_id,
      "generation_profile_id" => $PokemonGlobal.ironmon_generation_profile_id, "seed" => 123456,
      "result" => "active", "attempt_number" => 1, "statistics" => nil }
    $PokemonGlobal.ironmon_run_ledger = ledger
    timings = []
    [:mixed, :custom_fusions_only].each do |policy|
      $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new(:mixed, policy)
      Ironmon.reset_species_generator_cache
      $PokemonGlobal.ironmon_rival_lab_decision = nil
      wally = wally_initialize
      wally.currentTeam = [Ironmon.mark_persistent_trainer_species(Pokemon.new(:B1H4, 7))]
      rival = initializeRivalBattledTrainer
      $PokemonGlobal.battledTrainers[BATTLED_TRAINER_RIVAL_KEY] = rival
      Ironmon.apply_rival_lab_decision(Ironmon.rival_lab_decision(:B1H4, :B7H1, false))
      connection = Ironmon::TrackerConnection.new
      Ironmon.instance_variable_set(:@tracker_connection, connection)
      before = live_story_state
      pending_before = Ironmon.instance_variable_get(:@pending_generated_evolutions)
      committed_before = Ironmon.instance_variable_get(:@committed_generated_evolutions)
      Ironmon.tracker_area_catalog.each do |area|
        area["trainers"].each do |entry|
          next if entry["battle_kind"] != "wally" && !(entry["battle_kind"] == "rival" && entry["story_stage"] == 2)
          started = Ironmon.tracker_uptime_seconds
          party = request_live_party(connection, area, entry, ["world.trainer_parties"])
          timings << "#{policy} #{entry['battle_kind']}: #{((Ironmon.tracker_uptime_seconds-started)*1000).round(1)} ms"
          File.binwrite(File.join(output, "timing.txt"), timings.join("\n"))
          assert(party["details_revealed"] && !party["party"].empty?, "#{policy} live #{entry['battle_kind']} preview is available")
        end
      end
      assert(before == live_story_state, "#{policy} fusion story previews preserve the saved story teams, individuals, moves, levels, decisions, and progress")
      assert(pending_before.equal?(Ironmon.instance_variable_get(:@pending_generated_evolutions)) && committed_before.equal?(Ironmon.instance_variable_get(:@committed_generated_evolutions)), "preview evolution checks preserve live pending evolution state")
    end
  ensure
    Object.send(:define_method, :updateRivalTeamForThirdBattle, original_third_battle) if original_third_battle
    $PokemonGlobal, $game_variables, $game_switches, $game_self_switches = globals if globals
    Ironmon.instance_variable_set(:@tracker_connection, original_connection)
    Ironmon.instance_variable_set(:@base_stat_randomization_ready, readiness[0]) if readiness
    Ironmon.instance_variable_set(:@evolution_randomization_ready, readiness[1]) if readiness
    Ironmon.reset_species_generator_cache
    Ironmon.reset_evolution_generator_cache
  end

  def self.live_story_state
    teams = $PokemonGlobal.battledTrainers.map do |key, trainer|
      [key, trainer.object_id, trainer.inventory,
        trainer.currentTeam.map { |pokemon| [pokemon.object_id, pokemon.personalID, pokemon.species, pokemon.level, pokemon.moves.map(&:id)] }]
    end
    return Ironmon.tracker_json_generate([teams, $PokemonGlobal.ironmon_run_ledger,
      $PokemonGlobal.ironmon_rival_lab_decision, $PokemonGlobal.ironmon_story_trainer_parties,
      $game_variables, $game_switches, $game_self_switches.map { |key, value| [key, value] }])
  end

  def self.run
    globals = [$PokemonGlobal, $PokemonSystem, $PokemonTemp, $game_temp,
               $game_switches, $game_variables, $game_self_switches, $game_map, $Trainer]
    original_core = Object.instance_method(:ironmon_area_progress_original_trainer_battle_core)
    original_double = Object.instance_method(:ironmon_area_progress_original_pb_double_trainer_battle)
    original_reference = Ironmon.method(:current_area_event_reference)
    original_connection = Ironmon.instance_variable_get(:@tracker_connection)
    original_pool = Ironmon.instance_variable_get(:@custom_fusion_pool_service)
    begin
      $PokemonGlobal = PokemonGlobalMetadata.new
      $PokemonGlobal.ironmon_mode = true
      $PokemonGlobal.ironmon_seed = 1
      $PokemonGlobal.ironmon_run_id = "story-trainer-test"
      $PokemonGlobal.ironmon_generation_profile_id = Ironmon.current_generation_profile_id
      $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new(:normal_only, :normal_only)
      $PokemonGlobal.ironmon_species_generator_version = Ironmon::SpeciesGenerator::SCHEMA_VERSION
      $PokemonSystem = PokemonSystem.new
      $PokemonTemp = PokemonTemp.new
      $game_temp = Game_Temp.new
      $game_switches = []
      $game_variables = []
      $game_self_switches = { [73, 53, "A"] => true }
      $game_map = Struct.new(:map_id).new(73)
      $Trainer = Struct.new(:selected_difficulty, :lowest_difficulty).new
      def $Trainer.make_foreign_ID; 1; end
      def $Trainer.name; "Runtime Test"; end
      def $Trainer.party; []; end
      def $Trainer.rival_appearance; nil; end
      pool = Object.new
      def pool.pool; [:B1H2, :B2H1]; end
      Ironmon.instance_variable_set(:@custom_fusion_pool_service, pool)
      Ironmon.reset_species_generator_cache
      entry = Ironmon.tracker_area_catalog.flat_map { |area| area["trainers"] }.
        find { |candidate| candidate["battle_kind"] == "wally" }
      assert(entry && entry["map_id"] == 73 && entry["event_id"] == 53, "catalog identifies Wally's actual story event")
      assert(entry["completion_switch_id"] == 2145, "catalog obtains the victory switch from the win branch")
      assert(!Ironmon.tracker_area_event_completed?(entry), "preparation self-switch is not victory")
      recipe = { "run_id" => "story-trainer-test" }
      hidden = Ironmon.tracker_area_story_trainer_entry(entry, recipe, {}, false, false, false, false)
      assert(!hidden["defeated"] && !hidden["details_revealed"] && hidden["party"].empty?, "unfought Wally remains concealed")
      wally = wally_initialize
      wally.currentTeam = [
        Ironmon.mark_persistent_trainer_species(Pokemon.new(:MAGNEMITE, 16, nil)),
        Ironmon.mark_persistent_trainer_species(Pokemon.new(:SQUIRTLE, 19, nil))
      ]
      before = Marshal.dump(wally)
      assert(Ironmon.tracker_live_wally_party(entry).any? { |member| member["species_id"] == "SQUIRTLE:0" }, "live preview retains the story starter")
      assert(Marshal.dump(wally) == before, "preview does not alter Wally's saved team")
      connection = Object.new
      connection.instance_variable_set(:@packages, [])
      def connection.connected?; true; end
      def connection.send_area_discovery(area, category, keys)
        @packages << [area, category, keys]
        return true
      end
      Ironmon.instance_variable_set(:@tracker_connection, connection)
      reference = [73, 53]
      Ironmon.define_singleton_method(:current_area_event_reference) { |*_| reference }
      result = 2
      admitted = nil
      Object.send(:define_method, :ironmon_area_progress_original_trainer_battle_core) do |*trainers|
        admitted = Ironmon.tracker_story_party_snapshot(trainers[0].party)
        result
      end
      wally_battle
      assert(Ironmon.story_trainer_parties.empty?, "loss does not record a victorious party")
      assert(connection.instance_variable_get(:@packages).empty?, "loss does not publish discovery")
      result = 1
      assert(wally_battle, "story battle returns the actual win")
      assert(Ironmon.story_trainer_parties[entry["entry_id"]] == admitted, "win records the actual admitted battle party")
      assert(connection.instance_variable_get(:@packages) == [["area:73", "trainer", [entry["entry_id"]]]], "Wally uses the ordinary trainer discovery channel")
      disclosed = Ironmon.tracker_area_story_trainer_entry(entry, recipe, {}, false, false, false, false)
      assert(disclosed["defeated"] && disclosed["details_revealed"], "victory reveals the real story party")
      assert(disclosed["party"].any? { |member| member["species_id"] == "SQUIRTLE:0" && member["level"] == 28 }, "story starter survives the exact battle pipeline")
      assert(disclosed["party"].none? { |member| member.key?("moves") || member.key?("abilities") }, "identity discovery does not grant battle-set permissions")
      connection.instance_variable_get(:@packages).clear
      reference = [87, 84]
      members = [[:TEAM_MAGMA_GRUNT_M, "Craig", 88], [:TEAM_MAGMA_GRUNT_F, "Dayle", 87]]
      ids = members.map { |type, name, event| Ironmon.tracker_area_event_entry_id("trainers", 87, event, type, name) }
      Object.send(:define_method, :ironmon_area_progress_original_pb_double_trainer_battle) { |*_| true }
      Ironmon.queue_area_trainer_entries(ids)
      assert(pbDoubleTrainerBattle(:TEAM_MAGMA_GRUNT_M, "Craig", 0, nil, :TEAM_MAGMA_GRUNT_F, "Dayle"), "scripted double battle preserves victory")
      assert(connection.instance_variable_get(:@packages) == [["area:87", "trainer", ids]], "successive scripted double battles mark only their actual participants")
      connection.instance_variable_get(:@packages).clear
      Object.send(:define_method, :ironmon_area_progress_original_pb_double_trainer_battle) { |*_| raise "test interruption" }
      Ironmon.queue_area_trainer_entries(ids)
      begin
        pbDoubleTrainerBattle(:TEAM_MAGMA_GRUNT_M, "Craig", 0, nil, :TEAM_MAGMA_GRUNT_F, "Dayle")
        raise "Expected interrupted battle"
      rescue RuntimeError => error
        raise if error.message != "test interruption"
      end
      assert(connection.instance_variable_get(:@packages).empty?, "interrupted scripted battles publish nothing")
      assert(Ironmon.instance_variable_get(:@pending_area_trainer_entries).empty?, "interrupted scripted battle clears pending participants")
      archived_recipe = { "story_trainer_parties" => Ironmon.tracker_story_trainer_recipe }
      $PokemonGlobal.battledTrainers = {}
      $PokemonGlobal.ironmon_run_id = "different-run"
      archived = Ironmon.tracker_area_story_trainer_entry(entry, archived_recipe, {}, true, true, true, true)
      assert(archived["party"].map { |member| [member["species_id"], member["level"]] } == disclosed["party"].map { |member| [member["species_id"], member["level"]] }, "archive uses its captured party independently of the current run")
      assert(archived["party"].all? { |member| member["abilities_revealed"] && member["moves_revealed"] }, "archive restores captured abilities and moves")
      preview_recipe = { "story_trainer_previews" => archived_recipe["story_trainer_parties"] }
      preview = Ironmon.tracker_area_story_trainer_entry(entry, preview_recipe, {}, true, true, true, true)
      assert(preview["details_revealed"] && !preview["party"].empty? && !preview["defeated"], "archive previews reveal an unplayed party without awarding victory")
      hidden_preview = Ironmon.tracker_area_story_trainer_entry(entry, preview_recipe, {}, false, true, false, false)
      assert(!hidden_preview["details_revealed"] && hidden_preview["party"].empty?, "generated previews do not bypass live disclosure")
      static_recipe = { "run_id" => "older-run", "seed" => 1,
        "species_generator_version" => Ironmon::SpeciesGenerator::SCHEMA_VERSION,
        "configuration" => Ironmon.configuration_snapshot, "data_mode" => "classic" }
      scripted_count = 0
      Ironmon.tracker_area_catalog.each do |area|
        scripted = area["trainers"].select { |candidate| ["move_tutor", "scaled"].include?(candidate["battle_kind"]) }
        next if scripted.empty?
        rows = Ironmon.tracker_area_trainer_entries(area.merge("trainers" => scripted), static_recipe, {}, true, true)
        assert(rows.all? { |row| row["details_revealed"] && !row["party"].empty? }, "full archive reconstructs tutors and adventurers without victory snapshots")
        scripted_count += rows.length
      end
      assert(scripted_count == 15, "all fifteen scripted catalog teams are reconstructible")
      $game_variables[VAR_HOENN_CHOSEN_STARTER_INDEX] = 0
      $game_variables[VAR_TRAINER_GENDER] = GENDER_MALE
      before = Marshal.dump([$PokemonGlobal, $game_variables, $game_switches])
      previews = Ironmon.tracker_current_story_trainer_previews
      assert(previews.length == 3 && previews.values.map(&:length).sort == [1, 2, 3], "story templates prepare all three rival stages")
      assert(before == Marshal.dump([$PokemonGlobal, $game_variables, $game_switches]), "story preview replay leaves the saved team, variables, and switches unchanged")
      assert(Ironmon.tracker_current_story_trainer_previews.equal?(previews), "story previews are reused while the saved context remains unchanged")
      assert(!Ironmon.tracker_story_recipe_matches_current_run?(static_recipe), "older runs cannot borrow the current story context")
      unknown = Ironmon.tracker_area_story_trainer_entry(entry, {}, {}, true, true, true, true)
      assert(!unknown["details_revealed"] && unknown["party"].empty?, "legacy archive never substitutes the placeholder PBS party")
      test_live_story_lookup
      puts "Story trainer runtime regressions passed"
    ensure
      Object.send(:define_method, :ironmon_area_progress_original_trainer_battle_core, original_core)
      Object.send(:define_method, :ironmon_area_progress_original_pb_double_trainer_battle, original_double)
      Ironmon.define_singleton_method(:current_area_event_reference, original_reference)
      Ironmon.instance_variable_set(:@tracker_connection, original_connection)
      Ironmon.instance_variable_set(:@custom_fusion_pool_service, original_pool)
      Ironmon.cancel_area_trainer_event
      $PokemonGlobal, $PokemonSystem, $PokemonTemp, $game_temp,
        $game_switches, $game_variables, $game_self_switches, $game_map, $Trainer = globals
      Ironmon.reset_species_generator_cache
    end
  end
end

IronmonStoryTrainerRuntimeTests.run
