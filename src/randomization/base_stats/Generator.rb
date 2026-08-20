#===============================================================================
# Deterministic Pokemon base-stat randomization
#===============================================================================

module Ironmon
  class BaseStatRandomizationError < StandardError; end

  class BaseStatGenerator
    SCHEMA_VERSION = 1
    RULES_VERSION = 1
    MINIMUM_STAT = 5
    MAXIMUM_STAT = 255
    WEIGHT_RANGE = 1_000_000
    STAT_ORDER = [
      :HP, :ATTACK, :DEFENSE, :SPECIAL_ATTACK, :SPECIAL_DEFENSE, :SPEED
    ].freeze
    HEAD_DOMINANT_STATS = [
      :HP, :SPECIAL_ATTACK, :SPECIAL_DEFENSE
    ].freeze
    attr_reader :source_fingerprint

    def initialize(seed, source_fingerprint)
      @seed = seed.to_i
      @source_fingerprint = source_fingerprint
      @stat_cache = {}
      @fusion_cache = {}
    end

    def stats_for(species_data, identity = nil, original_stats = nil)
      identity ||= Ironmon.base_stat_species_identity(species_data)
      original_stats ||= Ironmon.original_base_stats_for(species_data)
      key = [identity, *STAT_ORDER.map { |stat| original_stats[stat].to_i }]
      cached = @stat_cache[key]
      return cached if cached
      generated = distribute(identity, original_stats)
      @stat_cache[key] = generated.freeze
      return @stat_cache[key]
    end

    def fusion_stats_for(species_data)
      identity = Ironmon.base_stat_species_identity(species_data)
      cached = @fusion_cache[identity]
      return cached if cached
      body = stats_for(species_data.body_pokemon)
      head = stats_for(species_data.head_pokemon)
      @fusion_cache[identity] = fuse(body, head).freeze
      return @fusion_cache[identity]
    end

    def fuse(body_stats, head_stats)
      result = {}
      STAT_ORDER.each do |stat|
        head_dominant = HEAD_DOMINANT_STATS.include?(stat)
        dominant = head_dominant ? head_stats[stat] : body_stats[stat]
        other = head_dominant ? body_stats[stat] : head_stats[stat]
        result[stat] = ((2 * dominant.to_i) / 3) + (other.to_i / 3)
      end
      return result
    end

    def validate_source(identity, original_stats)
      total = STAT_ORDER.inject(0) do |sum, stat|
        value = original_stats[stat]
        if !value || value.to_i <= 0
          raise BaseStatRandomizationError,
                "#{identity} has no valid #{stat} base stat"
        end
        sum + value.to_i
      end
      minimum_total = MINIMUM_STAT * STAT_ORDER.length
      maximum_total = MAXIMUM_STAT * STAT_ORDER.length
      if total < minimum_total || total > maximum_total
        raise BaseStatRandomizationError,
              "#{identity} has BST #{total}, outside #{minimum_total}-#{maximum_total}"
      end
      return true
    end

    private

    def distribute(identity, original_stats)
      validate_source(identity, original_stats)
      total = STAT_ORDER.inject(0) do |sum, stat|
        sum + original_stats[stat].to_i
      end
      values = Array.new(STAT_ORDER.length, MINIMUM_STAT)
      capacities = Array.new(
        STAT_ORDER.length, MAXIMUM_STAT - MINIMUM_STAT
      )
      weights = STAT_ORDER.map do |stat|
        1 + (deterministic_value(identity, stat, "weight") % WEIGHT_RANGE)
      end
      remaining = total - (MINIMUM_STAT * STAT_ORDER.length)
      while remaining > 0
        active = []
        capacities.each_with_index do |capacity, index|
          active << index if capacity > 0
        end
        if active.empty?
          raise BaseStatRandomizationError,
                "#{identity} cannot fit within the per-stat maximum"
        end
        total_weight = active.inject(0) { |sum, index| sum + weights[index] }
        round_total = remaining
        remainders = []
        distributed = 0
        active.each do |index|
          numerator = round_total * weights[index]
          share = numerator / total_weight
          allocation = [share, capacities[index]].min
          values[index] += allocation
          capacities[index] -= allocation
          remaining -= allocation
          distributed += allocation
          remainders << [numerator % total_weight, index]
        end
        remainders.sort_by! { |remainder, index| [-remainder, index] }
        remainders.each do |_remainder, index|
          break if remaining <= 0
          next if capacities[index] <= 0
          values[index] += 1
          capacities[index] -= 1
          remaining -= 1
          distributed += 1
        end
        if distributed <= 0
          raise BaseStatRandomizationError,
                "#{identity} base-stat allocation made no progress"
        end
      end
      result = {}
      STAT_ORDER.each_with_index { |stat, index| result[stat] = values[index] }
      return result
    end

    def deterministic_value(*parts)
      return Ironmon.fnv1a_64_joined(
        [SCHEMA_VERSION, @seed, "base_stats", *parts]
      )
    end
  end

  SIZE_CATEGORY_BASE_STATS = {
    [:PUMPKABOO, :SMALL] => {
      :HP => 44, :ATTACK => 66, :DEFENSE => 70,
      :SPECIAL_ATTACK => 44, :SPECIAL_DEFENSE => 55, :SPEED => 56
    },
    [:PUMPKABOO, :LARGE] => {
      :HP => 54, :ATTACK => 66, :DEFENSE => 70,
      :SPECIAL_ATTACK => 44, :SPECIAL_DEFENSE => 55, :SPEED => 46
    },
    [:PUMPKABOO, :SUPER] => {
      :HP => 59, :ATTACK => 66, :DEFENSE => 70,
      :SPECIAL_ATTACK => 44, :SPECIAL_DEFENSE => 55, :SPEED => 41
    },
    [:GOURGEIST, :SMALL] => {
      :HP => 55, :ATTACK => 85, :DEFENSE => 122,
      :SPECIAL_ATTACK => 58, :SPECIAL_DEFENSE => 75, :SPEED => 99
    },
    [:GOURGEIST, :LARGE] => {
      :HP => 75, :ATTACK => 95, :DEFENSE => 122,
      :SPECIAL_ATTACK => 58, :SPECIAL_DEFENSE => 75, :SPEED => 69
    },
    [:GOURGEIST, :SUPER] => {
      :HP => 85, :ATTACK => 100, :DEFENSE => 122,
      :SPECIAL_ATTACK => 58, :SPECIAL_DEFENSE => 75, :SPEED => 54
    }
  }.freeze

  def self.normal_base_stat_species?(species_data)
    return false if !species_data
    return species_data.id_number > 0 && species_data.id_number <= NB_POKEMON
  end

  def self.fusion_base_stat_species?(species_data)
    return false if !species_data
    return false if species_data.id_number <= NB_POKEMON
    return false if species_data.id_number >= Settings::ZAPMOLCUNO_NB
    return species_data.respond_to?(:body_pokemon) &&
      species_data.respond_to?(:head_pokemon)
  end

  def self.base_stat_species_identity(species_data)
    return species_data.id.to_s
  end

  def self.base_stat_size_identity(species_data, size_category)
    return "#{base_stat_species_identity(species_data)}|size:#{size_category}"
  end

  def self.original_base_stats_for(species_data)
    if fusion_base_stat_species?(species_data)
      body = original_base_stats_for(species_data.body_pokemon)
      head = original_base_stats_for(species_data.head_pokemon)
      return BaseStatGenerator.new(0, nil).fuse(body, head)
    end
    return species_data.ironmon_unrandomized_base_stats.dup
  end

  def self.original_base_stats_for_pokemon(pokemon)
    exception = pokemon.getBaseStatsFormException
    return exception.dup if exception
    return original_base_stats_for(pokemon.species_data)
  end

  def self.generated_base_stats_for(species_data, generator = nil)
    generator ||= base_stat_generator
    return generator.fusion_stats_for(species_data) if
      fusion_base_stat_species?(species_data)
    return generator.stats_for(species_data) if
      normal_base_stat_species?(species_data)
    return original_base_stats_for(species_data)
  end

  def self.generated_base_stats_for_pokemon(pokemon, generator = nil)
    generator ||= base_stat_generator
    exception = pokemon.getBaseStatsFormException
    if exception
      identity = base_stat_size_identity(
        pokemon.species_data, pokemon.size_category
      )
      return generator.stats_for(pokemon.species_data, identity, exception)
    end
    return generated_base_stats_for(pokemon.species_data, generator)
  end

  def self.base_stat_source_entries
    entries = {}
    GameData::Species.each do |species|
      next if !normal_base_stat_species?(species)
      identity = base_stat_species_identity(species)
      entries[identity] = species.ironmon_unrandomized_base_stats.dup
    end
    SIZE_CATEGORY_BASE_STATS.each do |key, stats|
      identity = "#{key[0]}|size:#{key[1]}"
      entries[identity] = stats
    end
    return entries.keys.sort.map { |identity| [identity, entries[identity]] }
  end

  def self.base_stat_source_fingerprint
    return @base_stat_source_fingerprint if @base_stat_source_fingerprint
    entries = [BaseStatGenerator::RULES_VERSION,
               BaseStatGenerator::MINIMUM_STAT,
               BaseStatGenerator::MAXIMUM_STAT]
    base_stat_source_entries.each do |identity, stats|
      entries << identity
      BaseStatGenerator::STAT_ORDER.each { |stat| entries << stats[stat] }
    end
    @base_stat_source_fingerprint = fnv1a_64_fingerprint(entries)
    return @base_stat_source_fingerprint
  end

  def self.validate_base_stat_sources
    fingerprint = base_stat_source_fingerprint
    return true if @validated_base_stat_source_fingerprint == fingerprint
    validator = BaseStatGenerator.new(0, base_stat_source_fingerprint)
    base_stat_source_entries.each do |identity, stats|
      validator.validate_source(identity, stats)
    end
    @validated_base_stat_source_fingerprint = fingerprint
    return true
  end

end
