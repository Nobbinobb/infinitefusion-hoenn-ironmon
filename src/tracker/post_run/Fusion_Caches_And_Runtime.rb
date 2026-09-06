#===============================================================================
# Ironmon completed-run fusion lookup, caches, and runtime integration
#===============================================================================

module Ironmon
  def self.tracker_lookup_fusion_bases(species, recipe = nil,
                                       obtainability = false)
    return [] if !species.is_a?(GameData::FusedSpecies)
    return [
      tracker_lookup_relation(
        species.body_pokemon, "Body component", recipe, obtainability
      ),
      tracker_lookup_relation(
        species.head_pokemon, "Head component", recipe, obtainability
      )
    ]
  end

  def self.tracker_lookup_reverse_fusion(species, recipe,
                                         obtainability = false)
    return nil if !species.is_a?(GameData::FusedSpecies)
    mapper = tracker_post_run_fusion_mapper(recipe)
    reverse = GameData::Species.get(mapper.paired_species(species.id))
    return tracker_lookup_relation(
      reverse, "Ironmon reverse", recipe, obtainability
    )
  end

  def self.tracker_lookup_fusion_materials(
    species, mapper, offset, limit, recipe = nil, obtainability = false
  )
    return { "matches" => [], "total" => 0 } if
      !species.is_a?(GameData::FusedSpecies)
    pairs = mapper.material_pairs_for(species.id)
    return tracker_format_fusion_material_pairs(
      pairs.slice(offset, limit) || [], pairs.length, recipe, obtainability
    )
  end

  def self.tracker_lookup_fusion_material_assignments(
    assignments, total, recipe = nil, obtainability = false
  )
    pairs = assignments.map do |assignment|
      [assignment["body_id"].to_i, assignment["head_id"].to_i]
    end
    return tracker_format_fusion_material_pairs(
      pairs, total, recipe, obtainability
    )
  end

  def self.tracker_format_fusion_material_pairs(
    pairs, total, recipe = nil, obtainability = false
  )
    matches = pairs.map do |body_id, head_id|
      body = GameData::Species.get(body_id)
      head = GameData::Species.get(head_id)
      {
        "body" => tracker_lookup_relation(
          body, "Body material", recipe, obtainability
        ),
        "head" => tracker_lookup_relation(
          head, "Head material", recipe, obtainability
        )
      }
    end
    return { "matches" => matches, "total" => total }
  end

  def self.tracker_post_run_fusion_mapper(recipe)
    return player_fusion_mapper if tracker_loaded_recipe?(recipe)
    run_id = recipe["run_id"]
    tracker_fusion_mappers[run_id] ||= PlayerFusionMapper.new(
      recipe["seed"], custom_fusion_pool, {}, {},
      base_stat_generator_for(
        recipe["seed"], recipe["base_stat_source_fingerprint"]
      ),
      recipe["player_fusion_generator_version"]
    )
    return tracker_fusion_mappers[run_id]
  end

  def self.tracker_obtainability_fusion_mapper(recipe, work_checkpoint = nil)
    stat_generator = if tracker_loaded_recipe?(recipe)
                       base_stat_generator
                     else
                       base_stat_generator_for(
                         recipe["seed"], recipe["base_stat_source_fingerprint"]
                       )
                     end
    return PlayerFusionMapper.new(
      recipe["seed"], custom_fusion_pool, {}, {},
      stat_generator,
      recipe["player_fusion_generator_version"], work_checkpoint
    )
  end

  def self.tracker_lookup_normal_species(species_id)
    species_key = species_id.to_s.split(":", 2)[0]
    species = GameData::Species.try_get(species_key.to_sym)
    if !species || species.id_number <= 0 || species.id_number > NB_POKEMON ||
       !normal_species_pool.include?(species.id)
      raise TrackerLookupError.new("pokemon_not_found", "The selected fusion material is not available in this run.")
    end
    return species
  end

  def self.tracker_lookup_relation(species, label, recipe = nil,
                                   obtainability = false)
    result = {
      "species_id" => "#{species.id}:0",
      "species_name" => tracker_species_display_name(species),
      "sprite_path" => tracker_lookup_sprite_path(species),
      "label" => label
    }
    if recipe && obtainability
      result["obtainability_status"] =
        tracker_obtainability_status(species, recipe)
    end
    return result
  end

  def self.tracker_lookup_sprite_path(species)
    cache_key = [active_generation_profile_id, species.id]
    cached = tracker_sprite_paths[cache_key]
    return cached if tracker_sprite_paths.key?(cache_key)
    loader = BattleSpriteLoader.new
    pif_sprite = loader.get_pif_sprite_from_species(species.id)
    tracker_sprite_paths[cache_key] = tracker_resolved_sprite_path(pif_sprite)
    return tracker_sprite_paths[cache_key]
  rescue Exception
    tracker_sprite_paths[cache_key] = nil if species && cache_key
    return nil
  end
  def self.tracker_search_indexes
    @tracker_search_indexes ||= {}
  end

  def self.tracker_search_result_cache
    @tracker_search_result_cache ||= {}
  end

  def self.tracker_lookup_cache
    @tracker_lookup_cache ||= {}
  end

  def self.tracker_fusion_mappers
    @tracker_fusion_mappers ||= {}
  end

  def self.tracker_sprite_paths
    @tracker_sprite_paths ||= {}
  end

  def self.tracker_store_bounded(cache, key, value, limit)
    cache.delete(key)
    cache.delete(cache.keys.first) if cache.length >= limit
    cache[key] = value
  end

  def self.reset_tracker_post_run_cache
    @tracker_search_indexes = nil
    @tracker_search_result_cache = nil
    @tracker_search_fusion_name_parts = nil
    @tracker_lookup_cache = nil
    @tracker_wild_occurrence_work = nil
    @tracker_wild_occurrence_sources = nil
    @tracker_fusion_mappers = nil
    @tracker_area_fusion_work = nil
    @tracker_sprite_paths = nil
    @tracker_evolution_generators = nil
    @tracker_fusion_evolution_generators = nil
    @tracker_trainer_location_index = nil
    @tracker_obtainability_services = nil
    @tracker_obtainability_ready_scene = nil
  end
end

Events.onEndBattle += proc do |_sender, event|
  decision = event[0]
  if [2, 5].include?(decision) && !Ironmon.tracker_auto_revive_enabled?
    Ironmon.complete_run(:lost)
  end
end

alias ironmon_tracker_original_hall_of_fame_entry pbHallOfFameEntry
def pbHallOfFameEntry
  Ironmon.complete_run(:won)
  return ironmon_tracker_original_hall_of_fame_entry
end
