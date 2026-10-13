module Ironmon
  class DefenseOverview
    DAMAGE_PROFILE_CACHE_LIMIT = 16
    DAMAGE_PROFILE_CACHE_MAX_TYPES = 32
    DAMAGE_PROFILE_CACHE_MAX_RULES = 64

    # Generic factor composition preserves the chart for activation checks.
    # The catalog supplies values and categories; rule IDs and text are opaque.
    def build_damage_profiles
      if respond_to?(:defer_damage_profiles?) && defer_damage_profiles?
        export_damage_profile_facts
        return
      end
      global_rules = @rules.select(&:all_type_factor?)
      type_rules = @rules - global_rules
      @result["all_type_effects"] = global_rules.map(&:factor_snapshot)
      key = damage_profile_key
      profiles = if key
                   DefenseOverview.send(:cached_damage_profiles, key) do
                     calculate_damage_profiles(type_rules)
                   end
                 else
                   calculate_damage_profiles(type_rules)
                 end
      @result["type_matchups"].each_with_index do |entry, index|
        profile = profiles[index]
        entry["physical_min"], entry["physical_max"] = profile[0], profile[1]
        entry["type_physical_min"], entry["type_physical_max"] = profile[2], profile[3]
        entry["special_min"], entry["special_max"] = profile[4], profile[5]
        entry["type_special_min"], entry["type_special_max"] = profile[6], profile[7]
        entry["adjustments"] = profile[8].map { |rule_index| @rules[rule_index].snapshot }
        entry["exceptions"] = []
      end
    end

    # Only normalized mechanics participate. Display names and descriptions are
    # rebuilt from this call's rules, including on a cache hit.
    def damage_profile_key
      entries = @result["type_matchups"]
      return nil if entries.length > DAMAGE_PROFILE_CACHE_MAX_TYPES || @rules.length > DAMAGE_PROFILE_CACHE_MAX_RULES
      matchups = entries.map do |entry|
        value = entry["multiplier"]
        return nil if !damage_profile_scalar?(value)
        [entry["type"].to_sym, value].freeze
      end
      identities = {}.compare_by_identity
      rules = @rules.each_with_index.map do |rule, index|
        return nil if !rule.instance_of?(DefenseRule) || rule.types.length > DAMAGE_PROFILE_CACHE_MAX_TYPES
        return nil if !rule.types.all? { |type| type.is_a?(Symbol) } || (rule.category && !rule.category.is_a?(Symbol))
        active, conditional = rule.active, rule.conditional
        return nil if !(active.nil? || active.equal?(true) || active.equal?(false)) ||
          !(conditional.nil? || conditional.equal?(true) || conditional.equal?(false))
        factor = rule.factor
        if factor.is_a?(Hash)
          supported = factor.length <= DAMAGE_PROFILE_CACHE_MAX_TYPES && factor.all? do |type, value|
            type.is_a?(Symbol) && (value.nil? || value.equal?(false) || damage_profile_scalar?(value))
          end
          return nil if !supported
          factor = factor.dup.freeze
        elsif !factor.nil? && !factor.equal?(false) && !damage_profile_scalar?(factor)
          return nil
        end
        identities[rule] ||= index
        [identities[rule], active, rule.types.dup.freeze, rule.category, factor,
         conditional, rule.all_type_factor?].freeze
      end
      return [matchups.freeze, rules.freeze].freeze
    end

    def damage_profile_scalar?(value)
      return true if value.is_a?(Integer)
      return value.is_a?(Float) && value.finite? && (!value.zero? || 1.0 / value > 0)
    end

    def self.cached_damage_profiles(key)
      @damage_profile_cache ||= {}
      profiles = @damage_profile_cache[key]
      return profiles if profiles
      profiles = yield
      @damage_profile_cache.shift if @damage_profile_cache.length >= DAMAGE_PROFILE_CACHE_LIMIT
      @damage_profile_cache[key] = profiles
      return profiles
    end
    private_class_method :cached_damage_profiles

    # Numeric rows retain the original multiplication order and first ordered
    # object indexes; duplicate rule IDs are never treated as equal mechanics.
    def calculate_damage_profiles(type_rules)
      indexes = {}.compare_by_identity
      @rules.each_with_index { |rule, index| indexes[rule] ||= index }
      return @result["type_matchups"].map do |entry|
        attack = entry["type"].to_sym
        adjustments = []
        values = []
        [:physical, :special].each do |category|
          low, high, applied = compose_damage_profile(entry["multiplier"], attack, category, @rules)
          values.push(low, high)
          adjustments.concat(applied)
          low, high = compose_damage_profile(entry["multiplier"], attack, category, type_rules)
          values.push(low, high)
        end
        values << adjustments.uniq.map { |rule| indexes.fetch(rule) }.freeze
        values.freeze
      end.freeze
    end
    private :damage_profile_key, :damage_profile_scalar?, :calculate_damage_profiles

    # Both presentations use the same arithmetic and unresolved-condition rules.
    def compose_damage_profile(multiplier, attack, category, rules)
      low = high = multiplier
      adjustments = []
      rules.each do |rule|
        factor = rule.factor_for(attack, category)
        next if !factor || factor == 1 || high == 0
        unknown = rule.conditional || rule.active.nil?
        low *= unknown ? [factor, 1.0].min : factor
        high *= unknown ? [factor, 1.0].max : factor
        adjustments << rule
      end
      return low, high, adjustments
    end
  end
end
