#===============================================================================
# Ironmon shortened Hoenn opening events
#===============================================================================

module Ironmon
  EARLY_GAME_ROUTE_101_MAP_ID = 5
  EARLY_GAME_ROUTE_101_RIVAL_EVENT_ID = 20
  EARLY_GAME_LAB_MAP_ID = 18
  EARLY_GAME_LAB_PROFESSOR_EVENT_ID = 5
  EARLY_GAME_LAB_RIVAL_EVENT_ID = 19
  EARLY_GAME_PETALBURG_GYM_MAP_ID = 61
  EARLY_GAME_PETALBURG_GYM_CONTROLLER_EVENT_ID = 6
  EARLY_GAME_PETALBURG_GYM_WALLY_EVENT_ID = 7
  EARLY_GAME_PETALBURG_GYM_POKEMON_EVENT_ID = 13
  EARLY_GAME_PETALBURG_GYM_POKEMON_EVENT_NAME =
    "IRONMON_WALLY_WILD_POKEMON"
  EARLY_GAME_PETALBURG_GYM_POKEMON_X = 9
  EARLY_GAME_PETALBURG_GYM_POKEMON_Y = 13
  EARLY_GAME_SWITCH_STARTER_RECEIVED = 2012
  EARLY_GAME_SWITCH_RIVAL_DEFEATED = 2014
  EARLY_GAME_SWITCH_LAB_RETURN = 2016
  EARLY_GAME_SWITCH_OPENING_COMPLETE = 2017
  EARLY_GAME_SWITCH_FIRST_RIVAL_WON = 2089
  EARLY_GAME_SWITCH_WALLY_SECOND_CATCH = 2025
  EARLY_GAME_SWITCH_WALLY_RETURNING_TO_GYM = 2078
  EARLY_GAME_SWITCH_ITEM_TUTORIAL = 27
  EARLY_GAME_VARIABLE_QUESTS_ACCEPTED = 96
  EARLY_GAME_WALLY_RETURN_PLAYER_X = 10
  EARLY_GAME_WALLY_RETURN_PLAYER_Y = 10
  EARLY_GAME_WALLY_RETURN_PLAYER_DIRECTION = 8
  EARLY_GAME_RIVAL_CHARACTER_FOLDER = "Graphics/Characters/Ironmon"
  EARLY_GAME_RIVAL_CHARACTER_PREFIX = "Ironmon/early_game_rival"

  def self.early_game_mart_stock(stock)
    return stock if !active? || Settings::GAME_ID != :IF_HOENN
    return stock if !$game_map || $game_map.map_id != POKEMART_MAP_ID
    return stock if !stock.is_a?(Array)
    city = get_city_numerical_id_hoenn(pbGet(VAR_CURRENT_CITY))
    return stock if !city || city < get_city_numerical_id_hoenn(:PETALBURG)
    item_ids = stock.map { |item| GameData::Item.try_get(item)&.id }
    return stock if !item_ids.include?(:POKEBALL) ||
                    !item_ids.include?(:POTION) || item_ids.include?(:REPEL)
    return stock + [:REPEL]
  end

  def self.early_game_script_event_list(script)
    return [
      RPG::EventCommand.new(355, 0, [script]),
      RPG::EventCommand.new(0, 0, [])
    ]
  end

  def self.patch_early_game_map(map_id, map)
    return false if !map || !map.events
    case map_id
    when EARLY_GAME_ROUTE_101_MAP_ID
      return patch_early_game_route_101(map)
    when EARLY_GAME_LAB_MAP_ID
      return patch_early_game_lab(map)
    when EARLY_GAME_PETALBURG_GYM_MAP_ID
      return patch_early_game_petalburg_gym(map)
    end
    return false
  end

  def self.patch_early_game_route_101(map)
    event = map.events[EARLY_GAME_ROUTE_101_RIVAL_EVENT_ID]
    return false if !event || event.name != HOENN_RIVAL_EVENT_NAME ||
                    event.pages.empty?
    page = event.pages[0]
    closing_index = page.list.index do |command|
      command.code == 101 && command.parameters[0] ==
        "Oh well, let's head back to the lab!"
    end
    return false if !closing_index
    movement_command = page.list[(closing_index + 1)..-1].find do |command|
      command.code == 209 && command.parameters[0] == 0
    end
    return false if !movement_command || !movement_command.parameters[1]
    route = movement_command.parameters[1]
    route.repeat = false
    route.skippable = true
    route.list = [
      RPG::MoveCommand.new(PBMoveRoute::ThroughOn),
      RPG::MoveCommand.new(PBMoveRoute::Up),
      RPG::MoveCommand.new(PBMoveRoute::Up),
      RPG::MoveCommand.new(PBMoveRoute::Up),
      RPG::MoveCommand.new(PBMoveRoute::Up),
      RPG::MoveCommand.new(PBMoveRoute::Up),
      RPG::MoveCommand.new(PBMoveRoute::Up),
      RPG::MoveCommand.new(PBMoveRoute::Up),
      RPG::MoveCommand.new(PBMoveRoute::Opacity, [0]),
      RPG::MoveCommand.new(PBMoveRoute::End)
    ]
    page.direction_fix = false
    page.list[closing_index].parameters[0] =
      "Oh well, let's head to Oldale Town!"
    return true
  end

  def self.patch_early_game_lab(map)
    event = map.events[EARLY_GAME_LAB_PROFESSOR_EVENT_ID]
    rival_event = map.events[EARLY_GAME_LAB_RIVAL_EVENT_ID]
    return false if !event || event.name != "birch" || event.pages.empty? ||
                    !rival_event || rival_event.name != HOENN_RIVAL_EVENT_NAME ||
                    rival_event.pages.empty?
    page = event.pages[0]
    expected = page.list.any? do |command|
      command.code == 655 && command.parameters[0] ==
        "pbAddPokemon(starter,5,true,true)"
    end
    rival_page = rival_event.pages[0]
    return false if !expected || !rival_page.condition.switch1_valid ||
                    rival_page.condition.switch1_id !=
                      EARLY_GAME_SWITCH_RIVAL_DEFEATED ||
                    rival_page.graphic.character_name !=
                      TEMPLATE_CHARACTER_FILE
    page.list = early_game_script_event_list(
      "Ironmon.run_first_rival_lab_sequence"
    )
    rival_page.condition.switch1_valid = false
    rival_page.condition.switch2_valid = false
    rival_page.condition.variable_valid = false
    rival_page.condition.self_switch_valid = false
    rival_page.graphic.character_name = ""
    rival_page.through = true
    rival_page.direction_fix = false
    rival_page.list = [RPG::EventCommand.new(0, 0, [])]
    return true
  end

  def self.patch_early_game_petalburg_gym(map)
    event = map.events[EARLY_GAME_PETALBURG_GYM_CONTROLLER_EVENT_ID]
    return false if !event || event.pages.empty?
    page = event.pages[0]
    commands = page.list
    wally_entrance = commands.index do |command|
      command.code == 108 && command.parameters[0] == "Wally enters"
    end
    post_entrance = commands.index do |command|
      command.code == 355 && command.parameters[0] ==
        "pbCallBubDown(2,5) #Norman" &&
        commands.index(command) > wally_entrance.to_i
    end
    expected = commands.any? do |command|
      command.code == 355 && command.parameters[0] == "wally_initialize()"
    end
    return false if !expected || !wally_entrance || !post_entrance ||
                    map.events[EARLY_GAME_PETALBURG_GYM_POKEMON_EVENT_ID]
    opening = commands[0, 9]
    entrance = commands[wally_entrance...post_entrance]
    page.list = opening + entrance + early_game_script_event_list(
      "Ironmon.run_wally_gym_sequence"
    )
    pokemon_event = RPG::Event.new(
      EARLY_GAME_PETALBURG_GYM_POKEMON_X,
      EARLY_GAME_PETALBURG_GYM_POKEMON_Y
    )
    pokemon_event.id = EARLY_GAME_PETALBURG_GYM_POKEMON_EVENT_ID
    pokemon_event.name = EARLY_GAME_PETALBURG_GYM_POKEMON_EVENT_NAME
    pokemon_page = pokemon_event.pages[0]
    pokemon_page.graphic.character_name = ""
    pokemon_page.through = true
    pokemon_page.list = [RPG::EventCommand.new(0, 0, [])]
    map.events[EARLY_GAME_PETALBURG_GYM_POKEMON_EVENT_ID] = pokemon_event
    patch_wally_return_dialogue(map)
    return true
  end

  def self.patch_wally_return_dialogue(map)
    event = map.events[9]
    return false if !event || event.pages.empty?
    page = event.pages[0]
    changed = false
    if !wally_tutorial_uses_fusion?
      gift_line = page.list.index do |command|
        [101, 401].include?(command.code) &&
          command.parameters[0] ==
            "And I can't forget about the \\V[1] you gave me too!"
      end
      farewell = page.list.index do |command|
        command.code == 101 && command.parameters[0] ==
          "I hope we'll meet again, \\PN. And you too, Mr. "
      end
      if gift_line && farewell
        first = gift_line > 0 && page.list[gift_line - 1].code == 355 ?
          gift_line - 1 : gift_line
        last = farewell > 0 && page.list[farewell - 1].code == 355 ?
          farewell - 1 : farewell
        page.list.slice!(first...last)
        changed = true
      end
    end
    replacements = {
      "went and caught another all on my own! " =>
        "caught it all on my own!",
      "that I was able to catch Ralts. " =>
        "that I was able to catch \\V[2]. "
    }
    if wally_tutorial_uses_fusion?
      replacements[
        "And I can't forget about the \\V[1] you gave me too!"
      ] = "And I can't forget the \\V[1] you gave me, Mr. Norman!"
    end
    page.list.each do |command|
      next if ![101, 401].include?(command.code)
      text = command.parameters[0]
      if text.include?("showed me how to catch")
        command.parameters[0] = "A wild Pokémon ran into the Gym, and I "
        changed = true
        next
      end
      next if !replacements.key?(text)
      command.parameters[0] = replacements[text]
      changed = true
    end
    return changed
  end

  def self.wait_for_early_game_movement(event)
    return if !event
    pbWait(1) while event.moving? || event.move_route_forcing
  end

  def self.normalized_early_game_rival_appearance(gender, current)
    if gender == GENDER_FEMALE
      hat = HAT_MAY
      clothes = CLOTHES_MAY
      default_hair = HAIR_MAY
    else
      hat = HAT_BRENDAN
      clothes = CLOTHES_BRENDAN
      default_hair = HAIR_BRENDAN
    end
    skin_color = current && current.skin_color ? current.skin_color : 1
    hair = current ? current.hair : nil
    hair = "3_#{default_hair}" if !pbResolveBitmap(
      getOverworldHairFilename(hair)
    )
    hair_color = current && current.hair_color ? current.hair_color : 0
    return TrainerAppearance.new(
      skin_color, hat, clothes, hair, hair_color, 0, 0
    )
  end

  def self.materialize_early_game_rival_character(gender, appearance)
    Dir.mkdir(EARLY_GAME_RIVAL_CHARACTER_FOLDER) if
      !Dir.exist?(EARLY_GAME_RIVAL_CHARACTER_FOLDER)
    components = [
      gender, appearance.skin_color, appearance.hair,
      appearance.hair_color
    ].map { |value| value.to_s.gsub(/[^A-Za-z0-9_-]/, "_") }
    character_name = "#{EARLY_GAME_RIVAL_CHARACTER_PREFIX}_#{components.join("_")}"
    bitmap = generateNPCClothedBitmapStatic(appearance)
    bitmap.save_to_png("Graphics/Characters/#{character_name}.png")
    bitmap.dispose
    return character_name
  end

  def self.arm_starter_rescue_battle
    @starter_rescue_battle_pending = true
  end

  def self.starter_rescue_battle_pending?
    return @starter_rescue_battle_pending == true
  end

  def self.finish_starter_rescue_battle
    pending = starter_rescue_battle_pending?
    @starter_rescue_battle_pending = false
    return pending
  end

  def self.abort_current_early_game_event
    interpreter = pbMapInterpreter
    return false if !interpreter || !interpreter.running?
    event = interpreter.get_self
    event.erase if event
    interpreter.command_end
    return true
  end

  def self.queue_early_game_conditional_event_abort
    @early_game_conditional_event_abort_queued = true
    return true
  end

  def self.consume_queued_early_game_conditional_event_abort
    queued = @early_game_conditional_event_abort_queued == true
    @early_game_conditional_event_abort_queued = false
    return queued
  end

  def self.queue_early_game_loss_reset
    @early_game_loss_reset_queued = true
    return true
  end

  def self.finish_queued_early_game_loss_reset
    return false if !@early_game_loss_reset_queued
    return false if pbMapInterpreterRunning?
    @early_game_loss_reset_queued = false
    return start_checkpoint_reset(true)
  end

  def self.run_first_rival_lab_sequence
    pbCallBub(2, EARLY_GAME_LAB_PROFESSOR_EVENT_ID)
    pbMessage(_INTL(
      "Thanks for rescuing me, \\PN. Keep the Pokémon you used."
    ))
    starter = pbGet(VAR_HOENN_STARTER)
    pbAddPokemon(starter, 5, true, true)

    rival = $game_map.events[EARLY_GAME_LAB_RIVAL_EVENT_ID]
    if rival
      rival_gender = isPlayerMale ? GENDER_FEMALE : GENDER_MALE
      rival_appearance = normalized_early_game_rival_appearance(
        rival_gender, $Trainer.rival_appearance
      )
      $Trainer.rival_appearance = rival_appearance
      rival_character = materialize_early_game_rival_character(
        rival_gender, rival_appearance
      )
      rival.moveto(8, 14)
      rival.opacity = 0
      rival.direction_fix = false
      rival.character_name = rival_character
      rival.direction = 8
      pbWait(1)
      pbSEPlay("Entering Door")
      pbMoveRoute(rival, [
        PBMoveRoute::Opacity, 255,
        PBMoveRoute::ChangeSpeed, 4,
        PBMoveRoute::Up, PBMoveRoute::Up, PBMoveRoute::Up,
        PBMoveRoute::Up, PBMoveRoute::Up, PBMoveRoute::Up
      ])
      wait_for_early_game_movement(rival)
      rival.turn_toward_player
      pbWait(6)
    end

    pbCallBub(2, EARLY_GAME_LAB_RIVAL_EVENT_ID)
    pbMessage(_INTL(
      "\\PN! I heard you got your first Pokémon. Let's see what it can do!"
    ))
    $PokemonGlobal.nextBattleBGM = "battle_rival"
    won = hoennRivalBattle(
      _INTL("Wow! That's great! You're pretty good!"), true
    )
    return true if !won || failed_run_locked?

    pbCallBub(2, EARLY_GAME_LAB_RIVAL_EVENT_ID)
    pbMessage(_INTL(
      "That was a great battle, \\PN! You've gotten strong already."
    ))

    $game_switches[EARLY_GAME_SWITCH_FIRST_RIVAL_WON] = true
    announce_tracker_active_run_preparation_ready
    $Trainer.has_pokedex = true
    pbUnlockDex
    pbCallBub(2, EARLY_GAME_LAB_PROFESSOR_EVENT_ID)
    pbMessage(_INTL(
      "Excellent! Take this Pokédex and start your journey."
    ))
    pbReceiveItem(:POKEBALL, 5)

    if rival
      pbCallBub(2, EARLY_GAME_LAB_RIVAL_EVENT_ID)
      pbMessage(_INTL("I'm heading out too. See you on the road!"))
      rival.turn_down
      pbWait(6)
      pbMoveRoute(rival, [
        PBMoveRoute::ChangeSpeed, 5,
        PBMoveRoute::Down, PBMoveRoute::Down, PBMoveRoute::Down,
        PBMoveRoute::Down, PBMoveRoute::Down, PBMoveRoute::Down,
        PBMoveRoute::Opacity, 0
      ])
      wait_for_early_game_movement(rival)
      pbSEPlay("Exit Door")
    end

    $game_switches[EARLY_GAME_SWITCH_STARTER_RECEIVED] = true
    $game_switches[EARLY_GAME_SWITCH_RIVAL_DEFEATED] = true
    $game_switches[EARLY_GAME_SWITCH_LAB_RETURN] = true
    $game_switches[EARLY_GAME_SWITCH_OPENING_COMPLETE] = true
    $game_switches[EARLY_GAME_SWITCH_ITEM_TUTORIAL] = false
    rival.character_name = "" if rival
    $game_map.need_refresh = true
    Audio.me_stop
    return true
  end

  def self.generate_wally_story_pokemon(context, level, excluded_species = [])
    pool = normal_species_pool
    start = progression_random_value(context, 0) % pool.length
    pool.length.times do |offset|
      species = pool[(start + offset) % pool.length]
      next if excluded_species.include?(species)
      return build_wally_story_pokemon(species, level)
    end
    raise SpeciesGenerationError,
          "no normal Pokemon is available for Wally's story team"
  end

  def self.wally_tutorial_uses_fusion?
    return configuration.trainer_policy != Configuration::POLICY_NORMAL_ONLY
  end

  def self.wally_tutorial_fusion_plan
    pool = normal_species_pool
    if !pool || pool.length < 2
      raise SpeciesGenerationError,
            "Wally's tutorial needs two normal material species"
    end
    mapper = player_fusion_mapper
    body_index = progression_random_value(
      :wally_tutorial_fusion_body, 0
    ) % pool.length
    head_index = progression_random_value(
      :wally_tutorial_fusion_head, 0
    ) % (pool.length - 1)
    head_index += 1 if head_index >= body_index
    body = GameData::Species.get(pool[body_index])
    head = GameData::Species.get(pool[head_index])
    fusion = GameData::Species.get(mapper.species(body.id, head.id))
    if !custom_fusion_species?(fusion.id)
      raise SpeciesGenerationError,
            "Wally's Ironmon materials did not produce a custom fusion"
    end
    return {
      :fusion => fusion.id,
      :body => body.id,
      :head => head.id
    }
  rescue PlayerFusionMappingError => e
    raise SpeciesGenerationError,
          "Wally's Ironmon fusion materials are unavailable: #{e.message}"
  end

  def self.build_wally_story_pokemon(species, level)
    pokemon = Pokemon.new(species, level)
    pokemon.obtain_method = 0
    pokemon.record_first_moves
    return pokemon
  end

  def self.wally_tutorial_catch_pokemon(fusion_plan = nil)
    if wally_tutorial_uses_fusion?
      fusion_plan ||= wally_tutorial_fusion_plan
      return build_wally_story_pokemon(fusion_plan[:body], 10)
    end
    pokemon = generate_wally_story_pokemon(:wally_quick_catch, 10)
    return mark_persistent_trainer_species(pokemon)
  end

  def self.build_wally_tutorial_fusion(body_pokemon, head_pokemon,
                                       fusion_plan = nil)
    fusion_plan ||= wally_tutorial_fusion_plan
    level = (body_pokemon.level + head_pokemon.level) / 2
    pokemon = Pokemon.new(fusion_plan[:fusion], level)
    return mark_persistent_trainer_species(pokemon)
  end

  def self.accept_early_game_quest(id, show_description = false)
    return false if isQuestAlreadyAccepted?(id)
    quest = QUESTS[id]
    return false if !quest
    $game_variables[EARLY_GAME_VARIABLE_QUESTS_ACCEPTED] =
      $game_variables[EARLY_GAME_VARIABLE_QUESTS_ACCEPTED].to_i + 1
    $game_variables[VAR_NB_QUEST_ACTIVE] =
      $game_variables[VAR_NB_QUEST_ACTIVE].to_i + 1
    if quest.type == :MAIN_QUEST
      showNewMainQuestMessage(quest.name, quest.desc, show_description)
    else
      showNewSideQuestMessage(quest.name, quest.desc, show_description)
    end
    pbAddQuest(id)
    return true
  end

  def self.run_wally_gym_sequence
    wally_initialize
    fusion_plan = nil
    if wally_tutorial_uses_fusion?
      fusion_plan = wally_tutorial_fusion_plan
      pokemon = build_wally_story_pokemon(fusion_plan[:head], 5)
      wally_add_pokemon_directly(pokemon)
      pbSet(1, pokemon.name)

      pbCallBubDown(2, 5)
      pbMessage(_INTL(
        "Wally, take this {1}. It needs a trainer as much as you need a partner.",
        pokemon.name
      ))
      pbCallBubUp(2, EARLY_GAME_PETALBURG_GYM_WALLY_EVENT_ID)
      pbMessage(_INTL("Th-thank you, Mr. Norman!"))
    end

    finishQuest("main_dad", true)
    accept_early_game_quest("main_wally")
    $game_switches[SWITCH_WALLY_CATCHING_POKEMON] = true
    return run_wally_quick_catch_sequence(fusion_plan)
  end

  def self.run_wally_gym_gift_sequence
    return run_wally_gym_sequence
  end

  def self.show_wally_quick_catch_pokemon(pokemon)
    event = $game_map.events[
      EARLY_GAME_PETALBURG_GYM_POKEMON_EVENT_ID
    ]
    return if !event
    event.moveto(
      EARLY_GAME_PETALBURG_GYM_POKEMON_X,
      EARLY_GAME_PETALBURG_GYM_POKEMON_Y
    )
    event.opacity = 0
    event.character_name = ""
    setEventGraphicsToPokemon(
      pokemon.species, EARLY_GAME_PETALBURG_GYM_POKEMON_EVENT_ID,
      pokemon.shiny?
    )
    pbSEPlay("Entering Door")
    pbMoveRoute(event, [
      PBMoveRoute::Opacity, 255,
      PBMoveRoute::ChangeSpeed, 4,
      PBMoveRoute::Up, PBMoveRoute::Up
    ])
    wait_for_early_game_movement(event)
  end

  def self.run_wally_quick_catch_sequence(fusion_plan = nil)
    pokemon = wally_tutorial_catch_pokemon(fusion_plan)
    pbSet(2, pokemon.name)
    show_wally_quick_catch_pokemon(pokemon)
    playCry(pokemon.species)
    pbMessage(_INTL("A wild {1} ran right up to Wally!", pokemon.name))
    pbSEPlay("throw")
    pbWait(12)
    pbSEPlay("Battle catch click")
    pbMEPlay("caught_pokemon")
    wally_add_pokemon_directly(pokemon)
    pbMessage(_INTL("Wally caught {1}!", pokemon.name))

    event = $game_map.events[
      EARLY_GAME_PETALBURG_GYM_POKEMON_EVENT_ID
    ]
    if event
      pbMoveRoute(event, [PBMoveRoute::Opacity, 0])
      wait_for_early_game_movement(event)
      event.character_name = ""
    end
    $game_player.moveto(
      EARLY_GAME_WALLY_RETURN_PLAYER_X,
      EARLY_GAME_WALLY_RETURN_PLAYER_Y
    )
    $game_player.direction = EARLY_GAME_WALLY_RETURN_PLAYER_DIRECTION
    $PokemonTemp.prevent_ow_encounters = false
    $game_switches[SWITCH_WALLY_CATCHING_POKEMON] = false
    $game_switches[SWITCH_WALLY_GAVE_POKEMON] = true
    $game_switches[EARLY_GAME_SWITCH_WALLY_SECOND_CATCH] = true
    $game_switches[EARLY_GAME_SWITCH_WALLY_RETURNING_TO_GYM] = true
    $game_map.need_refresh = true
    return true
  end

  def self.reset_after_early_game_loss(won, early_battle)
    return false if won || !early_battle
    complete_run(:lost) if !failed_run_locked?
    return false if !failed_run_locked?
    return queue_early_game_loss_reset
  end
end

alias ironmon_early_game_original_pb_pokemon_mart pbPokemonMart
def pbPokemonMart(stock, speech_welcome = nil, cantsell = false,
                  speech_bye = nil, speech_what_else = nil)
  stock = Ironmon.early_game_mart_stock(stock) if !cantsell
  return ironmon_early_game_original_pb_pokemon_mart(
    stock, speech_welcome, cantsell, speech_bye, speech_what_else
  )
end

Ironmon.register_graphics_update_hook(
  :early_game_loss_reset,
  proc { Ironmon.finish_queued_early_game_loss_reset }
)

Events.onMapCreate += proc do |_sender, event|
  map_id = event[0]
  map = event[1]
  Ironmon.patch_early_game_map(map_id, map) if Ironmon.active?
end
