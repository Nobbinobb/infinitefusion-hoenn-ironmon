#===============================================================================
# Ironmon move-access validation, metadata, and generator readiness
#===============================================================================

module Ironmon
  def self.validate_move_access_sources
    validation_key = [
      MoveAccessGenerator::SCHEMA_VERSION,
      level_up_move_pool_fingerprint,
      level_up_move_contextual_fingerprint,
      move_access_source_fingerprint,
      egg_move_access_source_fingerprint,
      machine_roster_fingerprint(:tm),
      machine_roster_fingerprint(:tr),
      machine_move_access_source_fingerprint(:tm),
      machine_move_access_source_fingerprint(:tr),
      ordinary_tutor_catalog_fingerprint,
      ordinary_tutor_source_fingerprint,
      specialized_tutor_catalog_fingerprint,
      specialized_tutor_source_fingerprint
    ]
    return true if @validated_move_access_source_key == validation_key
    validator = MoveAccessGenerator.new(
      0, allowed_level_up_move_pool, level_up_move_pool_fingerprint,
      move_access_source_fingerprint
    )
    if ORDINARY_TUTOR_SLOTS.map { |slot| slot[:id] }.uniq.length !=
       ORDINARY_TUTOR_SLOTS.length
      raise MoveAccessRandomizationError,
            "the ordinary tutor catalog contains duplicate slot IDs"
    end
    if ORDINARY_TUTOR_SLOTS.length > allowed_level_up_move_pool.length
      raise MoveAccessRandomizationError,
            "the ordinary tutor offering pool is too small"
    end
    ORDINARY_TUTOR_SLOTS.each do |slot|
      if !registered_move?(slot[:original_move])
        raise MoveAccessRandomizationError,
              "ordinary tutor slot #{slot[:id]} has an unknown source move"
      end
    end
    [:regular, :legendary].each do |channel|
      catalog = original_specialized_tutor_catalog(channel)
      if catalog.empty?
        raise MoveAccessRandomizationError,
              "the #{channel} Fusion Tutor catalog is empty"
      end
      if catalog.uniq.length != catalog.length
        raise MoveAccessRandomizationError,
              "the #{channel} Fusion Tutor catalog contains duplicates"
      end
      if catalog.length > allowed_level_up_move_pool.length
        raise MoveAccessRandomizationError,
              "the allowed Fusion Tutor move pool is too small"
      end
      catalog.each do |move|
        if !registered_move?(move)
          raise MoveAccessRandomizationError,
                "the #{channel} Fusion Tutor catalog has an unknown move"
        end
      end
    end
    move_access_source_entries.each do |identity, moves|
      validator.validate_source(identity, moves)
      species = GameData::Species.get(identity.to_sym)
      missing_level_one_entries = 4 - moves.count do |entry|
        entry[0].to_i <= 1
      end
      missing_level_one_entries = 0 if missing_level_one_entries < 0
      required_entries = moves.length + missing_level_one_entries
      if required_entries > eligible_level_up_move_pool(species).length
        raise MoveAccessRandomizationError,
              "the allowed move pool is too small for #{identity}"
      end
      if eligible_damaging_level_up_move_pool(species).empty?
        raise MoveAccessRandomizationError,
              "no damaging level-1 move is available for #{identity}"
      end
    end
    egg_move_access_source_entries.each do |identity, moves|
      species = GameData::Species.get(identity.to_sym)
      if moves.length > eligible_level_up_move_pool(species).length
        raise MoveAccessRandomizationError,
              "the allowed Egg move pool is too small for #{identity}"
      end
    end
    [:tm, :tr].each do |channel|
      machine_move_access_source_entries(channel).each do |identity, moves|
        species = GameData::Species.get(identity.to_sym)
        if moves.length > eligible_machine_move_pool(species, channel).length
          raise MoveAccessRandomizationError,
                "the allowed #{channel.to_s.upcase} move pool is too small for #{identity}"
        end
      end
    end
    ordinary_tutor_source_entries.each do |identity, moves|
      species = GameData::Species.get(identity.to_sym)
      if moves.length > eligible_level_up_move_pool(species).length
        raise MoveAccessRandomizationError,
              "the allowed tutor move pool is too small for #{identity}"
      end
    end
    @validated_move_access_source_key = validation_key.freeze
    return true
  end

  def self.prepare_move_access_randomization
    @move_access_randomization_ready = false
    raise MoveAccessRandomizationError, "run metadata is unavailable" if
      !$PokemonGlobal
    pool = allowed_level_up_move_pool
    validate_move_access_sources
    record_generator_metadata(move_access_metadata_values(pool))
    reset_move_access_generator_cache
    move_access_generator
    @move_access_randomization_ready = true
    @move_access_randomization_error_message = nil
    patch_loaded_ordinary_tutor_events
    return true
  rescue MoveAccessRandomizationError => e
    @move_access_randomization_error_message = _INTL(
      "Ironmon could not prepare move-access randomization: {1}", e.message
    )
    echoln @move_access_randomization_error_message
    return false
  rescue Exception => e
    @move_access_randomization_error_message = _INTL(
      "Ironmon could not prepare move-access randomization because of an unexpected error: {1}",
      e.message
    )
    echoln @move_access_randomization_error_message
    return false
  end

  def self.move_access_metadata_values(pool = allowed_level_up_move_pool)
    return {
      :ironmon_move_access_generator_version =>
        MoveAccessGenerator::SCHEMA_VERSION,
      :ironmon_move_pool_size => pool.length,
      :ironmon_move_pool_fingerprint => level_up_move_pool_fingerprint,
      :ironmon_move_contextual_restriction_fingerprint =>
        level_up_move_contextual_fingerprint,
      :ironmon_move_source_fingerprint => move_access_source_fingerprint,
      :ironmon_egg_move_source_fingerprint =>
        egg_move_access_source_fingerprint,
      :ironmon_tm_roster_size => machine_move_pool(:tm).length,
      :ironmon_tm_roster_fingerprint => machine_roster_fingerprint(:tm),
      :ironmon_tm_source_fingerprint =>
        machine_move_access_source_fingerprint(:tm),
      :ironmon_tr_roster_size => machine_move_pool(:tr).length,
      :ironmon_tr_roster_fingerprint => machine_roster_fingerprint(:tr),
      :ironmon_tr_source_fingerprint =>
        machine_move_access_source_fingerprint(:tr),
      :ironmon_tutor_catalog_size => ORDINARY_TUTOR_SLOTS.length,
      :ironmon_tutor_catalog_fingerprint =>
        ordinary_tutor_catalog_fingerprint,
      :ironmon_tutor_source_fingerprint => ordinary_tutor_source_fingerprint,
      :ironmon_fusion_tutor_regular_catalog_size =>
        original_specialized_tutor_catalog(:regular).length,
      :ironmon_fusion_tutor_legendary_catalog_size =>
        original_specialized_tutor_catalog(:legendary).length,
      :ironmon_fusion_tutor_catalog_fingerprint =>
        specialized_tutor_catalog_fingerprint,
      :ironmon_fusion_tutor_source_fingerprint =>
        specialized_tutor_source_fingerprint
    }
  end

  def self.move_access_metadata_checks
    expected = move_access_metadata_values
    return MOVE_ACCESS_METADATA_FIELDS.map do |field_name|
      [field_name.to_s, $PokemonGlobal.public_send(field_name),
       expected[field_name]]
    end
  end

  def self.current_move_access_randomization?
    return false if !$PokemonGlobal
    return generator_metadata_matches?(move_access_metadata_checks)
  rescue Exception
    return false
  end

  def self.ensure_move_access_randomization
    @move_access_randomization_ready = false
    return false if !$PokemonGlobal
    if !current_move_access_randomization?
      raise MoveAccessRandomizationError,
            "the saved move-access generator or source data is incompatible"
    end
    reset_move_access_generator_cache
    move_access_generator
    @move_access_randomization_ready = true
    patch_loaded_ordinary_tutor_events
    return true
  end

  def self.move_access_randomization_active?
    return false if !$PokemonGlobal
    return false if $PokemonGlobal.ironmon_mode != true
    return @move_access_randomization_ready == true
  end

  def self.move_access_generator
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    if !@move_access_generator || @move_access_generator_seed != seed
      @move_access_generator_seed = seed
      @move_access_generator = MoveAccessGenerator.new(
        seed, allowed_level_up_move_pool, level_up_move_pool_fingerprint,
        move_access_source_fingerprint
      )
    end
    return @move_access_generator
  end

  def self.reset_move_access_generator_cache
    @move_access_generator = nil
    @move_access_generator_seed = nil
  end

  def self.suspend_move_access_randomization
    @move_access_randomization_ready = false
    reset_move_access_generator_cache
  end

  def self.move_access_randomization_error_message
    return @move_access_randomization_error_message ||
      _INTL("Ironmon could not prepare move-access randomization.")
  end
end
