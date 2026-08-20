#===============================================================================
# Caught-fusion component selection and post-pivot item-path enforcement
#===============================================================================

module Ironmon
  class CaughtFusionTransformationError < StandardError; end

  CAUGHT_UNFUSION_SCHEMA_VERSION = 1
  CAUGHT_UNFUSION_NAMESPACE = "caught_unfusion_component"

  FUSION_ITEM_IDS = [
    :DNASPLICERS,
    :SUPERSPLICERS,
    :INFINITESPLICERS,
    :INFINITESPLICERS2
  ].freeze
  REVERSAL_ITEM_IDS = [:DNAREVERSER, :INFINITEREVERSERS].freeze

  def self.caught_fusion_components(candidate)
    validate_caught_fusion_right(candidate)
    body_id = getBasePokemonID(candidate.species, true)
    head_id = getBasePokemonID(candidate.species, false)
    if !body_id || !head_id || body_id <= 0 || head_id <= 0 ||
       body_id > NB_POKEMON || head_id > NB_POKEMON
      raise CaughtFusionTransformationError,
            "this special fusion has no two normal components"
    end
    components = [
      {:species_id => body_id, :role => :body},
      {:species_id => head_id, :role => :head}
    ]
    components.sort_by! do |component|
      [component[:species_id], component[:role] == :body ? 0 : 1]
    end
    components.each_with_index do |component, index|
      component[:canonical_index] = index
    end
    return components
  end

  def self.select_caught_unfusion_component(candidate, acquisition_id,
                                             interactive = false)
    if !acquisition_id
      raise CaughtFusionTransformationError,
            "the caught fusion has no stable acquisition identifier"
    end
    state = pivot_state
    pending = state.pending_pivot
    if !state.pending? || pending[:acquisition_id] != acquisition_id.to_s
      raise CaughtFusionTransformationError,
            "the caught-fusion transaction does not match"
    end
    components = caught_fusion_components(candidate)
    stored_index = pending[:unfusion_component_index]
    if stored_index.is_a?(Integer) && components[stored_index]
      return components[stored_index]
    end

    selected = if configuration.unfusion_setting ==
                  Configuration::UNFUSION_PLAYER_CHOICE
                 if interactive
                   choose_caught_unfusion_component(components)
                 else
                   components[0]
                 end
               else
                 components[deterministic_unfusion_component_index(
                   acquisition_id, components
                 )]
               end
    pending[:unfusion_component_index] = selected[:canonical_index]
    return selected
  end

  def self.choose_caught_unfusion_component(components)
    commands = components.map do |component|
      GameData::Species.get(component[:species_id]).real_name
    end
    loop do
      choice = pbMessage(
        _INTL("Choose one component to keep. Its summary cannot be inspected."),
        commands,
        -1
      )
      return components[choice] if choice && choice >= 0 &&
        choice < components.length
      pbMessage(_INTL("The unfusion choice cannot be cancelled."))
    end
  end

  def self.deterministic_unfusion_component_index(acquisition_id, components)
    component_ids = components.map { |component| component[:species_id] }.sort
    value = fnv1a_64_joined([
      CAUGHT_UNFUSION_SCHEMA_VERSION,
      $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0,
      CAUGHT_UNFUSION_NAMESPACE,
      acquisition_id.to_s,
      component_ids[0],
      component_ids[1]
    ])
    return value % components.length
  end

  def self.prepare_caught_unfusion_result(candidate, component)
    role = component[:role]
    species_id = component[:species_id]
    original = role == :body ? candidate.original_body :
      candidate.original_head
    if original.is_a?(Pokemon)
      result = original.clone
      result.species = species_id if result.species_data.id_number != species_id
      gained_exp = candidate.exp_gained_since_fused || 0
      result.exp += gained_exp if gained_exp > 0
    else
      level = calculateUnfuseLevelOldMethod(candidate, false)
      level = 1 if level < 1
      result = Pokemon.new(species_id, level)
    end

    result.name = GameData::Species.get(species_id).real_name
    result.item = candidate.item
    result.pif_sprite = nil
    result.original_body = nil
    result.original_head = nil
    result.exp_when_fused_body = nil
    result.exp_when_fused_head = nil
    result.exp_gained_since_fused = 0
    result.body_shiny = false
    result.head_shiny = false
    component_shiny = role == :body ? candidate.bodyShiny? :
      candidate.headShiny?
    if candidate.shiny? && !candidate.bodyShiny? && !candidate.headShiny?
      component_shiny = true
    end
    result.shiny = component_shiny == true
    result.natural_shiny = candidate.natural_shiny if component_shiny
    ability_index = role == :body ? candidate.body_original_ability_index :
      candidate.head_original_ability_index
    result.ability_index = ability_index if ability_index

    learned_moves = (candidate.learned_moves || []).dup
    candidate.moves.each do |move|
      learned_moves << move.id if !learned_moves.include?(move.id)
    end
    learned_moves.each { |move| result.add_learned_move(move) }
    result.obtain_method = 0
    result.ironmon_fusion_origin = nil
    result.ironmon_transformation_right = TRANSFORMATION_RIGHT_NONE
    result.calc_stats
    return result
  end

  def self.install_use_on_pokemon_item_block(item, original_handler)
    ItemHandlers::UseOnPokemon.add(item, proc do |used_item, pokemon, scene|
      if Ironmon.active?
        next Ironmon.display_transformation_item_block(scene)
      end
      next original_handler.call(used_item, pokemon, scene)
    end)
  end

  def self.install_use_in_field_item_block(item, original_handler)
    ItemHandlers::UseInField.add(item, proc do |used_item|
      if Ironmon.active?
        next Ironmon.display_transformation_item_block
      end
      next original_handler.call(used_item)
    end)
  end

  def self.display_transformation_item_block(scene = nil)
    message = _INTL("Fusion, unfusion, and reversal items cannot be used during an Ironmon run.")
    if scene && scene.respond_to?(:pbDisplay)
      scene.pbDisplay(message)
    else
      pbMessage(message)
    end
    return false
  end

  ORIGINAL_FUSION_USE_ON_POKEMON = {}
  ORIGINAL_FUSION_USE_IN_FIELD = {}
  (FUSION_ITEM_IDS + REVERSAL_ITEM_IDS).each do |item|
    ORIGINAL_FUSION_USE_ON_POKEMON[item] = ItemHandlers::UseOnPokemon[item]
    original_handler = ORIGINAL_FUSION_USE_ON_POKEMON[item]
    next if !original_handler
    install_use_on_pokemon_item_block(item, original_handler)
  end

  FUSION_ITEM_IDS.each do |item|
    ORIGINAL_FUSION_USE_IN_FIELD[item] = ItemHandlers::UseInField[item]
    original_handler = ORIGINAL_FUSION_USE_IN_FIELD[item]
    next if !original_handler
    install_use_in_field_item_block(item, original_handler)
  end
end
