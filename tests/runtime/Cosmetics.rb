module IronmonCosmeticRuntimeTests
  C = Ironmon::Cosmetics

  def self.assert(value, message)
    raise "Cosmetic test failed: #{message}" if !value
    @assertions += 1
  end

  def self.reject(message)
    raised = false
    begin
      yield
    rescue StandardError
      raised = true
    end
    assert(raised, message)
  end

  def self.appearance(catalog)
    clothes = catalog.available("clothes").first
    hair = catalog.available("hair").first
    hat = catalog.available("hat").first
    return { "clothes" => clothes["id"], "hair" => "#{hair["variants"].first}_#{hair["id"]}",
             "hat" => hat["id"], "hat2" => nil, "skin_tone" => 3,
             "hair_color" => 0, "clothes_color" => 0, "hat_color" => 0,
             "hat2_color" => 0, "bike_color" => 0 }
  end

  def self.test_prices
    [[100,20], [500,20], [501,40], [1500,40], [1501,80], [4000,80],
     [4001,150], [10000,150], [10001,250], [25000,250], [25001,400],
     [50000,400], [50001,600], [500000,600]].each do |price, expected|
      assert(C.points_for("hat", price) == expected * 5, "price boundary #{price}")
    end
    [nil, 0, -1, "bad"].each do |price|
      assert(C.points_for("clothes", price) == 400, "unpriced outfit fallback")
      assert(C.points_for("hair", price) == 100, "unpriced hair fallback")
    end
  end

  def self.test_profile(catalog, profile)
    draft = appearance(catalog)
    fresh = profile.read
    assert(!fresh["initial_claimed"], "a fresh profile has one free choice")
    assert(C.default_appearance_keys.all? { |key| fresh["owned"][key] },
           "both gender default outfits and hairstyles are permanently owned from the start")
    legacy = C::Profile.new(File.join(profile.directory, "legacy-defaults"))
    legacy.synchronize do
      state = legacy.empty
      state["points"] = 75
      legacy.write_unlocked(state)
    end
    migrated = legacy.read
    assert(migrated["points"] == 75 && !migrated["initial_claimed"] &&
           C.default_appearance_keys.all? { |key| migrated["owned"][key] },
           "existing profiles gain all starting defaults without changing progression")
    profile.confirm(catalog, draft, "slot:File A", true)
    state = profile.read
    expected_owned = (C.default_appearance_keys + C.appearance_keys(draft)).uniq.sort
    assert(state["initial_claimed"] && state["owned"].keys.sort == expected_owned,
           "defaults and only the final selected pieces are unlocked, not the browsed catalog")
    reject("a different new game cannot claim a second outfit") { profile.confirm(catalog, draft, "slot:File B", true) }
    assert(profile.read["points"] == 0, "free confirmation grants no currency")
    assert(profile.award("attempt-one", "trainer-one", 10), "first trainer reward")
    assert(!C::Profile.new(profile.directory).award("attempt-one", "trainer-one", 10), "reload does not repeat a reward")
    assert(profile.award("attempt-two", "trainer-one", 10), "a genuinely new attempt can earn it")
    assert(profile.award("attempt-one", "badge:0", 100), "badge award")
    assert(!profile.award("attempt-one", "badge:0", 100), "badge award is idempotent")
    assert(profile.award("attempt-one", "hall_of_fame", 500), "Hall of Fame award")
    before = profile.read["points"]
    item = catalog.available("clothes").find { |entry| entry["id"] != draft["clothes"] }
    state = profile.purchase(item)
    assert(state["points"] == before - item["points"] && state["owned"][item["key"]], "purchase debits and unlocks together")
    assert(profile.purchase(item)["points"] == state["points"], "owned pieces never cost twice")
    replacement = draft.merge("clothes" => item["id"], "hair_color" => 120)
    profile.confirm(catalog, replacement, "slot:File B", false)
    assert(profile.read["appearances"]["slot:File A"] == draft, "save-slot appearances are independent")
    assert(profile.read["appearances"]["slot:File B"] == replacement, "colors round-trip through native JSON")
    locked = catalog.available("hat").find { |entry| !profile.read["owned"][entry["key"]] }
    reject("confirmation cannot smuggle a locked piece") { profile.confirm(catalog, draft.merge("hat2" => locked["id"]), "slot:File C", false) }
    reject("unavailable variants cannot be equipped") { profile.confirm(catalog, draft.merge("hair" => "999_missing"), "slot:File C", false) }
    points = profile.read["points"]
    File.binwrite(File.join(profile.directory, "profile-writing.tmp"), "interrupted uncommitted write")
    assert(profile.read["points"] == points, "interrupted staging retains the committed snapshot")
    return draft
  end

  def self.test_unchanged_default_appearance(catalog, profile)
    previous_trainer = $Trainer
    before = profile.read
    assert(before["initial_claimed"], "default confirmation regression runs after the free selection is already used")
    [GENDER_MALE, GENDER_FEMALE].each do |gender|
      $Trainer = Player.new("Default appearance", :POKEMONTRAINER_Red)
      draft = { "clothes" => getDefaultClothes(gender), "hair" => "3_#{getDefaultHair(gender)}",
        "hat" => nil, "hat2" => nil, "skin_tone" => 1,
        "hair_color" => 0, "clothes_color" => 0, "hat_color" => 0,
        "hat2_color" => 0, "bike_color" => 0 }
      C.apply(draft)
      key = "slot:default-#{gender}"
      screen = C::WardrobeScreen.new(catalog, profile, key)
      assert(screen.appearance == draft, "the unedited gender #{gender} starting appearance is retained")
      state = profile.confirm(catalog, screen.appearance, key, false)
      assert(state["appearances"][key] == draft, "the unedited starting appearance can be confirmed after the free claim")
      assert(state["points"] == before["points"] && state["initial_appearance"] == before["initial_appearance"],
        "default confirmation spends no points and does not replace the one-time free selection")
    end
  ensure
    $Trainer = previous_trainer
  end

  def self.test_achievement_rewards(root)
    profile = C::Profile.new(File.join(root, "achievement-rewards"))
    first = "11111111-1111-1111-1111-111111111111"
    second = "22222222-2222-2222-2222-222222222222"
    before = profile.read
    assert(before["points"] == 0, "existing points are not scaled or backfilled")
    state = profile.award_achievements([first, first])["state"]
    assert(state["points"] == 100, "one new achievement adds exactly 100 points")
    assert(C::Profile.new(profile.directory).award_achievements([first])["state"]["points"] == 100, "reload and repeated delivery never pay twice")
    state = profile.award_achievements([first, second])["state"]
    assert(state["points"] == 200, "only newly delivered identities are credited")
    revision = state["revision"]
    assert(profile.award_achievements([second, first])["state"]["revision"] == revision, "duplicate delivery does not rewrite the profile")
    reject("malformed reward batches cannot partially credit points") { profile.award_achievements(["33333333-3333-3333-3333-333333333333", "invalid"]) }
    assert(profile.read["points"] == 200, "invalid reward batch leaves the full balance unchanged")
    profile.synchronize { profile.write_unlocked(profile.read_unlocked) }
    assert(profile.award_achievements([first])["state"]["points"] == 200, "snapshot revisions cannot reset achievement claims")
  end

  def self.test_corruption(root)
    profile = C::Profile.new(File.join(root, "corrupt"))
    profile.award("run", "a", 10)
    profile.award("run", "b", 10)
    File.binwrite(File.join(profile.directory, "profile-b.json"), "damaged")
    reject("damage never silently reissues free selection or rolls back currency") { profile.read }
  end

  def self.test_achievement_reward_retry(root)
    previous_directory = C.profile_directory
    previous_error = C.instance_variable_get(:@last_error)
    C.profile_directory = File.join(root, "achievement-retry")
    profile = C.profile
    profile.read
    messages = []
    connection = Object.new
    connection.define_singleton_method(:wardrobe_achievement_rewards_supported?) { true }
    connection.define_singleton_method(:send_event) { |event, payload| messages << [event, payload]; true }
    payload = { "version" => 1, "session_id" => Ironmon.game_session_id, "ids" => ["44444444-4444-4444-4444-444444444444"] }
    lock = File.open(File.join(profile.directory, "profile.lock"), "a+b")
    lock.flock(File::LOCK_EX)
    C.receive_achievement_rewards(payload, connection)
    assert(messages.empty?, "a locked profile is never acknowledged before currency commits")
    lock.flock(File::LOCK_UN)
    lock.close
    C.instance_variable_set(:@achievement_reward_retry_at, nil)
    C.retry_achievement_rewards(connection)
    assert(profile.read["points"] == 100 && messages.length == 1, "pending currency retries after the profile becomes available")
    assert(messages[0][0] == "wardrobe_achievement_rewards_acknowledged", "only a durable credit emits the reward acknowledgement")
    C.receive_achievement_rewards(payload, connection)
    assert(profile.read["points"] == 100 && messages.length == 2, "repeat delivery is acknowledged without another credit")
  ensure
    lock.close if lock && !lock.closed?
    C.profile_directory = previous_directory
    C.instance_variable_set(:@last_error, previous_error)
    C.instance_variable_set(:@pending_achievement_rewards, nil)
    C.instance_variable_set(:@achievement_reward_retry_at, nil)
  end

  def self.test_game_hooks(catalog, draft)
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_checkpoint_id = 345
    $PokemonGlobal.ironmon_run_ledger = Ironmon.default_run_ledger
    $PokemonGlobal.ironmon_run_ledger["current_attempt"] = {
      "result" => "active", "attempt_number" => 1, "seed" => 44,
      "statistics" => { "starting_badges" => 0 }
    }
    $Trainer = Player.new("Wardrobe test", :POKEMONTRAINER_Red)
    $Trainer.save_slot = "File A"
    C.apply(draft)
    C.begin_attempt
    first_id = C.attempt_id
    C.begin_attempt
    assert(first_id != C.attempt_id, "same-seed attempts receive different reward identities")
    start = C.profile.read["points"]
    notices = C.instance_variable_get(:@pending_notice_points).to_i
    C.award_trainer([13,1,"trainer"], 2, 1, true)
    assert(C.profile.read["points"] == start + 20, "trainer reward scales with badges")
    assert(C.instance_variable_get(:@pending_notice_points).to_i == notices,
           "trainer rewards are banked without queuing a notification")
    C.award_trainer([13,1,"trainer"], 2, 1, true)
    [0,2,3,4,5].each { |result| C.award_trainer([13,2,"trainer"], 2, result, true) }
    C.award_trainer([13,2,"trainer"], 2, 1, false)
    assert(C.profile.read["points"] == start + 20, "reload, loss, escape, capture and non-internal battles cannot pay")
    $Trainer.badges[0] = true
    C.check_milestones
    C.check_milestones
    assert(C.profile.read["points"] == start + 120, "badge is awarded once")
    assert(C.instance_variable_get(:@pending_notice_points).to_i == notices + 100,
           "badge milestones still queue their point notification")
    $PokemonGlobal.ironmon_run_ledger["current_attempt"]["result"] = "won"
    C.check_milestones
    C.check_milestones
    assert(C.profile.read["points"] == start + 620, "Hall of Fame is awarded once")
    assert(C.instance_variable_get(:@pending_notice_points).to_i == notices + 600,
           "Hall of Fame still queues its point notification")
    C.apply(draft.merge("clothes_color" => 200))
    C.restore_appearance
    assert(C.capture == draft, "load restores the slot's confirmed appearance")
    new_character = draft.merge("skin_tone" => 6, "hair_color" => 240, "clothes_color" => 120)
    C.apply(new_character)
    $Trainer.save_slot = "File New"
    screen = C::WardrobeScreen.new(catalog, C.profile, "slot:File New")
    assert(screen.appearance == new_character,
           "a new save's wardrobe starts from its selected character instead of the first shared outfit")
    C.restore_appearance
    assert(C.capture == new_character,
           "loading a new save keeps its selected character when that slot has no confirmed appearance")
    $Trainer.save_slot = nil
    Ironmon.instance_variable_set(:@reset_save_slot, "File A")
    Ironmon.with_checkpoint_reset_load do
      assert(C.appearance_key == "slot:File A", "checkpoint uses the target slot, not its stale saved slot")
      C.restore_appearance
    end
    Ironmon.instance_variable_set(:@reset_save_slot, nil)
    C.copy_slot_appearance("slot:File A", "File C")
    assert(C.profile.read["appearances"]["slot:File C"] == draft, "Save As copies only the confirmed appearance")
    run_only_clothes = catalog.available("clothes").find { |entry| !C.profile.read["owned"][entry["key"]] }
    run_only_hat = catalog.available("hat").find { |entry| !C.profile.read["owned"][entry["key"]] }
    run_only_hair = catalog.available("hair").find { |entry| !C.profile.read["owned"][entry["key"]] }
    shared_before = C.profile.read
    $Trainer.unlock_clothes(run_only_clothes["id"], true)
    $Trainer.unlock_hat(run_only_hat["id"], true)
    $Trainer.unlock_hair(run_only_hair["id"], true)
    assert($Trainer.unlocked_clothes.include?(run_only_clothes["id"]) &&
           $Trainer.unlocked_hats.include?(run_only_hat["id"]) &&
           $Trainer.unlocked_hairstyles.include?(run_only_hair["id"]),
           "ordinary game rewards still enter only the current run's inventory")
    assert(C.profile.read["owned"] == shared_before["owned"] && C.profile.read["points"] == shared_before["points"],
           "ordinary game rewards never become shared wardrobe unlocks")
    assert(nurseOutfitHeal.nil? && pickUpTypeItemSetBonus.nil? && !isWearingTeamRocketOutfit,
           "cosmetic healing, bonus items, and disguise benefits are disabled in Ironmon")
    map = load_data("Data/Map013.rxdata")
    assert(C.patch_bedroom(13, map), "the actual bedroom intro matches the guarded patch")
    scripts = map.events.values.flat_map { |event| event.pages.flat_map { |page| page.list } }
    intro = scripts.select { |command| command.code == 355 && command.parameters[0].include?("Cosmetics.intro_wardrobe") }
    assert(intro.length == 2, "both player bedroom intro variants are patched")
    assert(!C.patch_bedroom(13, map), "event patch is idempotent")
    assert(scripts.count { |command| command.code == 355 && command.parameters[0] == "pbCommonEvent(80) unless Ironmon.active?" } == 2,
           "the legacy wardrobe is not opened again after the new wardrobe")
  end

  def self.test_screen(catalog)
    $PokemonSystem = PokemonSystem.new
    $game_system = Game_System.new
    $game_switches = Game_Switches.new
    $game_variables = Game_Variables.new
    profile = C.profile
    screen = C::WardrobeScreen.new(catalog, profile, "slot:File A")
    before = C.capture
    screen.create_widgets
    screen.redraw_waiting(true)
    Graphics.update
    bitmap = Graphics.snap_to_bitmap
    bitmap.save_to_png(File.join($ironmon_cosmetic_test_root, "wardrobe-waiting.png"))
    bitmap.dispose
    snapshot = screen.snapshot(true)
    assert(snapshot["open"] && snapshot["catalog"].length == catalog.entries.length, "only the game constructs the wardrobe session and complete catalog")
    identity = { "game_session_id" => snapshot["game_session_id"], "wardrobe_id" => snapshot["wardrobe_id"], "revision" => 0 }
    C::WardrobeScreen::PREVIEW_MODES.each do |mode|
      preview = screen.preview_request(identity.merge("mode" => mode))
      bytes = preview["png_base64"].unpack1("m0")
      assert(bytes.start_with?("\x89PNG".b) && preview["width"] > 0 && preview["height"] > 0, "native #{mode} PNG is nonempty")
      assert(preview["dye_colors"].length == 36 && preview["base_colors"].length > 0, "native palette supplies direct hue and installed base choices")
      File.binwrite(File.join($ironmon_cosmetic_test_root, "wardrobe-#{mode}.png"), bytes)
    end
    portrait = screen.preview_request(identity.merge("mode" => "portrait"))
    head = screen.preview_request(identity.merge("mode" => "portrait", "head_only" => true))
    assert(head["height"] < portrait["height"], "hairstyles use a native head closeup")
    assert(C.capture == before, "image and palette requests never equip or mutate the player")
    assert(Dir.children(File.join(profile.directory, "previews")).empty?, "temporary native images are removed")
    assert(screen.preview_wearable_offset("bike", 2, 1, false) == [2, -2], "native bicycle hair offset retained")
    assert(screen.preview_wearable_offset("bike", 2, 1, true) == [2, -4], "native bicycle accessory offset retained")
    reject("a tracker has no opening action") { screen.handle_request(identity.merge("operation_id" => "a" * 32, "action" => "open")) }
    reject("a stale session cannot edit") { screen.preview_request(identity.merge("wardrobe_id" => "stale")) }
    reject("a stale revision cannot preview") { screen.preview_request(identity.merge("revision" => 42)) }
    request = identity.merge("operation_id" => "b" * 32, "action" => "change", "appearance" => screen.appearance.merge("hair_color" => 137))
    changed = screen.handle_request(request)
    assert(changed["appearance"]["hair_color"] == 137 && C.capture == before, "direct dye stages without equipping")
    assert(screen.handle_request(request) == changed && screen.revision == 1, "repeated operation is idempotent")
    reject("operation IDs cannot be reused for another mutation") { screen.handle_request(request.merge("appearance" => before)) }
    reject("stale edits cannot overwrite a newer draft") { screen.handle_request(identity.merge("operation_id" => "c" * 32, "action" => "change", "appearance" => before)) }
    state = profile.read
    id = "d" * 32
    saved = profile.save_outfit_profile(catalog, id, "Evening", screen.appearance)
    assert(!saved["outfit_profiles"][id]["appearance"].has_key?("skin_tone"), "saved outfit excludes character skin")
    assert(C::Profile.new(profile.directory).read["outfit_profiles"] == saved["outfit_profiles"], "saved outfits survive profile reload")
    reject("profile names cannot duplicate case-insensitively") { profile.save_outfit_profile(catalog, "e" * 32, "EVENING", screen.appearance) }
    reject("invalid profile names are rejected") { profile.rename_outfit_profile(id, "\n") }
    profile.rename_outfit_profile(id, "Night")
    stale_revision = profile.read["revision"]
    profile.award("parallel-game", "trainer:concurrent", 10)
    reject("stale shared-profile writes are rejected inside the lock") { profile.rename_outfit_profile(id, "Stale", stale_revision) }
    profile.save_outfit_profile(catalog, id, "Night", screen.appearance.merge("bike_color" => 111), true)
    assert(profile.read["outfit_profiles"][id]["appearance"]["bike_color"] == 111, "replace retains identity and updates saved dyes")
    loaded = screen.handle_request(identity.merge("revision" => 1, "operation_id" => "f" * 32, "action" => "load_profile", "profile_id" => id))
    assert(loaded["appearance"]["bike_color"] == 111 && loaded["appearance"]["skin_tone"] == changed["appearance"]["skin_tone"], "loading stages saved pieces without changing skin")
    assert(C.capture == before, "profile loading does not equip")
    profile.delete_outfit_profile(id)
    assert(profile.read["outfit_profiles"].empty?, "profile deletion persists")
    screen.close(false)
    assert(!screen.snapshot["open"] && C.capture == before, "cancel ends session without applying the draft")
  ensure
    screen.dispose if screen
  end

  def self.run
    @assertions = 0
    saved = [$Trainer, $PokemonGlobal, $PokemonSystem, $game_system, $game_switches, $game_variables]
    original_directory = C.profile_directory
    root = File.join($ironmon_cosmetic_test_root, "profile-#{Process.pid}-#{Time.now.to_i}")
    Dir.mkdir(root)
    begin
      C.profile_directory = File.join(root, "shared")
      catalog = C.catalog(true)
      assert(catalog.entries.length > 0, "installed catalogs are discovered without hand-maintained item lists")
      assert(catalog.entries.values.select { |entry| entry["available"] && entry["category"] != "hair" }.all? { |entry| entry["issues"].empty? }, "incomplete movement sprites are excluded")
      test_prices
      draft = test_profile(catalog, C.profile)
      test_unchanged_default_appearance(catalog, C.profile)
      test_corruption(root)
      test_achievement_rewards(root)
      test_achievement_reward_retry(root)
      test_game_hooks(catalog, draft)
      test_screen(catalog)
      File.binwrite(File.join($ironmon_cosmetic_test_root, "runtime.json"), C.encode({
        "passed" => true, "assertions" => @assertions,
        "catalog_entries" => catalog.entries.length,
        "available" => ["clothes", "hat", "hair"].to_h { |category| [category, catalog.available(category).length] }
      }))
    end
  ensure
    $Trainer, $PokemonGlobal, $PokemonSystem, $game_system, $game_switches, $game_variables = saved
    C.profile_directory = original_directory
    remove_test_tree(root) if root && File.directory?(root)
  end

  def self.remove_test_tree(root)
    expected = File.expand_path($ironmon_cosmetic_test_root) + "/profile-"
    raise "Unsafe test cleanup" if !File.expand_path(root).start_with?(expected)
    Dir.children(root).each do |name|
      path = File.join(root, name)
      if File.directory?(path)
        remove_test_tree(path)
      else
        File.delete(path)
      end
    end
    Dir.rmdir(root)
  end
end
