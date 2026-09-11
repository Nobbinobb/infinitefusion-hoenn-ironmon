module IronmonDevonSequenceRuntimeTests
  class Event < Game_Event
    def calculate_bush_depth; end
  end

  def self.assert(condition, message)
    raise "Devon sequence runtime test failed: #{message}" if !condition
    @assertions += 1
  end

  def self.stub(owner, name, &implementation)
    own = owner.instance_methods(false).include?(name) ||
          owner.private_instance_methods(false).include?(name)
    original = owner.instance_method(name) if own
    visibility = owner.private_method_defined?(name) ? :private : :public
    @restorations << [owner, name, original, visibility]
    owner.send(:define_method, name, &implementation)
  end

  def self.with_fixture
    @restorations = []
    globals = [$PokemonGlobal, $game_map, $game_variables, $game_switches,
               $game_self_switches, $game_temp, $game_player, $Trainer, $data_system]
    quest_ids = [Ironmon::DEVON_STOLEN_PARTS_QUEST] + Ironmon::DEVON_DELIVERY_QUESTS
    quests = quest_ids.to_h { |id| [id, QUESTS[id]] }
    quests.each { |id, quest| QUESTS[id] = Marshal.load(Marshal.dump(quest)) }
    @items = Hash.new(0)
    @messages = []
    @grants = []
    @reject_item = nil
    @lost = false
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    $game_variables = Game_Variables.new
    $game_switches = Game_Switches.new
    $game_self_switches = Game_SelfSwitches.new
    $game_temp = Game_Temp.new
    $game_player = Game_Character.new
    $Trainer = Struct.new(:quests, :quest_points, :last_visited_town_map_location).new([], 0, nil)
    $data_system = load_data("Data/System.rxdata")
    test = self
    stub(Object, :pbCallBub) { |*args| }
    stub(Object, :pbMessage) { |message, *args| test.messages << message }
    stub(Object, :showNewMainQuestMessage) { |*args| raise "Unexpected quest popup" }
    stub(Object, :turnPlayerTowardsEvent) { |*args| }
    stub(Object, :hasItem?) { |item| test.items[item] > 0 }
    stub(Object, :pbReceiveItem) do |item, *args|
      next false if item == test.reject_item
      test.items[item] += 1
      test.grants << item
      true
    end
    stub(Object, :pbFadeOutIn) { |*args, &block| block.call }
    stub(class << Ironmon; self; end, :failed_run_locked?) { test.lost? }
    yield
  ensure
    @restorations.reverse_each do |owner, name, original, visibility|
      owner.send(:remove_method, name)
      owner.send(:define_method, name, original) if original
      owner.send(visibility, name) if original
    end
    quests&.each { |id, quest| QUESTS[id] = quest }
    $PokemonGlobal, $game_map, $game_variables, $game_switches,
      $game_self_switches, $game_temp, $game_player, $Trainer, $data_system = globals
  end

  def self.items; @items; end
  def self.messages; @messages; end
  def self.grants; @grants; end
  def self.reject_item; @reject_item; end
  def self.lost?; @lost; end

  def self.map(map_id, patched = true)
    authored = load_data(sprintf("Data/Map%03d.rxdata", map_id))
    assert(Ironmon.patch_devon_sequence_map(map_id, authored), "supported map #{map_id} patches") if patched
    $game_map = Game_Map.new
    $game_map.instance_variable_set(:@map_id, map_id)
    relevant_ids = {28 => [7, 8, 16], 47 => [63, 67, 69, 70, 71, 72], 48 => [79, 80]}
    events = relevant_ids.fetch(map_id).to_h do |id|
      [id, Event.new(map_id, authored.events.fetch(id))]
    end
    $game_map.instance_variable_set(:@events, events)
    $game_map.instance_variable_set(:@common_events, {})
    return authored
  end

  def self.execute(page, event_id)
    interpreter = Interpreter.new
    interpreter.setup(page.list, event_id, $game_map.map_id)
    interpreter.update
    assert(!interpreter.running?, "event #{event_id} finishes without an autorun loop")
  end

  def self.snapshot
    return [@items.dup, [96, VAR_NB_QUEST_ACTIVE, VAR_NB_QUEST_COMPLETED, VAR_KARMA].map { |id| $game_variables[id] },
            $Trainer.quests.map { |quest| [quest.id, quest.completed] },
            [2076, 2096, 2097, 2092, 2095, 2098].map { |id| $game_switches[id] }]
  end

  def self.test_patch_boundaries
    [28, 47, 48].each do |map_id|
      authored = load_data(sprintf("Data/Map%03d.rxdata", map_id))
      original = Marshal.dump(authored)
      $PokemonGlobal.ironmon_mode = false
      assert(!Ironmon.patch_devon_sequence_map(map_id, authored), "normal play is not patched")
      assert(Marshal.dump(authored) == original, "normal play retains all authored commands")
      $PokemonGlobal.ironmon_mode = true
      assert(Ironmon.patch_devon_sequence_map(map_id, authored), "Ironmon patch applies")
      patched = Marshal.dump(authored)
      assert(!Ironmon.patch_devon_sequence_map(map_id, authored), "repeat patch is a no-op")
      assert(Marshal.dump(authored) == patched, "repeat patch preserves command lists")
    end
    town = load_data("Data/Map047.rxdata")
    town.events[72].pages[0].list = []
    original = Marshal.dump(town)
    assert(!Ironmon.patch_devon_sequence_map(47, town), "unknown event layout is rejected")
    assert(Marshal.dump(town) == original, "unsupported layout cannot receive a partial patch")
    town = load_data("Data/Map047.rxdata")
    original_events = Marshal.load(Marshal.dump(town.events))
    Ironmon.patch_devon_sequence_map(47, town)
    [60, 73, 92].each do |id|
      assert(Marshal.dump(town.events[id]) == Marshal.dump(original_events[id]), "rival event #{id} remains unchanged")
    end
    office = load_data("Data/Map048.rxdata")
    original_events = Marshal.load(Marshal.dump(office.events))
    Ironmon.patch_devon_sequence_map(48, office)
    (office.events.keys - [79]).each do |id|
      assert(Marshal.dump(office.events[id]) == Marshal.dump(original_events[id]), "office event #{id}, including stairs and Jukebox, remains accessible")
    end
  end

  def self.test_battle_and_rescue
    original = load_data("Data/Map028.rxdata").events[7].pages[0].list
    authored = map(28)
    battle = authored.events[7].pages[0].list
    battle_index = proc { |list| list.index { |c| c.code == 111 && c.parameters.inspect.include?("pbTrainerBattle") } }
    assert(Marshal.dump(original[battle_index.call(original)..-1]) == Marshal.dump(battle[battle_index.call(battle)..-1]), "battle identity, arguments and victory branch are unchanged")
    assert(!Ironmon.run_devon_rescue && @grants.empty?, "rescue cannot complete before victory")
    $game_self_switches[[28, 7, "A"]] = true
    @lost = true
    assert(!Ironmon.run_devon_rescue && @grants.empty?, "a failed run cannot receive rescue rewards")
    @lost = false
    $game_map.refresh
    execute(authored.events[7].pages[1], 7)
    assert(@items[:DEVONPARTS] == 1 && $game_switches[2076], "victory awards the parts and rescues Peeko")
    assert([8, 16].all? { |id| $game_self_switches[[28, id, "A"]] }, "both Pokemon event cleanup flags are retained")
    assert([7, 8, 16].all? { |id| $game_map.events[id].character_name.empty? }, "grunt, Briney and Peeko disappear after rescue")
    assert(!Ironmon.run_devon_rescue && @grants == [:DEVONPARTS], "rescue rewards cannot repeat")
    assert(!$game_switches[2097] && !$game_switches[2095], "rescue does not skip the delivery handoff or boat introduction")
  end

  def self.test_outdoor_handoff
    town = map(47)
    assert(!Ironmon.run_devon_handoff, "handoff requires rescue")
    execute(town.events[69].pages[0], 69)
    execute(town.events[67].pages[0], 67)
    assert($game_variables[96] == 1 && $game_variables[VAR_NB_QUEST_ACTIVE] == 1, "both request entrances accept one quest")
    assert($game_self_switches[[47, 69, "A"]], "north approach autorun is retired")
    $game_switches[2076] = true
    assert(!Ironmon.run_devon_handoff, "handoff also requires the actual parts")
    @items[:DEVONPARTS] = 1
    $game_switches[2096] = true
    execute(town.events[72].pages[0], 72)
    assert(@items == {:DEVONPARTS => 1, :EXPALL => 1, :LETTER => 1}, "all items survive the outdoor handoff")
    assert($game_switches[2097] && !$game_switches[2096], "delivery starts and office autorun ends")
    assert($game_map.map_id == 47 && !$game_temp.player_transferring, "outdoor handoff performs no transfer")
    assert($game_variables[96] == 3 && $game_variables[VAR_NB_QUEST_ACTIVE] == 2 && $game_variables[VAR_NB_QUEST_COMPLETED] == 1 && $game_variables[VAR_KARMA] == 1, "native quest counters and karma are preserved")
    assert($Trainer.quests.map { |q| [q.id, q.completed] } == [["main_stolen_parts", true], ["main_devon_parts", false], ["main_steven_letter", false]], "delivery quests remain active until their actual deliveries")
    before = snapshot
    execute(town.events[63].pages[2], 63)
    assert(snapshot == before, "talking to Stone again cannot duplicate items or bookkeeping")
    $game_map.refresh
    [70, 71, 72].each { |id| assert($game_map.events[id].list.length == 1, "return trigger #{id} is retired") }
    assert(!$game_switches[2092] && !$game_switches[2095] && !$game_switches[2098], "Steven, Briney and rival progression are not marked completed")
  end

  def self.test_legacy_office_and_partial_rewards
    $game_switches[2076] = true
    $game_switches[2096] = true
    @items[:DEVONPARTS] = 1
    @items[:EXPALL] = 1
    office = map(48)
    @reject_item = :LETTER
    assert(!Ironmon.run_devon_handoff && !$game_switches[2097], "failed letter grant does not advance the story")
    assert(@grants.empty?, "already-owned Exp. All is not awarded again")
    @reject_item = nil
    interpreter = Interpreter.new
    interpreter.setup(office.events[79].pages[0].list, 79, 48)
    assert(interpreter.execute_command, "legacy office evaluates the handoff conditional")
    assert(@grants == [:LETTER] && $game_switches[2097] && !$game_switches[2096], "legacy office finishes without duplicate Exp. All")
    assert($game_variables[VAR_NB_QUEST_ACTIVE] == 2, "missing old quest entry is repaired before completion")
    transfer = office.events[79].pages[0].list.find { |command| command.code == 201 }
    assert(transfer.indent == 1 && transfer.parameters == [0, 47, 23, 20, 2, 0], "only a successful office handoff exits at the normal Devon doorway")
    @items.delete(:LETTER)
    before = snapshot
    assert(!Ironmon.run_devon_handoff && snapshot == before, "a delivered letter is never recreated on a later visit")
  end

  def self.run
    @assertions = 0
    [:test_patch_boundaries, :test_battle_and_rescue, :test_outdoor_handoff,
     :test_legacy_office_and_partial_rewards].each do |test|
      with_fixture { send(test) }
    end
    File.binwrite($ironmon_devon_sequence_test_output_path, "Devon sequence runtime tests passed (#{@assertions} assertions)")
  end
end

IronmonDevonSequenceRuntimeTests.run
