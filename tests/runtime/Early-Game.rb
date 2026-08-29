module IronmonEarlyGameRuntimeTests
  OUTPUT_PATH = $ironmon_early_game_test_output_path.to_s

  def self.assert(condition, message)
    raise "Early-game runtime test failed: #{message}" if !condition
  end

  def self.script_commands(page)
    return page.list.select { |command| command.code == 355 }.map do |command|
      command.parameters[0]
    end
  end

  def self.test_lab_patch
    map = load_data("Data/Map018.rxdata")
    patched = Ironmon.patch_early_game_map(
      Ironmon::EARLY_GAME_LAB_MAP_ID, map
    )
    page = map.events[
      Ironmon::EARLY_GAME_LAB_PROFESSOR_EVENT_ID
    ].pages[0]
    assert(patched, "the supported lab event is patched")
    assert(
      script_commands(page) == ["Ironmon.run_first_rival_lab_sequence"],
      "the lab opening delegates to the shortened rival sequence"
    )
    rival_page = map.events[
      Ironmon::EARLY_GAME_LAB_RIVAL_EVENT_ID
    ].pages[0]
    assert(
      !rival_page.condition.switch1_valid &&
        !rival_page.condition.switch2_valid &&
        !rival_page.condition.variable_valid &&
        !rival_page.condition.self_switch_valid &&
        rival_page.graphic.character_name.empty? && rival_page.through &&
        !rival_page.direction_fix,
      "the rival event stays active, hidden, and pass-through before entering"
    )
    assert(
      rival_page.list.length == 1 && rival_page.list[0].code == 0,
      "the hidden rival page cannot trigger the old return-to-lab command"
    )
    runtime_source = File.binread(
      "Data/Scripts/997_Ironmon/016_Progression_Early_Game.rb"
    )
    assert(
      runtime_source.include?("normalized_early_game_rival_appearance") &&
        runtime_source.include?("materialize_early_game_rival_character") &&
        runtime_source.include?(
          "$Trainer.rival_appearance = rival_appearance"
        ),
      "the lab rival installs a concrete opposite-gender character sheet"
    )
    assert(
      runtime_source.include?("rival.turn_toward_player") &&
        runtime_source.include?("rival.turn_down") &&
        runtime_source.include?("I'm heading out too. See you on the road!") &&
        runtime_source.include?("PBMoveRoute::ChangeSpeed, 5") &&
        runtime_source.include?(
          "announce_tracker_active_run_preparation_ready"
        ),
      "the rival faces the player and visibly runs out after the battle"
    )
    loss_guard = runtime_source.index("return true if !won || failed_run_locked?")
    preparation_announcement = runtime_source.index(
      "announce_tracker_active_run_preparation_ready"
    )
    assert(
      loss_guard && preparation_announcement &&
        preparation_announcement > loss_guard,
      "only the rival victory path announces background preparation"
    )
    assert(
      !runtime_source.include?("clearEventCustomAppearance("),
      "the lab sequence avoids the base game's nil event-ID cleanup bug"
    )
  end

  def self.test_rival_appearance_assets
    cases = [
      [GENDER_FEMALE, HAT_MAY, CLOTHES_MAY, HAIR_MAY],
      [GENDER_MALE, HAT_BRENDAN, CLOTHES_BRENDAN, HAIR_BRENDAN]
    ]
    cases.each do |gender, expected_hat, expected_clothes, expected_hair|
      incomplete = TrainerAppearance.new(3, nil, nil, nil)
      appearance = Ironmon.normalized_early_game_rival_appearance(
        gender, incomplete
      )
      assert(
        appearance.hat == expected_hat &&
          appearance.clothes == expected_clothes &&
          appearance.hair == "3_#{expected_hair}",
        "the rival appearance has canonical gendered clothes, hair, and hat"
      )
      assert(
        pbResolveBitmap(getOverworldOutfitFilename(appearance.clothes)) &&
          pbResolveBitmap(getOverworldHairFilename(appearance.hair)) &&
          pbResolveBitmap(getOverworldHatFilename(appearance.hat)),
        "every normalized rival overworld layer resolves to an installed asset"
      )
      bitmap = generateNPCClothedBitmapStatic(appearance)
      assert(bitmap && !bitmap.disposed?, "the dressed rival bitmap is generated")
      bitmap.dispose
      character_name = Ironmon.materialize_early_game_rival_character(
        gender, appearance
      )
      assert(
        pbResolveBitmap("Graphics/Characters/#{character_name}"),
        "the dressed rival character sheet is materialized for map rendering"
      )
    end
  end

  def self.test_starter_rescue_battle_boundary
    map = load_data("Data/Map005.rxdata")
    event = map.events[Ironmon::STARTER_ENCOUNTER_EVENT_ID]
    assert(
      event && event.pages.any? do |page|
        selects_starter = page.list.any? do |command|
          [355, 655].include?(command.code) &&
            command.parameters[0].to_s.include?("hoennSelect")
        end
        starts_battle = page.list.any? do |command|
          command.parameters.inspect.include?("pbWildBattle")
        end
        selects_starter && starts_battle
      end,
      "the starter rescue boundary matches the authored selection and battle event"
    )
    Ironmon.finish_starter_rescue_battle
    Ironmon.arm_starter_rescue_battle
    assert(
      Ironmon.starter_rescue_battle_pending?,
      "starter selection arms the following rescue battle"
    )
    assert(
      Ironmon.finish_starter_rescue_battle &&
        !Ironmon.starter_rescue_battle_pending?,
      "the rescue battle consumes its pending marker exactly once"
    )
    compatibility_source = File.binread(
      "Data/Scripts/997_Ironmon/024_Release_073_Gameplay.rb"
    )
    assert(
      compatibility_source.include?("Ironmon.arm_starter_rescue_battle") &&
        compatibility_source.scan(
          "Ironmon.abort_current_early_game_event"
        ).length == 2,
      "both opening battle losses abort their remaining story commands"
    )
  end

  def self.test_route_101_rival_exit_patch
    map = load_data("Data/Map005.rxdata")
    patched = Ironmon.patch_early_game_map(
      Ironmon::EARLY_GAME_ROUTE_101_MAP_ID, map
    )
    page = map.events[
      Ironmon::EARLY_GAME_ROUTE_101_RIVAL_EVENT_ID
    ].pages[0]
    closing_index = page.list.index do |command|
      command.code == 101 && command.parameters[0] ==
        "Oh well, let's head to Oldale Town!"
    end
    assert(patched, "the supported Route 101 rival event is patched")
    assert(closing_index, "the Route 101 closing line points to Oldale")
    movement_command = page.list[(closing_index + 1)..-1].find do |command|
      command.code == 209 && command.parameters[0] == 0
    end
    movement_codes = movement_command.parameters[1].list.map do |movement|
      movement.code
    end
    assert(
      movement_codes.count(PBMoveRoute::Right) == 0 &&
        movement_codes.count(PBMoveRoute::Up) == 7 &&
        !movement_codes.include?(PBMoveRoute::Down) &&
        !page.direction_fix,
      "the Route 101 rival walks seven tiles straight north while facing north"
    )
    assert(
      movement_codes[-2] == PBMoveRoute::Opacity,
      "the Route 101 rival disappears only after completing the exit path"
    )
  end

  def self.test_wally_patches
    gym = load_data("Data/Map061.rxdata")
    patched_gym = Ironmon.patch_early_game_map(
      Ironmon::EARLY_GAME_PETALBURG_GYM_MAP_ID, gym
    )
    gym_page = gym.events[
      Ironmon::EARLY_GAME_PETALBURG_GYM_CONTROLLER_EVENT_ID
    ].pages[0]
    assert(patched_gym, "the supported Petalburg Gym event is patched")
    assert(
      script_commands(gym_page).include?(
        "Ironmon.run_wally_gym_gift_sequence"
      ),
      "the gym event delegates Wally's gift to Ironmon"
    )
    assert(
      gym_page.list.any? do |command|
        command.code == 108 && command.parameters[0] == "Wally enters"
      end,
      "the shortened gym event keeps Wally's entrance"
    )
    pokemon_event = gym.events[
      Ironmon::EARLY_GAME_PETALBURG_GYM_POKEMON_EVENT_ID
    ]
    assert(
      pokemon_event &&
        pokemon_event.name ==
          Ironmon::EARLY_GAME_PETALBURG_GYM_POKEMON_EVENT_NAME &&
        pokemon_event.x == Ironmon::EARLY_GAME_PETALBURG_GYM_POKEMON_X &&
        pokemon_event.y == Ironmon::EARLY_GAME_PETALBURG_GYM_POKEMON_Y &&
        pokemon_event.pages[0].graphic.character_name.empty? &&
        pokemon_event.pages[0].through,
      "the Gym patch creates a hidden pass-through wild Pokemon event"
    )
    runtime_source = File.binread(
      "Data/Scripts/997_Ironmon/016_Progression_Early_Game.rb"
    )
    assert(
      runtime_source.include?(
        "accept_early_game_quest(\"main_wally\")"
      ) && !runtime_source.include?("pbQuest(\"main_wally\", false)") &&
        !runtime_source.include?("queue_early_game_gym_transfer") &&
        !runtime_source.include?("wally_unfollow"),
      "Wally's quest is accepted without the broken quest-icon lookup"
    )
    assert(
      runtime_source.include?(
        "$game_player.moveto(\n      EARLY_GAME_WALLY_RETURN_PLAYER_X"
      ) && runtime_source.include?(
        "$game_player.direction = EARLY_GAME_WALLY_RETURN_PLAYER_DIRECTION"
      ),
      "the in-Gym catch aligns the player for the original follow-up route"
    )
    return_dialogue = gym.events[9].pages[0].list.select do |command|
      [101, 401].include?(command.code)
    end.map { |command| command.parameters[0] }
    assert(
      return_dialogue.any? { |text| text.include?("ran into the Gym") } &&
        return_dialogue.include?("that I was able to catch \\V[2]. ") &&
        !return_dialogue.any? { |text| text.include?("Ralts") },
      "the return dialogue describes and names the in-Gym catch"
    )
    assert(
      return_dialogue.include?(
        "And I can't forget the \\V[1] you gave me, Mr. Norman!"
      ),
      "the return dialogue credits Norman for the gift"
    )

    town = load_data("Data/Map007.rxdata")
    assert(
      !Ironmon.patch_early_game_map(7, town),
      "Petalburg Town no longer owns the quick catch trigger"
    )
  end

  def self.test_unsupported_map_is_unchanged
    map = load_data("Data/Map008.rxdata")
    assert(
      !Ironmon.patch_early_game_map(8, map),
      "unrelated maps are not patched"
    )
  end

  def self.test_early_loss_reset_gate
    singleton = class << Ironmon; self; end
    singleton.send(
      :alias_method, :early_game_original_failed_run_locked,
      :failed_run_locked?
    )
    singleton.send(
      :alias_method, :early_game_original_start_checkpoint_reset,
      :start_checkpoint_reset
    )
    singleton.send(
      :alias_method, :early_game_original_complete_run,
      :complete_run
    )
    resets = []
    completions = []
    locked = false
    singleton.send(:define_method, :failed_run_locked?) { locked }
    singleton.send(:define_method, :complete_run) do |result|
      completions << result
      locked = true if result == :lost
      true
    end
    singleton.send(:define_method, :start_checkpoint_reset) do |automatic|
      resets << automatic
      true
    end

    assert(
      !Ironmon.reset_after_early_game_loss(true, true),
      "a victory never resets"
    )
    assert(
      !Ironmon.reset_after_early_game_loss(false, false),
      "a later loss does not use the forced early reset"
    )
    assert(
      Ironmon.reset_after_early_game_loss(false, true),
      "an early loss queues a reset"
    )
    assert(completions == [:lost], "an early loss is locked before resetting")
    assert(resets.empty?, "the battle stack does not start the reset scene")
    assert(
      Ironmon.finish_queued_early_game_loss_reset,
      "the queued reset starts after the event interpreter yields"
    )
    assert(resets == [true], "the deferred early reset is automatic")
  ensure
    if singleton && singleton.method_defined?(
      :early_game_original_failed_run_locked
    )
      singleton.send(
        :alias_method, :failed_run_locked?,
        :early_game_original_failed_run_locked
      )
      singleton.send(:remove_method, :early_game_original_failed_run_locked)
    end
    if singleton && singleton.method_defined?(
      :early_game_original_start_checkpoint_reset
    )
      singleton.send(
        :alias_method, :start_checkpoint_reset,
        :early_game_original_start_checkpoint_reset
      )
      singleton.send(
        :remove_method, :early_game_original_start_checkpoint_reset
      )
    end
    if singleton && singleton.method_defined?(
      :early_game_original_complete_run
    )
      singleton.send(
        :alias_method, :complete_run,
        :early_game_original_complete_run
      )
      singleton.send(:remove_method, :early_game_original_complete_run)
    end
    Ironmon.instance_variable_set(:@early_game_loss_reset_queued, false)
  end

  def self.test_early_loss_event_abort
    singleton = class << Ironmon; self; end
    singleton.send(
      :alias_method, :early_game_original_map_interpreter,
      :pbMapInterpreter
    )
    actions = []
    event = Object.new
    event.define_singleton_method(:erase) { actions << :erase }
    interpreter = Object.new
    interpreter.define_singleton_method(:running?) { true }
    interpreter.define_singleton_method(:get_self) { event }
    interpreter.define_singleton_method(:command_end) { actions << :end }
    singleton.send(:define_method, :pbMapInterpreter) { interpreter }

    assert(
      Ironmon.abort_current_early_game_event,
      "an opening loss aborts its active map event"
    )
    assert(
      actions == [:erase, :end],
      "the autorun is erased before its interpreter can restart it"
    )
  ensure
    if singleton && singleton.method_defined?(
      :early_game_original_map_interpreter
    )
      singleton.send(
        :alias_method, :pbMapInterpreter,
        :early_game_original_map_interpreter
      )
      singleton.send(:remove_method, :early_game_original_map_interpreter)
    end
  end

  def self.test_conditional_event_abort_queue
    Ironmon.consume_queued_early_game_conditional_event_abort
    assert(
      Ironmon.queue_early_game_conditional_event_abort,
      "a starter-battle loss queues its conditional event abort"
    )
    assert(
      Ironmon.consume_queued_early_game_conditional_event_abort &&
        !Ironmon.consume_queued_early_game_conditional_event_abort,
      "the conditional event abort is consumed exactly once"
    )
    compatibility_source = File.binread(
      "Data/Scripts/997_Ironmon/024_Release_073_Gameplay.rb"
    )
    assert(
      compatibility_source.include?(
        "ironmon_073_original_command_111"
      ) && compatibility_source.include?(
        "Ironmon.queue_early_game_conditional_event_abort"
      ),
      "the starter event aborts only after its conditional result is stored"
    )
  ensure
    Ironmon.consume_queued_early_game_conditional_event_abort
  end

  def self.test_starter_loss_conditional_interpreter_boundary
    singleton = class << Ironmon; self; end
    singleton.send(
      :alias_method, :early_game_original_map_interpreter,
      :pbMapInterpreter
    )
    erased = false
    event = Object.new
    event.define_singleton_method(:erase) { erased = true }
    interpreter = Interpreter.new
    interpreter.define_singleton_method(:get_self) { event }
    interpreter.setup([
      RPG::EventCommand.new(111, 0, [
        12,
        "Ironmon.queue_early_game_conditional_event_abort; false"
      ]),
      RPG::EventCommand.new(0, 0, [])
    ], 0, Ironmon::EARLY_GAME_ROUTE_101_MAP_ID)
    singleton.send(:define_method, :pbMapInterpreter) { interpreter }

    result = interpreter.execute_command

    assert(
      !result && erased && !interpreter.running?,
      "the starter conditional stores its result before ending the event"
    )
  ensure
    Ironmon.consume_queued_early_game_conditional_event_abort
    if singleton && singleton.method_defined?(
      :early_game_original_map_interpreter
    )
      singleton.send(
        :alias_method, :pbMapInterpreter,
        :early_game_original_map_interpreter
      )
      singleton.send(:remove_method, :early_game_original_map_interpreter)
    end
  end

  def self.run
    test_lab_patch
    test_rival_appearance_assets
    test_starter_rescue_battle_boundary
    test_route_101_rival_exit_patch
    test_wally_patches
    test_unsupported_map_is_unchanged
    test_early_loss_reset_gate
    test_early_loss_event_abort
    test_conditional_event_abort_queue
    test_starter_loss_conditional_interpreter_boundary
    File.binwrite(OUTPUT_PATH, "early-game runtime tests passed")
  end
end

IronmonEarlyGameRuntimeTests.run
