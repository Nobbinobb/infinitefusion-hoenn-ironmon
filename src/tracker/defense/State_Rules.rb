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
      @catalog[:stages].each do |stat, category|
        stage = @battler.stages[stat]
        next if !stage || stage == 0
        base = @catalog[:parameters][:stage_base].to_f
        scale = stage > 0 ? (base + stage) / base : base / (base - stage)
        add_rule("stage_#{stat}", { section: :modifiers, factor: 1.0 / scale, category: category })
      end
    end
  end
end
