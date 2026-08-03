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
    actions = if !current
                candidate_fusion ?
                  [:take, :reverse_and_take, :unfuse_and_take] : [:take]
              elsif candidate_fusion
                [:swap, :swap_and_reverse, :swap_and_unfuse]
              elsif current.isFusion?
                [:swap]
              else
                [:swap, :fuse]
              end
    return actions
  end

  def self.pivot_action_label(action, current = nil, candidate = nil)
    label = PIVOT_ACTION_LABELS[action]
    return label if action != :fuse || !current || !candidate
    known_species = known_player_fusion_species(current.species,
                                                candidate.species)
    return label if !known_species
    return _INTL("Fuse (Known: {1})",
                 GameData::Species.get(known_species).real_name)
  rescue StandardError
    return label
  end

  def self.choose_blind_pivot_action(actions, current = nil, candidate = nil)
    commands = actions.map do |action|
      pivot_action_label(action, current, candidate)
    end
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

    action = nil
    loop do
      action ||= if action_selector
                   action_selector.call(actions.dup)
                 else
                   choose_blind_pivot_action(actions, current, candidate)
                 end
      if !actions.include?(action)
        raise PivotTransactionError, "The selected pivot action is not legal."
      end
      begin
        result = build_pivot_result(action, current, candidate,
                                    !action_selector, acquisition_id)
        commit_pending_result(acquisition_id, result, action)
        if !action_selector
          pbMessage(_INTL("The pivot is complete. You kept {1}.", result.name))
        end
        return true
      rescue StandardError => e
        if action_selector
          raise e
        end
        echoln "Ironmon pivot action failed: #{e.message}"
        pbMessage(_INTL("That result could not be created. Your current Pokemon was preserved; the chosen action will be retried."))
      end
    end
  end

  def self.build_pivot_result(action, current, candidate, interactive = false,
                              acquisition_id = nil)
    case action
    when :take, :swap
      result = candidate.clone
      mark_processed_caught_fusion(result) if result.isFusion?
      return result
    when :fuse
      return build_blind_fusion_result(current, candidate, interactive)
    when :reverse_and_take, :swap_and_reverse
      return build_reversed_caught_result(candidate)
    when :unfuse_and_take, :swap_and_unfuse
      return build_unfused_caught_result(candidate, acquisition_id,
                                         interactive)
    end
    raise PivotTransactionError, "The selected pivot action is unknown."
  end

  def self.build_blind_fusion_result(current, candidate, interactive = false)
    if !current || current.isFusion? || candidate.isFusion?
      raise PivotTransactionError, "Only two normal Pokemon can be fused."
    end
    body = current.clone
    head = candidate.clone
    result_species = player_fusion_species(body.species, head.species)
    body.original_body = current.clone
    body.original_head = candidate.clone
    body.exp_when_fused_body = current.exp
    body.exp_when_fused_head = candidate.exp
    body.exp_gained_since_fused = 0
    body.body_original_ability_index = current.ability_index
    body.head_original_ability_index = candidate.ability_index
    body.body_shiny = current.shiny?
    body.head_shiny = candidate.shiny?
    body.pif_sprite = nil
    body.species = result_species
    apply_normal_fusion_instance_data(body, head, interactive)
    body.name = GameData::Species.get(result_species).real_name if
      current.name == current.species_data.real_name
    body.calc_stats
    mark_player_created_fusion(body)
    return body
  end

  def self.apply_normal_fusion_instance_data(body, head, interactive)
    GameData::Stat.each_main do |stat|
      body.iv[stat.id] = ((body.iv[stat.id] + head.iv[stat.id]) / 2).floor
    end
    high_level = [body.level, head.level].max
    low_level = [body.level, head.level].min
    body.level = ((2 * high_level) + low_level) / 3
    available_moves = (body.moves + head.moves).uniq { |move| move.id }
    if available_moves.length <= 4
      body.moves = available_moves.map { |move| move.clone }
    elsif interactive
      scene = FusionMovesOptionsScene.new(body, head)
      screen = PokemonOptionScreen.new(scene)
      screen.pbStartScreen
      body.moves = scene.getSelectedMoves.map { |move| move.clone }
    else
      body.moves = available_moves[0, 4].map { |move| move.clone }
    end

    if interactive && body.nature.id != head.nature.id
      natures = [body.nature, head.nature]
      loop do
        choice = pbMessage(
          _INTL("Choose the fused Pokemon's nature."),
          natures.map { |nature| nature.real_name },
          -1
        )
        if choice && choice >= 0 && choice < natures.length
          body.nature = natures[choice].id
          break
        end
        pbMessage(_INTL("The fusion choice cannot be cancelled."))
      end
    end

    learned_moves = []
    learned_moves.concat(body.learned_moves) if body.learned_moves
    learned_moves.concat(head.learned_moves) if head.learned_moves
    available_moves.each do |move|
      learned_moves << move.id if !learned_moves.include?(move.id)
    end
    learned_moves.each { |move| body.add_learned_move(move) }

    return_items = [body.item, head.item].compact
    body.item = nil
    body.instance_variable_set(:@ironmon_pivot_return_items, return_items)
    body.obtain_method = 0
    body.calc_stats
    return body
  end

  def self.build_reversed_caught_result(candidate)
    validate_caught_fusion_right(candidate)
    result = candidate.clone
    reversed_species = paired_custom_fusion_species(result.species)
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

  def self.build_unfused_caught_result(candidate, acquisition_id = nil,
                                       interactive = false)
    validate_caught_fusion_right(candidate)
    component = select_caught_unfusion_component(
      candidate, acquisition_id, interactive
    )
    return prepare_caught_unfusion_result(candidate, component)
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
