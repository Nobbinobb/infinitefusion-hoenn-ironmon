#===============================================================================
# Generated evolution runtime integration
#===============================================================================

module Ironmon
  EVOLUTION_CONFLICT_FNV_OFFSET_BASIS = 14_695_981_039_346_656_037
  EVOLUTION_CONFLICT_FNV_PRIME = 1_099_511_628_211
  EVOLUTION_CONFLICT_FNV_MASK = 0xFFFFFFFFFFFFFFFF

  def self.normal_evolution_runtime_species?(species_data)
    return false if !species_data
    return false if species_data.is_a?(GameData::FusedSpecies)
    return species_data.id_number > 0 && species_data.id_number <= NB_POKEMON
  end

  def self.fusion_evolution_runtime_species?(species_data)
    return false if !species_data
    return false if !species_data.is_a?(GameData::FusedSpecies)
    return false if species_data.id_number <= NB_POKEMON
    return false if species_data.id_number >= Settings::ZAPMOLCUNO_NB
    body = species_data.body_pokemon
    head = species_data.head_pokemon
    return body && head && body.id_number > 0 && head.id_number > 0 &&
      body.id_number <= NB_POKEMON && head.id_number <= NB_POKEMON
  end

  def self.generated_evolution_runtime_species?(species_data)
    return normal_evolution_runtime_species?(species_data) ||
      fusion_evolution_runtime_species?(species_data)
  end

  def self.generated_evolution_blocked?(pokemon)
    return true if pokemon.egg? || pokemon.shadowPokemon?
    return true if pokemon.hasItem?(:EVERSTONE)
    return true if pokemon.hasAbility?(:BATTLEBOND)
    return false
  end

  def self.select_generated_normal_evolution(pokemon, context, item = nil, other_pokemon = nil, forced = false)
    return select_generated_evolution(
      pokemon, :normal, context, item, other_pokemon, forced
    )
  end

  def self.select_generated_fusion_evolution(pokemon, context, item = nil, other_pokemon = nil, forced = false)
    return select_generated_evolution(
      pokemon, :fusion, context, item, other_pokemon, forced
    )
  end

  def self.select_generated_evolution(pokemon, source_kind, context, item = nil, other_pokemon = nil, forced = false)
    clear_generated_evolution_runtime_state(pokemon)
    return nil if !evolution_randomization_active?
    valid_source = if source_kind == :fusion
                     fusion_evolution_runtime_species?(pokemon.species_data)
                   else
                     normal_evolution_runtime_species?(pokemon.species_data)
                   end
    return nil if !valid_source
    return nil if !forced && generated_evolution_blocked?(pokemon)
    branches = if source_kind == :fusion
                 generated_fusion_evolution_branches_for(pokemon.species_data)
               else
                 generated_normal_evolution_branches_for(pokemon.species_data)
               end
    eligible = []
    branches.each do |branch|
      next if forced && shedinja_only_branch?(branch)
      triggers = if forced
                   []
                 else
                   eligible_effective_evolution_methods(
                     pokemon, branch, context, item, other_pokemon
                   )
                 end
      next if !forced && triggers.empty?
      eligible << { :branch => branch, :triggers => triggers }
    end
    return nil if eligible.empty?
    selected = resolve_generated_evolution_conflict(
      pokemon, context, eligible
    )
    trigger = if forced
                nil
              else
                selected[:triggers].sort_by do |method|
                  effective_evolution_method_order(method)
                end[0]
              end
    pending = {
      :pokemon => pokemon,
      :source => pokemon.species_data.id.to_s,
      :source_kind => source_kind,
      :branch_identity => selected[:branch][:identity],
      :component_side => selected[:branch][:component_side],
      :target => selected[:branch][:target],
      :target_id => selected[:branch][:target_id],
      :method => trigger ? trigger[:method] : nil,
      :parameter => trigger ? trigger[:parameter] : nil,
      :activation_context => context,
      :forced => forced
    }
    record_generated_evolution_offer(pokemon, pending, selected[:branch]) if
      respond_to?(:record_generated_evolution_offer)
    pending_generated_evolutions[pokemon.object_id] = pending
    return pending[:target_id]
  end

  def self.eligible_effective_evolution_methods(pokemon, branch, context, item, other_pokemon)
    return branch[:effective_methods].select do |method|
      evolution_method_eligible?(
        pokemon, method[:method], method[:parameter], context, item,
        other_pokemon
      )
    end
  end

  def self.evolution_method_eligible?(pokemon, method, parameter, context, item = nil, other_pokemon = nil)
    method_data = GameData::Evolution.get(method)
    case context
    when :level_up
      return !!method_data.call_level_up(pokemon, parameter)
    when :use_item
      return !!method_data.call_use_item(pokemon, parameter, item)
    when :trade
      return !!method_data.call_on_trade(
        pokemon, parameter, other_pokemon
      )
    end
    return false
  end

  def self.resolve_generated_evolution_conflict(pokemon, context, eligible)
    ordered = eligible.sort_by { |entry| entry[:branch][:identity] }
    return ordered[0] if ordered.length == 1
    identities = ordered.map { |entry| entry[:branch][:identity] }
    value = evolution_conflict_value(
      $PokemonGlobal.ironmon_seed, pokemon.species_data.id,
      pokemon.personalID, context, *identities
    )
    return ordered[value % ordered.length]
  end

  def self.effective_evolution_method_order(method)
    indexes = method[:original_methods].map do |original|
      original[:source_index]
    end
    index = indexes.empty? ? 2_147_483_647 : indexes.min
    return [index, method[:method].to_s, method[:parameter].to_s]
  end

  def self.evolution_conflict_value(*parts)
    value = EVOLUTION_CONFLICT_FNV_OFFSET_BASIS
    values = [
      NormalEvolutionGenerator::SCHEMA_VERSION,
      NormalEvolutionGenerator::RULES_VERSION, "evolution_conflict",
      *parts
    ]
    values.each do |entry|
      entry.to_s.each_byte do |byte|
        value ^= byte
        value = (value * EVOLUTION_CONFLICT_FNV_PRIME) &
          EVOLUTION_CONFLICT_FNV_MASK
      end
      value ^= 0
      value = (value * EVOLUTION_CONFLICT_FNV_PRIME) &
        EVOLUTION_CONFLICT_FNV_MASK
    end
    return value
  end

  def self.pending_generated_evolutions
    @pending_generated_evolutions ||= {}
    return @pending_generated_evolutions
  end

  def self.pending_generated_evolution(pokemon)
    pending = pending_generated_evolutions[pokemon.object_id]
    return nil if !pending || !pending[:pokemon].equal?(pokemon)
    return pending
  end

  def self.clear_pending_generated_evolution(pokemon)
    return if !pokemon
    pending_generated_evolutions.delete(pokemon.object_id)
  end

  def self.clear_pending_generated_evolutions
    @pending_generated_evolutions = {}
    @committed_generated_evolutions = {}
  end

  def self.committed_generated_evolutions
    @committed_generated_evolutions ||= {}
    return @committed_generated_evolutions
  end

  def self.committed_generated_evolution(pokemon)
    committed = committed_generated_evolutions[pokemon.object_id]
    return nil if !committed || !committed[:pokemon].equal?(pokemon)
    return committed
  end

  def self.clear_committed_generated_evolution(pokemon)
    return if !pokemon
    committed_generated_evolutions.delete(pokemon.object_id)
  end

  def self.clear_generated_evolution_runtime_state(pokemon)
    clear_pending_generated_evolution(pokemon)
    clear_committed_generated_evolution(pokemon)
  end

  def self.apply_generated_evolution_after_effects(pokemon, new_species)
    pending = pending_generated_evolution(pokemon)
    return false if !pending
    target = GameData::Species.try_get(new_species)
    matches = target && pending[:target_id] == target.id &&
      pending[:source] == pokemon.species_data.id.to_s
    if !matches
      clear_pending_generated_evolution(pokemon)
      return false
    end
    begin
      if pending[:method]
        method_data = GameData::Evolution.get(pending[:method])
        method_data.call_after_evolution(
          pokemon, pending[:target_id], pending[:parameter], target.id
        )
      end
      apply_generated_shedinja_duplicate(pokemon, pending, target.id)
      record_generated_evolution_outcome(pending, "completed") if
        respond_to?(:record_generated_evolution_outcome)
      experience = generated_evolution_experience_state(pokemon)
      committed_generated_evolutions[pokemon.object_id] = {
        :pokemon => pokemon,
        :source => pending[:source],
        :target_id => pending[:target_id],
        :level => experience[:level],
        :progress_numerator => experience[:progress_numerator],
        :progress_denominator => experience[:progress_denominator]
      }
      pokemon.preEvolved_pif_sprite = nil
      return true
    ensure
      clear_pending_generated_evolution(pokemon)
    end
  end

  def self.apply_generated_shedinja_duplicate(pokemon, pending, evolved_species)
    branches = if pending[:source_kind] == :fusion
                 fusion_evolution_generator.branches_for(pending[:source])
               else
                 evolution_generator.branches_for(pending[:source])
               end
    primary = branches.find do |branch|
      branch[:identity] == pending[:branch_identity]
    end
    return false if !primary || !branch_has_effective_method?(primary, :Ninjask)
    duplicate = branches.find do |branch|
      same_side = pending[:source_kind] != :fusion ||
        branch[:component_side] == pending[:component_side]
      same_side && branch_has_effective_method?(branch, :Shedinja)
    end
    return false if !duplicate
    method = duplicate[:effective_methods].find do |entry|
      entry[:method] == :Shedinja
    end
    GameData::Evolution.get(:Shedinja).call_after_evolution(
      pokemon, duplicate[:target_id], method[:parameter], evolved_species
    )
    record_generated_evolution_duplicate(pending, duplicate) if
      respond_to?(:record_generated_evolution_duplicate)
    return true
  end

  def self.branch_has_effective_method?(branch, method)
    return branch[:effective_methods].any? do |entry|
      entry[:method] == method
    end
  end

  def self.generated_evolution_experience_state(pokemon)
    level = pokemon.level
    growth = pokemon.growth_rate
    maximum_level = GameData::GrowthRate.max_level
    if level >= maximum_level
      return {
        :level => level,
        :progress_numerator => 0,
        :progress_denominator => 1
      }
    end
    minimum = growth.minimum_exp_for_level(level)
    next_minimum = growth.minimum_exp_for_level(level + 1)
    denominator = [next_minimum - minimum, 1].max
    numerator = (pokemon.exp - minimum).clamp(0, denominator - 1)
    return {
      :level => level,
      :progress_numerator => numerator,
      :progress_denominator => denominator
    }
  end

  def self.restore_generated_evolution_experience(pokemon, committed)
    level = committed[:level]
    growth = pokemon.growth_rate
    minimum = growth.minimum_exp_for_level(level)
    if level >= GameData::GrowthRate.max_level
      rebased_exp = minimum
    else
      next_minimum = growth.minimum_exp_for_level(level + 1)
      span = [next_minimum - minimum, 1].max
      rebased_progress =
        (span * committed[:progress_numerator]) /
        committed[:progress_denominator]
      rebased_exp = minimum + rebased_progress
    end
    pokemon.instance_variable_set(:@exp, rebased_exp)
    pokemon.instance_variable_set(:@level, level)
    pokemon.calc_stats
    return rebased_exp
  end

  def self.shedinja_only_branch?(branch)
    methods = branch[:effective_methods].map { |entry| entry[:method] }.uniq
    return methods == [:Shedinja]
  end

  def self.story_generated_evolution_target(species, context = :story_progression, component_side = nil)
    return nil if !evolution_randomization_active?
    species_data = GameData::Species.try_get(species)
    if normal_evolution_runtime_species?(species_data)
      branches = evolution_generator.branches_for(species_data)
    elsif fusion_evolution_runtime_species?(species_data)
      branches = fusion_evolution_generator.branches_for(species_data)
      if component_side
        preferred = branches.select do |branch|
          branch[:component_side] == component_side
        end
        branches = preferred if !preferred.empty?
      end
    else
      return nil
    end
    branches = branches.reject do |branch|
      shedinja_only_branch?(branch)
    end
    return nil if branches.empty?
    ordered = branches.sort_by { |branch| branch[:identity] }
    value = evolution_conflict_value(
      $PokemonGlobal.ironmon_seed, species_data.id, 0, context,
      *ordered.map { |branch| branch[:identity] }
    )
    return ordered[value % ordered.length][:target_id]
  end
end

class Pokemon
  alias ironmon_evolution_original_check_evolution_on_level_up check_evolution_on_level_up
  def check_evolution_on_level_up(prompt_choice = true)
    if Ironmon.evolution_randomization_active? &&
       Ironmon.generated_evolution_runtime_species?(species_data)
      if Ironmon.fusion_evolution_runtime_species?(species_data)
        return Ironmon.select_generated_fusion_evolution(self, :level_up)
      end
      return Ironmon.select_generated_normal_evolution(self, :level_up)
    end
    Ironmon.clear_pending_generated_evolution(self)
    return ironmon_evolution_original_check_evolution_on_level_up(prompt_choice)
  end

  alias ironmon_evolution_original_check_evolution_on_use_item check_evolution_on_use_item
  def check_evolution_on_use_item(item_used)
    if Ironmon.evolution_randomization_active? &&
       Ironmon.generated_evolution_runtime_species?(species_data)
      if Ironmon.fusion_evolution_runtime_species?(species_data)
        return Ironmon.select_generated_fusion_evolution(
          self, :use_item, item_used
        )
      end
      return Ironmon.select_generated_normal_evolution(
        self, :use_item, item_used
      )
    end
    Ironmon.clear_pending_generated_evolution(self)
    return ironmon_evolution_original_check_evolution_on_use_item(item_used)
  end

  alias ironmon_evolution_original_check_evolution_on_trade check_evolution_on_trade
  def check_evolution_on_trade(other_pokemon)
    if Ironmon.evolution_randomization_active? &&
       Ironmon.generated_evolution_runtime_species?(species_data)
      if Ironmon.fusion_evolution_runtime_species?(species_data)
        return Ironmon.select_generated_fusion_evolution(
          self, :trade, nil, other_pokemon
        )
      end
      return Ironmon.select_generated_normal_evolution(
        self, :trade, nil, other_pokemon
      )
    end
    Ironmon.clear_pending_generated_evolution(self)
    return ironmon_evolution_original_check_evolution_on_trade(other_pokemon)
  end

  alias ironmon_evolution_original_action_after_evolution action_after_evolution
  def action_after_evolution(new_species)
    if Ironmon.evolution_randomization_active? &&
       Ironmon.generated_evolution_runtime_species?(species_data)
      Ironmon.apply_generated_evolution_after_effects(self, new_species)
      return
    end
    return ironmon_evolution_original_action_after_evolution(new_species)
  end

  alias ironmon_evolution_original_species= species=
  def species=(species_id)
    pending = Ironmon.pending_generated_evolution(self)
    if pending
      target = GameData::Species.try_get(species_id)
      if target && target.id == pending[:target_id]
        action_after_evolution(target.id)
      else
        Ironmon.clear_generated_evolution_runtime_state(self)
      end
    end
    committed = Ironmon.committed_generated_evolution(self)
    target = GameData::Species.try_get(species_id)
    matches = committed && target && target.id == committed[:target_id] &&
      species_data.id.to_s == committed[:source]
      self.ironmon_evolution_original_species = species_id
      if matches
        Ironmon.restore_generated_evolution_experience(self, committed)
      end
    Ironmon.clear_committed_generated_evolution(self)
  end

  alias ironmon_evolution_original_evolve_from_party= evolve_from_party=
  def evolve_from_party=(value)
    Ironmon.clear_generated_evolution_runtime_state(self) if value
    self.ironmon_evolution_original_evolve_from_party = value
  end
end

alias ironmon_evolution_original_force_evolution pbForceEvo
def pbForceEvo(pokemon)
  if Ironmon.evolution_randomization_active? &&
     Ironmon.generated_evolution_runtime_species?(pokemon.species_data)
    new_species = if Ironmon.fusion_evolution_runtime_species?(pokemon.species_data)
                    Ironmon.select_generated_fusion_evolution(
                      pokemon, :forced_item, nil, nil, true
                    )
                  else
                    Ironmon.select_generated_normal_evolution(
                      pokemon, :forced_item, nil, nil, true
                    )
                  end
    return false if !new_species
    evolution = PokemonEvolutionScene.new
    evolution.pbStartScreen(pokemon, new_species)
    evolution.pbEvolution
    evolution.pbEndScreen
    return true
  end
  return ironmon_evolution_original_force_evolution(pokemon)
end

alias ironmon_evolution_original_story_get_evolution getEvolution
def getEvolution(species_parameter, half_to_evolve = nil)
  species_data = GameData::Species.try_get(species_parameter)
  if Ironmon.evolution_randomization_active? &&
     Ironmon.generated_evolution_runtime_species?(species_data)
    component_side = if half_to_evolve == :HEAD
                       :head
                     elsif half_to_evolve == :BODY
                       :body
                     end
    target = Ironmon.story_generated_evolution_target(
      species_data, [:story_progression, half_to_evolve], component_side
    )
    target_data = GameData::Species.try_get(target)
    return target_data ? target_data.id_number : species_data.id_number
  end
  return ironmon_evolution_original_story_get_evolution(
    species_parameter, half_to_evolve
  )
end
