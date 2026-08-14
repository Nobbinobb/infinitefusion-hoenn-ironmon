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
    return complete_run(result)
  end

  def self.publish_run_completion
    recipe = tracker_completed_run_recipe
    if recipe
      run_ledger["last_completed_recipe"] = Marshal.load(Marshal.dump(recipe))
      tracker_connection.send_event("run_completed", recipe)
    end
  rescue Exception => e
    echoln "Ironmon tracker could not complete the run recipe: #{e.message}"
  end

  def self.tracker_recoverable_completed_run_recipe
    recipe = tracker_completed_run_recipe
    return recipe if recipe
    stored = run_ledger["last_completed_recipe"]
    return stored if stored.is_a?(Hash)
    return nil
  end

  def self.tracker_completed_run_recipe
    return nil if !active? || !$PokemonGlobal
    result = $PokemonGlobal.ironmon_run_result
    return nil if !result || result.to_s.empty?
    return {
      "schema_version" => 1,
      "run_id" => ensure_tracker_run_id,
      "seed" => $PokemonGlobal.ironmon_seed || 0,
      "result" => result.to_s,
      "game_version" => tracker_game_version,
      "ironmon_version" => VERSION,
      "configuration" => configuration_snapshot,
      "data_mode" => tracker_data_mode,
      "species_generator" => tracker_species_generator_recipe,
      "ability_generator" => tracker_ability_generator_recipe,
      "base_stat_generator" => tracker_base_stat_generator_recipe,
      "evolution_generator" => tracker_evolution_generator_recipe,
      "move_access_generator" => tracker_move_access_generator_recipe,
      "player_fusion_generator" => tracker_player_fusion_generator_recipe,
      "statistics" => tracker_attempt_statistics(current_run_attempt),
      "move_access_metrics" => move_access_metrics_snapshot,
      "evolution_metrics" => evolution_metrics_snapshot
    }
  end

  def self.tracker_attempt_statistics(attempt)
    return nil if !attempt.is_a?(Hash)
    tick_active_run_duration if attempt["result"] == "active"
    statistics = attempt["statistics"]
    return nil if !statistics.is_a?(Hash)
    if !statistics["trainer_species_names"].is_a?(Hash)
      statistics = normalize_attempt_statistics(statistics)
      attempt["statistics"] = statistics
    end
    ledger = run_ledger
    return {
      "schema_version" => statistics["schema_version"],
      "attempt_number" => attempt["attempt_number"],
      "seed" => attempt["seed"],
      "result" => attempt["result"],
      "active_seconds" => attempt["active_seconds"],
      "attempts_started" => ledger["attempts_started"],
      "attempts_lost" => ledger["attempts_lost"],
      "attempts_won" => ledger["attempts_won"],
      "attempts_abandoned" => ledger["attempts_abandoned"],
      "battles_completed" => statistics["battles_completed"],
      "highest_player_level" => statistics["highest_player_level"],
      "badges_earned" => statistics["badges_earned"],
      "total_item_healing" => statistics["total_item_healing"],
      "wasted_item_healing" => statistics["wasted_item_healing"],
      "items_used" => statistics["items_used"],
      "items_by_source" => statistics["items_by_source"],
      "trainer_species_counts" => statistics["trainer_species_counts"],
      "trainer_species_names" => statistics["trainer_species_names"],
      "trainer_species_distinct" => statistics["trainer_species_distinct"],
      "trainer_species_most_encountered" =>
        statistics["trainer_species_most_encountered"],
      "trainer_defeated_count" => statistics["trainer_defeated_count"],
      "trainer_defeated_bst_average" =>
        statistics["trainer_defeated_bst_average"],
      "trainer_defeated_bst_minimum" =>
        statistics["trainer_defeated_bst_minimum"],
      "trainer_defeated_bst_minimum_species" =>
        statistics["trainer_defeated_bst_minimum_species"],
      "trainer_defeated_bst_maximum" =>
        statistics["trainer_defeated_bst_maximum"],
      "trainer_defeated_bst_maximum_species" =>
        statistics["trainer_defeated_bst_maximum_species"]
    }
  end

  def self.tracker_species_generator_recipe
    return {
      "version" => $PokemonGlobal.ironmon_species_generator_version,
      "pool_fingerprint" => tracker_species_pool_fingerprint
    }
  end

  def self.tracker_ability_generator_recipe
    return {
      "version" => $PokemonGlobal.ironmon_ability_generator_version,
      "pool_size" => $PokemonGlobal.ironmon_ability_pool_size,
      "pool_fingerprint" => $PokemonGlobal.ironmon_ability_pool_fingerprint
    }
  end

  def self.tracker_base_stat_generator_recipe
    version = $PokemonGlobal.ironmon_base_stat_generator_version
    return nil if !version
    return {
      "version" => version,
      "source_fingerprint" => $PokemonGlobal.ironmon_base_stat_source_fingerprint
    }
  end

  def self.tracker_evolution_generator_recipe
    version = $PokemonGlobal.ironmon_evolution_generator_version
    return nil if !version
    return {
      "version" => version,
      "rules_version" => $PokemonGlobal.ironmon_evolution_rules_version,
      "source_fingerprint" => $PokemonGlobal.ironmon_evolution_source_fingerprint,
      "taxonomy_fingerprint" => $PokemonGlobal.ironmon_evolution_taxonomy_fingerprint,
      "method_fingerprint" => $PokemonGlobal.ironmon_evolution_method_fingerprint,
      "target_fingerprint" => $PokemonGlobal.ironmon_evolution_target_fingerprint,
      "base_stat_generator" => {
        "version" => $PokemonGlobal.ironmon_evolution_base_stat_generator_version,
        "source_fingerprint" => $PokemonGlobal.ironmon_evolution_base_stat_source_fingerprint
      },
      "fusion" => {
        "version" => $PokemonGlobal.ironmon_evolution_fusion_generator_version,
        "rules_version" => $PokemonGlobal.ironmon_evolution_fusion_rules_version,
        "target_pool" => {
          "version" => $PokemonGlobal.ironmon_evolution_fusion_target_pool_version,
          "size" => $PokemonGlobal.ironmon_evolution_fusion_target_pool_size,
          "fingerprint" => $PokemonGlobal.ironmon_evolution_fusion_target_pool_fingerprint
        }
      }
    }
  end

  def self.tracker_move_access_generator_recipe
    version = $PokemonGlobal.ironmon_move_access_generator_version
    return nil if !version
    return {
      "version" => version,
      "pool_fingerprint" => $PokemonGlobal.ironmon_move_pool_fingerprint,
      "contextual_restriction_fingerprint" => $PokemonGlobal.ironmon_move_contextual_restriction_fingerprint,
      "level_up_source_fingerprint" => $PokemonGlobal.ironmon_move_source_fingerprint,
      "egg_source_fingerprint" => $PokemonGlobal.ironmon_egg_move_source_fingerprint,
      "tm" => {
        "roster_fingerprint" => $PokemonGlobal.ironmon_tm_roster_fingerprint,
        "source_fingerprint" => $PokemonGlobal.ironmon_tm_source_fingerprint
      },
      "tr" => {
        "roster_fingerprint" => $PokemonGlobal.ironmon_tr_roster_fingerprint,
        "source_fingerprint" => $PokemonGlobal.ironmon_tr_source_fingerprint
      },
      "tutor" => {
        "catalog_fingerprint" => $PokemonGlobal.ironmon_tutor_catalog_fingerprint,
        "source_fingerprint" => $PokemonGlobal.ironmon_tutor_source_fingerprint
      },
      "fusion_tutor" => {
        "catalog_fingerprint" => $PokemonGlobal.ironmon_fusion_tutor_catalog_fingerprint,
        "source_fingerprint" => $PokemonGlobal.ironmon_fusion_tutor_source_fingerprint
      }
    }
  end

  def self.tracker_player_fusion_generator_recipe
    return {
      "version" => PlayerFusionMapper::SCHEMA_VERSION,
      "pool_size" => $PokemonGlobal.ironmon_custom_fusion_pool_size,
      "pool_fingerprint" => $PokemonGlobal.ironmon_custom_fusion_pool_fingerprint
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

  def self.tracker_evolution_candidate_search(payload, envelope_run_id)
    payload ||= {}
    recipe = tracker_validate_completed_recipe(payload["recipe"], envelope_run_id)
    return tracker_evolution_candidate_search_for_recipe(payload, recipe)
  end

  def self.tracker_fusion_material_search(payload, envelope_run_id)
    payload ||= {}
    recipe = tracker_validate_completed_recipe(payload["recipe"], envelope_run_id)
    return tracker_fusion_material_search_for_recipe(payload, recipe)
  end

  def self.tracker_wild_occurrence_search(payload, envelope_run_id)
    payload ||= {}
    recipe = tracker_validate_completed_recipe(payload["recipe"], envelope_run_id)
    return tracker_occurrence_search_for_recipe(payload, recipe, :wild)
  end

  def self.tracker_trainer_occurrence_search(payload, envelope_run_id)
    payload ||= {}
    recipe = tracker_validate_completed_recipe(payload["recipe"], envelope_run_id)
    return tracker_occurrence_search_for_recipe(payload, recipe, :trainer)
  end

  def self.tracker_occurrence_search_for_recipe(payload, recipe, kind)
    species_id = payload["species_id"].to_s
    species_key = species_id.split(":", 2)[0]
    species = GameData::Species.try_get(species_key.to_sym)
    if !species || !tracker_lookup_species_available?(species)
      raise TrackerLookupError.new(
        "pokemon_not_found", "The selected Pokemon is not available in this run."
      )
    end
    offset = payload["offset"].to_i
    limit = payload["limit"].to_i
    limit = 50 if limit == 0
    if offset < 0 || limit < 1 || limit > TRACKER_SEARCH_LIMIT
      raise TrackerLookupError.new(
        "invalid_page", "Occurrence pages must contain between 1 and 50 entries."
      )
    end
    cache_key = "#{recipe["run_id"]}|#{species.id}|#{kind}_occurrences"
    occurrences = tracker_lookup_cache[cache_key]
    if !occurrences
      occurrences = if kind == :wild
                      tracker_lookup_wild_occurrences(species, recipe)
                    else
                      tracker_lookup_trainer_occurrences(species, recipe)
                    end
      tracker_store_bounded(tracker_lookup_cache, cache_key, occurrences, 256)
    end
    return {
      "matches" => occurrences.slice(offset, limit) || [],
      "total" => occurrences.length
    }
  end

  def self.tracker_fusion_material_search_for_recipe(payload, recipe)
    species_id = payload["species_id"].to_s
    species_key = species_id.split(":", 2)[0]
    species = GameData::Species.try_get(species_key.to_sym)
    if !species || !tracker_lookup_species_available?(species) ||
       !species.is_a?(GameData::FusedSpecies)
      raise TrackerLookupError.new(
        "pokemon_not_found", "The selected fusion is not available in this run."
      )
    end
    offset = payload["offset"].to_i
    limit = payload["limit"].to_i
    limit = 50 if limit == 0
    if offset < 0 || limit < 1 || limit > TRACKER_SEARCH_LIMIT
      raise TrackerLookupError.new(
        "invalid_page", "Fusion-material pages must contain between 1 and 50 pairs."
      )
    end
    mapper = tracker_post_run_fusion_mapper(recipe)
    return tracker_lookup_fusion_materials(species, mapper, offset, limit)
  end

  def self.tracker_evolution_candidate_search_for_recipe(payload, recipe)
    species_id = payload["species_id"].to_s
    species_key = species_id.split(":", 2)[0]
    species = GameData::Species.try_get(species_key.to_sym)
    if !species || !tracker_lookup_species_available?(species)
      raise TrackerLookupError.new(
        "pokemon_not_found", "The selected Pokemon is not available in this run."
      )
    end
    side = payload["side"].to_s.downcase.to_sym
    if ![:normal, :head, :body].include?(side)
      raise TrackerLookupError.new(
        "invalid_evolution_side", "Select a normal, Head, or Body target list."
      )
    end
    offset = payload["offset"].to_i
    limit = payload["limit"].to_i
    limit = 50 if limit == 0
    if offset < 0 || limit < 1 || limit > TRACKER_SEARCH_LIMIT
      raise TrackerLookupError.new(
        "invalid_page", "Candidate pages must contain between 1 and 50 targets."
      )
    end
    query = payload["query"].to_s.strip.downcase
    targets = tracker_evolution_candidates_for(species, recipe, side)
    matches = targets.map do |target|
      name = tracker_search_species_name(target[:target_id])
      next if !name
      searchable = "#{name} #{target[:target]}".downcase
      next if !query.empty? && !searchable.include?(query)
      [target, name]
    end.compact
    matches.sort_by! do |entry|
      [entry[1].downcase, entry[0][:target]]
    end
    page = matches.slice(offset, limit) || []
    return {
      "matches" => page.map do |entry|
        tracker_evolution_candidate_snapshot(entry[0], entry[1])
      end,
      "total" => matches.length
    }
  end

  def self.tracker_pokemon_lookup_for_recipe(payload, recipe)
    species_id = payload["species_id"].to_s
    section = payload["section"].to_s
    section = "overview" if section.empty?
    valid_sections = ["overview", "abilities", "stats", "moves", "evolutions"]
    if !valid_sections.include?(section)
      raise TrackerLookupError.new("invalid_section", "The requested Pokemon information section is invalid.")
    end
    species_key = species_id.split(":", 2)[0]
    species = GameData::Species.try_get(species_key.to_sym)
    if !species || !tracker_lookup_species_available?(species)
      raise TrackerLookupError.new("pokemon_not_found", "The selected Pokemon is not available in this run.")
    end

    cache_key = "#{recipe["run_id"]}|#{species.id}|#{section}"
    cached = tracker_lookup_cache[cache_key]
    return cached if cached

    result = {
      "section" => section,
      "identity" => {
        "species_id" => "#{species.id}:0",
        "species_name" => species.name,
        "sprite_path" => tracker_lookup_sprite_path(species),
        "types" => species.types.map { |type| type.to_s },
        "fusion" => !normal_ability_species?(species)
      }
    }

    case section
    when "overview"
      fusion_mapper = tracker_post_run_fusion_mapper(recipe)
      result["overview"] = {
        "wild_occurrences" => tracker_occurrence_search_for_recipe(
          { "species_id" => species_id, "offset" => 0, "limit" => 50 },
          recipe, :wild
        ),
        "trainer_occurrences" => tracker_occurrence_search_for_recipe(
          { "species_id" => species_id, "offset" => 0, "limit" => 50 },
          recipe, :trainer
        ),
        "fusion_bases" => tracker_lookup_fusion_bases(species),
        "reverse_fusion" => tracker_lookup_reverse_fusion(species, fusion_mapper),
        "fusion_materials" => tracker_lookup_fusion_materials(
          species, fusion_mapper, 0, 50
        )
      }
    when "abilities"
      result["abilities"] = {
        "values" => tracker_lookup_abilities(species, recipe),
        "slots" => tracker_lookup_ability_slots(species, recipe),
        "generator" => tracker_lookup_ability_generator(recipe)
      }
    when "stats"
      original_stats = original_base_stats_for(species)
      generated_stats = tracker_lookup_generated_base_stats(species, recipe)
      result["stats"] = {
        "original" => tracker_base_stat_snapshot(original_stats),
        "original_total" => tracker_base_stat_total(original_stats),
        "generated" => tracker_base_stat_snapshot(generated_stats),
        "generated_total" => tracker_base_stat_total(generated_stats),
        "randomized" => !!recipe["base_stat_generator_version"],
        "generator" => tracker_lookup_base_stat_generator(recipe)
      }
    when "moves"
      move_access = tracker_lookup_move_access(species, recipe)
      result["moves"] = {
        "learnset" => move_access["learnset"],
        "access" => move_access,
        "generator" => tracker_lookup_move_generator(recipe)
      }
    when "evolutions"
      evolution_targets = tracker_lookup_evolution_targets(species, recipe)
      generated_evolutions = tracker_evolution_recipe?(recipe)
      generated_stats = tracker_lookup_generated_base_stats(species, recipe)
      result["evolutions"] = {
        "current_base_stat_total" => tracker_base_stat_total(generated_stats),
        "native_targets" => generated_evolutions ? [] : tracker_lookup_evolutions(species),
        "native_predecessors" => generated_evolutions ? [] : tracker_lookup_previous_evolutions(species),
        "generated_predecessors" => tracker_lookup_evolution_predecessors(species, recipe),
        "generated_targets" => evolution_targets[:normal],
        "head_targets" => evolution_targets[:head],
        "body_targets" => evolution_targets[:body],
        "generator" => tracker_lookup_evolution_generator(recipe)
      }
    end
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
    if recipe["schema_version"] != 1
      raise TrackerLookupError.new("invalid_recipe", "The completed-run recipe schema is unsupported.")
    end
    recipe = tracker_flatten_completed_recipe(recipe)
    if !SpeciesGenerator::SUPPORTED_SCHEMA_VERSIONS.include?(
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
    tracker_validate_evolution_recipe(recipe)
    move_version = recipe["move_access_generator_version"]
    move_metadata = [
      recipe["move_pool_fingerprint"],
      recipe["move_contextual_restriction_fingerprint"],
      recipe["move_source_fingerprint"],
      recipe["egg_move_source_fingerprint"],
      recipe["tm_roster_fingerprint"],
      recipe["tm_source_fingerprint"],
      recipe["tr_roster_fingerprint"],
      recipe["tr_source_fingerprint"],
      recipe["tutor_catalog_fingerprint"],
      recipe["tutor_source_fingerprint"],
      recipe["fusion_tutor_catalog_fingerprint"],
      recipe["fusion_tutor_source_fingerprint"]
    ]
    if move_version || move_metadata.compact.any?
      if move_version != MoveAccessGenerator::SCHEMA_VERSION
        raise TrackerLookupError.new(
          "generator_unavailable",
          "The required move-access generator is unavailable."
        )
      end
      expected_move_metadata = [
        level_up_move_pool_fingerprint,
        level_up_move_contextual_fingerprint,
        move_access_source_fingerprint,
        egg_move_access_source_fingerprint,
        machine_roster_fingerprint(:tm),
        machine_move_access_source_fingerprint(:tm),
        machine_roster_fingerprint(:tr),
        machine_move_access_source_fingerprint(:tr),
        ordinary_tutor_catalog_fingerprint,
        ordinary_tutor_source_fingerprint,
        specialized_tutor_catalog_fingerprint,
        specialized_tutor_source_fingerprint
      ]
      if move_metadata != expected_move_metadata
        raise TrackerLookupError.new(
          "incompatible_move_access",
          "The move-access source data no longer matches this run."
        )
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

  def self.tracker_flatten_completed_recipe(recipe)
    species = recipe["species_generator"] || {}
    ability = recipe["ability_generator"] || {}
    base_stats = recipe["base_stat_generator"] || {}
    evolution = recipe["evolution_generator"] || {}
    evolution_stats = evolution["base_stat_generator"] || {}
    fusion_evolution = evolution["fusion"] || {}
    fusion_target_pool = fusion_evolution["target_pool"] || {}
    moves = recipe["move_access_generator"] || {}
    tm = moves["tm"] || {}
    tr = moves["tr"] || {}
    tutor = moves["tutor"] || {}
    fusion_tutor = moves["fusion_tutor"] || {}
    player_fusion = recipe["player_fusion_generator"] || {}
    return {
      "run_id" => recipe["run_id"],
      "seed" => recipe["seed"],
      "result" => recipe["result"],
      "game_version" => recipe["game_version"],
      "ironmon_version" => recipe["ironmon_version"],
      "configuration" => recipe["configuration"],
      "data_mode" => recipe["data_mode"],
      "species_generator_version" => species["version"],
      "species_pool_fingerprint" => species["pool_fingerprint"],
      "ability_generator_version" => ability["version"],
      "ability_pool_fingerprint" => ability["pool_fingerprint"],
      "base_stat_generator_version" => base_stats["version"],
      "base_stat_source_fingerprint" => base_stats["source_fingerprint"],
      "evolution_generator_version" => evolution["version"],
      "evolution_rules_version" => evolution["rules_version"],
      "evolution_source_fingerprint" => evolution["source_fingerprint"],
      "evolution_taxonomy_fingerprint" => evolution["taxonomy_fingerprint"],
      "evolution_method_fingerprint" => evolution["method_fingerprint"],
      "evolution_target_fingerprint" => evolution["target_fingerprint"],
      "evolution_base_stat_generator_version" => evolution_stats["version"],
      "evolution_base_stat_source_fingerprint" => evolution_stats["source_fingerprint"],
      "fusion_evolution_generator_version" => fusion_evolution["version"],
      "fusion_evolution_rules_version" => fusion_evolution["rules_version"],
      "fusion_evolution_target_pool_version" => fusion_target_pool["version"],
      "fusion_evolution_target_pool_size" => fusion_target_pool["size"],
      "fusion_evolution_target_pool_fingerprint" => fusion_target_pool["fingerprint"],
      "move_access_generator_version" => moves["version"],
      "move_pool_fingerprint" => moves["pool_fingerprint"],
      "move_contextual_restriction_fingerprint" => moves["contextual_restriction_fingerprint"],
      "move_source_fingerprint" => moves["level_up_source_fingerprint"],
      "egg_move_source_fingerprint" => moves["egg_source_fingerprint"],
      "tm_roster_fingerprint" => tm["roster_fingerprint"],
      "tm_source_fingerprint" => tm["source_fingerprint"],
      "tr_roster_fingerprint" => tr["roster_fingerprint"],
      "tr_source_fingerprint" => tr["source_fingerprint"],
      "tutor_catalog_fingerprint" => tutor["catalog_fingerprint"],
      "tutor_source_fingerprint" => tutor["source_fingerprint"],
      "fusion_tutor_catalog_fingerprint" => fusion_tutor["catalog_fingerprint"],
      "fusion_tutor_source_fingerprint" => fusion_tutor["source_fingerprint"],
      "player_fusion_generator_version" => player_fusion["version"],
      "fusion_pool_fingerprint" => player_fusion["pool_fingerprint"],
      "move_access_metrics" => recipe["move_access_metrics"],
      "evolution_metrics" => recipe["evolution_metrics"]
    }
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

  def self.tracker_lookup_species_pool(_recipe)
    return (normal_species_pool + custom_fusion_pool).uniq
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
    return "all"
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

  def self.tracker_lookup_ability_slots(species, recipe)
    generator = AbilityGenerator.new(
      recipe["seed"], allowed_ability_pool, ability_pool_fingerprint
    )
    result = []
    if normal_ability_species?(species)
      tracker_lookup_append_ability_slots(
        result, :generated, species, generator.slots_for(species)
      )
      return result
    end

    final_slots = generator.fusion_slots_for(species)
    tracker_lookup_append_fusion_ability_slots(
      result, species, final_slots, generator
    )
    tracker_lookup_append_ability_slots(
      result, :body_generated, species.body_pokemon,
      generator.slots_for(species.body_pokemon)
    )
    tracker_lookup_append_ability_slots(
      result, :head_generated, species.head_pokemon,
      generator.slots_for(species.head_pokemon)
    )
    return result
  end

  def self.tracker_lookup_append_ability_slots(result, group, species, slots)
    originals = {
      :normal => original_normal_abilities(species),
      :hidden => original_hidden_abilities(species)
    }
    [:normal, :hidden].each do |kind|
      slots[kind].each_with_index do |ability, index|
        next if !ability
        result << tracker_lookup_ability_slot(
          group, kind, index, ability, originals[kind][index]
        )
      end
    end
  end

  def self.tracker_lookup_append_fusion_ability_slots(result, species, slots,
                                                        generator)
    originals = {
      :normal => original_normal_abilities(species),
      :hidden => original_hidden_abilities(species)
    }
    [:normal, :hidden].each do |kind|
      slots[kind].each_with_index do |ability, index|
        next if !ability
        source = tracker_lookup_fusion_ability_source(
          species, kind, index, generator
        )
        result << tracker_lookup_ability_slot(
          :final_fusion, kind, index, ability, originals[kind][index],
          source[0], source[1]
        )
      end
    end
  end

  def self.tracker_lookup_fusion_ability_source(species, kind, index,
                                                 generator)
    component = index.even? ? species.body_pokemon : species.head_pokemon
    component_name = index.even? ? "Body" : "Head"
    slots = generator.slots_for(component)
    if kind == :normal
      return ["#{component_name} normal 0", slots[:normal][0]]
    end
    if index < 2
      return ["#{component_name} normal 1", slots[:normal][1]]
    end
    return ["#{component_name} hidden 0", slots[:hidden][0]]
  end

  def self.tracker_lookup_ability_slot(group, kind, index, ability, original,
                                        source = nil, source_ability = nil)
    ability_data = GameData::Ability.get(ability)
    original_data = original ? GameData::Ability.get(original) : nil
    source_data = source_ability ? GameData::Ability.get(source_ability) : nil
    return {
      "group" => group.to_s,
      "kind" => kind.to_s,
      "index" => index,
      "ability_id" => ability_data.id.to_s,
      "ability_name" => ability_data.name,
      "original_ability_id" => original_data ? original_data.id.to_s : nil,
      "original_ability_name" => original_data ? original_data.name : nil,
      "eligibility" => tracker_lookup_ability_eligibility(ability),
      "active" => false,
      "source" => source,
      "source_ability_id" => source_data ? source_data.id.to_s : nil,
      "source_ability_name" => source_data ? source_data.name : nil,
      "restricted_source_replaced" => !!(
        source_ability && source_ability != ability
      )
    }
  end

  def self.tracker_lookup_ability_eligibility(ability)
    if AbilityGenerator::EXACT_SPECIES_ABILITY_RULES.key?(ability)
      return "exact_species"
    end
    if AbilityGenerator::COMPONENT_ABILITY_RULES.key?(ability)
      return "component_compatible"
    end
    return "universal"
  end

  def self.tracker_lookup_ability_generator(recipe)
    return tracker_lookup_generator_diagnostics(true, [
      ["run_seed", recipe["seed"]],
      ["schema", recipe["ability_generator_version"]],
      ["pool_rules", AbilityGenerator::POOL_RULES_VERSION],
      ["pool_size", allowed_ability_pool.length],
      ["pool_fingerprint", recipe["ability_pool_fingerprint"]]
    ])
  end

  def self.tracker_lookup_base_stat_generator(recipe)
    enabled = !!recipe["base_stat_generator_version"]
    return tracker_lookup_generator_diagnostics(enabled, [
      ["status", enabled ? "Enabled" : "Legacy stats"],
      ["run_seed", recipe["seed"]],
      ["schema", recipe["base_stat_generator_version"]],
      ["rules", BaseStatGenerator::RULES_VERSION],
      ["normal_range", "#{BaseStatGenerator::MINIMUM_STAT}-#{BaseStatGenerator::MAXIMUM_STAT}"],
      ["source_fingerprint", recipe["base_stat_source_fingerprint"]]
    ])
  end

  def self.tracker_lookup_move_generator(recipe)
    enabled = recipe["move_access_generator_version"] ==
      MoveAccessGenerator::SCHEMA_VERSION
    return tracker_lookup_generator_diagnostics(enabled, [
      ["status", enabled ? "Enabled" : "Legacy access"],
      ["run_seed", recipe["seed"]],
      ["schema", recipe["move_access_generator_version"]],
      ["pool_rules", MoveAccessGenerator::POOL_RULES_VERSION],
      ["move_pool_fingerprint", recipe["move_pool_fingerprint"]],
      ["restriction_fingerprint", recipe["move_contextual_restriction_fingerprint"]],
      ["level_up_source", recipe["move_source_fingerprint"]],
      ["egg_source", recipe["egg_move_source_fingerprint"]],
      ["tm_roster", recipe["tm_roster_fingerprint"]],
      ["tm_source", recipe["tm_source_fingerprint"]],
      ["tr_roster", recipe["tr_roster_fingerprint"]],
      ["tr_source", recipe["tr_source_fingerprint"]],
      ["tutor_catalog", recipe["tutor_catalog_fingerprint"]],
      ["tutor_source", recipe["tutor_source_fingerprint"]],
      ["fusion_tutor_catalog", recipe["fusion_tutor_catalog_fingerprint"]],
      ["fusion_tutor_source", recipe["fusion_tutor_source_fingerprint"]]
    ])
  end

  def self.tracker_lookup_evolution_generator(recipe)
    enabled = !!(
      recipe["evolution_generator_version"] ||
      recipe["fusion_evolution_generator_version"]
    )
    return tracker_lookup_generator_diagnostics(enabled, [
      ["status", enabled ? "Enabled" : "Native evolutions"],
      ["run_seed", recipe["seed"]],
      ["normal_schema", recipe["evolution_generator_version"]],
      ["normal_rules", recipe["evolution_rules_version"]],
      ["fusion_schema", recipe["fusion_evolution_generator_version"]],
      ["fusion_rules", recipe["fusion_evolution_rules_version"]],
      ["source_fingerprint", recipe["evolution_source_fingerprint"]],
      ["taxonomy_fingerprint", recipe["evolution_taxonomy_fingerprint"]],
      ["method_fingerprint", recipe["evolution_method_fingerprint"]],
      ["target_fingerprint", recipe["evolution_target_fingerprint"]],
      ["fusion_pool_version", recipe["fusion_evolution_target_pool_version"]],
      ["fusion_pool_size", recipe["fusion_evolution_target_pool_size"]],
      ["fusion_pool_fingerprint", recipe["fusion_evolution_target_pool_fingerprint"]],
      ["base_stat_schema", recipe["evolution_base_stat_generator_version"]],
      ["base_stat_source", recipe["evolution_base_stat_source_fingerprint"]]
    ])
  end

  def self.tracker_lookup_generator_diagnostics(enabled, values)
    entries = values.map do |key, value|
      next if value.nil? || value.to_s.empty?
      { "key" => key, "value" => value.to_s }
    end.compact
    return { "enabled" => enabled, "entries" => entries }
  end

  def self.tracker_lookup_move_access(species, recipe, pokemon = nil)
    randomized = recipe["move_access_generator_version"] ==
      MoveAccessGenerator::SCHEMA_VERSION
    generator = randomized ? tracker_move_access_generator(recipe) : nil
    level_entries = randomized ?
      generated_level_up_moves_for(species, generator) :
      original_level_up_moves_for(species)
    egg_moves = randomized ? generated_egg_moves_for(species, generator) :
      original_egg_moves_for(species)
    tm_moves = randomized ? generated_machine_moves_for(species, :tm, generator) :
      original_machine_moves_for(species, :tm)
    tr_moves = randomized ? generated_machine_moves_for(species, :tr, generator) :
      original_machine_moves_for(species, :tr)
    abstract_tutor = randomized ?
      generated_ordinary_tutor_moves_for(species, generator) :
      original_ordinary_tutor_moves_for(species)

    component_moves = tracker_move_access_component_moves(species, generator,
                                                          randomized)
    learnset = level_entries.each_with_index.map do |entry, index|
      tracker_move_access_entry(
        entry[1], "level_up", index,
        tracker_move_access_source_label(
          species, entry[1], component_moves[:level_body],
          component_moves[:level_head]
        ), entry[0]
      )
    end.compact
    egg = egg_moves.each_with_index.map do |move, index|
      tracker_move_access_entry(
        move, "egg", index,
        tracker_move_access_source_label(
          species, move, component_moves[:egg_body],
          component_moves[:egg_head]
        )
      )
    end.compact
    machines = tracker_move_access_machine_entries(
      species, :tm, tm_moves, component_moves[:tm_body],
      component_moves[:tm_head]
    )
    machines.concat(tracker_move_access_machine_entries(
      species, :tr, tr_moves, component_moves[:tr_body],
      component_moves[:tr_head], machines.length
    ))

    tutors = []
    ORDINARY_TUTOR_SLOTS.each do |slot|
      move = randomized ? generator.ordinary_tutor_offering_for(slot[:id]) :
        slot[:original_move]
      next if !abstract_tutor.include?(move)
      tutors << tracker_move_access_entry(
        move, "ordinary_tutor", tutors.length,
        tracker_move_access_source_label(
          species, move, component_moves[:tutor_body],
          component_moves[:tutor_head]
        ), nil, nil, slot[:id], slot[:location]
      )
    end

    if fusion_move_access_species?(species)
      pokemon ||= Pokemon.new(species.id, 1)
      [:regular, :legendary].each do |channel|
        moves = if randomized
                  generated_specialized_tutor_moves_for(
                    pokemon, channel, generator
                  )
                else
                  original_specialized_tutor_moves_for(pokemon, channel)
                end
        moves.each do |move|
          tutor_name = channel == :legendary ?
            "Fusion Move Tutor (Legendary)" : "Fusion Move Tutor (Regular)"
          tutors << tracker_move_access_entry(
            move, "fusion_tutor_#{channel}", tutors.length,
            "Displayed fusion", nil, nil,
            "fusion_tutor:#{channel}", tutor_name
          )
        end
      end
    end

    ordinary_supported = tutors.count do |entry|
      entry && entry["source"] == "ordinary_tutor"
    end
    return {
      "learnset" => learnset,
      "egg_moves" => egg,
      "machine_moves" => machines,
      "tutor_moves" => tutors.compact,
      "ordinary_tutor_abstract_count" => abstract_tutor.length,
      "ordinary_tutor_supported_count" => ordinary_supported
    }
  end

  def self.tracker_move_access_generator(recipe)
    @tracker_move_access_generators ||= {}
    key = [recipe["seed"], recipe["move_source_fingerprint"]]
    cached = @tracker_move_access_generators[key]
    return cached if cached
    @tracker_move_access_generators[key] = MoveAccessGenerator.new(
      recipe["seed"], allowed_level_up_move_pool,
      level_up_move_pool_fingerprint, move_access_source_fingerprint
    )
    return @tracker_move_access_generators[key]
  end

  def self.tracker_move_access_component_moves(species, generator, randomized)
    return {} if !fusion_move_access_species?(species)
    body = species.body_pokemon
    head = species.head_pokemon
    return {
      :level_body => randomized ? generator.moves_for(body).map { |entry| entry[1] } :
        original_level_up_moves_for(body).map { |entry| entry[1] },
      :level_head => randomized ? generator.moves_for(head).map { |entry| entry[1] } :
        original_level_up_moves_for(head).map { |entry| entry[1] },
      :egg_body => randomized ? generator.egg_moves_for(body) : original_egg_moves_for(body),
      :egg_head => randomized ? generator.egg_moves_for(head) : original_egg_moves_for(head),
      :tm_body => randomized ? generator.tm_moves_for(body) : original_machine_moves_for(body, :tm),
      :tm_head => randomized ? generator.tm_moves_for(head) : original_machine_moves_for(head, :tm),
      :tr_body => randomized ? generator.tr_moves_for(body) : original_machine_moves_for(body, :tr),
      :tr_head => randomized ? generator.tr_moves_for(head) : original_machine_moves_for(head, :tr),
      :tutor_body => randomized ? generator.tutor_moves_for(body) : original_ordinary_tutor_moves_for(body),
      :tutor_head => randomized ? generator.tutor_moves_for(head) : original_ordinary_tutor_moves_for(head)
    }
  end

  def self.tracker_move_access_source_label(species, move, body_moves,
                                            head_moves)
    return "Species" if !fusion_move_access_species?(species)
    body = body_moves && body_moves.include?(move)
    head = head_moves && head_moves.include?(move)
    return "Body + Head" if body && head
    return "Body" if body
    return "Head" if head
    return "Fusion"
  end

  def self.tracker_move_access_machine_entries(species, channel, moves,
                                               body_moves, head_moves,
                                               offset = 0)
    entries = []
    machine_item_roster(channel).each do |item|
      next if !moves.include?(item.move)
      entries << tracker_move_access_entry(
        item.move, channel.to_s, offset + entries.length,
        tracker_move_access_source_label(
          species, item.move, body_moves, head_moves
        ), nil, item
      )
    end
    return entries.compact
  end

  def self.tracker_move_access_entry(move_id, source, order, source_label,
                                     learned_level = nil, item = nil,
                                     tutor_id = nil, tutor_name = nil)
    move = GameData::Move.try_get(move_id)
    return nil if !move
    return {
      "id" => move.id.to_s,
      "name" => move.name,
      "learned_level" => learned_level,
      "learn_order" => order,
      "source" => source,
      "source_label" => source_label,
      "origin" => "generated_lookup",
      "type" => move.type.to_s,
      "category" => tracker_move_category(move),
      "description" => move.description,
      "power" => move.base_damage || 0,
      "accuracy" => move.accuracy || 0,
      "total_pp" => move.total_pp,
      "pp_after_use" => nil,
      "item_id" => item ? item.id.to_s : nil,
      "item_name" => item ? item.name : nil,
      "tutor_id" => tutor_id,
      "tutor_name" => tutor_name
    }
  end

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

  def self.tracker_evolution_predecessor_snapshot(branch)
    source = GameData::Species.get(branch[:source].to_sym)
    return {
      "species_id" => "#{source.id}:0",
      "species_name" => source.name,
      "sprite_path" => tracker_lookup_sprite_path(source),
      "base_stat_total" => branch[:source_bst],
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
      "effective_methods" => branch[:effective_methods].map do |method|
        tracker_evolution_snapshot(
          method[:method], method[:parameter]
        )["requirement"]
      end
    }
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

  def self.tracker_lookup_wild_occurrences(target, recipe)
    if recipe["species_generator_version"] ==
       SpeciesGenerator::LEGACY_SCHEMA_VERSION
      return tracker_lookup_legacy_wild_occurrences(target, recipe)
    end
    return [] if !SpeciesGenerator::SLOT_SCHEMA_VERSIONS.include?(
      recipe["species_generator_version"]
    )
    configuration_value = Configuration.from(recipe["configuration"])
    generator = if tracker_loaded_recipe?(recipe)
                  species_generator(:wild)
                 else
                  SpeciesGenerator.new(
                    recipe["seed"], :wild, configuration_value.wild_policy,
                    normal_species_pool, custom_fusion_pool, {},
                    recipe["species_generator_version"]
                  )
                end
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
            next if mapped != target.id
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
    table = tracker_lookup_wild_fusion_table(
      recipe, generator, mapper, mode, data, encounter_type, entries
    )
    return if !table[:target_ids][target.id_number]
    mapped_entries = table[:mapped_entries]
    mapped_entries.each do |body_entry, body, body_slot|
      mapped_entries.each do |head_entry, head, head_slot|
        next if mapper.species_number(body, head) != target.id_number
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

  def self.tracker_lookup_wild_fusion_table(
    recipe, generator, mapper, mode, data, encounter_type, entries
  )
    key = [recipe["run_id"], mode.name, data.map, data.version,
           encounter_type]
    cached = tracker_wild_fusion_tables[key]
    return cached if cached
    mapped_entries = entries.each_with_index.map do |entry, slot|
      context = [:table, mode.name, data.map, data.version,
                 encounter_type, slot]
      [entry, generator.map(entry[1], context), slot]
    end
    target_ids = {}
    mapped_entries.each do |_body_entry, body, _body_slot|
      mapped_entries.each do |_head_entry, head, _head_slot|
        target_ids[mapper.species_number(body, head)] = true
      end
    end
    table = {
      :mapped_entries => mapped_entries.freeze,
      :target_ids => target_ids.freeze
    }.freeze
    tracker_wild_fusion_tables[key] = table
    return table
  end

  def self.tracker_lookup_trainer_occurrences(target, recipe)
    if recipe["species_generator_version"] ==
       SpeciesGenerator::LEGACY_SCHEMA_VERSION
      return tracker_lookup_legacy_trainer_occurrences(target, recipe)
    end
    return [] if !SpeciesGenerator::SLOT_SCHEMA_VERSIONS.include?(
      recipe["species_generator_version"]
    )
    configuration_value = Configuration.from(recipe["configuration"])
    generator = if tracker_loaded_recipe?(recipe)
                  species_generator(:trainer)
                 else
                  SpeciesGenerator.new(
                    recipe["seed"], :trainer,
                    configuration_value.trainer_policy,
                    normal_species_pool, custom_fusion_pool, {},
                    recipe["species_generator_version"]
                  )
                end
    occurrences = []
    tracker_trainer_data_mode(recipe).list_all.each do |_trainer_id, trainer|
      trainer.pokemon.each_with_index do |pokemon, slot|
        source = GameData::Species.get(pokemon[:species])
        mapped = generator.map(source.id, [:pbs, trainer.id, slot])
        next if mapped != target.id
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
    return recipe["active_run"] == true &&
      recipe["run_id"] == $PokemonGlobal.ironmon_run_id
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
