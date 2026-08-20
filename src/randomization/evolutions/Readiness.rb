#===============================================================================
# Ironmon evolution metadata, readiness, and generator lifecycle
#===============================================================================

module Ironmon
  EVOLUTION_METADATA_FIELDS = [
    :ironmon_evolution_generator_version,
    :ironmon_evolution_rules_version,
    :ironmon_evolution_source_fingerprint,
    :ironmon_evolution_taxonomy_fingerprint,
    :ironmon_evolution_method_fingerprint,
    :ironmon_evolution_target_fingerprint,
    :ironmon_evolution_base_stat_generator_version,
    :ironmon_evolution_base_stat_source_fingerprint,
    :ironmon_evolution_fusion_generator_version,
    :ironmon_evolution_fusion_rules_version,
    :ironmon_evolution_fusion_target_pool_version,
    :ironmon_evolution_fusion_target_pool_size,
    :ironmon_evolution_fusion_target_pool_fingerprint
  ].freeze

  def self.prepare_evolution_randomization
    @evolution_randomization_ready = false
    raise EvolutionRandomizationError, "run metadata is unavailable" if
      !$PokemonGlobal
    validate_evolution_catalogs
    if !current_base_stat_randomization?
      raise EvolutionRandomizationError,
            "base-stat generator metadata is unavailable or incompatible"
    end
    catalog = evolution_catalog
    fusion_pool = custom_fusion_pool_info
    record_generator_metadata(
      evolution_metadata_values(catalog, fusion_pool)
    )
    reset_evolution_generator_cache
    evolution_generator.graph
    @evolution_randomization_ready = true
    @evolution_randomization_error_message = nil
    return true
  rescue EvolutionRandomizationError => e
    @evolution_randomization_error_message = _INTL(
      "Ironmon could not prepare evolution randomization: {1}", e.message
    )
    echoln @evolution_randomization_error_message
    return false
  rescue Exception => e
    @evolution_randomization_error_message = _INTL(
      "Ironmon could not prepare evolution randomization because of an unexpected error: {1}",
      e.message
    )
    echoln @evolution_randomization_error_message
    return false
  end

  def self.evolution_metadata_values(catalog = evolution_catalog,
                                     fusion_pool = custom_fusion_pool_info)
    return {
      :ironmon_evolution_generator_version =>
        NormalEvolutionGenerator::SCHEMA_VERSION,
      :ironmon_evolution_rules_version =>
        NormalEvolutionGenerator::RULES_VERSION,
      :ironmon_evolution_source_fingerprint => catalog.source_fingerprint,
      :ironmon_evolution_taxonomy_fingerprint => catalog.taxonomy_fingerprint,
      :ironmon_evolution_method_fingerprint => catalog.method_fingerprint,
      :ironmon_evolution_target_fingerprint =>
        catalog.normal_target_fingerprint,
      :ironmon_evolution_base_stat_generator_version =>
        BaseStatGenerator::SCHEMA_VERSION,
      :ironmon_evolution_base_stat_source_fingerprint =>
        base_stat_source_fingerprint,
      :ironmon_evolution_fusion_generator_version =>
        FusionEvolutionGenerator::SCHEMA_VERSION,
      :ironmon_evolution_fusion_rules_version =>
        FusionEvolutionGenerator::RULES_VERSION,
      :ironmon_evolution_fusion_target_pool_version =>
        fusion_pool[:schema_version],
      :ironmon_evolution_fusion_target_pool_size => fusion_pool[:size],
      :ironmon_evolution_fusion_target_pool_fingerprint =>
        fusion_pool[:fingerprint]
    }
  end

  def self.current_evolution_randomization?
    return false if !$PokemonGlobal
    return evolution_metadata_mismatch.nil?
  rescue Exception
    return false
  end

  def self.legacy_evolution_randomization?
    return false if !$PokemonGlobal
    return generator_metadata_absent?(EVOLUTION_METADATA_FIELDS)
  end

  def self.ensure_evolution_randomization
    @evolution_randomization_ready = false
    return false if !$PokemonGlobal
    if legacy_evolution_randomization?
      reset_evolution_generator_cache
      echoln "Ironmon retained native evolutions for a pre-Step-3.4 run."
      return true
    end
    legacy_version = legacy_generated_evolution_rules_version
    if legacy_version
      if !prepare_evolution_randomization
        raise EvolutionRandomizationError,
              evolution_randomization_error_message
      end
      echoln "Ironmon upgraded saved evolution rules from version #{legacy_version} to version #{NormalEvolutionGenerator::RULES_VERSION}."
      return true
    end
    if !current_evolution_randomization?
      mismatch = evolution_metadata_mismatch
      raise EvolutionRandomizationError,
            "the saved evolution #{mismatch} is incompatible"
    end
    reset_evolution_generator_cache
    evolution_generator.graph
    @evolution_randomization_ready = true
    return true
  end

  def self.legacy_generated_evolution_rules_version
    return nil if !$PokemonGlobal
    return legacy_evolution_metadata_version(
      $PokemonGlobal.ironmon_evolution_generator_version,
      $PokemonGlobal.ironmon_evolution_rules_version,
      $PokemonGlobal.ironmon_evolution_source_fingerprint,
      $PokemonGlobal.ironmon_evolution_taxonomy_fingerprint,
      $PokemonGlobal.ironmon_evolution_method_fingerprint,
      $PokemonGlobal.ironmon_evolution_target_fingerprint,
      $PokemonGlobal.ironmon_evolution_base_stat_generator_version,
      $PokemonGlobal.ironmon_evolution_base_stat_source_fingerprint,
      $PokemonGlobal.ironmon_evolution_fusion_generator_version,
      $PokemonGlobal.ironmon_evolution_fusion_rules_version,
      $PokemonGlobal.ironmon_evolution_fusion_target_pool_version,
      $PokemonGlobal.ironmon_evolution_fusion_target_pool_size,
      $PokemonGlobal.ironmon_evolution_fusion_target_pool_fingerprint
    )
  end

  def self.legacy_evolution_metadata_version(generator_version, rules_version, source_fingerprint, taxonomy_fingerprint, method_fingerprint, target_fingerprint, stat_version, stat_fingerprint, fusion_generator_version, fusion_rules_version, fusion_pool_version, fusion_pool_size, fusion_pool_fingerprint)
    catalog = evolution_catalog
    fusion_pool = custom_fusion_pool_info
    actual = [generator_version, rules_version, source_fingerprint,
              taxonomy_fingerprint, method_fingerprint, target_fingerprint,
              stat_version, stat_fingerprint, fusion_generator_version,
              fusion_rules_version, fusion_pool_version, fusion_pool_size,
              fusion_pool_fingerprint]
    [1, 2].each do |version|
      legacy = catalog.fingerprints_for_rules(version)
      expected = [
        NormalEvolutionGenerator::SCHEMA_VERSION, version, legacy[:source],
        legacy[:taxonomy], legacy[:method], legacy[:target],
        BaseStatGenerator::SCHEMA_VERSION, base_stat_source_fingerprint,
        FusionEvolutionGenerator::SCHEMA_VERSION, version,
        fusion_pool[:schema_version], fusion_pool[:size],
        fusion_pool[:fingerprint]
      ]
      return version if actual == expected
    end
    return nil
  end

  def self.evolution_randomization_active?
    return false if !$PokemonGlobal
    return false if $PokemonGlobal.ironmon_mode != true
    return @evolution_randomization_ready == true
  end

  def self.evolution_generator
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    catalog = evolution_catalog
    key = [
      seed, catalog.source_fingerprint, catalog.taxonomy_fingerprint,
      catalog.method_fingerprint, catalog.normal_target_fingerprint,
      BaseStatGenerator::SCHEMA_VERSION, base_stat_source_fingerprint
    ]
    if !@evolution_generator || @evolution_generator_key != key
      @evolution_generator_key = key
      @evolution_generator = NormalEvolutionGenerator.new(
        seed, catalog, BaseStatGenerator::SCHEMA_VERSION,
        base_stat_source_fingerprint
      )
    end
    return @evolution_generator
  end

  def self.evolution_generator_for(seed, source_fingerprint, taxonomy_fingerprint, method_fingerprint, target_fingerprint, stat_version, stat_fingerprint)
    catalog = evolution_catalog
    checks = [
      ["source catalog", source_fingerprint, catalog.source_fingerprint],
      ["taxonomy catalog", taxonomy_fingerprint,
       catalog.taxonomy_fingerprint],
      ["method catalog", method_fingerprint, catalog.method_fingerprint],
      ["normal-target catalog", target_fingerprint,
       catalog.normal_target_fingerprint],
      ["base-stat generator", stat_version,
       BaseStatGenerator::SCHEMA_VERSION],
      ["base-stat source catalog", stat_fingerprint,
       base_stat_source_fingerprint]
    ]
    mismatch = generator_metadata_mismatch(checks)
    if mismatch
      raise EvolutionRandomizationError,
            "the requested evolution #{mismatch} is incompatible"
    end
    return NormalEvolutionGenerator.new(
      seed, catalog, stat_version, stat_fingerprint
    )
  end

  def self.generated_normal_evolution_branches_for(source)
    return [] if !evolution_randomization_active?
    return evolution_generator.branches_for(source)
  end

  def self.reset_evolution_generator_cache
    @evolution_generator = nil
    @evolution_generator_key = nil
    reset_fusion_evolution_generator_cache if
      respond_to?(:reset_fusion_evolution_generator_cache)
  end

  def self.suspend_evolution_randomization
    @evolution_randomization_ready = false
    reset_evolution_generator_cache
    clear_pending_generated_evolutions if
      respond_to?(:clear_pending_generated_evolutions)
  end

  def self.evolution_randomization_error_message
    return @evolution_randomization_error_message ||
      _INTL("Ironmon could not prepare evolution randomization.")
  end

  def self.evolution_metadata_mismatch
    catalog = evolution_catalog
    fusion_pool = custom_fusion_pool_info
    expected = evolution_metadata_values(catalog, fusion_pool)
    checks = [
      ["generator schema",
       $PokemonGlobal.ironmon_evolution_generator_version,
       expected[:ironmon_evolution_generator_version]],
      ["rules version", $PokemonGlobal.ironmon_evolution_rules_version,
       expected[:ironmon_evolution_rules_version]],
      ["source catalog", $PokemonGlobal.ironmon_evolution_source_fingerprint,
       expected[:ironmon_evolution_source_fingerprint]],
      ["taxonomy catalog",
       $PokemonGlobal.ironmon_evolution_taxonomy_fingerprint,
       expected[:ironmon_evolution_taxonomy_fingerprint]],
      ["method catalog", $PokemonGlobal.ironmon_evolution_method_fingerprint,
       expected[:ironmon_evolution_method_fingerprint]],
      ["normal-target catalog",
       $PokemonGlobal.ironmon_evolution_target_fingerprint,
       expected[:ironmon_evolution_target_fingerprint]],
      ["base-stat generator",
       $PokemonGlobal.ironmon_evolution_base_stat_generator_version,
       expected[:ironmon_evolution_base_stat_generator_version]],
      ["base-stat source catalog",
       $PokemonGlobal.ironmon_evolution_base_stat_source_fingerprint,
       expected[:ironmon_evolution_base_stat_source_fingerprint]],
      ["fusion generator schema",
       $PokemonGlobal.ironmon_evolution_fusion_generator_version,
       expected[:ironmon_evolution_fusion_generator_version]],
      ["fusion rules version",
       $PokemonGlobal.ironmon_evolution_fusion_rules_version,
       expected[:ironmon_evolution_fusion_rules_version]],
      ["fusion target-pool schema",
       $PokemonGlobal.ironmon_evolution_fusion_target_pool_version,
       expected[:ironmon_evolution_fusion_target_pool_version]],
      ["fusion target-pool size",
       $PokemonGlobal.ironmon_evolution_fusion_target_pool_size,
       expected[:ironmon_evolution_fusion_target_pool_size]],
      ["fusion target-pool catalog",
       $PokemonGlobal.ironmon_evolution_fusion_target_pool_fingerprint,
       expected[:ironmon_evolution_fusion_target_pool_fingerprint]]
    ]
    return generator_metadata_mismatch(checks)
  end
end

Ironmon.register_game_load_hook(
  :evolution_randomization,
  proc { |_save_data| Ironmon.suspend_evolution_randomization },
  proc do |_save_data, _result|
    next if Ironmon.checkpoint_reset_loading?
    Ironmon.ensure_evolution_randomization if Ironmon.active?
  end
)
