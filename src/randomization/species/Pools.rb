#===============================================================================
# Ironmon species pools and generator construction
#===============================================================================

module Ironmon
  def self.normal_species_pool
    if !@normal_species_pool
      pool = []
      (1..NB_POKEMON).each do |species_id|
        species = GameData::Species.try_get(species_id)
        pool << species.id if species && species.id_number == species_id
      end
      if pool.empty?
        raise SpeciesGenerationError, "the normal species pool is empty"
      end
      @normal_species_pool = pool.freeze
    end
    return @normal_species_pool
  end

  def self.stored_species_mapping(kind)
    return {} if !$PokemonGlobal
    if kind == :wild
      mapping = $PokemonGlobal.ironmon_wild_species_map
      if !mapping.is_a?(Hash)
        mapping = {}
        $PokemonGlobal.ironmon_wild_species_map = mapping
      end
      return mapping
    end
    mapping = $PokemonGlobal.ironmon_trainer_species_map
    if !mapping.is_a?(Hash)
      mapping = {}
      $PokemonGlobal.ironmon_trainer_species_map = mapping
    end
    return mapping
  end

  def self.species_generator(kind)
    configuration_value = configuration
    policy = kind == :wild ? configuration_value.wild_policy :
      configuration_value.trainer_policy
    variable = kind == :wild ? :@wild_species_generator :
      :@trainer_species_generator
    generator = instance_variable_get(variable)
    if !generator
      schema_version = if $PokemonGlobal &&
                          SpeciesGenerator::SLOT_SCHEMA_VERSIONS.include?(
                            $PokemonGlobal.ironmon_species_generator_version
                          )
                         $PokemonGlobal.ironmon_species_generator_version
                       else
                         SpeciesGenerator::SCHEMA_VERSION
                       end
      generator = SpeciesGenerator.new(
        $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0,
        kind,
        policy,
        normal_species_pool,
        custom_fusion_pool,
        stored_species_mapping(kind),
        schema_version
      )
      instance_variable_set(variable, generator)
    end
    return generator
  end
end
