# Native profile transactions and collection wrappers with no window/input automation.
module IronmonCollectionWardrobeRuntimeTests
  def self.assert(condition, message)
    raise "Collection wardrobe test failed: #{message}" if !condition
    @assertions += 1
  end

  def self.run(root)
    @assertions = 0
    c = Ironmon::GameplayCollection
    c.capture_suspended = true
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonSystem = PokemonSystem.new
    $PokemonTemp = PokemonTemp.new
    $game_temp = Game_Temp.new
    $Trainer = Player.new("Wardrobe collection", :POKEMONTRAINER_Red)
    $PokemonGlobal.ironmon_mode = true
    c.directory_override = File.join(root, "producer")
    c.instance_variable_set(:@attempt_id, c.uuid)
    c.instance_variable_set(:@branch_id, c.uuid)
    c.instance_variable_set(:@disabled, false)
    c.capture_suspended = false
    catalog = Ironmon::Cosmetics::Catalog.new
    profile = Ironmon::Cosmetics::Profile.new(File.join(root, "wardrobe"))
    defaults = profile.read["owned"].keys
    clothes = catalog.available("clothes").find { |entry| !defaults.include?(entry["key"]) }
    replacement = catalog.available("clothes").find { |entry| entry["key"] != clothes["key"] && !defaults.include?(entry["key"]) }
    hair = catalog.available("hair").first
    assert(clothes && replacement && hair, "actual installed catalogue provides separate selectable pieces")
    initial = { "clothes" => clothes["id"], "hair" => "#{hair["variants"].first}_#{hair["id"]}", "hat" => nil, "hat2" => nil, "skin_tone" => 3, "clothes_color" => 0, "hair_color" => 0, "hat_color" => 0, "hat2_color" => 0, "bike_color" => 0 }
    profile.award("native-attempt", "trainer:initial", 1000)
    assert(!profile.award("native-attempt", "trainer:initial", 1000), "native duplicate award remains idempotent")
    screen = Ironmon::Cosmetics::WardrobeScreen.allocate
    screen.instance_variable_set(:@profile, profile)
    screen.instance_variable_set(:@appearance, initial.dup)
    screen.instance_variable_set(:@preview_mode, "portrait")
    screen.instance_variable_set(:@state, profile.read)
    screen.define_singleton_method(:run) do
      @state = @profile.confirm(catalog, @appearance, "slot:fixture", true)
      Ironmon::Cosmetics.apply(@appearance)
      @state = @profile.purchase(replacement)
      @profile.award("native-attempt", "badge:0", 500)
      before = @appearance.dup
      @appearance = @appearance.merge("clothes" => replacement["id"], "hair_color" => 120, "bike_color" => 40)
      Ironmon::GameplayCollection::Wardrobe.preview(self, before, @preview_mode, "selection")
      @state = @profile.confirm(catalog, @appearance, "slot:fixture", false)
      Ironmon::Cosmetics.apply(@appearance)
      @confirmed = true
      true
    end
    screen.singleton_class.prepend(IronmonCollectionWardrobeScreen)
    assert(screen.run, "native profile-backed screen wrapper result is preserved")
    achievement = "77777777-7777-7777-7777-777777777777"
    profile.award_achievements([achievement])
    profile.award_achievements([achievement])
    branch_before = c.branch_id
    attempt_before = c.attempt_id
    group = nil
    c.with_context("battle_id" => "source-battle", "attempt_id" => attempt_before, "branch_id" => branch_before) do
      group = c.open_group("native_context_retirement")
      c.instance_variable_set(:@branch_id, c.uuid)
      c.instance_variable_set(:@attempt_id, c.uuid)
      c.emit("collection.context_checkpoint", { "reason" => "new_branch_context_test" })
      c.close_group(group)
      c.close_group(group)
    end
    c.instance_variable_set(:@branch_id, branch_before)
    c.instance_variable_set(:@attempt_id, attempt_before)
    c.flush
    assert(!c.last_error, c.last_error)
    batch = c.journal.next_batch
    facts = c.decode(batch["json"])["events"]
    assert(batch["last_sequence"].to_i == facts.length, "fixture fits one complete real batch")
    checkpoint = facts.find { |event| event["payload"]["reason"] == "new_branch_context_test" }
    closure = facts.select { |event| event["type"] == "collection.effect_group_closed" && event["payload"]["effect_group_id"] == group }
    assert(checkpoint["attempt_id"] != attempt_before && checkpoint["battle_id"].nil?, "new lifecycle event ignores the retired dynamic battle and attempt")
    assert(closure.length == 1 && closure[0]["attempt_id"] == attempt_before && closure[0]["battle_id"] == "source-battle", "late group closes once under its retained original context")
    awards = facts.select { |event| event["type"] == "wardrobe.points_award_resolved" }.map { |event| event["payload"] }
    assert(awards.map { |payload| payload["award_outcome"] } == ["granted", "duplicate_claim", "granted", "granted"], "actual and duplicate award outcomes are canonical")
    assert(awards[-1]["award_source_kind"] == "achievement" && awards[-1]["actual_points_awarded"] == 100, "achievement currency is captured once with its actual source")
    purchase = facts.find { |event| event["type"] == "wardrobe.purchase_resolved" }["payload"]
    assert(purchase["actual_points_spent"] == replacement["points"] && purchase["owned_before"] == false && purchase["owned_after"] == true, "actual paid ownership/debit retained")
    confirmations = facts.select { |event| event["type"] == "wardrobe.appearance_confirmation_resolved" }.map { |event| event["payload"] }
    assert(confirmations.length == 2 && confirmations[-1]["appearance_after"]["outfit_key"] == replacement["key"], "actual canonical confirmed pieces retained")
    assert(confirmations[-1]["appearance_after"]["confirmed_dye_values"].any? { |value| value["field"] == "hair_color" && value["value"] == 120 }, "actual confirmed dye distribution retained")
    dyes = facts.select { |event| event["type"] == "wardrobe.dye_changed" }.map { |event| event["payload"] }
    hair_committed = dyes.find { |payload| payload["dye_field"] == "hair_color" && payload["change_stage"] == "committed" }
    assert(hair_committed["piece_key"] == "hair:#{hair["id"]}" && hair_committed["confirmation_event_id"] == confirmations[-1]["event_id"], "committed dyes link canonical piece and actual confirmation ID")
    assert(dyes.any? { |payload| payload["dye_field"] == "bike_color" && payload["piece_key"].nil? }, "bike tint does not invent an owned cosmetic piece")
    session = facts.find { |event| event["type"] == "wardrobe.session_ended" }["payload"]
    assert(session["points_spent_in_session"] == replacement["points"] && session["completed_purchase_count"] == 1, "session sums paid debits despite a simultaneous positive award")
    assert(session["confirmed_appearance_event_id"] == confirmations[-1]["event_id"] && session["result"] == "confirmed", "session links the successful native confirmation")
    assert(facts.select { |event| event["type"] == "wardrobe.ownership_changed" }.all? { |event| event["payload"]["owned_counts_after"]["total"] == event["payload"]["owned_counts_after"].values_at("clothes", "hair", "hat").sum }, "ownership totals match native categories")
    File.binwrite(File.join(root, "batches.json"), c.encode([c.decode(batch["json"])]))
    c.journal.close
    c.instance_variable_set(:@journal, nil)
    test_failure_isolation(c, root)
    File.binwrite(File.join(root, "wardrobe.json"), c.encode({ "status" => "passed", "assertions" => @assertions, "runtime" => RUBY_DESCRIPTION, "events" => facts.length }))
  ensure
    c.capture_suspended = true if c
  end

  def self.test_failure_isolation(c, root)
    original_journal = c.method(:journal)
    original_disabled = c.instance_variable_get(:@disabled)
    original_error = c.instance_variable_get(:@last_error)
    calls = 0
    c.define_singleton_method(:journal) { raise IOError, "isolated unavailable collection disk" }
    c.instance_variable_set(:@disabled, false)
    result = c::Resources.item_handler(:POTION, nil, "bag_outside_battle") { calls += 1; :native_result }
    assert(result == :native_result && calls == 1, "native item handler executes exactly once when journal construction fails")
    assert(c.last_error.include?("isolated unavailable"), "capture failure is visible without breaking gameplay")
    c.instance_variable_set(:@disabled, false)
    fallback_screen = Ironmon::Cosmetics::WardrobeScreen.allocate
    fallback_calls = 0
    fallback_screen.define_singleton_method(:run) { fallback_calls += 1; :native_screen_result }
    fallback_screen.singleton_class.prepend(IronmonCollectionWardrobeScreen)
    assert(fallback_screen.run == :native_screen_result && fallback_calls == 1, "journal failure during screen setup preserves the native wardrobe once")
    old_branch = c.branch_id
    new_branch = c.uuid
    old_attempt = c.attempt_id
    c.with_context("battle_id" => "old-battle", "attempt_id" => old_attempt, "branch_id" => old_branch) do
      c.instance_variable_set(:@branch_id, new_branch)
      c.instance_variable_set(:@attempt_id, "new-attempt")
      current = c.current_context
      assert(current["attempt_id"] == "new-attempt" && current["branch_id"] == new_branch && !current["battle_id"], "new branch ignores old dynamic battle context before native ensure unwinds")
    end
    calls = 0
    begin
      c.with_context("native_exception" => true) { calls += 1; raise ArgumentError, "native exception" }
    rescue ArgumentError
    end
    assert(calls == 1 && !c.current_context["native_exception"], "native exceptions propagate once and context unwinds once")
    c.with_context("outer" => "retained") do
      bad_context = Class.new(Hash) { def each; raise IOError, "isolated context setup failure"; end }.new
      setup_calls = 0
      native_result = c.with_context(bad_context) { setup_calls += 1; :native_result }
      assert(native_result == :native_result && setup_calls == 1 && c.current_context["outer"] == "retained", "failed context setup neither replays native work nor pops its outer context")
    end
    original_lookup = GameData::Item.method(:try_get)
    GameData::Item.define_singleton_method(:try_get) { |*args| raise IOError, "isolated optional name lookup failure" }
    value = c.catalogue_ref("item", :COLLECTION_METADATA_FAILURE)
    assert(value == "COLLECTION_METADATA_FAILURE", "optional catalogue name failure preserves normalized native identity")
  ensure
    GameData::Item.define_singleton_method(:try_get, original_lookup) if original_lookup
    c.define_singleton_method(:journal, original_journal) if original_journal
    c.instance_variable_set(:@disabled, original_disabled)
    c.instance_variable_set(:@last_error, original_error)
    c.instance_variable_set(:@branch_id, old_branch) if old_branch
    c.instance_variable_set(:@attempt_id, old_attempt) if old_attempt
  end
end
