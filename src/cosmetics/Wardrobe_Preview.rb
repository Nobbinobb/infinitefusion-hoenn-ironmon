# Native layered cosmetic rendering, shared by tracker previews.
module Ironmon
  module Cosmetics
    class WardrobePreview
      def render_preview(draft, mode = "portrait")
        wrappers = []
        directions = { "front" => 0, "left" => 1, "right" => 2, "back" => 3 }
        return render_overworld_preview(draft, directions[mode], "walk", false) if directions.has_key?(mode)
        return render_overworld_preview(draft, 2, "bike", true) if mode == "bike"
        base = AnimatedBitmap.new(getBaseTrainerSpriteFilename(draft["skin_tone"]))
        wrappers << base
        result = base.bitmap.clone
        layers = [[getTrainerSpriteOutfitFilename(draft["clothes"]), draft["clothes_color"]],
                  [getTrainerSpriteHairFilename(draft["hair"]), draft["hair_color"]]]
        ["hat2", "hat"].each do |field|
          layers << [getTrainerSpriteHatFilename(draft[field]), draft["#{field}_color"]] if draft[field]
        end
        layers.each do |path, dye|
          layer = AnimatedBitmap.new(path, dye)
          wrappers << layer
          result.blt(0, 0, layer.bitmap, layer.bitmap.rect)
        end
        return result
      rescue *RECOVERABLE_ERRORS
        result.dispose if result
        raise
      ensure
        wrappers.each(&:dispose)
      end

      def render_overworld_preview(draft, direction, action, include_bicycle)
        wrappers = []
        frame_index = 1
        base = AnimatedBitmap.new(getBaseOverworldSpriteFilename(action, draft["skin_tone"]))
        wrappers << base
        frame_width = base.bitmap.width / 4
        frame_height = base.bitmap.height / 4
        frame = Bitmap.new(frame_width, frame_height)
        source = Rect.new(frame_index * frame_width, direction * frame_height, frame_width, frame_height)
        if include_bicycle
          bicycle = AnimatedBitmap.new(getOverworldBicycleFilename, draft["bike_color"])
          wrappers << bicycle
          frame.blt(0, 0, bicycle.bitmap, source)
        end
        frame.blt(0, 0, base.bitmap, source)
        outfit = AnimatedBitmap.new(getOverworldOutfitFilename(draft["clothes"], action), draft["clothes_color"])
        wrappers << outfit
        frame.blt(0, 0, outfit.bitmap, source)
        hair = AnimatedBitmap.new(getOverworldHairFilename(draft["hair"]), draft["hair_color"])
        wrappers << hair
        hair_offset = preview_wearable_offset(action, direction, frame_index, false)
        frame.blt(hair_offset[0], hair_offset[1], hair.bitmap, source)
        ["hat2", "hat"].each do |field|
          next if !draft[field]
          layer = AnimatedBitmap.new(getOverworldHatFilename(draft[field]), draft["#{field}_color"])
          wrappers << layer
          accessory_height = layer.bitmap.height / 4
          accessory_source = Rect.new(0, direction * accessory_height, layer.bitmap.width, accessory_height)
          accessory_offset = preview_wearable_offset(action, direction, frame_index, true)
          frame.blt(accessory_offset[0], accessory_offset[1], layer.bitmap, accessory_source)
        end
        return frame
      rescue *RECOVERABLE_ERRORS
        frame.dispose if frame
        raise
      ensure
        wrappers.each(&:dispose)
      end

      def preview_wearable_offset(action, direction, frame_index, accessory)
        offsets = if action == "bike"
                    [Outfit_Offsets::BIKE_OFFSETS_DOWN, Outfit_Offsets::BIKE_OFFSETS_LEFT,
                     Outfit_Offsets::BIKE_OFFSETS_RIGHT, Outfit_Offsets::BIKE_OFFSETS_UP][direction]
                  else
                    Outfit_Offsets::BASE_OFFSET
                  end
        x, y = offsets[frame_index]
        y -= 2 if accessory && frame_index.odd?
        return [x, y]
      end

    end
  end
end
