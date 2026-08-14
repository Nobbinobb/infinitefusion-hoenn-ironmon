#===============================================================================
# Persistent automated-pivot state and Pokemon transformation markers
#===============================================================================

module Ironmon
  FUSION_ORIGIN_PLAYER_CREATED = :player_created
  FUSION_ORIGIN_CAUGHT = :caught
  FUSION_ORIGIN_PROCESSED_CAUGHT = :processed_caught
  FUSION_ORIGINS = [
    FUSION_ORIGIN_PLAYER_CREATED,
    FUSION_ORIGIN_CAUGHT,
    FUSION_ORIGIN_PROCESSED_CAUGHT
  ].freeze

  TRANSFORMATION_RIGHT_NONE = :none
  TRANSFORMATION_RIGHT_CAUGHT_PIVOT = :caught_pivot
  TRANSFORMATION_RIGHTS = [
    TRANSFORMATION_RIGHT_NONE,
    TRANSFORMATION_RIGHT_CAUGHT_PIVOT
  ].freeze

  class PivotState
    SCHEMA_VERSION = 3

    attr_reader :schema_version
    attr_reader :pending_pivot
    attr_reader :fusion_mappings
    attr_reader :discovered_fusion_mappings
    attr_reader :completed_acquisition_ids
    attr_reader :next_acquisition_sequence
    attr_reader :quarantined_pokemon
    attr_reader :excluded_acquisition_log

    def initialize
      @schema_version = SCHEMA_VERSION
      @pending_pivot = nil
      @fusion_mappings = {}
      @discovered_fusion_mappings = {}
      @completed_acquisition_ids = {}
      @next_acquisition_sequence = 0
      @quarantined_pokemon = []
      @excluded_acquisition_log = []
    end

    def migrate!
      if @schema_version != SCHEMA_VERSION
        @fusion_mappings = {}
        @discovered_fusion_mappings = {}
      end
      @pending_pivot = nil if !@pending_pivot.is_a?(Hash)
      @fusion_mappings = {} if !@fusion_mappings.is_a?(Hash)
      if !@discovered_fusion_mappings.is_a?(Hash)
        @discovered_fusion_mappings = {}
      end
      if !@completed_acquisition_ids.is_a?(Hash)
        @completed_acquisition_ids = {}
      end
      if !@next_acquisition_sequence.is_a?(Integer) ||
         @next_acquisition_sequence < 0
        @next_acquisition_sequence = 0
      end
      @quarantined_pokemon = [] if !@quarantined_pokemon.is_a?(Array)
      if !@excluded_acquisition_log.is_a?(Array)
        @excluded_acquisition_log = []
      end
      discard_completed_pending_pivot
      @schema_version = SCHEMA_VERSION
      return self
    end

    def current?
      return false if @schema_version != SCHEMA_VERSION
      return false if @pending_pivot && !@pending_pivot.is_a?(Hash)
      return false if !@fusion_mappings.is_a?(Hash)
      return false if !@discovered_fusion_mappings.is_a?(Hash)
      return false if !@completed_acquisition_ids.is_a?(Hash)
      return false if !@next_acquisition_sequence.is_a?(Integer)
      return false if @next_acquisition_sequence < 0
      return false if !@quarantined_pokemon.is_a?(Array)
      return false if !@excluded_acquisition_log.is_a?(Array)
      if @pending_pivot
        acquisition_id = @pending_pivot[:acquisition_id]
        return false if @pending_pivot[:status] != :pending
        return false if !acquisition_id
        return false if @completed_acquisition_ids[acquisition_id.to_s]
      end
      return true
    end

    def reserve_acquisition_id(run_seed)
      @next_acquisition_sequence += 1
      return sprintf("%08x-%08x", run_seed.to_i & 0x7FFFFFFF,
                     @next_acquisition_sequence)
    end

    def begin_pivot(acquisition_id, attributes = {})
      raise "An Ironmon pivot is already pending." if pending?
      if completed?(acquisition_id)
        raise "This Ironmon acquisition was already completed."
      end
      @pending_pivot = attributes.dup
      @pending_pivot[:acquisition_id] = acquisition_id.to_s
      @pending_pivot[:status] = :pending
      return @pending_pivot
    end

    def pending?
      return false if !@pending_pivot
      return @pending_pivot[:status] == :pending
    end

    def complete_pivot(acquisition_id)
      acquisition_id = acquisition_id.to_s
      if !pending? || @pending_pivot[:acquisition_id] != acquisition_id
        raise "The Ironmon pivot transaction does not match this acquisition."
      end
      @completed_acquisition_ids[acquisition_id] = true
      @pending_pivot = nil
      return true
    end

    def completed?(acquisition_id)
      return @completed_acquisition_ids[acquisition_id.to_s] == true
    end

    def cancel_pending_pivot(acquisition_id)
      return false if !pending?
      return false if @pending_pivot[:acquisition_id] != acquisition_id.to_s
      @pending_pivot = nil
      return true
    end

    def quarantine_pokemon(pokemon, reason)
      @quarantined_pokemon << {
        :pokemon => pokemon,
        :reason => reason
      }
      return pokemon
    end

    def record_excluded_acquisition(entry)
      @excluded_acquisition_log << entry.dup
      return entry
    end

    def self.from(value)
      return value.migrate! if value.is_a?(self)
      return new
    rescue Exception => e
      echoln "Ironmon pivot-state migration failed; using defaults: #{e.message}"
      return new
    end

    private

    def discard_completed_pending_pivot
      return if !@pending_pivot
      acquisition_id = @pending_pivot[:acquisition_id]
      if acquisition_id && @completed_acquisition_ids[acquisition_id.to_s]
        @pending_pivot = nil
      end
    end
  end

  def self.pivot_state
    return PivotState.new if !$PokemonGlobal
    stored = $PokemonGlobal.ironmon_pivot_state
    return stored if stored.is_a?(PivotState) && stored.current?
    migrated = PivotState.from(stored)
    $PokemonGlobal.ironmon_pivot_state = migrated
    return migrated
  end

  def self.reset_pivot_state
    return if !$PokemonGlobal
    $PokemonGlobal.ironmon_pivot_state = PivotState.new
  end

  def self.reserve_acquisition_id
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    return pivot_state.reserve_acquisition_id(seed)
  end

  def self.mark_player_created_fusion(pokemon)
    mark_fusion(pokemon, FUSION_ORIGIN_PLAYER_CREATED,
                TRANSFORMATION_RIGHT_NONE)
  end

  def self.mark_caught_fusion(pokemon)
    mark_fusion(pokemon, FUSION_ORIGIN_CAUGHT,
                TRANSFORMATION_RIGHT_CAUGHT_PIVOT)
  end

  def self.mark_processed_caught_fusion(pokemon)
    mark_fusion(pokemon, FUSION_ORIGIN_PROCESSED_CAUGHT,
                TRANSFORMATION_RIGHT_NONE)
  end

  def self.mark_fusion(pokemon, origin, transformation_right)
    raise "Invalid Ironmon fusion origin." if !FUSION_ORIGINS.include?(origin)
    if !TRANSFORMATION_RIGHTS.include?(transformation_right)
      raise "Invalid Ironmon transformation right."
    end
    pokemon.ironmon_fusion_origin = origin
    pokemon.ironmon_transformation_right = transformation_right
    return pokemon
  end
end
