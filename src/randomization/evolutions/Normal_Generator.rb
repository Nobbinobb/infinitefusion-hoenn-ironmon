#===============================================================================
# Deterministic normal evolution graph generation
#===============================================================================

module Ironmon
  class NormalEvolutionGenerator
    SCHEMA_VERSION = 1
    RULES_VERSION = 3
    PREFERRED_MINIMUM_PERCENT = 90
    PREFERRED_MAXIMUM_PERCENT = 115
    INTERMEDIATE_WEIGHT_FOR_INTERMEDIATE_REFERENCE = 60
    TERMINAL_WEIGHT_FOR_INTERMEDIATE_REFERENCE = 40
    INTERMEDIATE_WEIGHT_FOR_TERMINAL_REFERENCE = 40
    TERMINAL_WEIGHT_FOR_TERMINAL_REFERENCE = 60
    attr_reader :seed
    attr_reader :catalog
    attr_reader :base_stat_generator_version
    attr_reader :base_stat_source_fingerprint

    def initialize(seed, catalog, base_stat_generator_version, base_stat_source_fingerprint)
      @seed = seed.to_i
      @catalog = catalog
      @base_stat_generator_version = base_stat_generator_version
      @base_stat_source_fingerprint = base_stat_source_fingerprint
      @taxonomy_by_identity = {}
      @target_by_identity = {}
      @branches_by_source = Hash.new { |hash, key| hash[key] = [] }
      @hard_candidates_by_source = {}
      catalog.taxonomy_catalog.each do |entry|
        @taxonomy_by_identity[entry[:identity]] = entry
      end
      catalog.normal_target_catalog.each do |entry|
        @target_by_identity[entry[:identity]] = entry
      end
      catalog.branch_catalog.each do |branch|
        @branches_by_source[branch[:source]] << branch
      end
      @branches_by_source.each_value do |branches|
        branches.sort_by! { |branch| branch[:identity] }
      end
      @graph = nil
      @graph_fingerprint = nil
      @deterministic_base_value = hash_entries(
        Ironmon::FNV1A_64_OFFSET_BASIS,
        [
          SCHEMA_VERSION, RULES_VERSION, @seed, "normal_evolution",
          @catalog.source_fingerprint, @catalog.taxonomy_fingerprint,
          @catalog.method_fingerprint,
          @catalog.normal_target_fingerprint,
          @base_stat_generator_version, @base_stat_source_fingerprint
        ]
      )
    end

    def graph
      return @graph if @graph
      generated = {}
      @branches_by_source.keys.sort.each do |source|
        generated[source] = generate_source(source)
      end
      validate_graph(generated)
      @graph = deep_freeze(generated)
      return @graph
    end

    def branches_for(source)
      identity = normalize_identity(source)
      return graph[identity] || []
    end

    def candidate_targets_for(source)
      identity = normalize_identity(source)
      candidates = {}
      @branches_by_source[identity].each do |branch|
        candidate_plans(identity, branch).each do |plan|
          plan[:candidates].each do |target|
            candidates[target[:identity]] = {
              :target => target[:identity],
              :target_id => target[:id],
              :target_bst => target[:bst]
            }
          end
        end
      end
      return candidates.values.sort_by { |target| target[:target] }
    end

    def graph_fingerprint
      return @graph_fingerprint if @graph_fingerprint
      values = [SCHEMA_VERSION, RULES_VERSION, @seed]
      graph.keys.sort.each do |source|
        values << source
        graph[source].each do |branch|
          values.concat([
            branch[:identity], branch[:target], branch[:target_bst],
            branch[:bucket], branch[:fallback],
            branch[:bucket_reassigned]
          ])
        end
      end
      @graph_fingerprint = fingerprint(values)
      return @graph_fingerprint
    end

    def validate_graph(graph_to_validate = graph)
      generated_branch_count = 0
      @branches_by_source.each do |source, native_branches|
        generated = graph_to_validate[source]
        if !generated || generated.length != native_branches.length
          raise EvolutionRandomizationError,
                "#{source} did not receive one target per conceptual branch"
        end
        generated_branch_count += generated.length
        targets = generated.map { |branch| branch[:target] }
        families = generated.map { |branch| branch[:target_family] }
        if targets.uniq.length != targets.length
          raise EvolutionRandomizationError,
                "#{source} received duplicate generated targets"
        end
        if families.uniq.length != families.length
          raise EvolutionRandomizationError,
                "#{source} received targets from duplicate native families"
        end
        generated.each do |branch|
          validate_generated_branch(source, branch)
        end
      end
      if generated_branch_count != @catalog.branch_catalog.length
        raise EvolutionRandomizationError,
              "normal evolution graph has an incomplete branch count"
      end
      return true
    end

    private

    def generate_source(source)
      branches = @branches_by_source[source]
      plans_by_branch = {}
      branches.each do |branch|
        plans_by_branch[branch[:identity]] = candidate_plans(source, branch)
      end
      selected_plans = {}
      assignment = find_plan_assignment(branches, plans_by_branch,
                                        selected_plans, 0)
      if !assignment
        raise EvolutionRandomizationError,
              "#{source} has no complete valid normal evolution assignment"
      end
      return branches.map do |branch|
        target = assignment[:targets][branch[:identity]]
        plan_index = assignment[:plan_indexes][branch[:identity]]
        plan = plans_by_branch[branch[:identity]][plan_index]
        build_generated_branch(source, branch, target, plan, plan_index)
      end
    end

    def find_plan_assignment(branches, plans_by_branch, selected, index)
      if index >= branches.length
        targets = solve_target_assignment(branches, selected)
        return nil if !targets
        plan_indexes = {}
        selected.each do |branch_identity, selection|
          plan_indexes[branch_identity] = selection[:index]
        end
        return { :targets => targets, :plan_indexes => plan_indexes }
      end
      branch = branches[index]
      plans = plans_by_branch[branch[:identity]]
      plans.each_with_index do |plan, plan_index|
        selected[branch[:identity]] = { :plan => plan, :index => plan_index }
        result = find_plan_assignment(branches, plans_by_branch, selected,
                                      index + 1)
        return result if result
      end
      selected.delete(branch[:identity])
      return nil
    end

    def solve_target_assignment(branches, selected)
      assigned = {}
      used_targets = {}
      used_families = {}
      return assign_next_target(branches, selected, assigned, used_targets,
                                used_families)
    end

    def assign_next_target(branches, selected, assigned, used_targets, used_families)
      return assigned.dup if assigned.length >= branches.length
      remaining = branches.reject { |branch| assigned.key?(branch[:identity]) }
      ranked = remaining.map do |branch|
        plan = selected[branch[:identity]][:plan]
        candidates = plan[:candidates].reject do |target|
          used_targets[target[:identity]] || used_families[target[:family]]
        end
        [candidates.length, branch[:identity], branch, candidates]
      end
      ranked.sort_by! { |entry| [entry[0], entry[1]] }
      _count, _identity, branch, candidates = ranked[0]
      return nil if candidates.empty?
      candidates.each do |target|
        assigned[branch[:identity]] = target
        used_targets[target[:identity]] = true
        used_families[target[:family]] = true
        result = assign_next_target(branches, selected, assigned, used_targets,
                                    used_families)
        return result if result
        assigned.delete(branch[:identity])
        used_targets.delete(target[:identity])
        used_families.delete(target[:family])
      end
      return nil
    end

    def candidate_plans(source, branch)
      source_entry = @target_by_identity[source]
      source_taxonomy = @taxonomy_by_identity[source]
      reference = @target_by_identity[branch[:original_destination]]
      hard_candidates = hard_candidates_for(
        source, source_entry, source_taxonomy,
        @catalog.required_target_types(source, branch)
      )
      if hard_candidates.empty?
        raise EvolutionRandomizationError,
              "#{branch[:identity]} has no structurally valid stronger target"
      end
      preferred_minimum = [
        divide_round_up(reference[:bst] * PREFERRED_MINIMUM_PERCENT, 100),
        source_entry[:bst] + 1
      ].max
      preferred_maximum =
        (reference[:bst] * PREFERRED_MAXIMUM_PERCENT) / 100
      preferred = hard_candidates.select do |target|
        target[:bst] >= preferred_minimum && target[:bst] <= preferred_maximum
      end
      if preferred.empty?
        distance = hard_candidates.map do |target|
          (target[:bst] - reference[:bst]).abs
        end.min
        closest = hard_candidates.select do |target|
          (target[:bst] - reference[:bst]).abs == distance
        end
        return [candidate_plan(branch, :fallback, closest, true,
                               preferred_minimum, preferred_maximum,
                               reference[:bst])]
      end
      buckets = allowed_buckets(source_taxonomy[:role])
      available = buckets.select do |bucket|
        preferred.any? { |target| target_bucket(target) == bucket }
      end
      if available.empty?
        raise EvolutionRandomizationError,
              "#{branch[:identity]} has no preferred candidate bucket"
      end
      ordered = ordered_buckets(branch, source_taxonomy[:role], available)
      return ordered.map do |bucket|
        candidates = preferred.select do |target|
          target_bucket(target) == bucket
        end
        candidate_plan(branch, bucket, candidates, false,
                       preferred_minimum, preferred_maximum, reference[:bst])
      end
    end

    def candidate_plan(branch, bucket, candidates, fallback, minimum, maximum, reference_bst)
      priority_base = deterministic_state(
        "candidate", branch[:identity], bucket
      )
      ordered = candidates.sort_by do |target|
        [deterministic_value_from(priority_base, target[:identity]),
         target[:identity]]
      end
      return {
        :bucket => bucket,
        :candidates => ordered,
        :fallback => fallback,
        :preferred_minimum => minimum,
        :preferred_maximum => maximum,
        :reference_bst => reference_bst
      }
    end

    def hard_candidate?(source, source_taxonomy, target, required_types)
      return false if target[:identity] == source[:identity]
      return false if target[:family] == source_taxonomy[:family]
      return false if target[:bst] <= source[:bst]
      return false if (target[:types] & required_types).empty?
      if source_taxonomy[:role] == :intermediate
        return [:final, :standalone].include?(target[:role])
      end
      if source_taxonomy[:role] == :first_stage
        return [:intermediate, :final, :standalone].include?(target[:role])
      end
      return false
    end

    def hard_candidates_for(source_identity, source, source_taxonomy, required_types)
      key = [source_identity, required_types.sort_by(&:to_s)]
      cached = @hard_candidates_by_source[key]
      return cached if cached
      candidates = @catalog.normal_target_catalog.select do |target|
        hard_candidate?(source, source_taxonomy, target, required_types)
      end
      @hard_candidates_by_source[key] = candidates.freeze
      return @hard_candidates_by_source[key]
    end

    def allowed_buckets(source_role)
      return [:terminal] if source_role == :intermediate
      return [:intermediate, :terminal] if source_role == :first_stage
      raise EvolutionRandomizationError,
            "#{source_role} source cannot receive generated branches"
    end

    def ordered_buckets(branch, source_role, available)
      return available if available.length <= 1 || source_role == :intermediate
      intermediate_weight = if
        branch[:original_destination_role] == :intermediate
                              INTERMEDIATE_WEIGHT_FOR_INTERMEDIATE_REFERENCE
                            else
                              INTERMEDIATE_WEIGHT_FOR_TERMINAL_REFERENCE
                            end
      roll = deterministic_value("bucket", branch[:identity]) % 100
      selected = roll < intermediate_weight ? :intermediate : :terminal
      selected = available[0] if !available.include?(selected)
      return [selected] + (available - [selected])
    end

    def target_bucket(target)
      return :intermediate if target[:role] == :intermediate
      return :terminal if [:final, :standalone].include?(target[:role])
      return :invalid
    end

    def build_generated_branch(source, branch, target, plan, plan_index)
      source_entry = @target_by_identity[source]
      return {
        :identity => branch[:identity],
        :source => source,
        :original_destination => branch[:original_destination],
        :target => target[:identity],
        :target_id => target[:id],
        :source_bst => source_entry[:bst],
        :reference_bst => plan[:reference_bst],
        :target_bst => target[:bst],
        :preferred_minimum => plan[:preferred_minimum],
        :preferred_maximum => plan[:preferred_maximum],
        :bucket => plan[:bucket],
        :fallback => plan[:fallback],
        :bucket_reassigned => plan_index > 0,
        :target_role => target[:role],
        :target_family => target[:family],
        :effective_methods => branch[:effective_methods]
      }
    end

    def validate_generated_branch(source, branch)
      source_taxonomy = @taxonomy_by_identity[source]
      target = @target_by_identity[branch[:target]]
      if !target || target[:id] != branch[:target_id]
        raise EvolutionRandomizationError,
              "#{branch[:identity]} has an invalid target identity"
      end
      if target[:family] == source_taxonomy[:family]
        raise EvolutionRandomizationError,
              "#{branch[:identity]} retained its native family"
      end
      required_types = @catalog.required_target_types(source, branch)
      if (target[:types] & required_types).empty?
        raise EvolutionRandomizationError,
              "#{branch[:identity]} does not share a source type"
      end
      if branch[:target_bst] <= branch[:source_bst]
        raise EvolutionRandomizationError,
              "#{branch[:identity]} is not a strict BST improvement"
      end
      allowed = allowed_buckets(source_taxonomy[:role])
      if !allowed.include?(target_bucket(target))
        raise EvolutionRandomizationError,
              "#{branch[:identity]} has an invalid target stage"
      end
      if !branch[:fallback] &&
         (branch[:target_bst] < branch[:preferred_minimum] ||
          branch[:target_bst] > branch[:preferred_maximum])
        raise EvolutionRandomizationError,
              "#{branch[:identity]} escaped its preferred BST range"
      end
      if branch[:fallback]
        validate_closest_fallback(source, branch)
      end
      return true
    end

    def validate_closest_fallback(source, branch)
      source_entry = @target_by_identity[source]
      source_taxonomy = @taxonomy_by_identity[source]
      candidates = @catalog.normal_target_catalog.select do |target|
        hard_candidate?(
          source_entry, source_taxonomy, target,
          @catalog.required_target_types(source, branch)
        )
      end
      distance = candidates.map do |target|
        (target[:bst] - branch[:reference_bst]).abs
      end.min
      actual = (branch[:target_bst] - branch[:reference_bst]).abs
      if actual != distance
        raise EvolutionRandomizationError,
              "#{branch[:identity]} did not use the closest BST fallback"
      end
    end

    def normalize_identity(source)
      return source.id.to_s if source.respond_to?(:id)
      data = GameData::Species.try_get(source)
      return data.id.to_s if data
      return source.to_s
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

    def fingerprint(values)
      return Ironmon.fnv1a_64_fingerprint(values)
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
end
