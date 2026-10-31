module Ironmon
  module ResponsiveInput
    NATIVE_REPEAT_DELAY = 0.4
    NATIVE_REPEAT_INTERVAL = 0.1
    TRACKER_REPEAT_DELAY = 0.6
    TRACKER_REPEAT_INTERVAL = 0.15
    NATIVE_KEYS = Input.constants.map { |name| Input.const_get(name) }.
      select { |value| value.is_a?(Integer) }.uniq.freeze
    NATIVE_IDENTITIES = NATIVE_KEYS.each_with_object({}) do |key, identities|
      identities[key] = [:native, key].freeze
    end.freeze
    TRACKER_IDENTITIES = Ironmon::TRACKER_CONTROL_KEYS.each_key.each_with_object({}) do |action, identities|
      identities[action] = [:tracker, action].freeze
    end.freeze

    @native_press = Input.method(:press?).super_method

    def self.now
      return System.uptime.to_f / 1_000_000.0
    end

    def self.sequence
      return @sequence.to_i
    end

    def self.accelerated?
      return defined?(Ironmon::SpeedUp) && Ironmon::SpeedUp.multiplier > 1
    end

    def self.reset
      @sequence = 0
      # Tokens are reused, so identity lookup avoids the bundled Ruby value-hash
      # path and its seed-dependent allocations.
      @states = {}.compare_by_identity
    end

    def self.update_state(identity, pressed, time, delay, interval)
      if !pressed
        @states.delete(identity)
        return
      end
      state = @states[identity]
      if !state
        @states[identity] = { :started => time, :next_repeat => time + delay,
          :repeat => true, :trigger => true }
        return
      end
      state[:trigger] = false
      state[:repeat] = time >= state[:next_repeat]
      if state[:repeat]
        # Long frames produce one action, never a burst of missed repeats.
        state[:next_repeat] = time + interval
      end
    end

    def self.update
      @sequence = sequence + 1
      time = now
      NATIVE_KEYS.each do |key|
        update_state(NATIVE_IDENTITIES[key], @native_press.call(key), time,
          NATIVE_REPEAT_DELAY, NATIVE_REPEAT_INTERVAL)
      end
      frames = Ironmon.instance_variable_get(:@tracker_control_frames) || {}
      active = Ironmon.tracker_controls_active?
      Ironmon::TRACKER_CONTROL_KEYS.each_key do |action|
        identity = TRACKER_IDENTITIES[action]
        @states.delete(identity) if frames.fetch(action, 0) == 1
        update_state(identity, active && frames.fetch(action, 0) > 0,
          time, TRACKER_REPEAT_DELAY, TRACKER_REPEAT_INTERVAL)
      end
    end

    def self.identity(key)
      return TRACKER_IDENTITIES[Ironmon.tracker_control_action(key)] if
        Ironmon.tracker_control_managed?(key)
      return NATIVE_IDENTITIES[key] || [:native, key]
    end

    def self.repeat?(key)
      state = @states[identity(key)]
      return state && state[:repeat] ? true : false
    end

    def self.held_seconds(key)
      state = @states[identity(key)]
      return 0.0 if !state
      elapsed = now - state[:started]
      return elapsed > 0.0 ? elapsed : 0.0
    end

    def self.quick_action_triggered?(action, repeat = false)
      state = @states[TRACKER_IDENTITIES[action]]
      return false if !state
      return repeat ? state[:repeat] : state[:trigger]
    end

    module InputUpdates
      def update
        result = super
        Ironmon::ResponsiveInput.update
        return result
      end

      def repeat?(key)
        return Ironmon::ResponsiveInput.repeat?(key) if Ironmon::ResponsiveInput.accelerated?
        return super
      end

      def time?(key)
        return Ironmon::ResponsiveInput.held_seconds(key) * 1000.0 if
          Ironmon::ResponsiveInput.accelerated? && Ironmon.tracker_control_managed?(key)
        return super
      end
    end

    reset
  end
end

Input.singleton_class.prepend(Ironmon::ResponsiveInput::InputUpdates)
