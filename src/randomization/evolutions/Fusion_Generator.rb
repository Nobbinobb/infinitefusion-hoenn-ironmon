#===============================================================================
# Deterministic complete-fusion evolution generation
#===============================================================================

module Ironmon
  class FusionEvolutionGenerator
    SCHEMA_VERSION = 1
    RULES_VERSION = 3
    PREFERRED_MINIMUM_PERCENT = 90
    PREFERRED_MAXIMUM_PERCENT = 115
    INTERMEDIATE_WEIGHT_FOR_INTERMEDIATE_REFERENCE = 60
    INTERMEDIATE_WEIGHT_FOR_TERMINAL_REFERENCE = 40
    attr_reader :seed
    attr_reader :catalog
    attr_reader :target_pool_info

    def initialize(seed, catalog, target_species, target_pool_info, base_stat_generator)
      @seed = seed.to_i
      @catalog = catalog
      @target_species = target_species
      @target_pool_info = target_pool_info
      @base_stat_generator = base_stat_generator
      @taxonomy_by_identity = {}
      @branches_by_source = Hash.new { |hash, key| hash[key] = [] }
      catalog.taxonomy_catalog.each do |entry|
        @taxonomy_by_identity[entry[:identity]] = entry
      end
      catalog.branch_catalog.each do |branch|
        @branches_by_source[branch[:source]] << branch
      end
      @branches_by_source.each_value do |branches|
        branches.sort_by! { |branch| branch[:identity] }
      end
      @target_pools = nil
      @target_by_identity = nil
      @target_bst_cache = {}
      @hard_candidates_by_source = {}
      @branches_by_fusion = {}
      @candidate_targets_by_fusion = {}
      @deterministic_base_value = hash_entries(
        Ironmon::FNV1A_64_OFFSET_BASIS,
        [
          SCHEMA_VERSION, RULES_VERSION, @seed, "fusion_evolution",
          catalog.source_fingerprint, catalog.taxonomy_fingerprint,
          catalog.method_fingerprint, target_pool_info[:schema_version],
          target_pool_info[:size], target_pool_info[:fingerprint],
          BaseStatGenerator::SCHEMA_VERSION,
          @base_stat_generator.source_fingerprint
        ]
      )
    end

    def branches_for(source)
      species = GameData::Species.try_get(source)
      return [] if !standard_fusion?(species)
      identity = species.id.to_s
      return @branches_by_fusion[identity] if
        @branches_by_fusion.key?(identity)
      @branches_by_fusion[identity] = deep_freeze(generate_source(species))
      return @branches_by_fusion[identity]
    end

    def cached_source_count
      return @branches_by_fusion.length
    end

    def candidate_targets_for(source)
      species = GameData::Species.try_get(source)
      return { :head => [], :body => [] } if !standard_fusion?(species)
      identity = species.id.to_s
      cached = @candidate_targets_by_fusion[identity]
      return cached if cached
      branches = conceptual_branches_for(species)
      source_bst = fusion_bst(species)
      source_families = [
        taxonomy_for(species.body_pokemon)[:family],
        taxonomy_for(species.head_pokemon)[:family]
      ].uniq
      result = { :head => {}, :body => {} }
      branches.each do |branch|
        candidates = candidate_targets_for_branch(
          species, branch, source_bst, source_families
        )
        candidates.each do |target|
          result[branch[:side]][target[:identity]] = {
            :target => target[:identity],
            :target_id => target[:id],
            :target_bst => target_bst(target)
          }
        end
      end
      targets = {}
      result.each do |side, entries|
        targets[side] = entries.values.sort_by { |target| target[:target] }
      end
      @candidate_targets_by_fusion[identity] = deep_freeze(targets)
      return @candidate_targets_by_fusion[identity]
    end

    def target_pool_counts
      build_target_pools
      return {
        :continuing => @target_pools[:continuing].length,
        :terminal => @target_pools[:terminal].length
      }
    end

    def validate_source(source)
      species = GameData::Species.get(source)
      generated = branches_for(species)
      native = conceptual_branches_for(species)
      if generated.length != native.length
        raise EvolutionRandomizationError,
              "#{species.id} did not receive one target per fusion branch"
      end
      targets = generated.map { |branch| branch[:target] }
      if targets.uniq.length != targets.length
        raise EvolutionRandomizationError,
              "#{species.id} received duplicate complete-fusion targets"
      end
      generated.each { |branch| validate_generated_branch(species, branch) }
      return true
    end

    private

    def standard_fusion?(species)
      return false if !species || !species.is_a?(GameData::FusedSpecies)
      return false if species.id_number <= NB_POKEMON
      return false if species.id_number >= Settings::ZAPMOLCUNO_NB
      body = species.body_pokemon
      head = species.head_pokemon
      return normal_component?(body) && normal_component?(head)
    end

    def normal_component?(species)
      return species && species.id_number > 0 &&
        species.id_number <= NB_POKEMON
    end

    def conceptual_branches_for(species)
      branches = []
      [[:body, species.body_pokemon], [:head, species.head_pokemon]].each do |side, component|
        @branches_by_source[component.id.to_s].each do |branch|
          branches << {
            :identity => "#{species.id}|#{side}|#{branch[:identity]}",
            :side => side,
            :component => component,
            :component_branch => branch
          }
        end
      end
      branches.sort_by! { |branch| branch[:identity] }
      return branches
    end

    def generate_source(species)
      branches = conceptual_branches_for(species)
      return [] if branches.empty?
      source_bst = fusion_bst(species)
      source_families = [
        taxonomy_for(species.body_pokemon)[:family],
        taxonomy_for(species.head_pokemon)[:family]
      ].uniq
      plans_by_branch = {}
      branches.each do |branch|
        plans_by_branch[branch[:identity]] = candidate_plans(
          species, branch, source_bst, source_families, branches.length
        )
      end
      selected = {}
      assignment = find_plan_assignment(
        branches, plans_by_branch, selected, 0
      )
      if !assignment
        plans_by_branch = upward_expansion_plans(
          species, branches, source_bst, source_families
        )
        assignment = find_upward_expansion_assignment(
          branches, plans_by_branch
        )
      end
      if !assignment
        raise EvolutionRandomizationError,
              "#{species.id} has no complete valid fusion evolution assignment"
      end
      return branches.map do |branch|
        plan_index = assignment[:plan_indexes][branch[:identity]]
        plan = plans_by_branch[branch[:identity]][plan_index]
        target = assignment[:targets][branch[:identity]]
        build_generated_branch(
          species, branch, target, plan, plan_index, source_bst
        )
      end
    end

    def upward_expansion_plans(species, branches, source_bst, source_families)
      result = {}
      branches.each do |branch|
        result[branch[:identity]] = [upward_expansion_plan(
          species, branch, source_bst, source_families
        )]
      end
      return result
    end

    def find_upward_expansion_assignment(branches, plans_by_branch)
      (0..branches.length).each do |expansion_count|
        subsets = branches.combination(expansion_count).to_a
        subsets.sort_by! do |subset|
          upward_expansion_subset_priority(subset, plans_by_branch)
        end
        subsets.each do |subset|
          expanded = {}
          subset.each { |branch| expanded[branch[:identity]] = true }
          filtered_plans = {}
          valid = true
          branches.each do |branch|
            plan = plans_by_branch[branch[:identity]][0]
            candidates = plan[:candidates].select do |target|
              metadata = plan[:candidate_metadata][target[:identity]]
              !metadata[:upward_expansion] || expanded[branch[:identity]]
            end
            if candidates.empty?
              valid = false
              break
            end
            filtered_plans[branch[:identity]] = [
              plan.merge(:candidates => candidates)
            ]
          end
          next if !valid
          assignment = find_plan_assignment(
            branches, filtered_plans, {}, 0
          )
          return assignment if assignment
        end
      end
      return nil
    end

    def upward_expansion_subset_priority(subset, plans_by_branch)
      distance = subset.inject(0) do |sum, branch|
        plan = plans_by_branch[branch[:identity]][0]
        upward = plan[:candidates].select do |target|
          plan[:candidate_metadata][target[:identity]][:upward_expansion]
        end
        minimum = upward.map do |target|
          target_bst(target) - plan[:upward_expansion_floor]
        end.min
        sum + (minimum || Ironmon::FNV1A_64_MASK)
      end
      return [distance, subset.map { |branch| branch[:identity] }]
    end

    def upward_expansion_plan(species, branch, source_bst, source_families)
      component_taxonomy = taxonomy_for(branch[:component])
      reference_bst = natural_reference_bst(species, branch)
      hard_candidates = hard_candidates_for(
        species, component_taxonomy[:role], source_bst, source_families,
        @catalog.required_target_types(
          branch[:component], branch[:component_branch]
        )
      )
      minimum = [
        divide_round_up(reference_bst * PREFERRED_MINIMUM_PERCENT, 100),
        source_bst + 1
      ].max
      maximum = (reference_bst * PREFERRED_MAXIMUM_PERCENT) / 100
      preferred = candidates_in_bst_range(
        hard_candidates, minimum, maximum
      )
      standard = []
      metadata = {}
      if preferred.empty?
        closest = closest_bst_candidates(hard_candidates, reference_bst)
        standard = deterministic_candidate_order(
          closest,
          deterministic_state("candidate", branch[:identity], :fallback)
        )
        standard.each do |target|
          metadata[target[:identity]] = {
            :fallback => true, :upward_expansion => false
          }
        end
      else
        available = allowed_buckets(component_taxonomy[:role]).select do |bucket|
          preferred.any? { |target| target[:bucket] == bucket }
        end
        ordered_buckets(branch[:component_branch], available).each do |bucket|
          candidates = preferred.select { |target| target[:bucket] == bucket }
          ordered = deterministic_candidate_order(
            candidates,
            deterministic_state("candidate", branch[:identity], bucket)
          )
          ordered.each do |target|
            metadata[target[:identity]] = {
              :fallback => false, :upward_expansion => false
            }
          end
          standard.concat(ordered)
        end
      end
      expansion_floor = maximum
      if !standard.empty?
        expansion_floor = [
          expansion_floor,
          standard.map { |target| target_bst(target) }.max
        ].max
      end
      upward = hard_candidates.select do |target|
        target_bst(target) > expansion_floor
      end
      upward.sort_by! do |target|
        [
          target_bst(target),
          deterministic_value(
            "upward_expansion", branch[:identity], target[:identity]
          ),
          target[:identity]
        ]
      end
      upward.each do |target|
        metadata[target[:identity]] = {
          :fallback => true, :upward_expansion => true
        }
      end
      return {
        :bucket => :upward_expansion,
        :candidates => (standard + upward).uniq,
        :fallback => true,
        :assignment_rescue => true,
        :candidate_metadata => metadata,
        :upward_expansion_floor => expansion_floor,
        :preferred_minimum => minimum,
        :preferred_maximum => maximum,
        :reference_bst => reference_bst
      }
    end

    def candidate_plans(species, branch, source_bst, source_families, assignment_size)
      component_taxonomy = taxonomy_for(branch[:component])
      reference_bst = natural_reference_bst(species, branch)
      hard_candidates = hard_candidates_for(
        species, component_taxonomy[:role], source_bst, source_families,
        @catalog.required_target_types(
          branch[:component], branch[:component_branch]
        )
      )
      if hard_candidates.empty?
        raise EvolutionRandomizationError,
              "#{branch[:identity]} has no valid stronger custom target"
      end
      minimum = [
        divide_round_up(reference_bst * PREFERRED_MINIMUM_PERCENT, 100),
        source_bst + 1
      ].max
      maximum = (reference_bst * PREFERRED_MAXIMUM_PERCENT) / 100
      preferred = candidates_in_bst_range(
        hard_candidates, minimum, maximum
      )
      if preferred.empty?
        closest = closest_bst_candidates(hard_candidates, reference_bst)
        return [candidate_plan(branch, :fallback, closest, true, minimum,
                               maximum, reference_bst, assignment_size)]
      end
      available = allowed_buckets(component_taxonomy[:role]).select do |bucket|
        preferred.any? { |target| target[:bucket] == bucket }
      end
      ordered = ordered_buckets(branch[:component_branch], available)
      return ordered.map do |bucket|
        candidates = preferred.select { |target| target[:bucket] == bucket }
        candidate_plan(branch, bucket, candidates, false, minimum, maximum,
                       reference_bst, assignment_size)
      end
    end

    def candidate_targets_for_branch(species, branch, source_bst, source_families)
      component_taxonomy = taxonomy_for(branch[:component])
      reference_bst = natural_reference_bst(species, branch)
      hard_candidates = hard_candidates_for(
        species, component_taxonomy[:role], source_bst, source_families,
        @catalog.required_target_types(
          branch[:component], branch[:component_branch]
        )
      )
      return [] if hard_candidates.empty?
      minimum = [
        divide_round_up(reference_bst * PREFERRED_MINIMUM_PERCENT, 100),
        source_bst + 1
      ].max
      maximum = (reference_bst * PREFERRED_MAXIMUM_PERCENT) / 100
      preferred = candidates_in_bst_range(hard_candidates, minimum, maximum)
      return preferred if !preferred.empty?
      return closest_bst_candidates(hard_candidates, reference_bst)
    end

    def candidate_plan(branch, bucket, candidates, fallback, minimum, maximum, reference_bst, assignment_size)
      priority = deterministic_state("candidate", branch[:identity], bucket)
      ordered = deterministic_candidate_prefix(
        candidates, priority, assignment_size
      )
      return {
        :bucket => bucket,
        :candidates => ordered,
        :fallback => fallback,
        :preferred_minimum => minimum,
        :preferred_maximum => maximum,
        :reference_bst => reference_bst
      }
    end

    def deterministic_candidate_prefix(candidates, priority, limit)
      ranked = []
      candidates.each do |target|
        entry = [
          deterministic_value_from(priority, target[:identity]),
          target[:identity], target
        ]
        if ranked.length < limit ||
           (entry[0, 2] <=> ranked[-1][0, 2]) < 0
          index = ranked.bsearch_index do |existing|
            (entry[0, 2] <=> existing[0, 2]) < 0
          end
          index ||= ranked.length
          ranked.insert(index, entry)
          ranked.pop if ranked.length > limit
        end
      end
      return ranked.map { |entry| entry[2] }
    end

    def deterministic_candidate_order(candidates, priority)
      return candidates.sort_by do |target|
        [
          deterministic_value_from(priority, target[:identity]),
          target[:identity]
        ]
      end
    end

    def find_plan_assignment(branches, plans_by_branch, selected, index)
      if index >= branches.length
        targets = solve_target_assignment(branches, selected)
        return nil if !targets
        indexes = {}
        selected.each do |identity, selection|
          indexes[identity] = selection[:index]
        end
        return { :targets => targets, :plan_indexes => indexes }
      end
      branch = branches[index]
      plans_by_branch[branch[:identity]].each_with_index do |plan, plan_index|
        selected[branch[:identity]] = { :plan => plan, :index => plan_index }
        result = find_plan_assignment(
          branches, plans_by_branch, selected, index + 1
        )
        return result if result
      end
      selected.delete(branch[:identity])
      return nil
    end

    def solve_target_assignment(branches, selected)
      assigned = {}
      used_targets = {}
      return assign_next_target(
        branches, selected, assigned, used_targets
      )
    end

    def assign_next_target(branches, selected, assigned, used_targets)
      return assigned.dup if assigned.length >= branches.length
      remaining = branches.reject { |branch| assigned.key?(branch[:identity]) }
      ranked = remaining.map do |branch|
        plan = selected[branch[:identity]][:plan]
        candidates = plan[:candidates].reject do |target|
          used_targets[target[:identity]]
        end
        [candidates.length, branch[:identity], branch, candidates]
      end
      ranked.sort_by! { |entry| [entry[0], entry[1]] }
      _count, _identity, branch, candidates = ranked[0]
      return nil if candidates.empty?
      candidates.each do |target|
        assigned[branch[:identity]] = target
        used_targets[target[:identity]] = true
        result = assign_next_target(
          branches, selected, assigned, used_targets
        )
        return result if result
        assigned.delete(branch[:identity])
        used_targets.delete(target[:identity])
      end
      return nil
    end

    def hard_candidates_for(species, source_role, source_bst, source_families, source_types)
      key = [species.id.to_s, source_role, source_types.sort_by(&:to_s)]
      cached = @hard_candidates_by_source[key]
      return cached if cached
      candidates = allowed_buckets(source_role).inject([]) do |all, bucket|
        all.concat(target_pools[bucket])
      end
      candidates = candidates.select do |target|
        (target[:families] & source_families).empty? &&
          !(target[:types] & source_types).empty? &&
          target_bst(target) > source_bst
      end
      candidates.sort_by! do |target|
        [target_bst(target), target[:identity]]
      end
      @hard_candidates_by_source[key] = candidates.freeze
      return @hard_candidates_by_source[key]
    end

    def candidates_in_bst_range(candidates, minimum, maximum)
      first = candidates.bsearch_index do |target|
        target_bst(target) >= minimum
      end
      return [] if !first
      after = candidates.bsearch_index do |target|
        target_bst(target) > maximum
      end
      after ||= candidates.length
      return [] if first >= after
      return candidates[first...after]
    end

    def closest_bst_candidates(candidates, reference_bst)
      upper = candidates.bsearch_index do |target|
        target_bst(target) >= reference_bst
      end
      indexes = []
      indexes << upper if upper
      indexes << upper - 1 if upper && upper > 0
      indexes << candidates.length - 1 if !upper
      distance = indexes.map do |index|
        (target_bst(candidates[index]) - reference_bst).abs
      end.min
      return candidates.select do |target|
        (target_bst(target) - reference_bst).abs == distance
      end
    end

    def build_target_pools
      return if @target_pools
      cache_key = [
        RULES_VERSION, @catalog.taxonomy_fingerprint,
        @target_pool_info[:schema_version], @target_pool_info[:size],
        @target_pool_info[:fingerprint]
      ]
      cached = Ironmon.cached_fusion_evolution_target_catalog(cache_key)
      if cached
        @target_pools = cached[:pools]
        @target_by_identity = cached[:by_identity]
        return
      end
      pools = { :continuing => [], :terminal => [] }
      by_identity = {}
      @target_species.each do |target_id|
        match = /\AB(\d+)H(\d+)\z/.match(target_id.to_s)
        next if !match
        body = GameData::Species.get(match[1].to_i)
        head = GameData::Species.get(match[2].to_i)
        body_taxonomy = taxonomy_for(body)
        head_taxonomy = taxonomy_for(head)
        bucket = fusion_target_bucket(
          body_taxonomy[:role], head_taxonomy[:role]
        )
        next if !bucket
        entry = {
          :identity => target_id.to_s,
          :id => target_id,
          :body => body,
          :head => head,
          :bucket => bucket,
          :families => [
            body_taxonomy[:family], head_taxonomy[:family]
          ].uniq.freeze,
          :types => fusion_target_types(body, head).freeze
        }.freeze
        pools[bucket] << entry
        by_identity[entry[:identity]] = entry
      end
      pools.each_value do |entries|
        entries.sort_by! { |entry| entry[:identity] }
        entries.freeze
      end
      @target_pools = pools.freeze
      @target_by_identity = by_identity.freeze
      Ironmon.cache_fusion_evolution_target_catalog(
        cache_key, @target_pools, @target_by_identity
      )
    end

    def target_pools
      build_target_pools
      return @target_pools
    end

    def fusion_target_bucket(body_role, head_role)
      return nil if body_role == :first_stage || head_role == :first_stage
      return :continuing if body_role == :intermediate ||
        head_role == :intermediate
      terminal = [:final, :standalone]
      return :terminal if terminal.include?(body_role) &&
        terminal.include?(head_role)
      return nil
    end

    def fusion_target_types(body, head)
      type1 = if head.type1 == :NORMAL && head.type2 == :FLYING
                head.type2
              else
                head.type1
              end
      type2 = body.type2 == type1 ? body.type1 : body.type2
      return [type1, type2].compact.uniq
    end

    def allowed_buckets(source_role)
      return [:terminal] if source_role == :intermediate
      return [:continuing, :terminal] if source_role == :first_stage
      raise EvolutionRandomizationError,
            "#{source_role} fusion component cannot evolve"
    end

    def ordered_buckets(component_branch, available)
      return available if available.length <= 1
      weight = if component_branch[:original_destination_role] == :intermediate
                 INTERMEDIATE_WEIGHT_FOR_INTERMEDIATE_REFERENCE
               else
                 INTERMEDIATE_WEIGHT_FOR_TERMINAL_REFERENCE
               end
      roll = deterministic_value("bucket", component_branch[:identity]) % 100
      selected = roll < weight ? :continuing : :terminal
      selected = available[0] if !available.include?(selected)
      return [selected] + (available - [selected])
    end

    def natural_reference_bst(species, branch)
      destination = GameData::Species.get(
        branch[:component_branch][:original_destination]
      )
      body = branch[:side] == :body ? destination : species.body_pokemon
      head = branch[:side] == :head ? destination : species.head_pokemon
      stats = @base_stat_generator.fuse(
        @base_stat_generator.stats_for(body),
        @base_stat_generator.stats_for(head)
      )
      return stats.values.inject(0) { |sum, value| sum + value.to_i }
    end

    def fusion_bst(species)
      stats = @base_stat_generator.fusion_stats_for(species)
      return stats.values.inject(0) { |sum, value| sum + value.to_i }
    end

    def target_bst(target)
      cached = @target_bst_cache[target[:identity]]
      return cached if cached
      stats = @base_stat_generator.fuse(
        @base_stat_generator.stats_for(target[:body]),
        @base_stat_generator.stats_for(target[:head])
      )
      @target_bst_cache[target[:identity]] = stats.values.inject(0) do |sum, value|
        sum + value.to_i
      end
      return @target_bst_cache[target[:identity]]
    end

    def build_generated_branch(species, branch, target, plan, plan_index, source_bst)
      component_branch = branch[:component_branch]
      candidate_metadata = if plan[:candidate_metadata]
                             plan[:candidate_metadata][target[:identity]]
                           end
      fallback = candidate_metadata ?
        candidate_metadata[:fallback] : plan[:fallback]
      upward_expansion = candidate_metadata ?
        candidate_metadata[:upward_expansion] : false
      result = {
        :identity => branch[:identity],
        :source => species.id.to_s,
        :component_side => branch[:side],
        :component_source => branch[:component].id.to_s,
        :component_branch_identity => component_branch[:identity],
        :original_destination => component_branch[:original_destination],
        :target => target[:identity],
        :target_id => target[:id],
        :source_bst => source_bst,
        :reference_bst => plan[:reference_bst],
        :target_bst => target_bst(target),
        :preferred_minimum => plan[:preferred_minimum],
        :preferred_maximum => plan[:preferred_maximum],
        :bucket => plan[:assignment_rescue] ? target[:bucket] : plan[:bucket],
        :fallback => fallback,
        :bucket_reassigned => plan[:assignment_rescue] == true ||
          plan_index > 0,
        :effective_methods => component_branch[:effective_methods]
      }
      if upward_expansion
        result[:upward_expansion] = true
        result[:upward_expansion_floor] = plan[:upward_expansion_floor]
      end
      return result
    end

    def validate_generated_branch(species, branch)
      build_target_pools
      target = @target_by_identity[branch[:target]]
      if !target || target[:id] != branch[:target_id]
        raise EvolutionRandomizationError,
              "#{branch[:identity]} has a non-custom fusion target"
      end
      source_families = [
        taxonomy_for(species.body_pokemon)[:family],
        taxonomy_for(species.head_pokemon)[:family]
      ].uniq
      if !(target[:families] & source_families).empty?
        raise EvolutionRandomizationError,
              "#{branch[:identity]} retained a source component family"
      end
      if branch[:target_bst] <= branch[:source_bst]
        raise EvolutionRandomizationError,
              "#{branch[:identity]} is not a strict fusion BST improvement"
      end
      component = branch[:component_side] == :body ?
        species.body_pokemon : species.head_pokemon
      required_types = @catalog.required_target_types(component, branch)
      if (target[:types] & required_types).empty?
        raise EvolutionRandomizationError,
              "#{branch[:identity]} does not share an evolving component type"
      end
      if !allowed_buckets(taxonomy_for(component)[:role]).include?(target[:bucket])
        raise EvolutionRandomizationError,
              "#{branch[:identity]} has an invalid fusion target stage"
      end
      if !branch[:fallback] &&
         (branch[:target_bst] < branch[:preferred_minimum] ||
          branch[:target_bst] > branch[:preferred_maximum])
        raise EvolutionRandomizationError,
              "#{branch[:identity]} escaped its fusion BST range"
      end
      if branch[:upward_expansion]
        if branch[:target_bst] <= branch[:upward_expansion_floor]
          raise EvolutionRandomizationError,
                "#{branch[:identity]} did not expand upward"
        end
      elsif branch[:fallback]
        validate_closest_fallback(species, branch)
      end
      return true
    end

    def validate_closest_fallback(species, branch)
      source_families = [
        taxonomy_for(species.body_pokemon)[:family],
        taxonomy_for(species.head_pokemon)[:family]
      ].uniq
      component = branch[:component_side] == :body ?
        species.body_pokemon : species.head_pokemon
      candidates = hard_candidates_for(
        species, taxonomy_for(component)[:role], branch[:source_bst],
        source_families, @catalog.required_target_types(component, branch)
      )
      closest = candidates.map do |target|
        (target_bst(target) - branch[:reference_bst]).abs
      end.min
      actual = (branch[:target_bst] - branch[:reference_bst]).abs
      if actual != closest
        raise EvolutionRandomizationError,
              "#{branch[:identity]} did not use the closest fusion BST"
      end
    end

    def taxonomy_for(species)
      taxonomy = @taxonomy_by_identity[species.id.to_s]
      if !taxonomy
        raise EvolutionRandomizationError,
              "#{species.id} has no evolution taxonomy"
      end
      return taxonomy
    end

    def divide_round_up(value, divisor)
      return (value + divisor - 1) / divisor
    end

    def deterministic_value(*parts)
      return hash_entries(@deterministic_base_value, parts)
    end

    def deterministic_state(*parts)
      return hash_entries(@deterministic_base_value, parts)
    end

    def deterministic_value_from(value, *parts)
      return hash_entries(value, parts)
    end

    def hash_entries(value, entries)
      return Ironmon.fnv1a_64_entries(entries, value)
    end

    def deep_freeze(value)
      if value.is_a?(Array)
        value.each { |entry| deep_freeze(entry) }
      elsif value.is_a?(Hash)
        value.each do |key, entry|
          deep_freeze(key)
          deep_freeze(entry)
        end
      end
      value.freeze
      return value
    end
  end

  def self.fusion_evolution_generator
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    info = custom_fusion_pool_info
    key = [
      seed, evolution_catalog.source_fingerprint,
      evolution_catalog.taxonomy_fingerprint,
      evolution_catalog.method_fingerprint, info[:schema_version], info[:size],
      info[:fingerprint], BaseStatGenerator::SCHEMA_VERSION,
      base_stat_source_fingerprint
    ]
    if !@fusion_evolution_generator || @fusion_evolution_generator_key != key
      @fusion_evolution_generator_key = key
      @fusion_evolution_generator = FusionEvolutionGenerator.new(
        seed, evolution_catalog, custom_fusion_pool, info, base_stat_generator
      )
    end
    return @fusion_evolution_generator
  end

  def self.generated_fusion_evolution_branches_for(source)
    return [] if !evolution_randomization_active?
    return fusion_evolution_generator.branches_for(source)
  end

  def self.reset_fusion_evolution_generator_cache
    @fusion_evolution_generator = nil
    @fusion_evolution_generator_key = nil
  end

  def self.cached_fusion_evolution_target_catalog(key)
    return nil if @fusion_evolution_target_catalog_key != key
    return @fusion_evolution_target_catalog
  end

  def self.cache_fusion_evolution_target_catalog(key, pools, by_identity)
    @fusion_evolution_target_catalog_key = key.freeze
    @fusion_evolution_target_catalog = {
      :pools => pools,
      :by_identity => by_identity
    }.freeze
  end

  def self.reset_fusion_evolution_target_catalog_cache
    @fusion_evolution_target_catalog_key = nil
    @fusion_evolution_target_catalog = nil
  end
end
