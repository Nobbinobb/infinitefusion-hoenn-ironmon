#===============================================================================
# Shared cosmetic ownership and exactly-once awards, outside all game saves
#===============================================================================
module Ironmon
  module Cosmetics
    class Profile
      attr_reader :directory

      def initialize(directory)
        @directory = directory
      end

      def empty
        return { "schema_version" => SCHEMA_VERSION, "revision" => 0,
                 "points" => 0, "initial_claimed" => false, "owned" => {},
                 "appearances" => {}, "initial_appearance" => nil, "awards" => {}, "outfit_profiles" => {} }
      end

      def synchronize
        Dir.mkdir(@directory) if !File.directory?(@directory)
        File.open(File.join(@directory, "profile.lock"), "a+b") do |lock|
          raise "The shared wardrobe is in use by another game." if !lock.flock(File::LOCK_EX | File::LOCK_NB)
          begin
            return yield
          ensure
            lock.flock(File::LOCK_UN)
          end
        end
      end

      def read_unlocked
        snapshots = ["profile-a.json", "profile-b.json"].filter_map do |name|
          path = File.join(@directory, name)
          next if !File.file?(path)
          envelope = Cosmetics.decode(File.read(path, encoding: Encoding::UTF_8))
          payload = envelope["payload"]
          raise "The shared wardrobe profile is damaged; it has not been reset." if
            !payload.is_a?(String) || Digest::SHA256.hexdigest(payload) != envelope["sha256"]
          state = Cosmetics.decode(payload)
          validate(state)
          state
        end
        return snapshots.max_by { |state| state["revision"] } || empty
      end

      def validate(state)
        raise "Unsupported or damaged wardrobe profile; it has not been reset." if
          !state.is_a?(Hash) || state["schema_version"] != SCHEMA_VERSION ||
          ![true, false].include?(state["initial_claimed"]) ||
          !["owned", "appearances", "awards"].all? { |key| state[key].is_a?(Hash) } ||
          !["revision", "points"].all? { |key| state[key].is_a?(Numeric) && state[key] >= 0 && state[key] == state[key].to_i }
        state["revision"] = state["revision"].to_i
        state["points"] = state["points"].to_i
        state["outfit_profiles"] = {} if !state.has_key?("outfit_profiles")
        profiles = state["outfit_profiles"]
        raise "The saved outfit profiles are damaged; they have not been reset." if !profiles.is_a?(Hash)
        profiles.each do |id, entry|
          raise "A saved outfit profile is damaged; it has not been reset." if
            !id.is_a?(String) || !id.match?(/\A[0-9a-f]{32}\z/) || !entry.is_a?(Hash) ||
            !entry["name"].is_a?(String) || entry["name"].strip.empty? || entry["name"].length > 48 ||
            !entry["appearance"].is_a?(Hash) || (entry["appearance"].keys - OUTFIT_PROFILE_FIELDS).any?
        end
      end

      def read
        return synchronize do
          state = read_unlocked
          write_unlocked(state) if unlock_default_appearance(state)
          state
        end
      end

      def transaction(expected_revision = nil)
        return synchronize do
          state = read_unlocked
          raise "The wardrobe profile has changed. Try again." if expected_revision && state["revision"] != expected_revision
          defaults_unlocked = unlock_default_appearance(state)
          changed = yield(state)
          write_unlocked(state) if defaults_unlocked || changed
          state
        end
      end

      def unlock_default_appearance(state)
        changed = false
        Cosmetics.default_appearance_keys.each do |key|
          next if state["owned"][key]
          state["owned"][key] = true
          changed = true
        end
        return changed
      end

      def write_unlocked(state)
        state["revision"] += 1
        validate(state)
        payload = Cosmetics.encode(state)
        envelope = Cosmetics.encode({ "payload" => payload, "sha256" => Digest::SHA256.hexdigest(payload) })
        target = File.join(@directory, state["revision"].odd? ? "profile-a.json" : "profile-b.json")
        temporary = File.join(@directory, "profile-writing.tmp")
        File.open(temporary, "wb") do |file|
          file.write(envelope)
          file.flush
          file.fsync
        end
        # Replace only the older snapshot. The latest committed revision always
        # survives a crash before rename; neither slot is silently reset on corruption.
        File.delete(target) if File.file?(target)
        File.rename(temporary, target)
      end

      def award(attempt_id, event_key, points)
        raise "Invalid cosmetic award" if attempt_id.to_s.empty? || event_key.to_s.empty? || points <= 0
        granted = false
        transaction do |state|
          claims = state["awards"][attempt_id] ||= {}
          next false if claims[event_key]
          claims[event_key] = points
          state["points"] += points
          granted = true
        end
        return granted
      end

      def purchase(entry, expected_revision = nil)
        raise "This cosmetic is unavailable." if !entry || !entry["available"]
        return transaction(expected_revision) do |state|
          next false if state["owned"][entry["key"]]
          raise "Not enough wardrobe points." if state["points"] < entry["points"]
          state["points"] -= entry["points"]
          state["owned"][entry["key"]] = true
        end
      end

      def award_achievements(ids)
        raise "Invalid wardrobe achievement rewards." if !ids.is_a?(Array) || ids.length > MAXIMUM_ACHIEVEMENT_REWARDS ||
          ids.any? { |id| !id.is_a?(String) || !id.match?(/\A[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\z/) }
        awarded = []
        balance_before = nil
        revision_before = nil
        state = transaction do |current|
          balance_before = current["points"]
          revision_before = current["revision"]
          claims = current["awards"]["achievement_unlocks"] ||= {}
          ids.map(&:downcase).uniq.each do |id|
            next if claims[id]
            claims[id] = ACHIEVEMENT_REWARD_POINTS
            awarded << id
          end
          current["points"] += awarded.length * ACHIEVEMENT_REWARD_POINTS
          !awarded.empty?
        end
        return { "state" => state, "awarded_ids" => awarded, "balance_before" => balance_before, "revision_before" => revision_before }
      end

      def confirm(catalog, appearance, appearance_key, initial, expected_revision = nil)
        selected_keys = Cosmetics.appearance_keys(appearance)
        raise "Select an available outfit and hairstyle." if !appearance["clothes"] || !appearance["hair"]
        Cosmetics.validate_appearance(catalog, appearance)
        return transaction(expected_revision) do |state|
          if initial
            raise "The free selection has already been claimed." if state["initial_claimed"]
            selected_keys.each { |key| state["owned"][key] = true }
            state["initial_claimed"] = true
            state["initial_appearance"] = appearance.dup
          else
            raise "This appearance contains a locked cosmetic." if selected_keys.any? { |key| !state["owned"][key] }
          end
          state["appearances"][appearance_key] = appearance.dup
          true
        end
      end

      def save_outfit_profile(catalog, id, name, appearance, replace = false, expected_revision = nil)
        validate_outfit_profile_id(id)
        title = validate_outfit_profile_name(name)
        Cosmetics.validate_appearance(catalog, appearance)
        selected = Cosmetics.appearance_keys(appearance)
        return transaction(expected_revision) do |state|
          profiles = state["outfit_profiles"]
          raise "This outfit profile no longer exists." if replace && !profiles.has_key?(id)
          raise "This outfit profile already exists." if !replace && profiles.has_key?(id)
          raise "Unlock the selected items first." if selected.any? { |key| !state["owned"][key] }
          reject_duplicate_outfit_name(profiles, id, title)
          profiles[id] = { "name" => title, "appearance" => appearance.select { |field, _value| OUTFIT_PROFILE_FIELDS.include?(field) } }
          true
        end
      end

      def rename_outfit_profile(id, name, expected_revision = nil)
        validate_outfit_profile_id(id)
        title = validate_outfit_profile_name(name)
        return transaction(expected_revision) do |state|
          profiles = state["outfit_profiles"]
          raise "This outfit profile no longer exists." if !profiles.has_key?(id)
          reject_duplicate_outfit_name(profiles, id, title)
          next false if profiles[id]["name"] == title
          profiles[id]["name"] = title
          true
        end
      end

      def delete_outfit_profile(id, expected_revision = nil)
        validate_outfit_profile_id(id)
        return transaction(expected_revision) { |state| !state["outfit_profiles"].delete(id).nil? }
      end

      def validate_outfit_profile_id(id)
        raise "Invalid outfit profile." if !id.is_a?(String) || !id.match?(/\A[0-9a-f]{32}\z/)
      end

      def validate_outfit_profile_name(name)
        raise "Enter a profile name of 1–48 characters." if !name.is_a?(String)
        title = name.strip
        raise "Enter a profile name of 1–48 characters." if title.empty? || title.length > 48 || title.match?(/[[:cntrl:]]/)
        return title
      end

      def reject_duplicate_outfit_name(profiles, id, title)
        raise "Name already used." if profiles.any? { |key, entry| key != id && entry["name"].downcase == title.downcase }
      end
    end

    def self.hair_parts(full_id)
      match = /\A(\d+)_(.+)\z/.match(full_id.to_s)
      return match ? [match[1].to_i, match[2]] : [nil, nil]
    end

    def self.appearance_keys(appearance)
      keys = ["clothes:#{appearance["clothes"]}", "hair:#{hair_parts(appearance["hair"])[1]}"]
      ["hat", "hat2"].each { |field| keys << "hat:#{appearance[field]}" if appearance[field] && !appearance[field].empty? }
      return keys.uniq
    end

    def self.validate_appearance(catalog, appearance)
      raise "Unavailable outfit." if !catalog.valid?("clothes", appearance["clothes"])
      version, style = hair_parts(appearance["hair"])
      hair = catalog.find("hair", style)
      raise "Unavailable hairstyle or color." if !hair || !hair["available"] || !hair["variants"].include?(version)
      ["hat", "hat2"].each do |field|
        id = appearance[field]
        raise "Unavailable accessory." if id && !id.empty? && !catalog.valid?("hat", id)
      end
      raise "Invalid skin tone." if !(1..6).include?(appearance["skin_tone"])
      DYE_FIELDS.each do |field|
        value = appearance[field]
        raise "Invalid dye." if !value.is_a?(Integer) || !(0...360).include?(value)
      end
      return true
    end
  end
end
