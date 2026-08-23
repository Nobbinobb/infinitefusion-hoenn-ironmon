module IronmonObtainabilityFoundationAuditExporter
  EXCLUDED_MAP_NAME = /\A(?:EVENT_TEMPLATES|QUEST_TEMPLATES|testing)\z|\Aquest_/i
  ACQUISITION_ENTRY_POINTS = {
    "pbAddPokemon" => "permanent_candidate",
    "pbAddPokemonSilent" => "permanent_candidate",
    "pbAddToParty" => "permanent_candidate",
    "pbAddToPartySilent" => "permanent_candidate",
    "pbAddForeignPokemon" => "permanent_candidate",
    "pbGenerateEgg" => "egg_candidate",
    "pbAddEgg" => "egg_candidate",
    "pbGenEgg" => "egg_candidate",
    "pbStartTrade" => "trade_candidate",
    "npcTrade" => "trade_candidate",
    "pbWildBattle" => "wild_encounter",
    "pbWildBattleSpecific" => "wild_encounter",
    "pbDoubleWildBattle" => "wild_encounter",
    "pbTripleWildBattle" => "wild_encounter"
  }.freeze
  RESOURCE_ENTRY_POINTS = {
    "pbItemBall" => "randomized_ground_or_explicit_bypass",
    "pbReceiveItem" => "authored_gift_or_randomized_tm_gift",
    "pbPokemonMart" => "filtered_standard_mart",
    "pbStoreItem" => "direct_bag_write"
  }.freeze
  CONSUMED_ITEM_METHODS = [
    :HappinessHoldItem, :HoldItem, :HoldItemMale, :HoldItemFemale,
    :DayHoldItem, :NightHoldItem, :HoldItemHappiness,
    :Item, :ItemMale, :ItemFemale, :ItemDay, :ItemNight, :ItemHappiness
  ].freeze
  REQUIRED_HOOKS = [
    ["Object", :method, :pbWildBattle,
     "024_Release_073_Gameplay.rb"],
    ["Object", :method, :pbWildBattleSpecific,
     "004_Encounter_Hooks_2_Wild_Battles.rb"],
    ["Object", :method, :pbDoubleWildBattle,
     "022_Area_Progress_Runtime_Integration.rb"],
    ["Object", :method, :pbTripleWildBattle,
     "022_Area_Progress_Runtime_Integration.rb"],
    ["Object", :method, :getRandomizedTo,
     "004_Encounter_Hooks_3_Gifts_And_Starters.rb"],
    ["Object", :method, :tryRandomizeGiftPokemon,
     "004_Encounter_Hooks_3_Gifts_And_Starters.rb"],
    ["Object", :method, :pbReceiveMysteryGift,
     "004_Encounter_Hooks_3_Gifts_And_Starters.rb"],
    ["Object", :method, :obtainRandomizedStarter,
     "004_Encounter_Hooks_3_Gifts_And_Starters.rb"],
    ["Object", :method, :promptCaughtPokemonAction,
     "010_Pivot_Acquisition_1_Runtime_Integration.rb"],
    ["Object", :method, :pbStorePokemon,
     "010_Pivot_Acquisition_1_Runtime_Integration.rb"],
    ["Object", :method, :pbAddPokemon,
     "010_Pivot_Acquisition_1_Runtime_Integration.rb"],
    ["Object", :method, :pbAddPokemonSilent,
     "010_Pivot_Acquisition_1_Runtime_Integration.rb"],
    ["Object", :method, :pbAddToParty,
     "010_Pivot_Acquisition_1_Runtime_Integration.rb"],
    ["Object", :method, :pbAddToPartySilent,
     "010_Pivot_Acquisition_1_Runtime_Integration.rb"],
    ["Object", :method, :pbAddRentalPokemon,
     "010_Pivot_Acquisition_1_Runtime_Integration.rb"],
    ["Object", :method, :pbGenerateEgg,
     "010_Pivot_Acquisition_1_Runtime_Integration.rb"],
    ["Object", :method, :pbAddEgg,
     "010_Pivot_Acquisition_1_Runtime_Integration.rb"],
    ["Object", :method, :pbGenEgg,
     "010_Pivot_Acquisition_1_Runtime_Integration.rb"],
    ["Object", :method, :pbStartTrade,
     "010_Pivot_Acquisition_1_Runtime_Integration.rb"],
    ["PokemonStorage", :instance_method, :pbStoreCaught,
     "010_Pivot_Acquisition_2_Storage_Integration.rb"],
    ["PokeBattle_RealBattlePeer", :instance_method, :pbStorePokemon,
     "010_Pivot_Acquisition_2_Storage_Integration.rb"],
    ["Object", :method, :pbHatch, "015_Pivot_Integration.rb"],
    ["PokemonEvolutionScene", :method, :pbDuplicatePokemon,
     "015_Pivot_Integration.rb"]
  ].freeze

  def self.csv_value(value)
    text = value.to_s
    return text if !text.include?(",") && !text.include?("\"") &&
      !text.include?("\n")
    return "\"#{text.gsub("\"", "\"\"")}\""
  end

  def self.write_row(file, values)
    values += [""] * (10 - values.length)
    file.write(values.map { |value| csv_value(value) }.join(","))
    file.write("\n")
  end

  def self.collect_strings(value, result)
    if value.is_a?(String)
      result << value
    elsif value.is_a?(Array)
      value.each { |entry| collect_strings(entry, result) }
    elsif value.is_a?(Hash)
      value.each do |key, entry|
        collect_strings(key, result)
        collect_strings(entry, result)
      end
    end
  end

  def self.command_script(commands)
    result = []
    commands.each { |command| collect_strings(command.parameters, result) }
    return result.join("\n")
  end

  def self.call_rows(context, script, entry_points = ACQUISITION_ENTRY_POINTS,
                     record_type = "acquisition_call")
    rows = []
    entry_points.each do |entry_point, classification|
      expression = /\b#{Regexp.escape(entry_point)}\s*\(([^\n;]*)/
      script.scan(expression).each do |match|
        arguments = match[0].to_s.strip.gsub(/\s+/, " ")
        rows << [record_type, context, entry_point, classification,
                 arguments, "", "", "", "", ""]
      end
    end
    return rows
  end

  def self.event_call_rows
    acquisition_rows = []
    resource_rows = []
    audited_map_paths.each do |path|
      map_id = File.basename(path)[/\d+/].to_i
      map = load_data(path)
      map.events.each_value do |event|
        event.pages.each_with_index do |page, page_index|
          context = "map:#{map_id}|event:#{event.id}|page:#{page_index + 1}"
          script = command_script(page.list)
          acquisition_rows.concat(call_rows(context, script))
          resource_rows.concat(call_rows(
            context, script, RESOURCE_ENTRY_POINTS, "resource_call"
          ))
        end
      end
    end
    common_events = load_data("Data/CommonEvents.rxdata")
    common_events.compact.each do |event|
      context = "common_event:#{event.id}"
      script = command_script(event.list)
      acquisition_rows.concat(call_rows(context, script))
      resource_rows.concat(call_rows(
        context, script, RESOURCE_ENTRY_POINTS, "resource_call"
      ))
    end
    return acquisition_rows, resource_rows
  end

  def self.audited_map_paths
    map_infos = load_data("Data/MapInfos.rxdata")
    return Dir.glob(File.join("Data", "Map[0-9][0-9][0-9].rxdata")).sort.
      select do |path|
        map_id = File.basename(path)[/\d+/].to_i
        info = map_infos[map_id]
        info && !info.name.to_s.match?(EXCLUDED_MAP_NAME)
      end
  end

  def self.source_call_rows
    acquisition_rows = []
    resource_rows = []
    root = File.join("Data", "Scripts")
    Dir.glob(File.join(root, "**", "*.rb")).sort.each do |path|
      relative = path.sub(/\A#{Regexp.escape(root)}[\\\/]/, "")
      next if relative.match?(/\A(?:020_Debug|052_Tests|997_|998_|999_)/)
      next if relative.match?(/(?:\A|[\\\/])(?:Debug|DevTools|Tests)(?:[\\\/]|\z)/i)
      File.readlines(path).each_with_index do |line, index|
        next if line.match?(/^\s*#/)
        ACQUISITION_ENTRY_POINTS.each do |entry_point, classification|
          next if line.match?(/^\s*def\s+#{Regexp.escape(entry_point)}\b/)
          next if !line.match?(/\b#{Regexp.escape(entry_point)}\s*\(/)
          acquisition_rows << ["acquisition_source_call", "#{relative}:#{index + 1}",
                   entry_point, classification, line.strip.gsub(/\s+/, " "),
                   "", "", "", "", ""]
        end
        RESOURCE_ENTRY_POINTS.each do |entry_point, classification|
          next if line.match?(/^\s*def\s+#{Regexp.escape(entry_point)}\b/)
          next if !line.match?(/\b#{Regexp.escape(entry_point)}\s*\(/)
          resource_rows << ["resource_source_call",
                            "#{relative}:#{index + 1}", entry_point,
                            classification, line.strip.gsub(/\s+/, " "),
                            "", "", "", "", ""]
        end
      end
    end
    return acquisition_rows, resource_rows
  end

  def self.runtime_hook(owner_name, lookup, method_name)
    owner = Object.const_get(owner_name)
    return owner.send(lookup, method_name)
  end

  def self.validate_hooks
    REQUIRED_HOOKS.each do |owner_name, lookup, method_name, expected_path|
      method = runtime_hook(owner_name, lookup, method_name)
      location = method.source_location
      label = "#{owner_name}.#{method_name}"
      raise "#{label} has no runtime source location" if !location
      normalized = location[0].to_s.tr("\\", "/")
      if File.basename(normalized) != expected_path
        raise "#{label} is not owned by #{expected_path}: #{normalized}"
      end
    end
  end

  def self.prototype_adapter_rows(event_rows, source_rows,
                                  event_resource_rows, source_resource_rows)
    service = Ironmon::TrackerObtainabilityService
    scripted = service::SCRIPTED_ACQUISITION_METHODS
    deferred = service::DEFERRED_ACQUISITION_METHODS
    acquisition_inventory = (event_rows + source_rows).map { |row| row[2] }.uniq
    missing = acquisition_inventory - scripted - deferred
    if !missing.empty?
      raise "obtainability acquisition adapters are missing: #{missing.sort.join(', ')}"
    end
    event_missing = event_rows.map { |row| row[2] }.uniq - scripted
    if !event_missing.empty?
      raise "event acquisition parser coverage is missing: #{event_missing.sort.join(', ')}"
    end
    resource_inventory = (event_resource_rows + source_resource_rows).map do |row|
      row[2]
    end.uniq
    resource_missing = resource_inventory - service::SCRIPTED_RESOURCE_METHODS
    if !resource_missing.empty?
      raise "obtainability resource adapters are missing: #{resource_missing.sort.join(', ')}"
    end
    evolution_missing = CONSUMED_ITEM_METHODS -
      service::ITEM_EVOLUTION_METHODS
    if !evolution_missing.empty?
      raise "obtainability item consumers are missing: #{evolution_missing.sort.join(', ')}"
    end
    rows = scripted.sort.map do |method_name|
      ["prototype_adapter", method_name, "scripted_acquisition_candidate",
       "conditions remain path-unproven"]
    end
    rows.concat(deferred.sort.map do |method_name|
      ["prototype_adapter", method_name, "deferred_acquisition_source",
       "result remains unknown rather than impossible"]
    end)
    rows.concat(service::SCRIPTED_RESOURCE_METHODS.sort.map do |method_name|
      ["prototype_adapter", method_name, "resource_inventory",
       "unresolved categories prevent a false impossible result"]
    end)
    return rows
  end

  def self.encounter_rows
    rows = []
    [GameData::Encounter, GameData::EncounterModern].each do |mode|
      next if !mode.respond_to?(:each)
      mode.each do |data|
        data.types.each do |encounter_type, entries|
          entries.each_with_index do |entry, slot|
            rows << ["encounter_slot", mode.name, data.map, data.version,
                     encounter_type, slot + 1, entry[1], entry[0],
                     entry[2], entry[3] || entry[2]]
          end
        end
      end
    end
    return rows
  end

  def self.starter_rows
    return [1, 4, 7].each_with_index.map do |source_id, slot|
      ["starter_slot", "starter:#{slot}", source_id, :wild,
       "[:starter, #{slot}]", "choice-exclusive; fused starters cannot unfuse"]
    end
  end

  def self.randomizable_item_slots
    slots = []
    catalog_path = $ironmon_obtainability_audit_area_catalog_path.to_s
    document = File.open(catalog_path, "rb") { |file| Marshal.load(file) }
    Ironmon.tracker_validate_area_catalog(document)
    document["areas"].each do |area|
      area["items"].each do |entry|
        randomizable = entry["authored_item_ids"].any? do |item_id|
          item = GameData::Item.try_get(item_id.to_sym)
          item && Ironmon.item_ground_source_randomizable?(item)
        end
        next if !randomizable
        slots << "map:#{entry["map_id"]}|event:#{entry["event_id"]}"
      end
    end
    Ironmon::ItemSlotGenerator::SPECIAL_GROUND_SLOTS.each_value do |slot|
      slots << slot[0]
    end
    return slots.uniq.sort
  end

  def self.authored_item_gifts
    result = Hash.new(0)
    rows = []
    event_call_sources = []
    audited_map_paths.each do |path|
      map_id = File.basename(path)[/\d+/].to_i
      map = load_data(path)
      map.events.each_value do |event|
        script = event.pages.map { |page| command_script(page.list) }.join("\n")
        event_call_sources << ["map:#{map_id}|event:#{event.id}", script]
      end
    end
    common_events = load_data("Data/CommonEvents.rxdata")
    common_events.compact.each do |event|
      event_call_sources << ["common_event:#{event.id}", command_script(event.list)]
    end
    event_call_sources.each do |context, script|
      script.scan(/pbReceiveItem\(\s*:([A-Za-z0-9_]+)/).flatten.each do |item_id|
        item = GameData::Item.try_get(item_id.to_sym)
        next if !item || item.is_TM?
        result[item.id] += 1
        rows << ["authored_item_gift", context, item.id,
                 :candidate_not_path_proven, item.name]
      end
    end
    return result, rows
  end

  def self.evolution_resource_rows(catalog, authored_gifts)
    rows = []
    used_methods = {}
    required_items = Hash.new do |hash, item_id|
      hash[item_id] = { :branches => 0, :methods => {} }
    end
    catalog.branch_catalog.each do |branch|
      branch[:effective_methods].each do |method|
        method_data = GameData::Evolution.get(method[:method])
        next if method_data.parameter != :Item
        used_methods[method[:method]] = true
        if !CONSUMED_ITEM_METHODS.include?(method[:method])
          raise "unaudited item evolution method #{method[:method]}"
        end
        item = GameData::Item.try_get(method[:parameter])
        raise "invalid evolution item #{method[:parameter]}" if !item
        required = required_items[item.id]
        required[:branches] += 1
        required[:methods][method[:method]] = true
        rows << ["evolution_item_requirement", branch[:source],
                 branch[:destination], method[:method], item.id, true,
                 method[:converted], Ironmon.item_ground_pool_eligible?(item),
                 authored_gifts[item.id], item.name]
      end
    end
    required_items.sort_by { |item_id, _data| item_id.to_s }.each do |item_id, data|
      item = GameData::Item.get(item_id)
      rows << ["required_item_summary", item_id, item.name,
               data[:branches], data[:methods].keys.sort.join("|"), true,
               "", Ironmon.item_ground_pool_eligible?(item),
               authored_gifts[item_id], Ironmon.item_result_category(item)]
    end
    rows << ["evolution_item_requirement", "duplicate_branch", "", :Shedinja,
             :POKEBALL, true, false, true, authored_gifts[:POKEBALL],
             "requires and consumes one Poke Ball"]
    return rows, required_items, used_methods
  end

  def self.write_audit(path, rows, summary)
    File.open(path, "wb") do |file|
      file.write("record_type,identity,value_1,value_2,value_3,value_4,value_5,value_6,value_7,detail\n")
      summary.sort_by { |key, _value| key.to_s }.each do |key, value|
        write_row(file, ["summary", key, value])
      end
      rows.each { |row| write_row(file, row) }
    end
  end

  def self.run(path)
    validate_hooks
    catalog = Ironmon.evolution_catalog
    catalog.validate
    event_rows, event_resource_rows = event_call_rows
    source_rows, source_resource_rows = source_call_rows
    adapter_rows = prototype_adapter_rows(
      event_rows, source_rows, event_resource_rows, source_resource_rows
    )
    encounters = encounter_rows
    starters = starter_rows
    item_slots = randomizable_item_slots
    authored_gifts, authored_gift_rows = authored_item_gifts
    resource_rows, required_items, used_methods = evolution_resource_rows(
      catalog, authored_gifts
    )
    required_resource_ids = required_items.keys + [:POKEBALL]
    required_gift_rows = authored_gift_rows.select do |row|
      required_resource_ids.include?(row[2])
    end
    hook_rows = REQUIRED_HOOKS.map do |owner_name, lookup, method_name, _path|
      location = runtime_hook(owner_name, lookup, method_name).source_location
      ["runtime_hook", "#{owner_name}.#{method_name}", lookup,
       location[0], location[1]]
    end
    item_slot_rows = item_slots.map do |slot_id|
      ["randomizable_item_slot", slot_id]
    end
    rows = hook_rows + adapter_rows + event_rows + source_rows + event_resource_rows +
      source_resource_rows + starters + encounters + item_slot_rows +
      required_gift_rows + resource_rows
    summary = {
      :schema_version => 1,
      :runtime_hooks => hook_rows.length,
      :prototype_adapters => adapter_rows.length,
      :event_acquisition_calls => event_rows.length,
      :source_acquisition_calls => source_rows.length,
      :event_resource_calls => event_resource_rows.length,
      :source_resource_calls => source_resource_rows.length,
      :starter_slots => starters.length,
      :encounter_slots => encounters.length,
      :randomizable_item_slots => item_slots.length,
      :evolution_item_methods => used_methods.length,
      :required_evolution_items => required_items.length,
      :evolution_item_branches => resource_rows.count do |row|
        row[0] == "evolution_item_requirement" && row[3] != :Shedinja
      end,
      :authored_required_item_gift_calls => required_gift_rows.length
    }
    write_audit(path, rows, summary)
    return summary
  end
end

output_path = $ironmon_obtainability_audit_output_path.to_s
game_root = $ironmon_obtainability_audit_game_root.to_s
source_root = $ironmon_obtainability_audit_source_root.to_s
manifest_path = $ironmon_obtainability_audit_manifest_path.to_s
area_catalog_path = $ironmon_obtainability_audit_area_catalog_path.to_s
exit! 0 if output_path.empty? || game_root.empty? || source_root.empty? ||
  manifest_path.empty? || area_catalog_path.empty?
begin
  Dir.chdir(game_root)
  File.binwrite("#{output_path}.progress", "exporter loaded\n")
  IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:997|998|999)/])
  GameData.load_all
  $game_temp = Game_Temp.new
  Game.load_sprites_list_caches
  IronmonScriptLoader.load_manifest(source_root, manifest_path)
  summary = IronmonObtainabilityFoundationAuditExporter.run(output_path)
  File.open("#{output_path}.summary", "wb") do |file|
    summary.each { |key, value| file.write("#{key}=#{value}\n") }
  end
  exit! 0
rescue Exception => error
  backtrace = error.backtrace ? error.backtrace.join("\n") : ""
  File.binwrite(
    "#{output_path}.error",
    "#{error.class}: #{error.message}\n#{backtrace}"
  )
  exit! 1
end
