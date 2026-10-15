module Ironmon
  DEFENSE_DAMAGE_PROFILE_VERSION = 1
  DEFENSE_DAMAGE_PROFILE_RULE_LIMIT = 256
  DEFENSE_DAMAGE_PROFILE_TYPE_LIMIT = 64

  class DefenseOverview
    def defer_damage_profiles?
      return Ironmon.tracker_connection.defense_damage_profiles_supported? &&
        @rules.length <= DEFENSE_DAMAGE_PROFILE_RULE_LIMIT &&
        @result["type_matchups"].length <= DEFENSE_DAMAGE_PROFILE_TYPE_LIMIT &&
        @rules.all? { |rule| damage_profile_rule_supported?(rule) }
    end

    # Conditions, source labels and concealed state have already been resolved.
    # Only numeric composition and repeated adjustment rows are deferred.
    def export_damage_profile_facts
      global_rules = @rules.select(&:all_type_factor?)
      @result["all_type_effects"] = global_rules.map(&:factor_snapshot)
      identities = {}
      rules = @rules.filter_map do |rule|
        next if rule.active == false || !rule.factor.is_a?(Numeric) && !rule.factor.is_a?(Hash)
        identity = identities[rule.object_id] ||= identities.length
        fact = {
          "rule_identity" => identity, "active" => rule.active,
          "category" => rule.category&.to_s, "conditional" => rule.conditional,
          "types" => rule.types.map(&:to_s), "has_moves" => !rule.moves.empty?,
          "global" => rule.all_type_factor?, "snapshot" => rule.snapshot
        }
        if rule.factor.is_a?(Hash)
          fact["type_factors"] = rule.factor.each_with_object({}) do |(type, factor), result|
            next if factor.nil? || factor == false
            result[type.to_s] = factor
          end
        else
          fact["factor"] = rule.factor
        end
        fact
      end
      @result["damage_profiles_deferred"] = true
      @result["damage_profile_facts"] = {
        "version" => DEFENSE_DAMAGE_PROFILE_VERSION, "rules" => rules
      }
    end

    private

    def damage_profile_rule_supported?(rule)
      return false if !rule.instance_of?(DefenseRule) ||
        !rule.active.equal?(true) && !rule.active.equal?(false) && !rule.active.nil? ||
        !rule.conditional.equal?(true) && !rule.conditional.equal?(false) ||
        !rule.category.nil? && !rule.category.is_a?(Symbol) ||
        !rule.types.is_a?(Array) || rule.types.length > DEFENSE_DAMAGE_PROFILE_TYPE_LIMIT ||
        !rule.types.all? { |type| type.is_a?(Symbol) && !type.to_s.empty? } ||
        !rule.moves.is_a?(Array) || !rule.moves.all? { |move| move.is_a?(String) }
      factor = rule.factor
      return true if factor.nil? || factor == false
      if factor.is_a?(Hash)
        return factor.length <= DEFENSE_DAMAGE_PROFILE_TYPE_LIMIT && factor.all? do |type, value|
          type.is_a?(Symbol) && !type.to_s.empty? &&
            (value.nil? || value == false || damage_profile_number_supported?(value))
        end
      end
      return damage_profile_number_supported?(factor)
    end

    def damage_profile_number_supported?(value)
      return (value.is_a?(Integer) || value.is_a?(Float)) && value.to_f.finite?
    end
  end
end
