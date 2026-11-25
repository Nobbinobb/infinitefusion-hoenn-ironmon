module Ironmon
  module SpeedTransitions
    MAXIMUM_VISUAL_MULTIPLIER = 3

    def self.active?
      return @depth.to_i > 0
    end

    def self.constrain_multiplier(value)
      return MAXIMUM_VISUAL_MULTIPLIER if active? && value > MAXIMUM_VISUAL_MULTIPLIER
      return value
    end

    def self.with_visual_speed
      @depth = @depth.to_i + 1
      Ironmon::SpeedUp.refresh_rate
      return yield
    ensure
      @depth -= 1
      Ironmon::SpeedUp.refresh_rate
    end

    def self.without_visual_speed
      previous = @depth.to_i
      @depth = 0
      Ironmon::SpeedUp.refresh_rate
      return yield
    ensure
      @depth = previous
      Ironmon::SpeedUp.refresh_rate
    end

    def self.native_duration(duration)
      frames = duration.to_i
      return 0 if frames <= 0
      logical_rate = Ironmon::SpeedUp.base_rate * Ironmon::SpeedUp.multiplier
      native_rate = Ironmon::SpeedUp::NATIVE_RATE.call
      result = (frames * native_rate.to_f / logical_rate).round
      return result > 0 ? result : 1
    end

    module GraphicsTransitions
      def update
        # Hoenn still iterates the return fade after the opening attempt is lost.
        return if Ironmon.discarding_opening_battle_return?
        return if Ironmon.finish_queued_early_game_loss_reset
        return super
      end

      def transition(duration = 8, filename = "", vague = 20)
        if filename == "" && $scene.is_a?(Scene_Map) && $scene.consume_opening_loss_reveal
          duration = 0
        end
        return Ironmon::SpeedTransitions.with_visual_speed { super(duration, filename, vague) }
      end

      def transition_KGC_SpecialTransition(duration = 8, filename = "", vague = 20)
        return super(Ironmon::SpeedTransitions.native_duration(duration), filename, vague)
      end

      def fadeout(duration)
        return Ironmon::SpeedTransitions.with_visual_speed do
          super(Ironmon::SpeedTransitions.native_duration(duration))
        end
      end

      def fadein(duration)
        return Ironmon::SpeedTransitions.with_visual_speed do
          super(Ironmon::SpeedTransitions.native_duration(duration))
        end
      end
    end

    module BattleTransitions
      private

      def pbUpdateSceneMap
        return if Ironmon.discarding_opening_battle_return?
        return super
      end

      def pbBattleAnimation(*args, &block)
        return Ironmon::SpeedTransitions.with_visual_speed do
          if block
            super(*args) do |*yielded|
              Ironmon::SpeedTransitions.without_visual_speed { block.call(*yielded) }
            end
          else
            super(*args)
          end
        end
      end
    end

    module BattleIntro
      def pbBattleIntroAnimation
        return Ironmon::SpeedTransitions.with_visual_speed { super }
      end
    end
  end
end

Graphics.singleton_class.prepend(Ironmon::SpeedTransitions::GraphicsTransitions)
Object.prepend(Ironmon::SpeedTransitions::BattleTransitions)
PokeBattle_Scene.prepend(Ironmon::SpeedTransitions::BattleIntro)
