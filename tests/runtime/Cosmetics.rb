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
      assert(C.points_for("hat", price) == expected, "price boundary #{price}")
    end
    [nil, 0, -1, "bad"].each do |price|
      assert(C.points_for("clothes", price) == 80, "unpriced outfit fallback")
      assert(C.points_for("hair", price) == 20, "unpriced hair fallback")
    end
  end

  def self.test_profile(catalog, profile)
    draft = appearance(catalog)
    assert(!profile.read["initial_claimed"], "a fresh profile has one free choice")
    profile.confirm(catalog, draft, "slot:File A", true)
    state = profile.read
    assert(state["initial_claimed"] && state["owned"].keys.sort == C.appearance_keys(draft).sort,
           "only final pieces are unlocked, not the browsed catalog")
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

  def self.test_corruption(root)
    profile = C::Profile.new(File.join(root, "corrupt"))
    profile.award("run", "a", 10)
    profile.award("run", "b", 10)
    File.binwrite(File.join(profile.directory, "profile-b.json"), "damaged")
    reject("damage never silently reissues free selection or rolls back currency") { profile.read }
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
    screen = C::WardrobeScreen.new(catalog, C.profile, "slot:File A")
    before = C.capture
    screen.create_widgets
    Graphics.update
    bitmap = Graphics.snap_to_bitmap
    bitmap.save_to_png(File.join($ironmon_cosmetic_test_root, "wardrobe.png"))
    bitmap.dispose
    assert(screen.preview_mode == "portrait", "the detailed portrait is the default preview")
    screen.instance_variable_get(:@window).index = C::WardrobeScreen::ROOT_FIELDS.index("preview")
    [["front", "front-preview.png"], ["back", "back-preview.png"],
     ["left", "left-preview.png"], ["right", "right-preview.png"]].each do |mode, filename|
      screen.move_horizontal(1)
      assert(screen.preview_mode == mode, "the root wardrobe can rotate to the #{mode} sprite")
      Graphics.update
      bitmap = Graphics.snap_to_bitmap
      bitmap.save_to_png(File.join($ironmon_cosmetic_test_root, filename))
      bitmap.dispose
    end
    screen.move_horizontal(1)
    assert(screen.preview_mode == "bike", "the bicycle is part of the preview rotation")
    assert(screen.preview_wearable_offset("bike", 2, 1, false) == [2, -2],
           "the bicycle preview applies the live right-facing hair offset")
    assert(screen.preview_wearable_offset("bike", 2, 1, true) == [2, -4],
           "the bicycle preview applies the live right-facing accessory offset and frame bob")
    screen.move_horizontal(1)
    assert(screen.preview_mode == "portrait", "preview rotation wraps back to the portrait")
    screen.appearance["bike_color"] = 120
    screen.show_root
    screen.instance_variable_get(:@window).index = C::WardrobeScreen::ROOT_FIELDS.index("bike_color")
    screen.redraw
    assert(screen.current_preview_mode == "bike" && screen.appearance["bike_color"] == 120,
           "bicycle dye automatically shows the dyed bicycle")
    Graphics.update
    bitmap = Graphics.snap_to_bitmap
    bitmap.save_to_png(File.join($ironmon_cosmetic_test_root, "bicycle-preview.png"))
    bitmap.dispose
    screen.show_browser("hat2")
    3.times { screen.move_horizontal(1) }
    Graphics.update
    bitmap = Graphics.snap_to_bitmap
    bitmap.save_to_png(File.join($ironmon_cosmetic_test_root, "accessories.png"))
    bitmap.dispose
    assert(C.capture == before, "real graphic previews never change live trainer fields")
    assert(screen.preview_appearance != before, "paging previews the highlighted accessory")
    screen.show_root
    screen.instance_variable_get(:@window).index = 6
    screen.move_horizontal(1)
    assert(screen.appearance["hair_color"] == 10, "dye controls modify only the draft")
    assert(C.capture == before, "draft dye changes do not leak into the game")
    screen.dispose
    screen = C::WardrobeScreen.new(catalog, C::Profile.new(File.join(C.profile.directory, "initial-preview")), "slot:new")
    screen.create_widgets
    Graphics.update
    bitmap = Graphics.snap_to_bitmap
    bitmap.save_to_png(File.join($ironmon_cosmetic_test_root, "initial-wardrobe.png"))
    bitmap.dispose
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
      test_corruption(root)
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
