#===============================================================================
# Ironmon area-progress engine integration
#===============================================================================

class PokemonEncounters
  alias ironmon_area_progress_original_setup setup
  def setup(map_id)
    result = ironmon_area_progress_original_setup(map_id)
    Ironmon.tag_area_encounter_tables(self, map_id)
    return result
  end

  alias ironmon_area_progress_original_choose_wild_pokemon choose_wild_pokemon
  def choose_wild_pokemon(enc_type, *arguments)
    return ironmon_area_progress_original_choose_wild_pokemon(
      enc_type, *arguments
    ) if !Ironmon.area_encounter_sequence_active?
    Ironmon.begin_area_encounter_selection
    result = ironmon_area_progress_original_choose_wild_pokemon(
      enc_type, *arguments
    )
    return Ironmon.finish_area_encounter_selection(result)
  rescue Exception
    Ironmon.clear_area_encounter_sequence
    raise
  end

  alias ironmon_area_progress_original_allow_encounter allow_encounter?
  def allow_encounter?(enc_data, repel_active = false)
    result = ironmon_area_progress_original_allow_encounter(
      enc_data, repel_active
    )
    Ironmon.clear_area_encounter_sequence if !result
    return result
  end
end

alias ironmon_area_progress_original_generate_wild_encounter generateWildEncounter
def generateWildEncounter(encounter_type)
  Ironmon.begin_area_encounter_sequence
  result = ironmon_area_progress_original_generate_wild_encounter(
    encounter_type
  )
  Ironmon.clear_area_encounter_sequence if !result
  return result
rescue Exception
  Ironmon.clear_area_encounter_sequence
  raise
end

alias ironmon_area_progress_original_pb_encounter pbEncounter
def pbEncounter(enc_type)
  Ironmon.begin_area_encounter_sequence
  return ironmon_area_progress_original_pb_encounter(enc_type)
ensure
  Ironmon.clear_area_encounter_sequence
end

alias ironmon_area_progress_original_spawn_random_overworld_pokemon_group spawn_random_overworld_pokemon_group
def spawn_random_overworld_pokemon_group(wild_pokemon = nil, radius = 10,
                                         max_group_size = 4, position = nil,
                                         terrain = nil)
  Ironmon.begin_area_encounter_sequence if !wild_pokemon
  result = ironmon_area_progress_original_spawn_random_overworld_pokemon_group(
    wild_pokemon, radius, max_group_size, position, terrain
  )
  Ironmon.clear_area_encounter_sequence if !result || result.empty?
  return result
rescue Exception
  Ironmon.clear_area_encounter_sequence
  raise
end

alias ironmon_area_progress_original_spawn_overworld_pokemon spawn_overworld_pokemon
def spawn_overworld_pokemon(wild_pokemon, position, terrain,
                            behavior_roaming = nil, behavior_noticed = nil)
  result = ironmon_area_progress_original_spawn_overworld_pokemon(
    wild_pokemon, position, terrain, behavior_roaming, behavior_noticed
  )
  Ironmon.commit_area_encounter_sequence if result
  return result
end

alias ironmon_area_progress_original_pb_wild_battle pbWildBattle
def pbWildBattle(species, level, outcomeVar = 1, canRun = true,
                 canLose = false)
  Ironmon.commit_area_encounter_sequence
  return ironmon_area_progress_original_pb_wild_battle(
    species, level, outcomeVar, canRun, canLose
  )
end

alias ironmon_area_progress_original_pb_double_wild_battle pbDoubleWildBattle
def pbDoubleWildBattle(species1, level1, species2, level2, outcomeVar = 1,
                       canRun = true, canLose = false)
  Ironmon.commit_area_encounter_sequence
  return ironmon_area_progress_original_pb_double_wild_battle(
    species1, level1, species2, level2, outcomeVar, canRun, canLose
  )
end

alias ironmon_area_progress_original_pb_triple_wild_battle pbTripleWildBattle
def pbTripleWildBattle(species1, level1, species2, level2, species3, level3,
                       outcomeVar = 1, canRun = true, canLose = false)
  Ironmon.commit_area_encounter_sequence
  return ironmon_area_progress_original_pb_triple_wild_battle(
    species1, level1, species2, level2, species3, level3, outcomeVar,
    canRun, canLose
  )
end

alias ironmon_area_progress_original_pb_trainer_battle pbTrainerBattle
def pbTrainerBattle(trainerID, trainerName, endSpeech = nil,
                    doubleBattle = false, trainerPartyID = 0,
                    canLose = false, outcomeVar = 1, name_override = nil,
                    trainer_type_overide = nil, event_id = nil, map_id = nil)
  entry_id = Ironmon.current_area_event_entry_id(
    "trainers", event_id, map_id
  )
  Ironmon.begin_area_trainer_event(entry_id)
  result = ironmon_area_progress_original_pb_trainer_battle(
    trainerID, trainerName, endSpeech, doubleBattle, trainerPartyID,
    canLose, outcomeVar, name_override, trainer_type_overide, event_id, map_id
  )
  waiting = $PokemonTemp && $PokemonTemp.waitingTrainer
  Ironmon.finish_area_trainer_event(result, waiting)
  return result
rescue Exception
  Ironmon.cancel_area_trainer_event
  raise
end

alias ironmon_area_progress_original_pb_double_trainer_battle pbDoubleTrainerBattle
def pbDoubleTrainerBattle(trainerID1, trainerName1, trainerPartyID1,
                          endSpeech1, trainerID2, trainerName2,
                          trainerPartyID2 = 0, endSpeech2 = nil,
                          canLose = false, outcomeVar = 1)
  entry_id = Ironmon.current_area_event_entry_id("trainers")
  Ironmon.begin_area_trainer_event(entry_id)
  result = ironmon_area_progress_original_pb_double_trainer_battle(
    trainerID1, trainerName1, trainerPartyID1, endSpeech1, trainerID2,
    trainerName2, trainerPartyID2, endSpeech2, canLose, outcomeVar
  )
  Ironmon.finish_area_trainer_event(result, false)
  return result
rescue Exception
  Ironmon.cancel_area_trainer_event
  raise
end

alias ironmon_area_progress_original_pb_triple_trainer_battle pbTripleTrainerBattle
def pbTripleTrainerBattle(trainerID1, trainerName1, trainerPartyID1,
                          endSpeech1, trainerID2, trainerName2,
                          trainerPartyID2, endSpeech2, trainerID3,
                          trainerName3, trainerPartyID3 = 0,
                          endSpeech3 = nil, canLose = false, outcomeVar = 1)
  entry_id = Ironmon.current_area_event_entry_id("trainers")
  Ironmon.begin_area_trainer_event(entry_id)
  result = ironmon_area_progress_original_pb_triple_trainer_battle(
    trainerID1, trainerName1, trainerPartyID1, endSpeech1, trainerID2,
    trainerName2, trainerPartyID2, endSpeech2, trainerID3, trainerName3,
    trainerPartyID3, endSpeech3, canLose, outcomeVar
  )
  Ironmon.finish_area_trainer_event(result, false)
  return result
rescue Exception
  Ironmon.cancel_area_trainer_event
  raise
end

alias ironmon_area_progress_original_pb_item_ball pbItemBall
def pbItemBall(item, quantity = 1, item_name = "", canRandom = true)
  entry_id = Ironmon.current_area_event_entry_id("items")
  Ironmon.begin_area_item_event(entry_id)
  result = ironmon_area_progress_original_pb_item_ball(
    item, quantity, item_name, canRandom
  )
  return Ironmon.finish_area_item_event(result, item)
rescue Exception
  Ironmon.cancel_area_item_event
  raise
end

alias ironmon_area_progress_original_pb_receive_item pbReceiveItem
def pbReceiveItem(item, quantity = 1, item_name = "", music = nil,
                  canRandom = true)
  entry_id = Ironmon.current_area_event_entry_id("items")
  Ironmon.begin_area_item_event(entry_id)
  result = ironmon_area_progress_original_pb_receive_item(
    item, quantity, item_name, music, canRandom
  )
  return Ironmon.finish_area_item_event(result, item)
rescue Exception
  Ironmon.cancel_area_item_event
  raise
end

class PokemonBag
  alias ironmon_area_progress_original_store_item pbStoreItem
  def pbStoreItem(item, qty = 1)
    result = ironmon_area_progress_original_store_item(item, qty)
    Ironmon.record_current_area_item(item) if result
    return result
  end
end
