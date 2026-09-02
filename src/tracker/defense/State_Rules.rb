module Ironmon
  class DefenseOverview
    def state_rules
      @catalog[:effects].each do |effect, definition|
        add_rule(definition[:id] || effect, definition) if effect?(effect)
      end
      @catalog.fetch(:state_conditions, []).each do |definition|
        add_rule(definition[:id], definition)
      end
      return if !@battle
      pending_recovery_rules
    end
  end
end
