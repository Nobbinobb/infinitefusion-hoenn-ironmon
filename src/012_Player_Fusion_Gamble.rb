#===============================================================================
# Deterministic player-fusion gamble mapping and discovery tracking
#===============================================================================

module Ironmon
  class PlayerFusionMappingError < StandardError; end

  class PlayerFusionMapper
    SCHEMA_VERSION = 2
    NAMESPACE = "player_fusion"
    FNV_OFFSET_BASIS = 14_695_981_039_346_656_037
    FNV_PRIME = 1_099_511_628_211
    FNV_MASK = 0xFFFFFFFFFFFFFFFF

    def initialize(seed, fusion_pool, mappings, discoveries)
      @seed = seed.to_i
      @fusion_pool = fusion_pool
      @mappings = mappings
      @discoveries = discoveries
      @fusion_pool_ids = nil
      @fusion_components = nil
      @fusion_pairs = nil
      @paired_result_ids = nil
      @material_pairs = {}
    end

    def species(body_species, head_species)
      body_id, head_id = normal_input_ids(body_species, head_species)
      pair = [body_id, head_id].sort
      result_ids = mapped_result_ids(pair)
      oriented_id = body_id <= head_id ? result_ids[0] : result_ids[1]
      validate_result_id(oriented_id)
      return GameData::Species.get(oriented_id).id
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
      pairs = []
      (1..NB_POKEMON).each do |first_id|
        (first_id..NB_POKEMON).each do |second_id|
          pair_index = deterministic_value("result", first_id, second_id) %
                       @fusion_pairs.length
          result_ids = @fusion_pairs[pair_index]
          pairs << [first_id, second_id] if result_ids[0] == species_id
          if first_id != second_id && result_ids[1] == species_id
            pairs << [second_id, first_id]
          end
        end
      end
      @material_pairs[species_id] = pairs.freeze
      return @material_pairs[species_id]
    end

    def prepare
      ensure_fusion_pool
      return true
    end

    private

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
      pair_index = deterministic_value("result", pair[0], pair[1]) %
                   @fusion_pairs.length
      first_id, second_id = @fusion_pairs[pair_index]
      @mappings[key] = [validate_result_id(first_id),
                        validate_result_id(second_id)]
      return @mappings[key]
    end

    def pair_key(pair)
      return "#{pair[0]}:#{pair[1]}"
    end

    def deterministic_value(*parts)
      value = FNV_OFFSET_BASIS
      input = [SCHEMA_VERSION, @seed, NAMESPACE, *parts].join("|")
      input.each_byte do |byte|
        value ^= byte
        value = (value * FNV_PRIME) & FNV_MASK
      end
      return value
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
      @fusion_pool_ids = @fusion_pool.map do |species|
        match = /\AB(\d+)H(\d+)\z/.match(species.to_s)
        if !match
          raise PlayerFusionMappingError,
                "the custom fusion pool contains an invalid identifier"
        end
        body_id = match[1].to_i
        head_id = match[2].to_i
        species_id = (body_id * NB_POKEMON) + head_id
        @fusion_components[species_id] = [body_id, head_id]
        species_id
      end
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
          partner_position = position + 1
          partner_position += 1 while partner_position < shuffled.length &&
            shares_component?(shuffled[position], shuffled[partner_position])
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
      state = FNV_OFFSET_BASIS if state == 0
      (shuffled.length - 1).downto(1) do |index|
        state ^= (state << 13) & FNV_MASK
        state ^= state >> 7
        state ^= (state << 17) & FNV_MASK
        state &= FNV_MASK
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
        state.discovered_fusion_mappings
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
