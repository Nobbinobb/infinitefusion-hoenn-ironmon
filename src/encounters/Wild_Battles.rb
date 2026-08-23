#===============================================================================
# Ironmon scripted wild battle integration
#===============================================================================

alias ironmon_original_pb_wild_battle pbWildBattle
def pbWildBattle(species, level, outcomeVar = 1, canRun = true, canLose = false)
  if Ironmon.active?
    context = Ironmon.wild_script_context(:single, 0)
    species = Ironmon.wild_battle_species_for(species, context)
    random_static = $game_switches[SWITCH_RANDOM_STATIC_ENCOUNTERS]
    begin
      $game_switches[SWITCH_RANDOM_STATIC_ENCOUNTERS] = false
      result = ironmon_original_pb_wild_battle(
        species, level, outcomeVar, canRun, canLose
      )
      Ironmon.synchronize_hoenn_starter_after_battle
      return result
    ensure
      $game_switches[SWITCH_RANDOM_STATIC_ENCOUNTERS] = random_static
    end
  end
  return ironmon_original_pb_wild_battle(species, level, outcomeVar, canRun, canLose)
end

alias ironmon_original_pb_wild_battle_specific pbWildBattleSpecific
def pbWildBattleSpecific(pokemon, outcomeVar = 1, canRun = true,
                         canLose = false)
  if Ironmon.active?
    Ironmon.prepare_wild_pokemon(
      pokemon, Ironmon.wild_script_context(:specific, 0)
    )
  end
  return ironmon_original_pb_wild_battle_specific(
    pokemon, outcomeVar, canRun, canLose
  )
end

alias ironmon_original_pb_wild_double_battle_specific pbWildDoubleBattleSpecific
def pbWildDoubleBattleSpecific(pokemon1, pokemon2, outcomeVar = 1,
                               canRun = true, canLose = false)
  if Ironmon.active?
    Ironmon.prepare_wild_pokemon(
      pokemon1, Ironmon.wild_script_context(:specific_double, 0)
    )
    Ironmon.prepare_wild_pokemon(
      pokemon2, Ironmon.wild_script_context(:specific_double, 1)
    )
  end
  return ironmon_original_pb_wild_double_battle_specific(
    pokemon1, pokemon2, outcomeVar, canRun, canLose
  )
end

alias ironmon_original_pb_1v2_wild_battle_specific pb1v2WildBattleSpecific
def pb1v2WildBattleSpecific(pokemon1, pokemon2, outcomeVar = 1,
                            canRun = true, canLose = false)
  if Ironmon.active?
    Ironmon.prepare_wild_pokemon(
      pokemon1, Ironmon.wild_script_context(:specific, 0)
    )
    Ironmon.prepare_wild_pokemon(
      pokemon2, Ironmon.wild_script_context(:specific, 1)
    )
  end
  return ironmon_original_pb_1v2_wild_battle_specific(
    pokemon1, pokemon2, outcomeVar, canRun, canLose
  )
end

alias ironmon_original_pb_1v3_wild_battle_specific pb1v3WildBattleSpecific
def pb1v3WildBattleSpecific(pokemon1, pokemon2, pokemon3, outcomeVar = 1,
                            canRun = true, canLose = false)
  if Ironmon.active?
    Ironmon.prepare_wild_pokemon(
      pokemon1, Ironmon.wild_script_context(:specific, 0)
    )
    Ironmon.prepare_wild_pokemon(
      pokemon2, Ironmon.wild_script_context(:specific, 1)
    )
    Ironmon.prepare_wild_pokemon(
      pokemon3, Ironmon.wild_script_context(:specific, 2)
    )
  end
  return ironmon_original_pb_1v3_wild_battle_specific(
    pokemon1, pokemon2, pokemon3, outcomeVar, canRun, canLose
  )
end

alias ironmon_original_pb_double_wild_battle pbDoubleWildBattle
def pbDoubleWildBattle(species1, level1, species2, level2,
                       outcomeVar = 1, canRun = true, canLose = false)
  if Ironmon.active?
    species1 = Ironmon.wild_battle_species_for(
      species1, Ironmon.wild_script_context(:double, 0)
    )
    species2 = Ironmon.wild_battle_species_for(
      species2, Ironmon.wild_script_context(:double, 1)
    )
  end
  return ironmon_original_pb_double_wild_battle(
    species1, level1, species2, level2, outcomeVar, canRun, canLose
  )
end

alias ironmon_original_pb_triple_wild_battle pbTripleWildBattle
def pbTripleWildBattle(species1, level1, species2, level2, species3, level3,
                       outcomeVar = 1, canRun = true, canLose = false)
  if Ironmon.active?
    species1 = Ironmon.wild_battle_species_for(
      species1, Ironmon.wild_script_context(:triple, 0)
    )
    species2 = Ironmon.wild_battle_species_for(
      species2, Ironmon.wild_script_context(:triple, 1)
    )
    species3 = Ironmon.wild_battle_species_for(
      species3, Ironmon.wild_script_context(:triple, 2)
    )
  end
  return ironmon_original_pb_triple_wild_battle(
    species1, level1, species2, level2, species3, level3,
    outcomeVar, canRun, canLose
  )
end

alias ironmon_original_pb_1v2_wild_battle pb1v2WildBattle
def pb1v2WildBattle(species1, level1, species2, level2,
                    outcomeVar = 1, canRun = true, canLose = false)
  if Ironmon.active?
    species1 = Ironmon.wild_species_for(
      species1, Ironmon.wild_script_context(:one_v_two, 0)
    )
    species2 = Ironmon.wild_species_for(
      species2, Ironmon.wild_script_context(:one_v_two, 1)
    )
  end
  return ironmon_original_pb_1v2_wild_battle(
    species1, level1, species2, level2, outcomeVar, canRun, canLose
  )
end

alias ironmon_original_pb_1v3_wild_battle pb1v3WildBattle
def pb1v3WildBattle(species1, level1, species2, level2, species3, level3,
                    outcomeVar = 1, canRun = true, canLose = false)
  if Ironmon.active?
    species1 = Ironmon.wild_species_for(
      species1, Ironmon.wild_script_context(:one_v_three, 0)
    )
    species2 = Ironmon.wild_species_for(
      species2, Ironmon.wild_script_context(:one_v_three, 1)
    )
    species3 = Ironmon.wild_species_for(
      species3, Ironmon.wild_script_context(:one_v_three, 2)
    )
  end
  return ironmon_original_pb_1v3_wild_battle(
    species1, level1, species2, level2, species3, level3,
    outcomeVar, canRun, canLose
  )
end
