module Ironmon
  # Presentation only. Battle handlers are not invoked on live battlers because
  # those handlers can animate items, reveal abilities or mutate battle state.
  class DefenseOverview
    def initialize(pokemon, battler = nil, enemy = false, revealed_ability = nil)
      @catalog = DefenseCatalog.load
      @attack_types = @catalog[:attack_types].map(&:to_sym)
      @chart = @catalog[:chart]
      @pokemon, @battler, @enemy = pokemon, battler, enemy
      @battle = battler ? battler.battle : nil
      @types = (battler ? battler.pbTypes(true) : [pokemon.type1, pokemon.type2]).compact.uniq
      @ability = if enemy
                   revealed_ability && revealed_ability["id"].to_sym
                 elsif battler
                   battler.ability_id
                 else
                   pokemon.ability_id
                 end
      @ability_definition = @catalog[:abilities].fetch(@ability, {})
      @effects = read_effects(battler && battler.effects, @catalog[:public_effects])
      @side = read_effects(battler && battler.pbOwnSide.effects, @catalog[:side_effects])
      @field = read_effects(@battle && @battle.field.effects, @catalog[:field_effects])
      # Only this boundary may read private player state. Enemy counterparts
      # remain nil even if a generated rule asks for a related condition.
      @item = enemy ? nil : (battler ? battler.item_id : pokemon.item)
      @item = @item.id if @item.respond_to?(:id)
      @item_definition = @catalog[:items].fetch(@item, {})
      @hp = enemy ? nil : (battler ? battler.hp : pokemon.hp)
      @totalhp = enemy ? nil : (battler ? battler.totalhp : pokemon.totalhp)
      @status = enemy ? nil : (battler ? battler.status : pokemon.status)
      @weather = @battle ? @battle.field.weather : :None
      @terrain = @battle ? @battle.field.terrain : :None
      @ability_active = !effect?(:GastroAcid)
      @item_active = !effect?(:Embargo) && !field?(:MagicRoom) && !ability_effect?(:disables_item)
      @grounded = item_effect?(:grounds) || @chart[:grounding_effects].any? { |id| effect?(id.to_sym) } ||
        field?(@chart[:grounding_field].to_sym)
      @airborne = !@grounded && (@types.include?(@chart[:airborne_type].to_sym) || independent_airborne?)
      @terrain_affected = !@airborne && !(battler && battler.semiInvulnerable?)
      @rules = []
      @result = {
        "in_battle" => !battler.nil?, "limited_information" => enemy,
        "ability_name" => @ability && GameData::Ability.get(@ability).name,
        "ability_description" => @ability && GameData::Ability.get(@ability).description,
        "ability_suppressed" => !!(@ability && !@ability_active),
        "type_matchups" => [], "modifiers" => [], "status_protections" => [],
        "move_protections" => [], "other_protections" => []
      }
    end

    def snapshot
      resolve_known_weather
      build_matchups
      ability_rules
      status_rules
      item_rules
      field_rules
      state_rules
      build_damage_profiles
      build_compact_effects
      @result.each_value do |value|
        value.sort_by! { |row| row["id"] } if value.is_a?(Array) && value.first && value.first.key?("id")
      end
      return @result
    end

    def read_effects(effects, names)
      return {} if !effects
      names.each_with_object({}) do |name, result|
        value = effects[PBEffects.const_get(name)]
        result[name.to_sym] = value == true || (value.is_a?(Numeric) && value > 0)
      end
    end

    def effect?(name); return !!@effects[name]; end
    def field?(name); return !!@field[name]; end
    def ability_effect?(key); return @ability_active && !!@ability_definition[key]; end
    def item_effect?(key); return @item_active && !!@item_definition[key]; end

    def independent_airborne?
      return ability_effect?(:airborne) || item_effect?(:airborne) ||
        @chart[:airborne_effects].any? { |id| effect?(id.to_sym) }
    end

    # Public metadata only; used by audit validation, not to infer enemy moves.
    def self.flagged_moves(flag)
      moves = []
      GameData::Move.each { |move| moves << move.name if move.flags.include?(flag) }
      return moves.sort
    end

    def known_battler_ability(battler)
      if battler.opposes?
        known = Ironmon.instance_variable_get(:@tracker_enemy_abilities)
        entry = known && known[battler.index]
        return entry && entry["id"].to_sym
      end
      player = Ironmon.instance_variable_get(:@tracker_player_battler)
      return battler.ability_id if battler.equal?(player) || (!@enemy && battler.equal?(@battler))
      return nil
    end

    def resolve_known_weather
      return if !@battle
      @battle.eachBattler do |battler|
        known = known_battler_ability(battler)
        next if !known || battler.effects[PBEffects::GastroAcid]
        next if !@catalog[:abilities].fetch(known, {})[:suppresses_weather]
        @weather = :None
        break
      end
    end

    def base_multiplier(attack)
      return @types.inject(1.0) do |value, defense|
        value * Effectiveness.calculate_one(attack, defense).to_f / Effectiveness::NORMAL_EFFECTIVE_ONE
      end
    end

    def matchup(attack)
      ground = @chart[:ground_attack].to_sym
      airborne_type = @chart[:airborne_type].to_sym
      return 1.0 if attack == ground && @types.include?(airborne_type) && item_effect?(:ground_matchup_override)
      @types.inject(1.0) do |value, defense|
        factor = Effectiveness.calculate_one(attack, defense).to_f / Effectiveness::NORMAL_EFFECTIVE_ONE
        factor = 1.0 if factor == 0 && item_effect?(:removes_chart_immunity)
        @chart[:removed_immunities].each do |entry|
          if defense.to_s == entry[:defense_type] && entry[:attack_types].include?(attack.to_s) && effect?(entry[:effect].to_sym)
            factor = 1.0
          end
        end
        factor = 1.0 if factor > 1 && @chart[:weather_neutralizes][@weather] == defense.to_s
        factor = 1.0 if defense == airborne_type && attack == ground && @grounded
        value * factor
      end
    end

    def build_matchups
      @attack_types.each do |attack|
        type = GameData::Type.try_get(attack)
        next if !type
        value = matchup(attack)
        value = 0.0 if @ability_active && @ability_definition[:immunity] == attack.to_s
        value = 0.0 if attack.to_s == @chart[:ground_attack] && !@grounded && independent_airborne?
        value = 0.0 if ability_effect?(:requires_super_effective) && value <= 1
        value = 0.0 if @chart[:weather_blocks][@weather] == attack.to_s
        @result["type_matchups"] << {
          "type" => attack.to_s, "name" => type.name, "base_multiplier" => base_multiplier(attack), "multiplier" => value
        }
      end
    end
  end

  def self.tracker_defense_snapshot(pokemon, battler = nil, enemy = false)
    known = enemy && battler && @tracker_enemy_abilities ? @tracker_enemy_abilities[battler.index] : nil
    return DefenseOverview.new(pokemon, battler, enemy, known).snapshot
  rescue StandardError => e
    echoln "Ironmon defense overview failed safely: #{e.message}"
    return nil
  end
end
