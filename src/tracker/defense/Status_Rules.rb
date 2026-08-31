module Ironmon
  class DefenseOverview
    def status_rules
      @catalog[:type_protections].each do |definition|
        next if definition[:more_type_effects] && !Settings::MORE_TYPE_EFFECTS
        next if (@types.map(&:to_s) & definition[:types]).empty?
        add_rule(definition[:id], definition)
      end
      return if !@battle
      uproar = false
      @battle.eachBattler { |battler| uproar ||= battler.effects[PBEffects::Uproar].to_i > 0 }
      if uproar
        definition = @catalog[:uproar]
        add_rule(definition[:id], definition, !ability_effect?(:ignores_uproar))
      end
    end
  end
end
