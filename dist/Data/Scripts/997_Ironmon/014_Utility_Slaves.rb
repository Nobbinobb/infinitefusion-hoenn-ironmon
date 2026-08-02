#===============================================================================
# One non-combat utility slot for story gifts and hidden field moves
#===============================================================================

module Ironmon
  UTILITY_SLAVE_EXCLUSION = :utility_slave

  def self.utility_slave?(pokemon)
    return pokemon &&
      pokemon.ironmon_party_exclusion == UTILITY_SLAVE_EXCLUSION
  rescue StandardError
    return false
  end

  def self.utility_slaves
    return [] if !$Trainer || !$Trainer.party
    return $Trainer.party.find_all { |pokemon| utility_slave?(pokemon) }
  end

  def self.build_utility_slave(candidate)
    if !candidate.is_a?(Pokemon) || candidate.egg?
      raise PivotTransactionError, "The utility candidate is invalid."
    end
    result = candidate.clone
    mark_processed_caught_fusion(result) if result.isFusion?
    result.ironmon_party_exclusion = UTILITY_SLAVE_EXCLUSION
    return result
  end

  def self.commit_pending_utility_slave(acquisition_id, utility_pokemon)
    state = pivot_state
    pending = state.pending_pivot
    if !state.pending? || pending[:acquisition_id] != acquisition_id.to_s
      raise PivotTransactionError, "The pending acquisition does not match."
    end
    if !utility_slave?(utility_pokemon)
      raise PivotTransactionError, "The utility result is not marked."
    end
    if usable_party.length != 1
      raise PivotTransactionError,
            "A utility slave requires one current usable Pokemon."
    end

    original_party = $Trainer.party.dup
    begin
      replacement_party = original_party.reject do |pokemon|
        utility_slave?(pokemon)
      end
      replacement_party << utility_pokemon
      if replacement_party.length > Settings::MAX_PARTY_SIZE
        raise PivotTransactionError,
              "There is no party slot available for a utility slave."
      end
      $Trainer.party = replacement_party
      if usable_party.length != 1 || utility_slaves != [utility_pokemon]
        raise PivotTransactionError, "The utility party is invalid."
      end
      state.complete_pivot(acquisition_id)
      return true
    rescue Exception => e
      $Trainer.party = original_party
      raise e if e.is_a?(PivotTransactionError)
      raise PivotTransactionError, e.message
    end
  end

  def self.battle_party_without_utility_slaves(party)
    return party if !party
    return party.map do |pokemon|
      utility_slave?(pokemon) ? nil : pokemon
    end
  end
end

class Player
  alias ironmon_utility_original_able_party able_party
  def able_party
    return ironmon_utility_original_able_party.reject do |pokemon|
      Ironmon.utility_slave?(pokemon)
    end
  end

  def able_pokemon_count
    return able_party.length
  end

  def remove_pokemon_at_index(index)
    return false if index < 0 || index >= party_count
    have_able = @party.each_with_index.any? do |pokemon, party_index|
      party_index != index && pokemon && pokemon.able? &&
        !Ironmon.utility_slave?(pokemon)
    end
    return false if !have_able
    @party.delete_at(index)
    return true
  end

  def has_other_able_pokemon?(index)
    return @party.each_with_index.any? do |pokemon, party_index|
      party_index != index && pokemon && pokemon.able? &&
        !Ironmon.utility_slave?(pokemon)
    end
  end
end

class PokeBattle_Battle
  alias ironmon_utility_original_initialize initialize
  def initialize(scene, party1, party2, player, opponent)
    filtered_party = Ironmon.battle_party_without_utility_slaves(party1)
    ironmon_utility_original_initialize(
      scene, filtered_party, party2, player, opponent
    )
  end
end

alias ironmon_utility_original_pb_pickup pbPickup
def pbPickup(pokemon)
  return if Ironmon.utility_slave?(pokemon)
  ironmon_utility_original_pb_pickup(pokemon)
end

alias ironmon_utility_original_pb_honey_gather pbHoneyGather
def pbHoneyGather(pokemon)
  return if Ironmon.utility_slave?(pokemon)
  ironmon_utility_original_pb_honey_gather(pokemon)
end
