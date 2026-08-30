#===============================================================================
# Ironmon field healing NPCs
#===============================================================================

module Ironmon
  HEALING_NPC_EVENT_ID = 900
  HEALING_NPC_CONFIGURATIONS = {
    30 => {
      :name => "IRONMON_PETALBURG_WOODS_HEALER",
      :x => 49,
      :y => 38,
      :character => "NPC_Hoenn_Ranger_F",
      :opening => [
        "The forest's spores can wear a Pokémon down.",
        "Let me tend to your team before you head deeper."
      ],
      :closing => "There we are! They're ready for the woods again."
    },
    31 => {
      :name => "IRONMON_ROUTE_116_HEALER",
      :x => 5,
      :y => 17,
      :character => "NPC_worker",
      :opening => [
        "The trainers and tunnel work make Route 116 rough",
        "on Pokémon. Let me patch your team up."
      ],
      :closing => "All set! Take care around the rocks."
    }
  }

  def self.healing_npc_event(configuration)
    event = RPG::Event.new(configuration[:x], configuration[:y])
    event.id = HEALING_NPC_EVENT_ID
    event.name = configuration[:name]
    page = event.pages[0]
    page.graphic.character_name = configuration[:character]
    page.graphic.direction = 2
    page.graphic.pattern = 0
    page.move_type = 0
    page.trigger = 0
    page.through = false
    page.list = [
      RPG::EventCommand.new(101, 0, [configuration[:opening][0]]),
      RPG::EventCommand.new(401, 0, [configuration[:opening][1]]),
      RPG::EventCommand.new(
        250, 0, [RPG::AudioFile.new("potion", 80, 100)]
      ),
      RPG::EventCommand.new(314, 0, [0]),
      RPG::EventCommand.new(101, 0, [configuration[:closing]]),
      RPG::EventCommand.new(0, 0, [])
    ]
    return event
  end

  def self.patch_healing_npc_map(map_id, map)
    configuration = HEALING_NPC_CONFIGURATIONS[map_id]
    return false if !configuration || !map || !map.events
    return false if map.events[HEALING_NPC_EVENT_ID]
    occupied = map.events.values.any? do |event|
      event.x == configuration[:x] && event.y == configuration[:y]
    end
    return false if occupied
    map.events[HEALING_NPC_EVENT_ID] = healing_npc_event(configuration)
    return true
  end
end

Events.onMapCreate += proc do |_sender, event|
  map_id = event[0]
  map = event[1]
  Ironmon.patch_healing_npc_map(map_id, map) if Ironmon.active?
end
