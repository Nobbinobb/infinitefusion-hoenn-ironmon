#===============================================================================
# Ironmon completed-run Pokemon and move reconstruction
#===============================================================================

module Ironmon
  def self.tracker_species_pool_fingerprint
    return species_pool_fingerprint(normal_species_pool)
  end

  def self.tracker_lookup_species_pool(_recipe)
    return (normal_species_pool + custom_fusion_pool).uniq
  end

  def self.tracker_search_matches(recipe, normalized_query, normal_only)
    index_key = tracker_search_index_key(recipe, normal_only)
    cache_key = "#{index_key}|#{normalized_query}"
    cached = tracker_search_result_cache[cache_key]
    return cached if cached
    matches = tracker_search_index(recipe, true).map do |entry|
      next if !entry[2].include?(normalized_query)
      {
        "species_id" => entry[0],
        "species_name" => entry[1],
        "fusion" => entry[3]
      }
    end.compact
    append_tracker_fusion_search_matches(
      matches, normalized_query
    ) if !normal_only
    matches.sort_by! do |match|
      name = match["species_name"].downcase
      [name.start_with?(normalized_query) ? 0 : 1, name]
    end
    tracker_store_bounded(tracker_search_result_cache, cache_key, matches.freeze, 64)
    return matches
  end

  def self.append_tracker_fusion_search_matches(matches, normalized_query)
    identity_query = /\A(?:b\d*(?:h\d*)?|\d+h\d*|h\d+|\d+)\z/.match?(
      normalized_query
    )
    custom_fusion_pool_numbers.each do |species_number|
      body_id = (species_number - 1) / NB_POKEMON
      head_id = species_number - (body_id * NB_POKEMON)
      name = tracker_search_fusion_name(body_id, head_id)
      next if !name
      identity = nil
      name_match = name.downcase.include?(normalized_query)
      if !name_match && identity_query
        identity = "B#{body_id}H#{head_id}"
        next if !identity.downcase.include?(normalized_query)
      elsif !name_match
        next
      end
      identity ||= "B#{body_id}H#{head_id}"
      matches << {
        "species_id" => "#{identity}:0",
        "species_name" => name,
        "fusion" => true
      }
    end
  end

  def self.tracker_search_index(recipe, normal_only)
    key = tracker_search_index_key(recipe, normal_only)
    return tracker_search_indexes[key] if tracker_search_indexes[key]
    species_pool = normal_species_pool
    index = species_pool.map do |species_id|
      name = tracker_search_species_name(species_id)
      next if !name
      stable_id = "#{species_id}:0"
      fusion = /\AB\d+H\d+\z/.match?(species_id.to_s)
      [stable_id, name, "#{name} #{species_id}".downcase, fusion]
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
    prefixes, suffixes = tracker_search_fusion_name_parts
    prefix = prefixes[head_id]
    suffix = suffixes[body_id]
    return nil if !prefix || !suffix
    prefix = prefix[0..-2] if prefix[-1] == suffix[0]
    suffix = suffix.capitalize if prefix.end_with?(" ")
    return prefix + suffix
  rescue Exception
    return nil
  end

  def self.tracker_search_fusion_name_parts
    return @tracker_search_fusion_name_parts if
      @tracker_search_fusion_name_parts
    prefixes = []
    suffixes = []
    (1..NB_POKEMON).each do |species_id|
      dex = GameData::NAT_DEX_MAPPING[species_id] || species_id
      split = GameData::SPLIT_NAMES[dex]
      next if !split
      prefixes[species_id] = split[0]
      suffixes[species_id] = split[1]
    end
    @tracker_search_fusion_name_parts = [
      prefixes.freeze, suffixes.freeze
    ].freeze
    return @tracker_search_fusion_name_parts
  end

  def self.tracker_lookup_species_available?(species)
    return true if normal_species_pool.include?(species.id)
    return true if custom_fusion_pool.include?(species.id)
    return false
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
end
