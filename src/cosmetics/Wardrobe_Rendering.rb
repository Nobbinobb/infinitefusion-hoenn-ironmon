module Ironmon
  module Cosmetics
    class WardrobeScreen
      def preview_image(appearance, mode, head_only)
        bitmap = render_preview(appearance, mode)
        bytes = bitmap.raw_data
        left, top, right, bottom = bitmap.width, bitmap.height, -1, -1
        bitmap.height.times do |y|
          bitmap.width.times do |x|
            next if bytes.getbyte((y * bitmap.width + x) * 4 + 3).to_i == 0
            left, right = [left, x].min, [right, x].max
            top, bottom = [top, y].min, [bottom, y].max
          end
        end
        raise "This wardrobe preview is empty." if right < left || bottom < top
        bottom = top + ((bottom - top) * 0.48).round if head_only
        if head_only
          left, right = bitmap.width, -1
          (top..bottom).each do |y|
            bitmap.width.times do |x|
              next if bytes.getbyte((y * bitmap.width + x) * 4 + 3).to_i == 0
              left, right = [left, x].min, [right, x].max
            end
          end
        end
        cropped = Bitmap.new(right - left + 1, bottom - top + 1)
        cropped.blt(0, 0, bitmap, Rect.new(left, top, cropped.width, cropped.height))
        directory = File.join(@profile.directory, "previews")
        Dir.mkdir(directory) if !File.directory?(directory)
        path = File.join(directory, "#{@wardrobe_id}.png")
        cropped.save_to_png(path)
        return { "png_base64" => [File.binread(path)].pack("m0"), "width" => cropped.width, "height" => cropped.height }
      ensure
        File.delete(path) if path && File.file?(path)
        cropped.dispose if cropped && !cropped.disposed?
        bitmap.dispose if bitmap && !bitmap.disposed?
      end

      def preview_palette(appearance, field)
        version, style = Cosmetics.hair_parts(appearance["hair"])
        hair = @catalog.find("hair", style)
        base_colors = hair["variants"].to_h do |variant|
          [variant.to_s, color_hex(representative_color(getTrainerSpriteHairFilename("#{variant}_#{style}")))]
        end
        path = if field == "hair_color"
                 getTrainerSpriteHairFilename(appearance["hair"])
               elsif field == "clothes_color"
                 getTrainerSpriteOutfitFilename(appearance["clothes"])
               elsif field == "bike_color"
                 getOverworldBicycleFilename
               else
                 slot = field == "hat_color" ? "hat" : "hat2"
                 appearance[slot] ? getTrainerSpriteHatFilename(appearance[slot]) : nil
               end
        palette = @palette_cache[[path, :dyes]] ||= dye_palette(representative_color(path))
        skin_colors = @palette_cache[:skin] ||= (1..6).to_h { |tone| [tone.to_s, color_hex(representative_color(getBaseTrainerSpriteFilename(tone)))] }
        return { "base_colors" => base_colors, "dye_colors" => palette, "skin_colors" => skin_colors }
      end

      def representative_color(path)
        return Color.new(128, 128, 128) if !path
        cached = @palette_cache[[path, :color]]
        return Color.new(*cached) if cached
        wrapper = AnimatedBitmap.new(path)
        bytes = wrapper.bitmap.raw_data
        colors = Hash.new(0)
        (bytes.bytesize / 4).times do |index|
          offset = index * 4
          next if bytes.getbyte(offset + 3) < 128
          rgb = [bytes.getbyte(offset), bytes.getbyte(offset + 1), bytes.getbyte(offset + 2)]
          next if rgb.max < 35 || rgb.max > 245
          colors[rgb] += 1
        end
        rgb = colors.max_by { |_key, count| count }&.first || [128, 128, 128]
        @palette_cache[[path, :color]] = rgb
        return Color.new(*rgb)
      ensure
        wrapper.dispose if wrapper
      end

      def dye_palette(color)
        pixel = Bitmap.new(1, 1)
        pixel.set_pixel(0, 0, color)
        return (0...360).step(10).map do |angle|
          tinted = pixel.clone
          begin
            tinted.hue_change(angle)
            color_hex(tinted.get_pixel(0, 0))
          ensure
            tinted.dispose
          end
        end
      ensure
        pixel.dispose if pixel
      end

      def color_hex(color)
        return sprintf("#%02x%02x%02x", color.red.to_i, color.green.to_i, color.blue.to_i)
      end
    end
  end
end
