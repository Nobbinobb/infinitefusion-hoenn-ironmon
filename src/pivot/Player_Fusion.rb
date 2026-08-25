#===============================================================================
# Deterministic player-fusion gamble mapping and discovery tracking
#===============================================================================

module Ironmon
  class PlayerFusionMappingError < StandardError; end

  class PlayerFusionMapper
    SCHEMA_VERSION = 3
    PREVIOUS_SCHEMA_VERSION = 2
    SUPPORTED_SCHEMA_VERSIONS = [PREVIOUS_SCHEMA_VERSION,
                                 SCHEMA_VERSION].freeze
    PREFERRED_MINIMUM_PERCENT = 90
    PREFERRED_MAXIMUM_PERCENT = 115
    NAMESPACE = "player_fusion"
    MATERIAL_ID_BITS = 10
    MATERIAL_ID_MASK = (1 << MATERIAL_ID_BITS) - 1
    RESULT_INDEX_BST_BUCKET_SIZE = 32

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
      @fusion_components = nil
      @fusion_pairs = nil
      @paired_result_ids = nil
      @target_data = {}
      @result_hash_prefix = nil
      @material_pair_codes = nil
      @material_pairs = {}
      @type_codes = {}
      @normal_stats = {}
      @result_candidate_index = nil
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

    def prepare
      ensure_fusion_pool
      return true
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
          @mappings[key] = [first_id, paired_result_id(first_id)]
          return @mappings[key]
        rescue PlayerFusionMappingError
          @mappings.delete(key)
          @discoveries.delete(key)
        end
      end
      if @schema_version == PREVIOUS_SCHEMA_VERSION
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
      first = target_data(first_id)
      second = target_data(second_id)
      if reverse_first
        return [second_id, first_id] if
          target_data_matches?(second, source_type_mask, forward_range) &&
          target_data_matches?(first, source_type_mask, reverse_range)
        return [first_id, second_id] if
          target_data_matches?(first, source_type_mask, forward_range) &&
          target_data_matches?(second, source_type_mask, reverse_range)
      else
        return [first_id, second_id] if
          target_data_matches?(first, source_type_mask, forward_range) &&
          target_data_matches?(second, source_type_mask, reverse_range)
        return [second_id, first_id] if
          target_data_matches?(second, source_type_mask, forward_range) &&
          target_data_matches?(first, source_type_mask, reverse_range)
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
          data = target_data(species_id)
          bucket = data[:bst] / RESULT_INDEX_BST_BUCKET_SIZE
          each_type_code(data[:type_mask]) do |type_code|
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
        target_data(species_id), source_type_mask, preferred_range
      )
    end

    def target_data_matches?(data, source_type_mask, preferred_range)
      return false if (data[:type_mask] & source_type_mask).zero?
      return preferred_range.include?(data[:bst])
    end

    def closest_result_ids(pair, source_type_mask, forward_bst, reverse_bst,
                           reverse_first)
      best = nil
      @fusion_pairs.each_with_index do |fusion_pair, index|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        first_id = fusion_pair[0]
        second_id = fusion_pair[1]
        first = target_data(first_id)
        second = target_data(second_id)
        if reverse_first
          best = closest_result_candidate(
            best, pair, source_type_mask, forward_bst, reverse_bst,
            second_id, first_id, second, first
          )
          best = closest_result_candidate(
            best, pair, source_type_mask, forward_bst, reverse_bst,
            first_id, second_id, first, second
          )
        else
          best = closest_result_candidate(
            best, pair, source_type_mask, forward_bst, reverse_bst,
            first_id, second_id, first, second
          )
          best = closest_result_candidate(
            best, pair, source_type_mask, forward_bst, reverse_bst,
            second_id, first_id, second, first
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
                                 reverse_bst, first_id, second_id, first,
                                 second)
      return best if (first[:type_mask] & source_type_mask).zero?
      return best if (second[:type_mask] & source_type_mask).zero?
      score = (first[:bst] - forward_bst).abs +
              (second[:bst] - reverse_bst).abs
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

    def target_data(species_id)
      return @target_data[species_id] if @target_data[species_id]
      species = GameData::Species.get(species_id)
      stats = @base_stat_generator.fusion_stats_for(species)
      @target_data[species_id] = {
        :bst => stats.values.inject(0) { |sum, value| sum + value.to_i },
        :type_mask => type_mask(
          [species.type1, species.type2].compact.uniq
        )
      }.freeze
      return @target_data[species_id]
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
      @fusion_components = {}
      fusion_pool_ids = []
      @fusion_pool.each_with_index do |species, index|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        match = /\AB(\d+)H(\d+)\z/.match(species.to_s)
        if !match
          raise PlayerFusionMappingError,
                "the custom fusion pool contains an invalid identifier"
        end
        body_id = match[1].to_i
        head_id = match[2].to_i
        species_id = (body_id * NB_POKEMON) + head_id
        @fusion_components[species_id] = [body_id, head_id]
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
      first = @fusion_components[first_id]
      second = @fusion_components[second_id]
      return first[0] == second[0] || first[0] == second[1] ||
             first[1] == second[0] || first[1] == second[1]
    end

    def validate_result_id(species_id)
      species = GameData::Species.try_get(species_id)
      if !species || species.id_number <= NB_POKEMON ||
         species.id_number >= Settings::ZAPMOLCUNO_NB ||
         (@fusion_components ? !@fusion_components[species.id_number] :
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
    if !@player_fusion_mapper || @player_fusion_mapper_seed != seed ||
       @player_fusion_mapper_state_id != state.object_id
      @player_fusion_mapper = PlayerFusionMapper.new(
        seed,
        custom_fusion_pool,
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
    return player_fusion_mapper.prepare
  end

  def self.reset_player_fusion_mapper_cache
    @player_fusion_mapper = nil
    @player_fusion_mapper_seed = nil
    @player_fusion_mapper_state_id = nil
  end
end
