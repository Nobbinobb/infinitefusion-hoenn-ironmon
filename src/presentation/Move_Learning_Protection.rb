module Ironmon
  module MoveLearningProtection
    INPUT_RELEASE_SECONDS = 0.15
    UPTIME_UNITS_PER_SECOND = 1_000_000.0

    def self.active?
      return @depth.to_i > 0
    end

    def self.protect
      @depth = @depth.to_i + 1
      return yield
    ensure
      @depth -= 1
    end

    def self.normal_speed_frame
      speed = $GameSpeed
      toggle = $CanToggle
      $GameSpeed = 0
      $CanToggle = false
      $frame = 0
      return yield
    ensure
      $GameSpeed = speed
      $CanToggle = toggle
      $frame = 0
    end

    def self.wait_for_released_input
      released_at = nil
      loop do
        Graphics.update
        Input.update
        yield
        if Input.press?(Input::USE) || Input.press?(Input::BACK)
          released_at = nil
        else
          now = System.uptime.to_f / UPTIME_UNITS_PER_SECOND
          released_at ||= now
          return if now - released_at >= INPUT_RELEASE_SECONDS
        end
      end
    end

    module SummaryScreen
      def pbStartForgetScreen(party, partyindex, move_to_learn)
        return super if !move_to_learn
        return Ironmon::MoveLearningProtection.protect { super }
      end
    end

    module SummaryScene
      def pbChooseMoveToForget(move_to_learn)
        if move_to_learn && Ironmon::MoveLearningProtection.active?
          Ironmon::MoveLearningProtection.wait_for_released_input { pbUpdate }
        end
        return super
      end
    end
  end
end

PokemonSummaryScreen.prepend(Ironmon::MoveLearningProtection::SummaryScreen)
PokemonSummary_Scene.prepend(Ironmon::MoveLearningProtection::SummaryScene)
