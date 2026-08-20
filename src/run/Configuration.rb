#===============================================================================
# Persistent Ironmon configuration
#===============================================================================

module Ironmon
  class Configuration
    SCHEMA_VERSION = 3

    POLICY_MIXED = :mixed
    POLICY_CUSTOM_FUSIONS_ONLY = :custom_fusions_only
    POLICY_NORMAL_ONLY = :normal_only
    POLICY_IDS = [
      POLICY_MIXED,
      POLICY_CUSTOM_FUSIONS_ONLY,
      POLICY_NORMAL_ONLY
    ].freeze

    DEFAULT_WILD_POLICY = POLICY_MIXED
    DEFAULT_TRAINER_POLICY = POLICY_MIXED

    UNFUSION_RANDOM_COMPONENT = :random_component
    UNFUSION_PLAYER_CHOICE = :player_choice
    UNFUSION_SETTING_IDS = [
      UNFUSION_RANDOM_COMPONENT,
      UNFUSION_PLAYER_CHOICE
    ].freeze
    DEFAULT_UNFUSION_SETTING = UNFUSION_RANDOM_COMPONENT
    DEFAULT_AUTOMATIC_RESET = false

    attr_reader :schema_version
    attr_reader :wild_policy
    attr_reader :trainer_policy
    attr_reader :unfusion_setting
    attr_reader :automatic_reset

    def initialize(wild_policy = DEFAULT_WILD_POLICY,
                   trainer_policy = DEFAULT_TRAINER_POLICY,
                   unfusion_setting = DEFAULT_UNFUSION_SETTING,
                   automatic_reset = DEFAULT_AUTOMATIC_RESET)
      @schema_version = SCHEMA_VERSION
      self.wild_policy = wild_policy
      self.trainer_policy = trainer_policy
      self.unfusion_setting = unfusion_setting
      self.automatic_reset = automatic_reset
    end

    def wild_policy=(policy)
      @wild_policy = normalize_policy(policy, DEFAULT_WILD_POLICY)
    end

    def trainer_policy=(policy)
      @trainer_policy = normalize_policy(policy, DEFAULT_TRAINER_POLICY)
    end

    def unfusion_setting=(setting)
      @unfusion_setting = normalize_value(
        setting,
        UNFUSION_SETTING_IDS,
        DEFAULT_UNFUSION_SETTING
      )
    end

    def automatic_reset=(value)
      @automatic_reset = value == true
    end

    def migrate!
      self.wild_policy = @wild_policy
      self.trainer_policy = @trainer_policy
      self.unfusion_setting = @unfusion_setting
      self.automatic_reset = @automatic_reset
      @schema_version = SCHEMA_VERSION
      return self
    end

    def current?
      return false if @schema_version != SCHEMA_VERSION
      return false if !POLICY_IDS.include?(@wild_policy)
      return false if !POLICY_IDS.include?(@trainer_policy)
      return false if !UNFUSION_SETTING_IDS.include?(@unfusion_setting)
      return false if ![true, false].include?(@automatic_reset)
      return true
    end

    def to_h
      return {
        :schema_version => @schema_version,
        :wild_policy => @wild_policy,
        :trainer_policy => @trainer_policy,
        :unfusion_setting => @unfusion_setting,
        :automatic_reset => @automatic_reset
      }
    end

    def self.from(value)
      return value.migrate! if value.is_a?(self)
      if value.is_a?(Hash)
        wild_policy = value[:wild_policy] || value["wild_policy"]
        trainer_policy = value[:trainer_policy] || value["trainer_policy"]
        unfusion_setting = value[:unfusion_setting] ||
                           value["unfusion_setting"]
        automatic_reset = if value.key?(:automatic_reset)
                            value[:automatic_reset]
                          else
                            value["automatic_reset"]
                          end
        return new(wild_policy, trainer_policy, unfusion_setting,
                   automatic_reset)
      end
      return new
    rescue Exception => e
      echoln "Ironmon configuration migration failed; using defaults: #{e.message}"
      return new
    end

    private

    def normalize_policy(policy, default_policy)
      return normalize_value(policy, POLICY_IDS, default_policy)
    end

    def normalize_value(value, allowed_values, default_value)
      allowed_values.each do |allowed_value|
        return allowed_value if value == allowed_value ||
                                value == allowed_value.to_s
      end
      return default_value
    end
  end

  def self.configuration
    return Configuration.new if !$PokemonGlobal
    stored = $PokemonGlobal.ironmon_configuration
    return stored if stored.is_a?(Configuration) && stored.current?
    migrated = Configuration.from(stored)
    $PokemonGlobal.ironmon_configuration = migrated
    return migrated
  end

  def self.configuration=(value)
    return if !$PokemonGlobal
    $PokemonGlobal.ironmon_configuration = Configuration.from(value)
  end

  def self.configuration_snapshot
    return configuration.to_h
  end

  def self.remember_current_seed_for_reset
    @seed_to_avoid = if $PokemonGlobal
                       $PokemonGlobal.ironmon_seed
                     else
                       nil
                     end
  end

  def self.generate_run_seed
    seed = rand(2_147_483_647)
    seed = rand(2_147_483_647) while seed == @seed_to_avoid
    @seed_to_avoid = nil
    return seed
  end
end
