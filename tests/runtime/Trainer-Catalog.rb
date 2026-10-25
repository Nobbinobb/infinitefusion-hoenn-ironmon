module IronmonTrainerCatalogRuntimeTests
  def self.assert(condition, message)
    raise "Trainer catalog test failed: #{message}" if !condition
  end

  def self.run
    root = File.dirname($ironmon_source_root)
    load File.join(root, "tools/generation/Trainer-Sources.rb")
    exporter = File.binread(File.join(root, "tools/generation/Export-AreaCatalog.rb")).split("\narea_catalog_output =").first
    eval(exporter, TOPLEVEL_BINDING, "Export-AreaCatalog.rb")
    entries = Ironmon.tracker_area_catalog.flat_map { |area| area["trainers"] }
    assert(entries.length == 135, "all 135 enabled authored trainer roles with battle data are present")
    assert(entries.map { |row| row["entry_id"] }.uniq.length == entries.length, "trainer identities are unique")
    assert(entries.count { |row| row["battle_kind"] == "move_tutor" } == 8, "all eight move tutor battles are included")
    assert(entries.count { |row| row["battle_kind"] == "scaled" } == 7, "all seven regional adventurer battles are included")
    assert(entries.none? { |row| row["battle_kind"] == "rematch" }, "disabled rematches do not create trainer entries")
    assert(entries.none? { |row| row["map_id"] == 104 && (5..8).include?(row["event_id"]) }, "Winstrate rematch menus do not duplicate their initial battles")
    assert(entries.count { |row| row["map_id"] == 77 && [5, 7, 8, 9].include?(row["event_id"]) } == 4, "the four initial Winstrate battles remain included")
    assert(entries.none? { |row| row["map_id"] == 25 && row["event_id"] == 18 }, "repeatable Pokemon Center rival rematch is excluded")
    assert(entries.none? { |row| row["map_id"] == 42 && row["trainer_name"] == "Willie" }, "unfinished trainer event without any battle data is excluded")
    assert(entries.count { |row| row["battle_kind"] == "wally" } == 1, "Wally is included once")
    assert(entries.count { |row| row["battle_kind"] == "rival" } == 3, "all three relocated and story rival encounters are included")
    assert(entries.any? { |row| row["map_id"] == 18 && row["event_id"] == 5 && row["battle_kind"] == "rival" }, "first rival belongs to the patched lab event")
    assert(entries.none? { |row| row["map_id"] == 11 && row["battle_kind"] == "rival" }, "obsolete first-rival events do not create phantom battles")
    nurse = entries.find { |row| row["trainer_name"] == "Felicity" }
    assert(nurse && nurse["map_id"] == 74 && nurse["event_id"] == 37, "move tutor override targets the physical nurse instead of its controller")
    [
      [11, 18, :TWIN_1, "Amy"], [11, 17, :TWIN_2, "Liv"],
      [20, 12, :TWIN_1, "Gina"], [20, 13, :TWIN_2, "Mia"],
      [74, 9, :SCHOOLMATE_JR, "Anna"], [74, 10, :SCHOOLMATE_SR, "Meg"],
      [87, 87, :TEAM_AQUA_GRUNT_F, "Delia"], [87, 87, :TEAM_MAGMA_GRUNT_F, "Dayle"],
      [87, 88, :TEAM_AQUA_GRUNT_M, "Hudson"], [87, 88, :TEAM_MAGMA_GRUNT_M, "Craig"]
    ].each do |map, event, type, name|
      id = Ironmon.tracker_area_event_entry_id("trainers", map, event, type, name)
      assert(id == "trainer:#{map}:#{type}:#{name}", "multi-battle participant #{name} resolves independently")
    end
    entries.each do |row|
      next if row["battle_kind"] == "rival"
      assert([GameData::Trainer, GameData::TrainerModern].any? { |mode| mode.try_get(row["trainer_type"].to_sym, row["trainer_name"], row["party_id"]) }, "#{row['entry_id']} has authoritative trainer data")
      map = load_data(sprintf("Data/Map%03d.rxdata", row["map_id"]))
      assert(map.events[row["source_event_id"] || row["event_id"]], "#{row['entry_id']} references a physical battle source, including spawned participants")
    end
    script = 'pbTrainerBattle(:HIKER,"Sunny",_INTL("One, two (three)!"),false,2,false,1,nil,nil,4,32)'
    values = IronmonAreaTrainerSources.calls(script, "pbTrainerBattle").first
    assert(values.length == 11 && values[4] == "2" && values[9] == "4", "balanced parsing preserves party and event IDs around nested dialogue")
    assert(IronmonAreaTrainerSources.calls('pbMultiTrainerBattle([[:TWIN_1,"Gina",12],[:TWIN_2,"Mia",13]])', "pbMultiTrainerBattle").first.length == 1, "nested participant arrays remain a single argument")
    puts "Complete scripted trainer catalog regressions passed"
  end
end

IronmonTrainerCatalogRuntimeTests.run
