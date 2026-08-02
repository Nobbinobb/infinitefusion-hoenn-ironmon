#===============================================================================
# Final pivot integration for story and party edge cases
#===============================================================================

module Ironmon
  def self.resolve_party_acquisition(pokemon, source, action_selector = nil)
    return false if !pokemon || !$Trainer || !$Trainer.party
    original_party = $Trainer.party.dup
    $Trainer.party.delete(pokemon)
    pokemon.ironmon_party_exclusion = nil
    result = intercept_acquisition(pokemon, source, action_selector)
    return result if result
    if !pivot_state.pending?
      pokemon.ironmon_party_exclusion = source
      $Trainer.party = original_party
    end
    return false
  end
end

alias ironmon_integration_original_hatch pbHatch
def pbHatch(pokemon)
  result = ironmon_integration_original_hatch(pokemon)
  if Ironmon.active?
    Ironmon.resolve_party_acquisition(pokemon, :egg_hatch)
  end
  return result
end

alias ironmon_integration_original_daycare_deposit pbDayCareDeposit
def pbDayCareDeposit(index)
  if Ironmon.active?
    pbMessage(_INTL("Ironmon does not allow Pokemon to be stored in Day Care."))
    return false
  end
  return ironmon_integration_original_daycare_deposit(index)
end

class << PokemonEvolutionScene
  alias ironmon_integration_original_duplicate_pokemon pbDuplicatePokemon
  def pbDuplicatePokemon(pokemon, new_species)
    return ironmon_integration_original_duplicate_pokemon(
      pokemon, new_species
    ) if !Ironmon.active?

    duplicate = pokemon.clone
    duplicate.species = new_species
    duplicate.name = nil
    duplicate.markings = 0
    duplicate.poke_ball = :POKEBALL
    duplicate.item = nil
    duplicate.clearAllRibbons
    duplicate.calc_stats
    duplicate.heal
    $Trainer.pokedex.register(duplicate)
    $Trainer.pokedex.set_owned(new_species)
    return Ironmon.intercept_acquisition(duplicate, :evolution_duplicate)
  end
end

alias ironmon_integration_original_first_able_pokemon pbFirstAblePokemon
def pbFirstAblePokemon(variable_id)
  return ironmon_integration_original_first_able_pokemon(variable_id) if
    !Ironmon.active?
  $Trainer.party.each_with_index do |pokemon, index|
    next if !pokemon || !pokemon.able? || Ironmon.utility_slave?(pokemon)
    pbSet(variable_id, index)
    return pokemon
  end
  pbSet(variable_id, -1)
  return nil
end

class AblePokemonRestriction
  alias ironmon_integration_original_valid isValid?
  def isValid?(pokemon)
    return false if Ironmon.active? && Ironmon.utility_slave?(pokemon)
    return ironmon_integration_original_valid(pokemon)
  end
end

class BugContestState
  alias ironmon_integration_original_set_pokemon pbSetPokemon
  def pbSetPokemon(chosen_pokemon)
    if Ironmon.active? &&
       Ironmon.utility_slave?($Trainer.party[chosen_pokemon])
      chosen_pokemon = $Trainer.party.index(Ironmon.usable_party[0])
    end
    return ironmon_integration_original_set_pokemon(chosen_pokemon)
  end
end
