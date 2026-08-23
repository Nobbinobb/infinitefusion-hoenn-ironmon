#===============================================================================
# Ironmon completed-run fusion lookup, caches, and runtime integration
#===============================================================================

module Ironmon
  def self.tracker_lookup_fusion_bases(species)
    return [] if !species.is_a?(GameData::FusedSpecies)
    return [
      tracker_lookup_relation(species.body_pokemon, "Body component"),
      tracker_lookup_relation(species.head_pokemon, "Head component")
    ]
  end

  def self.tracker_lookup_reverse_fusion(species, mapper)
    return nil if !species.is_a?(GameData::FusedSpecies)
    reverse = GameData::Species.get(mapper.paired_species(species.id))
    return tracker_lookup_relation(reverse, "Ironmon reverse")
  end

  def self.tracker_lookup_fusion_materials(species, mapper, offset, limit)
    return { "matches" => [], "total" => 0 } if
      !species.is_a?(GameData::FusedSpecies)
    pairs = mapper.material_pairs_for(species.id)
    matches = (pairs.slice(offset, limit) || []).map do |body_id, head_id|
      body = GameData::Species.get(body_id)
      head = GameData::Species.get(head_id)
      {
        "body" => tracker_lookup_relation(body, "Body material"),
        "head" => tracker_lookup_relation(head, "Head material")
      }
    end
    return { "matches" => matches, "total" => pairs.length }
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
      recipe["player_fusion_generator_version"], work_checkpoint, true
    )
  end

  def self.tracker_obtainability_fusion_evolution_generator(
    recipe, work_checkpoint = nil
  )
    stat_generator = if tracker_loaded_recipe?(recipe)
                       base_stat_generator
                     else
                       base_stat_generator_for(
                         recipe["seed"],
                         recipe["evolution_base_stat_source_fingerprint"]
                       )
                     end
    return FusionEvolutionGenerator.new(
      recipe["seed"], evolution_catalog, custom_fusion_pool,
      custom_fusion_pool_info, stat_generator, work_checkpoint
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

  def self.tracker_lookup_relation(species, label)
    return {
      "species_id" => "#{species.id}:0",
      "species_name" => species.name,
      "sprite_path" => tracker_lookup_sprite_path(species),
      "label" => label
    }
  end

  def self.tracker_lookup_sprite_path(species)
    cached = tracker_sprite_paths[species.id]
    return cached if tracker_sprite_paths.key?(species.id)
    loader = BattleSpriteLoader.new
    pif_sprite = loader.get_pif_sprite_from_species(species.id)
    path = loader.check_for_local_sprite(pif_sprite)
    tracker_sprite_paths[species.id] = path ? path.tr("\\", "/") : nil
    return tracker_sprite_paths[species.id]
  rescue Exception
    tracker_sprite_paths[species.id] = nil if species
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

  def self.tracker_wild_fusion_tables
    @tracker_wild_fusion_tables ||= {}
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
    @tracker_lookup_cache = nil
    @tracker_wild_fusion_tables = nil
    @tracker_fusion_mappers = nil
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
  Ironmon.complete_run(:lost) if [2, 5].include?(decision)
end

alias ironmon_tracker_original_hall_of_fame_entry pbHallOfFameEntry
def pbHallOfFameEntry
  Ironmon.complete_run(:won)
  return ironmon_tracker_original_hall_of_fame_entry
end
