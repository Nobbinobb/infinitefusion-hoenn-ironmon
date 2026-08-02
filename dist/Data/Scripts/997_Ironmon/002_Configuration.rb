#===============================================================================
# Persistent Ironmon configuration
#===============================================================================

module Ironmon
  class Configuration
    SCHEMA_VERSION = 1

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

    attr_reader :schema_version
    attr_reader :wild_policy
    attr_reader :trainer_policy

    def initialize(wild_policy = DEFAULT_WILD_POLICY,
                   trainer_policy = DEFAULT_TRAINER_POLICY)
      @schema_version = SCHEMA_VERSION
      self.wild_policy = wild_policy
      self.trainer_policy = trainer_policy
    end

    def wild_policy=(policy)
      @wild_policy = normalize_policy(policy, DEFAULT_WILD_POLICY)
    end

    def trainer_policy=(policy)
      @trainer_policy = normalize_policy(policy, DEFAULT_TRAINER_POLICY)
    end

    def migrate!
      self.wild_policy = @wild_policy
      self.trainer_policy = @trainer_policy
      @schema_version = SCHEMA_VERSION
      return self
    end

    def current?
      return false if @schema_version != SCHEMA_VERSION
      return false if !POLICY_IDS.include?(@wild_policy)
      return false if !POLICY_IDS.include?(@trainer_policy)
      return true
    end

    def to_h
      return {
        :schema_version => @schema_version,
        :wild_policy => @wild_policy,
        :trainer_policy => @trainer_policy
      }
    end

    def self.from(value)
      return value.migrate! if value.is_a?(self)
      if value.is_a?(Hash)
        wild_policy = value[:wild_policy] || value["wild_policy"]
        trainer_policy = value[:trainer_policy] || value["trainer_policy"]
        return new(wild_policy, trainer_policy)
      end
      return new
    rescue Exception => e
      echoln "Ironmon configuration migration failed; using defaults: #{e.message}"
      return new
    end

    private

    def normalize_policy(policy, default_policy)
      POLICY_IDS.each do |policy_id|
        return policy_id if policy == policy_id || policy == policy_id.to_s
      end
      return default_policy
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
