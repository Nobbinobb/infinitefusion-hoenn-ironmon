module IronmonEncounterTriggers
  LAND_TERRAINS = {
    :Grass_alt1 => "Land1", :Grass_alt2 => "Land2",
    :Grass_alt3 => "Land3", :TallGrass => "TallGrass"
  }.freeze

  def self.for_map(map, tileset)
    methods = {}
    map.width.times do |x|
      map.height.times do |y|
        [false, true].each do |bridge|
          terrain = terrain_at(map, tileset, x, y, bridge)
          if terrain.can_surf && !terrain.waterfall
            methods["Water"] = true
          end
          methods["Fishing"] = true if terrain.can_fish
          next if !walkable?(map, tileset, x, y, bridge) || terrain.ice
          methods["Cave"] = true
          if terrain.land_wild_encounters
            methods[LAND_TERRAINS.fetch(terrain.id, "Land")] = true
          end
        end
      end
    end
    scripts = event_scripts(map)
    if scripts.match?(/\bpbRockSmashRandomEncounter\b/) ||
       map.events.values.any? { |event| event.name.match?(/smashrock/i) }
      methods["RockSmash"] = true
    end
    if scripts.match?(/\bpbHeadbutt(?:Effect)?\b/) ||
       map.events.values.any? { |event| event.name.match?(/headbutttree/i) }
      methods["HeadbuttLow"] = true
      methods["HeadbuttHigh"] = true
    end
    scripts.scan(/\b(?:pbEncounter|choose_wild_pokemon|generateWildEncounter)\s*\(\s*:([A-Za-z0-9_]+)/) do |match|
      methods[match[0]] = true
    end
    return methods.keys.sort
  end

  def self.terrain_at(map, tileset, x, y, bridge)
    [2, 1, 0].each do |layer|
      terrain = GameData::TerrainTag.try_get(tileset.terrain_tags[map.data[x, y, layer]])
      next if terrain.id == :None || terrain.ignore_passability
      next if terrain.bridge && !bridge
      return terrain
    end
    return GameData::TerrainTag.get(:None)
  end

  def self.walkable?(map, tileset, x, y, bridge)
    [2, 1, 0].each do |layer|
      tile = map.data[x, y, layer]
      terrain = GameData::TerrainTag.try_get(tileset.terrain_tags[tile])
      next if terrain.ignore_passability || (terrain.bridge && !bridge)
      passage = tileset.passages[tile] & 0x0f
      return passage != 0x0f if terrain.bridge && bridge
      return false if passage == 0x0f
      return true if tileset.priorities[tile] == 0
    end
    return true
  end

  def self.event_scripts(map)
    strings = []
    common_events = nil
    visited = {}
    lists = []
    map.events.each_value do |event|
      event.pages.each do |page|
        lists << page.list
      end
    end
    until lists.empty?
      lists.shift.each do |command|
        if [355, 655].include?(command.code)
          strings.concat(command.parameters.select { |value| value.is_a?(String) })
        elsif command.code == 117 && !visited[command.parameters[0]]
          id = command.parameters[0]
          visited[id] = true
          common_events ||= load_data("Data/CommonEvents.rxdata")
          event = common_events[id]
          lists << event.list if event
        end
      end
    end
    return strings.join("\n")
  end
end
