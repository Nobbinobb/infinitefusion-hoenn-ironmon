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
    if legacy_evolution_metadata_version(*metadata)
      tracker_upgrade_evolution_recipe(recipe)
      return true
    end
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

  def self.tracker_upgrade_evolution_recipe(recipe)
    catalog = evolution_catalog
    fusion_pool = custom_fusion_pool_info
    recipe["evolution_generator_version"] =
      NormalEvolutionGenerator::SCHEMA_VERSION
    recipe["evolution_rules_version"] = NormalEvolutionGenerator::RULES_VERSION
    recipe["evolution_source_fingerprint"] = catalog.source_fingerprint
    recipe["evolution_taxonomy_fingerprint"] = catalog.taxonomy_fingerprint
    recipe["evolution_method_fingerprint"] = catalog.method_fingerprint
    recipe["evolution_target_fingerprint"] = catalog.normal_target_fingerprint
    recipe["evolution_base_stat_generator_version"] =
      BaseStatGenerator::SCHEMA_VERSION
    recipe["evolution_base_stat_source_fingerprint"] =
      base_stat_source_fingerprint
    recipe["fusion_evolution_generator_version"] =
      FusionEvolutionGenerator::SCHEMA_VERSION
    recipe["fusion_evolution_rules_version"] =
      FusionEvolutionGenerator::RULES_VERSION
    recipe["fusion_evolution_target_pool_version"] = fusion_pool[:schema_version]
    recipe["fusion_evolution_target_pool_size"] = fusion_pool[:size]
    recipe["fusion_evolution_target_pool_fingerprint"] =
      fusion_pool[:fingerprint]
  end

  def self.tracker_lookup_evolution_targets(species, recipe)
    empty = { :normal => [], :head => [], :body => [] }
    return empty if !tracker_evolution_recipe?(recipe)
    branches = if normal_evolution_runtime_species?(species)
                 tracker_normal_evolution_generator(recipe).branches_for(species)
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
      result[group] << tracker_evolution_target_snapshot(branch)
    end
    return result
  end

  def self.tracker_lookup_evolution_predecessors(species, recipe)
    return [] if !normal_evolution_runtime_species?(species)
    return [] if !tracker_evolution_recipe?(recipe)
    generator = tracker_normal_evolution_generator(recipe)
    predecessors = []
    generator.graph.each_value do |branches|
      branches.each do |branch|
        next if branch[:target] != species.id.to_s
        predecessors << tracker_evolution_predecessor_snapshot(branch)
      end
    end
    return predecessors.sort_by { |entry| entry["species_id"] }
  end

  def self.tracker_lookup_evolution_predecessor_page(species, recipe, offset, limit)
    return tracker_empty_evolution_predecessor_page(offset, limit) if
      !tracker_evolution_recipe?(recipe)
    if normal_evolution_runtime_species?(species)
      predecessors = tracker_lookup_evolution_predecessors(species, recipe)
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
    page = tracker_fusion_evolution_generator(recipe).predecessor_page_for(
      species, offset, limit
    )
    return {
      "matches" => page[:branches].map do |branch|
        tracker_evolution_predecessor_snapshot(branch)
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

  def self.tracker_evolution_predecessor_snapshot(branch)
    source = GameData::Species.get(branch[:source].to_sym)
    return {
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
  end

  def self.tracker_evolution_target_snapshot(branch)
    target = GameData::Species.get(branch[:target_id])
    return {
      "species_id" => "#{target.id}:0",
      "species_name" => target.name,
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

  def self.tracker_evolution_candidate_snapshot(target, name)
    species = GameData::Species.get(target[:target_id])
    return {
      "species_id" => "#{species.id}:0",
      "species_name" => name,
      "sprite_path" => tracker_lookup_sprite_path(species),
      "base_stat_total" => target[:target_bst]
    }
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

  def self.tracker_lookup_evolutions(species)
    relations = species.get_evolutions(true).map do |evolution|
      evolved_species = GameData::Species.try_get(evolution[0])
      next if !evolved_species || !tracker_lookup_species_available?(evolved_species)
      requirement = tracker_evolution_snapshot(evolution[1], evolution[2])["requirement"]
      tracker_lookup_relation(evolved_species, requirement)
    end.compact
    return relations.sort_by { |relation| [relation["label"], relation["species_name"]] }
  end

  def self.tracker_lookup_previous_evolutions(species)
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
      tracker_lookup_relation(previous_species, requirement)
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
