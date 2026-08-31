# Compiles reviewed audit classifications against the installed public catalogs.
# No live battle or save is loaded, and no version mismatch can invalidate data.
require "digest/sha2"

module IronmonDefensePresentationExport
  def self.symbolize(value)
    return value.to_i if value.is_a?(Float) && value.finite? && value == value.to_i
    return value.map { |entry| symbolize(entry) } if value.is_a?(Array)
    return value unless value.is_a?(Hash)
    return value.each_with_object({}) { |(key, entry), result| result[key.to_sym] = symbolize(entry) }
  end

  def self.resolve_moves(value)
    if value.is_a?(Hash)
      if value[:move_filter]
        value[:move_names] = filtered_moves(value[:move_filter])
      elsif value[:move_flag]
        names = []
        GameData::Move.each { |move| names << move.name if move.flags.include?(value[:move_flag]) }
        value[:move_names] = names.sort
      elsif value[:moves]
        value[:move_names] = value[:moves].filter_map { |id| GameData::Move.try_get(id.to_sym)&.name }.sort
      end
      value.each_value { |entry| resolve_moves(entry) }
    elsif value.is_a?(Array)
      value.each { |entry| resolve_moves(entry) }
    end
  end

  # Only inspect move metadata and pure predicates on detached move objects.
  # No battle handler or live battler is invoked by catalog generation.
  def self.filtered_moves(filter)
    names = []
    GameData::Move.each do |entry|
      next if filter[:flag] && !entry.flags.include?(filter[:flag])
      next if filter[:functions] && !filter[:functions].include?(entry.function_code)
      next if filter[:category] == "status" && entry.category != 2
      next if filter[:category] == "damaging" && entry.category == 2
      next if filter[:minimum_accuracy] && entry.accuracy < filter[:minimum_accuracy]
      next if filter[:targets_foe] && !GameData::Target.get(entry.target).targets_foe
      if filter[:recoil] || filter[:substitute_blocked]
        move = PokeBattle_Move.from_pokemon_move(nil, Pokemon::Move.new(entry.id))
        next if filter[:recoil] && !move.recoilMove?
        next if filter[:substitute_blocked] && move.ignoresSubstitute?(nil)
        next if filter[:substitute_blocked] && GameData::Target.get(entry.target).num_targets == 0
      end
      names << entry.name
    end
    return names.sort
  end

  def self.run
    document = symbolize(HTTPLite::JSON.parse(File.read($ironmon_defense_rules_path, encoding: Encoding::UTF_8)))
    raise "Unsupported defense audit schema" if document[:schema_version] != 1
    catalogs = { status: GameData::Status, weather: GameData::BattleWeather, move: GameData::Move }
    document[:label_catalogs].each do |kind, ids|
      ids.each do |id|
        entry = catalogs.fetch(kind).try_get(id.to_sym)
        document[:labels][id.to_sym] = entry ? entry.name : id
      end
    end
    resolve_moves(document)
    document[:source_catalog_hashes] = {}
    [GameData::Type, GameData::Move, GameData::Ability, GameData::Item].each do |catalog|
      path = File.join("Data", catalog::DATA_FILENAME)
      document[:source_catalog_hashes][catalog::DATA_FILENAME] = Digest::SHA256.hexdigest(File.binread(path))
    end
    rows = [["kind", "id", "name", "modeled", "factor", "protections", "recovery", "condition", "move_count"]]
    { ability: [GameData::Ability, document[:abilities]], item: [GameData::Item, document[:items]] }.each do |kind, pair|
      pair[0].each do |entry|
        definition = pair[1][entry.id]
        if kind == :item && document[:resist_berries][entry.id]
          definition = document[:resist_berry].merge(types: [document[:resist_berries][entry.id]])
        end
        append_rule_rows(rows, kind, entry.id, entry.name, definition)
      end
    end
    [:weather, :terrain, :side, :effects, :position_recovery, :linked_recovery].each do |table|
      document.fetch(table, {}).each do |id, definitions|
        definitions = [definitions] if definitions.is_a?(Hash)
        definitions.each_with_index { |rule, index| append_rule_rows(rows, table, "#{id}.#{index}", id, rule) }
      end
    end
    [:type_protections, :state_conditions].each do |table|
      document.fetch(table, []).each { |rule| append_rule_rows(rows, table, rule[:id], rule[:id], rule) }
    end
    csv = rows.map do |row|
      row.map do |value|
        value = encode(value) if value.is_a?(Hash) || value.is_a?(Array)
        '"' + value.to_s.gsub('"', '""') + '"'
      end.join(",")
    end.join("\n") + "\n"
    File.binwrite($ironmon_defense_audit_path, csv)
    temporary = $ironmon_defense_catalog_path + ".tmp"
    File.binwrite(temporary, encode(document) + "\n")
    File.rename(temporary, $ironmon_defense_catalog_path)
  end

  def self.append_rule_rows(rows, kind, id, name, definition)
    rows << [kind, id, name, !definition.nil?, definition && definition[:factor],
      definition && definition[:protections], definition && definition[:recovery],
      definition && definition[:condition], definition && definition.fetch(:move_names, []).length]
    return if !definition
    definition.fetch(:additional, []).each_with_index do |child, index|
      append_rule_rows(rows, "#{kind}_effect", "#{id}.additional.#{index}", name, child)
    end
    [:ally, :global].each do |scope|
      append_rule_rows(rows, "#{kind}_effect", "#{id}.#{scope}", name, definition[scope]) if definition[scope]
    end
  end

  # The native serializer rounds numbers to six significant digits. Keep Ruby's
  # round-trip numeric representation, using native JSON escaping for strings.
  def self.encode(value)
    return "{" + value.map { |key, child| "#{encode(key.to_s)}:#{encode(child)}" }.join(",") + "}" if value.is_a?(Hash)
    return "[" + value.map { |child| encode(child) }.join(",") + "]" if value.is_a?(Array)
    return HTTPLite::JSON.stringify(value.to_s) if value.is_a?(String) || value.is_a?(Symbol)
    return value.to_s if value.is_a?(Numeric) || value == true || value == false
    return "null" if value.nil?
    raise "Unsupported defense catalog value #{value.class}"
  end
end

begin
  IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:997|998|999)/])
  GameData.load_all
  IronmonDefensePresentationExport.run
  exit! 0
rescue Exception => error
  File.binwrite($ironmon_defense_catalog_path + ".error", "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")
  exit! 1
end
