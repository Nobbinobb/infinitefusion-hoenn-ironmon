#===============================================================================
# Ironmon ability pool metadata and generator readiness
#===============================================================================

module Ironmon
  def self.allowed_ability_pool
    return @allowed_ability_pool if @allowed_ability_pool
    contextual = {}
    AbilityGenerator::CONTEXTUAL_ABILITIES.each do |ability|
      contextual[ability] = true
    end
    pool = []
    GameData::Ability.each do |ability|
      next if !ability || contextual[ability.id]
      next if ability.id_number < 0
      pool << ability
    end
    pool.sort_by! { |ability| [ability.id_number, ability.id.to_s] }
    pool.map! { |ability| ability.id }
    if pool.empty?
      raise AbilityRandomizationError, "the allowed ability pool is empty"
    end
    @allowed_ability_pool = pool.freeze
    @ability_pool_fingerprint = ability_pool_fingerprint_for(pool)
    return @allowed_ability_pool
  end

  def self.ability_pool_fingerprint
    allowed_ability_pool
    return @ability_pool_fingerprint
  end

  def self.ability_pool_fingerprint_for(pool)
    entries = [AbilityGenerator::POOL_RULES_VERSION] + pool
    [AbilityGenerator::EXACT_SPECIES_ABILITY_RULES,
     AbilityGenerator::COMPONENT_ABILITY_RULES].each do |rules|
      rules.keys.sort_by { |ability| ability.to_s }.each do |ability|
        entries << ability
        rules[ability].each { |species| entries << species }
      end
    end
    return fnv1a_64_fingerprint(entries)
  end

  def self.prepare_ability_randomization
    @ability_randomization_ready = false
    raise AbilityRandomizationError, "run metadata is unavailable" if
      !$PokemonGlobal
    pool = allowed_ability_pool
    record_generator_metadata({
      :ironmon_ability_generator_version => AbilityGenerator::SCHEMA_VERSION,
      :ironmon_ability_pool_size => pool.length,
      :ironmon_ability_pool_fingerprint => ability_pool_fingerprint
    })
    reset_ability_generator_cache
    ability_generator
    @ability_randomization_ready = true
    @ability_randomization_error_message = nil
    return true
  rescue AbilityRandomizationError => e
    @ability_randomization_error_message = _INTL(
      "Ironmon could not prepare ability randomization: {1}", e.message
    )
    echoln @ability_randomization_error_message
    return false
  rescue Exception => e
    @ability_randomization_error_message = _INTL(
      "Ironmon could not prepare ability randomization because of an unexpected error: {1}",
      e.message
    )
    echoln @ability_randomization_error_message
    return false
  end

  def self.ability_metadata_checks(generator_version)
    return [
      ["generator schema",
       $PokemonGlobal.ironmon_ability_generator_version, generator_version],
      ["allowed pool size", $PokemonGlobal.ironmon_ability_pool_size,
       allowed_ability_pool.length],
      ["allowed pool catalog",
       $PokemonGlobal.ironmon_ability_pool_fingerprint,
       ability_pool_fingerprint]
    ]
  end

  def self.current_ability_randomization?
    return false if !$PokemonGlobal
    return generator_metadata_matches?(
      ability_metadata_checks(AbilityGenerator::SCHEMA_VERSION)
    )
  rescue Exception
    return false
  end

  def self.legacy_fusion_fallback_ability_randomization?
    return false if !$PokemonGlobal
    return generator_metadata_matches?(ability_metadata_checks(
      AbilityGenerator::LEGACY_FUSION_FALLBACK_SCHEMA_VERSION
    ))
  rescue Exception
    return false
  end

  def self.migrate_legacy_fusion_fallback_abilities
    record_generator_metadata({
      :ironmon_ability_generator_version => AbilityGenerator::SCHEMA_VERSION
    })
    reset_ability_generator_cache
    ability_generator
    @ability_randomization_ready = true
    if $Trainer && $Trainer.party
      $Trainer.party.each { |pokemon| normalize_ability_index(pokemon) }
    end
    echoln "Ironmon migrated fusion abilities to omit missing-slot fallbacks."
    return true
  end

  def self.ensure_ability_randomization
    @ability_randomization_ready = false
    return false if !$PokemonGlobal
    if generator_metadata_absent?([
         :ironmon_ability_generator_version,
         :ironmon_ability_pool_fingerprint
       ])
      generated = prepare_ability_randomization
      echoln "Ironmon migrated ability randomization metadata." if generated
      return generated
    end
    if legacy_fusion_fallback_ability_randomization?
      return migrate_legacy_fusion_fallback_abilities
    end
    if !current_ability_randomization?
      raise AbilityRandomizationError,
            "the saved ability generator or allowed pool is incompatible"
    end
    reset_ability_generator_cache
    ability_generator
    @ability_randomization_ready = true
    return true
  end

  def self.ability_randomization_active?
    return false if !$PokemonGlobal
    return false if $PokemonGlobal.ironmon_mode != true
    return @ability_randomization_ready == true
  end

  def self.ability_generator
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    if !@ability_generator || @ability_generator_seed != seed
      @ability_generator_seed = seed
      @ability_generator = AbilityGenerator.new(
        seed, allowed_ability_pool, ability_pool_fingerprint
      )
    end
    return @ability_generator
  end

  def self.reset_ability_generator_cache
    @ability_generator = nil
    @ability_generator_seed = nil
  end

  def self.suspend_ability_randomization
    @ability_randomization_ready = false
    reset_ability_generator_cache
  end

  def self.ability_randomization_error_message
    return @ability_randomization_error_message ||
      _INTL("Ironmon could not prepare ability randomization.")
  end
end
