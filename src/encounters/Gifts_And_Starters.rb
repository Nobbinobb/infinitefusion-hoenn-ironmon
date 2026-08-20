#===============================================================================
# Ironmon gift Pokemon and starter integration
#===============================================================================

alias ironmon_original_get_randomized_to getRandomizedTo
def getRandomizedTo(species)
  if Ironmon.active?
    mapped = Ironmon.wild_species_for(
      species, Ironmon.wild_script_context(:randomized_lookup)
    )
    mapped_data = GameData::Species.try_get(mapped)
    return mapped_data ? mapped_data.id_number : species
  end
  return ironmon_original_get_randomized_to(species)
end

alias ironmon_original_try_randomize_gift_pokemon tryRandomizeGiftPokemon
def tryRandomizeGiftPokemon(pokemon, dontRandomize = false)
  if Ironmon.active? && !dontRandomize &&
     !$game_switches[SWITCH_DONT_RANDOMIZE]
    pokemon.species = Ironmon.wild_species_for(
      pokemon.species, Ironmon.wild_script_context(:gift)
    )
    pokemon.reset_moves if Ironmon.move_access_randomization_active? &&
      !pokemon.shadowPokemon?
    return
  end
  return ironmon_original_try_randomize_gift_pokemon(pokemon, dontRandomize)
end

alias ironmon_original_check_porygon_encounter checkPorygonEncounter
def checkPorygonEncounter
  return if Ironmon.active?
  return ironmon_original_check_porygon_encounter
end

alias ironmon_original_receive_mystery_gift pbReceiveMysteryGift
def pbReceiveMysteryGift(id)
  if Ironmon.active? && $Trainer && $Trainer.mystery_gifts
    gift = $Trainer.mystery_gifts.find do |entry|
      entry[0] == id && entry.length > 1
    end
    if gift && gift[2].is_a?(Pokemon)
      pbMessage(_INTL("Pokemon Mystery Gifts are unavailable during an Ironmon run."))
      return false
    end
  end
  return ironmon_original_receive_mystery_gift(id)
end

alias ironmon_original_obtain_randomized_starter obtainRandomizedStarter
def obtainRandomizedStarter(starter_index)
  if Ironmon.active?
    source = [1, 4, 7][starter_index] || 7
    mapped = Ironmon.wild_species_for(source, [:starter, starter_index])
    return GameData::Species.get(mapped).id_number
  end
  return ironmon_original_obtain_randomized_starter(starter_index)
end

alias ironmon_original_set_rival_starter setRivalStarter
def setRivalStarter(starter_index1, starter_index2)
  if Ironmon.active?
    sources = [1, 4, 7]
    body = sources[starter_index1] || sources[0]
    head = sources[starter_index2] || sources[1]
    source = getFusionSpecies(body, head).id
    starter = GameData::Species.get(
      Ironmon.trainer_species_for(source, [:rival_starter, starter_index1,
                                           starter_index2])
    ).id_number
    pbSet(VAR_RIVAL_STARTER, starter)
    $game_switches[SWITCH_DEFINED_RIVAL_STARTER] = true
    return starter
  end
  return ironmon_original_set_rival_starter(starter_index1, starter_index2)
end

# Hoenn's persistent rival team later fuses its starter with other species.
# Keep a normal story source here so that operation never becomes a forbidden
# fusion of an already-fused species. The battle-boundary hook below applies
# the configured trainer policy to the result seen in battle.
alias ironmon_original_get_hoenn_rival_starter get_hoenn_rival_starter
def get_hoenn_rival_starter
  if Ironmon.active?
    case get_rival_starter_type
    when :GRASS then return GameData::Species.get(1)
    when :FIRE then return GameData::Species.get(4)
    when :WATER then return GameData::Species.get(7)
    end
    return GameData::Species.get(1)
  end
  return ironmon_original_get_hoenn_rival_starter
end
