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
                 "appearances" => {}, "initial_appearance" => nil, "awards" => {} }
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
      end

      def read
        return synchronize { read_unlocked }
      end

      def transaction
        return synchronize do
          state = read_unlocked
          changed = yield(state)
          write_unlocked(state) if changed
          state
        end
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

      def purchase(entry)
        raise "This cosmetic is unavailable." if !entry || !entry["available"]
        return transaction do |state|
          next false if state["owned"][entry["key"]]
          raise "Not enough wardrobe points." if state["points"] < entry["points"]
          state["points"] -= entry["points"]
          state["owned"][entry["key"]] = true
        end
      end

      def confirm(catalog, appearance, appearance_key, initial)
        selected_keys = Cosmetics.appearance_keys(appearance)
        raise "Select an available outfit and hairstyle." if !appearance["clothes"] || !appearance["hair"]
        Cosmetics.validate_appearance(catalog, appearance)
        return transaction do |state|
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
