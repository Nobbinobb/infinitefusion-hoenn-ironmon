#===============================================================================
# One-Pokemon party enforcement and acquisition interception
#===============================================================================

module Ironmon
  class PivotTransactionError < StandardError; end

  @acquisition_source = nil
  @acquisition_exclusion = nil

  def self.with_acquisition_source(source)
    previous_source = @acquisition_source
    @acquisition_source = source
    return yield
  ensure
    @acquisition_source = previous_source
  end

  def self.with_acquisition_exclusion(reason)
    previous_exclusion = @acquisition_exclusion
    @acquisition_exclusion = reason
    return yield
  ensure
    @acquisition_exclusion = previous_exclusion
  end

  def self.current_acquisition_source
    return @acquisition_source || :wild_catch
  end

  def self.current_acquisition_exclusion
    return @acquisition_exclusion
  end

  def self.party_excluded_pokemon?(pokemon)
    return true if !pokemon
    return true if pokemon.egg?
    return pokemon.ironmon_party_exclusion != nil
  rescue Exception
    return false
  end

  def self.usable_party
    return [] if !$Trainer || !$Trainer.party
    return $Trainer.party.find_all do |pokemon|
      pokemon && !party_excluded_pokemon?(pokemon)
    end
  end

  def self.enforce_party_limit
    return true if !$Trainer || !$Trainer.party
    usable = usable_party
    return true if usable.length <= 1
    usable[1..-1].each do |pokemon|
      $Trainer.party.delete(pokemon)
      pivot_state.quarantine_pokemon(pokemon, :party_limit_migration)
      echoln "Ironmon quarantined an extra party Pokemon: #{pokemon.species}"
    end
    $Trainer.party.compact!
    return usable_party.length <= 1
  end

  def self.record_excluded_acquisition(pokemon, reason, source = nil)
    source ||= current_acquisition_source
    acquisition_id = reserve_acquisition_id
    if pokemon.is_a?(Pokemon) && !pokemon.egg?
      pokemon.ironmon_party_exclusion = reason
    end
    species = pokemon.is_a?(Pokemon) ? pokemon.species : pokemon
    pivot_state.record_excluded_acquisition({
      :acquisition_id => acquisition_id,
      :source => source,
      :reason => reason,
      :species => species
    })
    echoln "Ironmon excluded acquisition #{acquisition_id}: #{reason}"
    return acquisition_id
  end

  def self.intercept_acquisition(pokemon, source = nil)
    raise PivotTransactionError, "No Pokemon was supplied." if !pokemon
    source ||= current_acquisition_source
    exclusion = current_acquisition_exclusion
    if pokemon.egg?
      record_excluded_acquisition(pokemon, :egg, source)
      return :excluded
    elsif exclusion
      record_excluded_acquisition(pokemon, exclusion, source)
      return :excluded
    end

    enforce_party_limit
    state = pivot_state
    if state.pending?
      raise PivotTransactionError, "Another Pokemon acquisition is pending."
    end
    acquisition_id = reserve_acquisition_id
    current = usable_party[0]
    mark_caught_fusion(pokemon) if pokemon.isFusion?
    state.begin_pivot(acquisition_id, {
      :source => source,
      :candidate => pokemon,
      :current_personal_id => current ? current.personalID : nil
    })
    return resolve_pending_pivot(acquisition_id)
  rescue PivotTransactionError => e
    echoln "Ironmon acquisition failed: #{e.message}"
    begin
      pbMessage(_INTL("The pivot could not be completed. Your current Pokemon was preserved."))
    rescue Exception
    end
    return false
  end

  def self.commit_pending_swap(acquisition_id)
    pending = pivot_state.pending_pivot
    candidate = pending ? pending[:candidate] : nil
    return commit_pending_result(acquisition_id, candidate)
  end

  def self.commit_pending_result(acquisition_id, result_pokemon, action = nil)
    state = pivot_state
    pending = state.pending_pivot
    if !state.pending? || pending[:acquisition_id] != acquisition_id.to_s
      raise PivotTransactionError, "The pending acquisition does not match."
    end
    if !result_pokemon.is_a?(Pokemon) || result_pokemon.egg?
      raise PivotTransactionError, "The pivot result is invalid."
    end

    original_party = $Trainer.party.dup
    discovery_snapshot = state.discovered_fusion_mappings.dup
    original_bag = $PokemonBag
    begin
      retained = original_party.find_all do |pokemon|
        pokemon && party_excluded_pokemon?(pokemon)
      end
      $Trainer.party = [result_pokemon] + retained
      if usable_party.length != 1 || usable_party[0] != result_pokemon
        raise PivotTransactionError, "The replacement party is invalid."
      end
      returned_items = result_pokemon.instance_variable_get(
        :@ironmon_pivot_return_items
      )
      if returned_items && !returned_items.empty? && original_bag
        staged_bag = Marshal.load(Marshal.dump(original_bag))
        returned_items.each do |item|
          if item && !staged_bag.pbStoreItem(item, 1)
            raise PivotTransactionError,
                  "There is no room for the fused Pokemon's held items."
          end
        end
        $PokemonBag = staged_bag
      end
      if result_pokemon.instance_variable_defined?(
        :@ironmon_pivot_return_items
      )
        result_pokemon.remove_instance_variable(:@ironmon_pivot_return_items)
      end
      if action == :fuse
        previous = original_party.find do |pokemon|
          pokemon && !party_excluded_pokemon?(pokemon)
        end
        record_player_fusion_discovery(previous, pending[:candidate],
                                       result_pokemon)
      end
      state.complete_pivot(acquisition_id)
      return true
    rescue Exception => e
      $Trainer.party = original_party
      $PokemonBag = original_bag
      state.discovered_fusion_mappings.clear
      state.discovered_fusion_mappings.update(discovery_snapshot)
      if e.is_a?(PivotTransactionError)
        raise e
      end
      raise PivotTransactionError, e.message
    end
  end

  def self.record_trade_acquisition(pokemon)
    return if !pokemon.is_a?(Pokemon)
    enforce_party_limit
    mark_processed_caught_fusion(pokemon) if pokemon.isFusion?
    acquisition_id = reserve_acquisition_id
    pivot_state.begin_pivot(acquisition_id, {
      :source => :trade,
      :candidate => pokemon,
      :current_personal_id => pokemon.personalID
    })
    pivot_state.complete_pivot(acquisition_id)
    return pokemon
  end

  def self.pc_movement_blocked?(pokemon)
    return false if !active? || !pokemon
    return !pokemon.egg?
  end

  def self.pc_movement_message(screen)
    screen.pbDisplay(_INTL("Ironmon does not allow usable Pokemon to move between the party and PC."))
  end
end

alias ironmon_pivot_original_prompt_caught_pokemon_action promptCaughtPokemonAction
def promptCaughtPokemonAction(pokemon)
  return ironmon_pivot_original_prompt_caught_pokemon_action(pokemon) if
    !Ironmon.active?
  if pokemon.egg? || Ironmon.current_acquisition_exclusion
    return ironmon_pivot_original_prompt_caught_pokemon_action(pokemon)
  end
  return Ironmon.intercept_acquisition(pokemon)
end

alias ironmon_pivot_original_pb_store_pokemon pbStorePokemon
def pbStorePokemon(pokemon)
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
  return Ironmon.with_acquisition_source(:gift_or_static) do
    ironmon_pivot_original_pb_add_pokemon(
      pokemon, level, see_form, dontRandomize, variableToSave
    )
  end
end

alias ironmon_pivot_original_pb_add_to_party pbAddToParty
def pbAddToParty(pokemon, level = 1, see_form = true, dontRandomize = false)
  return Ironmon.with_acquisition_source(:gift_or_static) do
    ironmon_pivot_original_pb_add_to_party(
      pokemon, level, see_form, dontRandomize
    )
  end
end

alias ironmon_pivot_original_pb_add_pokemon_silent pbAddPokemonSilent
def pbAddPokemonSilent(pokemon, level = 1, see_form = true)
  return ironmon_pivot_original_pb_add_pokemon_silent(
    pokemon, level, see_form
  ) if !Ironmon.active?
  return false if !pokemon
  pokemon = Pokemon.new(pokemon, level) if !pokemon.is_a?(Pokemon)
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
  return Ironmon.intercept_acquisition(pokemon, :gift_or_static)
end

alias ironmon_pivot_original_pb_add_to_party_silent pbAddToPartySilent
def pbAddToPartySilent(pokemon, level = nil, see_form = true)
  return ironmon_pivot_original_pb_add_to_party_silent(
    pokemon, level, see_form
  ) if !Ironmon.active?
  return false if !pokemon
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

alias ironmon_pivot_original_pb_add_rental_pokemon pbAddRentalPokemon
def pbAddRentalPokemon(species, level)
  return Ironmon.with_acquisition_exclusion(:rental) do
    ironmon_pivot_original_pb_add_rental_pokemon(species, level)
  end
end

alias ironmon_pivot_original_pb_generate_egg pbGenerateEgg
def pbGenerateEgg(pokemon, obtain_text = "")
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
  if Ironmon.active? && Ironmon.pivot_state.pending?
    pbMessage(_INTL("Another Pokemon acquisition must be resolved before trading."))
    return nil
  end
  pokemon = ironmon_pivot_original_pb_start_trade(
    pokemonIndex, newpoke, nickname, trainerName, trainerGender, savegame
  )
  Ironmon.record_trade_acquisition(pokemon) if Ironmon.active?
  return pokemon
end

class PokemonStorage
  alias ironmon_pivot_original_pb_copy pbCopy
  def pbCopy(boxDst, indexDst, boxSrc, indexSrc)
    pokemon = self[boxSrc, indexSrc]
    if (boxDst == -1 || boxSrc == -1) &&
       Ironmon.pc_movement_blocked?(pokemon)
      return false
    end
    return ironmon_pivot_original_pb_copy(boxDst, indexDst, boxSrc, indexSrc)
  end

  alias ironmon_pivot_original_move_caught_to_party pbMoveCaughtToParty
  def pbMoveCaughtToParty(pokemon)
    return false if Ironmon.pc_movement_blocked?(pokemon)
    return ironmon_pivot_original_move_caught_to_party(pokemon)
  end

  alias ironmon_pivot_original_move_caught_to_box pbMoveCaughtToBox
  def pbMoveCaughtToBox(pokemon, box)
    return false if Ironmon.pc_movement_blocked?(pokemon)
    return ironmon_pivot_original_move_caught_to_box(pokemon, box)
  end

  alias ironmon_pivot_original_store_caught pbStoreCaught
  def pbStoreCaught(pokemon)
    if Ironmon.active? && !Ironmon.party_excluded_pokemon?(pokemon)
      result = Ironmon.intercept_acquisition(pokemon, :direct_storage)
      if !result
        pbMessage(_INTL("The Pokemon could not be added, so your current Pokemon was preserved."))
      end
      return -1
    end
    return ironmon_pivot_original_store_caught(pokemon)
  end
end

class PokeBattle_RealBattlePeer
  alias ironmon_pivot_original_peer_store_pokemon pbStorePokemon
  def pbStorePokemon(player, pokemon)
    if Ironmon.active?
      result = Ironmon.intercept_acquisition(pokemon, :wild_catch)
      if !result
        pbMessage(_INTL("The Pokemon could not be added, so your current Pokemon was preserved."))
      end
      return result ? -1 : pbCurrentBox
    end
    return ironmon_pivot_original_peer_store_pokemon(player, pokemon)
  end
end

class PokemonStorageScreen
  alias ironmon_pivot_original_withdraw pbWithdraw
  def pbWithdraw(selected, heldpoke)
    pokemon = heldpoke || @storage[selected[0], selected[1]]
    if Ironmon.pc_movement_blocked?(pokemon)
      Ironmon.pc_movement_message(self)
      return false
    end
    return ironmon_pivot_original_withdraw(selected, heldpoke)
  end

  alias ironmon_pivot_original_store pbStore
  def pbStore(selected, heldpoke)
    pokemon = heldpoke || @storage[selected[0], selected[1]]
    if Ironmon.pc_movement_blocked?(pokemon)
      Ironmon.pc_movement_message(self)
      return false
    end
    return ironmon_pivot_original_store(selected, heldpoke)
  end

  alias ironmon_pivot_original_hold pbHold
  def pbHold(selected)
    pokemon = @storage[selected[0], selected[1]]
    if selected[0] == -1 && Ironmon.pc_movement_blocked?(pokemon)
      Ironmon.pc_movement_message(self)
      return false
    end
    return ironmon_pivot_original_hold(selected)
  end

  alias ironmon_pivot_original_place pbPlace
  def pbPlace(selected)
    if selected[0] == -1 && Ironmon.pc_movement_blocked?(@heldpkmn)
      Ironmon.pc_movement_message(self)
      return false
    end
    return ironmon_pivot_original_place(selected)
  end

  alias ironmon_pivot_original_swap pbSwap
  def pbSwap(selected)
    party_pokemon = selected[0] == -1 ? @storage[-1, selected[1]] : nil
    if selected[0] == -1 &&
       (Ironmon.pc_movement_blocked?(@heldpkmn) ||
        Ironmon.pc_movement_blocked?(party_pokemon))
      Ironmon.pc_movement_message(self)
      return false
    end
    return ironmon_pivot_original_swap(selected)
  end
end
