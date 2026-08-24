#===============================================================================
# Ironmon completed-run obtainability prototype
#===============================================================================

module Ironmon
  TRACKER_OBTAINABILITY_DIAGNOSTIC_CAPABILITIES = [
    "evolution.results", "fusion.material_pairs", "pokemon.all_active",
    "world.items", "world.wild_encounters"
  ].freeze

  class TrackerObtainabilityService
    MAX_PLANS_PER_SPECIES = 8
    MAX_REQUESTED_SPECIES = 2_000
    MAX_REQUESTED_EVOLUTION_EDGES = 4_000
    MAXIMUM_FUSION_MAPPING_BATCH = 8_192
    BACKGROUND_MILLISECONDS = 4.0
    FOREGROUND_MILLISECONDS = 250.0
    FOREGROUND_LEASE_SECONDS = 2.0
    TRACKER_MAPPING_LEASE_SECONDS = 5.0
    EXCLUDED_MAP_NAME = /\A(?:EVENT_TEMPLATES|QUEST_TEMPLATES|testing)\z|\Aquest_/i
    SCRIPTED_ACQUISITION_METHODS = [
      "pbAddPokemon", "pbAddPokemonSilent", "pbAddToParty",
      "pbAddToPartySilent", "pbGenerateEgg", "pbAddEgg", "pbGenEgg",
      "pbStartTrade", "npcTrade", "pbWildBattle", "pbDoubleWildBattle",
      "pbTripleWildBattle"
    ].freeze
    DEFERRED_ACQUISITION_METHODS = [
      "pbAddForeignPokemon", "pbWildBattleSpecific"
    ].freeze
    SCRIPTED_RESOURCE_METHODS = [
      "pbItemBall", "pbReceiveItem", "pbPokemonMart", "pbStoreItem"
    ].freeze
    ITEM_EVOLUTION_METHODS = [
      :HappinessHoldItem, :HoldItem, :HoldItemMale, :HoldItemFemale,
      :DayHoldItem, :NightHoldItem, :HoldItemHappiness,
      :Item, :ItemMale, :ItemFemale, :ItemDay, :ItemNight, :ItemHappiness
    ].freeze

    def initialize(recipe, deferred = false)
      @recipe = recipe
      @configuration = nil
      @plans = Hash.new { |hash, key| hash[key] = [] }
      @obtainable_count = 0
      @direct_caught_fusions = {}
      @candidate_species = {}
      @unresolved_sources = []
      @unresolved_resources = [
        "conditional authored gifts, specialized marts, and direct Bag writes"
      ]
      @resource_supply = Hash.new(0)
      @fusion_evolution_queue = []
      @fusion_evolution_work = nil
      @queued_fusion_evolutions = {}
      @normal_evolution_queue = []
      @normal_evolution_work = nil
      @queued_normal_evolutions = {}
      @possible_evolution_edges = {}
      @requested_evolution_edges = Hash.new do |hash, key|
        hash[key] = {}
      end
      @requested_evolution_edge_keys = {}
      @requested_evolution_queue = []
      @queued_requested_evolutions = {}
      @requested_target_species = {}
      @full_fusion_closure_requested = false
      @phase = :prepare_generators
      @pair_index = 0
      @pair_first = 0
      @pair_second = 0
      @pair_count = 0
      @material_pairs_prepared = false
      @material_ids = []
      @normal_generator = nil
      @fusion_generator = nil
      @fusion_mapper = nil
      @encounter_tables = nil
      @encounter_table_index = 0
      @map_paths = nil
      @map_path_index = 0
      @map_infos = nil
      @authored_pages = nil
      @authored_page_index = 0
      @authored_map_id = nil
      @authored_sources_complete = false
      @foreground_until = 0.0
      @fusion_evolution_warmed = false
      @player_fusion_pair_work = nil
      @precomputed_fusion_mapping_entries = []
      @tracker_mapping_until = 0.0
      @tracker_mapping_worker_disabled = false
      @tracker_mapping_batches_applied = 0
      @ruby_mapping_pairs_processed = 0
      @caught_fusion_entries = nil
      @caught_fusion_index = 0
      @work_fiber = nil
      @work_deadline = nil
      advance_until_ready if !deferred
    end

    def advance_for_milliseconds(milliseconds)
      duration = [milliseconds.to_f, 0.1].max / 1000.0
      @work_deadline = Ironmon.tracker_uptime_seconds + duration
      @work_fiber = nil if @work_fiber && !@work_fiber.alive?
      @work_fiber ||= Fiber.new do
        while @phase != :complete
          advance_work_unit
          cooperative_checkpoint
        end
      end
      @work_fiber.resume if @work_fiber.alive?
      return @phase == :complete
    rescue Exception
      @work_fiber = nil
      raise
    ensure
      @work_deadline = nil
    end

    def request_foreground
      @foreground_until = Ironmon.tracker_uptime_seconds +
        FOREGROUND_LEASE_SECONDS
    end

    def foreground_requested?
      return @phase != :complete &&
        Ironmon.tracker_uptime_seconds < @foreground_until
    end

    def foreground_deadline
      return @foreground_until
    end

    def complete?
      return @phase == :complete
    end

    def background_advance_allowed?
      return @phase != :authored_sources
    end

    def snapshot(species = nil, requested_species_ids = [], requested_edge_keys = [])
      request_target_species(species) if species
      request_evolution_edges(requested_edge_keys)
      result = {
        "phase" => @phase.to_s,
        "complete" => @phase == :complete,
        "background_complete" => complete? || !background_advance_allowed?,
        "processed_pairs" => @pair_index,
        "total_pairs" => @pair_count,
        "obtainable_count" => @obtainable_count,
        "unresolved_source_count" => @unresolved_sources.length,
        "unresolved_resource_count" => @unresolved_resources.length,
        "fusion_mapping_mode" => fusion_mapping_mode,
        "tracker_mapping_batches_applied" =>
          @tracker_mapping_batches_applied,
        "ruby_mapping_pairs_processed" => @ruby_mapping_pairs_processed,
        "obtainable_species_ids" => requested_species_ids.map do |value|
          identity = value.to_s.split(":", 2)[0].to_s
          key = identity.to_sym
          plans = @plans.key?(key) ? @plans[key] : nil
          next if identity.empty? || !plans || plans.empty?
          "#{identity}:0"
        end.compact.uniq,
        "obtainable_evolution_edge_keys" => requested_edge_keys.map do |value|
          key = value.to_s
          @possible_evolution_edges[key] ? key : nil
        end.compact.uniq,
        "fusion_mapping_work" => fusion_mapping_work_snapshot
      }
      result["target"] = target_snapshot(species) if species
      return result
    end

    def apply_fusion_mapping_batch(batch, foreground = false)
      return if !batch
      if !batch.is_a?(Hash) || batch["job_id"].to_s != fusion_mapping_job_id
        raise TrackerLookupError.new(
          "invalid_query", "The tracker material-mapping job is stale."
        )
      end
      offset = batch["offset"].to_i
      if offset < 0 || offset > @pair_index
        raise TrackerLookupError.new(
          "invalid_query", "The tracker material-mapping batch is out of order."
        )
      end
      packed_pairs = batch["packed_pairs"]
      if !packed_pairs.is_a?(Array) || packed_pairs.empty? ||
         packed_pairs.length % 4 != 0 ||
         packed_pairs.length > MAXIMUM_FUSION_MAPPING_BATCH * 4
        raise TrackerLookupError.new(
          "invalid_query", "The tracker material-mapping batch size is invalid."
        )
      end
      pair_count = packed_pairs.length / 4
      overlap = [@pair_index - offset, pair_count].min
      validate_fusion_mapping_entries(
        packed_pairs.first(overlap * 4), offset
      ) if
        overlap > 0
      packed_pairs = packed_pairs.drop(overlap * 4)
      return true if packed_pairs.empty?
      @work_fiber = nil
      @work_deadline = nil
      @player_fusion_pair_work = nil
      @precomputed_fusion_mapping_entries = packed_pairs.dup
      @tracker_mapping_batches_applied += 1
      @tracker_mapping_until = Ironmon.tracker_uptime_seconds +
        TRACKER_MAPPING_LEASE_SECONDS
      if foreground
        apply_next_fusion_mapping_entry until
          @precomputed_fusion_mapping_entries.empty?
      end
      return true
    end

    def disable_tracker_mapping_worker
      @tracker_mapping_worker_disabled = true
      @tracker_mapping_until = 0.0
    end

    def target_snapshot(species)
      identity = normalize_species_id(species)
      plan = best_plan(identity)
      if plan
        return {
          "status" => "obtainable",
          "reason" => plan[:reason],
          "path" => materialize_path(plan[:path]),
          "required_items" => stringify_counts(plan[:items])
        }
      end
      if @phase != :complete
        return {
          "status" => "calculating",
          "reason" => "Player-fusion combinations are still being checked.",
          "path" => [],
          "required_items" => {}
        }
      end
      if @candidate_species[identity]
        return {
          "status" => "unknown",
          "reason" => "An authored acquisition can produce this Pokemon, but its story conditions are not yet path-proven.",
          "path" => [@candidate_species[identity]],
          "required_items" => {}
        }
      end
      if !@unresolved_sources.empty? || !@unresolved_resources.empty?
        return {
          "status" => "unknown",
          "reason" => "No proven path was found, but some authored sources could not be resolved safely.",
          "path" => [],
          "required_items" => {}
        }
      end
      return {
        "status" => "unobtainable",
        "reason" => "No permanent acquisition, material, or evolution path can produce this Pokemon.",
        "path" => [],
        "required_items" => {}
      }
    end

    def passive_target_snapshot(species)
      identity = normalize_species_id(species)
      return target_snapshot(species) if best_plan(identity)
      return target_snapshot(species) if @full_fusion_closure_requested
      return {
        "status" => "calculating",
        "reason" => "This Pokemon has not been checked through its complete evolution chain yet.",
        "path" => [],
        "required_items" => {}
      }
    end

    private

    def fusion_mapping_mode
      return "ruby_fallback" if @tracker_mapping_worker_disabled ||
        @ruby_mapping_pairs_processed > 0
      return "tracker_worker" if @tracker_mapping_batches_applied > 0
      return "waiting_for_tracker" if @material_pairs_prepared &&
        @pair_index < @pair_count
      return "preparing"
    end

    def fusion_mapping_work_snapshot
      return nil if @tracker_mapping_worker_disabled
      return nil if !@material_pairs_prepared || @pair_index >= @pair_count
      return nil if !@precomputed_fusion_mapping_entries.empty?
      info = Ironmon.custom_fusion_pool_info
      @tracker_mapping_until = Ironmon.tracker_uptime_seconds +
        TRACKER_MAPPING_LEASE_SECONDS
      return {
        "job_id" => fusion_mapping_job_id,
        "seed" => @recipe["seed"],
        "generator_version" => @recipe["player_fusion_generator_version"],
        "base_stat_source_fingerprint" =>
          @recipe["base_stat_source_fingerprint"],
        "custom_fusion_pool_version" => info[:schema_version],
        "custom_fusion_pool_size" => info[:size],
        "custom_fusion_pool_fingerprint" => info[:fingerprint],
        "material_ids" => @material_ids.map do |identity|
          GameData::Species.get(identity).id_number
        end,
        "processed_pairs" => @pair_index,
        "total_pairs" => @pair_count
      }
    end

    def fusion_mapping_job_id
      return @fusion_mapping_job_id if @fusion_mapping_job_id
      material_numbers = @material_ids.map do |identity|
        GameData::Species.get(identity).id_number
      end
      fingerprint = Ironmon.fnv1a_64_fingerprint(material_numbers)
      @fusion_mapping_job_id = [
        @recipe["run_id"], @recipe["seed"],
        @recipe["player_fusion_generator_version"], fingerprint
      ].join(":")
      return @fusion_mapping_job_id
    end

    def apply_next_fusion_mapping_entry
      if @precomputed_fusion_mapping_entries.length < 4 ||
         @pair_index >= @pair_count
        raise TrackerLookupError.new(
          "invalid_query", "The tracker material mapping contains extra pairs."
        )
      end
      values = @precomputed_fusion_mapping_entries.shift(4)
      first_id = @material_ids[@pair_first]
      second_id = @material_ids[@pair_second]
      first_number = GameData::Species.get(first_id).id_number
      second_number = GameData::Species.get(second_id).id_number
      if values[0].to_i != first_number || values[1].to_i != second_number
        raise TrackerLookupError.new(
          "invalid_query", "The tracker material mapping does not match the expected pair."
        )
      end
      first_result = valid_tracker_fusion_result(values[2])
      second_result = valid_tracker_fusion_result(values[3])
      apply_precomputed_player_fusion_pair(
        first_id, second_id, first_result, second_result
      )
      finish_player_fusion_pair
    end

    def validate_fusion_mapping_entries(entries, offset)
      first_index, second_index = material_pair_indices(offset)
      entries.each_slice(4) do |values|
        first_id = @material_ids[first_index]
        second_id = @material_ids[second_index]
        first_number = GameData::Species.get(first_id).id_number
        second_number = GameData::Species.get(second_id).id_number
        if values[0].to_i != first_number || values[1].to_i != second_number
          raise TrackerLookupError.new(
            "invalid_query", "The tracker material mapping does not match the expected pair."
          )
        end
        valid_tracker_fusion_result(values[2])
        valid_tracker_fusion_result(values[3])
        second_index += 1
        if second_index >= @material_ids.length
          first_index += 1
          second_index = first_index
        end
      end
    end

    def material_pair_indices(offset)
      first_index = 0
      second_index = 0
      remaining = offset.to_i
      row_length = @material_ids.length
      while remaining >= row_length && row_length > 0
        remaining -= row_length
        first_index += 1
        row_length -= 1
      end
      second_index = first_index + remaining
      return first_index, second_index
    end

    def valid_tracker_fusion_result(value)
      number = value.to_i
      identity = Ironmon.fusion_species_identity(number)
      if !identity ||
         !Ironmon.custom_fusion_pool_service.include_number?(number)
        raise TrackerLookupError.new(
          "invalid_query", "The tracker returned a fusion outside the custom pool."
        )
      end
      return identity
    end

    def apply_precomputed_player_fusion_pair(first_id, second_id,
                                             first_result, second_result)
      first = GameData::Species.get(first_id)
      second = GameData::Species.get(second_id)
      first_plans = @plans[first_id]
      second_plans = @plans[second_id]
      first_plans.each do |first_plan|
        second_plans.each do |second_plan|
          combination = player_fusion_plan_combination(
            first_plan, second_plan
          )
          next if !combination
          add_player_fusion_combination(
            first_result, first, second, first_plan, second_plan,
            combination
          )
          if first_id != second_id
            add_player_fusion_combination(
              second_result, second, first, second_plan, first_plan,
              combination
            )
          end
        end
      end
    end

    def cooperative_checkpoint
      return if !@work_deadline
      return if Ironmon.tracker_uptime_seconds < @work_deadline
      Fiber.yield
    end

    def advance_until_ready
      advance_work_unit while ![:player_fusions, :complete].include?(@phase)
    end

    def advance_work_unit
      case @phase
      when :prepare_generators
        prepare_generators
      when :prepare_encounters
        prepare_encounter_tables
      when :encounter_sources
        if @encounter_table_index < @encounter_tables.length
          process_encounter_table(@encounter_tables[@encounter_table_index])
          @encounter_table_index += 1
        else
          @phase = :starter_sources
        end
      when :starter_sources
        seed_starter_sources
        prepare_authored_map_scan
        @phase = :resources
      when :authored_sources
        advance_authored_source_work
      when :resources
        build_resource_supply
        @phase = :normal_graph
      when :normal_graph
        complete = @normal_generator.advance_graph(1)
        @phase = :normal_evolutions if complete
      when :normal_evolutions
        close_normal_evolutions(1)
        @phase = :caught_fusion_transformations if
          !normal_evolution_pending?
      when :caught_fusion_transformations
        complete = apply_caught_fusion_transformations(1)
        @phase = :transformation_evolutions if complete
      when :transformation_evolutions
        close_normal_evolutions(1)
        prepare_material_pairs if !normal_evolution_pending?
      when :player_fusions, :fusion_evolutions
        advance_fusion_work
      when :requested_evolutions
        advance_requested_evolution_work
      end
    end

    def prepare_encounter_tables
      @encounter_tables = []
      mode = encounter_mode
      mode.each do |data|
        data.types.each do |encounter_type, entries|
          @encounter_tables << [mode.name, data.map, data.version,
                                encounter_type, entries]
        end
      end
      @phase = :encounter_sources
    end

    def prepare_generators
      checkpoint = proc { cooperative_checkpoint }
      @configuration ||= Configuration.from(@recipe["configuration"])
      @normal_generator ||= Ironmon.tracker_normal_evolution_generator(@recipe)
      @fusion_generator ||=
        Ironmon.tracker_obtainability_fusion_evolution_generator(
          @recipe, checkpoint
        )
      @fusion_mapper ||= Ironmon.tracker_obtainability_fusion_mapper(
        @recipe, checkpoint
      )
      @phase = :prepare_encounters
    end

    def advance_fusion_work
      if !@fusion_evolution_warmed
        @fusion_evolution_warmed = true
        return
      end
      if !@precomputed_fusion_mapping_entries.empty?
        apply_next_fusion_mapping_entry
        return
      end
      if @pair_index < @pair_count &&
         Ironmon.tracker_uptime_seconds < @tracker_mapping_until
        return
      end
      if @full_fusion_closure_requested && fusion_evolution_pending?
        @phase = :fusion_evolutions
        close_fusion_evolutions(1)
        return
      end
      if @pair_index < @pair_count
        @phase = :player_fusions
        process_player_fusion_pair
        return
      end
      request_full_fusion_closure_if_needed
      if fusion_evolution_pending?
        @phase = :fusion_evolutions
        close_fusion_evolutions(1)
        return
      end
      finish_evolution_work
    end

    def finish_evolution_work
      if !@requested_evolution_queue.empty?
        @phase = :requested_evolutions
      else
        @phase = @authored_sources_complete ? :complete : :authored_sources
      end
    end

    def request_target_species(species)
      identity = normalize_species_id(species)
      @requested_target_species[identity] = true
      request_full_fusion_closure_if_needed
    end

    def request_full_fusion_closure_if_needed
      return if @full_fusion_closure_requested
      return if !@material_pairs_prepared
      return if @pair_index < @pair_count
      unresolved = @requested_target_species.keys.any? do |identity|
        plans = @plans.key?(identity) ? @plans[identity] : nil
        !plans || plans.empty?
      end
      return if !unresolved
      @full_fusion_closure_requested = true
      @plans.each do |identity, plans|
        next if plans.empty?
        species = GameData::Species.try_get(identity)
        next if !species || !species.is_a?(GameData::FusedSpecies)
        queue_fusion_evolution(identity)
      end
      @phase = :fusion_evolutions if @phase == :complete
    end

    def request_evolution_edges(edge_keys)
      edge_keys.each do |value|
        key = value.to_s
        next if @requested_evolution_edge_keys[key]
        source_text, target_text = key.split(">", 2)
        next if !source_text || !target_text
        source = source_text.split(":", 2)[0].to_s
        target = target_text.split(":", 2)[0].to_s
        next if source.empty? || target.empty?
        source_id = requested_evolution_species_identity(source)
        target_id = requested_evolution_species_identity(target)
        next if !source_id || !target_id
        @requested_evolution_edge_keys[key] = true
        @requested_evolution_edges[source_id][target_id] = key
        queue_requested_evolution(source_id) if
          !@possible_evolution_edges[key]
      end
      if @phase == :complete && !@requested_evolution_queue.empty?
        @phase = :requested_evolutions
      end
    end

    def requested_evolution_species_identity(value)
      identity = value.to_s.to_sym
      fusion = Ironmon.fusion_species_identity(identity)
      return fusion if fusion && Ironmon.custom_fusion_species?(fusion)
      species = GameData::Species.try_get(identity)
      return species ? species.id : nil
    end

    def queue_requested_evolution(identity)
      return if !@requested_evolution_edges
      return if @queued_requested_evolutions[identity]
      return if @requested_evolution_edges[identity].empty?
      @queued_requested_evolutions[identity] = true
      @requested_evolution_queue << identity
    end

    def advance_requested_evolution_work
      source = @requested_evolution_queue.shift
      @queued_requested_evolutions.delete(source)
      process_requested_evolution_source(source) if source
      finish_evolution_work if @requested_evolution_queue.empty?
    end

    def process_requested_evolution_source(source)
      species = GameData::Species.get(source)
      branches = if species.is_a?(GameData::FusedSpecies)
                   @fusion_generator.branches_for(species)
                 else
                   @normal_generator.branches_for(species)
                 end
      by_target = {}
      branches.each { |branch| by_target[branch[:target_id]] = branch }
      plans = @plans[source].dup
      @requested_evolution_edges[source].each do |target, key|
        branch = by_target[target]
        next if !branch || plans.empty?
        possible = false
        plans.each do |plan|
          next_plans = branch_plans(plan, branch)
          possible = true if !next_plans.empty?
          next_plans.each { |next_plan| add_plan(target, next_plan) }
        end
        @possible_evolution_edges[key] = true if possible
      end
    end

    def process_player_fusion_pair
      if !@player_fusion_pair_work
        first_id = @material_ids[@pair_first]
        second_id = @material_ids[@pair_second]
        fusion_ids = @fusion_mapper.species_pair(first_id, second_id)
        orientations = [[
          fusion_ids[0],
          GameData::Species.get(first_id),
          GameData::Species.get(second_id),
          @plans[first_id], @plans[second_id]
        ]]
        if first_id != second_id
          orientations << [
            fusion_ids[1],
            GameData::Species.get(second_id),
            GameData::Species.get(first_id),
            @plans[second_id], @plans[first_id]
          ]
        end
        @player_fusion_pair_work = {
          :orientations => orientations,
          :first_plan_index => 0,
          :second_plan_index => 0
        }
      end
      work = @player_fusion_pair_work
      first = work[:orientations][0]
      first_plan = first[3][work[:first_plan_index]]
      second_plan = first[4][work[:second_plan_index]]
      combination = player_fusion_plan_combination(first_plan, second_plan)
      if combination
        work[:orientations].each do |fusion, body, head, body_plans, head_plans|
          body_plan = body_plans[work[:first_plan_index]]
          head_plan = head_plans[work[:second_plan_index]]
          if body_plans.equal?(first[4])
            body_plan = body_plans[work[:second_plan_index]]
            head_plan = head_plans[work[:first_plan_index]]
          end
          add_player_fusion_combination(
            fusion, body, head, body_plan, head_plan, combination
          )
        end
      end
      work[:second_plan_index] += 1
      if work[:second_plan_index] >= first[4].length
        work[:second_plan_index] = 0
        work[:first_plan_index] += 1
      end
      return if work[:first_plan_index] < first[3].length
      @player_fusion_pair_work = nil
      @ruby_mapping_pairs_processed += 1
      finish_player_fusion_pair
    rescue PlayerFusionMappingError
      @player_fusion_pair_work = nil
      @ruby_mapping_pairs_processed += 1
      finish_player_fusion_pair
    end

    def finish_player_fusion_pair
      @pair_index += 1
      @pair_second += 1
      if @pair_second >= @material_ids.length
        @pair_first += 1
        @pair_second = @pair_first
      end
    end

    def process_encounter_table(table)
      mode_name, map_id, version, encounter_type, entries = table
      generator = species_generator
      mapped = entries.each_with_index.map do |entry, slot|
        context = [:table, mode_name, map_id, version, encounter_type, slot]
        generator.map(entry[1], context)
      end
      mapped.each_with_index do |identity, slot|
        add_direct_source(
          identity, "Wild encounter",
          "Wild encounter on #{pbGetMapNameFromId(map_id)} " +
          "(#{encounter_type}, slot #{slot + 1})",
          nil, true
        )
      end
      return if @configuration.wild_policy !=
        Configuration::POLICY_NORMAL_ONLY
      mapped.each do |body|
        mapped.each do |head|
          begin
            fusion = @fusion_mapper.species(body, head)
            add_direct_source(
              fusion, "Wild encounter fusion",
              "A fused encounter assembled from two #{encounter_type} rows",
              nil, true
            )
          rescue PlayerFusionMappingError
          end
        end
      end
    end

    def seed_starter_sources
      generator = species_generator
      [1, 4, 7].each_with_index do |source, slot|
        identity = generator.map(source, [:starter, slot])
        constraint = { :starter => slot }
        add_direct_source(
          identity, "Starter choice", "Starter choice #{slot + 1}",
          constraint, false
        )
      end
    end

    def prepare_authored_map_scan
      @map_infos = load_data("Data/MapInfos.rxdata")
      @map_paths = Dir.glob(
        File.join("Data", "Map[0-9][0-9][0-9].rxdata")
      ).sort
      @map_path_index = 0
    rescue Exception => e
      @unresolved_sources << "catalog:#{e.class}:#{e.message}"
      @map_infos = {}
      @map_paths = []
    end

    def advance_authored_source_work
      if @authored_pages && @authored_page_index < @authored_pages.length
        page, event_id = @authored_pages[@authored_page_index]
        scan_command_list(page.list, @authored_map_id, event_id, {})
        @authored_page_index += 1
        return
      end
      @authored_pages = nil
      while @map_path_index < @map_paths.length
        path = @map_paths[@map_path_index]
        @map_path_index += 1
        map_id = File.basename(path)[/\d+/].to_i
        begin
          info = @map_infos[map_id]
          next if !info || info.name.to_s.match?(EXCLUDED_MAP_NAME)
          map = load_data(path)
          pages = []
          map.events.each_value do |event|
            event.pages.each { |page| pages << [page, event.id] }
          end
          next if pages.empty?
          @authored_pages = pages
          @authored_page_index = 0
          @authored_map_id = map_id
          return
        rescue Exception => e
          @unresolved_sources << "map:#{map_id}:#{e.class}:#{e.message}"
        end
      end
      @authored_sources_complete = true
      finish_evolution_work
    end

    def scan_command_list(commands, map_id, event_id, common_stack)
      script_chunks(commands).each do |index, script|
        scripted_calls(script).each do |method_name, arguments|
          add_scripted_call(method_name, arguments, map_id, event_id, index)
        end
      end
      commands.each do |command|
        next if command.code != 117
        common_id = command.parameters[0].to_i
        next if common_id < 1 || common_stack[common_id]
        @common_events ||= load_data("Data/CommonEvents.rxdata")
        common = @common_events[common_id]
        next if !common
        nested_stack = common_stack.dup
        nested_stack[common_id] = true
        scan_command_list(common.list, map_id, event_id, nested_stack)
      end
    end

    def script_chunks(commands)
      chunks = []
      index = 0
      while index < commands.length
        command = commands[index]
        if command.code == 355
          start_index = index
          parts = [command.parameters[0].to_s]
          while index + 1 < commands.length && commands[index + 1].code == 655
            index += 1
            parts << commands[index].parameters[0].to_s
          end
          chunks << [start_index, parts.join("\n")]
        end
        index += 1
      end
      return chunks
    end

    def scripted_calls(script)
      calls = []
      SCRIPTED_ACQUISITION_METHODS.each do |method_name|
        offset = 0
        loop do
          match = script.match(/\b#{Regexp.escape(method_name)}\s*\(/, offset)
          break if !match
          opening = match.end(0) - 1
          closing = matching_parenthesis(script, opening)
          break if !closing
          calls << [method_name, split_arguments(script[(opening + 1)...closing])]
          offset = closing + 1
        end
      end
      return calls
    end

    def matching_parenthesis(text, opening)
      depth = 0
      quote = nil
      escaped = false
      (opening...text.length).each do |index|
        character = text[index]
        if quote
          if escaped
            escaped = false
          elsif character == "\\"
            escaped = true
          elsif character == quote
            quote = nil
          end
          next
        end
        if character == "\"" || character == "'"
          quote = character
        elsif character == "("
          depth += 1
        elsif character == ")"
          depth -= 1
          return index if depth == 0
        end
      end
      return nil
    end

    def split_arguments(text)
      result = []
      start = 0
      depth = 0
      quote = nil
      escaped = false
      text.length.times do |index|
        character = text[index]
        if quote
          if escaped
            escaped = false
          elsif character == "\\"
            escaped = true
          elsif character == quote
            quote = nil
          end
          next
        end
        if character == "\"" || character == "'"
          quote = character
        elsif ["(", "[", "{"].include?(character)
          depth += 1
        elsif [")", "]", "}"].include?(character)
          depth -= 1
        elsif character == "," && depth == 0
          result << text[start...index].strip
          start = index + 1
        end
      end
      result << text[start..-1].to_s.strip
      return result
    end

    def add_scripted_call(method_name, arguments, map_id, event_id, index)
      positions = scripted_species_positions(method_name)
      positions.each_with_index do |position, subslot|
        expression = arguments[position]
        identity = literal_species(expression)
        if !identity
          next if expression.to_s == "starter" ||
            expression.to_s.include?("VAR_PLAYER_STARTER_CHOICE")
          @unresolved_sources <<
            "map:#{map_id}|event:#{event_id}|index:#{index}|#{method_name}:#{expression}"
          next
        end
        identity = randomized_scripted_species(
          identity, method_name, arguments, map_id, event_id, index, subslot
        )
        label = scripted_source_label(method_name)
        detail = "#{label} on #{pbGetMapNameFromId(map_id)} (event #{event_id})"
        species = GameData::Species.try_get(identity)
        @candidate_species[species.id] = detail if species &&
          Ironmon.tracker_lookup_species_available?(species)
      end
    end

    def scripted_species_positions(method_name)
      return [0, 2, 4] if method_name == "pbTripleWildBattle"
      return [0, 2] if method_name == "pbDoubleWildBattle"
      return [1] if method_name == "pbStartTrade"
      return [0]
    end

    def literal_species(expression)
      value = expression.to_s.strip
      match = value.match(/\A:([A-Za-z0-9_]+)\z/)
      return normalized_literal_species(match[1]) if match
      match = value.match(/\A(?:fusionOf|getFusionSpecies)\(\s*:([A-Za-z0-9_]+)\s*,\s*:([A-Za-z0-9_]+)\s*\)\z/)
      if match
        first = normalized_literal_species(match[1])
        second = normalized_literal_species(match[2])
        return nil if !first || !second
        return GameData::Species.get(
          getFusedPokemonIdFromSymbols(first, second)
        ).id
      end
      match = value.match(/\A(?:GameData::Species\.get\()?\s*(\d+)\s*\)?\z/)
      return normalized_literal_species(match[1].to_i) if match
      return nil
    rescue Exception
      return nil
    end

    def normalized_literal_species(value)
      species = GameData::Species.try_get(
        value.is_a?(String) ? value.upcase.to_sym : value
      )
      return species ? species.id : nil
    end

    def randomized_scripted_species(identity, method_name, arguments, map_id,
                                    event_id, index, subslot)
      if method_name.include?("WildBattle")
        purpose = method_name == "pbDoubleWildBattle" ? :double :
          (method_name == "pbTripleWildBattle" ? :triple : :single)
        context = [:script, map_id, event_id, index, purpose, subslot]
        return species_generator.map(identity, context)
      end
      if ["pbAddPokemon", "pbAddToParty"].include?(method_name)
        dont_randomize = arguments[3].to_s == "true"
        return identity if dont_randomize
        context = [:script, map_id, event_id, index, :gift, 0]
        return species_generator.map(identity, context)
      end
      return identity
    end

    def scripted_source_label(method_name)
      return "Scripted wild encounter" if method_name.include?("WildBattle")
      return "Egg hatch" if ["pbGenerateEgg", "pbAddEgg", "pbGenEgg"].include?(method_name)
      return "NPC trade" if ["pbStartTrade", "npcTrade"].include?(method_name)
      return "Gift or static Pokemon"
    end

    def add_direct_source(identity, reason, detail, constraints, caught)
      species = GameData::Species.try_get(identity)
      return if !species || !Ironmon.tracker_lookup_species_available?(species)
      plan = {
        :items => {},
        :constraints => constraints || {},
        :source_uses => constraints && constraints[:starter] ?
          { "starter:#{constraints[:starter]}" => 1 } : {},
        :reason => reason,
        :path => path_step(nil, nil, detail),
        :path_length => 1
      }
      added = add_plan(species.id, plan)
      if added && caught && species.is_a?(GameData::FusedSpecies)
        @direct_caught_fusions[species.id] = plan
      end
    end

    def build_resource_supply
      Ironmon.tracker_area_catalog.each do |area|
        area["items"].each do |entry|
          Ironmon.tracker_area_resolved_item_ids(entry, @recipe).each do |item_id|
            @resource_supply[item_id.to_sym] += 1
          end
        end
      end
      @resource_supply[:POKEBALL] += 1
    end

    def close_normal_evolutions(limit = nil)
      processed = 0
      while normal_evolution_pending? && (!limit || processed < limit)
        begin_normal_evolution_work if !@normal_evolution_work
        next if !@normal_evolution_work
        work = @normal_evolution_work
        branch = work[:branches][work[:branch_index]]
        plan = work[:plans][work[:plan_index]]
        next_plans = branch_plans(plan, branch)
        mark_possible_evolution(work[:source], branch) if !next_plans.empty?
        next_plans.each do |next_plan|
          add_plan(branch[:target_id], next_plan)
        end
        advance_normal_evolution_work
        processed += 1
      end
    end

    def normal_evolution_pending?
      @normal_evolution_queue ||= []
      return !!@normal_evolution_work || !@normal_evolution_queue.empty?
    end

    def begin_normal_evolution_work
      source = @normal_evolution_queue.shift
      @queued_normal_evolutions.delete(source)
      species = GameData::Species.get(source)
      branches = @normal_generator.branches_for(species)
      plans = @plans[source].dup
      if branches.empty? || plans.empty?
        @normal_evolution_work = nil
        return begin_normal_evolution_work if !@normal_evolution_queue.empty?
        return
      end
      @normal_evolution_work = {
        :source => source,
        :branches => branches,
        :plans => plans,
        :branch_index => 0,
        :plan_index => 0
      }
    end

    def advance_normal_evolution_work
      work = @normal_evolution_work
      work[:plan_index] += 1
      if work[:plan_index] >= work[:plans].length
        work[:plan_index] = 0
        work[:branch_index] += 1
      end
      @normal_evolution_work = nil if
        work[:branch_index] >= work[:branches].length
    end

    def close_fusion_evolutions(limit)
      processed = 0
      while fusion_evolution_pending? && (!limit || processed < limit)
        begin_fusion_evolution_work if !@fusion_evolution_work
        next if !@fusion_evolution_work
        work = @fusion_evolution_work
        branch = work[:branches][work[:branch_index]]
        plan = work[:plans][work[:plan_index]]
        next_plans = branch_plans(plan, branch)
        mark_possible_evolution(work[:source], branch) if !next_plans.empty?
        next_plans.each do |next_plan|
          add_plan(branch[:target_id], next_plan)
        end
        advance_fusion_evolution_work
        processed += 1
      end
    end

    def fusion_evolution_pending?
      return !!@fusion_evolution_work || !@fusion_evolution_queue.empty?
    end

    def begin_fusion_evolution_work
      source = @fusion_evolution_queue.shift
      @queued_fusion_evolutions.delete(source)
      species = GameData::Species.get(source)
      branches = @fusion_generator.branches_for(species)
      plans = @plans[source].dup
      if branches.empty? || plans.empty?
        @fusion_evolution_work = nil
        return begin_fusion_evolution_work if !@fusion_evolution_queue.empty?
        return
      end
      @fusion_evolution_work = {
        :source => source,
        :branches => branches,
        :plans => plans,
        :branch_index => 0,
        :plan_index => 0
      }
    end

    def advance_fusion_evolution_work
      work = @fusion_evolution_work
      work[:plan_index] += 1
      if work[:plan_index] >= work[:plans].length
        work[:plan_index] = 0
        work[:branch_index] += 1
      end
      @fusion_evolution_work = nil if
        work[:branch_index] >= work[:branches].length
    end

    def branch_plans(plan, branch)
      results = []
      branch[:effective_methods].each do |method|
        next_plan = clone_plan(plan)
        required_item = evolution_method_item(method)
        if required_item
          next_plan[:items][required_item] ||= 0
          next_plan[:items][required_item] += 1
          next if next_plan[:items][required_item] >
            @resource_supply[required_item]
        end
        target = GameData::Species.get(branch[:target_id])
        next_plan[:reason] = "Evolution"
        requirement = Ironmon.tracker_evolution_snapshot(
          method[:method], method[:parameter]
        )["requirement"]
        next_plan[:path] = path_step(
          plan[:path], nil, "Evolve into #{target.name}: #{requirement}"
        )
        next_plan[:path_length] = plan[:path_length].to_i + 1
        results << next_plan
      end
      return results
    end

    def evolution_method_item(method)
      return :POKEBALL if method[:method] == :Shedinja
      return nil if !ITEM_EVOLUTION_METHODS.include?(method[:method])
      item = GameData::Item.try_get(method[:parameter])
      return item ? item.id : nil
    end

    def mark_possible_evolution(source, branch)
      target = GameData::Species.get(branch[:target_id]).id
      @possible_evolution_edges["#{source}:0>#{target}:0"] = true
    end

    def apply_caught_fusion_transformations(limit = nil)
      @caught_fusion_entries ||= @direct_caught_fusions.to_a
      processed = 0
      while @caught_fusion_index < @caught_fusion_entries.length &&
            (!limit || processed < limit)
        identity, plan = @caught_fusion_entries[@caught_fusion_index]
        apply_caught_fusion_transformation(identity, plan)
        @caught_fusion_index += 1
        processed += 1
      end
      return @caught_fusion_index >= @caught_fusion_entries.length
    end

    def apply_caught_fusion_transformation(identity, plan)
      species = GameData::Species.get(identity)
      reverse = GameData::Species.get(@fusion_mapper.paired_species(species.id))
      reverse_plan = clone_plan(plan)
      reverse_plan[:reason] = "Caught-fusion reversal"
      reverse_plan[:path] = path_step(
        plan[:path], nil, "Reverse the caught fusion into #{reverse.name}"
      )
      reverse_plan[:path_length] = plan[:path_length].to_i + 1
      add_plan(reverse.id, reverse_plan)
      components = [species.body_pokemon, species.head_pokemon]
      if @configuration.unfusion_setting !=
         Configuration::UNFUSION_PLAYER_CHOICE
        @unresolved_sources << "caught-fusion random component acquisition order" if
          !@unresolved_sources.include?(
            "caught-fusion random component acquisition order"
          )
        components.each do |component|
          @candidate_species[component.id] ||=
            "Random-component unfusion of a caught #{species.name}"
        end
        return
      end
      components.each do |component|
        component_plan = clone_plan(plan)
        component_plan[:reason] = "Caught-fusion unfusion"
        component_plan[:path] = path_step(
          plan[:path], nil,
          "Unfuse the caught fusion and keep #{component.name}"
        )
        component_plan[:path_length] = plan[:path_length].to_i + 1
        add_plan(component.id, component_plan)
      end
    end

    def prepare_material_pairs
      @material_ids = @plans.keys.select do |identity|
        species = GameData::Species.try_get(identity)
        species && !species.is_a?(GameData::FusedSpecies) &&
          !@plans[identity].empty?
      end.sort_by { |identity| GameData::Species.get(identity).id_number }
      @pair_count = (@material_ids.length * (@material_ids.length + 1)) / 2
      @material_pairs_prepared = true
      @phase = @pair_count > 0 ? :player_fusions : :complete
    end

    def player_fusion_plan_combination(body_plan, head_plan)
      return if !compatible_constraints?(
        body_plan[:constraints], head_plan[:constraints]
      )
      items = merge_counts(body_plan[:items], head_plan[:items])
      return if !within_supply?(items)
      source_uses = merge_counts(
        body_plan[:source_uses], head_plan[:source_uses]
      )
      return if source_uses.any? { |_source, count| count > 1 }
      return {
        :items => items,
        :constraints => body_plan[:constraints].merge(
          head_plan[:constraints]
        ),
        :source_uses => source_uses,
        :path_length => body_plan[:path_length].to_i +
          head_plan[:path_length].to_i + 1
      }
    end

    def add_player_fusion_combination(fusion, body, head, body_plan, head_plan,
                                      combination)
      return add_plan(fusion, {
        :items => combination[:items],
        :constraints => combination[:constraints],
        :source_uses => combination[:source_uses],
        :reason => "Player fusion",
        :path => fusion_path_step(
          body_plan[:path], head_plan[:path],
          body.id, head.id
        ),
        :path_length => combination[:path_length]
      })
    end

    def add_plan(identity, plan)
      fusion_identity = Ironmon.fusion_species_identity(identity)
      species = nil
      if fusion_identity
        key = fusion_identity
        fused = true
      else
        species = identity.respond_to?(:id) ? identity :
          GameData::Species.try_get(identity)
        return false if !species
        key = species.id
        fused = species.is_a?(GameData::FusedSpecies)
      end
      plans = @plans[key]
      newly_obtainable = plans.empty?
      return false if plans.any? { |existing| plan_dominates?(existing, plan) }
      plans.delete_if { |existing| plan_dominates?(plan, existing) }
      plans << plan
      if plans.length > 1
        plans.sort_by! do |entry|
          [entry[:items].values.inject(0, :+), entry[:constraints].length,
           entry[:path_length].to_i]
        end
      end
      added = true
      if plans.length > MAX_PLANS_PER_SPECIES
        plans.slice!(MAX_PLANS_PER_SPECIES, plans.length)
        added = plans.include?(plan)
      end
      @obtainable_count = @obtainable_count.to_i + 1 if
        added && newly_obtainable
      if added && fused &&
         @full_fusion_closure_requested
        queue_fusion_evolution(key)
      elsif added && !fused
        @queued_normal_evolutions ||= {}
        @normal_evolution_queue ||= []
        if !@queued_normal_evolutions[key]
          @queued_normal_evolutions[key] = true
          @normal_evolution_queue << key
        end
      end
      queue_requested_evolution(key) if added
      return added
    end

    def queue_fusion_evolution(key)
      return if @queued_fusion_evolutions[key]
      @queued_fusion_evolutions[key] = true
      @fusion_evolution_queue << key
    end

    def plan_dominates?(first, second)
      return false if first[:constraints] != second[:constraints]
      return false if first[:items].any? do |item_id, count|
        count.to_i > second[:items][item_id].to_i
      end
      return false if first[:source_uses].any? do |source_id, count|
        count.to_i > second[:source_uses][source_id].to_i
      end
      return true
    end

    def compatible_constraints?(first, second)
      return !first.any? do |key, value|
        second.key?(key) && second[key] != value
      end
    end

    def merge_counts(first, second)
      result = first.dup
      second.each do |item_id, count|
        result[item_id] = result[item_id].to_i + count.to_i
      end
      return result
    end

    def within_supply?(items)
      return items.all? do |item_id, count|
        count.to_i <= @resource_supply[item_id]
      end
    end

    def clone_plan(plan)
      return {
        :items => plan[:items].dup,
        :constraints => plan[:constraints].dup,
        :source_uses => plan[:source_uses].dup,
        :reason => plan[:reason],
        :path => plan[:path],
        :path_length => plan[:path_length]
      }
    end

    def best_plan(identity)
      return nil if !@plans.key?(identity)
      plans = @plans[identity]
      return nil if !plans || plans.empty?
      return plans.min_by do |plan|
        [plan[:items].values.inject(0, :+), plan[:path_length].to_i]
      end
    end

    def path_step(first, second, text)
      return [first, second, text].freeze
    end

    def fusion_path_step(first, second, body_id, head_id)
      detail = [:fusion, body_id, head_id].freeze
      return [first, second, detail].freeze
    end

    def materialize_path(path)
      result = []
      seen_steps = {}
      pending = [[path, false]]
      until pending.empty?
        current, exiting = pending.pop
        next if !current
        if !exiting
          pending << [current, true]
          pending << [current[1], false] if current[1]
          pending << [current[0], false] if current[0]
          next
        end
        detail = current[2]
        step = if detail.is_a?(Array) && detail[0] == :fusion
                 body = GameData::Species.get(detail[1])
                 head = GameData::Species.get(detail[2])
                 "Fuse #{body.name} as Body with #{head.name} as Head"
               else
                 detail.to_s
               end
        next if step.empty? || seen_steps[step]
        seen_steps[step] = true
        result << step
      end
      return result
    end

    def species_generator
      return @species_generator if @species_generator
      @species_generator = SpeciesGenerator.new(
        @recipe["seed"], :wild,
        @configuration.wild_policy,
        Ironmon.normal_species_pool,
        Ironmon.custom_fusion_pool, {},
        @recipe["species_generator_version"]
      )
      return @species_generator
    end

    def encounter_mode
      if @recipe["data_mode"] == "remix" &&
         defined?(GameData::EncounterModern)
        return GameData::EncounterModern
      end
      return GameData::Encounter
    end

    def normalize_species_id(species)
      data = species.respond_to?(:id) ? species : GameData::Species.get(species)
      return data.id
    end

    def stringify_counts(counts)
      result = {}
      counts.each { |item_id, count| result[item_id.to_s] = count }
      return result
    end
  end

  def self.tracker_obtainability_services
    @tracker_obtainability_services ||= {}
  end

  def self.tracker_obtainability_service(recipe)
    key = recipe["run_id"]
    service = tracker_obtainability_services[key]
    return service if service
    service = TrackerObtainabilityService.new(recipe, true)
    tracker_store_bounded(tracker_obtainability_services, key, service, 4)
    return service
  end

  def self.tracker_obtainability(payload, envelope_run_id)
    payload ||= {}
    recipe = tracker_validate_completed_recipe(
      payload["recipe"], envelope_run_id
    )
    return tracker_obtainability_for_recipe(payload, recipe)
  end

  def self.tracker_obtainability_for_recipe(payload, recipe)
    payload ||= {}
    species_key = payload["species_id"].to_s.split(":", 2)[0].to_s
    species = species_key.empty? ? nil :
      GameData::Species.try_get(species_key.to_sym)
    if species_key != "" && (!species || !tracker_lookup_species_available?(species))
      raise TrackerLookupError.new(
        "pokemon_not_found", "The selected Pokemon is not available in this run."
      )
    end
    requested_species_ids = payload["species_ids"]
    requested_species_ids = [] if !requested_species_ids.is_a?(Array)
    if requested_species_ids.length >
       TrackerObtainabilityService::MAX_REQUESTED_SPECIES
      raise TrackerLookupError.new(
        "invalid_query", "Too many Pokemon were requested for obtainability."
      )
    end
    requested_edge_keys = payload["evolution_edge_keys"]
    requested_edge_keys = [] if !requested_edge_keys.is_a?(Array)
    if requested_edge_keys.length >
       TrackerObtainabilityService::MAX_REQUESTED_EVOLUTION_EDGES
      raise TrackerLookupError.new(
        "invalid_query", "Too many evolution connections were requested for obtainability."
      )
    end
    service = tracker_obtainability_service(recipe)
    service.disable_tracker_mapping_worker if
      payload["fusion_mapping_worker_unavailable"] == true
    service.apply_fusion_mapping_batch(
      payload["fusion_mapping_batch"], payload["foreground"] == true
    )
    service.request_foreground if payload["foreground"] == true
    prefix = "#{recipe["run_id"]}|"
    sections = ["overview", "abilities", "stats", "moves", "evolutions"]
    tracker_lookup_cache.keys.each do |key|
      parts = key.to_s.split("|", 4)
      tracker_lookup_cache.delete(key) if key.to_s.start_with?(prefix) &&
        sections.include?(parts[2])
    end
    return service.snapshot(species, requested_species_ids, requested_edge_keys)
  end

  def self.tracker_obtainability_snapshot(species, recipe)
    service = tracker_obtainability_services[recipe["run_id"]]
    return service.passive_target_snapshot(species) if service
    return {
      "status" => "calculating",
      "reason" => "Run obtainability has not been checked yet.",
      "path" => [],
      "required_items" => {}
    }
  end

  def self.update_tracker_obtainability
    return if !tracker_obtainability_runtime_ready?
    foreground = tracker_obtainability_services.values.select do |service|
      service.foreground_requested?
    end.max_by { |service| service.foreground_deadline }
    if foreground
      foreground.advance_for_milliseconds(
        TrackerObtainabilityService::FOREGROUND_MILLISECONDS
      )
      return
    end
    return if !tracker_obtainability_background_safe?
    recipe = tracker_debug_active_recipe
    service = tracker_obtainability_service(recipe)
    return if service.complete?
    return if !service.background_advance_allowed?
    service.advance_for_milliseconds(
      TrackerObtainabilityService::BACKGROUND_MILLISECONDS
    )
  rescue Exception => e
    echoln "Ironmon obtainability background update failed: #{e.message}"
  end

  def self.mark_tracker_obtainability_map_ready(scene)
    @tracker_obtainability_ready_scene = scene
  end

  def self.pause_tracker_obtainability_for_map
    @tracker_obtainability_ready_scene = nil
  end

  def self.tracker_obtainability_runtime_ready?
    return false if !active? || !$PokemonGlobal || tracker_battle_id
    return false if !tracker_connection.connected?
    return false if instance_variable_get(:@reset_in_progress)
    return false if instance_variable_get(:@tracker_seed_import_pending)
    return false if !defined?($scene) || !defined?(Scene_Map) ||
      !$scene.is_a?(Scene_Map)
    return @tracker_obtainability_ready_scene.equal?($scene)
  end

  def self.tracker_obtainability_background_safe?
    return false if !tracker_obtainability_runtime_ready?
    return false if defined?(pbMapInterpreterRunning?) &&
      pbMapInterpreterRunning?
    return true
  end
end
