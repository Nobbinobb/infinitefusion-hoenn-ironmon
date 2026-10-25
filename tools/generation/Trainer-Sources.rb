module IronmonAreaTrainerSources
  DIRECT_CALLS = ["pbTrainerBattle", "pbMoveTutorBattle", "scaledLevelBattle"].freeze

  def self.arguments(text)
    values = []
    start = 0
    depth = 0
    quote = nil
    escaped = false
    text.each_char.with_index do |character, index|
      if quote
        if escaped
          escaped = false
        elsif character == "\\"
          escaped = true
        elsif character == quote
          quote = nil
        end
      elsif ["'", '"'].include?(character)
        quote = character
      elsif ["(", "[", "{"].include?(character)
        depth += 1
      elsif [")", "]", "}"].include?(character)
        depth -= 1
      elsif character == "," && depth == 0
        values << text[start...index].strip
        start = index + 1
      end
    end
    values << text[start..-1].strip if start < text.length
    return values
  end

  def self.calls(text, name)
    result = []
    text.to_enum(:scan, /\b#{Regexp.escape(name)}\s*\(/).each do
      start = Regexp.last_match.end(0)
      depth = 1
      quote = nil
      escaped = false
      index = start
      while index < text.length
        character = text[index]
        if quote
          if escaped
            escaped = false
          elsif character == "\\"
            escaped = true
          elsif character == quote
            quote = nil
          end
        elsif ["'", '"'].include?(character)
          quote = character
        elsif character == "("
          depth += 1
        elsif character == ")"
          depth -= 1
          break if depth == 0
        end
        index += 1
      end
      raise "Unclosed #{name} call in trainer event" if depth != 0
      result << arguments(text[start...index])
    end
    return result
  end

  def self.integer(value, fallback)
    return fallback if !value || !value.match?(/\A\d+\z/)
    return value.to_i
  end

  def self.identity(values)
    type = values[0].to_s.match(/\A:([A-Za-z0-9_]+)\z/)
    name = values[1].to_s.match(/\A["']([^"']+)["']\z/)
    return nil if !type || !name
    return [type[1], name[1]]
  end

  def self.entry(map_id, event_id, type, name, party_id = 0)
    return {
      "entry_id" => "trainer:#{map_id}:#{type}:#{name}",
      "map_id" => map_id, "event_id" => event_id,
      "trainer_type" => type, "trainer_name" => name, "party_id" => party_id
    }
  end

  def self.completion(list, index, map_id, event_id)
    command = list[index]
    return {} if command.code != 111 || command.parameters[0] != 12
    list[(index + 1)..-1].each do |candidate|
      break if candidate.indent <= command.indent
      if candidate.code == 121 && candidate.parameters[2] == 0 &&
         candidate.parameters[0] == candidate.parameters[1]
        return { "completion_switch_id" => candidate.parameters[0] }
      elsif candidate.code == 123 && candidate.parameters[1] == 0
        return {
          "completion_self_switch" => candidate.parameters[0],
          "completion_map_id" => map_id, "completion_event_id" => event_id
        }
      end
    end
    return {}
  end

  def self.helper_source(name)
    return nil if !defined?($ironmon_source_root) || !$ironmon_source_root ||
      !Ironmon.respond_to?(name)
    file, line = Ironmon.method(name).source_location
    manifest = JSON.parse(File.binread(File.join($ironmon_source_root, "load_order.json")))
    source = manifest.find { |item| (item["output"] || item[:output]) == file }
    relative = source && (source["source"] || source[:source])
    return nil if !relative || !relative.start_with?("progression/")
    lines = File.readlines(File.join($ironmon_source_root, relative))
    indent = lines[line - 1][/\A\s*/]
    last = ((line)...lines.length).find { |index| lines[index].match?(/\A#{indent}end\s*\z/) }
    return last ? lines[line..last].join : nil
  end

  def self.extract(script, map_id, event_id, context, done, seen = [])
    result = []
    DIRECT_CALLS.each do |call|
      calls(script, call).each do |values|
        trainer = identity(values)
        raise "Unresolved #{call} identity at #{map_id}:#{event_id}" if !trainer
        event_index, map_index = case call
                                 when "pbTrainerBattle" then [9, 10]
                                 when "pbMoveTutorBattle" then [4, 5]
                                 else [2, 3]
                                 end
        target_map = integer(values[map_index], map_id)
        target_event = integer(values[event_index], event_id)
        party = call == "pbTrainerBattle" ? integer(values[4], 0) : 0
        row = entry(target_map, target_event, *trainer, party)
        if call != "pbTrainerBattle"
          row["battle_kind"] = call == "scaledLevelBattle" ? "scaled" : "move_tutor"
          row["uses_player_level"] = call == "scaledLevelBattle" || values[3] != "false"
        end
        result << row.merge(done)
      end
    end
    calls(script, "pbMultiTrainerBattle").each do |values|
      array = values[0].to_s
      raise "Unresolved multi-trainer event at #{map_id}:#{event_id}" if !array.start_with?("[") || !array.end_with?("]")
      arguments(array[1...-1]).each do |member|
        parts = arguments(member[1...-1])
        trainer = identity(parts)
        raise "Unresolved multi-trainer member at #{map_id}:#{event_id}" if !trainer
        result << entry(map_id, integer(parts[2], event_id), *trainer).merge(
          "source_map_id" => map_id, "source_event_id" => event_id
        ).merge(done)
      end
    end
    if script.match?(/\bwally_battle\b/)
      result << entry(map_id, event_id, "RIVAL2", "Wally").merge(
        "battle_kind" => "wally", "trainer_key" => "wally", "base_party_size" => 2
      ).merge(done)
    end
    if script.match?(/\bhoennRivalBattle\b/)
      stage = context.include?("updateRivalTeamForThirdBattle") ? 3 :
        (context.include?("updateRivalTeamForSecondBattle") ? 2 : 1)
      result << entry(map_id, event_id, "RIVAL1", "Rival").merge(
        "battle_kind" => "rival", "trainer_key" => "rival",
        "base_party_size" => [stage, 3].min, "story_stage" => stage,
        "ironmon_override" => !seen.empty?
      ).merge(done)
    end
    script.scan(/\bIronmon\.([A-Za-z0-9_]+)/).flatten.uniq.each do |name|
      next if seen.include?(name)
      source = helper_source(name)
      next if !source
      helper_done = done
      source.scan(/\$game_switches\[([A-Z0-9_]*RIVAL_DEFEATED)\]\s*=\s*true/) do |match|
        helper_done = done.merge("completion_switch_id" => Ironmon.const_get(match[0]))
      end
      result.concat(extract(source, map_id, event_id, source, helper_done, seen + [name]))
    end
    return result
  end

  def self.for_map(map_id, map)
    rows = []
    map.events.each_value do |event|
      context = IronmonAreaCatalogExporter.event_script(event)
      event.pages.each do |page|
        lists = [page.list]
        visited = {}
        until lists.empty?
          list = lists.shift
          list.each_with_index do |command, index|
            if command.code == 117 && !visited[command.parameters[0]]
              visited[command.parameters[0]] = true
              common = load_data("Data/CommonEvents.rxdata")[command.parameters[0]]
              lists << common.list if common
            end
            script = if command.code == 355
                       lines = [command.parameters[0].to_s]
                       next_index = index + 1
                       while list[next_index] && list[next_index].code == 655
                         lines << list[next_index].parameters[0].to_s
                         next_index += 1
                       end
                       lines.join("\n")
                     elsif command.code == 111 && command.parameters[0] == 12
                       command.parameters[1].to_s
                     end
            next if !script
            rows.concat(extract(script, map_id, event.id, context, completion(list, index, map_id, event.id)))
          end
        end
      end
    end
    return rows
  end

  def self.resolve(maps)
    entries = maps.flat_map { |map| map["trainers"] }
    relocated = entries.any? { |row| row["battle_kind"] == "rival" && row["story_stage"] == 1 && row["ironmon_override"] }
    maps.each do |map|
      map["trainers"] = map["trainers"].reject do |row|
        obsolete_rival = relocated && row["battle_kind"] == "rival" && row["story_stage"] == 1 && !row["ironmon_override"]
        missing_party = row["battle_kind"] != "rival" &&
          ![GameData::Trainer, GameData::TrainerModern].any? do |mode|
            mode.try_get(row["trainer_type"].to_sym, row["trainer_name"], row["party_id"])
          end
        obsolete_rival || missing_party
      end
      grouped = map["trainers"].group_by { |row| row["entry_id"] }
      map["trainers"] = grouped.values.map do |rows|
        row = rows[0]
        row["event_ids"] = rows.flat_map do |candidate|
          [candidate["event_id"], candidate["source_event_id"]].compact
        end.uniq.sort
        row
      end.sort_by { |row| [row["event_id"], row["trainer_type"], row["trainer_name"]] }
    end
  end
end
