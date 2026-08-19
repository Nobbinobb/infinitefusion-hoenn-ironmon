#===============================================================================
# Ironmon base-stat readiness and saved-run compatibility
#===============================================================================

module Ironmon
  def self.prepare_base_stat_randomization
    @base_stat_randomization_ready = false
    raise BaseStatRandomizationError, "run metadata is unavailable" if
      !$PokemonGlobal
    validate_base_stat_sources
    record_generator_metadata({
      :ironmon_base_stat_generator_version => BaseStatGenerator::SCHEMA_VERSION,
      :ironmon_base_stat_source_fingerprint => base_stat_source_fingerprint
    })
    reset_base_stat_generator_cache
    base_stat_generator
    @base_stat_randomization_ready = true
    @base_stat_randomization_error_message = nil
    return true
  rescue BaseStatRandomizationError => e
    @base_stat_randomization_error_message = _INTL(
      "Ironmon could not prepare base-stat randomization: {1}", e.message
    )
    echoln @base_stat_randomization_error_message
    return false
  rescue Exception => e
    @base_stat_randomization_error_message = _INTL(
      "Ironmon could not prepare base-stat randomization because of an unexpected error: {1}",
      e.message
    )
    echoln @base_stat_randomization_error_message
    return false
  end

  def self.current_base_stat_randomization?
    return false if !$PokemonGlobal
    return generator_metadata_matches?([
      ["generator schema",
       $PokemonGlobal.ironmon_base_stat_generator_version,
       BaseStatGenerator::SCHEMA_VERSION],
      ["source catalog",
       $PokemonGlobal.ironmon_base_stat_source_fingerprint,
       base_stat_source_fingerprint]
    ])
  rescue Exception
    return false
  end

  def self.ensure_base_stat_randomization
    @base_stat_randomization_ready = false
    return false if !$PokemonGlobal
    if generator_metadata_absent?([
         :ironmon_base_stat_generator_version,
         :ironmon_base_stat_source_fingerprint
       ])
      reset_base_stat_generator_cache
      echoln "Ironmon retained original base stats for a pre-Step-3.2 run."
      return true
    end
    if !current_base_stat_randomization?
      raise BaseStatRandomizationError,
            "the saved base-stat generator or source data is incompatible"
    end
    reset_base_stat_generator_cache
    base_stat_generator
    @base_stat_randomization_ready = true
    return true
  end

  def self.base_stat_randomization_active?
    return false if !$PokemonGlobal
    return false if $PokemonGlobal.ironmon_mode != true
    return @base_stat_randomization_ready == true
  end

  def self.base_stat_generator
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    if !@base_stat_generator || @base_stat_generator_seed != seed
      @base_stat_generator_seed = seed
      @base_stat_generator = BaseStatGenerator.new(
        seed, base_stat_source_fingerprint
      )
    end
    return @base_stat_generator
  end

  def self.base_stat_generator_for(seed, fingerprint)
    if fingerprint != base_stat_source_fingerprint
      raise BaseStatRandomizationError,
            "the requested base-stat source data is incompatible"
    end
    return BaseStatGenerator.new(seed, fingerprint)
  end

  def self.reset_base_stat_generator_cache
    @base_stat_generator = nil
    @base_stat_generator_seed = nil
  end

  def self.suspend_base_stat_randomization
    @base_stat_randomization_ready = false
    reset_base_stat_generator_cache
  end

  def self.base_stat_randomization_error_message
    return @base_stat_randomization_error_message ||
      _INTL("Ironmon could not prepare base-stat randomization.")
  end
end
