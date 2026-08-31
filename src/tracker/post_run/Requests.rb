#===============================================================================
# Ironmon completed-run tracker requests and recipe validation
#===============================================================================

module Ironmon
  def self.tracker_pokemon_search(payload, envelope_run_id)
    payload ||= {}
    recipe = tracker_validate_completed_recipe(payload["recipe"], envelope_run_id)
    return tracker_pokemon_search_for_recipe(payload, recipe)
  end

  def self.tracker_pokemon_search_for_recipe(payload, recipe, visibility = nil)
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
    tracker_debug_search_trace("matching_started") if
      respond_to?(:tracker_debug_search_trace)
    matches = tracker_search_matches(recipe, normalized_query, normal_only)
    tracker_debug_search_trace("matching_ready") if
      respond_to?(:tracker_debug_search_trace)
    page = matches.slice(offset, limit) || []
    if !visibility || visibility[:obtainability]
      page = page.each_with_index.map do |match, index|
        tracker_debug_search_trace("annotation_#{index}_started") if
          respond_to?(:tracker_debug_search_trace)
        species_key = match["species_id"].to_s.split(":", 2)[0]
        status = tracker_obtainability_identity_status(species_key, recipe)
        next match if !status
        match.merge(
          "obtainability_status" => status
        )
      end
    end
    tracker_debug_search_trace("annotation_ready") if
      respond_to?(:tracker_debug_search_trace)
    return {
      "matches" => page,
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

  def self.tracker_evolution_predecessor_search(payload, envelope_run_id)
    payload ||= {}
    recipe = tracker_validate_completed_recipe(payload["recipe"], envelope_run_id)
    return tracker_evolution_predecessor_search_for_recipe(payload, recipe)
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
    membership_species = payload["fusion_membership_species_id"]
    if kind == :wild && membership_species && membership_species != species_id
      raise TrackerLookupError.new(
        "fusion_material_target_mismatch",
        "The tracker material membership does not match the represented Pokemon."
      )
    end
    cache_key = tracker_occurrence_cache_key(recipe, species, kind)
    occurrences = tracker_lookup_cache[cache_key]
    if !occurrences
      occurrences = if kind == :wild
                      tracker_wild_occurrence_work(
                        cache_key, species, recipe,
                        payload["fusion_material_membership"]
                      ).results
                    else
                      tracker_lookup_trainer_occurrences(species, recipe)
                    end
      return { "matches" => [], "total" => 0, "pending" => true } if
        occurrences.nil?
      tracker_store_bounded(tracker_lookup_cache, cache_key, occurrences, 256)
    end
    return {
      "matches" => occurrences.slice(offset, limit) || [],
      "total" => occurrences.length
    }
  end

  def self.tracker_fusion_material_search_for_recipe(
    payload, recipe, obtainability = true
  )
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
    assignments = payload["material_assignments"]
    assignment_total = payload["material_assignment_total"]
    if assignments.is_a?(Array) && !assignment_total.nil?
      assignment_species_id = payload["material_assignment_species_id"]
      if assignment_species_id && assignment_species_id != species_id
        raise TrackerLookupError.new(
          "fusion_material_target_mismatch",
          "The tracker material page does not match the represented Pokemon."
        )
      end
      return tracker_lookup_fusion_material_assignments(
        assignments, assignment_total.to_i, recipe, obtainability
      )
    end
    mapper = tracker_post_run_fusion_mapper(recipe)
    if mapper.respond_to?(:material_pair_assignments_required?) &&
       mapper.material_pair_assignments_required?
      raise TrackerLookupError.new(
        "fusion_material_assignments_required",
        "The tracker fusion-material worker has not supplied this page."
      )
    end
    return tracker_lookup_fusion_materials(
      species, mapper, offset, limit, recipe, obtainability
    )
  end

  def self.tracker_evolution_candidate_search_for_recipe(
    payload, recipe, obtainability = true
  )
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
    targets = if fusion_evolution_runtime_species?(species) &&
      payload["packed_candidate_assignments"].is_a?(String)
                tracker_fusion_evolution_candidates_for_assignments(
                  payload["packed_candidate_assignments"]
                )
              else
                tracker_evolution_candidates_for(species, recipe, side)
              end
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
        tracker_evolution_candidate_snapshot(
          entry[0], entry[1], recipe, obtainability
        )
      end,
      "total" => matches.length
    }
  end

  def self.tracker_evolution_predecessor_search_for_recipe(
    payload, recipe, obtainability = true
  )
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
    limit = 8 if limit == 0
    if offset < 0 || limit < 1 || limit > TRACKER_SEARCH_LIMIT
      raise TrackerLookupError.new(
        "invalid_page", "Predecessor pages must contain between 1 and 50 targets."
      )
    end
    return tracker_lookup_evolution_predecessor_page(
      species, recipe, offset, limit, obtainability,
      payload["predecessor_assignments"]
    )
  end

  def self.tracker_pokemon_lookup_for_recipe(payload, recipe, visibility = nil)
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

    visibility_key = visibility ? visibility.sort_by { |entry| entry[0].to_s }.to_s : "all"
    cache_key = "#{recipe["run_id"]}|#{species.id}|#{section}|#{visibility_key}"
    cached = tracker_lookup_cache[cache_key]
    return cached if cached

    result = {
      "section" => section,
      "identity" => {
        "species_id" => "#{species.id}:0",
        "species_name" => species.name,
        "sprite_path" => tracker_lookup_sprite_path(species),
        "types" => species.types.map { |type| type.to_s },
        "fusion" => !normal_ability_species?(species),
        "obtainability" => if !visibility || visibility[:obtainability]
                             tracker_obtainability_snapshot(species, recipe)
                           else
                             {
                               "status" => "calculating",
                               "reason" => "Run obtainability access is not authorized.",
                               "path" => [],
                               "required_items" => {}
                             }
                           end
      }
    }

    case section
    when "overview"
      fusion_mapper = tracker_post_run_fusion_mapper(recipe)
      result["overview"] = {
        "wild_occurrences" => visibility && !visibility[:wild] ?
          { "matches" => [], "total" => 0 } :
          { "matches" => [], "total" => 0, "pending" => true },
        "trainer_occurrences" => visibility && !visibility[:trainer] ?
          { "matches" => [], "total" => 0 } :
          tracker_occurrence_search_for_recipe(
            { "species_id" => species_id, "offset" => 0, "limit" => 50 },
            recipe, :trainer
          ),
        "fusion_bases" => visibility && !visibility[:overview] ?
          [] : tracker_lookup_fusion_bases(
            species, recipe, !visibility || visibility[:obtainability]
          ),
        "reverse_fusion" => visibility && !visibility[:overview] ?
          nil : tracker_lookup_reverse_fusion(
            species, fusion_mapper, recipe,
            !visibility || visibility[:obtainability]
          ),
        "fusion_materials" => { "matches" => [], "total" => 0 }
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
      show_results = !visibility || visibility[:evolution_results]
      show_obtainability = !visibility || visibility[:obtainability]
      evolution_targets = show_results ?
        tracker_lookup_evolution_targets(
          species, recipe, show_obtainability,
          payload["evolution_assignments"]
        ) :
        { :normal => [], :head => [], :body => [] }
      generated_evolutions = tracker_evolution_recipe?(recipe)
      generated_stats = show_results ?
        tracker_lookup_generated_base_stats(species, recipe) : {}
      result["evolutions"] = {
        "current_stage_level" => tracker_evolution_graph_level(species),
        "current_base_stat_total" => show_results ?
          tracker_base_stat_total(generated_stats) : 0,
        "native_targets" => !show_results || generated_evolutions ?
          [] : tracker_lookup_evolutions(
            species, recipe, show_obtainability
          ),
        "native_predecessors" => !show_results || generated_evolutions ?
          [] : tracker_lookup_previous_evolutions(
            species, recipe, show_obtainability
          ),
        "generated_predecessors" => [],
        "generated_targets" => evolution_targets[:normal],
        "head_targets" => evolution_targets[:head],
        "body_targets" => evolution_targets[:body],
        "generator" => visibility && !visibility[:evolution_generator] ?
          {} : tracker_lookup_evolution_generator(recipe)
      }
    end
    tracker_store_bounded(tracker_lookup_cache, cache_key, result, 256) if
      !tracker_response_has_calculating_obtainability?(result)
    return result
  end

  def self.tracker_response_has_calculating_obtainability?(value)
    if value.is_a?(Array)
      return value.any? do |entry|
        tracker_response_has_calculating_obtainability?(entry)
      end
    end
    return false if !value.is_a?(Hash)
    return true if value["status"] == "calculating" ||
      value["obtainability_status"] == "calculating"
    return value.values.any? do |entry|
      tracker_response_has_calculating_obtainability?(entry)
    end
  end

  def self.tracker_fusion_preview(payload, envelope_run_id)
    payload ||= {}
    recipe = tracker_validate_completed_recipe(payload["recipe"], envelope_run_id)
    return tracker_fusion_preview_for_recipe(payload, recipe)
  end

  def self.tracker_fusion_preview_for_recipe(
    payload, recipe, obtainability = true
  )
    first = tracker_lookup_normal_species(payload["first_species_id"])
    second = tracker_lookup_normal_species(payload["second_species_id"])
    mapper = tracker_post_run_fusion_mapper(recipe)
    orientations = [[first, second], [second, first]].uniq do |pair|
      [pair[0].id, pair[1].id]
    end
    outcomes = orientations.map do |body, head|
      result = GameData::Species.get(mapper.species(body.id, head.id))
      service = tracker_obtainability_services[recipe["run_id"]]
      service.prove_player_fusion_pair(body, head, result) if
        obtainability && service
      {
        "body" => tracker_lookup_relation(
          body, "Body material", recipe, obtainability
        ),
        "head" => tracker_lookup_relation(
          head, "Head material", recipe, obtainability
        ),
        "result" => tracker_lookup_relation(
          result, "Ironmon result", recipe, obtainability
        )
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
    if !PlayerFusionMapper::SUPPORTED_SCHEMA_VERSIONS.include?(
      recipe["player_fusion_generator_version"]
    )
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
    tracker_validate_item_recipe(recipe)
    return recipe
  end

  def self.tracker_validate_item_recipe(recipe)
    version = recipe["item_generator_version"]
    return true if !version
    rules = recipe["item_pool_rules_version"]
    if version != ItemSlotGenerator::SCHEMA_VERSION ||
       !ItemSlotGenerator::RESULT_BANS_BY_RULES_VERSION.key?(rules)
      raise TrackerLookupError.new(
        "generator_unavailable", "The required item generator is unavailable."
      )
    end
    expected = [
      item_ground_pool(rules).length,
      item_ground_pool_fingerprint(rules),
      item_tm_pool(rules).length,
      item_tm_pool_fingerprint(rules),
      item_result_ban_fingerprint(rules),
      ItemSlotGenerator::SHOP_POLICY_VERSION
    ]
    actual = [
      recipe["item_ground_pool_size"],
      recipe["item_ground_pool_fingerprint"],
      recipe["item_tm_pool_size"],
      recipe["item_tm_pool_fingerprint"],
      recipe["item_result_ban_fingerprint"],
      recipe["item_shop_policy_version"]
    ]
    if actual != expected
      raise TrackerLookupError.new(
        "incompatible_item_pool", "The item pools no longer match this run."
      )
    end
    if rules >= ItemSlotGenerator::WEIGHTED_POOL_RULES_VERSION &&
       recipe["item_ground_total_weight"] != item_ground_total_weight(rules)
      raise TrackerLookupError.new(
        "incompatible_item_pool", "The item weights no longer match this run."
      )
    end
    return true
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
    items = recipe["item_generator"] || {}
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
      "item_generator" => recipe["item_generator"],
      "item_generator_version" => items["version"],
      "item_pool_rules_version" => items["rules_version"],
      "item_ground_pool_size" => items["ground_pool_size"],
      "item_ground_pool_fingerprint" => items["ground_pool_fingerprint"],
      "item_ground_total_weight" => items["ground_total_weight"],
      "item_tm_pool_size" => items["tm_pool_size"],
      "item_tm_pool_fingerprint" => items["tm_pool_fingerprint"],
      "item_result_ban_fingerprint" => items["result_ban_fingerprint"],
      "item_shop_policy_version" => items["shop_policy_version"],
      "item_mappings" => recipe["item_mappings"] || {},
      "tm_mappings" => recipe["tm_mappings"] || {}
    }
  end
end
