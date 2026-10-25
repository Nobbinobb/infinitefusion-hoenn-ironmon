# Tracker-owned toggle profiles; the native hold-mode sliders remain authoritative.
module Ironmon
  module ToggleSpeed
    LANES = ["overworld", "battle"].freeze
    MAXIMUM_STAGES = 128

    def self.default_configuration
      lanes = {}
      LANES.each do |lane|
        prefix = lane == "overworld" ? "ow" : "bt"
        lanes[lane] = [1, 2, 3].map do |value|
          { "id" => "#{prefix}-group-#{value}", "nodes" => [
            { "id" => "#{prefix}-#{value}", "value" => value }
          ] }
        end
      end
      { "lanes" => lanes, "links" => [2, 3].map do |value|
        { "id" => "matching-#{value}", "overworld" => "ow-#{value}", "battle" => "bt-#{value}" }
      end }
    end

    def self.identifier?(value)
      value.is_a?(String) && value.length.between?(1, 64) && /\A[A-Za-z0-9_-]+\z/.match?(value)
    end

    def self.validate(candidate)
      raise ArgumentError, "Speed profile requires lanes and connections." if
        !candidate.is_a?(Hash) || !candidate["lanes"].is_a?(Hash) || !candidate["links"].is_a?(Array)
      groups = {}; nodes = {}; indexes = {}
      LANES.each do |lane|
        list = candidate["lanes"][lane]
        raise ArgumentError, "Speed lane is empty or too large." if
          !list.is_a?(Array) || !list.length.between?(1, MAXIMUM_STAGES)
        count = 0; indexes[lane] = {}
        list.each_with_index do |group, index|
          raise ArgumentError, "Speed group is invalid." if !group.is_a?(Hash) ||
            !identifier?(group["id"]) || groups[group["id"]] || !group["nodes"].is_a?(Array) || group["nodes"].empty?
          groups[group["id"]] = true
          count += group["nodes"].length
          raise ArgumentError, "Speed lane has too many stages." if count > MAXIMUM_STAGES
          raise ArgumentError, "Normal speed must be first and standalone." if index == 0 && group["nodes"].length != 1
          group["nodes"].each do |node|
            raise ArgumentError, "Speed stage is invalid." if !node.is_a?(Hash) ||
              !identifier?(node["id"]) || nodes[node["id"]] || !node["value"].is_a?(Integer) ||
              (index == 0 ? node["value"] != 1 : !node["value"].between?(2, 10))
            nodes[node["id"]] = true
            indexes[lane][node["id"]] = [group["id"], node["value"]]
          end
        end
      end
      connected = {}; wires = {}
      raise ArgumentError, "Too many speed connections." if candidate["links"].length >= MAXIMUM_STAGES
      candidate["links"].each do |link|
        raise ArgumentError, "Speed connection is invalid." if
          !link.is_a?(Hash) || !identifier?(link["id"]) || wires[link["id"]]
        wires[link["id"]] = true
        LANES.each do |lane|
          entry = indexes[lane][link[lane]]
          raise ArgumentError, "Each accelerated group needs exactly one connection." if
            !entry || entry[1] == 1 || connected[entry[0]]
          connected[entry[0]] = true
        end
      end
      indexes.each_value do |lane|
        lane.each_value do |entry|
          raise ArgumentError, "Every multiplier or group needs a connection before saving." if entry[1] != 1 && !connected[entry[0]]
        end
      end
      Marshal.load(Marshal.dump(candidate))
    end

    def self.configuration
      Marshal.load(Marshal.dump(@configuration))
    end

    def self.configure(candidate)
      profile = validate(candidate)
      return configuration if profile == @configuration
      @configuration = profile; @memory ||= {}
      @cycles = {}; @nodes = {}; @destinations = {}
      LANES.each do |lane|
        @cycles[lane] = profile["lanes"][lane].flat_map do |group|
          group["nodes"].map do |node|
            @nodes[node["id"]] = { :lane => lane, :group => group["id"], :value => node["value"] }
            node["id"]
          end
        end
      end
      profile["links"].each do |link|
        @destinations[@nodes[link["overworld"]][:group]] = link["battle"]
        @destinations[@nodes[link["battle"]][:group]] = link["overworld"]
      end
      @memory.delete_if { |group, id| !@nodes[id] || @nodes[id][:group] != group }
      @context ||= current_context
      @current = @cycles[@context][0] if !@nodes[@current] || @nodes[@current][:lane] != @context
      @refresh = true
      configuration
    end

    def self.current_context
      $game_temp && $game_temp.in_battle ? "battle" : "overworld"
    end

    def self.remember
      node = @nodes[@current]
      @memory[node[:group]] = @current if node && node[:value] != 1
    end

    def self.before_update
      if !$PokemonSystem || $PokemonSystem.speedup != 1
        @was_toggle = false
        return false
      end
      if @was_toggle && !@refresh
        @current = @cycles[@context][$GameSpeed] || @cycles[@context][0]
        remember
      end
      context = current_context
      if context != @context
        remember
        source = @nodes[@current]
        if source[:value] == 1
          @current = @cycles[context][0]
        else
          entry = @destinations.fetch(source[:group])
          @current = @memory[@nodes.fetch(entry)[:group]] || entry
        end
        @context = context; @refresh = true
      end
      if @refresh || !@was_toggle
        SPEEDUP_STAGES.replace(@cycles[@context].map { |id| @nodes[id][:value] })
        $GameSpeed = @cycles[@context].index(@current) || 0
        $frame = 0; @refresh = false
      end
      $frame = 0 if $CanToggle && Input.trigger?(Input::X)
      @was_toggle = true
      true
    end

    def self.after_update
      @current = @cycles[@context][$GameSpeed] || @cycles[@context][0]
      remember
    end

    def self.overlay_state
      return nil if Ironmon::MoveLearningProtection.active?
      return nil if !$PokemonSystem || $PokemonSystem.speedup != 1 || !@was_toggle
      cycle = @cycles[@context]
      index = $GameSpeed.to_i
      index = 0 if !index.between?(0, cycle.length - 1)
      return [@nodes[cycle[index]][:value], index, cycle.length]
    end

    def self.reset
      @configuration = nil; @memory = {}; @current = nil
      @context = current_context; @was_toggle = false
      configure(default_configuration)
    end

    module GraphicsUpdates
      def update
        if Ironmon::MoveLearningProtection.active?
          return Ironmon::MoveLearningProtection.normal_speed_frame { super }
        end
        toggling = Ironmon::ToggleSpeed.before_update
        super
      ensure
        Ironmon::ToggleSpeed.after_update if toggling
      end
    end
    reset
  end
end
Graphics.singleton_class.prepend(Ironmon::ToggleSpeed::GraphicsUpdates)
