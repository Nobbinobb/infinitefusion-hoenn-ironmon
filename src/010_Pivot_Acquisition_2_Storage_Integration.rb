#===============================================================================
# Ironmon PC and storage acquisition enforcement
#===============================================================================

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

  alias ironmon_pivot_original_hold_multi pbHoldMulti
  def pbHoldMulti(box, selected_index)
    if Ironmon.active? && box == -1
      selected = getMultiSelection(box, nil)
      blocked = selected.any? do |index|
        Ironmon.pc_movement_blocked?(@storage[box, index])
      end
      if blocked
        Ironmon.pc_movement_message(self)
        return false
      end
    end
    return ironmon_pivot_original_hold_multi(box, selected_index)
  end

  alias ironmon_pivot_original_place_multi pbPlaceMulti
  def pbPlaceMulti(box, selected_index)
    if Ironmon.active? && box == -1 && @multiheldpkmn
      blocked = @multiheldpkmn.any? do |held|
        Ironmon.pc_movement_blocked?(held[0])
      end
      if blocked
        Ironmon.pc_movement_message(self)
        return false
      end
    end
    return ironmon_pivot_original_place_multi(box, selected_index)
  end
end
