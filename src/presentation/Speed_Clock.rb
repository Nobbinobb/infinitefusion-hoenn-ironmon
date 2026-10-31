module Ironmon
  module SpeedClock
    RAW_FRAME_COUNT = Graphics.method(:frame_count) unless const_defined?(:RAW_FRAME_COUNT, false)
    SET_RAW_FRAME_COUNT = Graphics.method(:frame_count=) unless const_defined?(:SET_RAW_FRAME_COUNT, false)
    SAVE_VERSION = 1
    LOGICAL_SAVE_KEY = :ironmon_logical_frame_count
    VERSION_SAVE_KEY = :ironmon_speed_clock_version
    ROUNDING_EPSILON = 0.000001
    @playtime_frames = RAW_FRAME_COUNT.call.to_f
    @last_raw_frame = RAW_FRAME_COUNT.call
    @native_scale = 1.0
    @tick_depth = 0
    @display_scopes = []
    @tick_sequence = 0
    @reset_sequence = 0

    def self.raw_frame_count
      return RAW_FRAME_COUNT.call
    end

    def self.set_raw_frame_count(value)
      return SET_RAW_FRAME_COUNT.call(value)
    end

    def self.native_scale
      return 1.0 if !defined?(Ironmon::SpeedUp)
      return Ironmon::SpeedUp::NATIVE_RATE.call.to_f / Ironmon::SpeedUp.base_rate
    end

    def self.synchronize_native_frames
      return if @tick_depth > 0
      current = raw_frame_count
      difference = current - @last_raw_frame
      @playtime_frames += difference / @native_scale if difference > 0
      @last_raw_frame = current
      @native_scale = native_scale
    end

    def self.begin_tick
      synchronize_native_frames
      @tick_depth += 1
      @tick_sequence += 1
    end

    def self.advance(multiplier, frozen = false)
      speed = multiplier.to_i
      speed = 1 if speed < 1
      @playtime_frames += 1.0 / speed if !frozen
      @last_raw_frame = raw_frame_count
      @native_scale = native_scale
      @tick_depth -= 1 if @tick_depth > 0
    end

    def self.cancel_tick
      rebase_native_frames
      @tick_depth -= 1 if @tick_depth > 0
    end

    def self.rebase_native_frames
      @last_raw_frame = raw_frame_count
      @native_scale = native_scale
    end

    def self.reset
      @playtime_frames = raw_frame_count.to_f
      @last_raw_frame = raw_frame_count
      @native_scale = native_scale
      @reset_sequence += 1
    end

    def self.reset_sequence
      return @reset_sequence
    end

    def self.playtime_frame_count
      synchronize_native_frames
      return (@playtime_frames + ROUNDING_EPSILON).floor
    end

    def self.with_playtime_frame_count
      @display_scopes << @tick_sequence
      return yield
    ensure
      @display_scopes.pop
    end

    def self.displaying_playtime?
      # Hall of Fame continues from drawing its time into an animated message.
      # A game update ends the display scope before that message animates.
      return @tick_depth == 0 && @display_scopes.last == @tick_sequence
    end

    def self.restore(save_data)
      @playtime_frames = save_data.fetch(:frame_count, raw_frame_count).to_f
      logical = save_data[LOGICAL_SAVE_KEY]
      if save_data[VERSION_SAVE_KEY] == SAVE_VERSION && logical.is_a?(Integer) && logical >= 0
        set_raw_frame_count(logical)
      end
      @last_raw_frame = raw_frame_count
      @native_scale = native_scale
    end

    module GraphicsFrames
      def frame_count
        return Ironmon::SpeedClock.playtime_frame_count if Ironmon::SpeedClock.displaying_playtime?
        return super
      end

      def frame_count=(value)
        result = super
        Ironmon::SpeedClock.reset
        return result
      end
    end

    module SaveValues
      def compile_save_hash
        result = super
        result[:frame_count] = Ironmon::SpeedClock.playtime_frame_count
        result[Ironmon::SpeedClock::LOGICAL_SAVE_KEY] = Ironmon::SpeedClock.raw_frame_count
        result[Ironmon::SpeedClock::VERSION_SAVE_KEY] = Ironmon::SpeedClock::SAVE_VERSION
        return result
      end

      def load_all_values(save_data)
        previous_reset = Ironmon::SpeedClock.reset_sequence
        result = super
        Ironmon::SpeedClock.restore(save_data) if Ironmon::SpeedClock.reset_sequence != previous_reset
        return result
      end
    end

    module SaveDisplay
      def pbStartScreen
        return Ironmon::SpeedClock.with_playtime_frame_count { super }
      end
    end

    module TrainerCardDisplay
      def pbDrawTrainerCardFront
        return Ironmon::SpeedClock.with_playtime_frame_count { super }
      end
    end

    module HallOfFameDisplay
      def writeTrainerData
        return Ironmon::SpeedClock.with_playtime_frame_count { super }
      end
    end
  end
end

Graphics.singleton_class.prepend(Ironmon::SpeedClock::GraphicsFrames)
SaveData.singleton_class.prepend(Ironmon::SpeedClock::SaveValues)
PokemonSave_Scene.prepend(Ironmon::SpeedClock::SaveDisplay)
PokemonTrainerCard_Scene.prepend(Ironmon::SpeedClock::TrainerCardDisplay)
HallOfFame_Scene.prepend(Ironmon::SpeedClock::HallOfFameDisplay)
