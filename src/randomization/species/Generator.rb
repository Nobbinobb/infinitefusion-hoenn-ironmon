#===============================================================================
# Deterministic wild and trainer species generators
#===============================================================================

module Ironmon
  class SpeciesGenerationError < StandardError; end

  class SpeciesGenerator
    SCHEMA_VERSION = 3
    PREVIOUS_SCHEMA_VERSION = 2
    LEGACY_SCHEMA_VERSION = 1
    SLOT_SCHEMA_VERSIONS = [PREVIOUS_SCHEMA_VERSION, SCHEMA_VERSION].freeze
    SUPPORTED_SCHEMA_VERSIONS = [LEGACY_SCHEMA_VERSION,
                                 *SLOT_SCHEMA_VERSIONS].freeze
    MIX_MULTIPLIER_ONE = 0xBF58476D1CE4E5B9
    MIX_MULTIPLIER_TWO = 0x94D049BB133111EB

    attr_reader :mapping

    def initialize(seed, namespace, policy, normal_pool, fusion_pool, mapping,
                   schema_version = SCHEMA_VERSION)
      @seed = seed.to_i
      @namespace = namespace.to_s
      @policy = policy
      @normal_pool = normal_pool
      @fusion_pool = fusion_pool
      @mapping = mapping || {}
      @schema_version = schema_version.to_i
      if !SLOT_SCHEMA_VERSIONS.include?(@schema_version)
        raise SpeciesGenerationError,
              "species generator schema #{@schema_version} is unsupported"
      end
    end

    def map(species, context)
      species_data = GameData::Species.try_get(species)
      return species if !species_data
      source_id = species_data.id_number
      return species if source_id <= 0
      return species if source_id >= Settings::ZAPMOLCUNO_NB
      mapped_id = map_id(source_id, context)
      mapped_species = GameData::Species.try_get(mapped_id)
      return mapped_species ? mapped_species.id : species
    rescue Exception => e
      echoln "Ironmon species mapping failed for #{species}: #{e.message}"
      return species
    end

    def map_id(source_id, context)
      key = mapping_key(source_id, context)
      stored = @mapping[key]
      if stored
        return stored if allowed_species_id?(stored)
        @mapping.delete(key)
      end
      pool = select_pool(source_id, context)
      if !pool || pool.empty?
        raise SpeciesGenerationError,
              "the #{@namespace} #{@policy} species pool is empty"
      end
      selected = pool[deterministic_value(source_id, context, "species") % pool.length]
      @mapping[key] = species_number(selected)
      return @mapping[key]
    end

    def map_number(species, context)
      return map_id(species_number(species), context)
    end

    private

    def allowed_species_id?(species_id)
      case @policy
      when Configuration::POLICY_NORMAL_ONLY
        @normal_pool_index ||= pool_index(@normal_pool)
        return @normal_pool_index.key?(species_id)
      when Configuration::POLICY_CUSTOM_FUSIONS_ONLY
        @fusion_pool_index ||= pool_index(@fusion_pool)
        return @fusion_pool_index.key?(species_id)
      else
        @normal_pool_index ||= pool_index(@normal_pool)
        @fusion_pool_index ||= pool_index(@fusion_pool)
        return @normal_pool_index.key?(species_id) ||
               @fusion_pool_index.key?(species_id)
      end
    end

    def pool_index(pool)
      shared = Ironmon.cached_custom_fusion_pool_index(pool)
      return shared if shared
      index = {}
      pool.each do |species|
        index[species_number(species)] = true
      end
      return index
    end

    def species_number(species)
      return species if species.is_a?(Integer)
      match = /\AB(\d+)H(\d+)\z/.match(species.to_s)
      return (match[1].to_i * NB_POKEMON) + match[2].to_i if match
      return GameData::Species.get(species).id_number
    end

    def select_pool(source_id, context)
      case @policy
      when Configuration::POLICY_NORMAL_ONLY
        return @normal_pool
      when Configuration::POLICY_CUSTOM_FUSIONS_ONLY
        return @fusion_pool
      else
        category = deterministic_category(source_id, context)
        return category == 0 ? @normal_pool : @fusion_pool
      end
    end

    def deterministic_category(source_id, context)
      value = deterministic_value(source_id, context, "category")
      return value % 2 if @schema_version == PREVIOUS_SCHEMA_VERSION
      value ^= value >> 30
      value = (value * MIX_MULTIPLIER_ONE) & Ironmon::FNV1A_64_MASK
      value ^= value >> 27
      value = (value * MIX_MULTIPLIER_TWO) & Ironmon::FNV1A_64_MASK
      value ^= value >> 31
      return value % 2
    end

    def mapping_key(source_id, context)
      normalized = context.is_a?(Array) ? context : [context]
      return [@schema_version, @namespace, *normalized, source_id]
    end

    def deterministic_value(source_id, context, purpose)
      normalized = context.is_a?(Array) ? context : [context]
      return Ironmon.fnv1a_64_joined(
        [@schema_version, @seed, @namespace, *normalized, source_id, purpose]
      )
    end
  end
end
