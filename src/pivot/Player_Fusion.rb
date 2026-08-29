#===============================================================================
# Deterministic player-fusion gamble mapping and discovery tracking
#===============================================================================

module Ironmon
  PLAYER_FUSION_PREPARATION_BUDGET_SECONDS = 0.004
  PLAYER_FUSION_UPTIME_UNITS_PER_SECOND = 1_000_000.0
  PLAYER_FUSION_TRACE_FILENAME = "player_fusion_preparation_trace.log"

  class PlayerFusionMappingError < StandardError; end

  class PlayerFusionMapper
    SCHEMA_VERSION = 5
    PREVIOUS_SCHEMA_VERSION = 3
    LEGACY_SCHEMA_VERSION = 2
    SUPPORTED_SCHEMA_VERSIONS = [LEGACY_SCHEMA_VERSION,
                                 PREVIOUS_SCHEMA_VERSION,
                                 SCHEMA_VERSION].freeze
    PREFERRED_MINIMUM_PERCENT = 90
    PREFERRED_MAXIMUM_PERCENT = 115
    NAMESPACE = "player_fusion"
    MATERIAL_ID_BITS = 10
    MATERIAL_ID_MASK = (1 << MATERIAL_ID_BITS) - 1
    RESULT_INDEX_BST_BUCKET_SIZE = 32

    attr_reader :preparation_stage
    attr_reader :preparation_progress

    def initialize(seed, fusion_pool, mappings, discoveries,
                   base_stat_generator = nil,
                   schema_version = SCHEMA_VERSION, work_checkpoint = nil,
                   use_result_index = false)
      @seed = seed.to_i
      @fusion_pool = fusion_pool
      @mappings = mappings
      @discoveries = discoveries
      @base_stat_generator = base_stat_generator || Ironmon.base_stat_generator
      @schema_version = schema_version.to_i
      @work_checkpoint = work_checkpoint
      @use_result_index = use_result_index
      if !SUPPORTED_SCHEMA_VERSIONS.include?(@schema_version)
        raise PlayerFusionMappingError,
              "player fusion schema #{@schema_version} is unsupported"
      end
      @fusion_pool_ids = nil
      @fusion_pool_membership = nil
      @fusion_pairs = nil
      @paired_result_ids = nil
      @target_bsts = []
      @target_type_masks = []
      @result_hash_prefix = nil
      @material_pair_codes = nil
      @schema_5_material_pair_codes = nil
      @material_pairs = {}
      @type_codes = {}
      @normal_stats = {}
      @result_candidate_index = nil
      @result_bst_index = nil
      @result_bst_minimum = nil
      @result_bst_maximum = nil
      @preparation_stage = :initialized
      @preparation_progress = 0
    end

    def species(body_species, head_species)
      return GameData::Species.get(
        species_number(body_species, head_species)
      ).id
    end

    def species_pair(first_species, second_species)
      first_id, second_id = normal_input_ids(first_species, second_species)
      pair = [first_id, second_id].sort
      result_ids = mapped_result_ids(pair)
      first_result, second_result = if first_id <= second_id
                                      result_ids
                                    else
                                      [result_ids[1], result_ids[0]]
                                    end
      return [
        GameData::Species.get(validate_result_id(first_result)).id,
        GameData::Species.get(validate_result_id(second_result)).id
      ]
    end

    def species_number(body_species, head_species)
      body_id, head_id = normal_input_ids(body_species, head_species)
      pair = [body_id, head_id].sort
      result_ids = mapped_result_ids(pair)
      oriented_id = body_id <= head_id ? result_ids[0] : result_ids[1]
      validate_result_id(oriented_id)
      return oriented_id
    end

    def known_species(body_species, head_species)
      body_id, head_id = normal_input_ids(body_species, head_species)
      pair = [body_id, head_id].sort
      return nil if !@discoveries[pair_key(pair)]
      result_ids = mapped_result_ids(pair)
      oriented_id = body_id <= head_id ? result_ids[0] : result_ids[1]
      validate_result_id(oriented_id)
      return GameData::Species.get(oriented_id).id
    end

    def discover(body_species, head_species, result_species)
      body_id, head_id = normal_input_ids(body_species, head_species)
      pair = [body_id, head_id].sort
      result_ids = mapped_result_ids(pair)
      expected_id = body_id <= head_id ? result_ids[0] : result_ids[1]
      actual_id = GameData::Species.get(result_species).id_number
      if actual_id != expected_id
        raise PlayerFusionMappingError,
              "the committed fusion does not match its mapped result"
      end
      @discoveries[pair_key(pair)] = result_ids.dup
      return true
    end

    def pair_key_for(body_species, head_species)
      return pair_key(normal_input_ids(body_species, head_species).sort)
    end

    def paired_species(fusion_species)
      ensure_fusion_pool
      species_id = validate_result_id(
        GameData::Species.get(fusion_species).id_number
      )
      paired_id = paired_result_id(species_id)
      return GameData::Species.get(paired_id).id
    end

    def material_pairs_for(fusion_species)
      ensure_fusion_pool
      species_id = validate_result_id(
        GameData::Species.get(fusion_species).id_number
      )
      return @material_pairs[species_id] if @material_pairs[species_id]
      if @schema_version == SCHEMA_VERSION
        ensure_schema_5_material_pair_codes
        codes = @schema_5_material_pair_codes.fetch(species_id, [])
        pairs = codes.map do |code|
          [code >> MATERIAL_ID_BITS, code & MATERIAL_ID_MASK]
        end.freeze
        @material_pairs[species_id] = pairs
        return @material_pairs[species_id]
      end
      if @schema_version == PREVIOUS_SCHEMA_VERSION
        pairs = schema_3_material_pairs_for(species_id).freeze
        @material_pairs[species_id] = pairs
        return @material_pairs[species_id]
      end
      ensure_material_pair_codes
      codes = @material_pair_codes.fetch(species_id, [])
      pairs = codes.map do |code|
        [code >> MATERIAL_ID_BITS, code & MATERIAL_ID_MASK]
      end.freeze
      @material_pairs[species_id] = pairs
      return @material_pairs[species_id]
    end

    def material_pair_assignments_required?
      return @schema_version == SCHEMA_VERSION &&
        !@schema_5_material_pair_codes
    end

    def prepare
      @preparation_stage = :fusion_pool
      ensure_fusion_pool
      @preparation_stage = :prepared
      return true
    end

    def finish_cooperative_preparation
      @work_checkpoint = nil
      @preparation_stage = :complete
      return self
    end

    private

    def ensure_material_pair_codes
      return if @material_pair_codes
      codes = {}
      (1..NB_POKEMON).each do |first_id|
        (first_id..NB_POKEMON).each do |second_id|
          pair_index = deterministic_result_value(first_id, second_id) %
                       @fusion_pairs.length
          result_ids = @fusion_pairs[pair_index]
          codes[result_ids[0]] ||= []
          codes[result_ids[0]] << pack_material_pair(first_id, second_id)
          if first_id != second_id
            codes[result_ids[1]] ||= []
            codes[result_ids[1]] << pack_material_pair(second_id, first_id)
          end
        end
      end
      codes.each_value(&:freeze)
      @material_pair_codes = codes.freeze
    end

    def schema_3_material_pairs_for(species_id)
      fusion_pair_index = @fusion_pairs.index do |fusion_pair|
        fusion_pair[0] == species_id || fusion_pair[1] == species_id
      end
      if !fusion_pair_index
        raise PlayerFusionMappingError,
              "a custom fusion has no result-pair position"
      end
      pairs = []
      seen = {}
      append_cached_material_pairs(species_id, pairs, seen)
      (1..NB_POKEMON).each do |first_id|
        (first_id..NB_POKEMON).each do |second_id|
          @work_checkpoint.call if @work_checkpoint &&
            (second_id % 32).zero?
          pair = [first_id, second_id]
          result_ids = schema_3_result_ids_at_pair(
            pair, fusion_pair_index
          )
          next if !result_ids
          append_material_pair(
            pairs, seen, first_id, second_id
          ) if result_ids[0] == species_id
          if first_id != second_id && result_ids[1] == species_id
            append_material_pair(
              pairs, seen, second_id, first_id
            )
          end
        end
      end
      return pairs.sort
    end

    def ensure_schema_5_material_pair_codes
      return if @schema_5_material_pair_codes
      codes = {}
      (1..NB_POKEMON).each do |first_id|
        (first_id..NB_POKEMON).each do |second_id|
          @work_checkpoint.call if @work_checkpoint &&
            (second_id % 32).zero?
          result_ids = select_schema_5_result_ids([first_id, second_id])
          codes[result_ids[0]] ||= []
          codes[result_ids[0]] << pack_material_pair(first_id, second_id)
          next if first_id == second_id
          codes[result_ids[1]] ||= []
          codes[result_ids[1]] << pack_material_pair(second_id, first_id)
        end
      end
      codes.each_value { |entries| entries.sort!.freeze }
      @schema_5_material_pair_codes = codes.freeze
    end

    def append_cached_material_pairs(species_id, pairs, seen)
      @mappings.keys.each do |key|
        match = /\A(\d+):(\d+)\z/.match(key.to_s)
        next if !match
        first_id = match[1].to_i
        second_id = match[2].to_i
        next if first_id <= 0 || second_id <= 0 ||
          first_id > NB_POKEMON || second_id > NB_POKEMON
        result_ids = mapped_result_ids([first_id, second_id].sort)
        append_material_pair(
          pairs, seen, first_id, second_id
        ) if result_ids[0] == species_id
        if first_id != second_id && result_ids[1] == species_id
          append_material_pair(
            pairs, seen, second_id, first_id
          )
        end
      end
    end

    def append_material_pair(pairs, seen, body_id, head_id)
      code = pack_material_pair(body_id, head_id)
      return if seen[code]
      seen[code] = true
      pairs << [body_id, head_id]
    end

    def schema_3_result_ids_at_pair(pair, fusion_pair_index)
      body = GameData::Species.get(pair[0])
      head = GameData::Species.get(pair[1])
      source_type_mask = type_mask([
        body.type1, body.type2, head.type1, head.type2
      ].compact.uniq)
      forward_range = preferred_range(normal_fusion_bst(body, head))
      reverse_range = preferred_range(normal_fusion_bst(head, body))
      reverse_first = deterministic_value(
        "orientation", pair[0], pair[1]
      ).odd?
      result_ids = matching_result_orientation(
        @fusion_pairs[fusion_pair_index], source_type_mask,
        forward_range, reverse_range, reverse_first
      )
      return nil if !result_ids
      previous_index = fusion_pair_index
      loop do
        previous_index -= 1
        previous_index = @fusion_pairs.length - 1 if previous_index < 0
        break if previous_index == fusion_pair_index
        break if result_pair_matches?(
          @fusion_pairs[previous_index], source_type_mask,
          forward_range, reverse_range
        )
      end
      return result_ids if previous_index == fusion_pair_index
      start = deterministic_result_value(pair[0], pair[1]) %
              @fusion_pairs.length
      distance_to_result = (fusion_pair_index - start) %
                           @fusion_pairs.length
      distance_to_previous = (previous_index - start) %
                             @fusion_pairs.length
      return distance_to_result < distance_to_previous ? result_ids : nil
    end

    def matching_result_orientation(fusion_pair, source_type_mask, forward_range,
                                    reverse_range, reverse_first)
      first = fusion_pair
      second = [fusion_pair[1], fusion_pair[0]]
      first, second = second, first if reverse_first
      return first if result_orientation_matches?(
        first, source_type_mask, forward_range, reverse_range
      )
      return second if result_orientation_matches?(
        second, source_type_mask, forward_range, reverse_range
      )
      return nil
    end

    def result_pair_matches?(fusion_pair, source_type_mask, forward_range,
                             reverse_range)
      return true if result_orientation_matches?(
        fusion_pair, source_type_mask, forward_range, reverse_range
      )
      reverse = [fusion_pair[1], fusion_pair[0]]
      return result_orientation_matches?(
        reverse, source_type_mask, forward_range, reverse_range
      )
    end

    def result_orientation_matches?(orientation, source_type_mask, forward_range,
                                    reverse_range)
      return target_matches?(
        orientation[0], source_type_mask, forward_range
      ) && target_matches?(
        orientation[1], source_type_mask, reverse_range
      )
    end

    def pack_material_pair(body_id, head_id)
      return (body_id << MATERIAL_ID_BITS) | head_id
    end

    def normal_input_ids(body_species, head_species)
      body = GameData::Species.try_get(body_species)
      head = GameData::Species.try_get(head_species)
      if !body || !head || body.id_number <= 0 || head.id_number <= 0 ||
         body.id_number > NB_POKEMON || head.id_number > NB_POKEMON
        raise PlayerFusionMappingError,
              "player fusion inputs must both be normal species"
      end
      return body.id_number, head.id_number
    end

    def mapped_result_ids(pair)
      ensure_fusion_pool
      key = pair_key(pair)
      stored = @mappings[key]
      if stored
        begin
          first_id = validate_result_id(
            stored.is_a?(Array) ? stored[0] : stored
          )
          if @schema_version == SCHEMA_VERSION && stored.is_a?(Array) &&
             stored.length >= 2
            second_id = validate_result_id(stored[1])
            if paired_result_id(first_id) == second_id
              @mappings[key] = [first_id, second_id]
              return @mappings[key]
            end
            raise PlayerFusionMappingError,
                  "the stored player fusion pair is inconsistent"
          end
          @mappings[key] = [first_id, paired_result_id(first_id)]
          return @mappings[key]
        rescue PlayerFusionMappingError
          @mappings.delete(key)
          @discoveries.delete(key)
        end
      end
      if @schema_version == LEGACY_SCHEMA_VERSION
        pair_index = deterministic_result_value(pair[0], pair[1]) %
                     @fusion_pairs.length
        first_id, second_id = @fusion_pairs[pair_index]
      else
        first_id, second_id = select_result_ids(pair)
      end
      @mappings[key] = [validate_result_id(first_id),
                        validate_result_id(second_id)]
      return @mappings[key]
    end

    def pair_key(pair)
      return "#{pair[0]}:#{pair[1]}"
    end

    def deterministic_value(*parts)
      return Ironmon.fnv1a_64_joined(
        [@schema_version, @seed, NAMESPACE, *parts]
      )
    end

    def deterministic_result_value(first_id, second_id)
      if !@result_hash_prefix
        prefix = [@schema_version, @seed, NAMESPACE, "result", ""].join("|")
        @result_hash_prefix = update_deterministic_hash(
          Ironmon::FNV1A_64_OFFSET_BASIS, prefix
        )
      end
      value = update_deterministic_hash(@result_hash_prefix, first_id.to_s)
      value = update_deterministic_hash(value, "|")
      return update_deterministic_hash(value, second_id.to_s)
    end

    def select_result_ids(pair)
      return select_schema_5_result_ids(pair) if
        @schema_version == SCHEMA_VERSION
      return select_schema_3_result_ids(pair)
    end

    def select_schema_3_result_ids(pair)
      return select_result_ids_linear(pair) if !@use_result_index
      body = GameData::Species.get(pair[0])
      head = GameData::Species.get(pair[1])
      source_types = [body.type1, body.type2, head.type1, head.type2].compact.uniq
      source_type_mask = type_mask(source_types)
      forward_bst = normal_fusion_bst(body, head)
      reverse_bst = normal_fusion_bst(head, body)
      forward_range = preferred_range(forward_bst)
      reverse_range = preferred_range(reverse_bst)
      start = deterministic_result_value(pair[0], pair[1]) %
              @fusion_pairs.length
      reverse_first = deterministic_value(
        "orientation", pair[0], pair[1]
      ).odd?
      result = indexed_result_ids(
        start, source_type_mask, forward_range, reverse_range, reverse_first
      )
      return result if result
      return closest_result_ids(
        pair, source_type_mask, forward_bst, reverse_bst, reverse_first
      )
    end

    def select_schema_5_result_ids(pair)
      body = GameData::Species.get(pair[0])
      head = GameData::Species.get(pair[1])
      source_type_mask = type_mask(
        [body.type1, body.type2, head.type1, head.type2].compact.uniq
      )
      range = fusion_bst_range(normal_bst(body), normal_bst(head))
      target_bst = range.begin + deterministic_value(
        "bst_target", pair[0], pair[1]
      ) % (range.end - range.begin + 1)
      result_ids = closest_schema_5_result_ids(
        pair, source_type_mask, target_bst, range
      )
      return result_ids if result_ids
      available_range = @result_bst_minimum..@result_bst_maximum
      result_ids = closest_schema_5_result_ids(
        pair, source_type_mask, target_bst, available_range
      )
      return result_ids if result_ids
      raise PlayerFusionMappingError,
            "no custom fusion pair satisfies the BST and type rules"
    end

    def fusion_bst_range(first_bst, second_bst)
      higher = [first_bst.to_i, second_bst.to_i].max
      lower = [first_bst.to_i, second_bst.to_i].min
      gap = higher - lower
      bonus_basis = [higher, 500].min
      maximum = higher + ((10 * bonus_basis) / (100 + gap))
      return higher..maximum if gap <= 40
      curve_loss = (bonus_basis * (gap - 40)) / (4 * (100 + gap))
      loss = [80, (3 * higher) / 20, curve_loss].min
      minimum = [lower, higher - loss].max
      return minimum..maximum
    end

    def closest_schema_5_result_ids(pair, source_type_mask, target_bst,
                                    permitted_range)
      ensure_result_bst_index
      best = nil
      maximum_distance = [
        (target_bst - @result_bst_minimum).abs,
        (@result_bst_maximum - target_bst).abs
      ].max
      (0..maximum_distance).each do |distance|
        break if best && distance > best[0]
        values = [target_bst - distance]
        values << target_bst + distance if distance > 0
        values.each do |bst|
          next if !permitted_range.include?(bst)
          entries = @result_bst_index[bst]
          next if !entries
          entries.each do |entry|
            position = entry >> 1
            orientation = entry & 1
            fusion_pair = @fusion_pairs[position]
            first_id = fusion_pair[orientation]
            second_id = fusion_pair[1 - orientation]
            next if (target_type_mask(first_id) & source_type_mask).zero?
            next if (target_type_mask(second_id) & source_type_mask).zero?
            second_bst = target_bst(second_id)
            next if !permitted_range.include?(second_bst)
            score = distance + (second_bst - target_bst).abs
            next if best && score > best[0]
            rank = deterministic_value(
              "target_match", pair[0], pair[1], first_id, second_id
            )
            candidate = [score, rank, first_id, second_id]
            best = candidate if !best ||
              (candidate[0, 2] <=> best[0, 2]) < 0
          end
        end
        @work_checkpoint.call if @work_checkpoint
      end
      return best ? [best[2], best[3]] : nil
    end

    def ensure_result_bst_index
      return if @result_bst_index
      index = {}
      @fusion_pairs.each_with_index do |fusion_pair, position|
        @work_checkpoint.call if @work_checkpoint && (position % 32).zero?
        fusion_pair.each_with_index do |species_id, orientation|
          bst = target_bst(species_id)
          index[bst] ||= []
          index[bst] << ((position << 1) | orientation)
        end
      end
      index.each_value(&:freeze)
      @result_bst_index = index.freeze
      bst_values = index.keys
      @result_bst_minimum = bst_values.min
      @result_bst_maximum = bst_values.max
    end

    def indexed_result_ids(start, source_type_mask, forward_range,
                           reverse_range, reverse_first)
      ensure_result_candidate_index
      states = result_candidate_states(
        start, source_type_mask, forward_range
      )
      while !states.empty?
        distance = states.map { |state| result_candidate_distance(state, start) }.min
        position = (start + distance) % @fusion_pairs.length
        result = matching_indexed_result_at(
          position, source_type_mask, forward_range, reverse_range,
          reverse_first
        )
        return result if result
        advance_result_candidate_states(states, position)
        @work_checkpoint.call if @work_checkpoint
      end
      return nil
    end

    def matching_indexed_result_at(position, source_type_mask, forward_range,
                                   reverse_range, reverse_first)
      fusion_pair = @fusion_pairs[position]
      first_id = fusion_pair[0]
      second_id = fusion_pair[1]
      if reverse_first
        return [second_id, first_id] if
          target_data_matches?(second_id, source_type_mask, forward_range) &&
          target_data_matches?(first_id, source_type_mask, reverse_range)
        return [first_id, second_id] if
          target_data_matches?(first_id, source_type_mask, forward_range) &&
          target_data_matches?(second_id, source_type_mask, reverse_range)
      else
        return [first_id, second_id] if
          target_data_matches?(first_id, source_type_mask, forward_range) &&
          target_data_matches?(second_id, source_type_mask, reverse_range)
        return [second_id, first_id] if
          target_data_matches?(second_id, source_type_mask, forward_range) &&
          target_data_matches?(first_id, source_type_mask, reverse_range)
      end
      return nil
    end

    def result_candidate_states(start, source_type_mask, forward_range)
      states = []
      minimum_bucket = forward_range.begin / RESULT_INDEX_BST_BUCKET_SIZE
      maximum_bucket = forward_range.end / RESULT_INDEX_BST_BUCKET_SIZE
      each_type_code(source_type_mask) do |type_code|
        buckets = @result_candidate_index[type_code]
        next if !buckets
        (minimum_bucket..maximum_bucket).each do |bucket|
          entries = buckets[bucket]
          next if !entries || entries.empty?
          index = lower_bound(entries, start << 1)
          index = 0 if index >= entries.length
          states << {
            :entries => entries,
            :index => index,
            :remaining => entries.length
          }
        end
      end
      return states
    end

    def result_candidate_distance(state, start)
      position = state[:entries][state[:index]] >> 1
      return (position - start) % @fusion_pairs.length
    end

    def advance_result_candidate_states(states, position)
      states.delete_if do |state|
        while state[:remaining] > 0 &&
              (state[:entries][state[:index]] >> 1) == position
          state[:remaining] -= 1
          state[:index] += 1
          state[:index] = 0 if state[:index] >= state[:entries].length
        end
        state[:remaining] <= 0
      end
    end

    def lower_bound(entries, value)
      low = 0
      high = entries.length
      while low < high
        middle = (low + high) / 2
        if entries[middle] < value
          low = middle + 1
        else
          high = middle
        end
      end
      return low
    end

    def each_type_code(mask)
      code = 0
      while mask > 0
        yield code if (mask & 1) == 1
        mask >>= 1
        code += 1
      end
    end

    def ensure_result_candidate_index
      return if @result_candidate_index
      indexes = []
      @fusion_pairs.each_with_index do |fusion_pair, position|
        @work_checkpoint.call if @work_checkpoint && (position % 32).zero?
        fusion_pair.each_with_index do |species_id, orientation|
          bucket = target_bst(species_id) / RESULT_INDEX_BST_BUCKET_SIZE
          each_type_code(target_type_mask(species_id)) do |type_code|
            indexes[type_code] ||= {}
            indexes[type_code][bucket] ||= []
            indexes[type_code][bucket] << ((position << 1) | orientation)
          end
        end
      end
      indexes.each do |buckets|
        next if !buckets
        buckets.each_value(&:freeze)
        buckets.freeze
      end
      @result_candidate_index = indexes.freeze
    end

    def select_result_ids_linear(pair)
      body = GameData::Species.get(pair[0])
      head = GameData::Species.get(pair[1])
      source_type_mask = type_mask(
        [body.type1, body.type2, head.type1, head.type2].compact.uniq
      )
      forward_bst = normal_fusion_bst(body, head)
      reverse_bst = normal_fusion_bst(head, body)
      forward_range = preferred_range(forward_bst)
      reverse_range = preferred_range(reverse_bst)
      start = deterministic_result_value(pair[0], pair[1]) %
              @fusion_pairs.length
      reverse_first = deterministic_value(
        "orientation", pair[0], pair[1]
      ).odd?
      @fusion_pairs.length.times do |offset|
        result = matching_indexed_result_at(
          (start + offset) % @fusion_pairs.length, source_type_mask,
          forward_range, reverse_range, reverse_first
        )
        return result if result
      end
      return closest_result_ids(
        pair, source_type_mask, forward_bst, reverse_bst, reverse_first
      )
    end

    def preferred_range(reference_bst)
      minimum = divide_round_up(
        reference_bst * PREFERRED_MINIMUM_PERCENT, 100
      )
      maximum = (reference_bst * PREFERRED_MAXIMUM_PERCENT) / 100
      return minimum..maximum
    end

    def target_matches?(species_id, source_type_mask, preferred_range)
      return target_data_matches?(
        species_id, source_type_mask, preferred_range
      )
    end

    def target_data_matches?(species_id, source_type_mask, preferred_range)
      return false if (target_type_mask(species_id) & source_type_mask).zero?
      return preferred_range.include?(target_bst(species_id))
    end

    def closest_result_ids(pair, source_type_mask, forward_bst, reverse_bst,
                           reverse_first)
      best = nil
      @fusion_pairs.each_with_index do |fusion_pair, index|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        first_id = fusion_pair[0]
        second_id = fusion_pair[1]
        if reverse_first
          best = closest_result_candidate(
            best, pair, source_type_mask, forward_bst, reverse_bst,
            second_id, first_id
          )
          best = closest_result_candidate(
            best, pair, source_type_mask, forward_bst, reverse_bst,
            first_id, second_id
          )
        else
          best = closest_result_candidate(
            best, pair, source_type_mask, forward_bst, reverse_bst,
            first_id, second_id
          )
          best = closest_result_candidate(
            best, pair, source_type_mask, forward_bst, reverse_bst,
            second_id, first_id
          )
        end
      end
      if !best
        raise PlayerFusionMappingError,
              "no custom fusion pair shares a consumed Pokemon type"
      end
      return [best[2], best[3]]
    end

    def closest_result_candidate(best, pair, source_type_mask, forward_bst,
                                 reverse_bst, first_id, second_id)
      return best if (target_type_mask(first_id) & source_type_mask).zero?
      return best if (target_type_mask(second_id) & source_type_mask).zero?
      score = (target_bst(first_id) - forward_bst).abs +
              (target_bst(second_id) - reverse_bst).abs
      return best if best && score > best[0]
      rank = deterministic_value(
        "fallback", pair[0], pair[1], first_id, second_id
      )
      candidate = [score, rank, first_id, second_id]
      return candidate if !best ||
        (candidate[0, 2] <=> best[0, 2]) < 0
      return best
    end

    def normal_fusion_bst(body, head)
      body_stats = normal_stats(body)
      head_stats = normal_stats(head)
      return BaseStatGenerator::STAT_ORDER.inject(0) do |sum, stat|
        head_dominant = BaseStatGenerator::HEAD_DOMINANT_STATS.include?(stat)
        dominant = head_dominant ? head_stats[stat] : body_stats[stat]
        other = head_dominant ? body_stats[stat] : head_stats[stat]
        sum + ((2 * dominant.to_i) / 3) + (other.to_i / 3)
      end
    end

    def normal_stats(species)
      return @normal_stats[species.id] if @normal_stats[species.id]
      @normal_stats[species.id] = @base_stat_generator.stats_for(species)
      return @normal_stats[species.id]
    end

    def normal_bst(species)
      return normal_stats(species).values.inject(0) do |sum, value|
        sum + value.to_i
      end
    end

    def prepare_target_data(species_id)
      return if @target_bsts[species_id]
      if !@fusion_pool_membership || !@fusion_pool_membership[species_id]
        raise PlayerFusionMappingError,
              "a custom fusion has no cached material components"
      end
      body_id = (species_id - 1) / NB_POKEMON
      head_id = species_id - (body_id * NB_POKEMON)
      body = GameData::Species.get(body_id)
      head = GameData::Species.get(head_id)
      body_stats = normal_stats(body)
      head_stats = normal_stats(head)
      bst = BaseStatGenerator::STAT_ORDER.inject(0) do |sum, stat|
        head_dominant = BaseStatGenerator::HEAD_DOMINANT_STATS.include?(stat)
        dominant = head_dominant ? head_stats[stat] : body_stats[stat]
        other = head_dominant ? body_stats[stat] : head_stats[stat]
        sum + ((2 * dominant.to_i) / 3) + (other.to_i / 3)
      end
      type1 = if head.type1 == :NORMAL && head.type2 == :FLYING
                head.type2
              else
                head.type1
              end
      type2 = body.type2 == type1 ? body.type1 : body.type2
      @target_bsts[species_id] = bst
      @target_type_masks[species_id] = type_mask(
        [type1, type2].compact.uniq
      )
    end

    def target_bst(species_id)
      prepare_target_data(species_id) if !@target_bsts[species_id]
      return @target_bsts[species_id]
    end

    def target_type_mask(species_id)
      prepare_target_data(species_id) if !@target_bsts[species_id]
      return @target_type_masks[species_id]
    end

    def type_mask(types)
      return types.inject(0) do |mask, type|
        code = @type_codes[type]
        if !code
          code = @type_codes.length
          @type_codes[type] = code
        end
        mask | (1 << code)
      end
    end

    def divide_round_up(value, divisor)
      return (value + divisor - 1) / divisor
    end

    def update_deterministic_hash(value, input)
      return Ironmon.fnv1a_64(input, value)
    end

    def paired_result_id(species_id)
      ensure_fusion_pool
      partner_id = @paired_result_ids[species_id]
      if !partner_id
        raise PlayerFusionMappingError,
              "a custom fusion has no Ironmon reverse partner"
      end
      return validate_result_id(partner_id)
    end

    def ensure_fusion_pool
      return if @fusion_pool_ids
      if !@fusion_pool || @fusion_pool.empty?
        raise PlayerFusionMappingError,
              "the custom fusion pool is empty"
      end
      @fusion_pool_membership = []
      fusion_pool_ids = []
      @fusion_pool.each_with_index do |species, index|
        @preparation_progress = index
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        if species.is_a?(Integer)
          species_id = species
          body_id = (species_id - 1) / NB_POKEMON
          head_id = species_id - (body_id * NB_POKEMON)
        else
          match = /\AB(\d+)H(\d+)\z/.match(species.to_s)
          if !match
            raise PlayerFusionMappingError,
                  "the custom fusion pool contains an invalid identifier"
          end
          body_id = match[1].to_i
          head_id = match[2].to_i
          species_id = (body_id * NB_POKEMON) + head_id
        end
        if body_id <= 0 || body_id > NB_POKEMON ||
           head_id <= 0 || head_id > NB_POKEMON
          raise PlayerFusionMappingError,
                "the custom fusion pool contains invalid components"
        end
        @fusion_pool_membership[species_id] = true
        fusion_pool_ids << species_id
      end
      @fusion_pool_ids = fusion_pool_ids
      if @fusion_pool_ids.length.odd?
        raise PlayerFusionMappingError,
              "the custom fusion pool cannot form complete reverse pairs"
      end
      build_fusion_pairs
    end

    def build_fusion_pairs
      return build_schema_5_fusion_pairs if @schema_version == SCHEMA_VERSION
      16.times do |attempt|
        shuffled = deterministic_shuffle(@fusion_pool_ids, attempt)
        pairs = []
        partners = {}
        pairing_failed = false
        (0...shuffled.length).step(2) do |position|
          @work_checkpoint.call if @work_checkpoint && (position % 32).zero?
          partner_position = position + 1
          while partner_position < shuffled.length &&
                shares_component?(
                  shuffled[position], shuffled[partner_position]
                )
            @work_checkpoint.call if @work_checkpoint &&
              (partner_position % 32).zero?
            partner_position += 1
          end
          if partner_position >= shuffled.length
            pairing_failed = true
            break
          end
          shuffled[position + 1], shuffled[partner_position] =
            shuffled[partner_position], shuffled[position + 1]
          first_id = shuffled[position]
          second_id = shuffled[position + 1]
          pairs << [first_id, second_id]
          partners[first_id] = second_id
          partners[second_id] = first_id
        end
        next if pairing_failed
        @fusion_pairs = pairs.freeze
        @paired_result_ids = partners.freeze
        return
      end
      raise PlayerFusionMappingError,
            "the custom fusion pool could not form disjoint reverse pairs"
    end

    def build_schema_5_fusion_pairs
      maximum_pair_difference = maximum_fusion_range_width
      16.times do |attempt|
        @preparation_stage = :strength_order
        @preparation_progress = attempt
        ordered = strength_ordered_fusion_ids(attempt)
        pairs = []
        pairing_failed = false
        @preparation_stage = :strength_pairing
        (0...ordered.length).step(2) do |position|
          @preparation_progress = position
          @work_checkpoint.call if @work_checkpoint && (position % 32).zero?
          partner_position = strength_partner_position(
            ordered, position, true, maximum_pair_difference
          )
          partner_position ||= strength_partner_position(
            ordered, position, false, maximum_pair_difference
          )
          if !partner_position
            repaired = repair_final_strength_pair(
              pairs, ordered[position], ordered[position + 1],
              maximum_pair_difference
            ) if position == ordered.length - 2
            if repaired
              break
            end
            pairing_failed = true
            break
          end
          ordered[position + 1], ordered[partner_position] =
            ordered[partner_position], ordered[position + 1]
          pairs << [ordered[position], ordered[position + 1]]
        end
        next if pairing_failed
        @preparation_stage = :strength_validation
        invalid_pair = pairs.each_with_index.any? do |fusion_pair, index|
          @preparation_progress = index
          @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
          first_bst = target_bst(fusion_pair[0])
          second_bst = target_bst(fusion_pair[1])
          (first_bst - second_bst).abs > maximum_pair_difference
        end
        next if invalid_pair
        install_fusion_pairs(pairs)
        return
      end
      raise PlayerFusionMappingError,
            "the custom fusion pool could not form strength-matched pairs"
    end

    def strength_ordered_fusion_ids(attempt)
      buckets = {}
      @fusion_pool_ids.each_with_index do |species_id, index|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        bst = target_bst(species_id)
        buckets[bst] ||= []
        buckets[bst] << [
          deterministic_value("pairing_rank", attempt, species_id),
          species_id
        ]
      end
      ordered = []
      buckets.keys.sort.each_with_index do |bst, index|
        @work_checkpoint.call if @work_checkpoint && (index % 2).zero?
        buckets[bst].sort_by! { |entry| entry[0] }
        buckets[bst].each { |entry| ordered << entry[1] }
      end
      return ordered
    end

    def strength_partner_position(ordered, position, require_shared_type,
                                  maximum_pair_difference)
      first_id = ordered[position]
      ((position + 1)...ordered.length).each do |candidate_position|
        @work_checkpoint.call if @work_checkpoint &&
          ((candidate_position - position) % 16).zero?
        second_id = ordered[candidate_position]
        difference = target_bst(second_id) - target_bst(first_id)
        break if difference > maximum_pair_difference
        next if shares_component?(first_id, second_id)
        if require_shared_type
          first_types = target_type_mask(first_id)
          second_types = target_type_mask(second_id)
          next if (first_types & second_types).zero?
        end
        return candidate_position
      end
      return nil
    end

    def repair_final_strength_pair(pairs, current_first, current_second,
                                   maximum_pair_difference)
      @preparation_stage = :strength_repair
      best = nil
      pairs.each_with_index do |previous, pair_index|
        @preparation_progress = pair_index
        @work_checkpoint.call if @work_checkpoint && (pair_index % 32).zero?
        [previous, previous.reverse].each do |orientation|
          replacements = [
            [current_first, orientation[0]],
            [current_second, orientation[1]]
          ]
          next if replacements.any? do |replacement|
            shares_component?(replacement[0], replacement[1])
          end
          next if replacements.any? do |replacement|
            first_bst = target_bst(replacement[0])
            second_bst = target_bst(replacement[1])
            (first_bst - second_bst).abs > maximum_pair_difference
          end
          type_penalty = replacements.count do |replacement|
            first_types = target_type_mask(replacement[0])
            second_types = target_type_mask(replacement[1])
            (first_types & second_types).zero?
          end
          distance = replacements.inject(0) do |sum, replacement|
            first_bst = target_bst(replacement[0])
            second_bst = target_bst(replacement[1])
            sum + (first_bst - second_bst).abs
          end
          score = [distance, type_penalty, pair_index]
          best = [score, pair_index, replacements] if !best ||
            (score <=> best[0]) < 0
        end
      end
      return false if !best
      pairs.delete_at(best[1])
      pairs.concat(best[2])
      return true
    end

    def maximum_fusion_range_width
      return @maximum_fusion_range_width if @maximum_fusion_range_width
      @preparation_stage = :maximum_range
      values = (1..NB_POKEMON).map do |species_id|
        normal_bst(GameData::Species.get(species_id))
      end.uniq
      maximum = 0
      values.each_with_index do |first_bst, index|
        @preparation_progress = index
        @work_checkpoint.call if @work_checkpoint && (index % 4).zero?
        values.each do |second_bst|
          range = fusion_bst_range(first_bst, second_bst)
          width = range.end - range.begin
          maximum = width if width > maximum
        end
      end
      @maximum_fusion_range_width = maximum
      return @maximum_fusion_range_width
    end

    def install_fusion_pairs(pairs)
      partners = {}
      pairs.each_with_index do |pair, index|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        partners[pair[0]] = pair[1]
        partners[pair[1]] = pair[0]
      end
      @fusion_pairs = pairs.freeze
      @paired_result_ids = partners.freeze
    end

    def deterministic_shuffle(source, attempt)
      shuffled = source.dup
      state = deterministic_value("pairing", attempt)
      state = Ironmon::FNV1A_64_OFFSET_BASIS if state == 0
      (shuffled.length - 1).downto(1) do |index|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        state ^= (state << 13) & Ironmon::FNV1A_64_MASK
        state ^= state >> 7
        state ^= (state << 17) & Ironmon::FNV1A_64_MASK
        state &= Ironmon::FNV1A_64_MASK
        swap_index = state % (index + 1)
        shuffled[index], shuffled[swap_index] =
          shuffled[swap_index], shuffled[index]
      end
      return shuffled
    end

    def shares_component?(first_id, second_id)
      first_body = (first_id - 1) / NB_POKEMON
      first_head = first_id - (first_body * NB_POKEMON)
      second_body = (second_id - 1) / NB_POKEMON
      second_head = second_id - (second_body * NB_POKEMON)
      return first_body == second_body || first_body == second_head ||
             first_head == second_body || first_head == second_head
    end

    def validate_result_id(species_id)
      species = GameData::Species.try_get(species_id)
      if !species || species.id_number <= NB_POKEMON ||
         species.id_number >= Settings::ZAPMOLCUNO_NB ||
         (@fusion_pool_membership ?
            !@fusion_pool_membership[species.id_number] :
          !Ironmon.custom_fusion_species?(species.id))
        raise PlayerFusionMappingError,
              "a player fusion mapping is no longer a valid custom fusion"
      end
      return species.id_number
    end
  end

  def self.player_fusion_mapper
    state = pivot_state
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    if player_fusion_preparation_matches?(seed, state.object_id)
      finish_player_fusion_preparation
    elsif @player_fusion_preparation_fiber
      clear_player_fusion_preparation
    end
    if !@player_fusion_mapper || @player_fusion_mapper_seed != seed ||
       @player_fusion_mapper_state_id != state.object_id
      @player_fusion_mapper = PlayerFusionMapper.new(
        seed,
        custom_fusion_pool_numbers,
        state.fusion_mappings,
        state.discovered_fusion_mappings,
        base_stat_generator
      )
      @player_fusion_mapper_seed = seed
      @player_fusion_mapper_state_id = state.object_id
    end
    return @player_fusion_mapper
  end

  def self.player_fusion_species(body_species, head_species)
    return player_fusion_mapper.species(body_species, head_species)
  end

  def self.known_player_fusion_species(body_species, head_species)
    return player_fusion_mapper.known_species(body_species, head_species)
  end

  def self.record_player_fusion_discovery(body_pokemon, head_pokemon,
                                          result_pokemon)
    if !body_pokemon || !head_pokemon || !result_pokemon
      raise PlayerFusionMappingError,
            "fusion discovery data is incomplete"
    end
    return player_fusion_mapper.discover(
      body_pokemon.species, head_pokemon.species, result_pokemon.species
    )
  end

  def self.paired_custom_fusion_species(fusion_species)
    return player_fusion_mapper.paired_species(fusion_species)
  end

  def self.prepare_player_fusion_pairing
    state = pivot_state
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    return true if @player_fusion_mapper &&
      @player_fusion_mapper_seed == seed &&
      @player_fusion_mapper_state_id == state.object_id
    return true if player_fusion_preparation_matches?(seed, state.object_id)
    mapper = PlayerFusionMapper.new(
      seed,
      custom_fusion_pool_numbers,
      state.fusion_mappings,
      state.discovered_fusion_mappings,
      base_stat_generator,
      PlayerFusionMapper::SCHEMA_VERSION,
      proc { Fiber.yield }
    )
    @player_fusion_preparation_mapper = mapper
    @player_fusion_preparation_seed = seed
    @player_fusion_preparation_state_id = state.object_id
    @player_fusion_preparation_fiber = Fiber.new { mapper.prepare }
    write_player_fusion_preparation_trace(mapper, "scheduled")
    return true
  end

  def self.advance_player_fusion_pairing
    fiber = @player_fusion_preparation_fiber
    return false if !fiber
    state = pivot_state
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    if !player_fusion_preparation_matches?(seed, state.object_id)
      clear_player_fusion_preparation
      return false
    end
    deadline = player_fusion_preparation_uptime +
      PLAYER_FUSION_PREPARATION_BUDGET_SECONDS
    write_player_fusion_preparation_trace(
      @player_fusion_preparation_mapper, "slice_started"
    )
    fiber.resume while fiber.alive? &&
      player_fusion_preparation_uptime < deadline
    write_player_fusion_preparation_trace(
      @player_fusion_preparation_mapper,
      fiber.alive? ? "slice_yielded" : "prepared"
    )
    install_prepared_player_fusion_mapper if !fiber.alive?
    return true
  rescue Exception => e
    echoln "Ironmon background player-fusion pairing failed: #{e.message}"
    clear_player_fusion_preparation
    return false
  end

  def self.finish_player_fusion_preparation
    fiber = @player_fusion_preparation_fiber
    return false if !fiber
    fiber.resume while fiber.alive?
    install_prepared_player_fusion_mapper
    return true
  rescue Exception
    clear_player_fusion_preparation
    raise
  end

  def self.player_fusion_preparation_matches?(seed, state_id)
    return @player_fusion_preparation_fiber &&
      @player_fusion_preparation_seed == seed &&
      @player_fusion_preparation_state_id == state_id
  end

  def self.install_prepared_player_fusion_mapper
    @player_fusion_mapper =
      @player_fusion_preparation_mapper.finish_cooperative_preparation
    @player_fusion_mapper_seed = @player_fusion_preparation_seed
    @player_fusion_mapper_state_id = @player_fusion_preparation_state_id
    write_player_fusion_preparation_trace(
      @player_fusion_preparation_mapper, "installed"
    )
    clear_player_fusion_preparation
  end

  def self.player_fusion_preparation_trace_path
    return nil if !defined?(SaveData::SAVE_DIR) || !SaveData::SAVE_DIR
    return File.join(SaveData::SAVE_DIR, PLAYER_FUSION_TRACE_FILENAME)
  end

  def self.write_player_fusion_preparation_trace(mapper, event)
    path = player_fusion_preparation_trace_path
    return false if !path || !mapper
    File.binwrite(
      path,
      "#{event}|#{mapper.preparation_stage}|" +
        "#{mapper.preparation_progress}|" +
        "#{player_fusion_preparation_uptime}\n"
    )
    return true
  rescue Exception
    return false
  end

  def self.clear_player_fusion_preparation
    @player_fusion_preparation_mapper = nil
    @player_fusion_preparation_seed = nil
    @player_fusion_preparation_state_id = nil
    @player_fusion_preparation_fiber = nil
  end

  def self.player_fusion_preparation_uptime
    return System.uptime.to_f / PLAYER_FUSION_UPTIME_UNITS_PER_SECOND
  end

  def self.reset_player_fusion_mapper_cache
    @player_fusion_mapper = nil
    @player_fusion_mapper_seed = nil
    @player_fusion_mapper_state_id = nil
    clear_player_fusion_preparation
  end
end
