module IronmonNewPlayerProtectionRuntimeTests
  OUTPUT_PATH = $ironmon_new_player_protection_test_output_path.to_s

  def self.assert(condition, message)
    raise "New-player protection runtime test failed: #{message}" if !condition
  end

  def self.with_ironmon_active(value)
    singleton = class << Ironmon; self; end
    singleton.send(
      :alias_method, :new_player_test_original_active, :active?
    )
    singleton.send(:define_method, :active?) { value }
    return yield
  ensure
    if singleton && singleton.method_defined?(:new_player_test_original_active)
      singleton.send(:alias_method, :active?, :new_player_test_original_active)
      singleton.send(:remove_method, :new_player_test_original_active)
    end
  end

  def self.with_gift_confirmation(answer)
    prompts = []
    singleton = class << Ironmon; self; end
    singleton.send(:define_method, :pbConfirmMessageSerious) do |message|
      prompts << message
      answer
    end
    result = yield(prompts)
    return result
  ensure
    singleton.send(:remove_method, :pbConfirmMessageSerious) if singleton &&
      singleton.instance_methods(false).include?(:pbConfirmMessageSerious)
  end

  def self.with_stubbed_gift_add
    calls = []
    Object.send(
      :alias_method, :new_player_test_original_gift_add,
      :ironmon_pivot_original_pb_add_pokemon
    )
    Object.send(:define_method, :ironmon_pivot_original_pb_add_pokemon) do |*args|
      calls << [args, Ironmon.current_acquisition_source]
      :accepted
    end
    Object.send(:private, :ironmon_pivot_original_pb_add_pokemon)
    return yield(calls)
  ensure
    if Object.private_method_defined?(:new_player_test_original_gift_add)
      Object.send(
        :alias_method, :ironmon_pivot_original_pb_add_pokemon,
        :new_player_test_original_gift_add
      )
      Object.send(:remove_method, :new_player_test_original_gift_add)
      Object.send(:private, :ironmon_pivot_original_pb_add_pokemon)
    end
  end

  def self.with_player_highest_level(level)
    original_trainer = $Trainer
    player = Object.new
    player.define_singleton_method(:highest_level_pokemon_in_party) { level }
    $Trainer = player
    return yield
  ensure
    $Trainer = original_trainer
  end

  def self.with_captured_messages
    messages = []
    Object.send(
      :alias_method, :new_player_test_original_pb_message, :pbMessage
    )
    Object.send(:define_method, :pbMessage) do |message, commands = nil, *_args|
      messages << [message, commands]
      0
    end
    Object.send(:private, :pbMessage)
    return yield(messages)
  ensure
    if Object.private_method_defined?(:new_player_test_original_pb_message)
      Object.send(
        :alias_method, :pbMessage, :new_player_test_original_pb_message
      )
      Object.send(:remove_method, :new_player_test_original_pb_message)
      Object.send(:private, :pbMessage)
    end
  end

  def self.with_interpreter_message_state(map_id)
    original_game_temp = $game_temp
    original_game_map = $game_map
    game_temp = Object.new
    game_temp.define_singleton_method(:message_window_showing) { false }
    game_map = Object.new
    game_map.define_singleton_method(:map_id) { map_id }
    $game_temp = game_temp
    $game_map = game_map
    return yield
  ensure
    $game_temp = original_game_temp
    $game_map = original_game_map
  end

  def self.test_gift_confirmation_is_separate_and_safe
    with_ironmon_active(true) do
      with_gift_confirmation(false) do |prompts|
        accepted = Ironmon.confirm_gift_acquisition(:PIKACHU)
        assert(!accepted, "gift can be declined before acquisition")
        assert(prompts.length == 1, "ordinary gift receives one confirmation")
        assert(
          prompts[0].include?("mandatory pivot"),
          "confirmation explains the pivot consequence"
        )
      end
      with_gift_confirmation(false) do |prompts|
        accepted = Ironmon.with_starter_acquisition do
          Ironmon.confirm_gift_acquisition(:PIKACHU)
        end
        assert(accepted, "starter acquisition bypasses refusal")
        assert(prompts.empty?, "starter acquisition has no gift confirmation")
      end
      with_gift_confirmation(false) do |prompts|
        accepted = Ironmon.with_acquisition_exclusion(:rental) do
          Ironmon.confirm_gift_acquisition(:PIKACHU)
        end
        assert(accepted, "temporary excluded acquisition bypasses refusal")
        assert(prompts.empty?, "excluded acquisition has no gift confirmation")
      end
    end
  end

  def self.test_gift_wrapper_declines_before_original_acquisition
    receiver = Object.new
    with_ironmon_active(true) do
      with_stubbed_gift_add do |calls|
        with_gift_confirmation(false) do |_prompts|
          result = receiver.send(:pbAddPokemon, :PIKACHU, 10)
          assert(result == false, "declined gift returns false")
          assert(calls.empty?, "declined gift never reaches acquisition")
        end
        with_gift_confirmation(true) do |_prompts|
          result = receiver.send(:pbAddPokemon, :PIKACHU, 10)
          assert(result == :accepted, "accepted gift reaches acquisition")
          assert(calls.length == 1, "accepted gift reaches acquisition once")
          assert(
            calls[0][1] == :gift_or_static,
            "accepted gift retains gift acquisition source"
          )
        end
      end
    end
  end

  def self.test_optional_trainer_choice_shows_effective_minimum
    map = load_data("Data/Map077.rxdata")
    page = map.events[5].pages[0]
    choice_index = page.list.index { |command| command.code == 102 }
    assert(choice_index, "Winstrate optional challenge choice exists")
    trainer = GameData::Trainer.get(:POKEFAN_M, "Victor", 0)
    expected = trainer.pokemon.map do |pokemon|
      Ironmon.scaled_level(pokemon[:level])
    end.min
    with_ironmon_active(true) do
      labels = Ironmon.optional_trainer_choice_labels(page.list, choice_index)
      assert(
        labels[0] == "Yes (lowest Lv. #{expected})",
        "Victor's Yes choice shows the weakest scaled team level"
      )
      assert(labels[1] == "No", "No choice remains unchanged")
    end
  end

  def self.test_scaled_adventurer_choice_uses_player_based_battle_level
    with_player_highest_level(21) do
      map = load_data("Data/Map007.rxdata")
      page = map.events[52].pages[0]
      choice_index = page.list.index { |command| command.code == 102 }
      assert(choice_index, "Petalburg adventurer challenge choice exists")
      with_ironmon_active(true) do
        labels = Ironmon.optional_trainer_choice_labels(page.list, choice_index)
        assert(
          labels[0] == "Yes (lowest Lv. 21)",
          "player-matched adventurer Yes choice shows the unmultiplied battle level"
        )
        assert(labels[1] == "No", "scaled adventurer No choice is unchanged")
      end
    end
  end

  def self.test_embedded_adventurer_choice_uses_runtime_interpreter_path
    map = load_data("Data/Map007.rxdata")
    page = map.events[52].pages[0]
    choice_index = page.list.index { |command| command.code == 102 }
    text_index = (0...choice_index).to_a.reverse.find do |index|
      page.list[index].code == 101
    end
    assert(text_index, "Petalburg adventurer prompt text exists")
    original_choices = page.list[choice_index].parameters[0].dup
    interpreter = Interpreter.new
    interpreter.setup(page.list, 52, 7)
    interpreter.instance_variable_set(:@index, text_index)
    with_player_highest_level(21) do
      with_ironmon_active(true) do
        with_interpreter_message_state(7) do
          with_captured_messages do |messages|
            interpreter.command_101
            assert(messages.length == 1, "embedded prompt displays one message")
            assert(
              messages[0][1][0] == "Yes (lowest Lv. 21)",
              "embedded prompt decorates Yes through the runtime interpreter"
            )
            assert(
              messages[0][1][1] == "No",
              "embedded prompt leaves No unchanged"
            )
          end
        end
      end
    end
    assert(
      page.list[choice_index].parameters[0] == original_choices,
      "runtime prompt restores the event's authored choices"
    )
  end

  def self.test_move_tutor_choices_show_their_actual_battle_level
    with_player_highest_level(21) do
      map = load_data("Data/Map020.rxdata")
      page = map.events[41].pages[0]
      choice_index = page.list.index { |command| command.code == 102 }
      assert(choice_index, "Echoed Voice tutor challenge choice exists")
      with_ironmon_active(true) do
        labels = Ironmon.optional_trainer_choice_labels(page.list, choice_index)
        assert(
          labels[0] == "Yes (lowest Lv. 21)",
          "player-matched move tutor shows the unmultiplied battle level"
        )
      end
    end

    map = load_data("Data/Map032.rxdata")
    page = map.events[4].pages[0]
    choice_index = page.list.index { |command| command.code == 102 }
    assert(choice_index, "Flash tutor challenge choice exists")
    trainer = GameData::Trainer.get(:HIKER, "Sunny", 0)
    expected = trainer.pokemon.map do |pokemon|
      Ironmon.scaled_level(pokemon[:level])
    end.min
    with_player_highest_level(21) do
      with_ironmon_active(true) do
        labels = Ironmon.optional_trainer_choice_labels(page.list, choice_index)
        assert(
          labels[0] == "Yes (lowest Lv. #{expected})",
          "non-scaling move tutor uses the authored Ironmon party level"
        )
      end
    end
  end

  def self.test_warning_requires_trainer_in_yes_branch
    list = [
      RPG::EventCommand.new(102, 0, [["Yes", "No"], 2]),
      RPG::EventCommand.new(402, 0, [0, "Yes"]),
      RPG::EventCommand.new(355, 1, ["pbMessage('Safe')"]),
      RPG::EventCommand.new(402, 0, [1, "No"]),
      RPG::EventCommand.new(
        111, 1, [12, "pbTrainerBattle(:POKEFAN_M,'Victor')"]
      ),
      RPG::EventCommand.new(404, 0, [])
    ]
    with_ironmon_active(true) do
      labels = Ironmon.optional_trainer_choice_labels(list, 0)
      assert(labels == ["Yes", "No"], "No-branch trainer adds no warning")
    end
  end

  def self.run
    test_gift_confirmation_is_separate_and_safe
    test_gift_wrapper_declines_before_original_acquisition
    test_optional_trainer_choice_shows_effective_minimum
    test_scaled_adventurer_choice_uses_player_based_battle_level
    test_embedded_adventurer_choice_uses_runtime_interpreter_path
    test_move_tutor_choices_show_their_actual_battle_level
    test_warning_requires_trainer_in_yes_branch
    File.binwrite(OUTPUT_PATH, "new-player protection runtime tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonNewPlayerProtectionRuntimeTests.run
