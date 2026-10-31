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
        return Ironmon::SpeedUp.update { fast_forward_update }
      end

      def update_KGC_SpecialTransition
        return Ironmon::SpeedUp.native_frame { super }
      end

      def frame_rate
        return Ironmon::SpeedUp.base_rate
      end

      def frame_rate=(value)
        Ironmon::SpeedUp.base_rate = value
      end

      def delta_s
        return 1.0 / frame_rate if Ironmon::SpeedUp.multiplier > 1
        return super
      end

      def freeze
        result = super
        Ironmon::SpeedUp.frozen = true
        return result
      end

      def transition(*args)
        Ironmon::SpeedUp.frozen = false
        return super
      ensure
        Ironmon::SpeedUp.frozen = false
      end

      def __reset__
        result = super
        Ironmon::SpeedUp.reset_engine
        return result
      end
    end
    reset
  end

  # Pace logical ticks independently of presentation, using the bundled
  # engine's 120 Hz limit and native skipping for slower displays.
  module SpeedUp
    NATIVE_RATE = Graphics.method(:frame_rate) unless const_defined?(:NATIVE_RATE, false)
    SET_NATIVE_RATE = Graphics.method(:frame_rate=) unless const_defined?(:SET_NATIVE_RATE, false)
    SET_NATIVE_FRAMESKIP = Graphics.method(:frameskip=) unless const_defined?(:SET_NATIVE_FRAMESKIP, false)
    ORIGINAL_FRAMESKIP = Graphics.frameskip unless const_defined?(:ORIGINAL_FRAMESKIP, false)
    NATIVE_UPDATE_LIMIT = 120
    STALL_SECONDS = 0.1
    RAPID_STAGE_SECONDS = 0.2
    STAGE_SETTLE_SECONDS = 0.08
    @base_rate = NATIVE_RATE.call
    @multiplier = 1
    @phase = 0
    @frozen = false

    class << self
      attr_reader :base_rate, :multiplier
      attr_accessor :frozen
    end

    def self.base_rate=(value)
      @base_rate = [value.to_i, 1].max
      @target_rate = nil
      apply_rate(true)
    end

    def self.now
      return Process.clock_gettime(Process::CLOCK_MONOTONIC)
    end

    def self.native_update_limit
      return NATIVE_UPDATE_LIMIT
    end

    def self.apply_rate(force_native = false)
      target = @base_rate * @multiplier
      limit = native_update_limit
      requested = target < limit ? target : limit
      actual = NATIVE_RATE.call
      skipping = @multiplier > 1 || ORIGINAL_FRAMESKIP
      return if @target_rate == target && @requested_native_rate == requested &&
        actual == @native_rate && @applied_frameskip == skipping && !@pending_native_rate

      previous_target = @target_rate
      stamp = now
      visual = defined?(Ironmon::SpeedTransitions) && Ironmon::SpeedTransitions.active?
      deferred = requested > actual && !force_native && !visual && @native_rate == actual &&
        @coalesce_until && stamp < @coalesce_until
      if deferred
        @pending_native_rate = requested
        applied = actual
      else
        applied = requested
      end
      reconfigured = @native_rate.nil? || applied != actual
      if reconfigured
        SET_NATIVE_RATE.call(applied)
        @native_rate = NATIVE_RATE.call
        Graphics.frame_reset
      else
        @native_rate = actual
      end
      if @applied_frameskip != skipping
        SET_NATIVE_FRAMESKIP.call(skipping)
        @applied_frameskip = skipping
      end
      if previous_target && previous_target > 0
        # Preserve progress toward the next presentation when only the logical
        # multiplier changes; restarting the limiter here forces excess waits.
        @phase = @phase * target / previous_target
      else
        @phase = target - @native_rate
      end
      @target_rate = target
      @requested_native_rate = requested
      if !deferred
        @pending_native_rate = nil
      end
      return reconfigured
    end

    def self.reset_engine
      @target_rate = nil
      @last_tick = nil
      @frozen = false
      @toggle_sequence = nil
      @pending_native_rate = nil
      @coalesce_until = nil
      @last_speed_edge_at = nil
      @applied_frameskip = nil
      reconfigured = apply_rate(true)
      Graphics.frame_reset if !reconfigured
    end

    def self.refresh_rate
      protected = Ironmon::MoveLearningProtection.active?
      Ironmon::ToggleSpeed.before_update if !protected
      Ironmon::SpeedClock.synchronize_native_frames if defined?(Ironmon::SpeedClock)
      @multiplier = selected_multiplier(protected)
      apply_rate(true)
      Ironmon::SpeedClock.synchronize_native_frames if defined?(Ironmon::SpeedClock)
    end

    def self.resume_after_loading
      @last_tick = nil
      @phase = @target_rate - @native_rate if @target_rate && @native_rate
      Graphics.frame_reset
    end

    def self.update
      return Ironmon::StartupPreparation.render_frame if
        defined?(Ironmon::StartupPreparation) && Ironmon::StartupPreparation.active?
      protected = Ironmon::MoveLearningProtection.active?
      mode = $PokemonSystem && $PokemonSystem.speedup
      battle = !!($game_temp && $game_temp.in_battle)
      boundary = @previous_protection != protected || @previous_mode != mode || @previous_battle != battle
      @previous_protection = protected
      @previous_mode = mode
      @previous_battle = battle
      toggling = Ironmon::ToggleSpeed.before_update if !protected
      sequence = defined?(Ironmon::ResponsiveInput) ? Ironmon::ResponsiveInput.sequence : nil
      fresh = sequence != @toggle_sequence
      @toggle_sequence = sequence
      if !protected && $CanToggle && fresh && Input.trigger?(Input::X) &&
          $PokemonSystem && $PokemonSystem.speedup == 1
        $GameSpeed = ($GameSpeed.to_i + 1) % SPEEDUP_STAGES.length
        stamp = now
        @coalesce_until = stamp + STAGE_SETTLE_SECONDS if @last_speed_edge_at && stamp - @last_speed_edge_at < RAPID_STAGE_SECONDS
        @last_speed_edge_at = stamp
      end
      @multiplier = selected_multiplier(protected)
      apply_rate(boundary || mode != 1)
      stamp = now
      if @last_tick && stamp - @last_tick > STALL_SECONDS
        Graphics.frame_reset
        @phase = @target_rate - @native_rate
      end
      @last_tick = stamp
      $frame = 0
      Ironmon::SpeedClock.begin_tick if defined?(Ironmon::SpeedClock)
      tick_open = true
      yield
      Ironmon::SpeedClock.advance(@multiplier, @frozen) if defined?(Ironmon::SpeedClock)
      tick_open = false
      Ironmon.run_graphics_update_hooks
    ensure
      Ironmon::SpeedClock.cancel_tick if tick_open && defined?(Ironmon::SpeedClock)
      Ironmon::ToggleSpeed.after_update if toggling
    end

    def self.selected_multiplier(protected = false)
      value = configured_multiplier(protected)
      return Ironmon::SpeedTransitions.constrain_multiplier(value) if defined?(Ironmon::SpeedTransitions)
      return value
    end

    def self.configured_multiplier(protected = false)
      return 1 if protected || !$PokemonSystem
      if $PokemonSystem.speedup == 1
        $GameSpeed = 0 if !$GameSpeed.to_i.between?(0, SPEEDUP_STAGES.length - 1)
        value = SPEEDUP_STAGES[$GameSpeed.to_i].to_i
        return value > 0 ? value : 1
      end
      return 1 if !$CanToggle || !Input.press?(Input::X)
      return Graphics.get_speedup_speed + 1
    end

    def self.native_frame
      @phase += @native_rate || @base_rate
      if @phase >= @target_rate.to_i
        @phase -= @target_rate.to_i
        return yield
      end
      if !@frozen
        if defined?(Ironmon::SpeedClock)
          Ironmon::SpeedClock.set_raw_frame_count(Ironmon::SpeedClock.raw_frame_count + 1)
        else
          Graphics.frame_count += 1
        end
      end
      return nil
    end
  end
end
Graphics.singleton_class.prepend(Ironmon::ToggleSpeed::GraphicsUpdates)
