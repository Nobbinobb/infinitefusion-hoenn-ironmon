module Ironmon
  module Achievements
    DISPLAY_SECONDS = 4.0
    POLL_SECONDS = 3.0

    def self.receive(payload)
      @queue ||= []
      @known ||= {}
      @displayed ||= {}
      Array(payload["unlocks"]).first(500).each do |notice|
        next if !notice.is_a?(Hash)
        id, name, condition = notice.values_at("id", "name", "condition")
        next if !id.is_a?(String) || id !~ /\A[0-9a-fA-F-]{36}\z/
        next if !name.is_a?(String) || !condition.is_a?(String)
        if @known[id] == :displayed
          @displayed[id] = true
        elsif !@known[id]
          @queue << { "id" => id, "name" => name, "condition" => condition }
          @known[id] = :queued
        end
      end
    end

    def self.tick
      if !Ironmon::GameplayCollection::World.loaded?
        if @window
          @window.dispose
          @window = nil
          @queue.unshift(@current)
          @current = nil
        end
        return
      end
      connection = Ironmon.tracker_connection
      return if !connection
      now = Ironmon::GameplayCollection.clock
      if connection.session_ready? && connection.achievement_notifications_supported? && connection.output_drained?
        if @displayed && !@displayed.empty?
          connection.send_event("achievement_displayed", { "ids" => @displayed.keys })
          @displayed.clear
        elsif !@last_poll || now - @last_poll >= POLL_SECONDS
          connection.send_event("achievement_poll", {})
          @last_poll = now
        end
      end
      if @window
        @window.update
        if now - @shown_at >= DISPLAY_SECONDS
          @window.dispose
          @window = nil
          @known[@current["id"]] = :displayed
          (@displayed ||= {})[@current["id"]] = true
          @current = nil
        end
      elsif @queue && !@queue.empty?
        notice = @queue[0]
        text = "Achievement unlocked!\n#{notice["name"]}\n#{notice["condition"]}"
        window = Window_UnformattedTextPokemon.new(text)
        window.resizeToFit(text, Graphics.width - 24)
        window.x = (Graphics.width - window.width) / 2
        window.y = 12
        window.z = 99_999
        @window, @current, @shown_at = window, @queue.shift, now
        @last_error = nil
      end
    rescue StandardError => error
      @last_error = "#{error.class}: #{error.message}"
      # Presentation errors leave the tracker-owned notice pending for retry.
      @window.dispose if @window && !@window.disposed?
      @window = nil
      @known.delete(@current["id"]) if @current && @known
      @current = nil
      echoln "Ironmon achievement notification: #{@last_error}"
    end
  end
end

Ironmon.register_graphics_update_hook(:achievement_notifications, proc { Ironmon::Achievements.tick })
