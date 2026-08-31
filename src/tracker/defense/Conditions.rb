module Ironmon
  class DefenseOverview
    # Evaluate the audit's small condition language, never executable Ruby.
    # Concealed values propagate as nil rather than consulting enemy state.
    def defense_condition(name)
      return true if !name
      return condition_value(@catalog[:conditions].fetch(name.to_sym))
    end

    def condition_value(node)
      return node if !node.is_a?(Hash)
      return nil if node[:battle_only] && !@battle
      return condition_field(node[:field]) if node[:field] && !node[:op]
      case node[:op].to_sym
      when :effect then return effect?(node[:id].to_sym)
      when :species then return @pokemon.isSpecies?(node[:id].to_sym)
      when :fusion then return @pokemon.isFusionOf(node[:id].to_sym)
      when :not
        value = condition_value(node[:value])
        return value.nil? ? nil : !value
      when :all, :any
        values = node[:values].map { |entry| condition_value(entry) }
        return false if node[:op] == "all" && values.include?(false)
        return true if node[:op] == "any" && values.include?(true)
        return nil if values.include?(nil)
        return node[:op] == "all"
      when :includes
        values = condition_field(node[:field])
        return values.nil? ? nil : values.include?(node[:value])
      when :in
        value = condition_value(node[:value])
        return value.nil? ? nil : node[:set].include?(value)
      when :equal, :less_than
        left = condition_value(node[:left])
        right = condition_value(node[:right])
        return nil if left.nil? || right.nil?
        return node[:op] == "equal" ? left == right : left < right
      end
      raise "Unknown defense condition operator #{node[:op]}"
    end

    # This is the allowlist of observable inputs the audit can inspect.
    def condition_field(name)
      case name.to_sym
      when :hp then return @hp
      when :total_hp then return @totalhp
      when :status then return @status && @status.to_s
      when :weather then return @weather.to_s
      when :terrain then return @terrain.to_s
      when :types then return @types.map(&:to_s)
      when :grounded then return @grounded
      when :airborne then return @airborne
      when :two_turn_function
        move = @battler && @battler.effects[PBEffects::TwoTurnAttack]
        return move ? GameData::Move.try_get(move)&.function_code : ""
      when :terrain_affected then return @terrain_affected
      when :form then return @battler ? @battler.form : @pokemon.form
      when :evolvable then return !@pokemon.species_data.get_evolutions(true).empty?
      when :transformed then return !!(@battler && @battler.effects[PBEffects::Transform])
      when :soul_dew_clause then return !!(@battle && @battle.rules["souldewclause"])
      when :soul_dew_powers_types then return Settings::SOUL_DEW_POWERS_UP_TYPES
      end
      raise "Unknown defense context field #{name}"
    end

    def add_rule(id, definition, enabled = true, source = nil)
      active = enabled == false ? false : defense_condition(definition[:condition])
      active = nil if enabled.nil? && active != false
      if definition[:super_effective]
        definition = definition.merge(types: @attack_types.select { |type| matchup(type) > 1 })
        active = false if definition[:types].empty?
      end
      rule = DefenseRule.new(id, definition, active, definition.fetch(:move_names, []), source)
      @rules << rule
      @result[rule.section.to_s] << rule.snapshot
      definition.fetch(:additional, []).each_with_index do |child, index|
        add_rule("#{id}_#{index}", child, active, source)
      end
      return rule
    end
  end
end
