#===============================================================================
# Ironmon completed-run evolution reconstruction
#===============================================================================

module Ironmon
  EVOLUTION_GRAPH_FUSION_LEVELS = {
    [0, 0] => 8,
    [0, 1] => 0,
    [1, 1] => 1,
    [1, 2] => 2,
    [1, 3] => 3,
    [0, 2] => 4,
    [2, 2] => 5,
    [2, 3] => 6,
    [0, 3] => 7,
    [3, 3] => 8
  }

  def self.tracker_evolution_recipe?(recipe)
    return recipe["evolution_generator_version"] ==
      NormalEvolutionGenerator::SCHEMA_VERSION
  end

  def self.tracker_validate_evolution_recipe(recipe)
    metadata = [
      recipe["evolution_generator_version"],
      recipe["evolution_rules_version"],
      recipe["evolution_source_fingerprint"],
      recipe["evolution_taxonomy_fingerprint"],
      recipe["evolution_method_fingerprint"],
      recipe["evolution_target_fingerprint"],
      recipe["evolution_base_stat_generator_version"],
      recipe["evolution_base_stat_source_fingerprint"],
      recipe["fusion_evolution_generator_version"],
      recipe["fusion_evolution_rules_version"],
      recipe["fusion_evolution_target_pool_version"],
      recipe["fusion_evolution_target_pool_size"],
      recipe["fusion_evolution_target_pool_fingerprint"]
    ]
    return true if metadata.compact.empty?
    catalog = evolution_catalog
    fusion_pool = custom_fusion_pool_info
    expected = [
      NormalEvolutionGenerator::SCHEMA_VERSION,
      NormalEvolutionGenerator::RULES_VERSION,
      catalog.source_fingerprint,
      catalog.taxonomy_fingerprint,
      catalog.method_fingerprint,
      catalog.normal_target_fingerprint,
      BaseStatGenerator::SCHEMA_VERSION,
      base_stat_source_fingerprint,
      FusionEvolutionGenerator::SCHEMA_VERSION,
      FusionEvolutionGenerator::RULES_VERSION,
      fusion_pool[:schema_version],
      fusion_pool[:size],
      fusion_pool[:fingerprint]
    ]
    if metadata != expected
      raise TrackerLookupError.new(
        "incompatible_evolutions",
        "The evolution source data no longer matches this run."
      )
    end
    return true
  end

  def self.tracker_lookup_evolution_targets(
    species, recipe, obtainability = true, assignments = nil
  )
    empty = { :normal => [], :head => [], :body => [] }
    return empty if !tracker_evolution_recipe?(recipe)
    branches = if normal_evolution_runtime_species?(species)
                 tracker_normal_evolution_generator(recipe).branches_for(species)
               elsif fusion_evolution_runtime_species?(species) &&
                     assignments.is_a?(Array)
                 tracker_fusion_evolution_targets_for_assignments(
                   species, assignments
                 )
               elsif fusion_evolution_runtime_species?(species)
                 tracker_fusion_evolution_generator(recipe).branches_for(species)
               else
                 []
               end
    result = { :normal => [], :head => [], :body => [] }
    seen = { :normal => {}, :head => {}, :body => {} }
    branches.each do |branch|
      group = branch[:component_side] || :normal
      next if seen[group][branch[:target]]
      seen[group][branch[:target]] = true
      result[group] << tracker_evolution_target_snapshot(
        branch, recipe, obtainability
      )
    end
    return result
  end

  def self.tracker_lookup_evolution_predecessors(
    species, recipe, obtainability = true
  )
    return [] if !normal_evolution_runtime_species?(species)
    return [] if !tracker_evolution_recipe?(recipe)
    generator = tracker_normal_evolution_generator(recipe)
    predecessors = []
    generator.graph.each_value do |branches|
      branches.each do |branch|
        next if branch[:target] != species.id.to_s
        predecessors << tracker_evolution_predecessor_snapshot(
          branch, recipe, obtainability
        )
      end
    end
    return predecessors.sort_by { |entry| entry["species_id"] }
  end

  def self.tracker_lookup_evolution_predecessor_page(
    species, recipe, offset, limit, obtainability = true,
    predecessor_assignments = nil
  )
    return tracker_empty_evolution_predecessor_page(offset, limit) if
      !tracker_evolution_recipe?(recipe)
    if normal_evolution_runtime_species?(species)
      predecessors = tracker_lookup_evolution_predecessors(
        species, recipe, obtainability
      )
      matches = predecessors.slice(offset, limit) || []
      continuation = offset + limit < predecessors.length ?
        "available" : "complete"
      return {
        "matches" => matches,
        "offset" => offset,
        "limit" => limit,
        "continuation" => continuation,
        "next_offset" => continuation == "complete" ? nil : offset + limit
      }
    end
    return tracker_empty_evolution_predecessor_page(offset, limit) if
      !fusion_evolution_runtime_species?(species)
    generator = tracker_fusion_evolution_generator(recipe)
    page = if predecessor_assignments.is_a?(Array)
             generator.predecessor_page_for_assignments(
               species, predecessor_assignments, offset, limit
             )
           else
             generator.predecessor_page_for(species, offset, limit)
           end
    return {
      "matches" => page[:branches].map do |branch|
        tracker_evolution_predecessor_snapshot(
          branch, recipe, obtainability
        )
      end,
      "offset" => page[:offset],
      "limit" => page[:limit],
      "continuation" => page[:continuation].to_s,
      "next_offset" => page[:next_offset]
    }
  end

  def self.tracker_empty_evolution_predecessor_page(offset, limit)
    return {
      "matches" => [],
      "offset" => offset,
      "limit" => limit,
      "continuation" => "complete",
      "next_offset" => nil
    }
  end

  def self.tracker_evolution_predecessor_snapshot(
    branch, recipe = nil, obtainability = false
  )
    source = GameData::Species.get(branch[:source].to_sym)
    result = {
      "species_id" => "#{source.id}:0",
      "species_name" => source.name,
      "sprite_path" => tracker_lookup_sprite_path(source),
      "base_stat_total" => branch[:source_bst],
      "stage_level" => tracker_evolution_graph_level(source),
      "component_side" => (branch[:component_side] || :normal).to_s,
      "effective_methods" => branch[:effective_methods].map do |method|
        tracker_evolution_snapshot(
          method[:method], method[:parameter]
        )["requirement"]
      end
    }
    if recipe && obtainability
      result["obtainability_status"] =
        tracker_obtainability_status(source, recipe)
    end
    return result
  end

  def self.tracker_fusion_evolution_targets_for_assignments(
    species, assignments
  )
    branches_by_identity = {}
    evolution_catalog.branch_catalog.each do |branch|
      branches_by_identity[branch[:identity]] = branch
    end
    result = []
    assignments.each do |assignment|
      next if !assignment.is_a?(Hash)
      target = tracker_fusion_species_for_numeric_id(
        assignment["target_id"].to_i
      )
      next if !target
      side = assignment["component_side"].to_s
      component_side = side == "head" ? :head : side == "body" ? :body : nil
      next if !component_side
      component_branch = branches_by_identity[
        assignment["component_branch_identity"].to_s
      ]
      next if !component_branch
      component = component_side == :body ?
        species.body_pokemon : species.head_pokemon
      next if component_branch[:source] != component.id.to_s
      result << {
        :identity => "#{species.id}|#{component_side}|#{component_branch[:identity]}",
        :source => species.id.to_s,
        :component_side => component_side,
        :component_source => component.id.to_s,
        :component_branch_identity => component_branch[:identity],
        :original_destination => component_branch[:original_destination],
        :target => target.id.to_s,
        :target_id => target.id,
        :target_bst => assignment["target_base_stat_total"].to_i,
        :effective_methods => component_branch[:effective_methods]
      }
    end
    return result
  end

  def self.tracker_fusion_evolution_candidates_for_assignments(packed)
    decoded = packed.to_s.unpack("m0")[0]
    return [] if !decoded || decoded.bytesize % 4 != 0
    result = {}
    decoded.unpack("L<*").each do |assignment|
      target_identity = tracker_fusion_identity_for_numeric_id(
        assignment >> 11
      )
      next if !target_identity
      result[target_identity] = {
        :target => target_identity.to_s,
        :target_id => target_identity,
        :target_bst => assignment & 0x7FF
      }
    end
    return result.values.sort_by { |target| target[:target] }
  end

  def self.tracker_fusion_species_for_numeric_id(numeric_id)
    identity = tracker_fusion_identity_for_numeric_id(numeric_id)
    return identity ? GameData::Species.try_get(identity) : nil
  end

  def self.tracker_fusion_identity_for_numeric_id(numeric_id)
    return nil if numeric_id <= NB_POKEMON ||
      numeric_id > (NB_POKEMON * NB_POKEMON) + NB_POKEMON
    body_id = (numeric_id - 1) / NB_POKEMON
    head_id = numeric_id - (body_id * NB_POKEMON)
    identity = "B#{body_id}H#{head_id}".to_sym
    return nil if !custom_fusion_species?(identity)
    return identity
  end

  def self.tracker_evolution_target_snapshot(
    branch, recipe = nil, obtainability = false
  )
    target = GameData::Species.get(branch[:target_id])
    result = {
      "species_id" => "#{target.id}:0",
      "species_name" => tracker_species_display_name(target),
      "sprite_path" => tracker_lookup_sprite_path(target),
      "base_stat_total" => branch[:target_bst],
      "stage_level" => tracker_evolution_graph_level(target),
      "component_side" => (branch[:component_side] || :normal).to_s,
      "effective_methods" => branch[:effective_methods].map do |method|
        tracker_evolution_snapshot(
          method[:method], method[:parameter]
        )["requirement"]
      end
    }
    if recipe && obtainability
      result["obtainability_status"] =
        tracker_obtainability_status(target, recipe)
    end
    return result
  end

  def self.tracker_evolution_graph_level(species)
    if species.is_a?(GameData::FusedSpecies)
      levels = [
        tracker_evolution_component_level(species.body_pokemon),
        tracker_evolution_component_level(species.head_pokemon)
      ].sort
      return EVOLUTION_GRAPH_FUSION_LEVELS[levels] || 8
    end
    level = tracker_evolution_component_level(species)
    return level == 0 ? 3 : level
  end

  def self.tracker_evolution_component_level(species)
    @tracker_evolution_taxonomy_roles ||= evolution_catalog.taxonomy_catalog.each_with_object({}) do |entry, roles|
      roles[entry[:identity]] = entry[:role]
    end
    identity = species.id.to_s
    role = @tracker_evolution_taxonomy_roles[identity] || :standalone
    return {
      :standalone => 0,
      :first_stage => 1,
      :intermediate => 2,
      :final => 3
    }[role] || 0
  end

  def self.tracker_evolution_candidates_for(species, recipe, side)
    if normal_evolution_runtime_species?(species)
      return [] if side != :normal
      return tracker_normal_evolution_generator(recipe).candidate_targets_for(
        species
      )
    end
    return [] if !fusion_evolution_runtime_species?(species) || side == :normal
    return tracker_fusion_evolution_generator(recipe).candidate_targets_for(
      species
    )[side]
  end

  def self.tracker_evolution_candidate_snapshot(
    target, name, recipe = nil, obtainability = false
  )
    species = GameData::Species.get(target[:target_id])
    result = {
      "species_id" => "#{species.id}:0",
      "species_name" => name,
      "sprite_path" => tracker_lookup_sprite_path(species),
      "base_stat_total" => target[:target_bst]
    }
    if recipe && obtainability
      result["obtainability_status"] =
        tracker_obtainability_status(species, recipe)
    end
    return result
  end

  def self.tracker_normal_evolution_generator(recipe)
    return evolution_generator if tracker_loaded_recipe?(recipe)
    @tracker_evolution_generators ||= {}
    key = [
      recipe["seed"], recipe["evolution_source_fingerprint"],
      recipe["evolution_taxonomy_fingerprint"],
      recipe["evolution_method_fingerprint"],
      recipe["evolution_target_fingerprint"],
      recipe["evolution_base_stat_generator_version"],
      recipe["evolution_base_stat_source_fingerprint"]
    ]
    cached = @tracker_evolution_generators[key]
    return cached if cached
    generator = evolution_generator_for(
      recipe["seed"], recipe["evolution_source_fingerprint"],
      recipe["evolution_taxonomy_fingerprint"],
      recipe["evolution_method_fingerprint"],
      recipe["evolution_target_fingerprint"],
      recipe["evolution_base_stat_generator_version"],
      recipe["evolution_base_stat_source_fingerprint"]
    )
    tracker_store_bounded(@tracker_evolution_generators, key, generator, 16)
    return generator
  end

  def self.tracker_fusion_evolution_generator(recipe)
    return fusion_evolution_generator if tracker_loaded_recipe?(recipe)
    @tracker_fusion_evolution_generators ||= {}
    key = [
      recipe["seed"], recipe["evolution_source_fingerprint"],
      recipe["evolution_taxonomy_fingerprint"],
      recipe["evolution_method_fingerprint"],
      recipe["fusion_evolution_generator_version"],
      recipe["fusion_evolution_rules_version"],
      recipe["fusion_evolution_target_pool_version"],
      recipe["fusion_evolution_target_pool_size"],
      recipe["fusion_evolution_target_pool_fingerprint"],
      recipe["evolution_base_stat_source_fingerprint"]
    ]
    cached = @tracker_fusion_evolution_generators[key]
    return cached if cached
    generator = FusionEvolutionGenerator.new(
      recipe["seed"], evolution_catalog, custom_fusion_pool,
      custom_fusion_pool_info,
      base_stat_generator_for(
        recipe["seed"], recipe["evolution_base_stat_source_fingerprint"]
      )
    )
    tracker_store_bounded(
      @tracker_fusion_evolution_generators, key, generator, 16
    )
    return generator
  end

  def self.tracker_lookup_evolutions(
    species, recipe = nil, obtainability = false
  )
    relations = species.get_evolutions(true).map do |evolution|
      evolved_species = GameData::Species.try_get(evolution[0])
      next if !evolved_species || !tracker_lookup_species_available?(evolved_species)
      requirement = tracker_evolution_snapshot(evolution[1], evolution[2])["requirement"]
      tracker_lookup_relation(
        evolved_species, requirement, recipe, obtainability
      )
    end.compact
    return relations.sort_by { |relation| [relation["label"], relation["species_name"]] }
  end

  def self.tracker_lookup_previous_evolutions(
    species, recipe = nil, obtainability = false
  )
    candidates = if species.is_a?(GameData::FusedSpecies)
                   tracker_lookup_previous_fusions(species)
                 else
                   previous_id = species.send(:get_previous_species)
                   previous_id == species.id ? [] : [GameData::Species.try_get(previous_id)]
                 end
    relations = candidates.compact.map do |previous_species|
      evolution = previous_species.get_evolutions(true).find do |entry|
        evolved_species = GameData::Species.try_get(entry[0])
        evolved_species && evolved_species.id == species.id
      end
      next if !evolution
      requirement = tracker_evolution_snapshot(evolution[1], evolution[2])["requirement"]
      tracker_lookup_relation(
        previous_species, requirement, recipe, obtainability
      )
    end.compact
    return relations.sort_by { |relation| relation["species_name"] }
  end

  def self.tracker_lookup_previous_fusions(species)
    candidates = []
    body_previous = species.body_pokemon.send(:get_previous_species)
    if body_previous != species.body_pokemon.id
      fusion_id = getFusedPokemonIdFromSymbols(body_previous, species.head_pokemon.id)
      candidates << GameData::Species.try_get(fusion_id)
    end
    head_previous = species.head_pokemon.send(:get_previous_species)
    if head_previous != species.head_pokemon.id
      fusion_id = getFusedPokemonIdFromSymbols(species.body_pokemon.id, head_previous)
      candidates << GameData::Species.try_get(fusion_id)
    end
    return candidates.compact.uniq { |candidate| candidate.id }
  end
end
