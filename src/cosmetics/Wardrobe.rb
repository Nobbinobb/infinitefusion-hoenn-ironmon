#===============================================================================
# Shared wardrobe: detached previews, one-time selection, permanent purchases
#===============================================================================
module Ironmon
  module Cosmetics
    class WardrobeScreen
      ROOT_FIELDS = ["clothes", "hair", "hat", "hat2", "skin_tone", "hair_version",
                     "hair_color", "clothes_color", "hat_color", "hat2_color",
                     "bike_color", "preview", "confirm", "cancel"].freeze
      PREVIEW_MODES = ["portrait", "front", "back", "left", "right", "bike"].freeze
      LABELS = { "clothes" => "Outfit", "hair" => "Hairstyle", "hat" => "Accessory 1",
                 "hat2" => "Accessory 2", "skin_tone" => "Skin tone", "hair_version" => "Base hair color",
                 "hair_color" => "Hair dye", "clothes_color" => "Outfit dye",
                 "hat_color" => "Accessory 1 dye", "hat2_color" => "Accessory 2 dye",
                 "bike_color" => "Bicycle dye", "preview" => "Preview",
                 "confirm" => "Confirm appearance", "cancel" => "Cancel" }.freeze
      PREVIEW_LABELS = { "portrait" => "Portrait", "front" => "Front sprite",
                         "back" => "Back sprite", "left" => "Left sprite",
                         "right" => "Right sprite", "bike" => "Bicycle" }.freeze
      attr_reader :appearance, :preview_mode

      def initialize(catalog, profile, appearance_key)
        @catalog = catalog
        @profile = profile
        @appearance_key = appearance_key
        @state = @profile.read
        @initial = !@state["initial_claimed"]
        stored = @state["appearances"][@appearance_key]
        @appearance = (stored || Cosmetics.capture).dup
        @browser_field = nil
        @owned_only = false
        @root_index = 0
        @preview_mode = "portrait"
        @closed = false
        @confirmed = false
        normalize_selection
      end

      def normalize_selection
        current = Cosmetics.capture
        APPEARANCE_FIELDS.each { |field| @appearance[field] = current[field] if !@appearance.has_key?(field) }
        DYE_FIELDS.each { |field| @appearance[field] = @appearance[field].to_i % 360 }
        @appearance["skin_tone"] = [[@appearance["skin_tone"].to_i, 1].max, 6].min
        ["clothes", "hair"].each do |category|
          version, style = Cosmetics.hair_parts(@appearance["hair"])
          id = category == "hair" ? style : @appearance[category]
          entry = @catalog.find(category, id)
          if !entry || !entry["available"]
            entry = @catalog.available(category).find { |item| @state["owned"][item["key"]] } || @catalog.available(category).first
          end
          raise "No complete #{category} graphics are installed." if !entry
          @appearance[category] = if category == "hair"
                                    "#{entry["variants"].include?(version) ? version : entry["variants"].first}_#{entry["id"]}"
                                  else
                                    entry["id"]
                                  end
        end
        ["hat", "hat2"].each { |field| @appearance[field] = nil if !@catalog.valid?("hat", @appearance[field]) }
      end

      def create_widgets
        @viewport = Viewport.new(0, 0, Graphics.width, Graphics.height)
        @viewport.z = 99990
        @background = BitmapSprite.new(Graphics.width, Graphics.height, @viewport)
        @background.bitmap.fill_rect(0, 0, Graphics.width, Graphics.height, Color.new(38, 45, 54))
        @overlay = BitmapSprite.new(Graphics.width, Graphics.height, @viewport)
        @preview = Sprite.new(@viewport)
        @window = Window_CommandPokemon.newEmpty(8, 54, 300, Graphics.height - 146, @viewport)
        @window.contents.font.size = 20
        show_root
      end

      def run
        create_widgets
        until @closed
          Graphics.update
          Input.update
          previous_index = @window.index
          @window.update
          redraw if previous_index != @window.index
          if Input.trigger?(Input::BACK)
            @browser_field ? show_root : cancel
          elsif Input.trigger?(Input::USE)
            @browser_field ? choose_item : activate_root
          elsif Input.trigger?(Input::LEFT)
            move_horizontal(-1)
          elsif Input.trigger?(Input::RIGHT)
            move_horizontal(1)
          elsif Input.trigger?(Input::ACTION) && @browser_field && !@initial
            @owned_only = !@owned_only
            show_browser(@browser_field)
          end
        end
        return @confirmed
      ensure
        dispose
      end

      def dispose
        @preview.bitmap.dispose if @preview && @preview.bitmap && !@preview.bitmap.disposed?
        [@window, @preview, @overlay, @background, @viewport].each { |object| object.dispose if object && !object.disposed? }
      end

      def show_root
        @browser_field = nil
        @window.commands = ROOT_FIELDS.map do |field|
          label = _INTL(LABELS[field])
          value = if DYE_FIELDS.include?(field)
                    @appearance[field].zero? ? _INTL("None") : @appearance[field].to_s
                  elsif field == "skin_tone"
                    @appearance[field].to_s
                  elsif field == "hair_version"
                    Cosmetics.hair_parts(@appearance["hair"])[0].to_s
                  elsif field == "preview"
                    _INTL(PREVIEW_LABELS[@preview_mode])
                  elsif field == "clothes"
                    entry_name("clothes", @appearance[field])
                  elsif field == "hair"
                    entry_name("hair", Cosmetics.hair_parts(@appearance[field])[1])
                  elsif ["hat", "hat2"].include?(field)
                    @appearance[field] ? entry_name("hat", @appearance[field]) : _INTL("None")
                  end
          value ? "#{label}: #{value}" : label
        end
        @window.index = @root_index
        redraw
      end

      def entry_name(category, id)
        entry = @catalog.find(category, id)
        return entry ? entry["name"] : id.to_s
      end

      def show_browser(field)
        @root_index = @window.index if !@browser_field
        @browser_field = field
        category = field == "hat2" ? "hat" : field
        @items = @catalog.available(category)
        @items = @items.select { |entry| @state["owned"][entry["key"]] } if @owned_only && !@initial
        @items.unshift(nil) if category == "hat"
        @items.unshift(:filter) if !@initial
        @items << :back
        @window.commands = @items.map do |entry|
          next _INTL("Back") if entry == :back
          next @owned_only ? _INTL("Filter: Obtained only") : _INTL("Filter: All items") if entry == :filter
          next _INTL("None") if !entry
          entry["name"]
        end
        @window.index = 0
        redraw
      end

      def preview_appearance
        draft = @appearance.dup
        entry = @browser_field ? @items[@window.index] : :back
        set_piece(draft, @browser_field, entry) if @browser_field && (entry.nil? || entry.is_a?(Hash))
        return draft
      end

      def set_piece(draft, field, entry)
        if field == "hair"
          version = Cosmetics.hair_parts(draft["hair"])[0]
          version = entry["variants"].first if !entry["variants"].include?(version)
          draft[field] = "#{version}_#{entry["id"]}"
        else
          draft[field] = entry ? entry["id"] : nil
        end
      end

      def redraw
        return if !@overlay
        draft = preview_appearance
        mode = current_preview_mode
        bitmap = render_preview(draft, mode)
        @preview.bitmap.dispose if @preview.bitmap
        @preview.bitmap = bitmap
        maximum_scale = mode == "portrait" ? 1.35 : 7.5
        scale = [maximum_scale, (Graphics.width - 320).to_f / bitmap.width, 250.0 / bitmap.height].min
        @preview.zoom_x = @preview.zoom_y = scale
        @preview.x = 310 + (Graphics.width - 310 - bitmap.width * scale) / 2
        @preview.y = 68
        canvas = @overlay.bitmap
        canvas.clear
        pbSetSystemFont(canvas)
        base = Color.new(238, 242, 246)
        shadow = Color.new(14, 18, 23)
        title = @initial ? _INTL("Choose your first appearance") : _INTL("Ironmon wardrobe")
        pbDrawShadowText(canvas, 16, 12, 350, 32, title, base, shadow)
        pbDrawShadowText(canvas, Graphics.width - 132, 12, 120, 32, _INTL("{1} points", @state["points"]), base, shadow, 2)
        pbDrawShadowText(canvas, 310, 42, Graphics.width - 318, 24, _INTL(PREVIEW_LABELS[mode]), base, shadow, 1)
        entry = @browser_field ? @items[@window.index] : nil
        status = if entry.is_a?(Hash)
                   @initial ? _INTL("Free selection") : (@state["owned"][entry["key"]] ? _INTL("Owned") : _INTL("{1} points", entry["points"]))
                 else
                   @initial ? _INTL("One-time selection") : _INTL("Preview")
                 end
        pbDrawShadowText(canvas, 310, 326, Graphics.width - 318, 30, status, base, shadow, 1)
        detail = if entry.is_a?(Hash)
                   entry["description"]
                 elsif entry == :filter
                   @owned_only ? _INTL("Showing only cosmetics you have permanently obtained.") : _INTL("Showing all installed cosmetics, including locked items you can preview.")
                 elsif @initial
                   _INTL("Only the final selected pieces become permanently unlocked.")
                 else
                   _INTL("Purchases are permanent. Confirm to save this appearance for this save slot.")
                 end
        canvas.font.size = 18
        drawTextEx(canvas, 16, Graphics.height - 86, Graphics.width - 32, 2, detail, base, shadow)
        hint = @browser_field ? _INTL("Confirm: select or change filter  |  Left/Right: page  |  Back: categories") : _INTL("Left/Right: adjust or rotate preview  |  Confirm: select  |  Back: cancel")
        pbDrawShadowText(canvas, 16, Graphics.height - 28, Graphics.width - 32, 26, hint, base, shadow)
      end

      def current_preview_mode
        return "bike" if !@browser_field && ROOT_FIELDS[@window.index] == "bike_color"
        return @preview_mode
      end

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

      def activate_root
        @root_index = @window.index
        field = ROOT_FIELDS[@root_index]
        if ["clothes", "hair", "hat", "hat2"].include?(field)
          show_browser(field)
        elsif field == "confirm"
          confirm
        elsif field == "cancel"
          cancel
        else
          move_horizontal(1)
        end
      end

      def move_horizontal(direction)
        if @browser_field
          @window.index = [[@window.index + direction * 8, 0].max, @items.length - 1].min
          redraw
          return
        end
        @root_index = @window.index
        field = ROOT_FIELDS[@root_index]
        if DYE_FIELDS.include?(field)
          @appearance[field] = (@appearance[field] + direction * 10) % 360
        elsif field == "skin_tone"
          @appearance[field] = ((@appearance[field] - 1 + direction) % 6) + 1
        elsif field == "hair_version"
          version, style = Cosmetics.hair_parts(@appearance["hair"])
          versions = @catalog.find("hair", style)["variants"]
          @appearance["hair"] = "#{versions[((versions.index(version) || 0) + direction) % versions.length]}_#{style}"
        elsif field == "preview"
          @preview_mode = PREVIEW_MODES[(PREVIEW_MODES.index(@preview_mode) + direction) % PREVIEW_MODES.length]
        end
        show_root
      end

      def choose_item
        entry = @items[@window.index]
        return show_root if entry == :back
        if entry == :filter
          @owned_only = !@owned_only
          return show_browser(@browser_field)
        end
        if entry && !@initial && !@state["owned"][entry["key"]]
          return if !pbConfirmMessage(_INTL("Permanently unlock {1} for {2} points?", entry["name"], entry["points"]))
          begin
            @state = @profile.purchase(entry)
          rescue StandardError => error
            pbMessage(error.message)
            return
          end
        end
        set_piece(@appearance, @browser_field, entry)
        show_root
      end

      def confirm
        question = @initial ? _INTL("Permanently unlock these selected pieces? This free selection is shared across all saves and cannot be claimed again.") : _INTL("Save this appearance? It will also be used after resetting this save slot.")
        return if !pbConfirmMessage(question)
        begin
          @state = @profile.confirm(@catalog, @appearance, @appearance_key, @initial)
        rescue StandardError => error
          pbMessage(error.message)
          return
        end
        Cosmetics.apply(@appearance)
        @confirmed = true
        @closed = true
      end

      def cancel
        @closed = true if pbConfirmMessage(_INTL("Discard appearance changes? Any completed purchases remain unlocked."))
      end
    end
  end
end
