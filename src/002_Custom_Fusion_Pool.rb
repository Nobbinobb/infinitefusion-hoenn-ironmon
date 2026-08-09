#===============================================================================
# Validated custom-sprite fusion pool
#===============================================================================

module Ironmon
  class CustomFusionPoolError < StandardError; end

  class CustomFusionPool
    SCHEMA_VERSION = 2
    FNV_OFFSET_BASIS = 14_695_981_039_346_656_037
    FNV_PRIME = 1_099_511_628_211
    FNV_MASK = 0xFFFFFFFFFFFFFFFF

    attr_reader :source_entry_count
    attr_reader :rejected_entry_count
    attr_reader :build_count
    attr_reader :fingerprint

    def initialize(sprite_index)
      @sprite_index = sprite_index
      @pool = nil
      @source_entry_count = 0
      @rejected_entry_count = 0
      @build_count = 0
      @fingerprint = nil
    end

    def pool
      return @pool if @pool
      build_pool
      return @pool
    end

    def size
      return pool.length
    end

    def info
      pool
      return {
        :schema_version => SCHEMA_VERSION,
        :size => @pool.length,
        :fingerprint => @fingerprint,
        :source_entries => @source_entry_count,
        :rejected_entries => @rejected_entry_count
      }
    end

    private

    def build_pool
      if !@sprite_index || !@sprite_index.respond_to?(:keys)
        raise CustomFusionPoolError,
              "the custom sprite index is unavailable"
      end

      source_keys = @sprite_index.keys
      if source_keys.empty?
        raise CustomFusionPoolError,
              "the custom sprite index is empty"
      end

      @build_count += 1
      @source_entry_count = source_keys.length
      @rejected_entry_count = 0
      accepted = {}
      valid_base_ids = valid_base_species_ids
      selectable_sprite_keys = selectable_custom_sprite_keys
      source_keys.each do |sprite_key|
        match = /\AB(\d+)H(\d+)\z/.match(sprite_key.to_s)
        if !match
          @rejected_entry_count += 1
          next
        end

        body_id = match[1].to_i
        head_id = match[2].to_i
        if !valid_base_ids[body_id] || !valid_base_ids[head_id]
          @rejected_entry_count += 1
          next
        end
        if !selectable_sprite_keys[sprite_key]
          @rejected_entry_count += 1
          next
        end

        dex_number = (body_id * NB_POKEMON) + head_id
        if dex_number <= NB_POKEMON ||
           dex_number >= Settings::ZAPMOLCUNO_NB
          @rejected_entry_count += 1
          next
        end

        accepted[dex_number] = "B#{body_id}H#{head_id}".to_sym
      end

      if accepted.empty?
        raise CustomFusionPoolError,
              "no valid custom-sprite fusion species were found"
      end

      # Two-way Ironmon reversal requires an even-sized pool. Exclude one
      # deterministic tail entry if the selectable catalogue is odd.
      if accepted.length.odd?
        accepted.delete(accepted.keys.max)
        @rejected_entry_count += 1
      end

      @pool = accepted.keys.sort.map { |dex_number| accepted[dex_number] }
      @pool.freeze
      @fingerprint = fingerprint_for(@pool)
    end

    def valid_base_species_ids
      valid_ids = {}
      (1..NB_POKEMON).each do |species_id|
        begin
          species = GameData::Species.try_get(species_id)
          valid_ids[species_id] = true if species &&
            species.id_number == species_id
        rescue Exception
          next
        end
      end
      return valid_ids
    end

    def selectable_custom_sprite_keys
      path = Settings::CREDITS_FILE_PATH
      if !path || !File.file?(path)
        raise CustomFusionPoolError,
              "the sprite credits index is unavailable"
      end
      selectable = {}
      File.foreach(path) do |line|
        row = line.strip.split(',')
        next if row.length < 3
        status = row[2].to_s.downcase
        next if status != "main" && status != "temp"
        match = /\A(\d+)\.(\d+)[a-zA-Z]*\z/.match(row[0].to_s)
        next if !match
        head_id = match[1].to_i
        body_id = match[2].to_i
        selectable["B#{body_id}H#{head_id}".to_sym] = true
      end
      if selectable.empty?
        raise CustomFusionPoolError,
              "the sprite credits index has no selectable custom sprites"
      end
      return selectable
    end

    def fingerprint_for(species_pool)
      hash_value = FNV_OFFSET_BASIS
      species_pool.each do |species|
        species.to_s.each_byte do |byte|
          hash_value ^= byte
          hash_value = (hash_value * FNV_PRIME) & FNV_MASK
        end
        hash_value ^= 0
        hash_value = (hash_value * FNV_PRIME) & FNV_MASK
      end
      return sprintf("%016x", hash_value)
    end
  end

  def self.custom_fusion_pool_service
    if !@custom_fusion_pool_service
      sprite_index = if $game_temp
                       $game_temp.custom_sprites_list
                     else
                       nil
                     end
      @custom_fusion_pool_service = CustomFusionPool.new(sprite_index)
    end
    return @custom_fusion_pool_service
  end

  def self.custom_fusion_pool
    return custom_fusion_pool_service.pool
  end

  def self.custom_fusion_pool_info
    return custom_fusion_pool_service.info
  end

  def self.prepare_custom_fusion_pool
    custom_fusion_pool
    @custom_fusion_pool_error_message = nil
    return true
  rescue CustomFusionPoolError => e
    @custom_fusion_pool_error_message = _INTL(
      "Ironmon could not load its custom fusion pool: {1}. Verify Data/sprites/CUSTOM_SPRITES and restart the game.",
      e.message
    )
    echoln @custom_fusion_pool_error_message
    return false
  rescue Exception => e
    @custom_fusion_pool_error_message = _INTL(
      "Ironmon could not load its custom fusion pool because of an unexpected error: {1}",
      e.message
    )
    echoln @custom_fusion_pool_error_message
    return false
  end

  def self.custom_fusion_pool_error_message
    return @custom_fusion_pool_error_message ||
      _INTL("Ironmon could not load its custom fusion pool.")
  end

  def self.record_custom_fusion_pool_metadata
    return if !$PokemonGlobal
    info = custom_fusion_pool_info
    $PokemonGlobal.ironmon_custom_fusion_pool_version = info[:schema_version]
    $PokemonGlobal.ironmon_custom_fusion_pool_size = info[:size]
    $PokemonGlobal.ironmon_custom_fusion_pool_fingerprint = info[:fingerprint]
  end

  def self.reset_custom_fusion_pool_cache
    @custom_fusion_pool_service = nil
    @custom_fusion_pool_error_message = nil
    @custom_fusion_species_index = nil
    reset_player_fusion_mapper_cache if
      respond_to?(:reset_player_fusion_mapper_cache)
    reset_fusion_evolution_target_catalog_cache if
      respond_to?(:reset_fusion_evolution_target_catalog_cache)
    reset_tracker_post_run_cache if respond_to?(:reset_tracker_post_run_cache)
  end
end
