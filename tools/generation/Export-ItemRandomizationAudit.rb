module IronmonItemRandomizationAuditExporter
  def self.csv_value(value)
    text = value.to_s
    return text if !text.include?(",") && !text.include?("\"") &&
      !text.include?("\n")
    return "\"#{text.gsub("\"", "\"\"")}\""
  end

  def self.event_script(event)
    strings = []
    event.pages.each do |page|
      page.list.each do |command|
        collect_strings(command.parameters, strings)
      end
    end
    return strings.join("\n")
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

  def self.row(file, values)
    values += [""] * (10 - values.length)
    file.write(values.map { |value| csv_value(value) }.join(","))
    file.write("\n")
  end

  def self.map_rows
    rows = []
    Dir.glob(File.join("Data", "Map[0-9][0-9][0-9].rxdata")).sort.each do |path|
      map_id = File.basename(path)[/\d+/].to_i
      map = load_data(path)
      map.events.each_value do |event|
        script = event_script(event)
        ground_ids = script.scan(/pbItemBall\(\s*:([A-Za-z0-9_]+)/).flatten
        ground_ids.each do |item_id|
          item = GameData::Item.try_get(item_id.to_sym)
          randomizable = item && Ironmon.item_ground_source_randomizable?(item)
          rows << ["ground_slot", "map:#{map_id}|event:#{event.id}", item_id,
                   randomizable, "", "", "", ""]
        end
        gift_ids = script.scan(/pbReceiveItem\(\s*:([A-Za-z0-9_]+)/).flatten
        gift_ids.each do |item_id|
          item = GameData::Item.try_get(item_id.to_sym)
          next if !item || !item.is_TM?
          rows << ["tm_gift_slot", "map:#{map_id}|event:#{event.id}",
                   item_id, true, "", "", "", ""]
        end
        script.scan(/pbPokemonMart\(\s*\[([^\]]*)\]/m).each do |match|
          stock = match[0].scan(/:([A-Za-z0-9_]+)/).flatten.map(&:upcase)
          if stock.empty?
            rows << ["standard_mart", "map:#{map_id}|event:#{event.id}", "",
                     "", "<dynamic>", "<existing balls/repels>", "",
                     "stock is assembled at runtime"]
          else
            filtered = Ironmon.standard_mart_stock(stock.map(&:to_sym)).map(&:to_s)
            rows << ["standard_mart", "map:#{map_id}|event:#{event.id}", "",
                     "", stock.join("|"), filtered.join("|"),
                     filtered.empty?, ""]
          end
        end
      end
    end
    return rows
  end

  def self.validate(ground_pool, tm_pool)
    raise "duplicate ground pool identifiers" if ground_pool.uniq != ground_pool
    raise "duplicate TM pool identifiers" if tm_pool.uniq != tm_pool
    raise "the ground item pool is empty" if ground_pool.empty?
    raise "the TM gift pool is empty" if tm_pool.empty?
    ground_pool.each do |item_id|
      item = GameData::Item.get(item_id)
      raise "key item #{item_id} entered the ground pool" if item.is_key_item?
      if Ironmon::ItemSlotGenerator::PROTECTED_HM_ITEMS.include?(item_id)
        raise "HM #{item_id} entered the ground pool"
      end
      if item.is_machine? && !item.is_TM?
        raise "non-TM machine #{item_id} entered the ground pool"
      end
      raise "banned item #{item_id} entered the ground pool" if
        Ironmon.item_result_banned?(item)
      raise "unsupported item #{item_id} entered the ground pool" if
        Ironmon.item_result_unsupported?(item)
      category = Ironmon.item_result_category(item)
      weight = Ironmon.item_result_weight(item)
      raise "item #{item_id} has no weighted category" if category == :excluded
      raise "item #{item_id} has an invalid weight" if weight < 1
    end
    tm_pool.each do |item_id|
      item = GameData::Item.get(item_id)
      if Ironmon::ItemSlotGenerator::PROTECTED_HM_ITEMS.include?(item_id)
        raise "HM #{item_id} entered the TM gift pool"
      end
      raise "non-TM #{item_id} entered the TM gift pool" if !item.is_TM?
    end
    categorized_bans = []
    GameData::Item.each do |item|
      categorized_bans << item.id if item.is_mail? || item.is_apricorn?
    end
    required_bans = categorized_bans + [:EXPSHARE] +
      Ironmon::ItemSlotGenerator::HM_TOOL_RESULT_BANS
    missing_bans = required_bans.uniq - Ironmon.item_result_bans
    if !missing_bans.empty?
      raise "current item categories are missing result bans: #{missing_bans.join(', ')}"
    end
  end

  def self.ground_exclusion_reason(item, rules)
    return "versioned result ban" if Ironmon.item_result_banned?(item, rules)
    return "key item or field tool" if item.is_key_item?
    if Ironmon::ItemSlotGenerator::PROTECTED_HM_ITEMS.include?(item.id)
      return "protected HM identifier"
    end
    return "unsupported registration" if Ironmon.item_result_unsupported?(item)
    return "non-TM machine" if item.is_machine? && !item.is_TM?
    return "other policy exclusion"
  end

  def self.run(path)
    rules = Ironmon::ItemSlotGenerator::POOL_RULES_VERSION
    ground_pool = Ironmon.item_ground_pool(rules)
    tm_pool = Ironmon.item_tm_pool(rules)
    validate(ground_pool, tm_pool)
    rows = map_rows
    Ironmon::ItemSlotGenerator::SPECIAL_GROUND_SLOTS.each_value do |slot|
      rows << ["special_ground_slot", slot[0], slot[1], true, "", "", "",
               "Ironmon-owned scripted reward"]
    end
    randomizable_slots = rows.select do |row_data|
      ["ground_slot", "special_ground_slot"].include?(row_data[0]) &&
        row_data[3] == true
    end.map { |row_data| row_data[1] }.uniq.length
    category_summary = Ironmon.item_category_summary(rules)
    total_weight = category_summary.values.sum do |category|
      category[:total_weight]
    end
    File.open(path, "wb") do |file|
      file.write("record_type,identity,item_id,randomizable,stock_before,stock_after,empty_after,detail,ground_category,ground_weight\n")
      row(file, ["summary", "schema", "", "", "", "", "",
                 Ironmon::ItemSlotGenerator::SCHEMA_VERSION])
      row(file, ["summary", "rules", "", "", "", "", "", rules])
      row(file, ["summary", "ground_pool_size", "", "", "", "", "",
                 ground_pool.length])
      row(file, ["summary", "ground_pool_fingerprint", "", "", "", "", "",
                 Ironmon.item_ground_pool_fingerprint(rules)])
      row(file, ["summary", "tm_pool_size", "", "", "", "", "",
                 tm_pool.length])
      row(file, ["summary", "tm_pool_fingerprint", "", "", "", "", "",
                 Ironmon.item_tm_pool_fingerprint(rules)])
      row(file, ["summary", "result_ban_fingerprint", "", "", "", "", "",
                 Ironmon.item_result_ban_fingerprint(rules)])
      row(file, ["summary", "ground_total_weight", "", "", "", "", "",
                 total_weight])
      category_summary.sort_by { |category, _values| category.to_s }.
        each do |category, values|
          expected = randomizable_slots * values[:total_weight].to_f /
            total_weight
          detail = "items=#{values[:item_count]}; " +
            "total_tickets=#{values[:total_weight]}; " +
            "expected_slots=#{format('%.2f', expected)}"
          row(file, ["category_summary", category, "", "", "", "", "",
                     detail, category,
                     Ironmon::ItemSlotGenerator::ITEM_CATEGORY_WEIGHTS[category]])
        end
      ground_pool.each do |item_id|
        item = GameData::Item.get(item_id)
        row(file, ["ground_pool", "", item_id, "", "", "", "", "",
                   Ironmon.item_result_category(item, rules),
                   Ironmon.item_result_weight(item, rules)])
      end
      tm_pool.each do |item_id|
        row(file, ["tm_pool", "", item_id, "", "", "", "",
                   "TM gifts select uniformly", :tm,
                   Ironmon.item_result_weight(GameData::Item.get(item_id), rules)])
      end
      excluded_items = []
      GameData::Item.each do |item|
        next if Ironmon.item_ground_pool_eligible?(item, rules)
        excluded_items << item
      end
      excluded_items.sort_by { |item| item.id.to_s }.each do |item|
        row(file, ["structural_exclusion", "", item.id, "", "", "", "",
                   ground_exclusion_reason(item, rules), :excluded, 0])
      end
      Ironmon.item_result_bans(rules).each do |item_id|
        row(file, ["result_ban", rules, item_id, "", "", "", "", "",
                   :banned, 0])
      end
      rows.each { |values| row(file, values) }
    end
    return {
      :ground_pool => ground_pool.length,
      :ground_weight => total_weight,
      :tm_pool => tm_pool.length,
      :ground_slots => rows.select { |row_data| row_data[0] == "ground_slot" }.
        map { |row_data| row_data[1] }.uniq.length +
        rows.count { |row_data| row_data[0] == "special_ground_slot" },
      :tm_gifts => rows.count { |row_data| row_data[0] == "tm_gift_slot" },
      :marts => rows.count { |row_data| row_data[0] == "standard_mart" }
    }
  end
end

output_path = $ironmon_item_audit_output_path.to_s
game_root = $ironmon_item_audit_game_root.to_s
hashing_path = $ironmon_deterministic_hashing_source_path.to_s
generator_path = $ironmon_item_generator_source_path.to_s
hook_path = $ironmon_item_hook_source_path.to_s
exit! 0 if output_path.empty? || game_root.empty? || hashing_path.empty? ||
  generator_path.empty? || hook_path.empty?
begin
  Dir.chdir(game_root)
  File.binwrite("#{output_path}.progress", "exporter loaded\n")
  IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:997|998|999)/])
  GameData.load_all
  Object.const_set(:Ironmon, Module.new) if !defined?(Ironmon)
  hashing_source = File.open(hashing_path, "rb") { |file| file.read }
  eval(hashing_source, TOPLEVEL_BINDING, hashing_path)
  generator_source = File.open(generator_path, "rb") { |file| file.read }
  eval(generator_source, TOPLEVEL_BINDING, generator_path)
  hook_source = File.open(hook_path, "rb") { |file| file.read }
  eval(hook_source, TOPLEVEL_BINDING, hook_path)
  summary = IronmonItemRandomizationAuditExporter.run(output_path)
  File.open("#{output_path}.summary", "wb") do |file|
    summary.each { |key, value| file.write("#{key}=#{value}\n") }
  end
rescue Exception => e
  File.open("#{output_path}.error", "wb") do |file|
    file.write("#{e.class}: #{e.message}\n")
    file.write(e.backtrace.join("\n")) if e.backtrace
  end
  exit! 1
end
