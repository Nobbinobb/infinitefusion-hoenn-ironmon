module IronmonRandomizationSimulator
  class InvariantError < StandardError; end

  OUTPUT_PATH = $ironmon_simulation_output_path.to_s
  PROGRESS_PATH = "#{OUTPUT_PATH}.progress"
  SEED_MODE = $ironmon_simulation_seed_mode.to_s
  START_SEED = $ironmon_simulation_start_seed.to_i
  SELECTION_SEED = $ironmon_simulation_selection_seed.to_i
  SEED_COUNT = $ironmon_simulation_seed_count.to_i
  FUSION_SAMPLES_PER_SEED =
    $ironmon_simulation_fusion_samples_per_seed.to_i
  FUSION_PREDECESSOR_SAMPLES =
    $ironmon_simulation_fusion_predecessor_samples.to_i
  ITEM_SLOTS_PER_SEED = $ironmon_simulation_item_slots_per_seed.to_i
  FUSION_PREDECESSOR_PAGE_LIMIT = 8
  MAX_FAILURES = 100
  ALERT_Z_SCORE = 6.0
  MAXIMUM_RUN_SEED = 2_147_483_646
  SEED_SELECTION_SCHEMA_VERSION = 1
  UINT64_MASK = 0xffffffffffffffff
  FUSION_MINIMUM_STAT =
    ((2 * Ironmon::BaseStatGenerator::MINIMUM_STAT) / 3) +
    (Ironmon::BaseStatGenerator::MINIMUM_STAT / 3)
  FUSION_MAXIMUM_STAT =
    ((2 * Ironmon::BaseStatGenerator::MAXIMUM_STAT) / 3) +
    (Ironmon::BaseStatGenerator::MAXIMUM_STAT / 3)
  PHASES = [
    "species", "abilities", "base stats", "move access", "evolutions",
    "items"
  ].freeze

  def self.simulation_seeds
    if SEED_MODE == "sequential"
      return (START_SEED...(START_SEED + SEED_COUNT)).to_a
    end

    seeds = []
    used = {}
    state = SELECTION_SEED & UINT64_MASK
    while seeds.length < SEED_COUNT
      state = (state + 0x9e3779b97f4a7c15) & UINT64_MASK
      mixed = state
      mixed = ((mixed ^ (mixed >> 30)) * 0xbf58476d1ce4e5b9) & UINT64_MASK
      mixed = ((mixed ^ (mixed >> 27)) * 0x94d049bb133111eb) & UINT64_MASK
      mixed ^= mixed >> 31
      candidate = mixed % (MAXIMUM_RUN_SEED + 1)
      next if used[candidate]
      used[candidate] = true
      seeds << candidate
    end
    return seeds
  end

  def self.increment(hash, key, amount = 1)
    hash[key.to_s] ||= 0
    hash[key.to_s] += amount
  end

  def self.assert(condition, message)
    raise InvariantError, message if !condition
  end

  def self.capture(failures, family, seed, identity = nil)
    yield
  rescue Exception => error
    return if failures.length >= MAX_FAILURES
    failures << {
      "family" => family.to_s,
      "seed" => seed,
      "identity" => identity ? identity.to_s : nil,
      "error" => error.class.to_s,
      "message" => error.message.to_s
    }
  end

  def self.normal_species
    return Ironmon.normal_species_pool.map do |identity|
      GameData::Species.get(identity)
    end
  end

  def self.fusion_sample(pool, seed, count)
    sample_count = [count, pool.length].min
    result = []
    used = {}
    index = 0
    while result.length < sample_count
      value = Ironmon.fnv1a_64_joined(
        [1, seed, "simulation_fusion_sample", index]
      )
      candidate = pool[value % pool.length]
      if !used[candidate]
        used[candidate] = true
        result << GameData::Species.get(candidate)
      end
      index += 1
    end
    return result
  end

  def self.registered_moves?(moves)
    return moves.all? { |move| Ironmon.registered_move?(move) }
  end

  def self.unique_compact?(values)
    compact = values.compact
    return compact.uniq.length == compact.length
  end

  def self.count_move_schedule(frequencies, schedule)
    schedule.each { |entry| increment(frequencies, entry[1]) }
  end

  def self.count_moves(frequencies, moves)
    moves.each { |move| increment(frequencies, move) }
  end

  def self.validate_move_schedule(schedule, identity)
    moves = schedule.map { |entry| entry[1] }
    level_one = schedule.select { |entry| entry[0].to_i <= 1 }
    starting = level_one.last(4).map { |entry| entry[1] }
    assert(moves.uniq.length == moves.length,
           "#{identity} received duplicate level-up moves")
    assert(registered_moves?(moves),
           "#{identity} received an unknown level-up move")
    assert(level_one.length >= 4,
           "#{identity} has fewer than four level-1 moves")
    assert(starting.any? { |move| Ironmon.damaging_level_up_move?(move) },
           "#{identity} has no damaging move in its starting four")
  end

  def self.validate_move_list(moves, identity, channel)
    assert(moves.uniq.length == moves.length,
           "#{identity} received duplicate #{channel} moves")
    assert(registered_moves?(moves),
           "#{identity} received an unknown #{channel} move")
  end

  def self.validate_fusion_predecessor_page(page, target)
    assert(page[:branches].length <= FUSION_PREDECESSOR_PAGE_LIMIT,
           "#{target} predecessor page exceeded its requested limit")
    assert(page[:branches].all? do |branch|
             branch[:target_id] == target
           end,
           "#{target} predecessor page contained an inexact result")
    diagnostics = page[:diagnostics]
    assert(diagnostics, "#{target} predecessor page omitted diagnostics")
    case page[:continuation]
    when :available
      assert(diagnostics[:predecessor_count] > FUSION_PREDECESSOR_PAGE_LIMIT,
             "#{target} predecessor page lacks its available result")
    when :unknown
      assert(page[:branches].length == FUSION_PREDECESSOR_PAGE_LIMIT,
             "#{target} predecessor page stopped before filling")
      assert(!diagnostics[:complete],
             "#{target} predecessor page left completion unresolved")
    when :complete
      assert(diagnostics[:complete],
             "#{target} predecessor page stopped without proving completion")
    else
      assert(false, "#{target} predecessor page returned an invalid continuation")
    end
  end

  def self.z_score(observed, total, probability)
    variance = total.to_f * probability * (1.0 - probability)
    return 0.0 if variance <= 0.0
    return (observed.to_f - (total.to_f * probability)) / Math.sqrt(variance)
  end

  def self.distribution_rows(observed, expected_weights, total_weight, total)
    expected_weights.keys.sort_by { |key| key.to_s }.map do |key|
      probability = expected_weights[key].to_f / total_weight.to_f
      count = observed[key.to_s] || 0
      {
        "category" => key.to_s,
        "observed" => count,
        "expected" => (total.to_f * probability).round(2),
        "probability" => probability.round(8),
        "z_score" => z_score(count, total, probability).round(3)
      }
    end
  end

  def self.top_counts(counts, limit = 10)
    return counts.map { |key, value| [key.to_s, value] }.
      sort_by { |entry| [-entry[1], entry[0]] }.first(limit).map do |entry|
        { "id" => entry[0], "count" => entry[1] }
      end
  end

  def self.write_report(report)
    temporary_path = "#{OUTPUT_PATH}.tmp"
    File.binwrite(temporary_path, JSON.generate(report) + "\n")
    File.delete(OUTPUT_PATH) if File.file?(OUTPUT_PATH)
    File.rename(temporary_path, OUTPUT_PATH)
  ensure
    File.delete(temporary_path) if temporary_path && File.file?(temporary_path)
  end

  def self.write_progress(seed, completed_seeds, phase_index, phase)
    work_total = SEED_COUNT * PHASES.length
    work_completed = (completed_seeds * PHASES.length) + phase_index
    File.binwrite(PROGRESS_PATH, [
      "seed=#{seed}",
      "completed_seeds=#{completed_seeds}",
      "total_seeds=#{SEED_COUNT}",
      "phase=#{phase}",
      "work_completed=#{work_completed}",
      "work_total=#{work_total}"
    ].join("\n") + "\n")
  end

  def self.run
    started_at = Time.now
    failures = []
    alerts = []
    write_progress(nil, 0, 0, "preparing catalogs")
    species_data = normal_species
    normal_ids = {}
    species_data.each { |species| normal_ids[species.id_number] = true }
    fusion_pool = Ironmon.custom_fusion_pool
    fusion_info = Ironmon.custom_fusion_pool_info
    fusion_ids = {}
    fusion_pool.each do |identity|
      fusion_ids[GameData::Species.get(identity).id_number] = true
    end

    ability_pool = Ironmon.allowed_ability_pool
    ability_expected_per_seed = Hash.new(0.0)
    ability_variance_per_seed = Hash.new(0.0)
    species_data.each do |species|
      slot_count = (
        Ironmon.original_normal_abilities(species) +
        Ironmon.original_hidden_abilities(species)
      ).compact.length
      eligible = Ironmon.eligible_ability_pool(species)
      probability = slot_count.to_f / eligible.length.to_f
      eligible.each do |ability|
        ability_expected_per_seed[ability.to_s] += probability
        ability_variance_per_seed[ability.to_s] +=
          probability * (1.0 - probability)
      end
    end
    move_pool = Ironmon.allowed_level_up_move_pool
    catalog = Ironmon.evolution_catalog
    catalog.validate
    rules = Ironmon::ItemSlotGenerator::POOL_RULES_VERSION
    ground_pool = Ironmon.item_ground_pool(rules)
    tm_pool = Ironmon.item_tm_pool(rules)
    ground_lookup = {}
    ground_pool.each { |item| ground_lookup[item] = true }
    tm_lookup = {}
    tm_pool.each { |item| tm_lookup[item] = true }

    mixed_categories = Hash.new(0)
    species_results = Hash.new(0)
    ability_frequencies = Hash.new(0)
    move_frequencies = Hash.new(0)
    stat_totals = Hash.new(0)
    stat_boundary_hits = Hash.new(0)
    evolution_totals = Hash.new(0)
    item_categories = Hash.new(0)
    tm_frequencies = Hash.new(0)
    fusion_sample_total = 0
    normal_observations = 0
    ability_observations = 0
    base_stat_observations = 0
    fusion_base_stat_observations = 0
    fusion_predecessor_page_observations = 0
    fusion_predecessor_returned_results = 0
    fusion_predecessor_continuations = Hash.new(0)
    fusion_stat_minimum = nil
    fusion_stat_maximum = nil
    move_species_observations = 0
    ground_observations = 0

    seeds = simulation_seeds
    seeds.each_with_index do |seed, seed_index|
      fusions = fusion_sample(fusion_pool, seed, FUSION_SAMPLES_PER_SEED)
      fusion_sample_total += fusions.length

      write_progress(seed, seed_index, 0, PHASES[0])
      capture(failures, :species, seed) do
        mixed = Ironmon::SpeciesGenerator.new(
          seed, :simulation_mixed, Ironmon::Configuration::POLICY_MIXED,
          Ironmon.normal_species_pool, fusion_pool, {}
        )
        normal_only = Ironmon::SpeciesGenerator.new(
          seed, :simulation_normal, Ironmon::Configuration::POLICY_NORMAL_ONLY,
          Ironmon.normal_species_pool, fusion_pool, {}
        )
        fusion_only = Ironmon::SpeciesGenerator.new(
          seed, :simulation_fusion,
          Ironmon::Configuration::POLICY_CUSTOM_FUSIONS_ONLY,
          Ironmon.normal_species_pool, fusion_pool, {}
        )
        species_data.each do |source|
          context = [:batch_simulation, source.id_number]
          mixed_id = mixed.map_id(source.id_number, context)
          normal_id = normal_only.map_id(source.id_number, context)
          fusion_id = fusion_only.map_id(source.id_number, context)
          assert(normal_ids[normal_id],
                 "normal-only policy produced #{normal_id}")
          assert(fusion_ids[fusion_id],
                 "fusion-only policy produced #{fusion_id}")
          category = normal_ids[mixed_id] ? :normal : :fusion
          assert(normal_ids[mixed_id] || fusion_ids[mixed_id],
                 "mixed policy produced disallowed species #{mixed_id}")
          increment(mixed_categories, category)
          increment(species_results, mixed_id)
          normal_observations += 3
        end
      end

      ability_generator = Ironmon::AbilityGenerator.new(
        seed, ability_pool, Ironmon.ability_pool_fingerprint
      )
      write_progress(seed, seed_index, 1, PHASES[1])
      capture(failures, :abilities, seed) do
        species_data.each do |species|
          slots = ability_generator.slots_for(species)
          values = slots[:normal] + slots[:hidden]
          assert(unique_compact?(values),
                 "#{species.id} received duplicate ability slots")
          eligible_pool = Ironmon.eligible_ability_pool(species)
          assert(values.compact.all? { |ability| eligible_pool.include?(ability) },
                 "#{species.id} received an ability outside its eligible pool")
          values.compact.each { |ability| increment(ability_frequencies, ability) }
          ability_observations += values.compact.length
        end
        fusions.each do |fusion|
          slots = ability_generator.fusion_slots_for(fusion)
          values = slots[:normal] + slots[:hidden]
          assert(values.compact.all? do |ability|
            Ironmon.registered_ability?(ability) &&
              !Ironmon.exact_species_ability?(ability)
          end, "#{fusion.id} received an invalid fusion ability")
          component_species = [
            Ironmon.ability_base_species(fusion.body_pokemon),
            Ironmon.ability_base_species(fusion.head_pokemon)
          ]
          values.compact.each do |ability|
            allowed_components =
              Ironmon::AbilityGenerator::COMPONENT_ABILITY_RULES[ability]
            next if !allowed_components
            assert((allowed_components & component_species).length > 0,
                   "#{fusion.id} received incompatible #{ability}")
          end
        end
      end

      base_stat_generator = Ironmon::BaseStatGenerator.new(
        seed, Ironmon.base_stat_source_fingerprint
      )
      write_progress(seed, seed_index, 2, PHASES[2])
      capture(failures, :base_stats, seed) do
        species_data.each do |species|
          original = Ironmon.original_base_stats_for(species)
          generated = base_stat_generator.stats_for(species)
          values = Ironmon::BaseStatGenerator::STAT_ORDER.map do |stat|
            value = generated[stat]
            assert(value >= Ironmon::BaseStatGenerator::MINIMUM_STAT &&
                   value <= Ironmon::BaseStatGenerator::MAXIMUM_STAT,
                   "#{species.id} #{stat} is outside the allowed range")
            increment(stat_totals, stat, value)
            if value == Ironmon::BaseStatGenerator::MINIMUM_STAT ||
               value == Ironmon::BaseStatGenerator::MAXIMUM_STAT
              increment(stat_boundary_hits, stat)
            end
            value
          end
          expected_total = Ironmon::BaseStatGenerator::STAT_ORDER.inject(0) do |sum, stat|
            sum + original[stat].to_i
          end
          assert(values.inject(0) { |sum, value| sum + value } == expected_total,
                 "#{species.id} did not preserve BST")
          base_stat_observations += 1
        end
        fusions.each do |fusion|
          generated = base_stat_generator.fusion_stats_for(fusion)
          generated.each do |stat, value|
            assert(value >= FUSION_MINIMUM_STAT &&
                   value <= FUSION_MAXIMUM_STAT,
                   "#{fusion.id} #{stat} is #{value}, outside the fusion range #{FUSION_MINIMUM_STAT}-#{FUSION_MAXIMUM_STAT}")
            fusion_stat_minimum = value if !fusion_stat_minimum ||
              value < fusion_stat_minimum
            fusion_stat_maximum = value if !fusion_stat_maximum ||
              value > fusion_stat_maximum
          end
          fusion_base_stat_observations += 1
        end
      end

      move_generator = Ironmon::MoveAccessGenerator.new(
        seed, move_pool, Ironmon.level_up_move_pool_fingerprint,
        Ironmon.move_access_source_fingerprint
      )
      write_progress(seed, seed_index, 3, PHASES[3])
      capture(failures, :move_access, seed) do
        species_data.each do |species|
          schedule = move_generator.moves_for(species)
          egg = move_generator.egg_moves_for(species)
          tm = move_generator.tm_moves_for(species)
          tr = move_generator.tr_moves_for(species)
          tutor = move_generator.tutor_moves_for(species)
          validate_move_schedule(schedule, species.id)
          validate_move_list(egg, species.id, :egg)
          validate_move_list(tm, species.id, :tm)
          validate_move_list(tr, species.id, :tr)
          validate_move_list(tutor, species.id, :tutor)
          count_move_schedule(move_frequencies, schedule)
          count_moves(move_frequencies, egg + tm + tr + tutor)
          move_species_observations += 1
        end
        fusions.each do |fusion|
          schedule = move_generator.fusion_moves_for(fusion)
          egg = move_generator.fusion_egg_moves_for(fusion)
          tm = move_generator.fusion_tm_moves_for(fusion)
          tr = move_generator.fusion_tr_moves_for(fusion)
          tutor = move_generator.fusion_tutor_moves_for(fusion)
          validate_move_schedule(schedule, fusion.id)
          validate_move_list(egg, fusion.id, :fusion_egg)
          validate_move_list(tm, fusion.id, :fusion_tm)
          validate_move_list(tr, fusion.id, :fusion_tr)
          validate_move_list(tutor, fusion.id, :fusion_tutor)
        end
        validate_move_list(
          move_generator.ordinary_tutor_offerings.values,
          "ordinary tutor catalog", :offerings
        )
      end

      write_progress(seed, seed_index, 4, PHASES[4])
      capture(failures, :evolutions, seed) do
        normal_generator = Ironmon::NormalEvolutionGenerator.new(
          seed, catalog, Ironmon::BaseStatGenerator::SCHEMA_VERSION,
          Ironmon.base_stat_source_fingerprint
        )
        graph = normal_generator.graph
        normal_generator.validate_graph(graph)
        graph.each_value do |branches|
          branches.each do |branch|
            increment(evolution_totals, :normal_branches)
            increment(evolution_totals, :normal_fallbacks) if branch[:fallback]
            increment(evolution_totals, :normal_bucket_reassignments) if
              branch[:bucket_reassigned]
          end
        end
        fusion_generator = Ironmon::FusionEvolutionGenerator.new(
          seed, catalog, fusion_pool, fusion_info, base_stat_generator
        )
        generated_fusion_branches = []
        fusions.each do |fusion|
          fusion_generator.validate_source(fusion)
          fusion_generator.branches_for(fusion).each do |branch|
            generated_fusion_branches << branch
            increment(evolution_totals, :fusion_branches)
            increment(evolution_totals, :fusion_fallbacks) if branch[:fallback]
            increment(evolution_totals, :fusion_upward_expansions) if
              branch[:upward_expansion]
            increment(evolution_totals, :fusion_bucket_reassignments) if
              branch[:bucket_reassigned]
          end
        end
        if fusion_predecessor_page_observations < FUSION_PREDECESSOR_SAMPLES &&
           !generated_fusion_branches.empty?
          selected_index = Ironmon.fnv1a_64_joined(
            [1, seed, "simulation_fusion_predecessor"]
          ) % generated_fusion_branches.length
          target = generated_fusion_branches[selected_index][:target_id]
          page = fusion_generator.predecessor_page_for(
            target, 0, FUSION_PREDECESSOR_PAGE_LIMIT
          )
          validate_fusion_predecessor_page(page, target)
          fusion_predecessor_page_observations += 1
          fusion_predecessor_returned_results += page[:branches].length
          increment(
            fusion_predecessor_continuations, page[:continuation]
          )
        end
      end

      write_progress(seed, seed_index, 5, PHASES[5])
      capture(failures, :items, seed) do
        generator = Ironmon.build_item_slot_generator(seed, rules)
        ITEM_SLOTS_PER_SEED.times do |slot_index|
          slot = "simulation:#{seed}:#{slot_index}"
          ground = generator.ground_item(slot)
          tm = generator.tm_gift(slot)
          assert(ground_lookup[ground],
                 "ground slot produced disallowed item #{ground}")
          assert(tm_lookup[tm], "TM gift produced disallowed item #{tm}")
          category = Ironmon.item_result_category(
            GameData::Item.get(ground), rules
          )
          increment(item_categories, category)
          increment(tm_frequencies, tm)
          ground_observations += 1
        end
      end
      write_progress(seed, seed_index + 1, 0, "complete")
    end

    expected_item_weights = {}
    Ironmon.item_category_summary(rules).each do |category, summary|
      expected_item_weights[category] = summary[:total_weight]
    end
    item_distribution = distribution_rows(
      item_categories, expected_item_weights,
      Ironmon.item_ground_total_weight(rules), ground_observations
    )
    item_distribution.each do |row|
      if row["z_score"].abs >= ALERT_Z_SCORE
        alerts << {
          "family" => "items",
          "metric" => "category_frequency",
          "category" => row["category"],
          "z_score" => row["z_score"],
          "message" => "Observed category frequency is at least #{ALERT_Z_SCORE.to_i} standard deviations from its ticket-weight expectation."
        }
      end
    end

    mixed_total = mixed_categories.values.inject(0) { |sum, value| sum + value }
    mixed_fusion_z = z_score(
      mixed_categories["fusion"] || 0, mixed_total, 0.5
    ).round(3)
    if mixed_fusion_z.abs >= ALERT_Z_SCORE
      alerts << {
        "family" => "species",
        "metric" => "mixed_fusion_frequency",
        "z_score" => mixed_fusion_z,
        "message" => "Mixed-policy fusion frequency is at least #{ALERT_Z_SCORE.to_i} standard deviations from 50%."
      }
    end

    ability_distribution = ability_expected_per_seed.keys.sort.map do |ability|
      observed = ability_frequencies[ability] || 0
      expected = ability_expected_per_seed[ability] * SEED_COUNT
      variance = ability_variance_per_seed[ability] * SEED_COUNT
      deviation = variance > 0.0 ?
        (observed - expected) / Math.sqrt(variance) : 0.0
      {
        "id" => ability,
        "observed" => observed,
        "expected" => expected.round(2),
        "standard_deviation" => Math.sqrt(variance).round(3),
        "z_score" => deviation.round(3)
      }
    end
    ability_distribution.sort_by! do |row|
      [-row["observed"], row["id"]]
    end
    ability_distribution.each do |row|
      expected = row["expected"]
      next if expected < 20.0 || ability_observations - expected < 20.0
      if row["z_score"].abs >= ALERT_Z_SCORE
        alerts << {
          "family" => "abilities",
          "metric" => "ability_frequency",
          "ability" => row["id"],
          "z_score" => row["z_score"],
          "message" => "Observed ability frequency is at least #{ALERT_Z_SCORE.to_i} standard deviations from its per-species eligibility expectation."
        }
      end
    end

    stat_means = {}
    Ironmon::BaseStatGenerator::STAT_ORDER.each do |stat|
      stat_means[stat.to_s] = if base_stat_observations > 0
                                (stat_totals[stat.to_s].to_f /
                                  base_stat_observations).round(3)
                              else
                                0.0
                              end
    end

    finished_at = Time.now
    report = {
      "schema_version" => 3,
      "status" => failures.empty? ? "passed" : "failed",
      "generated_at" => finished_at.utc.strftime("%Y-%m-%dT%H:%M:%SZ"),
      "duration_seconds" => (finished_at - started_at).round(3),
      "seed_selection" => {
        "mode" => SEED_MODE,
        "schema_version" => SEED_MODE == "random" ?
          SEED_SELECTION_SCHEMA_VERSION : nil,
        "selection_seed" => SEED_MODE == "random" ? SELECTION_SEED : nil,
        "start" => SEED_MODE == "sequential" ? START_SEED : nil,
        "end" => SEED_MODE == "sequential" ?
          START_SEED + SEED_COUNT - 1 : nil,
        "minimum" => seeds.min,
        "maximum" => seeds.max,
        "count" => seeds.length,
        "seeds" => seeds
      },
      "configuration" => {
        "fusion_samples_per_seed" => FUSION_SAMPLES_PER_SEED,
        "fusion_predecessor_samples" => FUSION_PREDECESSOR_SAMPLES,
        "fusion_predecessor_page_limit" => FUSION_PREDECESSOR_PAGE_LIMIT,
        "item_slots_per_seed" => ITEM_SLOTS_PER_SEED,
        "statistical_alert_z_score" => ALERT_Z_SCORE
      },
      "generators" => {
        "species" => {
          "schema_version" => Ironmon::SpeciesGenerator::SCHEMA_VERSION
        },
        "abilities" => {
          "schema_version" => Ironmon::AbilityGenerator::SCHEMA_VERSION,
          "pool_rules_version" =>
            Ironmon::AbilityGenerator::POOL_RULES_VERSION
        },
        "base_stats" => {
          "schema_version" => Ironmon::BaseStatGenerator::SCHEMA_VERSION,
          "rules_version" => Ironmon::BaseStatGenerator::RULES_VERSION,
          "source_fingerprint" => Ironmon.base_stat_source_fingerprint
        },
        "move_access" => {
          "schema_version" => Ironmon::MoveAccessGenerator::SCHEMA_VERSION
        },
        "normal_evolutions" => {
          "schema_version" =>
            Ironmon::NormalEvolutionGenerator::SCHEMA_VERSION,
          "rules_version" => Ironmon::NormalEvolutionGenerator::RULES_VERSION
        },
        "fusion_evolutions" => {
          "schema_version" =>
            Ironmon::FusionEvolutionGenerator::SCHEMA_VERSION,
          "rules_version" => Ironmon::FusionEvolutionGenerator::RULES_VERSION,
          "target_pool_schema_version" => fusion_info[:schema_version],
          "target_pool_fingerprint" => fusion_info[:fingerprint]
        },
        "fusion_predecessor_index" => {
          "schema_version" =>
            Ironmon::FusionPredecessorIndex::SCHEMA_VERSION
        },
        "items" => {
          "schema_version" => Ironmon::ItemSlotGenerator::SCHEMA_VERSION,
          "pool_rules_version" => Ironmon::ItemSlotGenerator::POOL_RULES_VERSION
        }
      },
      "catalogs" => {
        "normal_species" => species_data.length,
        "custom_fusions" => fusion_pool.length,
        "abilities" => ability_pool.length,
        "level_up_moves" => move_pool.length,
        "ground_items" => ground_pool.length,
        "tm_items" => tm_pool.length,
        "evolutions" => catalog.summary
      },
      "families" => {
        "species" => {
          "normal_observations" => normal_observations,
          "fusion_samples" => fusion_sample_total,
          "mixed_categories" => mixed_categories,
          "mixed_fusion_z_score" => mixed_fusion_z,
          "distinct_mixed_results" => species_results.length,
          "most_common_mixed_results" => top_counts(species_results)
        },
        "abilities" => {
          "slot_observations" => ability_observations,
          "distinct_results" => ability_frequencies.length,
          "most_common_results" => ability_distribution.first(10),
          "distribution" => ability_distribution
        },
        "base_stats" => {
          "species_observations" => base_stat_observations,
          "fusion_species_observations" => fusion_base_stat_observations,
          "allowed_normal_range" => {
            "minimum" => Ironmon::BaseStatGenerator::MINIMUM_STAT,
            "maximum" => Ironmon::BaseStatGenerator::MAXIMUM_STAT
          },
          "allowed_fusion_range" => {
            "minimum" => FUSION_MINIMUM_STAT,
            "maximum" => FUSION_MAXIMUM_STAT
          },
          "observed_fusion_range" => {
            "minimum" => fusion_stat_minimum,
            "maximum" => fusion_stat_maximum
          },
          "mean_by_stat" => stat_means,
          "boundary_hits_by_stat" => stat_boundary_hits
        },
        "move_access" => {
          "species_observations" => move_species_observations,
          "distinct_results" => move_frequencies.length,
          "most_common_results" => top_counts(move_frequencies)
        },
        "evolutions" => evolution_totals.merge({
          "fusion_predecessor_page_observations" =>
            fusion_predecessor_page_observations,
          "fusion_predecessor_returned_results" =>
            fusion_predecessor_returned_results,
          "fusion_predecessor_continuations" =>
            fusion_predecessor_continuations
        }),
        "items" => {
          "ground_observations" => ground_observations,
          "category_distribution" => item_distribution,
          "distinct_tm_results" => tm_frequencies.length,
          "most_common_tm_results" => top_counts(tm_frequencies)
        }
      },
      "hard_failures" => failures,
      "statistical_alerts" => alerts
    }
    write_report(report)
  ensure
    File.delete(PROGRESS_PATH) if File.file?(PROGRESS_PATH)
  end
end

IronmonRandomizationSimulator.run
