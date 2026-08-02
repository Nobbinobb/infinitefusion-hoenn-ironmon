#===============================================================================
# Non-cancellable blind pivot decision and transactional result preparation
#===============================================================================

module Ironmon
  PIVOT_ACTION_LABELS = {
    :take => _INTL("Take"),
    :swap => _INTL("Swap"),
    :fuse => _INTL("Fuse"),
    :reverse_and_take => _INTL("Reverse and Take"),
    :unfuse_and_take => _INTL("Unfuse and Take One"),
    :swap_and_reverse => _INTL("Swap and Reverse"),
    :swap_and_unfuse => _INTL("Swap and Unfuse")
  }.freeze

  def self.pivot_actions(current, candidate)
    raise PivotTransactionError, "The candidate is invalid." if
      !candidate.is_a?(Pokemon) || candidate.egg?
    candidate_fusion = candidate.isFusion?
    if !current
      return [:take] if !candidate_fusion
      return [:take, :reverse_and_take, :unfuse_and_take]
    end
    return [:swap, :swap_and_reverse, :swap_and_unfuse] if candidate_fusion
    return [:swap] if current.isFusion?
    return [:swap, :fuse]
  end

  def self.choose_blind_pivot_action(actions)
    commands = actions.map { |action| PIVOT_ACTION_LABELS[action] }
    loop do
      choice = pbMessage(
        _INTL("Choose the new Pokemon's fate. Its hidden data cannot be inspected before this choice."),
        commands,
        -1
      )
      return actions[choice] if choice && choice >= 0 && choice < actions.length
      pbMessage(_INTL("This acquisition must be resolved before continuing."))
    end
  end

  def self.resolve_pending_pivot(acquisition_id, action_selector = nil)
    state = pivot_state
    pending = state.pending_pivot
    if !state.pending? || pending[:acquisition_id] != acquisition_id.to_s
      raise PivotTransactionError, "The pending acquisition does not match."
    end
    candidate = pending[:candidate]
    current = usable_party[0]
    expected_personal_id = pending[:current_personal_id]
    if (current ? current.personalID : nil) != expected_personal_id
      raise PivotTransactionError, "The current Pokemon changed during the pivot."
    end
    actions = pivot_actions(current, candidate)

    loop do
      action = if action_selector
                 action_selector.call(actions.dup)
               else
                 choose_blind_pivot_action(actions)
               end
      if !actions.include?(action)
        raise PivotTransactionError, "The selected pivot action is not legal."
      end
      begin
        result = build_pivot_result(action, current, candidate)
        commit_pending_result(acquisition_id, result)
        pbMessage(_INTL("The pivot is complete. You kept {1}.", result.name)) if
          !action_selector
        return true
      rescue StandardError => e
        if action_selector
          raise e
        end
        echoln "Ironmon pivot action failed: #{e.message}"
        pbMessage(_INTL("That result could not be created. Your current Pokemon was preserved; choose again."))
      end
    end
  end

  def self.build_pivot_result(action, current, candidate)
    case action
    when :take, :swap
      result = candidate.clone
      mark_processed_caught_fusion(result) if result.isFusion?
      return result
    when :fuse
      return build_blind_fusion_result(current, candidate)
    when :reverse_and_take, :swap_and_reverse
      return build_reversed_caught_result(candidate)
    when :unfuse_and_take, :swap_and_unfuse
      return build_unfused_caught_result(candidate)
    end
    raise PivotTransactionError, "The selected pivot action is unknown."
  end

  # Step 2.4 replaces the ordinary orientation below with the run-seeded,
  # custom-sprite fusion mapping. Preparing a clone here keeps Step 2.3 fully
  # transactional and prevents either input from entering storage.
  def self.build_blind_fusion_result(current, candidate)
    if !current || current.isFusion? || candidate.isFusion?
      raise PivotTransactionError, "Only two normal Pokemon can be fused."
    end
    body = current.clone
    head = candidate.clone
    result_species = getFusionSpecies(body.species, head.species)
    body.original_body = current.clone
    body.original_head = candidate.clone
    body.exp_when_fused_body = current.exp
    body.exp_when_fused_head = candidate.exp
    body.exp_gained_since_fused = 0
    body.body_shiny = current.shiny?
    body.head_shiny = candidate.shiny?
    body.pif_sprite = nil
    body.species = result_species
    body.name = result_species.real_name if
      current.name == current.species_data.real_name
    body.calc_stats
    mark_player_created_fusion(body)
    return body
  end

  def self.build_reversed_caught_result(candidate)
    validate_caught_fusion_right(candidate)
    result = candidate.clone
    reversed_species = reverseFusionSpecies(result.species)
    if GameData::Species.get(reversed_species).species == result.species
      raise PivotTransactionError, "This special fusion cannot be reversed."
    end
    result.exp_when_fused_body, result.exp_when_fused_head =
      result.exp_when_fused_head, result.exp_when_fused_body
    result.head_shiny, result.body_shiny =
      result.body_shiny, result.head_shiny
    result.original_body, result.original_head =
      result.original_head, result.original_body
    result.pif_sprite = nil
    result.species = reversed_species
    result.name = GameData::Species.get(reversed_species).real_name
    result.calc_stats
    mark_processed_caught_fusion(result)
    return result
  end

  # Step 2.5 supplies the configured deterministic/player-choice component
  # selector. Until then, Step 2.3 uses the body component without exposing a
  # summary and discards the other component at commit.
  def self.build_unfused_caught_result(candidate)
    validate_caught_fusion_right(candidate)
    body_species = getBasePokemonID(candidate.species, true)
    if !body_species || body_species <= 0
      raise PivotTransactionError, "This special fusion cannot be unfused."
    end
    result = candidate.clone
    result.species = body_species
    result.name = GameData::Species.get(body_species).real_name
    result.pif_sprite = nil
    result.original_body = nil
    result.original_head = nil
    result.exp_when_fused_body = nil
    result.exp_when_fused_head = nil
    result.exp_gained_since_fused = 0
    result.body_shiny = false
    result.head_shiny = false
    result.ironmon_fusion_origin = nil
    result.ironmon_transformation_right = TRANSFORMATION_RIGHT_NONE
    result.calc_stats
    return result
  end

  def self.validate_caught_fusion_right(candidate)
    if !candidate.is_a?(Pokemon) || !candidate.isFusion?
      raise PivotTransactionError, "The candidate is not a fusion."
    end
    if candidate.ironmon_fusion_origin != FUSION_ORIGIN_CAUGHT ||
       candidate.ironmon_transformation_right != TRANSFORMATION_RIGHT_CAUGHT_PIVOT
      raise PivotTransactionError, "This fusion has no transformation right."
    end
    return true
  end
end
