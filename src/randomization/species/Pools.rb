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

  def self.fully_evolved_normal_species_pool
    if !@fully_evolved_normal_species_pool
      terminal_roles = [:final, :standalone]
      pool = evolution_catalog.normal_target_catalog.select do |entry|
        terminal_roles.include?(entry[:role])
      end.map { |entry| entry[:id] }
      if pool.empty?
        raise SpeciesGenerationError,
              "the fully evolved normal species pool is empty"
      end
      @fully_evolved_normal_species_pool = pool.freeze
    end
    return @fully_evolved_normal_species_pool
  end

  def self.fully_evolved_normal_species_index
    if !@fully_evolved_normal_species_index
      @fully_evolved_normal_species_index =
        fully_evolved_normal_species_pool.each_with_object({}) do |species, index|
          index[GameData::Species.get(species).id_number] = true
        end.freeze
    end
    return @fully_evolved_normal_species_index
  end

  def self.fully_evolved_custom_fusion_pool
    if !@fully_evolved_custom_fusion_pool
      terminal = fully_evolved_normal_species_index
      pool = custom_fusion_pool.select do |species|
        match = species.to_s.match(/\AB(\d+)H(\d+)\z/)
        match && terminal.key?(match[1].to_i) && terminal.key?(match[2].to_i)
      end
      if pool.empty?
        raise SpeciesGenerationError,
              "the fully evolved custom fusion pool is empty"
      end
      @fully_evolved_custom_fusion_pool = pool.freeze
    end
    return @fully_evolved_custom_fusion_pool
  end

  def self.fully_evolved_custom_fusion_index
    if !@fully_evolved_custom_fusion_index
      @fully_evolved_custom_fusion_index =
        fully_evolved_custom_fusion_pool.each_with_object({}) do |species, index|
          index[GameData::Species.get(species).id_number] = true
        end.freeze
    end
    return @fully_evolved_custom_fusion_index
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
