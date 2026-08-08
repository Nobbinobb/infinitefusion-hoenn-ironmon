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
      "data_mode" => tracker_data_mode,
      "species_generator_version" => $PokemonGlobal.ironmon_species_generator_version,
      "ability_generator_version" => $PokemonGlobal.ironmon_ability_generator_version,
      "base_stat_generator_version" => $PokemonGlobal.ironmon_base_stat_generator_version,
      "base_stat_source_fingerprint" => $PokemonGlobal.ironmon_base_stat_source_fingerprint,
      "player_fusion_generator_version" => PlayerFusionMapper::SCHEMA_VERSION,
      "species_pool_fingerprint" => tracker_species_pool_fingerprint,
      "ability_pool_fingerprint" => $PokemonGlobal.ironmon_ability_pool_fingerprint,
      "fusion_pool_fingerprint" => $PokemonGlobal.ironmon_custom_fusion_pool_fingerprint
    }
  end

  def self.tracker_pokemon_search(payload, envelope_run_id)
    payload ||= {}
    recipe = tracker_validate_completed_recipe(payload["recipe"], envelope_run_id)
    return tracker_pokemon_search_for_recipe(payload, recipe)
  end

  def self.tracker_pokemon_search_for_recipe(payload, recipe)
    query = payload["query"].to_s.strip
    raise TrackerLookupError.new("invalid_query", "Enter a Pokemon name to search.") if query.empty?
    offset = payload["offset"].to_i
    limit = payload["limit"].to_i
    limit = 20 if limit == 0
    raise TrackerLookupError.new("invalid_page", "Search offset must not be negative.") if offset < 0
    if limit < 1 || limit > TRACKER_SEARCH_LIMIT
      raise TrackerLookupError.new("invalid_page", "Search page size must be between 1 and 50.")
    end
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
    return tracker_pokemon_lookup_for_recipe(payload, recipe)
  end

  def self.tracker_pokemon_lookup_for_recipe(payload, recipe)
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
    original_stats = original_base_stats_for(species)
    generated_stats = tracker_lookup_generated_base_stats(species, recipe)
    result = {
      "species_id" => "#{species.id}:0",
      "species_name" => species.name,
      "sprite_path" => tracker_lookup_sprite_path(species),
      "types" => species.types.map { |type| type.to_s },
      "original_base_stats" => tracker_base_stat_snapshot(original_stats),
      "original_base_stat_total" => tracker_base_stat_total(original_stats),
      "base_stats" => tracker_base_stat_snapshot(generated_stats),
      "base_stat_total" => tracker_base_stat_total(generated_stats),
      "base_stats_randomized" => !!recipe["base_stat_generator_version"],
      "abilities" => tracker_lookup_abilities(species, recipe),
      "learnset" => learnset,
      "evolutions" => tracker_lookup_evolutions(species),
      "previous_evolutions" => tracker_lookup_previous_evolutions(species),
      "wild_occurrences" => tracker_lookup_wild_occurrences(species, recipe),
      "trainer_occurrences" => tracker_lookup_trainer_occurrences(species, recipe),
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
    return tracker_fusion_preview_for_recipe(payload, recipe)
  end

  def self.tracker_fusion_preview_for_recipe(payload, recipe)
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
    if !recipe.is_a?(Hash) || recipe["run_id"].to_s.empty?
      raise TrackerLookupError.new("invalid_recipe", "The completed-run recipe is missing.")
    end
    if recipe["run_id"] != envelope_run_id
      raise TrackerLookupError.new("run_mismatch", "The request and recipe identify different runs.")
    end
    if recipe["result"].to_s == "active" || recipe["result"].to_s.empty?
      raise TrackerLookupError.new("run_active", "Complete generated lookup is unavailable during an active run.")
    end
    supported_species_versions = [SpeciesGenerator::LEGACY_SCHEMA_VERSION,
                                  SpeciesGenerator::SCHEMA_VERSION]
    if !supported_species_versions.include?(
      recipe["species_generator_version"]
    )
      raise TrackerLookupError.new("generator_unavailable", "The required species generator is unavailable.")
    end
    if recipe["ability_generator_version"] != AbilityGenerator::SCHEMA_VERSION
      raise TrackerLookupError.new("generator_unavailable", "The required ability generator is unavailable.")
    end
    base_stat_version = recipe["base_stat_generator_version"]
    base_stat_fingerprint = recipe["base_stat_source_fingerprint"]
    if base_stat_version || base_stat_fingerprint
      if base_stat_version != BaseStatGenerator::SCHEMA_VERSION
        raise TrackerLookupError.new("generator_unavailable", "The required base-stat generator is unavailable.")
      end
      if base_stat_fingerprint != base_stat_source_fingerprint
        raise TrackerLookupError.new("incompatible_base_stats", "The base-stat source data no longer matches this run.")
      end
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

  def self.tracker_base_stat_snapshot(stats)
    return {
      "hp" => stats[:HP],
      "attack" => stats[:ATTACK],
      "defense" => stats[:DEFENSE],
      "special_attack" => stats[:SPECIAL_ATTACK],
      "special_defense" => stats[:SPECIAL_DEFENSE],
      "speed" => stats[:SPEED]
    }
  end

  def self.tracker_base_stat_total(stats)
    return BaseStatGenerator::STAT_ORDER.inject(0) do |sum, stat|
      sum + stats[stat].to_i
    end
  end

  def self.tracker_lookup_generated_base_stats(species, recipe)
    return original_base_stats_for(species) if
      !recipe["base_stat_generator_version"]
    generator = base_stat_generator_for(
      recipe["seed"], recipe["base_stat_source_fingerprint"]
    )
    return generated_base_stats_for(species, generator)
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

  def self.tracker_lookup_wild_occurrences(target, recipe)
    if recipe["species_generator_version"] ==
       SpeciesGenerator::LEGACY_SCHEMA_VERSION
      return tracker_lookup_legacy_wild_occurrences(target, recipe)
    end
    return [] if recipe["species_generator_version"] !=
      SpeciesGenerator::SCHEMA_VERSION
    configuration_value = Configuration.from(recipe["configuration"])
    generator = SpeciesGenerator.new(
      recipe["seed"], :wild, configuration_value.wild_policy,
      normal_species_pool, custom_fusion_pool, {}
    )
    modes = if recipe["data_mode"] == "remix" &&
               defined?(GameData::EncounterModern)
              [GameData::EncounterModern]
            else
              [GameData::Encounter]
            end
    occurrences = []
    modes.each do |mode|
      mode.each do |data|
        data.types.each do |encounter_type, entries|
          total = entries.inject(0) { |sum, entry| sum + entry[0].to_i }
          entries.each_with_index do |entry, slot|
            context = [:table, mode.name, data.map, data.version,
                       encounter_type, slot]
            mapped = generator.map(entry[1], context)
            mapped_data = GameData::Species.try_get(mapped)
            next if !mapped_data || mapped_data.id != target.id
            source = GameData::Species.get(entry[1])
            chance = total > 0 ? (entry[0].to_f * 100.0 / total).round(2) : nil
            occurrences << {
              "map_id" => data.map,
              "route_name" => pbGetMapNameFromId(data.map),
              "mode" => mode == GameData::Encounter ? "Classic" : "Remix",
              "encounter_version" => data.version,
              "encounter_type" => encounter_type.to_s,
              "slot" => slot + 1,
              "minimum_level" => entry[2].to_i,
              "maximum_level" => (entry[3] || entry[2]).to_i,
              "source_species_id" => "#{source.id}:0",
              "source_species_name" => source.name,
              "chance_percent" => chance,
              "chance_is_conditional" => false
            }
          end
          if configuration_value.wild_policy ==
             Configuration::POLICY_NORMAL_ONLY
            tracker_lookup_wild_fusion_occurrences(
              occurrences, target, recipe, generator, mode, data,
              encounter_type, entries, total
            )
          end
        end
      end
    end
    return occurrences.sort_by do |entry|
      [entry["route_name"], entry["encounter_type"], entry["slot"]]
    end
  end

  def self.tracker_lookup_legacy_wild_occurrences(target, recipe)
    return [] if !tracker_loaded_recipe?(recipe)
    mapping = $PokemonGlobal.ironmon_wild_species_map
    return [] if !mapping.is_a?(Hash)
    mode = if recipe["data_mode"] == "remix" &&
              defined?(GameData::EncounterModern)
             GameData::EncounterModern
           else
             GameData::Encounter
           end
    occurrences = []
    mode.each do |data|
      data.types.each do |encounter_type, entries|
        total = entries.inject(0) { |sum, entry| sum + entry[0].to_i }
        entries.each_with_index do |entry, slot|
          source = GameData::Species.get(entry[1])
          next if mapping[source.id_number].to_i != target.id_number
          chance = total > 0 ? (entry[0].to_f * 100.0 / total).round(2) : nil
          occurrences << {
            "map_id" => data.map,
            "route_name" => pbGetMapNameFromId(data.map),
            "mode" => mode == GameData::Encounter ? "Classic" : "Remix",
            "encounter_version" => data.version,
            "encounter_type" => encounter_type.to_s,
            "slot" => slot + 1,
            "minimum_level" => entry[2].to_i,
            "maximum_level" => (entry[3] || entry[2]).to_i,
            "source_species_id" => "#{source.id}:0",
            "source_species_name" => source.name,
            "chance_percent" => chance,
            "chance_is_conditional" => false
          }
        end
      end
    end
    return occurrences.sort_by do |entry|
      [entry["route_name"], entry["encounter_type"], entry["slot"]]
    end
  end

  def self.tracker_lookup_wild_fusion_occurrences(
    occurrences, target, recipe, generator, mode, data, encounter_type,
    entries, total
  )
    return if target.id_number <= NB_POKEMON || total <= 0
    mapper = tracker_post_run_fusion_mapper(recipe)
    entries.each_with_index do |body_entry, body_slot|
      body_context = [:table, mode.name, data.map, data.version,
                      encounter_type, body_slot]
      body = generator.map(body_entry[1], body_context)
      entries.each_with_index do |head_entry, head_slot|
        head_context = [:table, mode.name, data.map, data.version,
                        encounter_type, head_slot]
        head = generator.map(head_entry[1], head_context)
        result = mapper.species(body, head)
        result_data = GameData::Species.try_get(result)
        next if !result_data || result_data.id != target.id
        body_source = GameData::Species.get(body_entry[1])
        head_source = GameData::Species.get(head_entry[1])
        chance = body_entry[0].to_f * head_entry[0].to_f * 100.0 /
          (total * total)
        occurrences << {
          "map_id" => data.map,
          "route_name" => pbGetMapNameFromId(data.map),
          "mode" => mode == GameData::Encounter ? "Classic" : "Remix",
          "encounter_version" => data.version,
          "encounter_type" => encounter_type.to_s,
          "slot" => body_slot + 1,
          "secondary_slot" => head_slot + 1,
          "minimum_level" => body_entry[2].to_i,
          "maximum_level" => (body_entry[3] || body_entry[2]).to_i,
          "source_species_id" => "#{body_source.id}:0",
          "source_species_name" => body_source.name,
          "secondary_source_species_id" => "#{head_source.id}:0",
          "secondary_source_species_name" => head_source.name,
          "chance_percent" => chance.round(2),
          "chance_is_conditional" => true
        }
      end
    end
  rescue PlayerFusionMappingError => e
    echoln "Ironmon tracker skipped wild fusion occurrences: #{e.message}"
  end

  def self.tracker_lookup_trainer_occurrences(target, recipe)
    if recipe["species_generator_version"] ==
       SpeciesGenerator::LEGACY_SCHEMA_VERSION
      return tracker_lookup_legacy_trainer_occurrences(target, recipe)
    end
    return [] if recipe["species_generator_version"] !=
      SpeciesGenerator::SCHEMA_VERSION
    configuration_value = Configuration.from(recipe["configuration"])
    generator = SpeciesGenerator.new(
      recipe["seed"], :trainer, configuration_value.trainer_policy,
      normal_species_pool, custom_fusion_pool, {}
    )
    occurrences = []
    tracker_trainer_data_mode(recipe).list_all.each do |_trainer_id, trainer|
      trainer.pokemon.each_with_index do |pokemon, slot|
        source = GameData::Species.get(pokemon[:species])
        mapped = generator.map(source.id, [:pbs, trainer.id, slot])
        mapped_data = GameData::Species.try_get(mapped)
        next if !mapped_data || mapped_data.id != target.id
        trainer_type = GameData::TrainerType.try_get(trainer.trainer_type)
        occurrence = {
          "trainer_id" => tracker_lookup_trainer_id(trainer),
          "trainer_name" => trainer.name,
          "trainer_type" => trainer_type ? trainer_type.name :
            trainer.trainer_type.to_s,
          "slot" => slot + 1,
          "level" => pokemon[:level].to_i,
          "source_species_id" => "#{source.id}:0",
          "source_species_name" => source.name
        }
        tracker_append_trainer_locations(occurrences, occurrence, trainer)
      end
    end
    occurrences.uniq! do |entry|
      [entry["trainer_id"], entry["slot"], entry["source_species_id"],
       entry["map_id"]]
    end
    return occurrences.sort_by do |entry|
      [entry["trainer_type"], entry["trainer_name"], entry["slot"]]
    end
  end

  def self.tracker_lookup_legacy_trainer_occurrences(target, recipe)
    return [] if !tracker_loaded_recipe?(recipe)
    mapping = $PokemonGlobal.ironmon_trainer_species_map
    return [] if !mapping.is_a?(Hash)
    occurrences = []
    tracker_trainer_data_mode(recipe).list_all.each do |_trainer_id, trainer|
      trainer.pokemon.each_with_index do |pokemon, slot|
        source = GameData::Species.get(pokemon[:species])
        next if mapping[source.id_number].to_i != target.id_number
        trainer_type = GameData::TrainerType.try_get(trainer.trainer_type)
        occurrence = {
          "trainer_id" => tracker_lookup_trainer_id(trainer),
          "trainer_name" => trainer.name,
          "trainer_type" => trainer_type ? trainer_type.name :
            trainer.trainer_type.to_s,
          "slot" => slot + 1,
          "level" => pokemon[:level].to_i,
          "source_species_id" => "#{source.id}:0",
          "source_species_name" => source.name
        }
        tracker_append_trainer_locations(occurrences, occurrence, trainer)
      end
    end
    occurrences.uniq! do |entry|
      [entry["trainer_id"], entry["slot"], entry["source_species_id"],
       entry["map_id"]]
    end
    return occurrences.sort_by do |entry|
      [entry["trainer_type"], entry["trainer_name"], entry["slot"]]
    end
  end

  def self.tracker_loaded_recipe?(recipe)
    return false if !$PokemonGlobal
    return recipe["run_id"] == $PokemonGlobal.ironmon_run_id
  end

  def self.tracker_lookup_trainer_id(trainer)
    components = trainer.id.is_a?(Array) ? trainer.id : [trainer.id]
    return components.map { |component| component.to_s }.join(":")
  end

  def self.tracker_append_trainer_locations(occurrences, occurrence, trainer)
    locations = tracker_lookup_trainer_locations(trainer)
    if locations.empty?
      occurrences << occurrence
      return
    end
    locations.each do |location|
      occurrences << occurrence.merge(location)
    end
  end

  def self.tracker_lookup_trainer_locations(trainer)
    components = trainer.id.is_a?(Array) ? trainer.id : [trainer.id]
    key = "#{components[0]}\0#{components[1]}"
    return tracker_trainer_location_index[key] || []
  end

  def self.tracker_trainer_location_index
    return @tracker_trainer_location_index if @tracker_trainer_location_index
    index = {}
    pattern = /pbTrainerBattle\(\s*:([A-Za-z0-9_]+)\s*,\s*["']([^"']+)["']/
    Dir.glob(File.join("Data", "Map[0-9][0-9][0-9].rxdata")).each do |path|
      map_id = File.basename(path)[/\d+/].to_i
      File.open(path, "rb") do |file|
        file.read.scan(pattern).each do |trainer_type, trainer_name|
          route_name = pbGetMapNameFromId(map_id)
          next if route_name.to_s.match?(/\Aquest_/i)
          key = "#{trainer_type}\0#{trainer_name}"
          index[key] ||= {}
          index[key][map_id] = {
            "map_id" => map_id,
            "route_name" => route_name
          }
        end
      end
    end
    @tracker_trainer_location_index = {}
    index.each do |key, locations|
      @tracker_trainer_location_index[key] = locations.values
    end
    return @tracker_trainer_location_index
  rescue Exception => e
    echoln "Ironmon tracker could not index trainer locations: #{e.message}"
    @tracker_trainer_location_index = {}
    return @tracker_trainer_location_index
  end

  def self.tracker_data_mode
    return "remix" if $game_switches &&
      $game_switches[SWITCH_MODERN_MODE]
    return "expert" if $game_switches &&
      $game_switches[SWITCH_EXPERT_MODE]
    return "classic"
  end

  def self.tracker_trainer_data_mode(recipe)
    return GameData::TrainerModern if recipe["data_mode"] == "remix" &&
      defined?(GameData::TrainerModern)
    return GameData::TrainerExpert if recipe["data_mode"] == "expert" &&
      defined?(GameData::TrainerExpert)
    return GameData::Trainer
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
    @tracker_trainer_location_index = nil
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
