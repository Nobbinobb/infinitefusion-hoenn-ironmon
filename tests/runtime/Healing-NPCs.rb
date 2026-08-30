module IronmonHealingNpcRuntimeTests
  OUTPUT_PATH = $ironmon_healing_npc_test_output_path.to_s

  def self.assert(condition, message)
    raise "Healing NPC runtime test failed: #{message}" if !condition
  end

  def self.verify_map(map_id, x, y, character, location_text)
    map = load_data(sprintf("Data/Map%03d.rxdata", map_id))
    original_count = map.events.length
    added = Ironmon.patch_healing_npc_map(map_id, map)
    assert(added, "map #{map_id} receives its healing NPC")
    assert(
      map.events.length == original_count + 1,
      "map #{map_id} receives exactly one event"
    )
    event = map.events[Ironmon::HEALING_NPC_EVENT_ID]
    assert(event, "map #{map_id} healing NPC uses the reserved event ID")
    assert(
      event.x == x && event.y == y,
      "map #{map_id} healing NPC uses the screenshot location"
    )
    page = event.pages[0]
    assert(
      page.graphic.character_name == character,
      "map #{map_id} healing NPC uses its location-appropriate graphic"
    )
    assert(
      page.trigger == 0 && !page.through,
      "map #{map_id} healing NPC is a solid talk interaction"
    )
    dialogue = page.list.select do |command|
      [101, 401].include?(command.code)
    end.map { |command| command.parameters[0] }.join(" ")
    assert(
      dialogue.include?(location_text),
      "map #{map_id} dialogue refers to its location"
    )
    audio = page.list.find { |command| command.code == 250 }
    assert(
      audio && audio.parameters[0].name == "potion",
      "map #{map_id} healing uses the established recovery sound"
    )
    healing = page.list.find { |command| command.code == 314 }
    assert(
      healing && healing.parameters == [0],
      "map #{map_id} heals the entire player party"
    )
    original_trainer = $Trainer
    heal_count = 0
    trainer = Object.new
    trainer.define_singleton_method(:heal_party) { heal_count += 1 }
    $Trainer = trainer
    interpreter = Interpreter.new
    interpreter.setup(
      [healing, RPG::EventCommand.new(0, 0, [])], 0, map_id
    )
    interpreter.execute_command
    assert(
      heal_count == 1,
      "map #{map_id} recovery command invokes the engine's party heal"
    )
    assert(
      !Ironmon.patch_healing_npc_map(map_id, map),
      "map #{map_id} healing NPC insertion is idempotent"
    )
  ensure
    $Trainer = original_trainer
  end

  def self.run
    verify_map(30, 49, 38, "NPC_Hoenn_Ranger_F", "forest's spores")
    verify_map(31, 5, 17, "NPC_worker", "Route 116")
    unrelated = load_data("Data/Map005.rxdata")
    assert(
      !Ironmon.patch_healing_npc_map(5, unrelated),
      "unrelated maps receive no healing NPC"
    )
    File.binwrite(OUTPUT_PATH, "healing NPC runtime tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonHealingNpcRuntimeTests.run
