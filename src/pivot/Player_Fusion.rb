#===============================================================================
# Deterministic player-fusion gamble mapping and discovery tracking
#===============================================================================

module Ironmon
  PLAYER_FUSION_PREPARATION_BUDGET_SECONDS = 0.001
  PLAYER_FUSION_PREPARATION_INTERVAL_SECONDS = 1.0 / 60
  PLAYER_FUSION_UPTIME_UNITS_PER_SECOND = 1_000_000.0

  class PlayerFusionMappingError < StandardError; end
  class PlayerFusionStrengthPairingError < PlayerFusionMappingError; end

  class PlayerFusionMapper
    SCHEMA_VERSION = 2
    SUPPORTED_SCHEMA_VERSIONS = [1, SCHEMA_VERSION].freeze
    NAMESPACE = "player_fusion"
    MATERIAL_ID_BITS = 10
    MATERIAL_ID_MASK = (1 << MATERIAL_ID_BITS) - 1
    PAIRING_BST_STEP = 5

    attr_reader :preparation_stage
    attr_reader :pairing_bst_limit
    attr_reader :pairing_relaxation_steps

    def initialize(seed, fusion_pool, mappings, discoveries,
                   base_stat_generator = nil,
                   schema_version = SCHEMA_VERSION, work_checkpoint = nil)
      @seed = seed.to_i
      @fusion_pool = fusion_pool
      @mappings = mappings
      @discoveries = discoveries
      @base_stat_generator = base_stat_generator || Ironmon.base_stat_generator
      @schema_version = schema_version.to_i
      @work_checkpoint = work_checkpoint
      if !SUPPORTED_SCHEMA_VERSIONS.include?(@schema_version)
        raise PlayerFusionMappingError,
              "player fusion schema #{@schema_version} is unsupported"
      end
      @fusion_pool_ids = nil
      @fusion_pool_error = nil
      @fusion_pool_membership = nil
      @fusion_pairs = nil
      @paired_result_ids = nil
      @imported_pairing = false
      @target_bsts = []
      @target_type_masks = []
      @result_hash_prefix = nil
      @material_pair_codes = nil
      @material_pairs = {}
      @type_codes = {}
      @normal_stats = {}
      @result_bst_index = nil
      @result_bst_minimum = nil
      @result_bst_maximum = nil
      @preparation_stage = :initialized
      @pairing_bst_limit = nil
      @pairing_relaxation_steps = 0
    end

    def species(body_species, head_species)
      return GameData::Species.get(
        species_number(body_species, head_species)
      ).id
    end

    def preview_species(body_species, head_species)
      preview = clone
      preview.instance_variable_set(:@mappings, @mappings.dup)
      return preview.species(body_species, head_species)
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
      ensure_material_pair_codes
      codes = @material_pair_codes.fetch(species_id, [])
      pairs = codes.map do |code|
        [code >> MATERIAL_ID_BITS, code & MATERIAL_ID_MASK]
      end.freeze
      @material_pairs[species_id] = pairs
      return @material_pairs[species_id]
    end

    def material_pair_assignments_required?
      return !@material_pair_codes
    end

    def prepare
      @preparation_stage = :fusion_pool
      ensure_fusion_pool
      if @imported_pairing
        @preparation_stage = :target_data
        @fusion_pool_ids.each_with_index do |species_id, index|
          @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
          prepare_target_data(species_id)
        end
      end
      @preparation_stage = :prepared
      return true
    end

    def pairing_prepared?
      return !!(@fusion_pairs && @paired_result_ids)
    end

    def startup_prepared?
      return pairing_prepared? && !!@result_bst_index
    end

    def prepare_startup_result_index(work_checkpoint = nil)
      return true if @result_bst_index
      previous_checkpoint = @work_checkpoint
      restore_checkpoint = true
      @work_checkpoint = work_checkpoint || previous_checkpoint
      @preparation_stage = :result_index
      ensure_fusion_pool
      ensure_result_bst_index
      @preparation_stage = :prepared
      return true
    ensure
      @work_checkpoint = previous_checkpoint if restore_checkpoint
    end

    def import_prepared_pairing(packed_pairs, work_checkpoint = nil)
      return false if !packed_pairs.is_a?(String) || !@fusion_pool ||
        @fusion_pool.empty? || @fusion_pool.length.odd?
      byte_count = @fusion_pool.length * 4
      return false if packed_pairs.bytesize != ((byte_count + 2) / 3) * 4
      packed = packed_pairs.unpack1("m0")
      return false if packed.bytesize != byte_count
      membership = []
      @fusion_pool.each_with_index do |species_id, index|
        work_checkpoint.call if work_checkpoint && (index % 128).zero?
        return false if !species_id.is_a?(Integer) || membership[species_id]
        body_id = (species_id - 1) / NB_POKEMON
        head_id = species_id - body_id * NB_POKEMON
        return false if body_id < 1 || body_id > NB_POKEMON ||
          head_id < 1 || head_id > NB_POKEMON
        membership[species_id] = true
      end
      partners = {}; pairs = []
      packed.unpack("V*").each_slice(2).with_index do |(first, second), index|
        work_checkpoint.call if work_checkpoint && (index % 128).zero?
        return false if first <= NB_POKEMON || first >= Settings::ZAPMOLCUNO_NB ||
          second <= NB_POKEMON || second >= Settings::ZAPMOLCUNO_NB
        return false if !membership[first] || !membership[second] ||
          partners[first] || partners[second] || shares_component?(first, second)
        partners[first] = second
        partners[second] = first
        pairs << [first, second].freeze
      end
      @fusion_pool_membership = membership
      @fusion_pool_ids = @fusion_pool.dup.freeze
      @fusion_pairs = pairs.freeze
      @paired_result_ids = partners.freeze
      @imported_pairing = true
      @preparation_stage = :native_pairing
      return true
    rescue ArgumentError
      return false
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
          @work_checkpoint.call if @work_checkpoint &&
            (second_id % 32).zero?
          result_ids = select_result_ids([first_id, second_id])
          codes[result_ids[0]] ||= []
          codes[result_ids[0]] << pack_material_pair(first_id, second_id)
          next if first_id == second_id
          codes[result_ids[1]] ||= []
          codes[result_ids[1]] << pack_material_pair(second_id, first_id)
        end
      end
      codes.each_value { |entries| entries.sort!.freeze }
      @material_pair_codes = codes.freeze
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
          if SUPPORTED_SCHEMA_VERSIONS.include?(@schema_version) && stored.is_a?(Array) &&
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
      first_id, second_id = select_result_ids(pair)
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
      body = GameData::Species.get(pair[0])
      head = GameData::Species.get(pair[1])
      source_type_mask = type_mask(
        [body.type1, body.type2, head.type1, head.type2].compact.uniq
      )
      range = fusion_bst_range(normal_bst(body), normal_bst(head))
      target_bst = range.begin + deterministic_value(
        "bst_target", pair[0], pair[1]
      ) % (range.end - range.begin + 1)
      result_ids = closest_result_ids(
        pair, source_type_mask, target_bst, range
      )
      return result_ids if result_ids
      available_range = @result_bst_minimum..@result_bst_maximum
      result_ids = closest_result_ids(
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

    def closest_result_ids(pair, source_type_mask, target_bst, permitted_range)
      ensure_result_bst_index
      # Every candidate shares this prefix; continue the same hash from it.
      rank_prefix = deterministic_value("target_match", pair[0], pair[1], "")
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
            rank = update_deterministic_hash(
              rank_prefix, "#{first_id}|#{second_id}"
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

    def normal_fusion_bst(body, head)
      body_stats = normal_stats(body)
      head_stats = normal_stats(head)
      return @base_stat_generator.fuse(
        body_stats, head_stats, body.id_number * NB_POKEMON + head.id_number
      ).values.inject(0, :+)
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
      bst = @base_stat_generator.fuse(
        body_stats, head_stats, species_id
      ).values.inject(0, :+)
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
      raise @fusion_pool_error if @fusion_pool_error
      return if @fusion_pairs && @paired_result_ids
      if !@fusion_pool || @fusion_pool.empty?
        raise PlayerFusionMappingError,
              "the custom fusion pool is empty"
      end
      @fusion_pool_membership = []
      fusion_pool_ids = []
      @fusion_pool.each_with_index do |species, index|
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
    rescue StandardError => error
      @fusion_pool_ids = nil
      @fusion_pool_membership = nil
      @fusion_pairs = nil
      @paired_result_ids = nil
      @fusion_pool_error = error
      raise
    end

    def build_fusion_pairs
      maximum_pair_difference = maximum_fusion_range_width
      @pairing_bst_limit = maximum_pair_difference
      return build_fusion_pairs_for_limit(maximum_pair_difference) if @schema_version == 1
      ensure_disjoint_partners_exist
      minimum_bst = nil
      maximum_bst = nil
      @preparation_stage = :target_data
      @fusion_pool_ids.each_with_index do |species_id, index|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        bst = target_bst(species_id)
        minimum_bst = bst if !minimum_bst || bst < minimum_bst
        maximum_bst = bst if !maximum_bst || bst > maximum_bst
      end
      full_span = maximum_bst - minimum_bst
      loop do
        begin
          build_fusion_pairs_for_limit(@pairing_bst_limit)
          return
        rescue PlayerFusionStrengthPairingError
          raise if @pairing_bst_limit >= full_span
          @pairing_bst_limit = [@pairing_bst_limit + PAIRING_BST_STEP, full_span].min
          @pairing_relaxation_steps += 1
        end
      end
    end

    def ensure_disjoint_partners_exist
      components = Array.new(NB_POKEMON + 1, 0)
      component_pairs = {}
      @fusion_pool_ids.each_with_index do |species_id, index|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        body = (species_id - 1) / NB_POKEMON
        head = species_id - body * NB_POKEMON
        components[body] += 1
        components[head] += 1 if head != body
        key = [body, head].minmax
        component_pairs[key] = component_pairs.fetch(key, 0) + 1
      end
      @fusion_pool_ids.each_with_index do |species_id, index|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        body = (species_id - 1) / NB_POKEMON
        head = species_id - body * NB_POKEMON
        overlapping = components[body]
        overlapping += components[head] - component_pairs[[body, head].minmax] if head != body
        next if overlapping < @fusion_pool_ids.length
        raise PlayerFusionMappingError,
              "custom fusion #{species_id} has no disjoint reverse partner"
      end
    end

    def build_fusion_pairs_for_limit(maximum_pair_difference)
      16.times do |attempt|
        @preparation_stage = :strength_order
        ordered = strength_ordered_fusion_ids(attempt)
        pairs = []
        pairing_failed = false
        @preparation_stage = :strength_pairing
        (0...ordered.length).step(2) do |position|
          @work_checkpoint.call if @work_checkpoint && (position % 32).zero?
          partner_position = strength_partner_position(
            ordered, position, true, maximum_pair_difference
          )
          partner_position ||= strength_partner_position(
            ordered, position, false, maximum_pair_difference
          )
          if !partner_position
            ensure_strength_partner_exists(ordered[position], maximum_pair_difference)
            ensure_strength_partner_exists(ordered[position + 1], maximum_pair_difference)
            repaired = repair_strength_pair(
              pairs, ordered[position], ordered[position + 1],
              maximum_pair_difference
            )
            if repaired
              next
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
          @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
          first_bst = target_bst(fusion_pair[0])
          second_bst = target_bst(fusion_pair[1])
          (first_bst - second_bst).abs > maximum_pair_difference
        end
        next if invalid_pair
        install_fusion_pairs(pairs)
        return
      end
      failure_class = @schema_version == 1 ? PlayerFusionMappingError : PlayerFusionStrengthPairingError
      raise failure_class,
            "the custom fusion pool could not form strength-matched pairs"
    end

    def strength_ordered_fusion_ids(attempt)
      buckets = {}
      @fusion_pool_ids.each_with_index do |species_id, index|
        @work_checkpoint.call if @work_checkpoint && (index % 4).zero?
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

    def ensure_strength_partner_exists(species_id, maximum)
      nearest_difference = nil
      found = @fusion_pool_ids.each_with_index.any? do |candidate, index|
        @work_checkpoint.call if @work_checkpoint && (index % 32).zero?
        next false if shares_component?(species_id, candidate)
        difference = (target_bst(species_id) - target_bst(candidate)).abs
        nearest_difference = difference if !nearest_difference || difference < nearest_difference
        difference <= maximum
      end
      return if found
      failure_class = @schema_version == 1 ? PlayerFusionMappingError : PlayerFusionStrengthPairingError
      raise failure_class,
            "custom fusion #{species_id} (#{target_bst(species_id)} BST) " +
              "has no disjoint reverse partner within #{maximum} BST" +
              (nearest_difference ? "; the nearest requires #{nearest_difference} BST" : "")
    end

    def repair_strength_pair(pairs, current_first, current_second,
                             maximum_pair_difference)
      @preparation_stage = :strength_repair
      best = nil
      pairs.each_with_index do |previous, pair_index|
        @work_checkpoint.call if @work_checkpoint && (pair_index % 4).zero?
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
      return repair_strength_chain(
        pairs, current_first, current_second, maximum_pair_difference
      ) if !best
      pairs.delete_at(best[1])
      pairs.concat(best[2])
      return true
    end

    def repair_strength_chain(pairs, current_first, current_second, maximum)
      @preparation_stage = :strength_repair_chain
      candidates_by_bst = {}
      pairs.each_with_index do |pair, index|
        @work_checkpoint.call if @work_checkpoint && (index % 4).zero?
        [pair, pair.reverse].each_with_index do |(partner, displaced), orientation|
          bst = target_bst(partner)
          candidates_by_bst[bst] ||= []
          candidates_by_bst[bst] << [index, orientation, partner, displaced]
        end
      end
      queue = [[current_first, [], []]]
      visited = { current_first => true }
      position = 0
      while position < queue.length
        @work_checkpoint.call if @work_checkpoint
        current, replacements, removed = queue[position]
        position += 1
        bst = target_bst(current)
        candidates = ((bst - maximum)..(bst + maximum)).flat_map do |value|
          candidates_by_bst[value] || []
        end.sort_by { |entry| entry[0] * 2 + entry[1] }
        candidates.each_with_index do |candidate, candidate_index|
          @work_checkpoint.call if @work_checkpoint &&
            (candidate_index % 4).zero?
          index, _orientation, partner, displaced = candidate
          next if removed.include?(index)
          next if visited[displaced] ||
            !strength_pair_compatible?(current, partner, maximum)
          if strength_pair_compatible?(displaced, current_second, maximum)
            (removed + [index]).sort.reverse_each do |removed_index|
              pairs.delete_at(removed_index)
            end
            pairs.concat(replacements + [
              [current, partner], [displaced, current_second]
            ])
            return true
          end
          visited[displaced] = true
          queue << [displaced, replacements + [[current, partner]],
                    removed + [index]]
        end
      end
      return false
    end

    def strength_pair_compatible?(first, second, maximum)
      return (target_bst(first) - target_bst(second)).abs <= maximum &&
        !shares_component?(first, second)
    end

    def maximum_fusion_range_width
      return @maximum_fusion_range_width if @maximum_fusion_range_width
      @preparation_stage = :maximum_range
      values = []
      (1..NB_POKEMON).each do |species_id|
        @work_checkpoint.call if @work_checkpoint &&
          (species_id % 4).zero?
        values << normal_bst(GameData::Species.get(species_id))
      end
      values.uniq!
      maximum = 0
      values.each_with_index do |first_bst, index|
        @work_checkpoint.call if @work_checkpoint && (index % 4).zero?
        values.each_with_index do |second_bst, second_index|
          @work_checkpoint.call if @work_checkpoint &&
            (second_index % 32).zero?
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
      if species_id.is_a?(Integer) && @fusion_pool_membership
        return species_id if species_id > NB_POKEMON &&
          species_id < Settings::ZAPMOLCUNO_NB &&
          @fusion_pool_membership[species_id]
        raise PlayerFusionMappingError,
              "a player fusion mapping is no longer a valid custom fusion"
      end
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
    generator_version = player_fusion_generator_version
    if player_fusion_preparation_matches?(seed, state.object_id)
      finish_player_fusion_preparation
    elsif @player_fusion_preparation_fiber
      clear_player_fusion_preparation
    end
    if !@player_fusion_mapper || @player_fusion_mapper_seed != seed ||
       @player_fusion_mapper_state_id != state.object_id ||
       @player_fusion_mapper_version != generator_version
      @player_fusion_mapper = PlayerFusionMapper.new(
        seed,
        custom_fusion_pool_numbers,
        state.fusion_mappings,
        state.discovered_fusion_mappings,
        base_stat_generator,
        generator_version
      )
      @player_fusion_mapper_seed = seed
      @player_fusion_mapper_state_id = state.object_id
      @player_fusion_mapper_version = generator_version
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
    generator_version = player_fusion_generator_version
    return true if @player_fusion_mapper &&
      @player_fusion_mapper_seed == seed &&
      @player_fusion_mapper_state_id == state.object_id &&
      @player_fusion_mapper_version == generator_version
    return true if player_fusion_preparation_matches?(seed, state.object_id)
    mapper = PlayerFusionMapper.new(
      seed,
      custom_fusion_pool_numbers,
      state.fusion_mappings,
      state.discovered_fusion_mappings,
      base_stat_generator,
      generator_version,
      proc { Fiber.yield }
    )
    @player_fusion_preparation_mapper = mapper
    @player_fusion_preparation_seed = seed
    @player_fusion_preparation_state_id = state.object_id
    @player_fusion_preparation_version = generator_version
    @player_fusion_preparation_fiber = Fiber.new { mapper.prepare }
    @player_fusion_preparation_last_work_at = nil
    return true
  end

  def self.player_fusion_pairing_ready?
    state = pivot_state
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    return true if @player_fusion_mapper &&
      @player_fusion_mapper_seed == seed &&
      @player_fusion_mapper_state_id == state.object_id &&
      @player_fusion_mapper_version == player_fusion_generator_version &&
      @player_fusion_mapper.pairing_prepared?
    return !!(player_fusion_preparation_matches?(seed, state.object_id) &&
      @player_fusion_preparation_mapper.pairing_prepared?)
  end

  def self.import_tracker_player_fusion_pairing(assignment, work_checkpoint = nil)
    return false if !assignment.is_a?(Hash) || assignment["version"] != 1 ||
      assignment["session_id"] != game_session_id
    expected = tracker_active_fusion_assignment_recipe
    recipe = assignment["recipe"]
    return false if !expected || !recipe.is_a?(Hash) || expected != recipe
    state = pivot_state
    seed = $PokemonGlobal.ironmon_seed
    stat_generator = base_stat_generator
    import_stats = assignment["base_stats_version"] == 1 &&
      recipe["base_stat_generator_version"] == BaseStatGenerator::SCHEMA_VERSION
    if player_fusion_pairing_ready?
      stat_generator.import_prepared_stats(
        assignment["base_stats"], seed, recipe["base_stat_source_fingerprint"]
      ) if import_stats
      return true
    end
    mapper = PlayerFusionMapper.new(
      seed, custom_fusion_pool_numbers, state.fusion_mappings,
      state.discovered_fusion_mappings, stat_generator,
      player_fusion_generator_version, proc { Fiber.yield }
    )
    return false if !mapper.import_prepared_pairing(assignment["packed_pairs"], work_checkpoint)
    return false if expected != tracker_active_fusion_assignment_recipe ||
      !state.equal?(pivot_state) || !$PokemonGlobal ||
      seed != $PokemonGlobal.ironmon_seed
    stat_generator.import_prepared_stats(
      assignment["base_stats"], seed, recipe["base_stat_source_fingerprint"]
    ) if import_stats
    clear_player_fusion_preparation
    @player_fusion_preparation_mapper = mapper
    @player_fusion_preparation_seed = seed
    @player_fusion_preparation_state_id = state.object_id
    @player_fusion_preparation_version = player_fusion_generator_version
    @player_fusion_preparation_fiber = Fiber.new { mapper.prepare }
    return true
  end

  def self.player_fusion_preparation_safe?
    return true if !defined?($game_player) || !$game_player
    return false if $game_player.respond_to?(:moving?) && $game_player.moving?
    return false if defined?(Input) && Input.respond_to?(:dir4) &&
      Input.dir4 != 0
    return true
  end

  def self.advance_player_fusion_pairing
    fiber = @player_fusion_preparation_fiber
    return false if !fiber
    now = player_fusion_preparation_uptime
    return false if @player_fusion_preparation_last_work_at &&
      now >= @player_fusion_preparation_last_work_at &&
      now - @player_fusion_preparation_last_work_at < PLAYER_FUSION_PREPARATION_INTERVAL_SECONDS
    state = pivot_state
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    if !player_fusion_preparation_matches?(seed, state.object_id)
      clear_player_fusion_preparation
      return false
    end
    started = player_fusion_preparation_uptime
    @player_fusion_preparation_last_work_at = started
    deadline = started + PLAYER_FUSION_PREPARATION_BUDGET_SECONDS
    while fiber.alive? && player_fusion_preparation_uptime < deadline
      fiber.resume
    end
    install_prepared_player_fusion_mapper if !fiber.alive?
    return true
  rescue Exception => e
    echoln "Ironmon background player-fusion pairing failed: #{e.message}"
    clear_player_fusion_preparation
    return false
  ensure
    if started
      @player_fusion_preparation_last_work_at = player_fusion_preparation_uptime
    end
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

  def self.finish_player_fusion_startup_preparation
    prepare_player_fusion_pairing
    fiber = @player_fusion_preparation_fiber
    mapper = fiber ? @player_fusion_preparation_mapper : @player_fusion_mapper
    return false if !mapper
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    state_id = pivot_state.object_id
    version = player_fusion_generator_version
    while fiber && fiber.alive?
      fiber.resume
      yield mapper if block_given?
      validate_player_fusion_startup_context(mapper, fiber, seed, state_id, version)
    end
    index_fiber = Fiber.new do
      mapper.prepare_startup_result_index(proc { Fiber.yield })
    end
    while index_fiber.alive?
      index_fiber.resume
      yield mapper if block_given?
      validate_player_fusion_startup_context(mapper, fiber, seed, state_id, version)
    end
    validate_player_fusion_startup_context(mapper, fiber, seed, state_id, version)
    if fiber
      install_prepared_player_fusion_mapper
    else
      mapper.finish_cooperative_preparation
    end
    yield mapper if block_given?
    return true
  rescue Exception
    clear_player_fusion_preparation if @player_fusion_preparation_mapper.equal?(mapper)
    raise
  end

  def self.validate_player_fusion_startup_context(mapper, fiber, seed, state_id, version)
    current_seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    valid = current_seed == seed && pivot_state.object_id == state_id &&
      player_fusion_generator_version == version
    valid &&= fiber ? @player_fusion_preparation_fiber.equal?(fiber) &&
      @player_fusion_preparation_mapper.equal?(mapper) : @player_fusion_mapper.equal?(mapper)
    if !valid
      raise PlayerFusionMappingError, "The loaded run changed during startup preparation"
    end
    return true
  end

  def self.player_fusion_preparation_matches?(seed, state_id)
    return @player_fusion_preparation_fiber &&
      @player_fusion_preparation_seed == seed &&
      @player_fusion_preparation_state_id == state_id &&
      @player_fusion_preparation_version == player_fusion_generator_version
  end

  def self.install_prepared_player_fusion_mapper
    @player_fusion_mapper =
      @player_fusion_preparation_mapper.finish_cooperative_preparation
    @player_fusion_mapper_seed = @player_fusion_preparation_seed
    @player_fusion_mapper_state_id = @player_fusion_preparation_state_id
    @player_fusion_mapper_version = @player_fusion_preparation_version
    clear_player_fusion_preparation
  end

  def self.clear_player_fusion_preparation
    @player_fusion_preparation_mapper = nil
    @player_fusion_preparation_seed = nil
    @player_fusion_preparation_state_id = nil
    @player_fusion_preparation_version = nil
    @player_fusion_preparation_fiber = nil
    @player_fusion_preparation_last_work_at = nil
  end

  def self.player_fusion_preparation_uptime
    return System.uptime.to_f / PLAYER_FUSION_UPTIME_UNITS_PER_SECOND
  end

  def self.reset_player_fusion_mapper_cache
    @player_fusion_mapper = nil
    @player_fusion_mapper_seed = nil
    @player_fusion_mapper_state_id = nil
    @player_fusion_mapper_version = nil
    clear_player_fusion_preparation
  end
end
