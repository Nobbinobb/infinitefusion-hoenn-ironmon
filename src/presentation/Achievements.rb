module Ironmon
  module Achievements
    SLIDE_SECONDS = 0.18
    HOLD_SECONDS = 2.6
    DISPLAY_SECONDS = SLIDE_SECONDS * 2 + HOLD_SECONDS
    POLL_SECONDS = 3.0
    MAXIMUM_QUEUE = 64

    class Toast
      HEIGHT = 64
      MAXIMUM_WIDTH = 340
      MARGIN = 8
      ICON_SIZE = 50
      ICON_INSET = (HEIGHT - ICON_SIZE) / 2
      TITLE_SIZE = 20
      TITLE_LINE_HEIGHT = 22
      PANEL_ALPHA = Ironmon::OverlayStyle::PANEL_ALPHA
      SEPARATOR_X = 63
      TEXT_X = 77
      attr_reader :width, :height, :name

      def initialize(notice, now)
        @width = [MAXIMUM_WIDTH, Graphics.width - MARGIN * 2].min
        @height = HEIGHT
        @name = notice["name"]
        @started_at = now
        @sprite = Sprite.new
        @sprite.z = 99_999
        @sprite.bitmap = Bitmap.new(@width, @height)
        draw(notice)
        update(now)
      rescue Exception
        dispose
        raise
      end

      def x; return @sprite.x; end
      def y; return @sprite.y; end
      def disposed?; return !@sprite || @sprite.disposed?; end

      def dispose
        return if !@sprite
        @sprite.bitmap.dispose if @sprite.bitmap && !@sprite.bitmap.disposed?
        @sprite.dispose if !@sprite.disposed?
      end

      def update(now)
        elapsed = [now - @started_at, 0.0].max
        return true if elapsed >= DISPLAY_SECONDS
        if elapsed < SLIDE_SECONDS
          fraction = 1.0 - (1.0 - elapsed / SLIDE_SECONDS) ** 3
        elsif elapsed < SLIDE_SECONDS + HOLD_SECONDS
          fraction = 1.0
        else
          fraction = (1.0 - (elapsed - SLIDE_SECONDS - HOLD_SECONDS) / SLIDE_SECONDS) ** 3
        end
        @sprite.x = (-@width + (@width + MARGIN) * fraction).round
        @sprite.y = MARGIN
        @sprite.update
        return false
      end

      def draw(notice)
        bitmap = @sprite.bitmap
        Ironmon::OverlayStyle.draw_panel(bitmap)
        bitmap.fill_rect(SEPARATOR_X, 12, 1, 40, Color.new(69, 103, 121, PANEL_ALPHA))
        icon = Bitmap.new(notice["icon_width"], notice["icon_height"])
        icon.raw_data = notice["pixels"]
        if icon.width == ICON_SIZE && icon.height == ICON_SIZE
          bitmap.blt(ICON_INSET, ICON_INSET, icon, icon.rect)
        else
          bitmap.stretch_blt(Rect.new(ICON_INSET, ICON_INSET, ICON_SIZE, ICON_SIZE), icon, icon.rect)
        end
        bitmap.font.name = MessageConfig.pbTryFonts("Arial")
        bitmap.font.size = TITLE_SIZE
        bitmap.font.bold = true
        bitmap.font.shadow = false if bitmap.font.respond_to?(:shadow=)
        bitmap.font.color = Color.new(245, 248, 252)
        text_width = @width - TEXT_X - 14
        lines = wrapped_lines(bitmap, @name, text_width, 2)
        text_y = (HEIGHT - lines.length * TITLE_LINE_HEIGHT) / 2
        lines.each_with_index do |line, index|
          bitmap.draw_text(TEXT_X, text_y + index * TITLE_LINE_HEIGHT, text_width, TITLE_LINE_HEIGHT, line, 0)
        end
      ensure
        icon.dispose if icon && !icon.disposed?
      end

      def wrapped_lines(bitmap, text, available, maximum_lines)
        remaining = text.gsub(/\s+/, " ").strip
        lines = []
        while !remaining.empty? && lines.length < maximum_lines
          if bitmap.text_size(remaining).width <= available
            lines << remaining
            break
          end
          length = 0
          remaining.each_char do |_character|
            break if bitmap.text_size(remaining[0, length + 1]).width > available
            length += 1
          end
          length = [length, 1].max
          candidate = remaining[0, length]
          if lines.length == maximum_lines - 1
            candidate = candidate[0...-1] while !candidate.empty? && bitmap.text_size(candidate.rstrip + "…").width > available
            lines << candidate.rstrip + "…"
            break
          end
          boundary = candidate.rindex(" ")
          length = boundary if boundary && boundary > 0
          lines << remaining[0, length].rstrip
          remaining = remaining[length..-1].lstrip
        end
        return lines.empty? ? [""] : lines
      end
    end

    class OverlayToast
      LEASE_SECONDS = 0.75
      attr_reader :presentation_id

      def initialize(notice, now)
        @notice = notice
        @connection = Ironmon.tracker_connection
        @presentation_id = Random.new.bytes(16).unpack1("H*")
        @pending_since = now
        @last_tick = now
        @elapsed = 0.0
        @visible = false
        send_start(now)
      end

      def disposed?; return !!@disposed; end

      def accept_overlay(payload, now)
        return if @fallback || @disposed || payload["presentation_id"] != @presentation_id
        @ready = payload["ready"] == true
        @visible = payload["visible"] == true
        @last_acknowledged = now
      end

      def update(now)
        return @fallback.update(now) if @fallback
        if @ready.nil?
          send_start(now) if !@last_start_at || now - @last_start_at >= 0.2
          fallback(now) if now - @pending_since >= LEASE_SECONDS
          return false
        end
        if !@ready || !@last_acknowledged || now - @last_acknowledged >= LEASE_SECONDS
          fallback(now)
          return false
        end
        if !@started
          @started = true
          @last_tick = now
          send_frame
          return false
        end
        @elapsed += [now - @last_tick, 0.0].max if @visible
        @last_tick = now
        send_frame
        return @elapsed >= DISPLAY_SECONDS
      end

      def dispose
        return if @disposed
        send_stop
        @fallback.dispose if @fallback
        @disposed = true
      end

      def fallback(now)
        send_stop
        @fallback = Toast.new(@notice, now)
      end

      def send_start(now)
        return if !@connection || !@connection.session_ready? || !@connection.output_drained?
        payload = { "presentation_id" => @presentation_id, "achievement_id" => @notice["id"] }
        @sent_start = true if @connection.send_event("achievement_overlay_start", payload)
        @last_start_at = now
      end

      def send_frame
        return if !@connection || !@connection.session_ready? || !@connection.output_drained?
        @connection.send_event("achievement_overlay_frame", { "presentation_id" => @presentation_id, "elapsed" => [@elapsed, DISPLAY_SECONDS].min })
      end

      def send_stop
        return if @stopped || !@sent_start || !@connection || !@connection.session_ready?
        @stopped = @connection.send_event("achievement_overlay_stop", { "presentation_id" => @presentation_id })
      end
    end

    def self.overlay_ready(payload)
      @window.accept_overlay(payload, Ironmon::GameplayCollection.clock) if @window && @window.respond_to?(:accept_overlay)
    end

    def self.decode_notice(notice)
      return nil if !notice.is_a?(Hash)
      name, icon = notice.values_at("name", "icon")
      return nil if !name.is_a?(String) || name.strip.empty? || name.length > 256 || !icon.is_a?(Hash)
      width, height, encoded = icon.values_at("width", "height", "pixels")
      return nil if !width.is_a?(Integer) || !height.is_a?(Integer) || !width.between?(1, 64) || !height.between?(1, 64)
      return nil if !encoded.is_a?(String) || encoded.bytesize > 21_848
      pixels = encoded.unpack1("m0")
      return nil if pixels.bytesize != width * height * 4
      return { "name" => name, "icon_width" => width, "icon_height" => height, "pixels" => pixels }
    rescue ArgumentError
      return nil
    end

    def self.receive(payload)
      @queue ||= []
      @known ||= {}
      @displayed ||= {}
      Array(payload["unlocks"]).first(8).each do |notice|
        next if !notice.is_a?(Hash)
        id = notice["id"]
        next if !id.is_a?(String) || id !~ /\A[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\z/
        id = id.downcase
        if !Ironmon.feature_enabled?("achievement_popups") || @known[id] == :displayed
          @known[id] = :displayed
          @displayed[id] = true
        elsif !@known[id] && @queue.length < MAXIMUM_QUEUE
          decoded = decode_notice(notice)
          next if !decoded
          @queue << decoded.merge("id" => id)
          @known[id] = :queued
        end
      end
    end

    def self.suppress_notifications
      return if Ironmon.feature_enabled?("achievement_popups")
      @known ||= {}
      @displayed ||= {}
      notices = @queue || []
      if @current
        @window.dispose if @window
        @window = nil
        notices << @current
        @current = nil
      end
      notices.each do |notice|
        @known[notice["id"]] = :displayed
        @displayed[notice["id"]] = true
      end
      @queue.clear if @queue
    end

    def self.tick(now = Ironmon::GameplayCollection.clock)
      suppress_notifications
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
      if connection && connection.session_ready? && connection.achievement_notifications_supported? && connection.output_drained?
        if @displayed && !@displayed.empty?
          @displayed.clear if connection.send_event("achievement_displayed", { "ids" => @displayed.keys })
        elsif !@last_poll || now - @last_poll >= POLL_SECONDS
          @last_poll = now if connection.send_event("achievement_poll", {})
        end
      end
      if @window
        if @window.update(now)
          @window.dispose
          @window = nil
          @known[@current["id"]] = :displayed
          (@displayed ||= {})[@current["id"]] = true
          @current = nil
        end
      elsif @queue && !@queue.empty? && (!@retry_at || now >= @retry_at)
        @current = @queue.shift
        use_overlay = connection && connection.respond_to?(:achievement_overlay_supported?) && connection.achievement_overlay_supported?
        @window = use_overlay ? OverlayToast.new(@current, now) : Toast.new(@current, now)
        @shown_at = now
        @last_error = nil
        @retry_at = nil
      end
    rescue StandardError => error
      @last_error = "#{error.class}: #{error.message}"
      @window.dispose if @window && !@window.disposed?
      @window = nil
      @queue.unshift(@current) if @current
      @current = nil
      @retry_at = now + POLL_SECONDS
      echoln "Ironmon achievement notification: #{@last_error}"
    end
  end
end

Ironmon.register_graphics_update_hook(:achievement_notifications, proc { Ironmon::Achievements.tick })
