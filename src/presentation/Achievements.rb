module Ironmon
  module Achievements
    SLIDE_SECONDS = 0.18
    HOLD_SECONDS = 2.6
    DISPLAY_SECONDS = SLIDE_SECONDS * 2 + HOLD_SECONDS
    POLL_SECONDS = 3.0
    MAXIMUM_QUEUE = 64

    class Toast
      HEIGHT = 64
      MAXIMUM_WIDTH = 280
      MARGIN = 8
      ICON_SIZE = 48
      TITLE_SIZE = 20
      TITLE_LINE_HEIGHT = 22
      PANEL_ALPHA = 217
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
        fill_rounded_rect(bitmap, 0, 0, @width, HEIGHT, 5, Color.new(67, 87, 106, PANEL_ALPHA))
        fill_rounded_rect(bitmap, 1, 1, @width - 2, HEIGHT - 2, 4, Color.new(19, 26, 36, PANEL_ALPHA))
        (1...SEPARATOR_X).each do |x|
          fraction = (x - 1).to_f / (SEPARATOR_X - 2)
          color = Color.new((26 - 7 * fraction).round, (40 - 14 * fraction).round, (53 - 17 * fraction).round, PANEL_ALPHA)
          inset = 1 + rounded_inset(x - 1, @width - 2, 4)
          bitmap.fill_rect(x, inset, 1, HEIGHT - inset * 2, color)
        end
        bitmap.fill_rect(SEPARATOR_X, 12, 1, 40, Color.new(69, 103, 121, PANEL_ALPHA))
        icon = Bitmap.new(notice["icon_width"], notice["icon_height"])
        icon.raw_data = notice["pixels"]
        bitmap.stretch_blt(Rect.new(8, 8, ICON_SIZE, ICON_SIZE), icon, icon.rect)
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

      def rounded_inset(position, length, radius)
        edge = [position, length - 1 - position].min
        return 0 if edge >= radius
        distance = radius - edge - 0.5
        return (radius - Math.sqrt(radius * radius - distance * distance)).round
      end

      def fill_rounded_rect(bitmap, x, y, width, height, radius, color)
        height.times do |row|
          inset = rounded_inset(row, height, radius)
          bitmap.fill_rect(x + inset, y + row, width - inset * 2, 1, color)
        end
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
        if @known[id] == :displayed
          @displayed[id] = true
        elsif !@known[id] && @queue.length < MAXIMUM_QUEUE
          decoded = decode_notice(notice)
          next if !decoded
          @queue << decoded.merge("id" => id)
          @known[id] = :queued
        end
      end
    end

    def self.preview(payload)
      return false if !Ironmon::GameplayCollection::World.loaded?
      @queue ||= []
      return false if @queue.length >= MAXIMUM_QUEUE || (@current && @current["preview"]) || @queue.any? { |notice| notice["preview"] }
      decoded = decode_notice(payload)
      return false if !decoded
      @queue << decoded.merge("preview" => true)
      return true
    end

    def self.tick(now = Ironmon::GameplayCollection.clock)
      if !Ironmon::GameplayCollection::World.loaded?
        if @window
          @window.dispose
          @window = nil
          @queue.unshift(@current) if !@current["preview"]
          @current = nil
        end
        @queue.reject! { |notice| notice["preview"] } if @queue
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
          if !@current["preview"]
            @known[@current["id"]] = :displayed
            (@displayed ||= {})[@current["id"]] = true
          end
          @current = nil
        end
      elsif @queue && !@queue.empty? && (!@retry_at || now >= @retry_at)
        @current = @queue.shift
        @window = Toast.new(@current, now)
        @shown_at = now
        @last_error = nil
        @retry_at = nil
      end
    rescue StandardError => error
      @last_error = "#{error.class}: #{error.message}"
      @window.dispose if @window && !@window.disposed?
      @window = nil
      @queue.unshift(@current) if @current && !@current["preview"]
      @current = nil
      @retry_at = now + POLL_SECONDS
      echoln "Ironmon achievement notification: #{@last_error}"
    end
  end
end

Ironmon.register_graphics_update_hook(:achievement_notifications, proc { Ironmon::Achievements.tick })
