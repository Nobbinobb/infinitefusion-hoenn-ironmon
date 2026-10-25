module Ironmon
  module Cosmetics
    WARDROBE_PROTOCOL_VERSION = 1
    WARDROBE_ACTIONS = %w[change purchase confirm cancel save_profile replace_profile rename_profile delete_profile load_profile].freeze

    class << self
      attr_accessor :active_wardrobe, :last_wardrobe_snapshot
    end

    def self.wardrobe_open?
      return !@active_wardrobe.nil?
    end

    def self.wardrobe_snapshot
      return @active_wardrobe.snapshot(true) if @active_wardrobe
      return @last_wardrobe_snapshot if @last_wardrobe_snapshot && @last_wardrobe_snapshot["game_session_id"] == Ironmon.game_session_id
      return { "protocol_version" => WARDROBE_PROTOCOL_VERSION, "game_session_id" => Ironmon.game_session_id, "open" => false }
    end

    def self.wardrobe_request(payload, preview = false)
      raise "Open the wardrobe in the game first." if !@active_wardrobe
      raise "A matching tracker is required." if !Ironmon.tracker_connection.wardrobe_supported?
      return preview ? @active_wardrobe.preview_request(payload) : @active_wardrobe.handle_request(payload)
    end

    class WardrobeScreen < WardrobePreview
      PREVIEW_MODES = %w[portrait front back left right bike].freeze
      attr_reader :appearance, :preview_mode, :revision, :wardrobe_id

      def initialize(catalog, profile, appearance_key)
        @catalog, @profile, @appearance_key = catalog, profile, appearance_key
        @state = @profile.read
        @initial = !@state["initial_claimed"]
        @appearance = (@state["appearances"][@appearance_key] || Cosmetics.capture).dup
        @equipped = Cosmetics.capture
        @preview_mode = "portrait"
        @wardrobe_id = Random.urandom(16).unpack1("H*")
        @revision = 0
        @closed = @confirmed = false
        @preview_cache = {}
        @palette_cache = {}
        @operation_cache = {}
        normalize_selection
      end

      def normalize_selection
        current = Cosmetics.capture
        APPEARANCE_FIELDS.each { |field| @appearance[field] = current[field] if !@appearance.has_key?(field) }
        DYE_FIELDS.each { |field| @appearance[field] = @appearance[field].to_i % 360 }
        @appearance["skin_tone"] = [[@appearance["skin_tone"].to_i, 1].max, 6].min
        version, style = Cosmetics.hair_parts(@appearance["hair"])
        ["clothes", "hair"].each do |category|
          id = category == "hair" ? style : @appearance[category]
          entry = @catalog.find(category, id)
          entry = @catalog.available(category).find { |item| @state["owned"][item["key"]] } || @catalog.available(category).first if !entry || !entry["available"]
          raise "No complete #{category} graphics are installed." if !entry
          @appearance[category] = category == "hair" ? "#{entry["variants"].include?(version) ? version : entry["variants"].first}_#{entry["id"]}" : entry["id"]
        end
        ["hat", "hat2"].each { |field| @appearance[field] = nil if !@catalog.valid?("hat", @appearance[field]) }
      end

      def run
        raise "A wardrobe is already open." if Cosmetics.active_wardrobe
        Ironmon.tick_active_run_duration
        Cosmetics.active_wardrobe = self
        Ironmon.clear_tracker_controls
        create_widgets
        was_ready = false
        until @closed
          Graphics.ironmon_runtime_hooks_original_update
          Input.update
          Ironmon.update_tracker_connection
          ready = Ironmon.tracker_connection.wardrobe_supported?
          publish(true) if ready && !was_ready
          redraw_waiting(ready) if ready != was_ready || @waiting_ready.nil?
          was_ready = ready
          close(false) if Input.trigger?(Input::BACK)
        end
        return @confirmed
      ensure
        @closed = true
        begin
          Cosmetics.last_wardrobe_snapshot = snapshot(false)
          publish(false) if Cosmetics.active_wardrobe.equal?(self)
        ensure
          Cosmetics.active_wardrobe = nil if Cosmetics.active_wardrobe.equal?(self)
          Ironmon.resume_run_duration
          Ironmon.clear_tracker_controls
          dispose
        end
      end

      def create_widgets
        @viewport = Viewport.new(0, 0, Graphics.width, Graphics.height)
        @viewport.z = 99990
        @background = BitmapSprite.new(Graphics.width, Graphics.height, @viewport)
        @background.bitmap.fill_rect(0, 0, Graphics.width, Graphics.height, Color.new(38, 45, 54))
        @overlay = BitmapSprite.new(Graphics.width, Graphics.height, @viewport)
      end

      def redraw_waiting(ready)
        @waiting_ready = ready
        canvas = @overlay.bitmap
        canvas.clear
        pbSetSystemFont(canvas)
        base, shadow = Color.new(238, 242, 246), Color.new(14, 18, 23)
        pbDrawShadowText(canvas, 16, Graphics.height / 2 - 64, Graphics.width - 32, 40, _INTL("Wardrobe"), base, shadow, 1)
        text = ready ? _INTL("Continue in the Ironmon Tracker.") : _INTL("Connect the Ironmon Tracker to edit your outfit.")
        pbDrawShadowText(canvas, 16, Graphics.height / 2 - 14, Graphics.width - 32, 32, text, base, shadow, 1)
        pbDrawShadowText(canvas, 16, Graphics.height - 50, Graphics.width - 32, 32, _INTL("Back: Cancel"), base, shadow, 1)
      end

      def dispose
        [@overlay, @background, @viewport].each { |object| object.dispose if object && !object.disposed? }
      end

      def snapshot(include_catalog = false)
        @state = @profile.read
        result = {
          "protocol_version" => WARDROBE_PROTOCOL_VERSION, "game_session_id" => Ironmon.game_session_id,
          "wardrobe_id" => @wardrobe_id, "revision" => @revision, "profile_revision" => @state["revision"],
          "appearance_key" => @appearance_key, "open" => !@closed, "confirmed" => @confirmed,
          "initial_selection" => @initial, "points" => @state["points"], "owned_keys" => @state["owned"].keys,
          "appearance" => @appearance.dup, "equipped_appearance" => @equipped.dup,
          "loaded_profile_id" => @loaded_profile_id,
          "outfit_profiles" => @state["outfit_profiles"].map do |id, entry|
            draft = @appearance.merge(entry["appearance"])
            { "id" => id, "name" => entry["name"], "appearance" => draft, "available" => profile_available?(draft) }
          end.sort_by { |entry| [entry["name"].downcase, entry["id"]] }
        }
        result["catalog"] = catalog_payload if include_catalog
        return result
      end

      def catalog_payload
        @catalog_payload ||= @catalog.entries.values.map do |entry|
          entry.select { |key, _value| %w[key id category name tags points available variants].include?(key) }
        end.sort_by { |entry| [entry["category"], entry["name"].downcase, entry["id"]] }
        return @catalog_payload
      end

      def profile_available?(appearance)
        Cosmetics.validate_appearance(@catalog, appearance)
        return Cosmetics.appearance_keys(appearance).all? { |key| @state["owned"][key] }
      rescue StandardError
        return false
      end

      def publish(include_catalog)
        Ironmon.tracker_connection.send_event("wardrobe_state", snapshot(include_catalog))
      end

      def validate_request(payload)
        raise "The wardrobe session has changed." if @closed || !payload.is_a?(Hash) ||
          payload["game_session_id"] != Ironmon.game_session_id || payload["wardrobe_id"] != @wardrobe_id
        raise "The wardrobe has changed. Try again." if payload["revision"] != @revision
      end

      def handle_request(payload)
        operation = payload.is_a?(Hash) ? payload["operation_id"] : nil
        raise "Invalid wardrobe operation." if !operation.is_a?(String) || !operation.match?(/\A[0-9a-f]{32}\z/)
        fingerprint = Digest::SHA256.hexdigest(Cosmetics.encode(payload))
        if @operation_cache.has_key?(operation)
          cached = @operation_cache[operation]
          raise "Wardrobe operation identity was reused." if cached[0] != fingerprint
          return cached[1]
        end
        validate_request(payload)
        action = payload["action"]
        raise "Invalid wardrobe action." if !WARDROBE_ACTIONS.include?(action)
        @state = @profile.read
        if %w[purchase save_profile replace_profile rename_profile delete_profile confirm].include?(action)
          raise "The wardrobe profile has changed. Try again." if payload["profile_revision"] != @state["revision"]
        end
        if action == "change"
          change_appearance(payload["appearance"])
        elsif action == "purchase"
          raise "Confirm your free look first." if @initial
          entry = @catalog.entries[payload["piece_key"]]
          raise "This item is not selected." if !entry || !Cosmetics.appearance_keys(@appearance).include?(entry["key"])
          @state = @profile.purchase(entry, payload["profile_revision"])
        elsif action == "confirm"
          @state = @profile.confirm(@catalog, @appearance, @appearance_key, @initial, payload["profile_revision"])
          Cosmetics.apply(@appearance)
          @equipped = @appearance.dup
          close(true)
        elsif action == "cancel"
          close(false)
        elsif action == "save_profile" || action == "replace_profile"
          @state = @profile.save_outfit_profile(@catalog, payload["profile_id"], payload["name"], @appearance, action == "replace_profile", payload["profile_revision"])
          @loaded_profile_id = payload["profile_id"]
        elsif action == "rename_profile"
          @state = @profile.rename_outfit_profile(payload["profile_id"], payload["name"], payload["profile_revision"])
        elsif action == "delete_profile"
          @state = @profile.delete_outfit_profile(payload["profile_id"], payload["profile_revision"])
          @loaded_profile_id = nil if @loaded_profile_id == payload["profile_id"]
        elsif action == "load_profile"
          entry = @state["outfit_profiles"][payload["profile_id"]]
          raise "This outfit profile no longer exists." if !entry
          draft = @appearance.merge(entry["appearance"])
          raise "This profile contains unavailable items." if !profile_available?(draft)
          change_appearance(draft)
          @loaded_profile_id = payload["profile_id"]
        end
        @revision += 1
        response = snapshot(false)
        @operation_cache.shift if @operation_cache.length >= 128
        @operation_cache[operation] = [fingerprint, response]
        Cosmetics.last_wardrobe_snapshot = response if @closed
        return response
      end

      def change_appearance(appearance)
        raise "Invalid wardrobe appearance." if !appearance.is_a?(Hash) || appearance.keys.sort != APPEARANCE_FIELDS.sort
        draft = appearance.dup
        Cosmetics.validate_appearance(@catalog, draft)
        @appearance = draft
      end

      def close(confirmed)
        @confirmed = confirmed
        @closed = true
      end

      def preview_request(payload)
        validate_request(payload)
        mode = payload["mode"] || "portrait"
        raise "Invalid wardrobe preview." if !PREVIEW_MODES.include?(mode)
        draft = @appearance.dup
        if payload["profile_id"]
          entry = @state["outfit_profiles"][payload["profile_id"]]
          raise "This outfit profile no longer exists." if !entry
          draft.merge!(entry["appearance"])
          raise "This profile contains unavailable items." if !profile_available?(draft)
        end
        piece = payload["piece_key"] ? @catalog.entries[payload["piece_key"]] : nil
        raise "Unavailable wardrobe item." if payload["piece_key"] && (!piece || !piece["available"])
        if piece
          field = piece["category"] == "hat" ? payload["accessory_slot"] : piece["category"]
          raise "Invalid accessory slot." if piece["category"] == "hat" && !%w[hat hat2].include?(field)
          if field == "hair"
            version = Cosmetics.hair_parts(draft["hair"])[0]
            draft[field] = "#{piece["variants"].include?(version) ? version : piece["variants"].first}_#{piece["id"]}"
          else
            draft[field] = piece["id"]
          end
        end
        field = payload["dye_field"] || "hair_color"
        raise "Invalid dye field." if !DYE_FIELDS.include?(field)
        head = payload["head_only"] == true && mode == "portrait"
        cache_key = Digest::SHA256.hexdigest(Cosmetics.encode([draft, mode, head, field, piece.nil?]))
        result = @preview_cache[cache_key]
        if !result
          result = preview_image(draft, mode, head)
          result.merge!(preview_palette(draft, field)) if !piece
          @preview_cache.shift if @preview_cache.length >= 96
          @preview_cache[cache_key] = result
        end
        return result.merge("wardrobe_id" => @wardrobe_id, "revision" => @revision)
      end
    end
  end
end
