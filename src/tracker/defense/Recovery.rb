module Ironmon
  class DefenseOverview
    # Old informational catalogs keep their original tuple representation.
    def recovery_events(value)
      return value if value.empty? || value.first.is_a?(Hash)
      trigger, outcome = value
      hp = outcome.is_a?(Array)
      return [{ trigger: trigger, outcomes: [hp ? { kind: "hp_fraction", amount: outcome } :
        { kind: "text", label: outcome }], per_turn: hp, legacy: true }]
    end

    def append_recovery(event, active)
      return if event[:berry] && known_opponent_effect?(:blocks_berries)
      details = event[:outcomes].filter_map do |outcome|
        hp = outcome[:kind].start_with?("hp_")
        next if hp && effect?(:HealBlock) && !event[:ignore_heal_block]
        text = recovery_outcome(outcome, event)
        text = recovery_format(:per_turn, text) if event[:per_turn] && !event[:legacy]
        text = recovery_format(:chance, event[:chance], text) if event[:chance]
        text = recovery_format(:once, text) if event[:once]
        text
      end
      return if details.empty?
      if event[:confusion_flavor] && !@enemy &&
         @pokemon.nature.stat_changes.any? { |stat, change| stat.to_s == event[:confusion_flavor] && change < 0 }
        details << recovery_format(:confusion_warning)
      end
      @result["recovery"] << {
        "label" => recovery_trigger(event), "active" => active, "moves" => [], "healing_amounts" => details
      }
    end

    def recovery_outcome(outcome, event)
      return effect_label(outcome[:label]) if outcome[:kind] == "text"
      if event[:legacy]
        return _INTL(@catalog[:parameters][:healing_format].dup, *outcome[:amount])
      end
      text = recovery_format(outcome[:kind].to_sym, *Array(outcome[:amount]))
      boost = @item_active && @item_definition[:recovery_multiplier]
      text = recovery_format(:boost, text, boost) if event[:drain_boost] && boost
      return text
    end

    def recovery_format(key, *values)
      return _INTL(@catalog[:recovery_formats].fetch(key).dup, *values)
    end

    def recovery_trigger(event)
      count = event[:countdown]
      return effect_label(event[:trigger]) if !count
      label = @catalog[:recovery_formats][:countdown_labels][count.to_s.to_sym]
      return label ? effect_label(label) : recovery_format(:countdown, count - 1)
    end

    def known_opponent_effect?(flag)
      return false if !@battle
      @battle.eachBattler do |other|
        next if !other.opposes?(@battler)
        return true if known_ability_effect?(other, flag)
      end
      return false
    end

    def known_ability_effect?(battler, flag)
      known = battler.equal?(@battler) ? @ability : known_battler_ability(battler)
      return false if !known || battler.effects[PBEffects::GastroAcid]
      return !!@catalog[:abilities].fetch(known, {})[flag]
    end

    # Read only public counters and recipient links, never WishAmount, source HP,
    # concealed items/status, or an unrevealed source ability.
    def pending_recovery_rules
      position = @battle.positions[@battler.index]
      @catalog.fetch(:position_recovery, {}).each do |effect, definition|
        value = position && position.effects[PBEffects.const_get(effect)]
        next if value != true && !(value.is_a?(Numeric) && value > 0)
        events = definition[:recovery].map do |event|
          definition[:countdown] ? event.merge(countdown: value) : event
        end
        add_rule("position_#{effect}", definition.merge(recovery: events))
      end
      @catalog.fetch(:linked_recovery, {}).each do |effect, definition|
        @battle.eachBattler do |other|
          next if other.effects[PBEffects.const_get(effect)] != @battler.index
          next if definition.fetch(:blocked_by, []).any? { |flag| known_ability_effect?(other, flag.to_sym) }
          add_rule("linked_#{effect}_#{other.index}", definition)
        end
      end
    end
  end
end
