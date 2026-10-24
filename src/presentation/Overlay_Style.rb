module Ironmon
  module OverlayStyle
    PANEL_ALPHA = 191
    STATUS_WIDTH = 146
    STATUS_HEIGHT = 26
    MARGIN = 8
    STATUS_GAP = 4

    def self.rounded_inset(position, length, radius)
      edge = [position, length - 1 - position].min
      return 0 if edge >= radius
      distance = radius - edge - 0.5
      return (radius - Math.sqrt(radius * radius - distance * distance)).round
    end

    def self.fill_rounded_rect(bitmap, x, y, width, height, radius, color)
      height.times do |row|
        inset = rounded_inset(row, height, radius)
        bitmap.fill_rect(x + inset, y + row, width - inset * 2, 1, color)
      end
    end

    def self.draw_panel(bitmap)
      width = bitmap.width; height = bitmap.height
      fill_rounded_rect(bitmap, 0, 0, width, height, 5, Color.new(67, 87, 106, PANEL_ALPHA))
      fill_rounded_rect(bitmap, 1, 1, width - 2, height - 2, 4, Color.new(19, 26, 36, PANEL_ALPHA))
      gradient_width = [(width * 0.225).round, 2].max
      (1...gradient_width).each do |x|
        fraction = (x - 1).to_f / (gradient_width - 2)
        color = Color.new((26 - 7 * fraction).round, (40 - 14 * fraction).round, (53 - 17 * fraction).round, PANEL_ALPHA)
        inset = 1 + rounded_inset(x - 1, width - 2, 4)
        bitmap.fill_rect(x, inset, 1, height - inset * 2, color)
      end
    end

    def self.set_font(bitmap, size)
      bitmap.font.name = MessageConfig.pbTryFonts("Arial")
      bitmap.font.size = size
      bitmap.font.bold = false
      bitmap.font.italic = false
      bitmap.font.shadow = false if bitmap.font.respond_to?(:shadow=)
      bitmap.font.outline = false if bitmap.font.respond_to?(:outline=)
      bitmap.font.color = Color.new(245, 248, 252)
    end

    def self.draw_divider(bitmap, x)
      bitmap.fill_rect(x, 6, 1, 14, Color.new(69, 103, 121, PANEL_ALPHA))
    end
  end
end
