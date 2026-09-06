#===============================================================================
# Deterministic wild and trainer species generators
#===============================================================================

module Ironmon
  class SpeciesGenerationError < StandardError; end

  class SpeciesGenerator
    SCHEMA_VERSION = 2
    SLOT_SCHEMA_VERSIONS = [1, SCHEMA_VERSION].freeze
    SUPPORTED_SCHEMA_VERSIONS = SLOT_SCHEMA_VERSIONS
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

    def map(species, context, fully_evolved = false)
      species_data = GameData::Species.try_get(species)
      return species if !species_data
      source_id = species_data.id_number
      return species if source_id <= 0
      return species if source_id >= Settings::ZAPMOLCUNO_NB
      mapped_id = map_id(source_id, context, fully_evolved)
      mapped_species = GameData::Species.try_get(mapped_id)
      return mapped_species ? mapped_species.id : species
    rescue Exception => e
      echoln "Ironmon species mapping failed for #{species}: #{e.message}"
      return species
    end

    def map_id(source_id, context, fully_evolved = false)
      fully_evolved = fully_evolved && @schema_version >= 2
      key = mapping_key(source_id, context, fully_evolved)
      stored = @mapping[key]
      if stored
        return stored if allowed_species_id?(stored, fully_evolved)
        @mapping.delete(key)
      end
      pool = select_pool(source_id, context, fully_evolved)
      if !pool || pool.empty?
        raise SpeciesGenerationError,
              "the #{@namespace} #{@policy} species pool is empty"
      end
      purpose = fully_evolved ? "fully_evolved_species" : "species"
      selected = pool[deterministic_value(source_id, context, purpose) % pool.length]
      @mapping[key] = species_number(selected)
      return @mapping[key]
    end

    def map_number(species, context, fully_evolved = false)
      return map_id(species_number(species), context, fully_evolved)
    end

    private

    def allowed_species_id?(species_id, fully_evolved)
      normal_pool = fully_evolved ?
        Ironmon.fully_evolved_normal_species_pool : @normal_pool
      fusion_pool = fully_evolved ?
        Ironmon.fully_evolved_custom_fusion_pool : @fusion_pool
      case @policy
      when Configuration::POLICY_NORMAL_ONLY
        return normal_pool_index(normal_pool, fully_evolved).key?(species_id)
      when Configuration::POLICY_CUSTOM_FUSIONS_ONLY
        return fusion_pool_index(fusion_pool, fully_evolved).key?(species_id)
      else
        return normal_pool_index(normal_pool, fully_evolved).key?(species_id) ||
               fusion_pool_index(fusion_pool, fully_evolved).key?(species_id)
      end
    end

    def normal_pool_index(pool, fully_evolved)
      variable = fully_evolved ? :@fully_evolved_normal_pool_index :
        :@normal_pool_index
      index = instance_variable_get(variable)
      if !index
        index = pool_index(pool)
        instance_variable_set(variable, index)
      end
      return index
    end

    def fusion_pool_index(pool, fully_evolved)
      variable = fully_evolved ? :@fully_evolved_fusion_pool_index :
        :@fusion_pool_index
      index = instance_variable_get(variable)
      if !index
        index = pool_index(pool)
        instance_variable_set(variable, index)
      end
      return index
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

    def select_pool(source_id, context, fully_evolved)
      normal_pool = fully_evolved ?
        Ironmon.fully_evolved_normal_species_pool : @normal_pool
      fusion_pool = fully_evolved ?
        Ironmon.fully_evolved_custom_fusion_pool : @fusion_pool
      case @policy
      when Configuration::POLICY_NORMAL_ONLY
        return normal_pool
      when Configuration::POLICY_CUSTOM_FUSIONS_ONLY
        return fusion_pool
      else
        category = deterministic_category(source_id, context)
        return category == 0 ? normal_pool : fusion_pool
      end
    end

    def deterministic_category(source_id, context)
      value = deterministic_value(source_id, context, "category")
      value ^= value >> 30
      value = (value * MIX_MULTIPLIER_ONE) & Ironmon::FNV1A_64_MASK
      value ^= value >> 27
      value = (value * MIX_MULTIPLIER_TWO) & Ironmon::FNV1A_64_MASK
      value ^= value >> 31
      return value % 2
    end

    def mapping_key(source_id, context, fully_evolved)
      normalized = context.is_a?(Array) ? context : [context]
      key = [@schema_version, @namespace, *normalized, source_id]
      key << :fully_evolved if fully_evolved
      return key
    end

    def deterministic_value(source_id, context, purpose)
      normalized = context.is_a?(Array) ? context : [context]
      return Ironmon.fnv1a_64_joined(
        [@schema_version, @seed, @namespace, *normalized, source_id, purpose]
      )
    end
  end
end
