#===============================================================================
# One-Pokemon party enforcement and acquisition interception
#===============================================================================

module Ironmon
  class PivotTransactionError < StandardError; end

  @acquisition_source = nil
  @acquisition_exclusion = nil
  @starter_acquisition = false

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

  def self.with_starter_acquisition
    previous = @starter_acquisition
    @starter_acquisition = true
    return yield
  ensure
    @starter_acquisition = previous
  end

  def self.starter_acquisition?
    return @starter_acquisition == true
  end

  def self.mark_starter_pokemon(pokemon)
    pokemon.instance_variable_set(:@ironmon_starter_pokemon, true)
    return pokemon
  end

  def self.starter_pokemon?(pokemon)
    return pokemon &&
      pokemon.instance_variable_get(:@ironmon_starter_pokemon) == true
  rescue StandardError
    return false
  end

  def self.synchronize_hoenn_starter_after_battle
    return if !$Trainer || !$Trainer.party
    starter = pbGet(VAR_HOENN_STARTER)
    return if !starter_pokemon?(starter)
    current = $Trainer.party.find do |pokemon|
      pokemon && pokemon.personalID == starter.personalID
    end
    return if !current
    pbSet(VAR_HOENN_STARTER, current)
  rescue StandardError
    return
  end

  def self.current_acquisition_source
    return @acquisition_source || :wild_catch
  end

  def self.current_acquisition_exclusion
    return @acquisition_exclusion
  end

  def self.gift_acquisition_confirmation_required?(pokemon)
    return false if !active? || !pokemon
    return false if starter_acquisition? || starter_pokemon?(pokemon)
    return false if current_acquisition_exclusion
    return false if pokemon.is_a?(Pokemon) && pokemon.egg?
    return true
  end

  def self.confirm_gift_acquisition(pokemon)
    return true if !gift_acquisition_confirmation_required?(pokemon)
    return pbConfirmMessageSerious(
      _INTL("Accept this gifted Pokemon? Accepting starts a mandatory pivot.")
    )
  rescue Exception => error
    echoln "Ironmon gift confirmation failed: #{error.message}"
    return false
  end

  def self.party_excluded_pokemon?(pokemon)
    return true if !pokemon
    return true if pokemon.egg?
    return pokemon.ironmon_party_exclusion != nil
  rescue Exception
    return false
  end

  def self.legacy_utility_pokemon?(pokemon)
    return pokemon && pokemon.ironmon_party_exclusion == :utility_slave
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
    $Trainer.party.find_all do |pokemon|
      legacy_utility_pokemon?(pokemon)
    end.each do |pokemon|
      $Trainer.party.delete(pokemon)
      pivot_state.quarantine_pokemon(pokemon, :removed_utility_slot)
      echoln "Ironmon removed a legacy utility Pokemon: #{pokemon.species}"
    end
    $Trainer.party.compact!
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

  def self.intercept_acquisition(pokemon, source = nil,
                                 action_selector = nil)
    return false if block_failed_run_action
    raise PivotTransactionError, "No Pokemon was supplied." if !pokemon
    source ||= current_acquisition_source
    source = :starter if starter_pokemon?(pokemon)
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
    starter = starter_pokemon?(pokemon)
    mark_caught_fusion(pokemon) if pokemon.isFusion? && !starter
    state.begin_pivot(acquisition_id, {
      :source => source,
      :candidate => pokemon,
      :current_personal_id => current ? current.personalID : nil
    })
    if starter
      action_selector = proc do |actions|
        actions.include?(:take) ? :take : :swap
      end
    end
    return resolve_pending_pivot(acquisition_id, action_selector)
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

  def self.pc_movement_blocked?(pokemon)
    return false if !active? || !pokemon
    return !pokemon.egg?
  end

  def self.pc_movement_message(screen)
    screen.pbDisplay(_INTL("Ironmon does not allow usable Pokemon to move between the party and PC."))
  end
end
