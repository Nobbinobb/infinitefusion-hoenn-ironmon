module Ironmon
  class DefenseOverview
    # Project semantic effects, without reverse-mapping serialized IDs or names.
    def build_compact_effects
      protections = @rules.reject { |rule| rule.active == false }.flat_map do |rule|
        effects = rule.protections.flat_map do |effect|
          [effect] + @catalog.fetch(:protection_implications, {}).fetch(effect.to_sym, [])
        end
        effects.map do |effect|
          { "label" => effect_label(effect), "active" => rule.active, "moves" => rule.moves }
        end
      end
      @result["protections"] = protections.group_by { |row| row["label"] }.map do |label, rows|
        { "label" => label, "active" => rows.any? { |row| row["active"] } ? true : nil,
          "moves" => rows.flat_map { |row| row["moves"] }.uniq.sort }
      end.sort_by { |row| row["label"] }
      @result["recovery"] = []
      @rules.each do |rule|
        next if rule.active == false || !rule.recovery
        recovery_events(rule.recovery).each { |event| append_recovery(event, rule.active) }
      end
    end

    def effect_label(id)
      return @catalog[:labels].fetch(id.to_sym)
    end
  end
end
