module IronmonAreaCatalogScriptLoader
  def self.load_directory(path, root = false)
    entries = Dir.entries(path) - [".", "..", ".git", ".idea", ".gitignore"]
    files, folders = entries.partition do |entry|
      !File.directory?(File.join(path, entry))
    end
    files.sort.each do |file_name|
      path_name = File.join(path, file_name)
      code = File.open(path_name, "rb") { |file| file.read }
      eval(code, TOPLEVEL_BINDING, file_name)
    end
    folders.sort.each do |folder_name|
      next if root && excluded_root_folder?(folder_name)
      load_directory(File.join(path, folder_name))
    end
  end

  def self.excluded_root_folder?(folder_name)
    return true if folder_name.match?(/\A999/)
    return false if $ironmon_area_catalog_validate_installed_scripts
    return folder_name.match?(/\A(?:997|998)/)
  end
end

module IronmonAreaCatalogExporter
  EXCLUDED_MAP_NAME = /\A(?:EVENT_TEMPLATES|QUEST_TEMPLATES|testing)\z|\Aquest_/i

  def self.run(output_path)
    map_infos = load_data("Data/MapInfos.rxdata")
    encounter_map_ids = encounter_map_ids()
    physical_maps = []
    Dir.glob(File.join("Data", "Map[0-9][0-9][0-9].rxdata")).sort.each do |path|
      map_id = File.basename(path)[/\d+/].to_i
      info = map_infos[map_id]
      next if !info
      name = info.name.to_s
      next if name.match?(EXCLUDED_MAP_NAME)
      map = load_data(path)
      trainers = trainer_entries(map_id, map)
      items = item_entries(map_id, map)
      next if trainers.empty? && items.empty? && !encounter_map_ids[map_id]
      physical_maps << {
        "map_id" => map_id,
        "name" => name,
        "trainers" => trainers,
        "items" => items
      }
    end
    areas = physical_maps.group_by { |map| map["name"] }.map do |name, maps|
      maps.sort_by! { |map| map["map_id"] }
      map_ids = maps.map { |map| map["map_id"] }
      {
        "area_id" => "area:#{map_ids.join(',')}",
        "name" => name,
        "map_ids" => map_ids,
        "trainers" => maps.flat_map { |map| map["trainers"] },
        "items" => maps.flat_map { |map| map["items"] }
      }
    end
    areas.sort_by! { |area| area["map_ids"].first }
    document = { "schema_version" => 1, "areas" => areas }
    temporary_path = "#{output_path}.tmp"
    File.open(temporary_path, "wb") { |file| Marshal.dump(document, file) }
    File.rename(temporary_path, output_path)
    return document
  end

  def self.csv_value(value)
    text = value.to_s
    return text if !text.include?(",") && !text.include?("\"") &&
      !text.include?("\n")
    return "\"#{text.gsub("\"", "\"\"")}\""
  end

  def self.write_audit(path, document)
    File.open(path, "wb") do |file|
      file.write("record_type,area_id,area_name,map_ids,entry_id,map_id,event_id,x,y,hidden,kind,authored_ids\n")
      document["areas"].each do |area|
        common = [area["area_id"], area["name"], area["map_ids"].join("|")]
        file.write((["area"] + common + ["", "", "", "", "", "", "", ""]).map { |value| csv_value(value) }.join(","))
        file.write("\n")
        area["trainers"].each do |trainer|
          values = ["trainer"] + common + [
            trainer["entry_id"], trainer["map_id"], trainer["event_id"],
            "", "", "", "", "#{trainer["trainer_type"]}:#{trainer["trainer_name"]}:#{trainer["party_id"]}"
          ]
          file.write(values.map { |value| csv_value(value) }.join(","))
          file.write("\n")
        end
        area["items"].each do |item|
          values = ["item"] + common + [
            item["entry_id"], item["map_id"], item["event_id"],
            item["x"], item["y"], item["hidden"], item["kind"],
            item["authored_item_ids"].join("|")
          ]
          file.write(values.map { |value| csv_value(value) }.join(","))
          file.write("\n")
        end
      end
    end
  end

  def self.encounter_map_ids
    result = {}
    [GameData::Encounter, GameData::EncounterModern].each do |mode|
      next if !mode.respond_to?(:each)
      mode.each { |data| result[data.map.to_i] = true }
    end
    return result
  end

  def self.event_script(event)
    scripts = []
    event.pages.each do |page|
      page.list.each do |command|
        collect_parameter_strings(command.parameters, scripts)
      end
    end
    return scripts.join("\n")
  end

  def self.collect_parameter_strings(value, result)
    if value.is_a?(String)
      result << value
    elsif value.is_a?(Array)
      value.each { |entry| collect_parameter_strings(entry, result) }
    elsif value.is_a?(Hash)
      value.each do |key, entry|
        collect_parameter_strings(key, result)
        collect_parameter_strings(entry, result)
      end
    end
  end

  def self.trainer_entries(map_id, map)
    result = []
    map.events.each_value do |event|
      script = event_script(event)
      match = script.match(
        /pbTrainerBattle\(\s*:([A-Za-z0-9_]+)\s*,\s*["']([^"']+)["']([^;]*)/
      )
      next if !match
      party_match = match[3].match(/,\s*(?:true|false)\s*,\s*(\d+)\s*,/)
      party_id = party_match ? party_match[1].to_i : 0
      result << {
        "entry_id" => "trainer:#{map_id}:#{match[1]}:#{match[2]}",
        "map_id" => map_id,
        "event_id" => event.id.to_i,
        "trainer_type" => match[1],
        "trainer_name" => match[2],
        "party_id" => party_id
      }
    end
    return result.sort_by do |entry|
      [entry["event_id"], entry["trainer_type"], entry["trainer_name"]]
    end
  end

  def self.item_entries(map_id, map)
    result = []
    map.events.each_value do |event|
      script = event_script(event)
      name = event.name.to_s
      item_ball = script.include?("pbItemBall")
      hidden_named = name.match?(/hiddenitem/i)
      special_named = name.match?(/\AItem Present\z/i) ||
        name.match?(/\AMeteorite\z/i)
      next if !item_ball && !hidden_named && !special_named
      item_ids = script.scan(/pbItemBall\(\s*:([A-Za-z0-9_]+)/).flatten
      if item_ids.empty? && special_named
        symbols = script.scan(/:([A-Za-z][A-Za-z0-9_]*)/).flatten
        item_ids = symbols.select do |identifier|
          GameData::Item.try_get(identifier.to_sym)
        end
      end
      item_ids.map!(&:upcase)
      item_ids.uniq!
      visible = event.pages.any? do |page|
        graphic = page.graphic
        graphic && (!graphic.character_name.to_s.empty? || graphic.tile_id.to_i > 0)
      end
      hidden = hidden_named || !visible
      kind = if hidden
               "hidden_item"
             elsif name.match?(/\AItem Present\z/i)
               "special_present"
             elsif name.match?(/\AMeteorite\z/i)
               "special_tile_pickup"
             else
               "standard_ground_item"
             end
      result << {
        "entry_id" => "item:#{map_id}:#{event.id}",
        "map_id" => map_id,
        "event_id" => event.id.to_i,
        "x" => event.x.to_i,
        "y" => event.y.to_i,
        "kind" => kind,
        "hidden" => hidden,
        "itemfinder" => hidden && name.match?(/\AItem/i),
        "authored_item_ids" => item_ids.sort
      }
    end
    return result.sort_by { |entry| entry["event_id"] }
  end
end

area_catalog_output = $ironmon_area_catalog_output_path.to_s
area_catalog_game_root = $ironmon_area_catalog_game_root.to_s
area_catalog_audit_path = $ironmon_area_catalog_audit_path.to_s
exit! 0 if area_catalog_output.empty? || area_catalog_game_root.empty?
begin
  Dir.chdir(area_catalog_game_root)
  File.binwrite("#{area_catalog_output}.progress", "exporter loaded\n")
  IronmonAreaCatalogScriptLoader.load_directory("Data/Scripts", true)
  File.open("#{area_catalog_output}.progress", "ab") do |file|
    file.write("game scripts loaded\n")
  end
  GameData.load_all
  File.open("#{area_catalog_output}.progress", "ab") do |file|
    file.write("game data loaded\n")
  end
  area_catalog_document = IronmonAreaCatalogExporter.run(area_catalog_output)
  if !area_catalog_audit_path.empty?
    IronmonAreaCatalogExporter.write_audit(
      area_catalog_audit_path, area_catalog_document
    )
  end
  if !$ironmon_area_catalog_runtime_test_source.to_s.empty?
    eval(
      $ironmon_area_catalog_runtime_test_source,
      TOPLEVEL_BINDING,
      "Test-Area-Progress-Runtime.rb"
    )
  end
  trainer_count = area_catalog_document["areas"].sum do |area|
    area["trainers"].length
  end
  item_count = area_catalog_document["areas"].sum do |area|
    area["items"].length
  end
  hidden_item_count = area_catalog_document["areas"].sum do |area|
    area["items"].count { |item| item["hidden"] }
  end
  File.binwrite(
    "#{area_catalog_output}.summary",
    "areas=#{area_catalog_document["areas"].length}\n" \
      "trainers=#{trainer_count}\nitems=#{item_count}\n" \
      "hidden_items=#{hidden_item_count}\n"
  )
  exit! 0
rescue Exception => error
  backtrace = error.backtrace ? error.backtrace.join("\n") : ""
  File.binwrite(
    "#{area_catalog_output}.error",
    "#{error.class}: #{error.message}\n#{backtrace}"
  )
  exit! 1
end
