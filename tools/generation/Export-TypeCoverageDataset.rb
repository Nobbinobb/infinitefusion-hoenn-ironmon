module IronmonTypeCoverageExporter
  SCHEMA_VERSION = 1
  STANDARD_TYPES = [
    :NORMAL, :FIRE, :WATER, :ELECTRIC, :GRASS, :ICE, :FIGHTING,
    :POISON, :GROUND, :FLYING, :PSYCHIC, :BUG, :ROCK, :GHOST,
    :DRAGON, :DARK, :STEEL, :FAIRY
  ].freeze
  TYPE_ORDER = STANDARD_TYPES.each_with_index.to_h.freeze

  def self.run(output_path, audit_path)
    normal_pool = Ironmon.normal_species_pool
    fusion_pool = Ironmon.custom_fusion_pool
    profiles = {}
    normal_pool.each do |species_id|
      species = GameData::Species.get(species_id)
      add_profile(profiles, species_types(species), "normal_count")
    end
    fusion_pool.each do |species_id|
      add_profile(profiles, fusion_types(species_id), "fusion_count")
    end

    rows = profiles.values.sort_by do |profile|
      profile["types"].map { |type| TYPE_ORDER[type.to_sym] }
    end
    document = {
      "schema_version" => SCHEMA_VERSION,
      "game_version" => Settings::GAME_VERSION_NUMBER.to_s,
      "normal_pool_size" => normal_pool.length,
      "normal_pool_fingerprint" =>
        Ironmon.species_pool_fingerprint(normal_pool),
      "fusion_pool_schema_version" =>
        Ironmon::CustomFusionPool::SCHEMA_VERSION,
      "fusion_pool_size" => fusion_pool.length,
      "fusion_pool_fingerprint" => Ironmon.custom_fusion_pool_info[:fingerprint],
      "profiles" => rows
    }
    validate_document(document)
    validate_tracker_context(document)
    write_json(output_path, document)
    write_audit(audit_path, document) if !audit_path.empty?
    return document
  end

  def self.species_types(species)
    return normalize_types([species.type1, species.type2])
  end

  def self.fusion_types(species_id)
    match = /\AB(\d+)H(\d+)\z/.match(species_id.to_s)
    raise "invalid fusion pool identifier #{species_id}" if !match
    fusion = GameData::FusedSpecies.allocate
    fusion.instance_variable_set(
      :@body_pokemon, GameData::Species.get(match[1].to_i)
    )
    fusion.instance_variable_set(
      :@head_pokemon, GameData::Species.get(match[2].to_i)
    )
    type1 = fusion.calculate_type1
    fusion.instance_variable_set(:@type1, type1)
    return normalize_types([type1, fusion.calculate_type2])
  end

  def self.normalize_types(types)
    normalized = types.compact.uniq
    if normalized.empty? || normalized.length > 2
      raise "invalid defensive type profile #{normalized.inspect}"
    end
    normalized.each do |type|
      raise "unsupported defensive type #{type}" if !TYPE_ORDER.key?(type)
    end
    return normalized.sort_by { |type| TYPE_ORDER[type] }
  end

  def self.add_profile(profiles, types, count_name)
    key = types.join("|")
    profile = profiles[key] ||= {
      "types" => types.map(&:to_s),
      "normal_count" => 0,
      "fusion_count" => 0
    }
    profile[count_name] += 1
  end

  def self.validate_document(document)
    profiles = document["profiles"]
    raise "coverage profile dataset is empty" if profiles.empty?
    raise "coverage profile dataset exceeds 171 profiles" if profiles.length > 171
    normal_total = profiles.sum { |profile| profile["normal_count"] }
    fusion_total = profiles.sum { |profile| profile["fusion_count"] }
    if normal_total != document["normal_pool_size"]
      raise "normal coverage count #{normal_total} does not match pool size"
    end
    if fusion_total != document["fusion_pool_size"]
      raise "fusion coverage count #{fusion_total} does not match pool size"
    end
  end

  def self.validate_tracker_context(document)
    previous_global = $PokemonGlobal
    begin
      $PokemonGlobal = PokemonGlobalMetadata.new
      $PokemonGlobal.ironmon_mode = false
      if Ironmon.tracker_type_coverage_context
        raise "inactive runs must not expose type coverage context"
      end
      $PokemonGlobal.ironmon_mode = true
      Ironmon::Configuration::POLICY_IDS.each do |policy|
        Ironmon.configuration = Ironmon::Configuration.new(
          Ironmon::Configuration::DEFAULT_WILD_POLICY, policy
        )
        context = Ironmon.tracker_type_coverage_context
        expected = {
          "trainer_policy" => policy.to_s,
          "normal_pool_size" => document["normal_pool_size"],
          "normal_pool_fingerprint" => document["normal_pool_fingerprint"],
          "fusion_pool_schema_version" =>
            document["fusion_pool_schema_version"],
          "fusion_pool_size" => document["fusion_pool_size"],
          "fusion_pool_fingerprint" => document["fusion_pool_fingerprint"]
        }
        if context != expected
          raise "tracker type coverage context does not match generated data"
        end
      end
    ensure
      $PokemonGlobal = previous_global
    end
  end

  def self.write_json(path, document)
    temporary_path = "#{path}.tmp"
    lines = ["{"]
    lines << "  \"schema_version\": #{document["schema_version"]},"
    lines << "  \"game_version\": #{json_string(document["game_version"])},"
    lines << "  \"normal_pool_size\": #{document["normal_pool_size"]},"
    lines << "  \"normal_pool_fingerprint\": #{json_string(document["normal_pool_fingerprint"])},"
    lines << "  \"fusion_pool_schema_version\": #{document["fusion_pool_schema_version"]},"
    lines << "  \"fusion_pool_size\": #{document["fusion_pool_size"]},"
    lines << "  \"fusion_pool_fingerprint\": #{json_string(document["fusion_pool_fingerprint"])},"
    lines << "  \"profiles\": ["
    document["profiles"].each_with_index do |profile, index|
      types = profile["types"].map { |type| json_string(type) }.join(", ")
      suffix = index == document["profiles"].length - 1 ? "" : ","
      lines << "    { \"types\": [#{types}], \"normal_count\": #{profile["normal_count"]}, \"fusion_count\": #{profile["fusion_count"]} }#{suffix}"
    end
    lines << "  ]"
    lines << "}"
    File.binwrite(temporary_path, "#{lines.join("\n")}\n")
    File.delete(path) if File.exist?(path)
    File.rename(temporary_path, path)
  end

  def self.json_string(value)
    escaped = value.to_s.gsub("\\", "\\\\").gsub("\"", "\\\"")
    return "\"#{escaped}\""
  end

  def self.write_audit(path, document)
    File.open(path, "wb") do |file|
      file.write("types,normal_count,fusion_count\n")
      document["profiles"].each do |profile|
        file.write(
          "#{profile["types"].join("|")}," \
          "#{profile["normal_count"]},#{profile["fusion_count"]}\n"
        )
      end
    end
  end
end

output_path = $ironmon_type_coverage_output_path.to_s
audit_path = $ironmon_type_coverage_audit_path.to_s
game_root = $ironmon_type_coverage_game_root.to_s
source_path = $ironmon_type_coverage_source_path.to_s
exit! 0 if output_path.empty? || game_root.empty? || source_path.empty?
begin
  Dir.chdir(game_root)
  File.binwrite("#{output_path}.progress", "exporter loaded\n")
  IronmonScriptLoader.load_directory(
    "Data/Scripts", [/\A(?:997|998|999)/]
  )
  IronmonScriptLoader.load_directory(source_path)
  File.open("#{output_path}.progress", "ab") do |file|
    file.write("game and Ironmon scripts loaded\n")
  end
  GameData.load_all
  $game_temp = Game_Temp.new
  Game.load_custom_sprites_list_cache
  File.open("#{output_path}.progress", "ab") do |file|
    file.write("game data and custom sprite index loaded\n")
  end
  document = IronmonTypeCoverageExporter.run(output_path, audit_path)
  File.binwrite(
    "#{output_path}.summary",
    "profiles=#{document["profiles"].length}\n" \
      "normal_pool_size=#{document["normal_pool_size"]}\n" \
      "fusion_pool_size=#{document["fusion_pool_size"]}\n"
  )
  exit! 0
rescue Exception => error
  backtrace = error.backtrace ? error.backtrace.join("\n") : ""
  File.binwrite(
    "#{output_path}.error",
    "#{error.class}: #{error.message}\n#{backtrace}"
  )
  exit! 1
end
