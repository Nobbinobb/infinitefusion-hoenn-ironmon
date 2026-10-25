source_root = ENV.fetch("IRONMON_SMOKE_SOURCE_ROOT").tr("\\", "/")
output_root = ENV.fetch("IRONMON_SMOKE_OUTPUT_ROOT").tr("\\", "/")
$ironmon_loss_audit_output = output_root
begin
  module System
    def self.data_directory
      return File.join($ironmon_loss_audit_output, "userdata")
    end
  end
  Dir.mkdir(System.data_directory) if !Dir.exist?(System.data_directory)
  load File.join(source_root, "tools/generation/Script-Loader.rb")
  IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:997|998|999)/])
  GameData.load_all
  IronmonScriptLoader.load_manifest(File.join(source_root, "src"), File.join(source_root, "src/load_order.json"), false)
  Ironmon.send(:remove_const, :TRACKER_PORT)
  Ironmon.const_set(:TRACKER_PORT, 0)
  $data_system = load_data("Data/System.rxdata")
  $data_common_events = load_data("Data/CommonEvents.rxdata")
  $data_tilesets = load_data("Data/Tilesets.rxdata")
  $PokemonTemp = PokemonTemp.new
  SaveData.initialize_bootup_values
  SaveData.load_new_game_values
  input = SaveData.read_from_file(File.join(output_root, "File-G.rxdata"))
  input_global = input[:global_metadata] || input.values.find { |entry| entry.is_a?(PokemonGlobalMetadata) }
  input_player = input[:player] || input.values.find { |entry| entry.is_a?(Player) }
  File.binwrite(File.join(output_root, "save-metadata.json"), Ironmon.tracker_json_generate({
    "keys" => input.keys.map(&:to_s), "seed" => input_global.ironmon_seed,
    "rules" => input_global.ironmon_configuration.to_h, "slot" => input_player.save_slot,
    "offers" => input_global.ironmon_starter_species,
    "attempt" => input_global.ironmon_run_ledger["current_attempt"],
    "save_directory_isolated" => SaveData::SAVE_DIR.start_with?(output_root)
  }))
  raise "Audit save directory escaped its isolated output" if !SaveData::SAVE_DIR.start_with?(output_root)
  Game.load_custom_sprites_list_cache
  initialize_alt_sprite_substitutions
  def Ironmon.tracker_ready?; true; end
  SaveData.mark_values_as_unloaded
  Game.load(input)
  Ironmon.activate_run_generation_profile(Ironmon.pinned_generation_profile_id)
  runs = Ironmon::GameplayCollection::JsonReader.new(File.binread(File.join(output_root, "new-rule-runs.json"))).parse
  replay = runs.map do |entry|
    rules = Ironmon::Configuration.from(entry["rules"])
    version = entry["recipe"]["species_generator"]["version"]
    generator = Ironmon.starter_species_generator(entry["seed"], rules, version)
    offers = [1, 4, 7].each_with_index.map do |source, index|
      number = generator.map_number(source, [:starter, index])
      number > NB_POKEMON ? Ironmon.fusion_species_identity(number).to_s : GameData::Species.get(number).id.to_s
    end
    { "seed" => entry["seed"], "species" => offers }
  end
  File.binwrite(File.join(output_root, "replayed-offers.json"), Ironmon.tracker_json_generate(replay))

  # Reproduce a first-rival loss using the latest completed run's exact rules and offers.
  row = runs.last
  $PokemonGlobal.ironmon_seed = row["seed"]
  $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.from(row["rules"])
  $PokemonGlobal.ironmon_run_result = nil
  $PokemonGlobal.ironmon_run_id = row["run_id"]
  $PokemonGlobal.ironmon_run_ledger["current_attempt"].merge!("run_id" => row["run_id"], "seed" => row["seed"], "result" => "active")
  $PokemonGlobal.ironmon_starter_species = replay.last["species"].map(&:to_sym)
  $PokemonGlobal.ironmon_rival_lab_decision = nil
  Ironmon.reset_species_generator_cache
  $game_variables[VAR_HOENN_CHOSEN_STARTER_INDEX] = 0
  rival = initializeRivalBattledTrainer
  $PokemonGlobal.battledTrainers = { BATTLED_TRAINER_RIVAL_KEY => rival }
  phases = []
  watched = [:complete_run, :publish_run_completion, :tracker_completed_run_recipe,
    :tracker_run_reproduction_recipe, :tracker_current_story_trainer_previews,
    :tracker_story_trainer_recipe, :queue_early_game_loss_reset, :start_checkpoint_reset]
  stacks = Hash.new { |hash, key| hash[key] = [] }
  trace = TracePoint.new(:call, :return) do |event|
    next if !watched.include?(event.method_id)
    if event.event == :call
      stacks[event.method_id] << System.uptime
    elsif (started = stacks[event.method_id].pop)
      phases << { "method" => event.method_id.to_s, "milliseconds" => (System.uptime - started) / 1000.0 }
    end
  end
  started = Ironmon.tracker_uptime_seconds
  Ironmon.complete_run(:lost)
  elapsed = Ironmon.tracker_uptime_seconds - started
  recipe = Ironmon.run_ledger["last_completed_recipe"]
  raise "Opening loss eagerly prepared future teams" if !recipe["story_trainer_previews"].empty?
  raise "Opening context missing" if recipe["opening_rival_context"]["starter_species_ids"] != replay.last["species"]
  before = (Ironmon.instance_variable_get(:@tracker_obtainability_services) || {}).length
  paused = Ironmon.tracker_obtainability_for_recipe({ "foreground" => true }, {})
  raise "Lost opening did not pause lookup" if paused["phase"] != "waiting_for_reset"
  after = (Ironmon.instance_variable_get(:@tracker_obtainability_services) || {}).length
  raise "Paused lookup constructed a service" if before != after
  raise "Recovery rebuilt the completed recipe" if !Ironmon.tracker_recoverable_completed_run_recipe.equal?(recipe)
  connection = Ironmon::TrackerConnection.new
  request = { "request_id" => "deferred-loss-lookup", "command" => "pokemon_search",
    "run_id" => recipe["run_id"], "payload" => { "recipe" => recipe, "query" => "" } }
  connection.send(:handle_request, request)
  raise "Archive request was processed during loss" if connection.instance_variable_get(:@deferred_lookup_requests) != [request]
  raise "Paused archive request sent a response" if !connection.instance_variable_get(:@output_buffer).empty?
  connection.send(:resume_deferred_lookup_request)
  raise "Paused queue resumed too early" if connection.instance_variable_get(:@deferred_lookup_requests).empty?
  Ironmon.instance_variable_set(:@main_menu_open, true)
  connection.send(:resume_deferred_lookup_request)
  raise "Archive queue did not resume at the title screen" if !connection.instance_variable_get(:@deferred_lookup_requests).empty?
  raise "Resumed request bypassed normal validation" if !connection.instance_variable_get(:@output_buffer).include?("invalid_query")
  Ironmon.instance_variable_set(:@main_menu_open, false)
  started = Ironmon.tracker_uptime_seconds
  expected = Ironmon.tracker_current_story_trainer_previews
  eager_ms = (Ironmon.tracker_uptime_seconds - started) * 1000
  Ironmon.instance_variable_set(:@tracker_story_trainer_preview_cache, {})
  started = Ironmon.tracker_uptime_seconds
  trace.enable { Ironmon.tracker_current_story_trainer_previews }
  traced_eager_ms = (Ironmon.tracker_uptime_seconds - started) * 1000
  raise "Baseline lacked future rival teams" if expected.values.compact.length < 3
  flat = Ironmon.tracker_flatten_completed_recipe(recipe)
  $PokemonGlobal.ironmon_seed += 7
  $PokemonGlobal.ironmon_starter_species = nil
  $PokemonGlobal.battledTrainers = {}
  $game_variables[VAR_HOENN_CHOSEN_STARTER_INDEX] = 2
  preserved = Marshal.dump([$PokemonGlobal, $Trainer, $game_variables, $game_switches])
  generation = [:@pending_generated_evolutions, :@committed_generated_evolutions,
    :@base_stat_generator, :@evolution_generator, :@trainer_species_generator].map do |name|
    [name, Ironmon.instance_variable_get(name)]
  end
  entries = Ironmon.tracker_area_catalog.flat_map { |area| area["trainers"] }.
    select { |entry| entry["battle_kind"] == "rival" }
  entries.each do |entry|
    preview = Ironmon.tracker_archived_opening_rival_preview(flat, entry)
    raise "Archived stage #{entry["story_stage"]} differs: #{preview.inspect}; expected #{expected[entry["entry_id"]].inspect}" if
      preview != expected[entry["entry_id"]]
  end
  raise "Archive replay changed live save state" if preserved != Marshal.dump([$PokemonGlobal, $Trainer, $game_variables, $game_switches])
  replaced = generation.select { |name, value| !Ironmon.instance_variable_get(name).equal?(value) }
  raise "Archive replay replaced live generators: #{replaced.map { |name, value| [name, value.object_id, Ironmon.instance_variable_get(name).object_id] }.inspect}" if !replaced.empty?
  globals = [$PokemonGlobal, $Trainer, $game_variables, $game_switches]
  $PokemonGlobal = $Trainer = $game_variables = $game_switches = nil
  title_recipe = flat.merge("run_id" => "title-archive-replay")
  raise "Title archive lost rival team" if Ironmon.tracker_archived_opening_rival_preview(title_recipe, entries.last) != expected[entries.last["entry_id"]]
  raise "Title archive left a loaded save" if $PokemonGlobal || $Trainer || $game_variables || $game_switches
  $PokemonGlobal, $Trainer, $game_variables, $game_switches = globals
  unknown_context = flat["opening_rival_context"].merge(
    "starter_species_ids" => ["BULBASAUR", "CHARMANDER", "SQUIRTLE"], "lab_species_id" => nil)
  unknown_recipe = flat.merge("run_id" => "unprepared-fusion-loss", "opening_rival_context" => unknown_context)
  later_entry = entries.find { |entry| entry["story_stage"] == 2 }
  raise "Unprepared fusion was fabricated during archive lookup" if Ironmon.tracker_archived_opening_rival_preview(unknown_recipe, later_entry)
  normal_recipe = unknown_recipe.merge("run_id" => "normal-only-loss",
    "configuration" => Ironmon::Configuration.from(flat["configuration"]).to_h.merge(:trainer_policy => :normal_only))
  normal_preview = Ironmon.tracker_archived_opening_rival_preview(normal_recipe, later_entry)
  raise "Normal-only archive lost its rival team: #{normal_preview.inspect}" if !normal_preview || normal_preview.empty? ||
    normal_preview.any? { |member| member["species_id"].start_with?("B") && /\AB\d+H\d+:/.match?(member["species_id"]) }
  File.binwrite(Ironmon.checkpoint_path, File.binread(File.join(output_root, "Checkpoint-G.rxdata")))
  load_phases = []
  original_wild_mapping = Ironmon.method(:wild_species_for)
  Ironmon.define_singleton_method(:wild_species_for) do |*arguments|
    raise "Checkpoint mapped encounters for the discarded seed" if checkpoint_reset_loading?
    original_wild_mapping.call(*arguments)
  end
  generation_probe = Module.new
  [:apply_preset, :prepare_custom_fusion_pool, :prepare_player_fusion_pairing,
   :prepare_ability_randomization, :prepare_base_stat_randomization, :prepare_evolution_randomization,
   :prepare_move_access_randomization, :prepare_item_randomization, :prepare_species_mappings,
   :prepare_rival_lab_resources, :begin_run_attempt, :refresh_loaded_wild_encounter_table].each do |name|
    generation_probe.send(:define_method, name) do |*arguments, **keywords, &block|
      phase_started = tracker_uptime_seconds
      value = super(*arguments, **keywords, &block)
      load_phases << { "hook" => name.to_s, "phase" => "reset_generation",
        "milliseconds" => (tracker_uptime_seconds - phase_started) * 1000 }
      value
    end
  end
  Ironmon.singleton_class.prepend(generation_probe)
  [[Game.singleton_class, :load_map], [SaveData.singleton_class, :load_all_values],
   [Object, :onLoadExistingGame], [Object, :pbAutoplayOnSave], [Game_Map, :update],
   [$PokemonMap.class, :updateMap], [PokemonEncounters, :setup],
   [$MapFactory.class, :setMapChanged], [$MapFactory.class, :setup],
   [Game.singleton_class, :load_secret_bases], [Object, :pbUpdateVehicle]].each do |owner, name|
    original = owner.instance_method(name)
    owner.send(:define_method, name) do |*arguments|
      phase_started = Ironmon.tracker_uptime_seconds
      value = original.bind(self).call(*arguments)
      load_phases << { "hook" => name.to_s, "phase" => "base_game",
        "milliseconds" => (Ironmon.tracker_uptime_seconds - phase_started) * 1000 }
      value
    end
  end
  Ironmon.instance_variable_get(:@game_load_hooks).each do |hook|
    [:before, :after].each do |phase|
      callback = hook[phase]
      next if !callback
      hook[phase] = proc do |*arguments|
        phase_started = Ironmon.tracker_uptime_seconds
        value = callback.call(*arguments)
        load_phases << { "hook" => hook[:name].to_s, "phase" => phase.to_s,
          "milliseconds" => (Ironmon.tracker_uptime_seconds - phase_started) * 1000 }
        value
      end
    end
  end
  reset_started = Ironmon.tracker_uptime_seconds
  raise "Owned checkpoint reset did not start" if !Ironmon.start_checkpoint_reset(true)
  reset_queued = Ironmon.tracker_uptime_seconds
  $scene.main
  checkpoint_loaded = Ironmon.tracker_uptime_seconds
  raise "Checkpoint did not load a map scene" if !$scene.is_a?(Scene_Map)
  $scene.createSpritesets
  reset_finished = Ironmon.tracker_uptime_seconds
  raise "Owned checkpoint reset failed: #{Ironmon.instance_variable_get(:@reset_notice).inspect}" if Ironmon.instance_variable_get(:@reset_in_progress) ||
    Ironmon.current_run_attempt["result"] != "active" || $PokemonGlobal.ironmon_seed == row["seed"]
  mode = $PokemonEncounters.getEncounterMode
  data = mode.get($game_map.map_id, $PokemonGlobal.encounter_version) ||
    GameData::Encounter.get($game_map.map_id, $PokemonGlobal.encounter_version)
  tables = $PokemonEncounters.instance_variable_get(:@encounter_tables)
  data.types.each do |type, slots|
    slots.each_with_index do |slot, index|
      expected_species = Ironmon.wild_species_for(slot[1], [:table, mode.name, data.map, data.version, type, index])
      raise "Replacement attempt retained a stale encounter table" if tables[type][index][1] != expected_species
    end
  end
  $scene.disposeSpritesets
  warm_started = Ironmon.tracker_uptime_seconds
  Ironmon.complete_run(:lost)
  raise "Warm checkpoint reset did not start" if !Ironmon.start_checkpoint_reset(true)
  $scene.main
  $scene.createSpritesets
  warm_finished = Ironmon.tracker_uptime_seconds
  raise "Warm reset did not restore a playable attempt" if Ironmon.instance_variable_get(:@reset_in_progress) ||
    Ironmon.current_run_attempt["result"] != "active"
  File.binwrite(File.join(output_root, "loss-timing.json"), Ironmon.tracker_json_generate({
    "completion_ms" => elapsed * 1000, "phases" => phases,
    "traced_eager_ms" => traced_eager_ms, "untraced_eager_ms" => eager_ms,
    "recipe_previews" => recipe["story_trainer_previews"].keys,
    "archive_stages_verified" => entries.length,
    "reset_stage_ms" => (reset_queued - reset_started) * 1000,
    "checkpoint_load_ms" => (checkpoint_loaded - reset_queued) * 1000,
    "new_attempt_and_sprites_ms" => (reset_finished - checkpoint_loaded) * 1000,
    "checkpoint_reset_total_ms" => (reset_finished - reset_started) * 1000,
    "warm_loss_and_reset_ms" => (warm_finished - warm_started) * 1000,
    "load_phases" => load_phases
  }))
  File.binwrite(File.join(output_root, "result.txt"), "Passed native starter replay, fast loss completion, paused lookup, and isolated live/title archive replay.\n")
  exit!(0)
rescue Exception => error
  File.binwrite(File.join(output_root, "result.txt"), "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")
  exit!(1)
end
