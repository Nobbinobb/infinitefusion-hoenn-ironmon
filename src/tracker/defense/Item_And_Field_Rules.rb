module Ironmon
  class DefenseOverview
    def item_rules
      return if !@item || @enemy
      source = GameData::Item.get(@item)
      type = @catalog[:resist_berries][@item]
      if type
        definition = @catalog[:resist_berry]
        qualifies = type == definition[:allow_resisted_type] || matchup(type.to_sym) >= definition[:minimum_matchup]
        add_rule(definition[:id], definition.merge(types: [type]), @item_active && qualifies, source)
      end
      if !@item_definition.empty?
        add_rule(@item_definition[:id] || "item_#{@item}", @item_definition, @item_active, source)
      end
    end

    def field_rules
      return if !@battle
      [@catalog[:weather].fetch(@weather, []), @catalog[:terrain].fetch(@terrain, [])].flatten.each do |definition|
        add_rule(definition[:id], definition)
      end
      @catalog[:side].each do |effect, definition|
        add_rule(definition[:id] || effect, definition) if @side[effect]
      end
      @catalog[:sports].each do |effect, definition|
        active = field?(definition[:field].to_sym)
        @battle.eachBattler { |battler| active ||= battler.effects[PBEffects.const_get(effect)] == true }
        next if !active
        add_rule(effect, { section: :modifiers, factor: @catalog[:parameters][:sport_factor], types: [definition[:type]] })
      end
      factor_key = @battle.pbSideBattlerCount(@battler) > 1 ? :screen_multi_factor : :screen_single_factor
      @catalog[:screens].each do |effect, definition|
        next if !@side[effect]
        active = !definition[:disabled_by] || !@side[definition[:disabled_by].to_sym]
        add_rule(effect, definition.merge(section: :modifiers, factor: @catalog[:parameters][factor_key]), active)
      end
      ally_rules
    end
  end
end
