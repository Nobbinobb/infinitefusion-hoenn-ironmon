module Ironmon
  class DefenseOverview
    # All ability-specific data comes from the release audit catalog.
    def ability_rules
      return if !@ability || @ability_definition.empty?
      source = GameData::Ability.get(@ability)
      enabled = @ability_active || @ability_definition[:ignores_suppression] == true
      add_rule("ability_#{@ability}", @ability_definition, enabled, source)
      if @ability_definition[:contact_factor]
        add_rule("contact_#{@ability}", {
          section: :modifiers, factor: @ability_definition[:contact_factor],
          conditional: true
        }, @ability_active, source)
      end
    end

    def ally_rules
      seen_auras = []
      @battle.eachBattler do |ally|
        known = known_battler_ability(ally)
        next if !known || ally.effects[PBEffects::GastroAcid]
        definition = @catalog[:abilities].fetch(known, {})
        source = GameData::Ability.get(known)
        if definition[:global] && ally.index != @battler.index
          add_rule("global_#{ally.index}", definition[:global], true, source)
        end
        aura = definition[:aura]
        if aura && !seen_auras.include?(aura)
          seen_auras << aura
          factor = @catalog[:parameters][known_aura_break? ? :aura_break_factor : :aura_factor]
          add_rule("aura_#{ally.index}", { section: :modifiers, factor: factor, types: [aura] }, true, source)
        end
        next if ally.index == @battler.index || ally.opposes?(@battler)
        shared = definition[:allies] ? definition : definition[:ally]
        add_rule("ally_#{ally.index}", shared, true, source) if shared
      end
    end

    def known_aura_break?
      @battle.eachBattler do |battler|
        known = known_battler_ability(battler)
        next if !known || battler.effects[PBEffects::GastroAcid]
        return true if @catalog[:abilities].fetch(known, {})[:reverses_aura]
      end
      return false
    end
  end
end
