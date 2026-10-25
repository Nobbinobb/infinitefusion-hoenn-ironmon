#===============================================================================
# Tracker-owned Ironmon creation, title-menu loading and recoverable session gate
#===============================================================================
require "digest"

module Ironmon
  GAME_MANAGEMENT_PROTOCOL = 1
  TRACKER_HEARTBEAT_TIMEOUT = 8.0
  class TrackerRequiredError < StandardError; end

  def self.game_session_id
    @game_session_id ||= "#{Process.pid}:#{Time.now.to_f}:#{rand(2_147_483_647)}"
  end

  def self.tracker_ready?
    return tracker_connection.session_ready?
  end

  def self.automatic_reset_enabled?
    return tracker_connection.automatic_reset?
  end

  def self.saved_ironmon?(data)
    global = data.is_a?(Hash) ? data[:global_metadata] : nil
    return global && global.ironmon_mode == true
  end

  def self.save_revision(slot)
    path = SaveData.get_full_path(slot)
    return "empty" if !File.file?(path)
    return Digest::SHA256.hexdigest(File.binread(path))
  end

  def self.game_management_snapshot
    snapshot = { "protocol_version" => GAME_MANAGEMENT_PROTOCOL,
      "state" => @main_menu_open ? @management_screen_state : "gameplay",
      "menu_id" => @main_menu_open ? @main_menu_id : nil, "slots" => [] }
    return snapshot if !@main_menu_open
    @management_slot_snapshots ||= {}
    (SaveData::MANUAL_SLOTS + SaveData::AUTO_SLOTS).each do |slot|
      path = SaveData.get_full_path(slot)
      signature = File.file?(path) ? [File.size(path), File.mtime(path).to_f] : nil
      cached = @management_slot_snapshots[slot]
      if cached && cached[0] == signature
        snapshot["slots"] << cached[1]
        next
      end
      entry = { "slot" => slot, "manual" => SaveData::MANUAL_SLOTS.include?(slot),
        "revision" => save_revision(slot), "occupied" => File.file?(path),
        "ironmon" => false, "loadable" => false }
      if entry["occupied"]
        begin
          data = SaveData.read_from_file(path)
          entry["ironmon"] = !!saved_ironmon?(data)
          entry["loadable"] = SaveData.valid?(data)
          player = data[:player]
          entry["trainer"] = player ? player.name.to_s : ""
          entry["badges"] = player ? player.badge_count : 0
          if entry["ironmon"]
            global = data[:global_metadata]
            rules = global.ironmon_configuration
            rules ||= Configuration.new
            entry["loadable"] &&= rules.is_a?(Configuration) && rules.current?
            if entry["loadable"]
              entry["rules"] = { "wild_policy" => rules.wild_policy.to_s,
                "starter_policy" => rules.starter_policy.to_s,
                "trainer_policy" => rules.trainer_policy.to_s,
                "unfusion_setting" => rules.unfusion_setting.to_s }
            end
            ledger = global.ironmon_run_ledger
            attempt = ledger.is_a?(Hash) ? ledger["current_attempt"] : nil
            entry["attempt_number"] = attempt["attempt_number"] if attempt.is_a?(Hash)
          end
          map = data[:map_factory]
          entry["location"] = map && map.map ? pbGetMapNameFromId(map.map.map_id) : ""
        rescue StandardError
          entry["loadable"] = false
        end
      end
      snapshot["slots"] << entry
      @management_slot_snapshots[slot] = [signature, entry]
    end
    return snapshot
  end

  def self.open_game_menu(screen, state = "main_menu")
    @load_screen = screen
    @management_slot_snapshots = {}
    @main_menu_id = "#{game_session_id}:#{rand(2_147_483_647)}"
    @main_menu_open = true
    @management_screen_state = state
  end

  def self.close_game_menu
    @main_menu_open = false
    @management_screen_state = nil
    @load_screen = nil
    @game_management_request = nil
  end

  def self.request_game_management(command, payload)
    raise "Return to the game's title screen or main menu first." if !@main_menu_open
    raise "A compatible tracker must be connected." if !tracker_ready?
    raise "A game action is already pending." if @game_management_request
    raise "The main menu has changed. Refresh and try again." if
      !payload.is_a?(Hash) || payload["menu_id"] != @main_menu_id
    slot = payload["slot"]
    allowed = command == "start_ironmon" ? SaveData::MANUAL_SLOTS :
      SaveData::MANUAL_SLOTS + SaveData::AUTO_SLOTS
    raise "Invalid save slot." if !allowed.include?(slot)
    raise "The save has changed. Refresh and try again." if
      payload["revision"] != save_revision(slot)
    if command == "start_ironmon"
      raise "Confirm overwriting this save first." if
        File.file?(SaveData.get_full_path(slot)) && payload["overwrite"] != true
      rules = payload["rules"]
      raise "Invalid Ironmon rules." if !rules.is_a?(Hash) ||
        rules.keys.sort != ["starter_policy", "trainer_policy", "unfusion_setting", "wild_policy"] ||
        !Configuration::POLICY_IDS.map(&:to_s).include?(rules["wild_policy"]) ||
        !Configuration::POLICY_IDS.map(&:to_s).include?(rules["trainer_policy"]) ||
        !Configuration::POLICY_IDS.map(&:to_s).include?(rules["starter_policy"]) ||
        !Configuration::UNFUSION_SETTING_IDS.map(&:to_s).include?(rules["unfusion_setting"])
    else
      data = SaveData.read_from_file(SaveData.get_full_path(slot))
      raise "This slot is not a valid Ironmon save." if
        !saved_ironmon?(data) || !SaveData.valid?(data)
    end
    @game_management_request = Marshal.load(Marshal.dump(payload.merge("command" => command)))
    return { "accepted" => true }
  end

  def self.dispatch_game_management
    return if !@main_menu_open || !@game_management_request
    return if !tracker_connection.output_drained?
    # The response has been flushed before unwinding the title-menu chooser.
    request = @game_management_request
    @game_management_request = nil
    throw :ironmon_game_management, request
  end

  def self.perform_game_management(request, load_screen = nil)
    raise "The tracker disconnected. Try again." if !tracker_ready?
    slot = request["slot"]
    raise "The save has changed. Try again." if request["revision"] != save_revision(slot)
    if request["command"] == "continue_ironmon"
      data = SaveData.read_from_file(SaveData.get_full_path(slot))
      raise "This is not a valid Ironmon save." if !saved_ironmon?(data) || !SaveData.valid?(data)
      SaveData.mark_values_as_unloaded
      Game.load(data)
      $game_switches[SWITCH_V5_1] = true
      raise "The game's load screen is unavailable." if !load_screen
      load_screen.ensureCorrectDifficulty
      load_screen.setGameMode
      initialize_alt_sprite_substitutions
      $PokemonGlobal.autogen_sprites_cache = {}
      preload_party(data[:player])
      return true
    end
    path = SaveData.get_full_path(slot)
    checkpoint = checkpoint_path_for_slot(slot)
    old_save = File.file?(path) ? File.binread(path) : nil
    old_checkpoint = File.file?(checkpoint) ? File.binread(checkpoint) : nil
    begin
      deactivate_run_generation_profile
      SaveData.mark_values_as_unloaded
      Game.start_new
      rules = request["rules"]
      self.configuration = Configuration.new(rules["wild_policy"], rules["trainer_policy"], rules["unfusion_setting"], false, rules["starter_policy"])
      $Trainer.save_slot = slot
      raise generation_error_message if !apply_preset(:new_run, nil, false)
      raise "The tracker disconnected. Try again." if !tracker_ready?
      raise "The new Ironmon game could not be saved." if !save_new_ironmon_slot(slot)
      File.delete(checkpoint) if File.file?(checkpoint)
      initialize_alt_sprite_substitutions
      start_tracker_run
      return true
    rescue Exception
      restore_management_file(path, old_save)
      restore_management_file(checkpoint, old_checkpoint)
      $PokemonGlobal.ironmon_mode = false if $PokemonGlobal
      raise
    end
  end

  def self.save_new_ironmon_slot(slot)
    path = SaveData.get_full_path(slot)
    temporary = "#{path}.ironmon-new"
    Game.backup_savefile(path, slot)
    @management_staging_slot = slot
    @management_staging_path = temporary
    saved = Game.save(slot, false, safe: true)
    @management_staging_slot = nil
    @management_staging_path = nil
    return false if !saved
    data = SaveData.read_from_file(temporary)
    raise "The new Ironmon save did not validate." if !SaveData.valid?(data) || !saved_ironmon?(data)
    File.rename(temporary, path)
    return true
  ensure
    @management_staging_slot = nil
    @management_staging_path = nil
    File.delete(temporary) if temporary && File.file?(temporary)
  end

  def self.restore_management_file(path, bytes)
    if bytes
      temporary = "#{path}.ironmon-restore"
      File.binwrite(temporary, bytes)
      File.delete(path) if File.file?(path)
      File.rename(temporary, path)
    elsif File.file?(path)
      File.delete(path)
    end
  end

  def self.wait_for_tracker_session
    return if !$scene.is_a?(Scene_Map) || !active? || tracker_ready? || checkpoint_reset_loading? || restoring_live_snapshot? ||
      @waiting_for_tracker || @management_load_screen || @reset_in_progress || @seed_import_in_progress
    @waiting_for_tracker = true
    tick_active_run_duration
    viewport = Viewport.new(0, 0, Graphics.width, Graphics.height)
    viewport.z = 999999
    sprite = Sprite.new(viewport)
    sprite.bitmap = Bitmap.new(Graphics.width, Graphics.height)
    sprite.bitmap.fill_rect(0, 0, Graphics.width, Graphics.height, Color.new(9, 13, 18, 235))
    sprite.bitmap.font.size = 22
    sprite.bitmap.draw_text(0, Graphics.height / 2 - 50, Graphics.width, 40, "Ironmon paused", 1)
    sprite.bitmap.font.size = 16
    sprite.bitmap.draw_text(0, Graphics.height / 2, Graphics.width, 40, "Reconnect the tracker to continue this attempt.", 1)
    until tracker_ready?
      Graphics.ironmon_runtime_hooks_original_update
      Input.update
      update_tracker_connection
    end
    Input.update
  ensure
    if @waiting_for_tracker
      sprite.bitmap.dispose if sprite && sprite.bitmap
      sprite.dispose if sprite
      viewport.dispose if viewport
      resume_run_duration
      @waiting_for_tracker = false
    end
  end
end

module IronmonGameTitleScreen
  def closeTitle
    Ironmon.close_game_menu
    super
  end

  def closeTitleDelete
    Ironmon.close_game_menu
    super
  end

  def update
    Ironmon.open_game_menu(nil, "title_screen")
    request = catch(:ironmon_game_management) { super; nil }
    return if !request
    Ironmon.close_game_menu
    pbBGMStop(1.0)
    disposeTitle
    begin
      # No native chooser is opened, so unrelated startup dialogs and the last
      # selected save cannot interrupt the action confirmed in the tracker.
      Ironmon.perform_game_management(request, PokemonLoadScreen.allocate)
    rescue StandardError => error
      pbMessage(_INTL("Ironmon could not open this run: {1}", error.message))
      $scene = Scene_Intro.new
    end
  ensure
    Ironmon.close_game_menu
  end
end

module IronmonGameMenuScreen
  def pbStartLoadScreen
    loop do
      request = catch(:ironmon_game_management) { super; nil }
      return if !request
      Ironmon.close_game_menu
      @scene.pbEndScene
      begin
        return if Ironmon.perform_game_management(request, self)
      rescue StandardError => error
        pbMessage(_INTL("Ironmon could not open this run: {1}", error.message))
        $scene = Scene_Intro.new
        return
      end
    end
  rescue Ironmon::TrackerRequiredError => error
    pbMessage(error.message)
    $scene = Scene_Intro.new
    return
  ensure
    Ironmon.close_game_menu
  end
end

module IronmonGameMenuChooser
  def pbChoose(commands, continue_idx)
    screen = Ironmon.instance_variable_get(:@management_load_screen)
    Ironmon.open_game_menu(screen)
    loop do
      command = super
      data = screen ? screen.instance_variable_get(:@save_data) : nil
      return command if command != continue_idx || !Ironmon.saved_ironmon?(data) || Ironmon.tracker_ready?
      Ironmon.close_game_menu
      pbMessage(_INTL("Connect the Ironmon tracker before continuing this save."))
      Ironmon.open_game_menu(screen)
    end
  ensure
    Ironmon.close_game_menu
  end
end

module IronmonGameMenuOwner
  def pbStartLoadScreen
    Ironmon.instance_variable_set(:@management_load_screen, self)
    super
  ensure
    Ironmon.instance_variable_set(:@management_load_screen, nil)
  end
end

module IronmonGameLoadGate
  def load(data)
    if Ironmon.saved_ironmon?(data) && !Ironmon.checkpoint_reset_loading? && !Ironmon.restoring_live_snapshot? && !Ironmon.tracker_ready?
      raise Ironmon::TrackerRequiredError, "Connect the Ironmon tracker before loading this save."
    end
    super
  end
end

module IronmonStagedSavePath
  def get_full_path(slot)
    if slot == Ironmon.instance_variable_get(:@management_staging_slot)
      path = Ironmon.instance_variable_get(:@management_staging_path)
      return path if path
    end
    super
  end
end

PokemonLoadScreen.prepend(IronmonGameMenuScreen)
Scene_Intro.prepend(IronmonGameTitleScreen)
PokemonLoadScreen.prepend(IronmonGameMenuOwner)
PokemonLoad_Scene.prepend(IronmonGameMenuChooser)
Game.singleton_class.prepend(IronmonGameLoadGate)
SaveData.singleton_class.prepend(IronmonStagedSavePath)
