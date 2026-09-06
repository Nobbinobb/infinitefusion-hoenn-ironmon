module Ironmon
  class DefenseOverview
    # Generic factor composition preserves the chart for activation checks.
    # The catalog supplies values and categories; rule IDs and text are opaque.
    def build_damage_profiles
      global_rules = @rules.select(&:all_type_factor?)
      type_rules = @rules - global_rules
      @result["all_type_effects"] = global_rules.map(&:factor_snapshot)
      @result["type_matchups"].each do |entry|
        attack = entry["type"].to_sym
        adjustments = []
        [:physical, :special].each do |category|
          low, high, applied = compose_damage_profile(entry["multiplier"], attack, category, @rules)
          entry["#{category}_min"] = low
          entry["#{category}_max"] = high
          adjustments.concat(applied)
          low, high = compose_damage_profile(entry["multiplier"], attack, category, type_rules)
          entry["type_#{category}_min"] = low
          entry["type_#{category}_max"] = high
        end
        entry["adjustments"] = adjustments.uniq.map(&:snapshot)
        entry["exceptions"] = []
      end
    end

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
