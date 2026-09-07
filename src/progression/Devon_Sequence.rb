#===============================================================================
# Ironmon shortened Devon Parts rescue and outdoor delivery handoff
#===============================================================================

module Ironmon
  DEVON_RUSTURF_MAP_ID = 28
  DEVON_RUSTBORO_MAP_ID = 47
  DEVON_INTERIORS_MAP_ID = 48
  DEVON_SWITCH_PEEKO_RESCUED = 2076
  DEVON_SWITCH_OFFICE_PENDING = 2096
  DEVON_SWITCH_DELIVERIES_STARTED = 2097
  DEVON_STOLEN_PARTS_QUEST = "main_stolen_parts"
  DEVON_DELIVERY_QUESTS = ["main_devon_parts", "main_steven_letter"].freeze

  def self.devon_sequence_active?
    return active? && Settings::GAME_ID == :IF_HOENN
  end

  def self.devon_script_page(map, event_id, page_index, expected_script)
    page = map.events[event_id]&.pages&.[](page_index)
    return nil if !page || !page.list.any? do |command|
      command.code == 355 && command.parameters[0] == expected_script
    end
    return page
  end

  def self.patch_devon_sequence_map(map_id, map)
    return false if !devon_sequence_active? || !map || !map.events
    case map_id
    when DEVON_RUSTURF_MAP_ID
      return patch_devon_rescue(map)
    when DEVON_RUSTBORO_MAP_ID
      return patch_devon_rustboro(map)
    when DEVON_INTERIORS_MAP_ID
      return patch_devon_office(map)
    end
    return false
  end

  def self.patch_devon_rescue(map)
    event = map.events[7]
    rescue_page = devon_script_page(map, 7, 1, "pbReceiveItem(:DEVONPARTS)")
    return false if !event || !rescue_page || !map.events[8] || !map.events[16]
    battle_page = event.pages[0]
    battle_index = battle_page.list.index do |command|
      command.code == 111 && command.parameters[0] == 12 &&
        command.parameters[1].to_s.start_with?(
          'pbTrainerBattle(:TEAM_MAGMA_GRUNT_M,"Walter",'
        )
    end
    return false if !battle_index || !rescue_page.condition.self_switch_valid ||
                    rescue_page.condition.self_switch_ch != "A"
    opening = battle_page.list[0...battle_index].reject do |command|
      [101, 401].include?(command.code) ||
        (command.code == 355 && command.parameters[0].to_s.start_with?("pbCallBub"))
    end
    battle_page.list = [
      RPG::EventCommand.new(101, 0, ["You want the parts and Peeko? You'll have to beat me!"])
    ] + opening + battle_page.list[battle_index..-1]
    rescue_page.list = early_game_script_event_list("Ironmon.run_devon_rescue")
    return true
  end

  def self.patch_devon_rustboro(map)
    request = devon_script_page(map, 67, 0, 'pbQuest("main_stolen_parts")')
    approach = devon_script_page(map, 69, 0, 'pbQuest("main_stolen_parts")')
    handoff = devon_script_page(map, 72, 0, 'finishQuest("main_stolen_parts",true)')
    stone = map.events[63]&.pages&.[](2)
    return false if !request || !approach || !handoff || !stone ||
                    !stone.condition.switch1_valid ||
                    stone.condition.switch1_id != DEVON_SWITCH_PEEKO_RESCUED ||
                    !handoff.condition.switch1_valid ||
                    handoff.condition.switch1_id != DEVON_SWITCH_OFFICE_PENDING
    request.list = early_game_script_event_list("Ironmon.run_devon_request")
    approach.list = early_game_script_event_list("Ironmon.run_devon_request")
    handoff.list = early_game_script_event_list("Ironmon.run_devon_handoff")
    stone.list = early_game_script_event_list("Ironmon.run_devon_handoff")
    return true
  end

  def self.patch_devon_office(map)
    page = devon_script_page(map, 79, 0, "pbReceiveItem(:LETTER)")
    return false if !page || !page.condition.switch1_valid ||
                    page.condition.switch1_id != DEVON_SWITCH_OFFICE_PENDING
    page.list = [
      RPG::EventCommand.new(111, 0, [12, "Ironmon.run_devon_handoff"]),
      RPG::EventCommand.new(201, 1, [0, DEVON_RUSTBORO_MAP_ID, 23, 20, 2, 0]),
      RPG::EventCommand.new(0, 1, []),
      RPG::EventCommand.new(412, 0, []),
      RPG::EventCommand.new(0, 0, [])
    ]
    return true
  end

  def self.run_devon_request
    return false if !devon_sequence_active? || failed_run_locked? ||
                    $game_map.map_id != DEVON_RUSTBORO_MAP_ID ||
                    $game_switches[DEVON_SWITCH_PEEKO_RESCUED]
    turnPlayerTowardsEvent(67)
    pbCallBub(2, 67)
    pbMessage(_INTL("Team Magma stole my Devon Parts! Please recover them from Rusturf Tunnel, east of Route 116."))
    accept_early_game_quest(DEVON_STOLEN_PARTS_QUEST, false, false)
    $game_self_switches[[DEVON_RUSTBORO_MAP_ID, 69, "A"]] = true
    $game_map.need_refresh = true
    return true
  end

  def self.run_devon_rescue
    return false if !devon_sequence_active? || failed_run_locked? ||
                    $game_map.map_id != DEVON_RUSTURF_MAP_ID ||
                    !$game_self_switches[[DEVON_RUSTURF_MAP_ID, 7, "A"]] ||
                    $game_switches[DEVON_SWITCH_PEEKO_RESCUED]
    return false if !hasItem?(:DEVONPARTS) && !pbReceiveItem(:DEVONPARTS)
    pbFadeOutIn do
      briney = $game_map.events[7]
      briney.character_name = "NPC_Hoenn_Briney"
      briney.turn_toward_player
      peeko = $game_map.events[16]
      peeko.character_name = "fusion_wingull_poochyena"
      $game_self_switches[[DEVON_RUSTURF_MAP_ID, 8, "A"]] = true
      $game_map.refresh
    end
    pbCallBub(2, 7)
    pbMessage(_INTL("Mr. Briney: Thank you for saving Peeko! If you need a ride, visit my cottage on Route 104."))
    pbFadeOutIn do
      $game_self_switches[[DEVON_RUSTURF_MAP_ID, 16, "A"]] = true
      $game_switches[DEVON_SWITCH_PEEKO_RESCUED] = true
      $game_map.refresh
    end
    return true
  end

  def self.run_devon_handoff
    return false if !devon_sequence_active? || failed_run_locked? ||
                    ![DEVON_RUSTBORO_MAP_ID, DEVON_INTERIORS_MAP_ID].include?($game_map.map_id) ||
                    !$game_switches[DEVON_SWITCH_PEEKO_RESCUED] ||
                    $game_switches[DEVON_SWITCH_DELIVERIES_STARTED]
    return false if !hasItem?(:DEVONPARTS)
    stone_id = $game_map.map_id == DEVON_RUSTBORO_MAP_ID ? 63 : 80
    turnPlayerTowardsEvent(stone_id)
    pbCallBub(2, stone_id)
    pbMessage(_INTL("Mr. Stone: Thank you! Here's Exp. All. Briney can sail you to Dewford from Route 104."))
    return false if !hasItem?(:EXPALL) && !pbReceiveItem(:EXPALL)
    return false if !hasItem?(:LETTER) && !pbReceiveItem(:LETTER)
    pbCallBub(2, stone_id)
    pbMessage(_INTL("Take the letter to Steven in Granite Cave and the parts to Stern in Slateport."))
    accept_early_game_quest(DEVON_STOLEN_PARTS_QUEST, false, false)
    finishQuest(DEVON_STOLEN_PARTS_QUEST, true)
    DEVON_DELIVERY_QUESTS.each do |id|
      accept_early_game_quest(id, false, false)
    end
    $game_switches[DEVON_SWITCH_DELIVERIES_STARTED] = true
    $game_switches[DEVON_SWITCH_OFFICE_PENDING] = false
    $game_self_switches[[DEVON_RUSTBORO_MAP_ID, 69, "A"]] = true
    $game_map.need_refresh = true
    return true
  end
end

Events.onMapCreate += proc do |_sender, event|
  Ironmon.patch_devon_sequence_map(event[0], event[1])
end
