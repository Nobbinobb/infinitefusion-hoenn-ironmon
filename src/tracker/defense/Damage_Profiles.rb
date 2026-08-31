module Ironmon
  class DefenseOverview
    # Generic factor composition preserves the chart for activation checks.
    # The catalog supplies values and categories; rule IDs and text are opaque.
    def build_damage_profiles
      @result["type_matchups"].each do |entry|
        attack = entry["type"].to_sym
        adjustments = []
        [:physical, :special].each do |category|
          low = high = entry["multiplier"]
          @rules.each do |rule|
            factor = rule.factor_for(attack, category)
            next if !factor || factor == 1 || high == 0
            unknown = rule.conditional || rule.active.nil?
            low *= unknown ? [factor, 1.0].min : factor
            high *= unknown ? [factor, 1.0].max : factor
            adjustments << rule
          end
          entry["#{category}_min"] = low
          entry["#{category}_max"] = high
        end
        entry["adjustments"] = adjustments.uniq.map(&:snapshot)
        entry["exceptions"] = []
      end
    end
  end
end
