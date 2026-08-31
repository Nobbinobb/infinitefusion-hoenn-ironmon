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
  end
end
