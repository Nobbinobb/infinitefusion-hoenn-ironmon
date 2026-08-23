#===============================================================================
# Deterministic complete-fusion evolution generation
#===============================================================================

module Ironmon
  class FusionEvolutionGenerator
    SCHEMA_VERSION = 1
    RULES_VERSION = 4
    PREFERRED_MINIMUM_PERCENT = 90
    PREFERRED_MAXIMUM_PERCENT = 115
    INTERMEDIATE_WEIGHT_FOR_INTERMEDIATE_REFERENCE = 60
    INTERMEDIATE_WEIGHT_FOR_TERMINAL_REFERENCE = 40
    PREDECESSOR_SCAN_CACHE_LIMIT = 8
    PREDECESSOR_PAGE_LIMIT = 50
    SOURCE_CONTEXT_CACHE_LIMIT = 512
    attr_reader :seed
    attr_reader :catalog
    attr_reader :target_pool_info

    def initialize(seed, catalog, target_species, target_pool_info, base_stat_generator, work_checkpoint = nil)
      @seed = seed.to_i
      @catalog = catalog
      @target_species = target_species
      @target_pool_info = target_pool_info
      @base_stat_generator = base_stat_generator
      @work_checkpoint = work_checkpoint
      @valid_fusion_identities = {}
      target_species.each_with_index do |species_id, index|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        @valid_fusion_identities[species_id.to_s] = true
      end
      @taxonomy_by_identity = {}
      @family_id_by_family = {}
      @branches_by_source = Hash.new { |hash, key| hash[key] = [] }
      catalog.taxonomy_catalog.each do |entry|
        @taxonomy_by_identity[entry[:identity]] = entry
      end
      families = catalog.taxonomy_catalog.map { |entry| entry[:family] }
      families.uniq.sort.each_with_index do |family, index|
        @family_id_by_family[family] = index
      end
      catalog.branch_catalog.each do |branch|
        @branches_by_source[branch[:source]] << branch
      end
      @branches_by_source.each_value do |branches|
        branches.sort_by! { |branch| branch[:identity] }
      end
      @target_pools = nil
      @target_pools_by_type = nil
      @target_pools_by_type_and_bst = nil
      @target_pools_by_type_signature = {}
      @target_pools_by_type_signature_and_bst = {}
      @empty_type_signature = [].freeze
      @type_signatures_by_first_type = {}
      @component_branch_metadata_by_object_id = {}
      catalog.branch_catalog.each do |branch|
        component = GameData::Species.get(branch[:source])
        component_taxonomy = taxonomy_for(component)
        required_types = @catalog.required_target_types(component, branch)
        @component_branch_metadata_by_object_id[branch.object_id] = {
          :component => component,
          :component_taxonomy => component_taxonomy,
          :allowed_buckets => allowed_buckets(component_taxonomy[:role]),
          :required_types => required_types,
          :type_signature => canonical_type_signature(required_types),
          :destination => GameData::Species.get(branch[:original_destination])
        }.freeze
      end
      @seed_target_index_build_milliseconds = 0
      @target_by_identity = nil
      @target_bst_cache = {}
      @component_stats_by_identity = {}
      @hard_candidates_by_source = {}
      @source_contexts_by_fusion = {}
      @standard_assignment_state_by_source_id =
        "\0" * ((NB_POKEMON * (NB_POKEMON + 1)) + 1)
      @performance_timings = Hash.new(0.0)
      @branches_by_fusion = {}
      @candidate_targets_by_fusion = {}
      @predecessors_by_fusion = {}
      @predecessor_diagnostics_by_fusion = {}
      @predecessor_scans_by_fusion = {}
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
      return [] if !valid_fusion_source?(species)
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
      return { :head => [], :body => [] } if !valid_fusion_source?(species)
      identity = species.id.to_s
      cached = @candidate_targets_by_fusion[identity]
      return cached if cached
      context = source_context_for(species)
      result = { :head => {}, :body => {} }
      context[:branches].each do |branch|
        candidates = candidate_targets_for_branch(
          context, branch
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

    def predecessor_branches_for(target)
      species = GameData::Species.try_get(target)
      return [] if !valid_fusion_source?(species)
      identity = species.id.to_s
      return @predecessors_by_fusion[identity] if
        @predecessors_by_fusion.key?(identity)
      predecessors, diagnostics = generate_predecessors(species)
      @predecessors_by_fusion[identity] = deep_freeze(predecessors)
      @predecessor_diagnostics_by_fusion[identity] = deep_freeze(diagnostics)
      return @predecessors_by_fusion[identity]
    end

    def predecessor_lookup_diagnostics(target)
      species = GameData::Species.try_get(target)
      return nil if !valid_fusion_source?(species)
      identity = species.id.to_s
      return @predecessor_diagnostics_by_fusion[identity] if
        @predecessor_diagnostics_by_fusion.key?(identity)
      scan = @predecessor_scans_by_fusion[identity]
      return predecessor_scan_diagnostics(scan) if scan
      predecessor_branches_for(species) if
        !@predecessor_diagnostics_by_fusion.key?(identity)
      return @predecessor_diagnostics_by_fusion[identity]
    end

    def predecessor_page_for(target, offset, limit)
      normalized_offset = offset.to_i
      normalized_limit = limit.to_i
      validate_predecessor_page(normalized_offset, normalized_limit)
      species = GameData::Species.try_get(target)
      return empty_predecessor_page(
        normalized_offset, normalized_limit
      ) if !valid_fusion_source?(species)
      identity = species.id.to_s
      if @predecessors_by_fusion.key?(identity)
        return predecessor_page_from_complete_result(
          @predecessors_by_fusion[identity], normalized_offset,
          normalized_limit, @predecessor_diagnostics_by_fusion[identity]
        )
      end
      scan = predecessor_scan_for(species)
      advance_predecessor_scan(
        scan, normalized_offset + normalized_limit
      )
      return predecessor_page_from_scan(
        scan, normalized_offset, normalized_limit
      )
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
      native = conceptual_branches_for(
        species.id.to_s, species.body_pokemon, species.head_pokemon
      )
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

    def generate_predecessors(target_species)
      scan = predecessor_scan_for(target_species)
      advance_predecessor_scan(scan, nil)
      return [scan[:predecessors], predecessor_scan_diagnostics(scan)]
    end

    def predecessor_scan_for(target_species)
      identity = target_species.id.to_s
      cached = @predecessor_scans_by_fusion.delete(identity)
      if cached
        @predecessor_scans_by_fusion[identity] = cached
        return cached
      end
      scan = build_predecessor_scan(target_species)
      @predecessor_scans_by_fusion[identity] = scan
      while @predecessor_scans_by_fusion.length > PREDECESSOR_SCAN_CACHE_LIMIT
        @predecessor_scans_by_fusion.shift
      end
      return scan
    end

    def build_predecessor_scan(target_species)
      started_at = Time.now
      build_target_pools
      target = @target_by_identity[target_species.id.to_s]
      if !target
        return empty_predecessor_scan(target_species, started_at)
      end
      target_family_ids = target[:family_ids]
      target_types = target[:types]
      target_strength = target_bst(target)
      initialization_milliseconds = elapsed_milliseconds(started_at)
      structural_started_at = Time.now
      compatible_branches = 0
      compatible_components = {}
      @catalog.branch_catalog.each do |component_branch|
        metadata = component_branch_metadata(component_branch)
        component = metadata[:component]
        next if !metadata[:allowed_buckets].include?(target[:bucket])
        next if !types_overlap?(metadata[:required_types], target_types)
        compatible_branches += 1
        compatible_components[component.id.to_s] = component
      end
      source_ids = Ironmon.fusion_predecessor_index.source_ids_for(
        target[:bucket], target_types
      )
      family_candidates = []
      source_ids.each do |source_id|
        body, head = fusion_components_for_source_id(source_id)
        source_family_ids = family_ids_for_components(body, head)
        next if family_ids_overlap?(source_family_ids, target_family_ids)
        family_candidates << [source_id, body, head]
      end
      structural_milliseconds = elapsed_milliseconds(structural_started_at)
      bst_started_at = Time.now
      sources = {}
      family_candidates.each do |source_id, body, head|
        source_bst = fusion_bst_for_components(body, head)
        next if source_bst >= target_strength
        sources[fusion_identity_for_source_id(source_id)] = [
          source_id, source_bst
        ]
      end
      bst_milliseconds = elapsed_milliseconds(bst_started_at)
      ordered_sources = sources.keys.sort.map { |identity| sources[identity] }
      return {
        :target_entry => target,
        :target_bst => target_strength,
        :sources => ordered_sources.map { |entry| entry[0] },
        :source_bsts => ordered_sources.map { |entry| entry[1] },
        :next_source_index => 0,
        :predecessors => [],
        :complete => sources.empty?,
        :initialization_milliseconds => initialization_milliseconds,
        :structural_lookup_milliseconds => structural_milliseconds,
        :bst_filter_milliseconds => bst_milliseconds,
        :seed_index_pending => !@target_pools_by_type_and_bst,
        :seed_index_build_milliseconds => 0,
        :seeded_ranking_milliseconds => 0,
        :assignment_verification_milliseconds => 0,
        :verification_candidate_gathering_milliseconds => 0,
        :verification_candidate_ranking_milliseconds => 0,
        :verification_assignment_solver_milliseconds => 0,
        :verification_other_milliseconds => 0,
        :standard_shortlist_count => 0,
        :upward_rescue_check_count => 0,
        :verified_source_count => 0,
        :generated_source_count => 0,
        :catalog_branch_count => @catalog.branch_catalog.length,
        :compatible_component_branch_count => compatible_branches,
        :compatible_component_count => compatible_components.length,
        :source_combination_count => source_ids.length,
        :family_eligible_count => family_candidates.length,
        :bst_eligible_count => sources.length
      }
    end

    def empty_predecessor_scan(target_species, started_at)
      return {
        :target_entry => {
          :identity => target_species.id.to_s,
          :bucket => nil,
          :types => [],
          :families => []
        },
        :target_bst => nil,
        :sources => [],
        :source_bsts => [],
        :next_source_index => 0,
        :predecessors => [],
        :complete => true,
        :initialization_milliseconds => elapsed_milliseconds(started_at),
        :structural_lookup_milliseconds => 0,
        :bst_filter_milliseconds => 0,
        :seed_index_pending => false,
        :seed_index_build_milliseconds => 0,
        :seeded_ranking_milliseconds => 0,
        :assignment_verification_milliseconds => 0,
        :verification_candidate_gathering_milliseconds => 0,
        :verification_candidate_ranking_milliseconds => 0,
        :verification_assignment_solver_milliseconds => 0,
        :verification_other_milliseconds => 0,
        :standard_shortlist_count => 0,
        :upward_rescue_check_count => 0,
        :verified_source_count => 0,
        :generated_source_count => 0,
        :catalog_branch_count => @catalog.branch_catalog.length,
        :compatible_component_branch_count => 0,
        :compatible_component_count => 0,
        :source_combination_count => 0,
        :family_eligible_count => 0,
        :bst_eligible_count => 0
      }
    end

    def advance_predecessor_scan(scan, required_result_count)
      target = scan[:target_entry]
      while !scan[:complete]
        if required_result_count &&
           scan[:predecessors].length >= required_result_count
          break
        end
        source_index = scan[:next_source_index]
        source_id = scan[:sources][source_index]
        if !source_id
          scan[:complete] = true
          break
        end
        scan[:next_source_index] += 1
        context = source_context_for_source_id(
          source_id, scan[:source_bsts][source_index]
        )
        ranking_started_at = Time.now
        verification_required = if target_in_standard_plans?(context, target)
                                  scan[:standard_shortlist_count] += 1
                                  true
                                elsif standard_assignment_guaranteed?(
                                  context, source_id
                                )
                                  false
                                else
                                  scan[:upward_rescue_check_count] += 1
                                  true
                                end
        scan[:seeded_ranking_milliseconds] += elapsed_milliseconds_precise(
          ranking_started_at
        )
        if scan[:seed_index_pending] && @target_pools_by_type_and_bst
          scan[:seed_index_pending] = false
          scan[:seed_index_build_milliseconds] =
            @seed_target_index_build_milliseconds
        end
        next if !verification_required
        scan[:verified_source_count] += 1
        source = fusion_species_for_source_id(source_id)
        context[:species] = source
        cache_source_context(context)
        source_was_cached = @branches_by_fusion.key?(source.id.to_s)
        performance_before = @performance_timings.dup
        verification_started_at = Time.now
        branches_for(source).each do |branch|
          scan[:predecessors] << branch if
            branch[:target] == target[:identity]
        end
        verification_milliseconds = elapsed_milliseconds_precise(
          verification_started_at
        )
        gathering_milliseconds = performance_timing_delta(
          performance_before, :hard_candidate_build
        )
        ranking_milliseconds = performance_timing_delta(
          performance_before, :candidate_prefix_ranking
        )
        solver_milliseconds = performance_timing_delta(
          performance_before, :assignment_solver
        )
        scan[:assignment_verification_milliseconds] += verification_milliseconds
        scan[:verification_candidate_gathering_milliseconds] +=
          gathering_milliseconds
        scan[:verification_candidate_ranking_milliseconds] +=
          ranking_milliseconds
        scan[:verification_assignment_solver_milliseconds] +=
          solver_milliseconds
        measured_milliseconds = gathering_milliseconds + ranking_milliseconds +
          solver_milliseconds
        scan[:verification_other_milliseconds] += [
          verification_milliseconds - measured_milliseconds, 0
        ].max
        scan[:generated_source_count] += 1 if !source_was_cached
      end
      scan[:complete] = true if
        scan[:next_source_index] >= scan[:sources].length
      return scan
    end

    def predecessor_scan_diagnostics(scan)
      target = scan[:target_entry]
      elapsed = scan[:initialization_milliseconds] +
        scan[:structural_lookup_milliseconds] +
        scan[:bst_filter_milliseconds] +
        scan[:seeded_ranking_milliseconds] +
        scan[:assignment_verification_milliseconds]
      return {
        :target => target[:identity],
        :target_bucket => target[:bucket],
        :target_types => target[:types],
        :target_bst => scan[:target_bst],
        :catalog_branch_count => scan[:catalog_branch_count],
        :compatible_component_branch_count =>
          scan[:compatible_component_branch_count],
        :compatible_component_count => scan[:compatible_component_count],
        :source_combination_count => scan[:source_combination_count],
        :family_eligible_count => scan[:family_eligible_count],
        :bst_eligible_count => scan[:bst_eligible_count],
        :eligible_source_count => scan[:sources].length,
        :scanned_source_count => scan[:next_source_index],
        :standard_shortlist_count => scan[:standard_shortlist_count],
        :upward_rescue_check_count => scan[:upward_rescue_check_count],
        :verified_source_count => scan[:verified_source_count],
        :generated_source_count => scan[:generated_source_count],
        :predecessor_count => scan[:predecessors].length,
        :complete => scan[:complete],
        :initialization_milliseconds => scan[:initialization_milliseconds],
        :structural_lookup_milliseconds => scan[:structural_lookup_milliseconds],
        :bst_filter_milliseconds => scan[:bst_filter_milliseconds],
        :seed_index_build_milliseconds =>
          scan[:seed_index_build_milliseconds],
        :seeded_ranking_milliseconds =>
          scan[:seeded_ranking_milliseconds].round,
        :assignment_verification_milliseconds =>
          scan[:assignment_verification_milliseconds].round,
        :verification_candidate_gathering_milliseconds =>
          scan[:verification_candidate_gathering_milliseconds].round,
        :verification_candidate_ranking_milliseconds =>
          scan[:verification_candidate_ranking_milliseconds].round,
        :verification_assignment_solver_milliseconds =>
          scan[:verification_assignment_solver_milliseconds].round,
        :verification_other_milliseconds =>
          scan[:verification_other_milliseconds].round,
        :elapsed_milliseconds => elapsed.round
      }
    end

    def predecessor_page_from_scan(scan, offset, limit)
      diagnostics = predecessor_scan_diagnostics(scan)
      return predecessor_page_result(
        scan[:predecessors], offset, limit, scan[:complete], diagnostics
      )
    end

    def predecessor_page_from_complete_result(predecessors, offset, limit, diagnostics)
      return predecessor_page_result(
        predecessors, offset, limit, true, diagnostics
      )
    end

    def predecessor_page_result(predecessors, offset, limit, complete, diagnostics)
      branches = predecessors[offset, limit] || []
      continuation = if predecessors.length > offset + limit
                       :available
                     elsif complete
                       :complete
                     else
                       :unknown
                     end
      return deep_freeze({
        :branches => branches.dup,
        :offset => offset,
        :limit => limit,
        :continuation => continuation,
        :next_offset => continuation == :complete ? nil : offset + limit,
        :diagnostics => diagnostics
      })
    end

    def empty_predecessor_page(offset, limit)
      return deep_freeze({
        :branches => [],
        :offset => offset,
        :limit => limit,
        :continuation => :complete,
        :next_offset => nil,
        :diagnostics => nil
      })
    end

    def validate_predecessor_page(offset, limit)
      if offset < 0
        raise ArgumentError, "predecessor page offset cannot be negative"
      end
      if limit < 1 || limit > PREDECESSOR_PAGE_LIMIT
        raise ArgumentError,
              "predecessor page limit must be between 1 and #{PREDECESSOR_PAGE_LIMIT}"
      end
    end

    def fusion_components_for_source_id(source_id)
      body_id = (source_id - 1) / NB_POKEMON
      head_id = source_id - (body_id * NB_POKEMON)
      return [
        GameData::Species.get(body_id), GameData::Species.get(head_id)
      ]
    end

    def fusion_species_for_source_id(source_id)
      body_id = (source_id - 1) / NB_POKEMON
      head_id = source_id - (body_id * NB_POKEMON)
      return GameData::Species.get("B#{body_id}H#{head_id}".to_sym)
    end

    def fusion_identity_for_source_id(source_id)
      body_id = (source_id - 1) / NB_POKEMON
      head_id = source_id - (body_id * NB_POKEMON)
      return "B#{body_id}H#{head_id}"
    end

    def fusion_bst_for_components(body, head)
      return fused_bst(
        component_stats_for(body), component_stats_for(head)
      )
    end

    def elapsed_milliseconds(started_at)
      return ((Time.now - started_at) * 1000).round
    end

    def elapsed_milliseconds_precise(started_at)
      return (Time.now - started_at) * 1000
    end

    def measure_performance(name)
      started_at = Time.now
      result = yield
      @performance_timings[name] += elapsed_milliseconds_precise(started_at)
      return result
    end

    def performance_timing_delta(before, name)
      return @performance_timings[name] - before.fetch(name, 0.0)
    end

    def target_in_standard_plans?(context, target)
      context[:branches].each do |branch|
        metadata = component_branch_metadata(branch[:component_branch])
        next if !metadata[:allowed_buckets].include?(target[:bucket])
        next if !types_overlap?(metadata[:required_types], target[:types])
        branch_context = branch_context_for(context, branch, metadata)
        minimum = branch_context[:minimum]
        maximum = branch_context[:maximum]
        target_strength = target_bst(target)
        if target_strength >= minimum && target_strength <= maximum
          return true if target_in_deterministic_prefix?(
            context, branch, branch_context, target
          )
          next
        end
        preferred_exists = metadata[:allowed_buckets].any? do |bucket|
          candidate_count_at_least?(
            bucket, context[:source_family_ids],
            branch_context[:type_signature],
            minimum, maximum, 1
          )
        end
        next if preferred_exists
        plans = candidate_plans(context, branch)
        return true if plans[0][:candidates].any? do |candidate|
          candidate[:identity] == target[:identity]
        end
      end
      return false
    end

    def target_in_deterministic_prefix?(context, branch, branch_context, selected_target)
      source_family_ids = context[:source_family_ids]
      type_signature = branch_context[:type_signature]
      minimum = branch_context[:minimum]
      maximum = branch_context[:maximum]
      limit = context[:branches].length
      priority = candidate_priority_for(
        branch, branch_context, selected_target[:bucket]
      )
      ranked = deterministic_type_candidate_prefix(
        selected_target[:bucket], type_signature, minimum, maximum,
        source_family_ids, priority, limit
      )
      return false if !ranked.any? do |target|
        target[:identity] == selected_target[:identity]
      end
      plan = candidate_plan_from_ordered(
        selected_target[:bucket], ranked, false,
        minimum, maximum, branch_context[:reference_bst]
      )
      branch_context[:plans_by_bucket][selected_target[:bucket]] = plan
      return true
    end

    def standard_assignment_guaranteed?(context, source_id)
      state = @standard_assignment_state_by_source_id.getbyte(source_id)
      return state == 2 if state != 0
      if context[:branches].empty?
        @standard_assignment_state_by_source_id.setbyte(source_id, 2)
        return true
      end
      guaranteed = true
      context[:branches].each do |branch|
        metadata = component_branch_metadata(branch[:component_branch])
        branch_context = branch_context_for(context, branch, metadata)
        sufficient = metadata[:allowed_buckets].any? do |bucket|
          candidate_count_at_least?(
            bucket, context[:source_family_ids],
            branch_context[:type_signature],
            branch_context[:minimum], branch_context[:maximum],
            context[:branches].length
          )
        end
        if !sufficient
          guaranteed = false
          break
        end
      end
      @standard_assignment_state_by_source_id.setbyte(
        source_id, guaranteed ? 2 : 1
      )
      return guaranteed
    end

    def candidate_count_at_least?(bucket, source_family_ids, type_signature, minimum, maximum, required_count)
      count = 0
      each_target_in_indexed_bst_range(
        bucket, type_signature, minimum, maximum
      ) do |target|
        next if family_ids_overlap?(target[:family_ids], source_family_ids)
        count += 1
        return true if count >= required_count
      end
      return false
    end

    def standard_fusion?(species)
      return false if !species || !species.is_a?(GameData::FusedSpecies)
      return false if species.id_number <= NB_POKEMON
      return false if species.id_number >= Settings::ZAPMOLCUNO_NB
      body = species.body_pokemon
      head = species.head_pokemon
      return normal_component?(body) && normal_component?(head)
    end

    def valid_fusion_source?(species)
      return standard_fusion?(species) &&
        @valid_fusion_identities[species.id.to_s] == true
    end

    def normal_component?(species)
      return species && species.id_number > 0 &&
        species.id_number <= NB_POKEMON
    end

    def conceptual_branches_for(identity, body, head)
      branches = []
      [[:body, body], [:head, head]].each do |side, component|
        @branches_by_source[component.id.to_s].each do |branch|
          branches << {
            :identity => "#{identity}|#{side}|#{branch[:identity]}",
            :side => side,
            :component => component,
            :component_branch => branch
          }
        end
      end
      return branches
    end

    def source_context_for(species)
      identity = species.id.to_s
      cached = @source_contexts_by_fusion.delete(identity)
      if cached
        @source_contexts_by_fusion[identity] = cached
        return cached
      end
      context = build_source_context(
        identity, species.body_pokemon, species.head_pokemon, species
      )
      cache_source_context(context)
      return context
    end

    def source_context_for_source_id(source_id, source_bst)
      body, head = fusion_components_for_source_id(source_id)
      identity = fusion_identity_for_source_id(source_id)
      return build_source_context(identity, body, head, nil, source_bst)
    end

    def build_source_context(identity, body, head, species, source_bst = nil)
      return {
        :identity => identity,
        :species => species,
        :body => body,
        :head => head,
        :branches => conceptual_branches_for(identity, body, head),
        :source_bst => source_bst || fusion_bst_for_components(body, head),
        :source_family_ids => family_ids_for_components(body, head),
        :branch_contexts => {}
      }
    end

    def cache_source_context(context)
      identity = context[:identity]
      @source_contexts_by_fusion.delete(identity)
      @source_contexts_by_fusion[identity] = context
      while @source_contexts_by_fusion.length > SOURCE_CONTEXT_CACHE_LIMIT
        @source_contexts_by_fusion.shift
      end
      return context
    end

    def component_branch_metadata(component_branch)
      return @component_branch_metadata_by_object_id.fetch(
        component_branch.object_id
      )
    end

    def branch_context_for(context, branch, metadata = nil)
      cached = context[:branch_contexts][branch[:identity]]
      return cached if cached
      metadata ||= component_branch_metadata(branch[:component_branch])
      reference_bst = natural_reference_bst(context, branch, metadata)
      minimum = [
        divide_round_up(reference_bst * PREFERRED_MINIMUM_PERCENT, 100),
        context[:source_bst] + 1
      ].max
      maximum = (reference_bst * PREFERRED_MAXIMUM_PERCENT) / 100
      branch_context = {
        :component_taxonomy => metadata[:component_taxonomy],
        :required_types => metadata[:required_types],
        :type_signature => metadata[:type_signature],
        :reference_bst => reference_bst,
        :minimum => minimum,
        :maximum => maximum,
        :plans_by_bucket => {},
        :priorities_by_bucket => {}
      }
      context[:branch_contexts][branch[:identity]] = branch_context
      return branch_context
    end

    def generate_source(species)
      context = source_context_for(species)
      branches = context[:branches]
      return [] if branches.empty?
      plans_by_branch = {}
      branches.each do |branch|
        plans_by_branch[branch[:identity]] = candidate_plans(context, branch)
      end
      selected = {}
      assignment = measure_performance(:assignment_solver) do
        find_plan_assignment(branches, plans_by_branch, selected, 0)
      end
      if !assignment
        plans_by_branch = upward_expansion_plans(context)
        assignment = measure_performance(:assignment_solver) do
          find_upward_expansion_assignment(branches, plans_by_branch)
        end
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
          species, branch, target, plan, plan_index, context[:source_bst]
        )
      end
    end

    def upward_expansion_plans(context)
      result = {}
      context[:branches].each do |branch|
        result[branch[:identity]] = [upward_expansion_plan(
          context, branch
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

    def upward_expansion_plan(context, branch)
      branch_context = branch_context_for(context, branch)
      component_taxonomy = branch_context[:component_taxonomy]
      reference_bst = branch_context[:reference_bst]
      hard_candidates = hard_candidates_for(
        context[:identity], component_taxonomy[:role], context[:source_bst],
        context[:source_family_ids], branch_context[:type_signature]
      )
      minimum = branch_context[:minimum]
      maximum = branch_context[:maximum]
      preferred = candidates_in_bst_range(
        hard_candidates, minimum, maximum
      )
      standard = []
      metadata = {}
      if preferred.empty?
        closest = closest_bst_candidates(hard_candidates, reference_bst)
        standard = deterministic_candidate_order(
          closest,
          candidate_priority_for(branch, branch_context, :fallback)
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
            candidate_priority_for(branch, branch_context, bucket)
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

    def candidate_plans(context, branch)
      branch_context = branch_context_for(context, branch)
      component_taxonomy = branch_context[:component_taxonomy]
      reference_bst = branch_context[:reference_bst]
      minimum = branch_context[:minimum]
      maximum = branch_context[:maximum]
      preferred_plans = {}
      available = allowed_buckets(component_taxonomy[:role]).select do |bucket|
        plan = preferred_candidate_plan(
          context, branch, branch_context, bucket, minimum, maximum,
          reference_bst
        )
        preferred_plans[bucket] = plan
        !plan[:candidates].empty?
      end
      if available.empty?
        hard_candidates = hard_candidates_for(
          context[:identity], component_taxonomy[:role], context[:source_bst],
          context[:source_family_ids], branch_context[:type_signature]
        )
        if hard_candidates.empty?
          raise EvolutionRandomizationError,
                "#{branch[:identity]} has no valid stronger custom target"
        end
        cached = branch_context[:plans_by_bucket][:fallback]
        return [cached] if cached
        closest = closest_bst_candidates(hard_candidates, reference_bst)
        plan = candidate_plan(
          branch, branch_context, :fallback, closest, true, minimum, maximum,
          reference_bst, context[:branches].length
        )
        branch_context[:plans_by_bucket][:fallback] = plan
        return [plan]
      end
      ordered = ordered_buckets(branch[:component_branch], available)
      return ordered.map do |bucket|
        cached = branch_context[:plans_by_bucket][bucket]
        next cached if cached
        plan = preferred_plans[bucket]
        branch_context[:plans_by_bucket][bucket] = plan
        plan
      end
    end

    def candidate_targets_for_branch(context, branch)
      branch_context = branch_context_for(context, branch)
      component_taxonomy = branch_context[:component_taxonomy]
      reference_bst = branch_context[:reference_bst]
      hard_candidates = hard_candidates_for(
        context[:identity], component_taxonomy[:role], context[:source_bst],
        context[:source_family_ids], branch_context[:type_signature]
      )
      return [] if hard_candidates.empty?
      minimum = branch_context[:minimum]
      maximum = branch_context[:maximum]
      preferred = candidates_in_bst_range(hard_candidates, minimum, maximum)
      return preferred if !preferred.empty?
      return closest_bst_candidates(hard_candidates, reference_bst)
    end

    def candidate_plan(branch, branch_context, bucket, candidates, fallback, minimum, maximum, reference_bst, assignment_size)
      priority = candidate_priority_for(branch, branch_context, bucket)
      ordered = measure_performance(:candidate_prefix_ranking) do
        deterministic_candidate_prefix(candidates, priority, assignment_size)
      end
      return candidate_plan_from_ordered(
        bucket, ordered, fallback, minimum, maximum, reference_bst
      )
    end

    def preferred_candidate_plan(context, branch, branch_context, bucket, minimum, maximum, reference_bst)
      cached = branch_context[:plans_by_bucket][bucket]
      return cached if cached
      priority = candidate_priority_for(branch, branch_context, bucket)
      ordered = measure_performance(:candidate_prefix_ranking) do
        deterministic_type_candidate_prefix(
          bucket, branch_context[:type_signature], minimum, maximum,
          context[:source_family_ids], priority, context[:branches].length
        )
      end
      return candidate_plan_from_ordered(
        bucket, ordered, false, minimum, maximum, reference_bst
      )
    end

    def candidate_priority_for(branch, branch_context, bucket)
      cached = branch_context[:priorities_by_bucket][bucket]
      return cached if cached
      priority = deterministic_state("candidate", branch[:identity], bucket)
      branch_context[:priorities_by_bucket][bucket] = priority
      return priority
    end

    def candidate_plan_from_ordered(bucket, ordered, fallback, minimum, maximum, reference_bst)
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
      return deterministic_range_prefix(
        candidates, 0, candidates.length, priority, limit, nil
      )
    end

    def deterministic_candidate_order(candidates, priority)
      return deterministic_candidate_prefix(
        candidates, priority, candidates.length
      )
    end

    def deterministic_type_candidate_prefix(bucket, type_signature, minimum, maximum, source_family_ids, priority, limit)
      targets = targets_for_type_signature(bucket, type_signature)
      return deterministic_range_prefix(
        targets, 0, targets.length, priority, limit, source_family_ids
      ) do |target|
        strength = target_bst(target)
        strength >= minimum && strength <= maximum
      end
    end

    def deterministic_range_prefix(targets, first, after, priority, limit, excluded_family_ids)
      return [] if limit <= 0 || first >= after
      length = after - first
      swaps = {}
      state = priority
      result = []
      position = 0
      while position < length && result.length < limit
        @work_checkpoint.call if @work_checkpoint && (position % 32).zero?
        offset, state = deterministic_bounded_value(state, length - position)
        selected_position = position + offset
        original_position = swaps.fetch(selected_position, selected_position)
        current_position = swaps.fetch(position, position)
        swaps[selected_position] = current_position if
          selected_position != position
        swaps.delete(position)
        target = targets[first + original_position]
        position += 1
        next if excluded_family_ids && family_ids_overlap?(
          target[:family_ids], excluded_family_ids
        )
        next if block_given? && !yield(target)
        result << target
      end
      return result
    end

    def deterministic_bounded_value(state, bound)
      rejection_threshold = (Ironmon::FNV1A_64_MASK + 1) % bound
      loop do
        state, value = next_shuffle_value(state)
        return [value % bound, state] if value >= rejection_threshold
      end
    end

    def next_shuffle_value(state)
      state = (state + 0x9E3779B97F4A7C15) & Ironmon::FNV1A_64_MASK
      value = state
      value = ((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9) &
        Ironmon::FNV1A_64_MASK
      value = ((value ^ (value >> 27)) * 0x94D049BB133111EB) &
        Ironmon::FNV1A_64_MASK
      value ^= value >> 31
      return [state, value & Ironmon::FNV1A_64_MASK]
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

    def hard_candidates_for(source_identity, source_role, source_bst, source_family_ids, source_types)
      type_signature = canonical_type_signature(source_types)
      key = [source_identity, source_role, type_signature]
      cached = @hard_candidates_by_source[key]
      return cached if cached
      candidates = measure_performance(:hard_candidate_build) do
        if @target_pools_by_type_and_bst
          ranges = []
          allowed_buckets(source_role).each do |bucket|
            range = indexed_bst_range(
              bucket, type_signature, source_bst + 1, nil
            )
            ranges << range if range
          end
          merge_indexed_candidate_ranges(
            ranges, source_bst, source_family_ids
          )
        else
          unindexed_hard_candidates(
            source_role, source_bst, source_family_ids, type_signature
          )
        end
      end
      @hard_candidates_by_source[key] = candidates.freeze
      return @hard_candidates_by_source[key]
    end

    def unindexed_hard_candidates(source_role, source_bst, source_family_ids, type_signature)
      candidates = []
      allowed_buckets(source_role).each do |bucket|
        candidates.concat(
          targets_for_type_signature(bucket, type_signature)
        )
      end
      filtered = []
      candidates.each_with_index do |target, index|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        next if family_ids_overlap?(target[:family_ids], source_family_ids)
        next if target_bst(target) <= source_bst
        filtered << target
      end
      index = 0
      filtered.sort_by! do |target|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        index += 1
        [target_bst(target), target[:identity]]
      end
      return filtered
    end

    def merge_indexed_candidate_ranges(ranges, source_bst, source_family_ids)
      positions = ranges.map { |range| range[1] }
      candidates = []
      loop do
        selected_range_index = nil
        selected_target = nil
        ranges.each_with_index do |range, range_index|
          position = positions[range_index]
          next if position >= range[2]
          target = range[0][position]
          if !selected_target || indexed_target_before?(target, selected_target)
            selected_range_index = range_index
            selected_target = target
          end
        end
        break if !selected_target
        positions[selected_range_index] += 1
        next if family_ids_overlap?(
          selected_target[:family_ids], source_family_ids
        )
        next if target_bst(selected_target) <= source_bst
        candidates << selected_target
      end
      return candidates
    end

    def indexed_target_before?(left, right)
      comparison = target_bst(left) <=> target_bst(right)
      return comparison < 0 if comparison != 0
      return left[:identity] < right[:identity]
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
      @target_species.each_with_index do |target_id, index|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
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
          :family_ids => [
            @family_id_by_family[body_taxonomy[:family]],
            @family_id_by_family[head_taxonomy[:family]]
          ].uniq.freeze,
          :types => fusion_target_types(body, head).freeze
        }.freeze
        pools[bucket] << entry
        by_identity[entry[:identity]] = entry
      end
      pools.each_value do |entries|
        index = 0
        entries.sort_by! do |entry|
          @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
          index += 1
          entry[:identity]
        end
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

    def target_pools_by_type
      return @target_pools_by_type if @target_pools_by_type
      result = {}
      target_pools.each do |bucket, targets|
        by_type = {}
        targets.each_with_index do |target, index|
          @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
          target[:types].each do |type|
            by_type[type] ||= []
            by_type[type] << target
          end
        end
        by_type.each_value(&:freeze)
        result[bucket] = by_type
      end
      @target_pools_by_type = result.freeze
      return @target_pools_by_type
    end

    def targets_for_type_signature(bucket, type_signature)
      by_signature = @target_pools_by_type_signature[bucket] ||= {}
      cached = by_signature[type_signature]
      return cached if cached
      lists = type_signature.map do |type|
        target_pools_by_type[bucket][type] || []
      end
      combined = merge_sorted_target_lists(lists, false)
      by_signature[type_signature] = combined.freeze
      return by_signature[type_signature]
    end

    def target_pools_by_type_and_bst
      return @target_pools_by_type_and_bst if @target_pools_by_type_and_bst
      started_at = Time.now
      result = {}
      target_pools_by_type.each do |bucket, by_type|
        sorted_by_type = {}
        by_type.each do |type, targets|
          sorted_by_type[type] = targets.sort_by do |target|
            [target_bst(target), target[:identity]]
          end.freeze
        end
        result[bucket] = sorted_by_type.freeze
      end
      @target_pools_by_type_and_bst = result.freeze
      @seed_target_index_build_milliseconds = elapsed_milliseconds(started_at)
      return @target_pools_by_type_and_bst
    end

    def targets_for_type_signature_and_bst(bucket, type_signature)
      by_signature = @target_pools_by_type_signature_and_bst[bucket] ||= {}
      cached = by_signature[type_signature]
      return cached if cached
      lists = type_signature.map do |type|
        target_pools_by_type_and_bst[bucket][type] || []
      end
      combined = merge_sorted_target_lists(lists, true)
      by_signature[type_signature] = combined.freeze
      return by_signature[type_signature]
    end

    def merge_sorted_target_lists(lists, by_bst)
      return [] if lists.empty?
      return lists[0] if lists.length == 1
      positions = Array.new(lists.length, 0)
      result = []
      last_identity = nil
      iteration = 0
      loop do
        @work_checkpoint.call if @work_checkpoint && (iteration % 32).zero?
        iteration += 1
        selected_list_index = nil
        selected_target = nil
        lists.each_with_index do |targets, list_index|
          target = targets[positions[list_index]]
          next if !target
          if !selected_target
            selected_list_index = list_index
            selected_target = target
            next
          end
          before = if by_bst
                     indexed_target_before?(target, selected_target)
                   else
                     target[:identity] < selected_target[:identity]
                   end
          if before
            selected_list_index = list_index
            selected_target = target
          end
        end
        break if !selected_target
        positions[selected_list_index] += 1
        next if selected_target[:identity] == last_identity
        result << selected_target
        last_identity = selected_target[:identity]
      end
      return result
    end

    def indexed_bst_range(bucket, type_signature, minimum, maximum)
      targets = targets_for_type_signature_and_bst(bucket, type_signature)
      first = targets.bsearch_index do |target|
        target_bst(target) >= minimum
      end
      return nil if !first
      after = if maximum
                targets.bsearch_index do |target|
                  target_bst(target) > maximum
                end
              else
                targets.length
              end
      after ||= targets.length
      return nil if first >= after
      return [targets, first, after]
    end

    def each_target_in_indexed_bst_range(bucket, type_signature, minimum, maximum)
      range = indexed_bst_range(bucket, type_signature, minimum, maximum)
      return if !range
      targets, first, after = range
      index = first
      while index < after
        yield targets[index]
        index += 1
      end
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

    def natural_reference_bst(context, branch, metadata)
      destination = metadata[:destination]
      body = branch[:side] == :body ? destination : context[:body]
      head = branch[:side] == :head ? destination : context[:head]
      return fusion_bst_for_components(body, head)
    end

    def target_bst(target)
      cached = @target_bst_cache[target[:identity]]
      return cached if cached
      @target_bst_cache[target[:identity]] = fusion_bst_for_components(
        target[:body], target[:head]
      )
      return @target_bst_cache[target[:identity]]
    end

    def component_stats_for(species)
      identity = species.id.to_s
      cached = @component_stats_by_identity[identity]
      return cached if cached
      @component_stats_by_identity[identity] =
        @base_stat_generator.stats_for(species)
      return @component_stats_by_identity[identity]
    end

    def fused_bst(body_stats, head_stats)
      return ((2 * head_stats[:HP].to_i) / 3) +
        (body_stats[:HP].to_i / 3) +
        ((2 * body_stats[:ATTACK].to_i) / 3) +
        (head_stats[:ATTACK].to_i / 3) +
        ((2 * body_stats[:DEFENSE].to_i) / 3) +
        (head_stats[:DEFENSE].to_i / 3) +
        ((2 * head_stats[:SPECIAL_ATTACK].to_i) / 3) +
        (body_stats[:SPECIAL_ATTACK].to_i / 3) +
        ((2 * head_stats[:SPECIAL_DEFENSE].to_i) / 3) +
        (body_stats[:SPECIAL_DEFENSE].to_i / 3) +
        ((2 * body_stats[:SPEED].to_i) / 3) +
        (head_stats[:SPEED].to_i / 3)
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
      source_family_ids = family_ids_for_components(
        species.body_pokemon, species.head_pokemon
      )
      if family_ids_overlap?(target[:family_ids], source_family_ids)
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
      if !types_overlap?(target[:types], required_types)
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
      source_family_ids = family_ids_for_components(
        species.body_pokemon, species.head_pokemon
      )
      component = branch[:component_side] == :body ?
        species.body_pokemon : species.head_pokemon
      candidates = hard_candidates_for(
        species.id.to_s, taxonomy_for(component)[:role], branch[:source_bst],
        source_family_ids, @catalog.required_target_types(component, branch)
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

    def family_ids_for_components(body, head)
      result = [
        @family_id_by_family[taxonomy_for(body)[:family]],
        @family_id_by_family[taxonomy_for(head)[:family]]
      ]
      return result.uniq
    end

    def family_ids_overlap?(left, right)
      left.each do |family_id|
        return true if right.include?(family_id)
      end
      return false
    end

    def types_overlap?(left, right)
      left.each do |type|
        return true if right.include?(type)
      end
      return false
    end

    def canonical_type_signature(types)
      return @empty_type_signature if types.empty?
      if types.length > 2
        signature = types.sort_by(&:to_s).freeze
        return signature
      end
      first = types[0]
      second = types[1]
      if second && second.to_s < first.to_s
        first, second = second, first
      end
      by_second_type = @type_signatures_by_first_type[first] ||= {}
      cached = by_second_type[second]
      return cached if cached
      signature = second ? [first, second].freeze : [first].freeze
      by_second_type[second] = signature
      return signature
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
