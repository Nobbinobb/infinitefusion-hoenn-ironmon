#===============================================================================
# Ironmon hooks for encounters that bypass the standard randomizer
#===============================================================================

alias ironmon_original_hoenn_select_starter hoennSelectStarter
def hoennSelectStarter
  Ironmon.capture_checkpoint
  return ironmon_original_hoenn_select_starter
end

class StaticOverworldPokemonEvent
  alias ironmon_original_setup_pokemon setup_pokemon
  def setup_pokemon(species, level, terrain = :Land, behavior_roaming = nil, behavior_noticed = nil)
    if !instance_variable_get(:@ironmon_randomized_species) && Ironmon.active?
      species = Ironmon.randomized_species_for(species)
      instance_variable_set(:@ironmon_randomized_species, true)
    end
    ironmon_original_setup_pokemon(species, level, terrain, behavior_roaming, behavior_noticed)
  end
end

module GameData
  class Trainer
    alias ironmon_original_replace_species_with_placeholder replace_species_with_placeholder
    def replace_species_with_placeholder(species)
      resolved_species = ironmon_original_replace_species_with_placeholder(species)
      return Ironmon.randomized_species_for(resolved_species)
    end
  end
end

alias ironmon_original_pb_wild_battle pbWildBattle
def pbWildBattle(species, level, outcomeVar = 1, canRun = true, canLose = false)
  species_data = GameData::Species.try_get(species)
  if Ironmon.active? && species_data && species_data.id_number > NB_POKEMON
    species = Ironmon.randomized_species_for(species)
  end
  return ironmon_original_pb_wild_battle(species, level, outcomeVar, canRun, canLose)
end
