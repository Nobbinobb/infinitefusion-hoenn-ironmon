#===============================================================================
# Permanent profile commits and semantic draft edits, never render polling.
#===============================================================================
module Ironmon
  module GameplayCollection
    module Wardrobe
      def self.profile_ref(profile)
        return Digest::SHA256.hexdigest(File.expand_path(profile.directory))
      end

      def self.counts(owned)
        result = { "clothes" => 0, "hair" => 0, "hat" => 0, "total" => owned.length }
        owned.keys.each { |key| category = key.split(":")[0]; result[category] = result.fetch(category, 0) + 1 }
        return result
      end

      def self.appearance_keys(appearance)
        keys = []
        keys << "clothes:#{appearance["clothes"]}" if !appearance["clothes"].nil? && !appearance["clothes"].to_s.empty?
        style = Ironmon::Cosmetics.hair_parts(appearance["hair"])[1]
        keys << "hair:#{style}" if style
        ["hat", "hat2"].each do |field|
          keys << "hat:#{appearance[field]}" if !appearance[field].nil? && !appearance[field].to_s.empty?
        end
        return keys.uniq
      end

      def self.appearance_snapshot(appearance)
        result = appearance.dup
        version, style = Ironmon::Cosmetics.hair_parts(appearance["hair"])
        accessories = [appearance["hat"], appearance["hat2"]].compact.reject { |value| value.to_s.empty? }
        result.merge!(
          "canonical_piece_keys" => appearance_keys(appearance),
          "outfit_key" => appearance["clothes"] ? "clothes:#{appearance["clothes"]}" : nil,
          "hair_style_key" => style ? "hair:#{style}" : nil,
          "hair_version" => version,
          "accessory_slot_1_key" => appearance["hat"].to_s.empty? ? nil : "hat:#{appearance["hat"]}",
          "accessory_slot_2_key" => appearance["hat2"].to_s.empty? ? nil : "hat:#{appearance["hat2"]}",
          "distinct_accessory_count" => accessories.uniq.length,
          "confirmed_dye_values" => Ironmon::Cosmetics::DYE_FIELDS.map { |field| { "field" => field, "value" => appearance[field] } },
          "non_default_dye_fields" => Ironmon::Cosmetics::DYE_FIELDS.select { |field| appearance[field].is_a?(Integer) && appearance[field] != 0 },
          "dye_reference_version" => "native-hue-zero-1"
        )
        return result
      end

      def self.piece_for_dye(field, appearance)
        return "clothes:#{appearance["clothes"]}" if field == "clothes_color" && appearance["clothes"]
        return "hair:#{Ironmon::Cosmetics.hair_parts(appearance["hair"])[1]}" if field == "hair_color" && appearance["hair"]
        slot = field == "hat_color" ? "hat" : field == "hat2_color" ? "hat2" : nil
        return slot && !appearance[slot].to_s.empty? ? "hat:#{appearance[slot]}" : nil
      end

      def self.committed_dyes(before, after, session, confirmation)
        Ironmon::Cosmetics::DYE_FIELDS.each do |field|
          next if !before[field].is_a?(Integer) || before[field] == after[field]
          GameplayCollection.emit("wardrobe.dye_changed", {
            "wardrobe_session_id" => session, "piece_key" => piece_for_dye(field, after),
            "dye_field" => field, "value_before" => before[field], "value_after" => after[field],
            "change_stage" => "committed", "confirmation_event_id" => confirmation, "reverted_before_close" => false
          }, "wardrobe_session_id" => session)
        end
      end

      def self.ownership(profile, before, after, source)
        added = after["owned"].keys - before["owned"].keys
        removed = before["owned"].keys - after["owned"].keys
        return if added.empty? && removed.empty?
        GameplayCollection.emit("wardrobe.ownership_changed", {
          "cosmetic_profile_ref" => profile_ref(profile), "profile_revision" => after["revision"],
          "added_piece_keys" => added, "removed_piece_keys" => removed, "ownership_source" => source,
          "owned_counts_after" => counts(after["owned"]), "catalog_version" => Ironmon::Cosmetics::SCHEMA_VERSION.to_s
        })
      end

      def self.preview(screen, before, mode_before, input)
        after = screen.appearance
        mode = screen.preview_mode
        changed = after.keys.select { |key| after[key] != before[key] }
        return if changed.empty? && mode_before == mode
        session = screen.instance_variable_get(:@collection_session_id)
        GameplayCollection.emit("wardrobe.preview_changed", {
          "wardrobe_session_id" => session, "preview_mode" => mode,
          "draft_piece_keys" => appearance_keys(after),
          "locked_piece_keys" => appearance_keys(after).reject { |key| screen.instance_variable_get(:@state)["owned"][key] },
          "changed_field" => changed.length == 1 ? changed[0] : nil, "changed_fields" => changed,
          "input_kind" => mode_before != mode ? "mode_changed" : changed == ["hair"] && input == "horizontal" ? "hair_version_changed" : changed.any? { |field| ["clothes", "hair", "hat", "hat2"].include?(field) } ? "piece_selected" : "other",
          "input_origin" => input, "applied_to_player" => false
        }, "wardrobe_session_id" => session)
        changed.each do |field|
          next if !Ironmon::Cosmetics::DYE_FIELDS.include?(field)
          GameplayCollection.emit("wardrobe.dye_changed", {
            "wardrobe_session_id" => session, "piece_key" => piece_for_dye(field, after),
            "dye_field" => field, "value_before" => before[field], "value_after" => after[field],
            "change_stage" => "draft", "confirmation_event_id" => nil, "reverted_before_close" => nil
          }, "wardrobe_session_id" => session)
        end
        screen.instance_variable_set(:@collection_preview_changes, screen.instance_variable_get(:@collection_preview_changes).to_i + 1)
      end
    end
  end
end

module IronmonCollectionCosmeticProfile
  def award(attempt_id, event_key, points)
    return super if !Ironmon::GameplayCollection::Resources.active?
    before = read
    result = super
    Ironmon::GameplayCollection::Resources.observe do
      after = read
      Ironmon::GameplayCollection.emit("wardrobe.points_award_resolved", {
        "cosmetic_profile_ref" => Ironmon::GameplayCollection::Wardrobe.profile_ref(self),
        "profile_revision_before" => before["revision"], "profile_revision_after" => after["revision"],
        "claim_ref" => Digest::SHA256.hexdigest(Ironmon::Cosmetics.encode([attempt_id, event_key])),
        "award_source_kind" => event_key.start_with?("trainer:") ? "trainer_victory" : event_key.start_with?("badge:") ? "badge_award" : "other",
        "award_event_key" => event_key,
        "nominal_points" => points, "balance_before" => before["points"], "balance_after" => after["points"],
        "actual_points_awarded" => after["points"] - before["points"], "award_outcome" => result ? "granted" : "duplicate_claim"
      })
    end
    return result
  end

  def purchase(entry)
    return super if !Ironmon::GameplayCollection::Resources.active?
    before = read
    result = super
    Ironmon::GameplayCollection::Resources.observe do
      after = result
      session = Ironmon::GameplayCollection.current_context["wardrobe_session_id"]
      Ironmon::GameplayCollection.emit("wardrobe.purchase_resolved", {
        "cosmetic_profile_ref" => Ironmon::GameplayCollection::Wardrobe.profile_ref(self), "wardrobe_session_id" => session,
        "piece_key" => entry["key"], "category" => entry["category"] || entry["key"].split(":")[0],
        "quoted_points" => entry["points"], "actual_points_spent" => before["points"] - after["points"],
        "owned_before" => before["owned"].has_key?(entry["key"]), "owned_after" => after["owned"].has_key?(entry["key"]),
        "profile_revision_before" => before["revision"], "profile_revision_after" => after["revision"], "outcome" => after["revision"] != before["revision"] ? "success" : "no_effect"
      })
      Ironmon::GameplayCollection::Wardrobe.ownership(self, before, after, "paid_purchase")
      screen = Thread.current[:ironmon_collection_wardrobe_screen]
      if screen && before["points"] > after["points"]
        screen.instance_variable_set(:@collection_purchases, screen.instance_variable_get(:@collection_purchases).to_i + 1)
        screen.instance_variable_set(:@collection_points_spent, screen.instance_variable_get(:@collection_points_spent).to_i + before["points"] - after["points"])
      end
    end
    return result
  end

  def confirm(catalog, appearance, appearance_key, initial)
    return super if !Ironmon::GameplayCollection::Resources.active?
    before = read
    previous_actual = Ironmon::Cosmetics.capture
    result = super
    Ironmon::GameplayCollection::Resources.observe do
      previous = previous_actual
      session = Ironmon::GameplayCollection.current_context["wardrobe_session_id"]
      confirmation = Ironmon::GameplayCollection.next_id("event")
      committed = Ironmon::GameplayCollection.emit("wardrobe.appearance_confirmation_resolved", {
        "event_id" => confirmation,
        "cosmetic_profile_ref" => Ironmon::GameplayCollection::Wardrobe.profile_ref(self),
        "wardrobe_session_id" => session,
        "appearance_slot_ref" => appearance_key, "initial_free_claim" => initial,
        "appearance_before" => Ironmon::GameplayCollection::Wardrobe.appearance_snapshot(previous),
        "appearance_after" => Ironmon::GameplayCollection::Wardrobe.appearance_snapshot(appearance),
        "changed_fields" => appearance.keys.select { |key| appearance[key] != previous[key] },
        "newly_owned_piece_keys" => result["owned"].keys - before["owned"].keys,
        "profile_revision" => result["revision"], "outcome" => "success"
      })
      confirmation = nil if !committed
      screen = Thread.current[:ironmon_collection_wardrobe_screen]
      screen.instance_variable_set(:@collection_confirmation_event, confirmation) if screen
      Ironmon::GameplayCollection::Wardrobe.committed_dyes(previous, appearance, session, confirmation)
      Ironmon::GameplayCollection::Wardrobe.ownership(self, before, result, initial ? "initial_free_claim" : "other")
    end
    return result
  end
end
Ironmon::Cosmetics::Profile.prepend(IronmonCollectionCosmeticProfile)

module IronmonCollectionWardrobeScreen
  def run
    collection = Ironmon::GameplayCollection
    return super if !collection.enabled?
    begin
      @collection_session_id = collection.next_id("wardrobe")
    rescue StandardError => error
      collection.fail_collection(error)
      @collection_session_id = nil
      return super
    end
    @collection_preview_changes = 0
    @collection_purchases = 0
    @collection_points_spent = 0
    @collection_confirmation_event = nil
    previous_screen = Thread.current[:ironmon_collection_wardrobe_screen]
    Thread.current[:ironmon_collection_wardrobe_screen] = self
    completed = false
    result = collection.with_context("wardrobe_session_id" => @collection_session_id, "cosmetic_profile_ref" => collection::Wardrobe.profile_ref(@profile)) { super }
    completed = true
    return result
  ensure
    if @collection_session_id
      collection::Resources.observe do
        collection.emit("wardrobe.session_ended", {
          "wardrobe_session_id" => @collection_session_id, "cosmetic_profile_ref" => collection::Wardrobe.profile_ref(@profile),
          "result" => completed ? (@confirmed ? "confirmed" : "cancelled") : "error",
          "confirmed_appearance_event_id" => @collection_confirmation_event,
          "completed_purchase_count" => @collection_purchases,
          "points_spent_in_session" => @collection_points_spent,
          "semantic_preview_change_count" => @collection_preview_changes, "failure_category" => completed ? nil : "native_exception"
        }, "wardrobe_session_id" => @collection_session_id)
      end
      @collection_session_id = nil
    end
    Thread.current[:ironmon_collection_wardrobe_screen] = previous_screen if defined?(previous_screen)
  end

  def move_horizontal(direction)
    before = @appearance.dup
    mode = @preview_mode
    result = super
    Ironmon::GameplayCollection::Resources.observe { Ironmon::GameplayCollection::Wardrobe.preview(self, before, mode, "horizontal") } if @collection_session_id
    return result
  end

  def choose_item
    before = @appearance.dup
    mode = @preview_mode
    result = super
    Ironmon::GameplayCollection::Resources.observe { Ironmon::GameplayCollection::Wardrobe.preview(self, before, mode, "selection") } if @collection_session_id
    return result
  end
end
Ironmon::Cosmetics::WardrobeScreen.prepend(IronmonCollectionWardrobeScreen)
