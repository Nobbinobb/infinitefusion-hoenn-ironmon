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
