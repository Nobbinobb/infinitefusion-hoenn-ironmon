#===============================================================================
# Deterministic Pokemon ability randomization
#===============================================================================

module Ironmon
  class AbilityRandomizationError < StandardError; end

  class AbilityGenerator
    SCHEMA_VERSION = 3
    LEGACY_FUSION_FALLBACK_SCHEMA_VERSION = 2
    ASSIGNMENT_SCHEMA_VERSION = 2
    POOL_RULES_VERSION = 2
    EXACT_SPECIES_ABILITY_RULES = {
      :RKSSYSTEM       => [:SILVALLY],
      :BATTLEBOND      => [:GRENINJA],
      :POWERCONSTRUCT  => [:ZYGARDE],
      :SCHOOLING       => [:WISHIWASHI],
      :ICEFACE         => [:EISCUE],
      :ZENMODE         => [:DARMANITAN],
      :FORECAST        => [:CASTFORM]
    }.freeze

    COMPONENT_ABILITY_RULES = {
      :DISGUISE     => [:MIMIKYU],
      :SHIELDSDOWN  => [:MINIOR, :MINIOR_M, :MINIOR_C]
    }.freeze

    CONTEXTUAL_ABILITIES = (
      EXACT_SPECIES_ABILITY_RULES.keys + COMPONENT_ABILITY_RULES.keys
    ).freeze

    attr_reader :pool
    attr_reader :pool_fingerprint

    def initialize(seed, pool, pool_fingerprint)
      @seed = seed.to_i
      @pool = pool
      @pool_fingerprint = pool_fingerprint
      @slot_cache = {}
      @fusion_slot_cache = {}
    end

    def slots_for(species_data)
      identity = Ironmon.ability_species_identity(species_data)
      cached = @slot_cache[identity]
      return cached if cached

      original_normal = Ironmon.original_normal_abilities(species_data)
      original_hidden = Ironmon.original_hidden_abilities(species_data)
      defined_slot_count = original_normal.compact.length +
        original_hidden.compact.length
      eligible_pool = Ironmon.eligible_ability_pool(species_data)
      if defined_slot_count > eligible_pool.length
        raise AbilityRandomizationError,
              "the allowed ability pool is too small for #{identity}"
      end

      used = {}
      normal = generate_slot_array(
        identity, :normal, original_normal, used, eligible_pool
      )
      hidden = generate_slot_array(
        identity, :hidden, original_hidden, used, eligible_pool
      )
      result = {
        :normal => normal.freeze,
        :hidden => hidden.freeze
      }.freeze
      @slot_cache[identity] = result
      return result
    end

    def fusion_fallback(identity, kind, index, used = {})
      return select_ability(identity, kind, index, used, @pool, "fusion")
    end

    def fusion_slots_for(species_data)
      identity = Ironmon.ability_species_identity(species_data)
      cached = @fusion_slot_cache[identity]
      return cached if cached

      body = component_slots_for(species_data.body_pokemon)
      head = component_slots_for(species_data.head_pokemon)
      normal = Ironmon.fusion_ability_slots(
        species_data, [body[:normal][0], head[:normal][0]], :normal
      ).freeze
      hidden = Ironmon.fusion_ability_slots(species_data, [
        body[:normal][1],
        head[:normal][1],
        body[:hidden][0],
        head[:hidden][0]
      ], :hidden)
      hidden = Ironmon.trim_trailing_nil_ability_slots(hidden).freeze
      result = { :normal => normal, :hidden => hidden }.freeze
      @fusion_slot_cache[identity] = result
      return result
    end

    private

    def component_slots_for(species_data)
      return slots_for(species_data) if
        Ironmon.normal_ability_species?(species_data)
      return {
        :normal => Ironmon.original_normal_abilities(species_data),
        :hidden => Ironmon.original_hidden_abilities(species_data)
      }
    end

    def generate_slot_array(identity, kind, original_slots, used, pool)
      generated = Array.new(original_slots.length)
      original_slots.each_with_index do |original_ability, index|
        next if !original_ability
        generated[index] = select_ability(
          identity, kind, index, used, pool, "species"
        )
        used[generated[index]] = true
      end
      return generated
    end

    def select_ability(identity, kind, index, used, pool, namespace)
      start = deterministic_value(identity, kind, index, namespace, "start") %
        pool.length
      pool.length.times do |offset|
        candidate = pool[(start + offset) % pool.length]
        return candidate if !used[candidate]
      end
      raise AbilityRandomizationError,
            "no unique ability remains for #{identity} #{kind} slot #{index}"
    end

    def deterministic_value(*parts)
      return Ironmon.fnv1a_64_joined(
        [ASSIGNMENT_SCHEMA_VERSION, @seed, "ability", *parts]
      )
    end
  end
end
