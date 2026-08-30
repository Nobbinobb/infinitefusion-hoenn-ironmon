#===============================================================================
# Ironmon acquisition and trade engine integration
#===============================================================================

alias ironmon_pivot_original_prompt_caught_pokemon_action promptCaughtPokemonAction
def promptCaughtPokemonAction(pokemon)
  return false if Ironmon.block_failed_run_action
  return ironmon_pivot_original_prompt_caught_pokemon_action(pokemon) if
    !Ironmon.active?
  if pokemon.egg? || Ironmon.current_acquisition_exclusion
    return ironmon_pivot_original_prompt_caught_pokemon_action(pokemon)
  end
  return Ironmon.intercept_acquisition(pokemon)
end

alias ironmon_pivot_original_pb_store_pokemon pbStorePokemon
def pbStorePokemon(pokemon)
  return false if Ironmon.block_failed_run_action
  return ironmon_pivot_original_pb_store_pokemon(pokemon) if !Ironmon.active?
  if pokemon.egg? || Ironmon.current_acquisition_exclusion
    reason = Ironmon.current_acquisition_exclusion || :egg
    Ironmon.record_excluded_acquisition(pokemon, reason)
    return ironmon_pivot_original_pb_store_pokemon(pokemon)
  end
  return Ironmon.intercept_acquisition(pokemon)
end

alias ironmon_pivot_original_pb_add_pokemon pbAddPokemon
def pbAddPokemon(pokemon, level = 1, see_form = true, dontRandomize = false,
                 variableToSave = nil)
  return false if Ironmon.block_failed_run_action
  return false if !Ironmon.confirm_gift_acquisition(pokemon)
  return Ironmon.with_acquisition_source(:gift_or_static) do
    ironmon_pivot_original_pb_add_pokemon(
      pokemon, level, see_form, dontRandomize, variableToSave
    )
  end
end

alias ironmon_pivot_original_pb_add_to_party pbAddToParty
def pbAddToParty(pokemon, level = 1, see_form = true, dontRandomize = false)
  return false if Ironmon.block_failed_run_action
  return false if !Ironmon.confirm_gift_acquisition(pokemon)
  return Ironmon.with_acquisition_source(:gift_or_static) do
    ironmon_pivot_original_pb_add_to_party(
      pokemon, level, see_form, dontRandomize
    )
  end
end

alias ironmon_pivot_original_pb_add_pokemon_silent pbAddPokemonSilent
def pbAddPokemonSilent(pokemon, level = 1, see_form = true)
  return false if Ironmon.block_failed_run_action
  return ironmon_pivot_original_pb_add_pokemon_silent(
    pokemon, level, see_form
  ) if !Ironmon.active?
  return false if !pokemon
  return false if !Ironmon.confirm_gift_acquisition(pokemon)
  pokemon = Pokemon.new(pokemon, level) if !pokemon.is_a?(Pokemon)
  Ironmon.mark_starter_pokemon(pokemon) if
    Ironmon.starter_acquisition?
  if Ironmon.current_acquisition_exclusion
    Ironmon.record_excluded_acquisition(
      pokemon, Ironmon.current_acquisition_exclusion, :gift_or_static
    )
    return ironmon_pivot_original_pb_add_pokemon_silent(
      pokemon, level, see_form
    )
  end
  $Trainer.pokedex.register(pokemon) if see_form
  $Trainer.pokedex.set_owned(pokemon.species)
  pokemon.record_first_moves
  source = Ironmon.starter_pokemon?(pokemon) ? :starter : :gift_or_static
  return Ironmon.intercept_acquisition(pokemon, source)
end

alias ironmon_pivot_original_pb_add_to_party_silent pbAddToPartySilent
def pbAddToPartySilent(pokemon, level = nil, see_form = true)
  return false if Ironmon.block_failed_run_action
  return ironmon_pivot_original_pb_add_to_party_silent(
    pokemon, level, see_form
  ) if !Ironmon.active?
  return false if !pokemon
  return false if !Ironmon.confirm_gift_acquisition(pokemon)
  pokemon = Pokemon.new(pokemon, level) if !pokemon.is_a?(Pokemon)
  if Ironmon.current_acquisition_exclusion
    Ironmon.record_excluded_acquisition(
      pokemon, Ironmon.current_acquisition_exclusion, :gift_or_static
    )
    return ironmon_pivot_original_pb_add_to_party_silent(
      pokemon, level, see_form
    )
  end
  $Trainer.pokedex.register(pokemon) if see_form
  $Trainer.pokedex.set_owned(pokemon.species)
  pokemon.record_first_moves
  return Ironmon.intercept_acquisition(pokemon, :gift_or_static)
end

alias ironmon_pivot_original_pb_add_foreign_pokemon pbAddForeignPokemon
def pbAddForeignPokemon(pokemon, level = 1, owner_name = nil,
                        nickname = nil, owner_gender = 0, see_form = true)
  return false if Ironmon.block_failed_run_action
  return false if !Ironmon.confirm_gift_acquisition(pokemon)
  return Ironmon.with_acquisition_source(:gift_or_static) do
    ironmon_pivot_original_pb_add_foreign_pokemon(
      pokemon, level, owner_name, nickname, owner_gender, see_form
    )
  end
end

alias ironmon_pivot_original_pb_add_rental_pokemon pbAddRentalPokemon
def pbAddRentalPokemon(species, level)
  return false if Ironmon.block_failed_run_action
  return Ironmon.with_acquisition_exclusion(:rental) do
    ironmon_pivot_original_pb_add_rental_pokemon(species, level)
  end
end

alias ironmon_pivot_original_pb_generate_egg pbGenerateEgg
def pbGenerateEgg(pokemon, obtain_text = "")
  return false if Ironmon.block_failed_run_action
  result = ironmon_pivot_original_pb_generate_egg(pokemon, obtain_text)
  if result && Ironmon.active?
    Ironmon.record_excluded_acquisition(pokemon, :egg, :egg)
  end
  return result
end

def pbAddEgg(pokemon, obtain_text = "")
  return pbGenerateEgg(pokemon, obtain_text)
end

def pbGenEgg(pokemon, obtain_text = "")
  return pbGenerateEgg(pokemon, obtain_text)
end

alias ironmon_pivot_original_pb_start_trade pbStartTrade
def pbStartTrade(pokemonIndex, newpoke, nickname, trainerName,
                 trainerGender = 0, savegame = false)
  return false if Ironmon.block_failed_run_action
  if Ironmon.active? && Ironmon.pivot_state.pending?
    pbMessage(_INTL("Another Pokemon acquisition must be resolved before trading."))
    return nil
  end
  if Ironmon.active?
    outgoing = $Trainer.party[pokemonIndex]
    if !Ironmon.progression_trade_pokemon?(outgoing)
      outgoing = Ironmon.prepare_progression_trade_pokemon({
        :species => outgoing.species,
        :context => [:trade, newpoke, trainerName]
      })
      pokemonIndex = $Trainer.party.index(outgoing)
    end
  end
  pokemon = nil
  begin
    pokemon = ironmon_pivot_original_pb_start_trade(
      pokemonIndex, newpoke, nickname, trainerName, trainerGender, savegame
    )
  ensure
    Ironmon.cleanup_progression_trade_pokemon if Ironmon.active?
  end
  if Ironmon.active?
    Ironmon.resolve_party_acquisition(pokemon, :trade)
  end
  return pokemon
end
