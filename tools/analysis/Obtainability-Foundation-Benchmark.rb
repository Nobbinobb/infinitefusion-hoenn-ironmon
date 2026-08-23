module IronmonObtainabilityFoundationBenchmark
  OUTPUT_PATH = $ironmon_obtainability_benchmark_output_path.to_s
  PROGRESS_PATH = "#{OUTPUT_PATH}.progress"
  SEED = $ironmon_obtainability_benchmark_seed.to_i
  AREA_CATALOG_PATH = $ironmon_obtainability_benchmark_area_catalog_path.to_s
  FULL_MATERIAL_SCAN = $ironmon_obtainability_benchmark_full_material_scan == true
  INVERSE_TARGET_COUNT = $ironmon_obtainability_benchmark_inverse_target_count.to_i
  MATERIAL_SAMPLE_LIMITS = [64, 256].freeze
  POLICIES = [
    ["normal_only", Ironmon::Configuration::POLICY_NORMAL_ONLY],
    ["mixed", Ironmon::Configuration::POLICY_MIXED],
    ["custom_fusions_only", Ironmon::Configuration::POLICY_CUSTOM_FUSIONS_ONLY]
  ].freeze
  EXCLUDED_MAP_NAME = /\A(?:EVENT_TEMPLATES|QUEST_TEMPLATES|testing)\z|\Aquest_/i

  def self.assert(condition, message)
    raise "Obtainability foundation benchmark failed: #{message}" if !condition
  end

  def self.elapsed_milliseconds(started_at)
    return ((Time.now - started_at) * 1000.0).round(3)
  end

  def self.measure
    started_at = Time.now
    value = yield
    return value, elapsed_milliseconds(started_at)
  end

  def self.write_progress(phase)
    File.binwrite(PROGRESS_PATH, "phase=#{phase}\n")
  end

  def self.area_catalog
    document = File.open(AREA_CATALOG_PATH, "rb") do |file|
      Marshal.load(file)
    end
    Ironmon.tracker_validate_area_catalog(document)
    return document
  end

  def self.item_slot_ids(document)
    slots = []
    document["areas"].each do |area|
      area["items"].each do |entry|
        randomizable = entry["authored_item_ids"].any? do |item_id|
          item = GameData::Item.try_get(item_id.to_sym)
          item && Ironmon.item_ground_source_randomizable?(item)
        end
        slots << "map:#{entry["map_id"]}|event:#{entry["event_id"]}" if
          randomizable
      end
    end
    Ironmon::ItemSlotGenerator::SPECIAL_GROUND_SLOTS.each_value do |slot|
      slots << slot[0]
    end
    return slots.uniq.sort
  end

  def self.required_evolution_items(catalog)
    items = {}
    catalog.branch_catalog.each do |branch|
      branch[:effective_methods].each do |method|
        method_data = GameData::Evolution.get(method[:method])
        next if method_data.parameter != :Item
        item = GameData::Item.get(method[:parameter])
        items[item.id] = true
      end
    end
    items[:POKEBALL] = true
    return items.keys.sort_by(&:to_s)
  end

  def self.collect_strings(value, result)
    if value.is_a?(String)
      result << value
    elsif value.is_a?(Array)
      value.each { |entry| collect_strings(entry, result) }
    elsif value.is_a?(Hash)
      value.each do |key, entry|
        collect_strings(key, result)
        collect_strings(entry, result)
      end
    end
  end

  def self.command_script(commands)
    result = []
    commands.each { |command| collect_strings(command.parameters, result) }
    return result.join("\n")
  end

  def self.authored_required_item_gifts(required)
    required_index = {}
    required.each { |item_id| required_index[item_id] = true }
    scripts = []
    map_infos = load_data("Data/MapInfos.rxdata")
    Dir.glob(File.join("Data", "Map[0-9][0-9][0-9].rxdata")).sort.each do |path|
      map_id = File.basename(path)[/\d+/].to_i
      info = map_infos[map_id]
      next if !info || info.name.to_s.match?(EXCLUDED_MAP_NAME)
      map = load_data(path)
      map.events.each_value do |event|
        script = event.pages.map do |page|
          command_script(page.list)
        end.join("\n")
        scripts << script
      end
    end
    common_events = load_data("Data/CommonEvents.rxdata")
    common_events.compact.each do |event|
      scripts << command_script(event.list)
    end
    counts = Hash.new(0)
    scripts.each do |script|
      script.scan(/pbReceiveItem\(\s*:([A-Za-z0-9_]+)/).flatten.each do |item_id|
        identity = item_id.to_sym
        counts[identity] += 1 if required_index[identity]
      end
    end
    return counts
  end

  def self.encounter_catalog(mode)
    rows = []
    mode.each { |data| rows << data }
    if mode != GameData::Encounter
      GameData::Encounter.each do |data|
        rows << data if !mode.get(data.map, data.version)
      end
    end
    return rows
  end

  def self.benchmark_encounters(normal_pool, fusion_pool)
    modes = [["classic", GameData::Encounter]]
    modes << ["remix", GameData::EncounterModern] if
      defined?(GameData::EncounterModern)
    results = []
    normal_material_seeds = {}
    modes.each do |mode_name, mode|
      entries = encounter_catalog(mode)
      POLICIES.each do |policy_name, policy|
        generator = Ironmon::SpeciesGenerator.new(
          SEED, :wild, policy, normal_pool, fusion_pool, {}
        )
        mapped, elapsed = measure do
          values = []
          entries.each do |data|
            data.types.each do |encounter_type, slots|
              slots.each_with_index do |entry, slot|
                values << generator.map(
                  entry[1],
                  [:table, mode.name, data.map, data.version,
                   encounter_type, slot]
                )
              end
            end
          end
          values
        end
        normal = mapped.select do |identity|
          GameData::Species.get(identity).id_number <= NB_POKEMON
        end.uniq
        fusion = mapped.uniq - normal
        unfusion_components = fusion.flat_map do |identity|
          species = GameData::Species.get(identity)
          if species.respond_to?(:get_body_species_symbol) &&
             species.respond_to?(:get_head_species_symbol)
            [species.get_body_species_symbol, species.get_head_species_symbol]
          else
            []
          end
        end.uniq
        material_seeds = (normal + unfusion_components).uniq
        scenario = "#{mode_name}|#{policy_name}"
        normal_material_seeds[scenario] = {
          "mode" => mode_name,
          "policy" => policy_name,
          "species" => material_seeds
        }
        results << {
          "mode" => mode_name,
          "policy" => policy_name,
          "slot_count" => mapped.length,
          "mapping_count" => generator.mapping.length,
          "unique_results" => mapped.uniq.length,
          "unique_normal_results" => normal.length,
          "unique_fusion_results" => fusion.length,
          "unique_unfusion_components" => unfusion_components.length,
          "normal_material_seed_species" => material_seeds.length,
          "elapsed_milliseconds" => elapsed
        }
      end
    end
    return results, normal_material_seeds
  end

  def self.benchmark_starters(normal_pool, fusion_pool)
    POLICIES.map do |policy_name, policy|
      generator = Ironmon::SpeciesGenerator.new(
        SEED, :wild, policy, normal_pool, fusion_pool, {}
      )
      results, elapsed = measure do
        [1, 4, 7].each_with_index.map do |source, slot|
          generator.map(source, [:starter, slot])
        end
      end
      {
        "policy" => policy_name,
        "choice_slots" => results.length,
        "results" => results.map(&:to_s),
        "elapsed_milliseconds" => elapsed,
        "detail" => "choices are exclusive; fused starters cannot unfuse"
      }
    end
  end

  def self.benchmark_items(document, catalog)
    slots = item_slot_ids(document)
    required = required_evolution_items(catalog)
    generator = Ironmon.build_item_slot_generator(
      SEED, Ironmon::ItemSlotGenerator::POOL_RULES_VERSION
    )
    results, elapsed = measure do
      slots.map { |slot_id| generator.ground_item(slot_id) }
    end
    counts = Hash.new(0)
    results.each { |item_id| counts[item_id] += 1 }
    required_counts = {}
    required.each { |item_id| required_counts[item_id.to_s] = counts[item_id] }
    gift_counts, gift_elapsed = measure do
      authored_required_item_gifts(required)
    end
    gift_occurrences = {}
    required.each do |item_id|
      gift_occurrences[item_id.to_s] = gift_counts[item_id]
    end
    return {
      "slot_count" => slots.length,
      "unique_results" => counts.length,
      "required_item_types" => required.length,
      "available_required_item_types" => required_counts.values.count do |count|
        count > 0
      end,
      "randomized_ground_required_item_quantities" => required_counts,
      "authored_required_item_gift_occurrences" => gift_occurrences,
      "candidate_required_item_types" => required.count do |item_id|
        counts[item_id] > 0 || gift_counts[item_id] > 0
      end,
      "gift_detail" => "authored gift occurrences are conditional candidates, not guaranteed simultaneous supply",
      "elapsed_milliseconds" => elapsed,
      "authored_gift_scan_milliseconds" => gift_elapsed
    }, slots
  end

  def self.normal_evolution_generator(catalog)
    return Ironmon::NormalEvolutionGenerator.new(
      SEED, catalog, Ironmon::BaseStatGenerator::SCHEMA_VERSION,
      Ironmon.base_stat_source_fingerprint
    )
  end

  def self.benchmark_normal_closures(catalog, material_seed_scenarios)
    generator = normal_evolution_generator(catalog)
    graph, graph_elapsed = measure { generator.graph }
    closures = {}
    closure_results = material_seed_scenarios.keys.sort.map do |scenario|
      source = material_seed_scenarios[scenario]
      encounter_species = source["species"]
      reachable, closure_elapsed = measure do
        found = {}
        pending = encounter_species.dup
        until pending.empty?
          identity = GameData::Species.get(pending.shift).id
          next if found[identity]
          found[identity] = true
          (graph[identity] || []).each do |branch|
            pending << branch[:target] if !found[branch[:target]]
          end
        end
        found.keys
      end
      closures[scenario] = {
        "mode" => source["mode"],
        "policy" => source["policy"],
        "species" => reachable
      }
      {
        "mode" => source["mode"],
        "policy" => source["policy"],
        "seed_species" => encounter_species.length,
        "reachable_species" => reachable.length,
        "closure_milliseconds" => closure_elapsed
      }
    end
    return {
      "generated_sources" => graph.length,
      "generated_branches" => graph.values.inject(0) do |sum, branches|
        sum + branches.length
      end,
      "graph_build_milliseconds" => graph_elapsed,
      "modes" => closure_results
    }, closures
  end

  def self.player_fusion_mapper(fusion_pool)
    return Ironmon::PlayerFusionMapper.new(
      SEED, fusion_pool, {}, {},
      Ironmon::BaseStatGenerator.new(
        SEED, Ironmon.base_stat_source_fingerprint
      )
    )
  end

  def self.benchmark_material_mapping(fusion_pool, reachable_species, limit)
    identities = reachable_species.sort_by do |identity|
      GameData::Species.get(identity).id_number
    end
    identities = identities.first(limit) if limit && limit > 0
    mapper = player_fusion_mapper(fusion_pool)
    unique_results = {}
    pair_count = 0
    orientation_count = 0
    _value, elapsed = measure do
      identities.each_with_index do |first, first_index|
        (first_index...identities.length).each do |second_index|
          second = identities[second_index]
          unique_results[mapper.species_number(first, second)] = true
          pair_count += 1
          orientation_count += 1
          if first != second
            unique_results[mapper.species_number(second, first)] = true
            orientation_count += 1
          end
        end
      end
    end
    record = {
      "material_species" => identities.length,
      "unordered_pairs" => pair_count,
      "oriented_queries" => orientation_count,
      "stored_mappings" => mapper.instance_variable_get(:@mappings).length,
      "unique_fusion_results" => unique_results.length,
      "elapsed_milliseconds" => elapsed
    }
    return record, unique_results.keys
  end

  def self.sample_targets(targets, count)
    count = [count, targets.length].min
    return [] if count <= 0
    return (0...count).map do |index|
      target_index = if count == 1
                       targets.length / 2
                     else
                       (index * (targets.length - 1)) / (count - 1)
                     end
      targets[target_index]
    end.uniq
  end

  def self.inverse_targets(fusion_pool, mapped_targets)
    count = [INVERSE_TARGET_COUNT, fusion_pool.length].min
    return [] if count <= 0
    result = sample_targets(mapped_targets.sort, count).map do |target|
      ["known_forward_result", target]
    end
    pool_miss = fusion_pool.find do |target|
      !mapped_targets.include?(GameData::Species.get(target).id_number)
    end
    result << ["arbitrary_pool_result", pool_miss] if pool_miss
    return result
  end

  def self.benchmark_inverse_lookup(fusion_pool, reachable_species,
                                    mapped_targets)
    reachable_ids = {}
    reachable_species.each do |identity|
      reachable_ids[GameData::Species.get(identity).id_number] = true
    end
    inverse_targets(fusion_pool, mapped_targets).map do |kind, target|
      mapper = player_fusion_mapper(fusion_pool)
      pairs, elapsed = measure { mapper.material_pairs_for(target) }
      possible = pairs.count do |body_id, head_id|
        reachable_ids[body_id] && reachable_ids[head_id]
      end
      {
        "kind" => kind,
        "target" => GameData::Species.get(target).id.to_s,
        "material_pairs" => pairs.length,
        "reachable_material_pairs" => possible,
        "elapsed_milliseconds" => elapsed
      }
    end
  end

  def self.write_report(report)
    temporary_path = "#{OUTPUT_PATH}.tmp"
    File.binwrite(temporary_path, JSON.generate(report) + "\n")
    File.delete(OUTPUT_PATH) if File.file?(OUTPUT_PATH)
    File.rename(temporary_path, OUTPUT_PATH)
  end

  def self.run
    write_progress("loading catalogs")
    document = area_catalog
    catalog = Ironmon.evolution_catalog
    catalog.validate
    normal_pool = Ironmon.normal_species_pool
    fusion_pool = Ironmon.custom_fusion_pool

    write_progress("encounter enumeration")
    encounter_results, material_seed_scenarios = benchmark_encounters(
      normal_pool, fusion_pool
    )
    assert(!material_seed_scenarios.empty?,
           "encounters produced no normal species")

    write_progress("starter enumeration")
    starter_results = benchmark_starters(normal_pool, fusion_pool)

    write_progress("item supply enumeration")
    item_result, item_slots = benchmark_items(document, catalog)
    assert(!item_slots.empty?, "the item supply contains no slots")

    write_progress("normal evolution closure")
    closure_result, reachable_scenarios = benchmark_normal_closures(
      catalog, material_seed_scenarios
    )

    material_scenario, material_source = reachable_scenarios.max_by do |_scenario, source|
      source["species"].length
    end
    reachable_species = material_source["species"]

    material_limits = MATERIAL_SAMPLE_LIMITS.select do |limit|
      limit <= reachable_species.length
    end
    if FULL_MATERIAL_SCAN && !material_limits.include?(reachable_species.length)
      material_limits << reachable_species.length
    end
    write_progress("player fusion material mapping")
    mapped_targets = []
    material_results = material_limits.map do |limit|
      record, targets = benchmark_material_mapping(
        fusion_pool, reachable_species, limit
      )
      record["source_scenario"] = material_scenario
      mapped_targets = targets if targets.length > mapped_targets.length
      record
    end

    write_progress("single target inverse lookup")
    inverse_results = benchmark_inverse_lookup(
      fusion_pool, reachable_species, mapped_targets
    )

    report = {
      "schema_version" => 1,
      "purpose" => "preimplementation obtainability access-shape baseline",
      "seed" => SEED,
      "full_material_scan" => FULL_MATERIAL_SCAN,
      "catalogs" => {
        "normal_species" => normal_pool.length,
        "custom_fusions" => fusion_pool.length,
        "area_item_slots" => item_slots.length,
        "evolution_branches" => catalog.branch_catalog.length
      },
      "benchmarks" => {
        "encounter_enumeration" => encounter_results,
        "starter_enumeration" => starter_results,
        "item_supply_enumeration" => item_result,
        "normal_evolution_closure" => closure_result,
        "player_fusion_material_mapping" => material_results,
        "single_target_inverse_material_lookup" => inverse_results
      }
    }
    write_report(report)
  ensure
    File.delete(PROGRESS_PATH) if File.file?(PROGRESS_PATH)
  end
end

IronmonObtainabilityFoundationBenchmark.run
