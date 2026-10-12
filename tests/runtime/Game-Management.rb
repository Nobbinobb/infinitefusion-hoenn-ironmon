module IronmonGameManagementRuntimeTests
  OUTPUT_PATH = $ironmon_game_management_test_output_path.to_s

  def self.assert(condition, message)
    raise "Game-management runtime test failed: #{message}" if !condition
  end

  def self.rejects(message)
    begin
      yield
    rescue StandardError => error
      assert(error.message.include?(message), "expected #{message.inspect}, got #{error.message.inspect}")
      return
    end
    raise "Game-management runtime test expected rejection: #{message}"
  end

  def self.payload(slot, rules = true)
    value = { "menu_id" => Ironmon.instance_variable_get(:@main_menu_id),
      "slot" => slot, "revision" => Ironmon.save_revision(slot), "overwrite" => false }
    value["rules"] = { "wild_policy" => "normal_only", "trainer_policy" => "mixed",
      "unfusion_setting" => "player_choice" } if rules
    return value
  end

  def self.run
    raise "The game-management save fixture path is missing." if $ironmon_game_management_test_save_root.to_s.empty?
    test_saves = File.expand_path($ironmon_game_management_test_save_root.to_s)
    Dir.mkdir(test_saves) if !Dir.exist?(test_saves)
    SaveData.send(:remove_const, :SAVE_DIR)
    SaveData.const_set(:SAVE_DIR, test_saves)
    Game.initialize
    SaveData.initialize_bootup_values
    connection = Ironmon::TrackerConnection.new
    connection.instance_variable_set(:@state, :connected)
    connection.instance_variable_set(:@session_ready, false)
    connection.instance_variable_set(:@output_buffer, "")
    connection.define_singleton_method(:update) { }
    Ironmon.instance_variable_set(:@tracker_connection, connection)
    Ironmon.open_game_menu(nil)
    empty = Ironmon.game_management_snapshot
    assert(empty["slots"].map { |slot| slot["slot"] } == SaveData::MANUAL_SLOTS + SaveData::AUTO_SLOTS, "all eight lettered saves and both autosaves come from the actual game")
    assert(empty["slots"].all? { |slot| !slot["occupied"] }, "fixture starts with no saves")
    rejects("compatible tracker") { Ironmon.request_game_management("start_ironmon", payload("File A")) }
    handshake = { "schema_version" => 1, "type" => "event", "event" => "tracker_connected", "payload" => {
      "management_protocol_version" => 1, "session_id" => "wrong", "game_root" => File.expand_path(".") } }
    connection.send(:handle_message, handshake)
    assert(!Ironmon.tracker_ready?, "a foreign game session cannot authorize Ironmon")
    handshake["payload"]["session_id"] = Ironmon.game_session_id
    connection.send(:handle_message, handshake)
    assert(Ironmon.tracker_ready? && Ironmon.automatic_reset_enabled?, "negotiated matching tracker enables default automatic reset")
    malformed = payload("File A"); malformed["rules"]["wild_policy"] = "invalid"
    rejects("Invalid Ironmon rules") { Ironmon.request_game_management("start_ironmon", malformed) }
    rejects("Invalid save slot") { Ironmon.request_game_management("start_ironmon", payload("Auto 1")) }
    stale = payload("File A"); stale["menu_id"] = "old-menu"
    rejects("menu has changed") { Ironmon.request_game_management("start_ironmon", stale) }
    request = payload("File A")
    assert(Ironmon.request_game_management("start_ironmon", request)["accepted"], "new title action is accepted")
    rejects("already pending") { Ironmon.request_game_management("start_ironmon", request) }
    action = catch(:ironmon_game_management) { Ironmon.dispatch_game_management }
    Ironmon.close_game_menu
    assert(Ironmon.perform_game_management(action), "real new game, preset and save complete")
    path = SaveData.get_full_path("File A")
    saved = SaveData.read_from_file(path)
    assert(SaveData.valid?(saved) && Ironmon.saved_ironmon?(saved), "new slot is a valid native Ironmon save")
    assert(saved[:player].save_slot == "File A", "save slot is bound to the selected letter")
    assert(saved[:global_metadata].ironmon_configuration.wild_policy == :normal_only, "tracker rules persist in native save metadata")
    assert(select_game_mode == :IRONMON, "intro reuses tracker-created mode without the old configuration menu")
    original_seed = $PokemonGlobal.ironmon_seed
    original_rules = Ironmon.configuration_snapshot
    connection.send(:handle_message, { "schema_version" => 1, "type" => "request", "request_id" => "live-reset", "command" => "update_settings", "payload" => { "automatic_reset" => false } })
    assert(!Ironmon.automatic_reset_enabled? && original_rules == Ironmon.configuration_snapshot && original_seed == $PokemonGlobal.ironmon_seed, "live reset changes neither seed nor saved rules")
    copy = Marshal.load(Marshal.dump(saved)); copy[:global_metadata].ironmon_mode = false
    File.binwrite(SaveData.get_full_path("File B"), Marshal.dump(copy))
    File.binwrite(SaveData.get_full_path("Auto 1"), Marshal.dump(saved))
    Ironmon.open_game_menu(nil)
    slots = Ironmon.game_management_snapshot["slots"]
    assert(slots.select { |slot| slot["ironmon"] }.map { |slot| slot["slot"] } == ["File A", "Auto 1"], "continue distinguishes Ironmon autosaves from normal saves")
    rejects("not a valid Ironmon") { Ironmon.request_game_management("continue_ironmon", payload("File B", false)) }
    rejects("Confirm overwriting") { Ironmon.request_game_management("start_ironmon", payload("File A")) }
    stale = payload("File A"); stale["overwrite"] = true
    File.binwrite(path, File.binread(path) + "changed")
    rejects("save has changed") { Ironmon.request_game_management("start_ironmon", stale) }
    File.binwrite(path, Marshal.dump(saved))
    checkpoint = Ironmon.checkpoint_path_for_slot("File A")
    File.binwrite(checkpoint, "original checkpoint")
    original_bytes = File.binread(path)
    overwrite = payload("File A"); overwrite["overwrite"] = true
    assert(Ironmon.request_game_management("start_ironmon", overwrite)["accepted"], "explicit overwrite is accepted")
    connection.instance_variable_set(:@output_buffer, "")
    action = catch(:ironmon_game_management) { Ironmon.dispatch_game_management }
    Ironmon.close_game_menu
    game_methods = Game.singleton_class
    game_methods.alias_method(:management_test_original_save, :save)
    game_methods.define_method(:save) do |slot, _auto = false, safe: false|
      File.binwrite(SaveData.get_full_path(slot), "partial failed save")
      false
    end
    rejects("could not be saved") { Ironmon.perform_game_management(action) }
    game_methods.alias_method(:save, :management_test_original_save)
    game_methods.remove_method(:management_test_original_save)
    assert(File.binread(path) == original_bytes && File.binread(checkpoint) == "original checkpoint", "failed overwrite restores exact save and checkpoint bytes")
    Ironmon.open_game_menu(nil)
    replace_normal = payload("File B"); replace_normal["overwrite"] = true
    assert(Ironmon.request_game_management("start_ironmon", replace_normal)["accepted"], "confirmed normal save can be replaced")
    connection.instance_variable_set(:@output_buffer, "")
    action = catch(:ironmon_game_management) { Ironmon.dispatch_game_management }
    Ironmon.close_game_menu
    assert(Ironmon.perform_game_management(action), "staged save replaces occupied slot in the bundled Windows runtime")
    assert(Ironmon.saved_ironmon?(SaveData.read_from_file(SaveData.get_full_path("File B"))), "normal slot now contains new Ironmon run")
    assert(Dir.glob(File.join(SaveData::SAVE_DIR, "backups", "File B", "*.rxdata")).length > 0, "normal native save backup is retained")
    Ironmon.open_game_menu(nil)
    continue_request = payload("Auto 1", false)
    assert(Ironmon.request_game_management("continue_ironmon", continue_request)["accepted"], "tracker can continue an Ironmon autosave")
    connection.instance_variable_set(:@output_buffer, "")
    action = catch(:ironmon_game_management) { Ironmon.dispatch_game_management }
    Ironmon.close_game_menu
    assert(Ironmon.perform_game_management(action, PokemonLoadScreen.allocate) && $PokemonGlobal.ironmon_seed == saved[:global_metadata].ironmon_seed, "native tracker continue restores the saved attempt")
    connection.instance_variable_set(:@session_ready, false)
    rejects("Connect the Ironmon tracker") { Game.load(saved) }
    assert(!Ironmon.saved_ironmon?(copy), "normal playthrough remains outside the Ironmon gate")
    $PokemonGlobal = saved[:global_metadata]
    $Trainer = saved[:player]
    playing_scene = $scene
    $scene = Object.new
    Ironmon.wait_for_tracker_session
    assert(!Ironmon.instance_variable_get(:@waiting_for_tracker), "returning to the title remains available without a tracker")
    $scene = playing_scene
    ticks = 0
    connection.define_singleton_method(:update) do
      ticks += 1
      @session_ready = true if ticks == 3
    end
    before = $PokemonGlobal.ironmon_seed
    Ironmon.wait_for_tracker_session
    assert(ticks == 3 && $PokemonGlobal.ironmon_seed == before, "native graphics pause pumps reconnect without restarting or replacing the attempt")
    $PokemonGlobal.ironmon_mode = false
    Settings.send(:remove_const, :STARTUP_MESSAGES_HOENN)
    Settings.const_set(:STARTUP_MESSAGES_HOENN, "")
    System.define_singleton_method(:data_directory) { test_saves }
    load_screen = PokemonLoadScreen.new(PokemonLoad_Scene.new)
    [:updateHttpSettingsFile, :updateCustomDexFile, :checkEnableSpritesDownload,
      :copyKeybindings].each { |method| load_screen.define_singleton_method(method) { } }
    load_screen.define_singleton_method(:find_newer_available_version) { nil }
    issued = false
    connection.define_singleton_method(:update) do
      if !issued && Ironmon.instance_variable_get(:@main_menu_open)
        issued = true
        Ironmon.request_game_management("continue_ironmon", IronmonGameManagementRuntimeTests.payload("File A", false))
        @output_buffer = ""
      end
    end
    load_screen.pbStartLoadScreen
    assert(issued && $PokemonGlobal.ironmon_seed == saved[:global_metadata].ironmon_seed,
      "actual native title chooser unwinds into tracker continue and loads the selected save")
    assert(!Ironmon.instance_variable_get(:@main_menu_open) && !Ironmon.instance_variable_get(:@management_load_screen),
      "title menu ownership is revoked after tracker loading")
    transition = Scene_Intro.new
    transition.define_singleton_method(:disposeTitle) do
      IronmonGameManagementRuntimeTests.assert(!Ironmon.instance_variable_get(:@main_menu_open),
        "a normal title keypress revokes management before the title animation and startup dialogs")
      raise "native title transition reached"
    end
    Ironmon.open_game_menu(nil, "title_screen")
    rejects("native title transition reached") { transition.closeTitle }
    assert(Ironmon.game_management_snapshot["state"] == "gameplay" && Ironmon.game_management_snapshot["slots"].empty?,
      "startup dialogs never advertise title-screen run management")
    test_title_screen(connection, "start_ironmon", "File C")
    assert($Trainer.save_slot == "File C" && SaveData.valid?(SaveData.read_from_file(SaveData.get_full_path("File C"))),
      "the title screen starts and saves a new Ironmon run without the native chooser")
    test_title_screen(connection, "continue_ironmon", "File A")
    assert($Trainer.save_slot == "File A" && $PokemonGlobal.ironmon_seed == saved[:global_metadata].ironmon_seed,
      "the title screen continues the selected Ironmon save without the native chooser")
    connection.send(:disconnect)
    assert(!Ironmon.tracker_ready?, "disconnect revokes session authorization")
    File.binwrite(OUTPUT_PATH, "PASS: actual bundled runtime; all native slots; handshake/session gate; menu, rules and stale-save validation; native Ironmon creation and persistence; separate live reset; failed overwrite restores save/checkpoint; load gate; native graphics pause/reconnect preserves attempt; title-screen start and continue without chooser or keypress.\n")
  ensure
    Ironmon.close_game_menu
    $PokemonGlobal.ironmon_mode = false if $PokemonGlobal
  end

  def self.test_title_screen(connection, command, slot)
    issued = false
    intro = Scene_Intro.new
    screen = Settings::HOENN ? HoennIntroScreen.new : GenOneStyle.new
    intro.instance_variable_set(:@screen, screen)
    intro.define_singleton_method(:closeTitle) { raise "The native main menu must not open for a tracker title action." }
    $scene = intro
    connection.define_singleton_method(:update) do
      next if issued || Ironmon.instance_variable_get(:@management_screen_state) != "title_screen"
      issued = true
      snapshot = Ironmon.game_management_snapshot
      IronmonGameManagementRuntimeTests.assert(snapshot["state"] == "title_screen" && snapshot["slots"].length == SaveData::MANUAL_SLOTS.length + SaveData::AUTO_SLOTS.length,
        "the title screen exposes the actual save slots")
      Ironmon.request_game_management(command, IronmonGameManagementRuntimeTests.payload(slot, command == "start_ironmon"))
      @output_buffer = ""
    end
    intro.update
    assert(issued && $scene.is_a?(Scene_Map), "the actual animated title loop dispatches the tracker request without a keypress")
    assert(!Ironmon.instance_variable_get(:@main_menu_open), "title-screen management is revoked after dispatch")
  ensure
    screen.dispose if screen && !screen.instance_variable_get(:@disposed)
  end
end

IronmonGameManagementRuntimeTests.run
