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

  def self.with_birthday_fixture
    original_globals = [$PokemonGlobal, $PokemonSystem, $PokemonBag,
                        $game_switches, $game_variables, $game_map, $Trainer]
    original_suspension = Ironmon::GameplayCollection.capture_suspended
    restorations = []
    stub = proc do |name, &implementation|
      restorations << [name, Object.instance_method(name),
                       Object.private_method_defined?(name)]
      Object.send(:define_method, name, &implementation)
      Object.send(:private, name)
    end
    messages = []
    entries = []
    dialogue = { :answer => 0, :messages => [] }
    pending_bubble = nil
    stub.call(:pbCallBub) { |*args| pending_bubble = args }
    stub.call(:pbMessage) do |message, choices = nil, cancel = 0, *_args|
      messages << message
      dialogue[:messages] << {
        :text => message, :choices => choices, :cancel => cancel,
        :bubble => pending_bubble
      }
      pending_bubble = nil
      choices ? dialogue[:answer] : 0
    end
    stub.call(:updatePinkanBerryDisplay) { nil }
    stub.call(:pbSetPokemonCenter) { nil }
    stub.call(:pbSet) { |*_args| nil }
    stub.call(:isPlayerBirthDay?) { true }
    stub.call(:ironmon_birthday_original_enter_common_building) do |kind, city|
      entries << [kind, city]
    end
    Ironmon::GameplayCollection.capture_suspended = true
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_run_ledger = Ironmon.default_run_ledger
    $PokemonGlobal.ironmon_run_ledger["current_attempt"] = {
      "run_id" => "birthday-fixture-first", "result" => "active", "seed" => 12345
    }
    $PokemonSystem = PokemonSystem.new
    $PokemonBag = PokemonBag.new
    $game_switches = Game_Switches.new
    $game_variables = Game_Variables.new
    $game_map = Struct.new(:map_id).new(25)
    $Trainer = Struct.new(:party, :heals, :birth_day, :birth_month).new([], 0, 12, 7)
    $Trainer.define_singleton_method(:heal_party) { self.heals += 1 }
    yield(messages, entries, dialogue)
  ensure
    restorations&.reverse_each do |name, original, was_private|
      Object.send(:define_method, name, original)
      Object.send(:private, name) if was_private
    end
    Ironmon::GameplayCollection.capture_suspended = original_suspension
    $PokemonGlobal, $PokemonSystem, $PokemonBag, $game_switches,
      $game_variables, $game_map, $Trainer = original_globals
  end

  def self.birthday_commands(map_id, page_index)
    map = load_data(sprintf("Data/Map%03d.rxdata", map_id))
    return map.events.fetch(7).pages.fetch(page_index).list
  end

  def self.birthday_gift_command?(command)
    return command.code == 355 &&
      command.parameters == [Ironmon::POKECENTER_BIRTHDAY_GIFT_SCRIPT]
  end

  def self.verify_birthday_event_lists
    [[25, 0], [25, 1], [27, 0]].each do |map_id, page_index|
      original = birthday_commands(map_id, page_index)
      original_bytes = Marshal.dump(original)
      interpreter = Interpreter.new
      interpreter.setup(original, 7, map_id)
      patched = interpreter.instance_variable_get(:@list)
      heal_index = patched.index { |command| command.code == 314 }
      gift_index = patched.index { |command| birthday_gift_command?(command) }
      assert(heal_index, "center #{map_id} page #{page_index} keeps the native heal")
      if map_id == 25 && page_index == 0
        name_index = patched.index do |command|
          command.code == 101 && command.parameters[0].start_with?("Your name is")
        end
        assert(name_index < gift_index && gift_index < heal_index, "registration offers the birthday gift after entering the player's name and before healing")
      else
        assert(gift_index == heal_index + 1, "registered center #{map_id} page #{page_index} offers an unclaimed gift after healing")
      end
      assert(patched[gift_index].indent == patched[heal_index].indent, "the gift stays inside the nurse's active dialogue branch")
      assert(
        patched.count { |command| birthday_gift_command?(command) } == 1,
        "center #{map_id} page #{page_index} has one guarded gift callback"
      )
      assert(
        !patched.any? do |command|
          ["set_player_birthday", "obtainBirthdayHat(:pokecenter)"].include?(
            command.parameters[0]
          ) || (command.code == 101 && command.parameters[0] == "And when is your birthday?")
        end,
        "center #{map_id} page #{page_index} has no date picker or annual hat"
      )
      expected = if map_id == 25 && page_index == 0
                   original[0...15] + original[23..-1]
                 elsif map_id == 27
                   original[3..-1]
                 else
                   original
                 end
      if map_id == 25 && page_index == 0
        spoken = patched.select { |command| command.code == 101 }.map { |command| command.parameters[0] }
        assert(spoken == [
          "Hello! Is this your first visit to a Pokémon Center?",
          "We restore tired Pokémon to full health, free of charge. Let me get you registered first.",
          "Your name is \\PN? Perfect. This will only take a moment.",
          "There we are, all registered!",
          "Now, let's take care of your Pokémon. We'll have them feeling better in no time.",
          "Thank you for waiting. Your Pokémon are back to full health!",
          "Take care, \\PN. We hope to see you again!"
        ], "the first-visit conversation uses the complete approved script")
        native_logic = proc do |command|
          ![101, 401].include?(command.code) && !birthday_gift_command?(command) &&
            !(command.code == 355 && command.parameters[0].to_s.strip == "pbCallBub(2,@event_id,true)")
        end
        assert(patched.select(&native_logic) == expected.select(&native_logic), "rewriting the dialogue preserves every native movement, healing and registration-state command")
      else
        assert(
          patched.reject { |command| birthday_gift_command?(command) } == expected,
          "center #{map_id} page #{page_index} preserves every other native command"
        )
      end
      assert(Marshal.dump(original) == original_bytes, "authored center commands are unchanged")
      interpreter.setup(patched, 7, map_id)
      assert(interpreter.instance_variable_get(:@list).equal?(patched), "center transformation is idempotent")
      if page_index == 1 || map_id == 27
        yes_index = patched.index { |command| command.code == 402 && command.parameters[0] == 0 }
        interpreter.instance_variable_set(:@index, yes_index)
        interpreter.instance_variable_set(:@branch, { 0 => 1 })
        interpreter.execute_command
        assert(
          interpreter.instance_variable_get(:@index) > heal_index + 1,
          "declining center #{map_id} healing skips both healing and the gift"
        )
      end
      $PokemonGlobal.ironmon_mode = false
      interpreter.setup(original, 7, map_id)
      assert(interpreter.instance_variable_get(:@list).equal?(original), "Classic keeps the exact original center list")
      $PokemonGlobal.ironmon_mode = true
      assert(Ironmon.pokecenter_birthday_event_list(map_id, 99, original).equal?(original), "unrelated event IDs remain untouched")
    end
    doorway = load_data("Data/Map026.rxdata").events.fetch(5).pages[0].list
    patched = Ironmon.pokecenter_birthday_event_list(26, 5, doorway)
    assert(patched[3].parameters == [12, "false"], "the shared upstairs doorway returns to the ordinary center")
    assert(doorway[3].parameters == [12, "isPlayerBirthDay?"], "the authored birthday doorway remains unchanged")
  end

  def self.verify_birthday_gift
    with_birthday_fixture do |messages, entries, dialogue|
      verify_birthday_event_lists
      pool = Ironmon.pokecenter_birthday_gift_pool
      [:POTION, :ETHER, :FULLHEAL, :ORANBERRY, :FIGYBERRY, :ENIGMABERRY,
       :REVIVE, :MAXREVIVE, :REVIVALHERB, :SACREDASH, :POISONMUSHROOM,
       :BANANA, :COFFEE, :ROCKETMEAL, :FANCYMEAL].each do |id|
        assert(pool.include?(id), "#{id} is a possible consumable recovery gift")
      end
      [:BLUEFLUTE, :REDFLUTE, :YELLOWFLUTE, :GOLDENBANANA, :LEFTOVERS,
       :SHELLBELL, :PPUP, :RARECANDY, :HPUP, :POKEFLUTE].each do |id|
        assert(!pool.include?(id), "#{id} is outside the consumable recovery gift pool")
      end
      assert(pool.uniq == pool && pool == pool.sort_by { |id| id.to_s }, "gift identifiers have stable unique ordering")
      item = Ironmon.pokecenter_birthday_gift_item(12345)
      assert(item == Ironmon.pokecenter_birthday_gift_item(12345), "gift selection is deterministic for the run seed")
      assert((1..100).map { |seed| Ironmon.pokecenter_birthday_gift_item(seed) }.uniq.length > 1, "new run seeds can select different gifts")

      receiver = Object.new
      receiver.send(:enter_pokemon_center, :OLDALE)
      assert(entries[-1] == [:POKEMON_CENTER, :OLDALE], "the native center entry ignores annual birthday routing in Ironmon")
      receiver.send(:enter_pokemon_center, :PETALBURG)
      assert(entries[-1] == [:POKEMON_CENTER, :PETALBURG], "a different city uses the same center and gift eligibility")
      receiver.send(:enter_common_building, :POKEMART, :PETALBURG)
      assert(entries[-1] == [:POKEMART, :PETALBURG], "other shared buildings retain their native type")

      original = birthday_commands(25, 0)
      interpreter = Interpreter.new
      interpreter.setup(original, 7, 25)
      list = interpreter.instance_variable_get(:@list)
      heal_index = list.index { |command| command.code == 314 }
      gift_index = list.index { |command| birthday_gift_command?(command) }
      interpreter.instance_variable_set(:@index, gift_index)
      interpreter.execute_command
      assert($PokemonBag.pbQuantity(item) == 1, "the actual nurse callback delivers exactly one item through native pbReceiveItem")
      assert($Trainer.heals == 0, "registration finishes the birthday conversation before starting the healing service")
      question = dialogue[:messages][0]
      assert(question[:choices] == ["Yes", "No"] && question[:cancel] == 2, "the birthday question has Yes/No choices and cancel selects No")
      assert(question[:text] == "Oh... the system is showing a birthday notification. Is today your birthday?", "the nurse asks the approved birthday question instead of speaking for the player")
      assert(question[:bubble] == [2, 7, true], "the prompt uses the nurse's ordinary speech bubble")
      yes_response = dialogue[:messages][1]
      assert(yes_response[:text] == "Wonderful! Happy birthday, \\PN!" && yes_response[:bubble] == [2, 7, true], "Yes keeps the approved birthday greeting in the nurse's speech bubble")
      assert(dialogue[:messages][2][:text] == "It looks like the system has a little present waiting for you. You registered just in time!", "Yes explains the system's present using the approved wording")
      assert(dialogue[:messages][3][:text] == "Here you are. I hope it comes in handy on your journey!", "the shared handover follows the selected reply")
      assert(dialogue[:messages][0...4].all? { |message| message[:bubble] == [2, 7, true] }, "every birthday line uses the nurse's ordinary speech bubble")
      assert(messages.none? { |text| text.start_with?("\\PN:") }, "there is no invented player dialogue")
      interpreter.instance_variable_set(:@index, heal_index)
      interpreter.execute_command
      assert($Trainer.heals == 1 && $PokemonBag.pbQuantity(item) == 1, "the nurse's original healing command still runs after the gift")
      assert($PokemonGlobal.ironmon_pokecenter_birthday_run_id == "birthday-fixture-first", "successful delivery saves the claiming run ID")
      assert($Trainer.birth_day == 12 && $Trainer.birth_month == 7, "the replacement never changes the player's birthday")
      message_count = messages.length
      assert(!Ironmon.receive_pokecenter_birthday_gift, "later healing cannot claim another gift in the same run")
      assert(messages.length == message_count && $PokemonBag.pbQuantity(item) == 1, "repeat healing has no birthday dialogue or extra item")
      $PokemonGlobal = Marshal.load(Marshal.dump($PokemonGlobal))
      assert(!Ironmon.receive_pokecenter_birthday_gift, "saving and reloading preserves the run's gift claim")

      attempt = Ironmon.current_run_attempt
      attempt["run_id"] = "birthday-fixture-new-attempt"
      dialogue[:answer] = 1
      message_start = dialogue[:messages].length
      assert(Ironmon.receive_pokecenter_birthday_gift, "a fresh attempt receives its own gift even with the same seed")
      assert($PokemonBag.pbQuantity(item) == 2, "a new attempt adds one further item")
      no_response = dialogue[:messages][message_start + 1]
      assert(no_response[:text] == "No? But it's right here in the system... I've only just entered your name!" && no_response[:bubble] == [2, 7, true], "No receives the approved system joke in the nurse's speech bubble and the same reward")
      assert(dialogue[:messages][message_start + 2][:text] == "Well, it's already approved your present. I'm certainly not going to argue with it!", "No continues with the approved present explanation")
      attempt["run_id"] = "birthday-fixture-full-bag"
      message_start = messages.length
      $PokemonBag.define_singleton_method(:pbCanStore?) { |*_args| false }
      assert(!Ironmon.receive_pokecenter_birthday_gift, "a full Bag defers the present before announcing its handover")
      assert(messages[message_start..-1].none? { |text| text.start_with?("Here you are.") || text.include?("You obtained") }, "a full Bag does not falsely announce that the present was received")
      assert(messages[-1] == "Oh, your Bag is full! Don't worry, I'll keep your present here. Come back when you have some room.", "the full-Bag reply uses the approved wording")
      assert($PokemonGlobal.ironmon_pokecenter_birthday_run_id == "birthday-fixture-new-attempt", "checking capacity does not consume the unclaimed present")
      $PokemonBag.singleton_class.send(:remove_method, :pbCanStore?)
      $PokemonBag.define_singleton_method(:pbStoreItem) { |*_args| false }
      assert(!Ironmon.receive_pokecenter_birthday_gift, "a rejected native inventory insertion does not claim the gift")
      assert(dialogue[:messages][-1][:bubble] == [2, 7, true], "the full-Bag reply also remains in the nurse's speech bubble")
      assert($PokemonGlobal.ironmon_pokecenter_birthday_run_id == "birthday-fixture-new-attempt", "a full Bag preserves the previous claim instead of consuming this run's gift")
      $PokemonBag.singleton_class.send(:remove_method, :pbStoreItem)
      assert(Ironmon.receive_pokecenter_birthday_gift && $PokemonBag.pbQuantity(item) == 3, "a later visit retries the same seed-selected item after space is available")

      attempt["run_id"] = "birthday-fixture-ended"
      ["lost", "won", "abandoned"].each do |result|
        attempt["result"] = result
        assert(!Ironmon.receive_pokecenter_birthday_gift, "a #{result} attempt cannot receive a new gift")
      end
      attempt["result"] = "active"
      $PokemonGlobal.ironmon_mode = false
      assert(!Ironmon.receive_pokecenter_birthday_gift, "Classic mode never receives an Ironmon gift")
      receiver.send(:enter_pokemon_center, :OLDALE)
      assert(entries[-1] == [:POKEMON_CENTER_BIRTHDAY, :OLDALE], "Classic retains the native annual birthday-center routing")
    end
  end

  def self.verify_later_dialogue_hook
    if !Interpreter.method_defined?(:_remoteNPCDialog_setup)
      load "Data/Scripts/998_Experimental/RemoteNPCDialogs/EventDialogIntercepter.rb"
    end
    assert(Interpreter.method_defined?(:_remoteNPCDialog_setup), "the native dialogue hook loads after the birthday hook")
    with_birthday_fixture do
      list = load_data("Data/Map005.rxdata").events.fetch(2).pages[0].list
      interpreter = Interpreter.new
      interpreter.setup(list, 2, 5)
      assert(interpreter.instance_variable_get(:@list).equal?(list), "the starter bag retains its commands with both event hooks loaded")
      assert(interpreter.instance_variable_get(:@event_id) == 2, "the native interpreter setup completes for the starter bag")
      verify_birthday_event_lists
    end
  end

  def self.run
    verify_map(30, 49, 38, "NPC_Hoenn_Ranger_F", "forest's spores")
    verify_map(31, 5, 17, "NPC_worker", "Route 116")
    unrelated = load_data("Data/Map005.rxdata")
    assert(
      !Ironmon.patch_healing_npc_map(5, unrelated),
      "unrelated maps receive no healing NPC"
    )
    verify_birthday_gift
    verify_later_dialogue_hook
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
