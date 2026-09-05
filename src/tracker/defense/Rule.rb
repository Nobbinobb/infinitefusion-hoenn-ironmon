module Ironmon
  # A presentation rule carries its mechanics directly. Neither labels nor
  # serialized identifiers participate in deciding which effects apply.
  class DefenseRule
    attr_reader :id, :section, :active, :types, :category, :factor,
                :conditional, :protections, :recovery, :moves

    def initialize(id, definition, active, moves, source = nil)
      @id = id
      @section = definition.fetch(:section, :other_protections).to_sym
      @active = active
      @types = definition.fetch(:types, []).map(&:to_sym)
      @category = definition[:category]&.to_sym
      @factor = definition[:factor]
      @conditional = definition.fetch(:conditional, false)
      @protections = definition.fetch(:protections, [])
      @recovery = definition[:recovery]
      @moves = moves
      @source = source
    end

    # Factors are relative presentation values, not rounded HP damage.
    def factor_for(type, category)
      return nil if @active == false || (@category && @category != category)
      return nil if !@types.empty? && !@types.include?(type)
      return @factor.is_a?(Hash) ? @factor[type] : @factor
    end

    def snapshot
      return {
        "id" => @id.to_s, "source" => @source ? @source.name : "",
        "description" => @source && @source.respond_to?(:description) ? @source.description : "",
        "active" => @active, "attack_types" => @types.map(&:to_s), "moves" => @moves
      }
    end

    # Only unconditional-in-type numeric rules can be moved out of the chart.
    # Contact, selected types and move-specific rules remain attached to types.
    def all_type_factor?
      return @active != false && !@conditional && @types.empty? && @factor.is_a?(Numeric) && @factor != 1 && @moves.empty?
    end

    def factor_snapshot
      label = @source ? @source.name : @id.to_s.gsub(/([a-z])([A-Z])/, '\1 \2').tr('_', ' ')
      return { "label" => label, "category" => @category&.to_s,
               "factor" => @factor, "conditional" => @conditional || @active.nil? }
    end
  end
end
