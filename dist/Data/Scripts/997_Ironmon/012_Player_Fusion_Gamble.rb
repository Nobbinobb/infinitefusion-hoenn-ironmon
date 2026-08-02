#===============================================================================
# Deterministic player-fusion gamble mapping and discovery tracking
#===============================================================================

module Ironmon
  class PlayerFusionMappingError < StandardError; end

  class PlayerFusionMapper
    SCHEMA_VERSION = 1
    NAMESPACE = "player_fusion"
    FNV_OFFSET_BASIS = 14_695_981_039_346_656_037
    FNV_PRIME = 1_099_511_628_211
    FNV_MASK = 0xFFFFFFFFFFFFFFFF

    def initialize(seed, fusion_pool, mappings, discoveries)
      @seed = seed.to_i
      @fusion_pool = fusion_pool
      @mappings = mappings
      @discoveries = discoveries
    end

    def species(body_species, head_species)
      body_id, head_id = normal_input_ids(body_species, head_species)
      pair = [body_id, head_id].sort
      canonical_id = canonical_result_id(pair)
      oriented_id = body_id <= head_id ? canonical_id :
        reverse_species_id(canonical_id)
      validate_result_id(oriented_id)
      return GameData::Species.get(oriented_id).id
    end

    def known_species(body_species, head_species)
      body_id, head_id = normal_input_ids(body_species, head_species)
      pair = [body_id, head_id].sort
      stored_id = @discoveries[pair_key(pair)]
      return nil if !stored_id
      canonical_id = validate_result_id(stored_id)
      oriented_id = body_id <= head_id ? canonical_id :
        reverse_species_id(canonical_id)
      validate_result_id(oriented_id)
      return GameData::Species.get(oriented_id).id
    end

    def discover(body_species, head_species, result_species)
      body_id, head_id = normal_input_ids(body_species, head_species)
      pair = [body_id, head_id].sort
      canonical_id = canonical_result_id(pair)
      expected_id = body_id <= head_id ? canonical_id :
        reverse_species_id(canonical_id)
      actual_id = GameData::Species.get(result_species).id_number
      if actual_id != expected_id
        raise PlayerFusionMappingError,
              "the committed fusion does not match its mapped result"
      end
      @discoveries[pair_key(pair)] = canonical_id
      return true
    end

    def pair_key_for(body_species, head_species)
      return pair_key(normal_input_ids(body_species, head_species).sort)
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

    def canonical_result_id(pair)
      key = pair_key(pair)
      stored_id = @mappings[key]
      return validate_result_id(stored_id) if stored_id
      if !@fusion_pool || @fusion_pool.empty?
        raise PlayerFusionMappingError,
              "the reversible custom fusion pool is empty"
      end
      index = deterministic_value(pair) % @fusion_pool.length
      selected_id = GameData::Species.get(@fusion_pool[index]).id_number
      @mappings[key] = validate_result_id(selected_id)
      return @mappings[key]
    end

    def pair_key(pair)
      return "#{pair[0]}:#{pair[1]}"
    end

    def deterministic_value(pair)
      value = FNV_OFFSET_BASIS
      input = [SCHEMA_VERSION, @seed, NAMESPACE,
               pair[0], pair[1]].join("|")
      input.each_byte do |byte|
        value ^= byte
        value = (value * FNV_PRIME) & FNV_MASK
      end
      return value
    end

    def reverse_species_id(species_id)
      reversed = reverseFusionSpecies(GameData::Species.get(species_id).id)
      return GameData::Species.get(reversed).id_number
    end

    def validate_result_id(species_id)
      species = GameData::Species.try_get(species_id)
      if !species || species.id_number <= NB_POKEMON ||
         species.id_number >= Settings::ZAPMOLCUNO_NB ||
         !Ironmon.custom_fusion_species?(species.id)
        raise PlayerFusionMappingError,
              "a player fusion mapping is no longer a valid custom fusion"
      end
      return species.id_number
    end
  end

  def self.reversible_custom_fusion_pool
    return @reversible_custom_fusion_pool if @reversible_custom_fusion_pool
    custom_pool = custom_fusion_pool
    species_index = {}
    custom_pool.each { |species| species_index[species.to_s] = true }
    pool = custom_pool.find_all do |species|
      match = /\AB(\d+)H(\d+)\z/.match(species.to_s)
      match && species_index["B#{match[2]}H#{match[1]}"]
    end
    if pool.empty?
      raise PlayerFusionMappingError,
            "no custom fusion has a custom-sprite reverse orientation"
    end
    @reversible_custom_fusion_pool = pool.freeze
    return @reversible_custom_fusion_pool
  end

  def self.player_fusion_mapper
    state = pivot_state
    return PlayerFusionMapper.new(
      $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0,
      reversible_custom_fusion_pool,
      state.fusion_mappings,
      state.discovered_fusion_mappings
    )
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

  def self.reset_player_fusion_mapper_cache
    @reversible_custom_fusion_pool = nil
  end
end
