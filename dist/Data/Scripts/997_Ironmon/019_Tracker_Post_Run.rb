#===============================================================================
# Ironmon tracker deterministic post-run inspection
#===============================================================================

module Ironmon
  TRACKER_SEARCH_LIMIT = 50

  class TrackerLookupError < StandardError
    attr_reader :code

    def initialize(code, message)
      @code = code
      super(message)
    end
  end

  def self.complete_tracker_run(result)
    return if !active? || !$PokemonGlobal
    return if $PokemonGlobal.ironmon_run_result
    $PokemonGlobal.ironmon_run_result = result.to_s
    recipe = tracker_completed_run_recipe
    tracker_connection.send_event("run_completed", recipe) if recipe
  rescue Exception => e
    echoln "Ironmon tracker could not complete the run recipe: #{e.message}"
  end

  def self.tracker_completed_run_recipe
    return nil if !active? || !$PokemonGlobal
    result = $PokemonGlobal.ironmon_run_result
    return nil if !result || result.to_s.empty?
    return {
      "run_id" => ensure_tracker_run_id,
      "seed" => $PokemonGlobal.ironmon_seed || 0,
      "result" => result.to_s,
      "game_version" => tracker_game_version,
      "ironmon_version" => VERSION,
      "configuration" => configuration_snapshot,
      "species_generator_version" => $PokemonGlobal.ironmon_species_generator_version,
      "ability_generator_version" => $PokemonGlobal.ironmon_ability_generator_version,
      "player_fusion_generator_version" => PlayerFusionMapper::SCHEMA_VERSION,
      "species_pool_fingerprint" => tracker_species_pool_fingerprint,
      "ability_pool_fingerprint" => $PokemonGlobal.ironmon_ability_pool_fingerprint,
      "fusion_pool_fingerprint" => $PokemonGlobal.ironmon_custom_fusion_pool_fingerprint
    }
  end

  def self.tracker_pokemon_search(payload, envelope_run_id)
    payload ||= {}
    query = payload["query"].to_s.strip
    raise TrackerLookupError.new("invalid_query", "Enter a Pokemon name to search.") if query.empty?
    offset = payload["offset"].to_i
    limit = payload["limit"].to_i
    limit = 20 if limit == 0
    raise TrackerLookupError.new("invalid_page", "Search offset must not be negative.") if offset < 0
    if limit < 1 || limit > TRACKER_SEARCH_LIMIT
      raise TrackerLookupError.new("invalid_page", "Search page size must be between 1 and 50.")
    end
    recipe = tracker_validate_completed_recipe(payload["recipe"], envelope_run_id)
    normalized_query = query.downcase
    normal_only = payload["normal_only"] == true
    matches = tracker_search_matches(recipe, normalized_query, normal_only)
    return {
      "matches" => matches.slice(offset, limit) || [],
      "total" => matches.length
    }
  end

  def self.tracker_pokemon_lookup(payload, envelope_run_id)
    payload ||= {}
    recipe = tracker_validate_completed_recipe(payload["recipe"], envelope_run_id)
    species_id = payload["species_id"].to_s
    species_key = species_id.split(":", 2)[0]
    species = GameData::Species.try_get(species_key.to_sym)
    if !species || !tracker_lookup_species_available?(species)
      raise TrackerLookupError.new("pokemon_not_found", "The selected Pokemon is not available in this run.")
    end

    cache_key = "#{recipe["run_id"]}|#{species.id}"
    cached = tracker_lookup_cache[cache_key]
    return cached if cached

    learnset = tracker_lookup_learnset(species)
    fusion_mapper = tracker_post_run_fusion_mapper(recipe)
    result = {
      "species_id" => "#{species.id}:0",
      "species_name" => species.name,
      "sprite_path" => tracker_lookup_sprite_path(species),
      "types" => species.types.map { |type| type.to_s },
      "base_stats" => tracker_lookup_base_stats(species),
      "base_stat_total" => species.base_stats.values.inject(0) { |sum, value| sum + value },
      "abilities" => tracker_lookup_abilities(species, recipe),
      "learnset" => learnset,
      "evolutions" => tracker_lookup_evolutions(species),
      "previous_evolutions" => tracker_lookup_previous_evolutions(species),
      "fusion_bases" => tracker_lookup_fusion_bases(species),
      "reverse_fusion" => tracker_lookup_reverse_fusion(species, fusion_mapper),
      "fusion_materials" => tracker_lookup_fusion_materials(species, fusion_mapper)
    }
    tracker_store_bounded(tracker_lookup_cache, cache_key, result, 256)
    return result
  end

  def self.tracker_fusion_preview(payload, envelope_run_id)
    payload ||= {}
    recipe = tracker_validate_completed_recipe(payload["recipe"], envelope_run_id)
    first = tracker_lookup_normal_species(payload["first_species_id"])
    second = tracker_lookup_normal_species(payload["second_species_id"])
    mapper = tracker_post_run_fusion_mapper(recipe)
    orientations = [[first, second], [second, first]].uniq do |pair|
      [pair[0].id, pair[1].id]
    end
    outcomes = orientations.map do |body, head|
      result = GameData::Species.get(mapper.species(body.id, head.id))
      {
        "body" => tracker_lookup_relation(body, "Body material"),
        "head" => tracker_lookup_relation(head, "Head material"),
        "result" => tracker_lookup_relation(result, "Ironmon result")
      }
    end
    return { "outcomes" => outcomes }
  end

  def self.tracker_validate_completed_recipe(recipe, envelope_run_id)
    if active? && $PokemonGlobal && !$PokemonGlobal.ironmon_run_result
      raise TrackerLookupError.new(
        "run_active",
        "Complete generated lookup is unavailable during an active run."
      )
    end
    if !recipe.is_a?(Hash) || recipe["run_id"].to_s.empty?
      raise TrackerLookupError.new("invalid_recipe", "The completed-run recipe is missing.")
    end
    if recipe["run_id"] != envelope_run_id
      raise TrackerLookupError.new("run_mismatch", "The request and recipe identify different runs.")
    end
    if recipe["result"].to_s == "active" || recipe["result"].to_s.empty?
      raise TrackerLookupError.new("run_active", "Complete generated lookup is unavailable during an active run.")
    end
    if recipe["species_generator_version"] != SpeciesGenerator::SCHEMA_VERSION
      raise TrackerLookupError.new("generator_unavailable", "The required species generator is unavailable.")
    end
    if recipe["ability_generator_version"] != AbilityGenerator::SCHEMA_VERSION
      raise TrackerLookupError.new("generator_unavailable", "The required ability generator is unavailable.")
    end
    if recipe["player_fusion_generator_version"] != PlayerFusionMapper::SCHEMA_VERSION
      raise TrackerLookupError.new("generator_unavailable", "The required player-fusion generator is unavailable.")
    end
    if recipe["species_pool_fingerprint"] != tracker_species_pool_fingerprint
      raise TrackerLookupError.new("incompatible_species_pool", "The normal Pokemon pool no longer matches this run.")
    end
    if recipe["ability_pool_fingerprint"] != ability_pool_fingerprint
      raise TrackerLookupError.new("incompatible_ability_pool", "The ability pool no longer matches this run.")
    end
    if recipe["fusion_pool_fingerprint"] != custom_fusion_pool_info[:fingerprint]
      raise TrackerLookupError.new("incompatible_fusion_pool", "The custom fusion pool no longer matches this run.")
    end
    return recipe
  end

  def self.tracker_species_pool_fingerprint
    value = SpeciesGenerator::FNV_OFFSET_BASIS
    normal_species_pool.each do |species|
      species.to_s.each_byte do |byte|
        value ^= byte
        value = (value * SpeciesGenerator::FNV_PRIME) & SpeciesGenerator::FNV_MASK
      end
      value ^= 0
      value = (value * SpeciesGenerator::FNV_PRIME) & SpeciesGenerator::FNV_MASK
    end
    return sprintf("%016x", value)
  end

  def self.tracker_lookup_species_pool(recipe)
    configuration_value = Configuration.from(recipe["configuration"])
    policies = [configuration_value.wild_policy, configuration_value.trainer_policy]
    pool = []
    includes_normal = policies.any? do |policy|
      policy != Configuration::POLICY_CUSTOM_FUSIONS_ONLY
    end
    includes_fusions = policies.any? do |policy|
      policy != Configuration::POLICY_NORMAL_ONLY
    end
    pool.concat(normal_species_pool) if includes_normal
    pool.concat(custom_fusion_pool) if includes_fusions
    return pool.uniq
  end

  def self.tracker_search_matches(recipe, normalized_query, normal_only)
    index_key = tracker_search_index_key(recipe, normal_only)
    cache_key = "#{index_key}|#{normalized_query}"
    cached = tracker_search_result_cache[cache_key]
    return cached if cached
    matches = tracker_search_index(recipe, normal_only).map do |entry|
      next if !entry[2].include?(normalized_query)
      {
        "species_id" => entry[0],
        "species_name" => entry[1]
      }
    end.compact
    matches.sort_by! do |match|
      name = match["species_name"].downcase
      [name.start_with?(normalized_query) ? 0 : 1, name]
    end
    tracker_store_bounded(tracker_search_result_cache, cache_key, matches.freeze, 64)
    return matches
  end

  def self.tracker_search_index(recipe, normal_only)
    key = tracker_search_index_key(recipe, normal_only)
    return tracker_search_indexes[key] if tracker_search_indexes[key]
    species_pool = normal_only ? normal_species_pool : tracker_lookup_species_pool(recipe)
    index = species_pool.map do |species_id|
      name = tracker_search_species_name(species_id)
      next if !name
      stable_id = "#{species_id}:0"
      [stable_id, name, "#{name} #{species_id}".downcase]
    end.compact
    tracker_search_indexes[key] = index.freeze
    return tracker_search_indexes[key]
  end

  def self.tracker_search_index_key(recipe, normal_only)
    return "normal" if normal_only
    configuration_value = Configuration.from(recipe["configuration"])
    policies = [configuration_value.wild_policy, configuration_value.trainer_policy]
    includes_normal = policies.any? do |policy|
      policy != Configuration::POLICY_CUSTOM_FUSIONS_ONLY
    end
    includes_fusions = policies.any? do |policy|
      policy != Configuration::POLICY_NORMAL_ONLY
    end
    return "#{includes_normal}|#{includes_fusions}"
  end

  def self.tracker_search_species_name(species_id)
    match = /\AB(\d+)H(\d+)\z/.match(species_id.to_s)
    return tracker_search_fusion_name(match[1].to_i, match[2].to_i) if match
    species = GameData::Species.try_get(species_id)
    return species ? species.name : nil
  end

  def self.tracker_search_fusion_name(body_id, head_id)
    body_dex = GameData::NAT_DEX_MAPPING[body_id] || body_id
    head_dex = GameData::NAT_DEX_MAPPING[head_id] || head_id
    prefix = GameData::SPLIT_NAMES[head_dex][0].dup
    suffix = GameData::SPLIT_NAMES[body_dex][1]
    prefix = prefix[0..-2] if prefix[-1] == suffix[0]
    suffix = suffix.capitalize if prefix.end_with?(" ")
    return prefix + suffix
  rescue Exception
    return nil
  end

  def self.tracker_lookup_species_available?(species)
    return true if normal_species_pool.include?(species.id)
    return true if custom_fusion_pool.include?(species.id)
    return false if !species.is_a?(GameData::FusedSpecies)
    return normal_species_pool.include?(species.body_pokemon.id) &&
      normal_species_pool.include?(species.head_pokemon.id)
  end

  def self.tracker_lookup_base_stats(species)
    stats = species.base_stats
    return {
      "hp" => stats[:HP],
      "attack" => stats[:ATTACK],
      "defense" => stats[:DEFENSE],
      "special_attack" => stats[:SPECIAL_ATTACK],
      "special_defense" => stats[:SPECIAL_DEFENSE],
      "speed" => stats[:SPEED]
    }
  end

  def self.tracker_lookup_abilities(species, recipe)
    generator = AbilityGenerator.new(
      recipe["seed"], allowed_ability_pool, ability_pool_fingerprint
    )
    slots = if normal_ability_species?(species)
              generator.slots_for(species)
            else
              generator.fusion_slots_for(species)
            end
    ability_ids = (slots[:normal] + slots[:hidden]).compact.uniq
    return ability_ids.map do |ability_id|
      tracker_ability_snapshot(GameData::Ability.get(ability_id))
    end
  end

  def self.tracker_lookup_learnset(species)
    return species.moves.each_with_index.map do |entry, index|
      move = GameData::Move.try_get(entry[1])
      next if !move
      {
        "id" => move.id.to_s,
        "name" => move.name,
        "learned_level" => entry[0],
        "learn_order" => index,
        "source" => "level_up",
        "origin" => "post_run",
        "type" => move.type.to_s,
        "category" => tracker_move_category(move),
        "description" => move.description,
        "power" => move.base_damage || 0,
        "accuracy" => move.accuracy || 0,
        "total_pp" => move.total_pp,
        "pp_after_use" => nil
      }
    end.compact
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

  def self.tracker_lookup_fusion_materials(species, mapper)
    return [] if !species.is_a?(GameData::FusedSpecies)
    return mapper.material_pairs_for(species.id).map do |body_id, head_id|
      body = GameData::Species.get(body_id)
      head = GameData::Species.get(head_id)
      {
        "body" => tracker_lookup_relation(body, "Body material"),
        "head" => tracker_lookup_relation(head, "Head material")
      }
    end
  end

  def self.tracker_post_run_fusion_mapper(recipe)
    run_id = recipe["run_id"]
    tracker_fusion_mappers[run_id] ||= PlayerFusionMapper.new(
      recipe["seed"], custom_fusion_pool, {}, {}
    )
    return tracker_fusion_mappers[run_id]
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
    pokemon = Pokemon.new(species.id, 100)
    tracker_sprite_paths[species.id] = tracker_sprite_path(pokemon)
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
    @tracker_fusion_mappers = nil
    @tracker_sprite_paths = nil
  end
end

Events.onEndBattle += proc do |_sender, event|
  decision = event[0]
  Ironmon.complete_tracker_run(:lost) if [2, 5].include?(decision)
end

alias ironmon_tracker_original_hall_of_fame_entry pbHallOfFameEntry
def pbHallOfFameEntry
  Ironmon.complete_tracker_run(:won)
  return ironmon_tracker_original_hall_of_fame_entry
end
