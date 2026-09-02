module IronmonCosmeticAudit
  C = Ironmon::Cosmetics

  def self.run
    catalog = C::Catalog.new
    entries = catalog.entries.values.sort_by { |entry| entry["key"] }
    entries.each do |entry|
      folder = File.join("Graphics", "Characters", "player", entry["category"], entry["id"])
      assets = entry["assets"].sort.map do |name|
        path = File.join(folder, name)
        [name, File.file?(path) ? Digest::SHA256.hexdigest(File.binread(path)) : nil]
      end
      entry["asset_fingerprint"] = Digest::SHA256.hexdigest(C.encode(assets))
    end
    previous = {}
    previous_warning = nil
    if $ironmon_cosmetic_previous_path && File.file?($ironmon_cosmetic_previous_path)
      begin
        document = C.decode(File.read($ironmon_cosmetic_previous_path, encoding: Encoding::UTF_8))
        previous = document.fetch("entries").to_h { |entry| [entry.fetch("key"), entry] }
      rescue StandardError => error
        previous_warning = "Previous audit could not be compared: #{error.message}"
      end
    end
    current = entries.to_h { |entry| [entry["key"], entry] }
    available = entries.select { |entry| entry["available"] }
    report = {
      "schema_version" => C::SCHEMA_VERSION,
      "game_version" => Settings::GAME_VERSION_NUMBER,
      "source_hashes" => catalog.source_hashes,
      "policy" => "Optional cosmetics: discover live metadata and assets; never gate gameplay on this audit.",
      "controls" => { "skin_tones" => (1..6).to_a, "dye_hues" => (0...360).step(10).to_a,
                      "dye_targets" => C::DYE_FIELDS, "accessory_slots" => 2,
                      "hair_color_variants" => "Detected per hairstyle from matching overworld and trainer sprites." },
      "point_bands" => C::PRICE_BANDS + [[nil, 600]],
      "fallback_prices" => { "clothes" => 80, "hat" => 80, "hair" => 20 },
      "rewards" => { "trainer_base" => 10, "per_badge" => 5, "badge_cap" => 8, "badge" => 100, "hall_of_fame" => 500 },
      "cosmetic_only_guards" => ["nurseOutfitHeal", "pickUpTypeItemSetBonus",
        "isWearingTeamRocketOutfit", "isWearingTeamAquaOutfit",
        "isWearingTeamMagmaOutfit", "CLOTHES_BREEDER", "HAT_ZOROARK", "HAT_TRUMPET"],
      "summary" => { "catalog_entries" => entries.length, "available_entries" => available.length,
                     "catalog_total_points" => entries.sum { |entry| entry["points"] },
                     "available_total_points" => available.sum { |entry| entry["points"] },
                     "fallback_count" => entries.count { |entry| entry["fallback_price"] },
                     "price_counts" => entries.group_by { |entry| entry["points"].to_s }.transform_values(&:length) },
      "changes" => { "added" => (current.keys - previous.keys).sort,
                     "removed" => (previous.keys - current.keys).sort,
                     "changed" => (current.keys & previous.keys).select { |key| current[key] != previous[key] }.sort },
      "warnings" => catalog.warnings + [previous_warning].compact,
      "entries" => entries
    }
    File.binwrite($ironmon_cosmetic_audit_path, C.encode(report))
  end
end

begin
  IronmonCosmeticAudit.run
  exit! 0
rescue Exception => error
  File.binwrite($ironmon_cosmetic_audit_path + ".error", "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")
  exit! 1
end
