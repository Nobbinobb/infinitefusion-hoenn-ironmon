module IronmonAreaEncounterLookupProbe
  def self.report
    @report ||= { "requests" => [], "mapping_errors" => [] }
  end

  def self.save_report
    File.binwrite(
      $ironmon_area_lookup_probe_options["report_path"],
      Ironmon.tracker_json_generate(report)
    )
  end

  def self.request(label, payload, run_id, debug = true)
    started = Ironmon.tracker_uptime_seconds
    response = Ironmon.tracker_area_lookup_detail(payload, run_id, debug)
    elapsed = (Ironmon.tracker_uptime_seconds - started) * 1000
    report["requests"] << {
      "phase" => label,
      "milliseconds" => elapsed.round(2),
      "pending" => response["pending"],
      "total" => response["total_count"],
      "fusions" => response["encounter_fusions"].length
    }
    raise "lookup blocked the game for #{elapsed.round} ms" if elapsed > 1000
    return response
  rescue Exception => error
    report["requests"] << {
      "phase" => label, "error" => error.message,
      "backtrace" => error.backtrace
    }
    raise
  ensure
    save_report
  end

  module MappingTrace
    def ensure_fusion_pool
      super
    rescue Exception => error
      IronmonAreaEncounterLookupProbe.report["mapping_errors"] << {
        "error" => error.message, "backtrace" => error.backtrace
      }
      raise
    end
  end

  def self.assert_concealed_fusion_page(response)
    entries = response["encounter_fusions"]
    raise "concealed fusion context is missing" if entries.empty?
    raise "concealed fusion page started result preparation" if response["pending"]
    raise "undisclosed fusion identities leaked" if entries.any? do |entry|
      entry["details_revealed"] ||
        ["species_id", "species_name", "sprite_path"].any? do |field|
          entry.key?(field)
        end
    end
    raise "undisclosed fusion materials leaked" if
      !(response["required_fusion_materials"] || []).empty?
  end

  module AreaMappingTrace
    def build_mapper
      report = IronmonAreaEncounterLookupProbe.report
      report["ruby_area_mapper_builds"] = report["ruby_area_mapper_builds"].to_i + 1
      super
    end
  end

  def self.run
    options = $ironmon_area_lookup_probe_options
    $game_temp = Game_Temp.new
    $PokemonTemp = PokemonTemp.new
    Game.load_sprites_list_caches
    if options["save_path"] && !options["save_path"].empty?
      data = SaveData.get_data_from_file(options["save_path"])
      $PokemonGlobal = data.fetch(:global_metadata)
      $game_switches = data.fetch(:switches)
      $game_variables = data.fetch(:variables)
      $Trainer = data.fetch(:player)
      $PokemonSystem = data.fetch(:pokemon_system)
    else
      $PokemonGlobal = PokemonGlobalMetadata.new
      $PokemonSystem = PokemonSystem.new
      $PokemonGlobal.ironmon_mode = true
      $PokemonGlobal.ironmon_seed = options["seed"]
      $PokemonGlobal.ironmon_run_id = "run-seed-#{options['seed']}"
      $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new(:mixed, :custom_fusions_only)
      $PokemonGlobal.ironmon_species_generator_version = Ironmon::SpeciesGenerator::SCHEMA_VERSION
      ledger = Ironmon.default_run_ledger
      ledger["current_attempt"] = {
        "run_id" => $PokemonGlobal.ironmon_run_id,
        "seed" => options["seed"], "result" => "active"
      }
      $PokemonGlobal.ironmon_run_ledger = ledger
      $game_switches = []
      $game_variables = []
    end
    $game_map = Struct.new(:map_id).new(5)
    Ironmon.reset_custom_fusion_pool_cache
    Ironmon.reset_species_generator_cache
    Ironmon::PlayerFusionMapper.prepend(MappingTrace)
    report["seed"] = $PokemonGlobal.ironmon_seed
    report["pool_size"] = Ironmon.custom_fusion_pool_numbers.length
    run_id = Ironmon.current_run_attempt["run_id"]
    payload = {
      "area_id" => "area:5", "category" => "encounter",
      "encounter_environment" => "cross", "discovery_keys" => [],
      "offset" => 0, "limit" => 10
    }
    if options["native_root"]
      run_native(options["native_root"])
      return
    end
    mappings = Marshal.dump(Ironmon.pivot_state.fusion_mappings)
    assert_concealed_fusion_page(
      request("ordinary_cold", payload, run_id, false)
    )
    raise "cold lookup must defer fusion preparation" if !request("cold", payload, run_id)["pending"]
    raise "repeated pending lookup must resume preparation" if !request("repeat", payload, run_id)["pending"]
    response = complete_request(payload, run_id)
    raise "fusion page was not filled" if response["encounter_fusions"].length != 10
    expected = response["encounter_fusions"]
    raise "reopening changed fusion results" if request("reopen", payload, run_id)["encounter_fusions"] != expected
    raise "lookup populated gameplay fusion mappings" if Marshal.dump(Ironmon.pivot_state.fusion_mappings) != mappings
    Ironmon.prepare_player_fusion_pairing
    while Ironmon.instance_variable_get(:@player_fusion_preparation_fiber)
      Ironmon.advance_player_fusion_pairing
    end
    raise "background preparation changed fusion results" if request("after_preparation", payload, run_id)["encounter_fusions"] != expected
    hidden = request("ordinary_live", payload, run_id, false)
    assert_concealed_fusion_page(hidden)
    raise "concealed page changed its public count" if
      hidden["encounter_fusions"].length != expected.length ||
      hidden["total_count"] != response["total_count"]
    recipe = Ironmon.tracker_active_area_recipe(Ironmon.current_run_attempt)
    work = Ironmon.tracker_area_fusion_work(recipe)
    work.instance_variable_get(:@results).each do |pair, entry|
      number = Ironmon.player_fusion_mapper.species_number(pair[0], pair[1])
      body = (number - 1) / NB_POKEMON
      head = number - body * NB_POKEMON
      raise "lookup and gameplay mapping disagree" if entry["species_id"] != "B#{body}H#{head}:0"
    end
    second_page = complete_request(payload.merge("offset" => 10), run_id)
    expected_count = [[second_page["total_count"] - 10, 0].max, 10].min
    raise "second fusion page was not filled" if second_page["encounter_fusions"].length != expected_count
    first_ids = expected.map { |entry| entry["entry_id"] }
    second_ids = second_page["encounter_fusions"].map { |entry| entry["entry_id"] }
    raise "fusion paging repeated entries" if !(first_ids & second_ids).empty?
    area = Ironmon.tracker_area_catalog.find { |entry| entry["area_id"] == "area:5" }
    metadata = Ironmon.tracker_area_encounter_metadata(area, recipe)
    grass_slots = metadata.count do |entry|
      Ironmon.tracker_area_encounter_environment(entry["encounter_type"]) == "grass"
    end
    same_page = complete_request(
      payload.merge("encounter_environment" => "grass", "offset" => grass_slots),
      run_id
    )
    raise "same-environment fusions missing" if same_page["encounter_fusions"].empty?
    raise "same-environment page contains cross fusions" if same_page["encounter_fusions"].any? { |entry| entry["cross_environment"] }
    report["passed"] = true
    save_report
  end

  def self.run_native(root)
    mappings = Marshal.dump(Ironmon.pivot_state.fusion_mappings)
    Ironmon::TrackerAreaFusionWork.prepend(AreaMappingTrace)
    profile = Module.new
    [:tracker_area_lookup_detail, :tracker_area_normal_encounter_sources,
     :tracker_lookup_fusion_sprite_path, :tracker_search_fusion_name,
     :sprite_credit_catalog, :tracker_resolved_sprite_path].each do |method_name|
      profile.define_method(method_name) do |*args|
        started = Ironmon.tracker_uptime_seconds
        value = super(*args)
        elapsed = (Ironmon.tracker_uptime_seconds - started) * 1000
        IronmonAreaEncounterLookupProbe.report["profile"] ||= []
        IronmonAreaEncounterLookupProbe.report["profile"] << {
          "method" => method_name.to_s, "milliseconds" => elapsed.round(3)
        } if elapsed > 1
        value
      end
    end
    Ironmon.singleton_class.prepend(profile)
    permission_free = $ironmon_area_lookup_probe_options["preparation_mode"] != "diagnostic"
    manual = $ironmon_area_lookup_probe_options["preparation_mode"] == "manual"
    $DEBUG = !permission_free
    $PokemonSystem.overworld_encounters = false
    $PokemonGlobal.ironmon_tracker_active_run_preparation_run_id =
      manual ? nil : Ironmon.current_run_attempt["run_id"]
    Ironmon.send(:remove_const, :TRACKER_PORT)
    Ironmon.const_set(:TRACKER_PORT, File.binread(root + "/port.txt").to_i)
    $scene = Scene_Map.new
    Ironmon.mark_tracker_obtainability_map_ready($scene)
    recipe = Ironmon.tracker_active_area_recipe(Ironmon.current_run_attempt)
    area = Ironmon.tracker_area_catalog.find { |entry| entry["area_id"] == "area:5" }
    metadata = Ironmon.tracker_area_encounter_metadata(area, recipe)
    grass_count = metadata.count { |entry| Ironmon.tracker_area_encounter_environment(entry["encounter_type"]) == "grass" }
    File.binwrite(root + "/grass-count.txt", grass_count.to_s)
    File.binwrite(root + "/recipe.json", Ironmon.tracker_json_generate(
      Ironmon.tracker_completed_run_recipe(:lost)
    ))
    started = Ironmon.tracker_uptime_seconds
    until File.exist?(root + "/stop.txt")
      mode = root + "/mode.txt"
      $PokemonSystem.overworld_encounters = File.binread(mode) == "on" if File.exist?(mode)
      Ironmon.update_tracker_connection
      Ironmon.update_tracker_obtainability
      Ironmon.update_tracker_area_fusions
      raise "native connection probe timed out" if Ironmon.tracker_uptime_seconds - started > 300
      sleep(0.005)
    end
    if permission_free
      run_id = Ironmon.current_run_attempt["run_id"]
      connection = Ironmon.tracker_connection
      raise "preparation test has diagnostic access" if connection.diagnostic_capability?("world.wild_encounters")
      response = Ironmon.tracker_run_lookup_preparation({
        "species_id" => "BULBASAUR:0", "species_ids" => ["BULBASAUR:0"],
        "evolution_edge_keys" => ["private"], "recipe" => { "run_id" => "other" }
      }, run_id)
      raise "full preparation did not complete" if !response["background_complete"]
      ["target", "obtainable_species_ids", "obtainable_evolution_edge_keys", "recipe"].each do |key|
        raise "preparation disclosed #{key}" if response.key?(key)
      end
      begin
        Ironmon.tracker_run_lookup_preparation({}, "other-run")
        raise "preparation accepted a stale run"
      rescue Ironmon::TrackerLookupError => error
        raise if error.code != "run_mismatch"
      end
      begin
        Ironmon.tracker_validate_debug_context(["world.wild_encounters"])
        raise "preparation granted diagnostic access"
      rescue Ironmon::TrackerDebugError => error
        raise if error.code != "debug_forbidden"
      end
      raise "preparation populated gameplay mappings" if Marshal.dump(Ironmon.pivot_state.fusion_mappings) != mappings
      report["permission_free"] = true
      report["passed"] = true
      save_report
      return
    end
    work = Ironmon.tracker_area_fusion_work(recipe)
    raise "native lookup started a Ruby mapper" if report["ruby_area_mapper_builds"].to_i != 0
    raise "native lookup unexpectedly rebuilt a Ruby mapping" if work.instance_variable_get(:@mapper)
    raise "native lookup populated gameplay mappings" if Marshal.dump(Ironmon.pivot_state.fusion_mappings) != mappings
    report["native_mapped_pairs"] = work.instance_variable_get(:@results).length
    raise "native lookup never produced fusion results" if report["native_mapped_pairs"] == 0
    target_entry = work.instance_variable_get(:@results).values.first
    target = GameData::Species.get(target_entry["species_id"].split(":").first.to_sym)
    active_recipe = Ironmon.tracker_debug_active_recipe
    key = Ironmon.tracker_occurrence_cache_key(active_recipe, target, :wild)
    native_locations = Ironmon.tracker_lookup_cache[key]
    raise "native location search did not cache the tested fusion" if !native_locations
    fallback = Ironmon::TrackerWildOccurrenceWork.new(target, active_recipe, nil)
    fallback_started = Ironmon.tracker_uptime_seconds
    fallback_locations = nil
    until fallback_locations
      fallback_locations = fallback.results
      raise "fallback location preparation timed out" if Ironmon.tracker_uptime_seconds - fallback_started > 90
    end
    report["fallback_location_milliseconds"] = ((Ironmon.tracker_uptime_seconds - fallback_started) * 1000).round(2)
    raise "native and fallback locations disagree" if fallback_locations != native_locations
    raise "fallback location lookup populated gameplay mappings" if Marshal.dump(Ironmon.pivot_state.fusion_mappings) != mappings
    hidden = request("ordinary_native", {
      "area_id" => "area:5", "category" => "encounter",
      "encounter_environment" => "cross", "discovery_keys" => [],
      "offset" => 0, "limit" => 10, "use_native_fusion_mapping" => true
    }, recipe["run_id"], false)
    assert_concealed_fusion_page(hidden)
    work.instance_variable_get(:@results).each do |pair, entry|
      number = Ironmon.player_fusion_mapper.species_number(pair[0], pair[1])
      body = (number - 1) / NB_POKEMON
      head = number - body * NB_POKEMON
      raise "native and gameplay mapping disagree" if entry["species_id"] != "B#{body}H#{head}:0"
    end
    report["passed"] = true
    save_report
  end

  def self.complete_request(payload, run_id)
    started = Ironmon.tracker_uptime_seconds
    maximum_slice = 0.0
    response = nil
    loop do
      25.times do
        slice_started = Ironmon.tracker_uptime_seconds
        Ironmon.instance_variable_get(:@tracker_area_fusion_work).each_value(&:advance)
        elapsed = Ironmon.tracker_uptime_seconds - slice_started
        maximum_slice = [maximum_slice, elapsed].max
      end
      response = request("poll", payload, run_id)
      break if !response["pending"]
      raise "fusion page preparation did not finish" if Ironmon.tracker_uptime_seconds - started > 90
    end
    report["preparation_milliseconds"] ||= ((Ironmon.tracker_uptime_seconds - started) * 1000).round(2)
    report["maximum_slice_milliseconds"] = [report["maximum_slice_milliseconds"].to_f, maximum_slice * 1000].max.round(2)
    raise "background slice blocked the game" if maximum_slice > 1.0
    return response
  end
end

IronmonAreaEncounterLookupProbe.run
$ironmon_wild_encounter_fusion_test_output_path =
  $ironmon_area_lookup_probe_options["report_path"] + ".regressions"
load $ironmon_area_lookup_probe_options["wild_test_path"]
