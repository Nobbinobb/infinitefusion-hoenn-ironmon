#===============================================================================
# Ironmon item-slot and Poké Mart integration
#===============================================================================

module Ironmon
  def self.with_item_slot(slot_id)
    @explicit_item_slot_ids ||= []
    @explicit_item_slot_ids << slot_id.to_s
    return yield
  ensure
    @explicit_item_slot_ids.pop if @explicit_item_slot_ids
  end

  def self.current_item_slot_id
    if @explicit_item_slot_ids && !@explicit_item_slot_ids.empty?
      return @explicit_item_slot_ids.last
    end
    interpreter = pbMapInterpreter
    map_id = interpreter ? interpreter.instance_variable_get(:@map_id).to_i : 0
    event_id = interpreter ? interpreter.instance_variable_get(:@event_id).to_i : 0
    if map_id < 1 && $game_map
      map_id = $game_map.map_id.to_i
    end
    if event_id < 1 && interpreter
      event = interpreter.get_character(0)
      event_id = event.id.to_i if event
    end
    return nil if map_id < 1 || event_id < 1
    return "map:#{map_id}|event:#{event_id}"
  end

  def self.resolve_ground_reward(item, slot_id = nil, generator = nil)
    item_data = GameData::Item.get(item)
    return item_data.id if !item_ground_source_randomizable?(item_data)
    identity = slot_id || current_item_slot_id
    generator ||= item_slot_generator
    return generator.ground_item(identity)
  end

  def self.resolve_tm_gift(item, slot_id = nil, generator = nil)
    item_data = GameData::Item.get(item)
    return item_data.id if !item_data.is_TM?
    identity = slot_id || current_item_slot_id
    generator ||= item_slot_generator
    return generator.tm_gift(identity)
  end

  def self.standard_mart_stock(stock)
    return [] if !stock.is_a?(Array)
    return stock.select do |item_id|
      item = GameData::Item.try_get(item_id)
      item && (item.is_poke_ball? ||
        ItemSlotGenerator::REPEL_ITEMS.include?(item.id))
    end
  end
end

alias ironmon_item_original_pb_item_ball pbItemBall
def pbItemBall(item, quantity = 1, item_name = "", canRandom = true)
  if Ironmon.item_randomization_active? && canRandom
    item = Ironmon.resolve_ground_reward(item)
    canRandom = false
  end
  return ironmon_item_original_pb_item_ball(
    item, quantity, item_name, canRandom
  )
end

alias ironmon_item_original_pb_receive_item pbReceiveItem
def pbReceiveItem(item, quantity = 1, item_name = "", music = nil,
                  canRandom = true)
  item_data = GameData::Item.get(item)
  if Ironmon.item_randomization_active? && canRandom && item_data.is_TM?
    item = Ironmon.resolve_tm_gift(item_data)
    canRandom = false
  end
  return ironmon_item_original_pb_receive_item(
    item, quantity, item_name, music, canRandom
  )
end

alias ironmon_item_original_pb_pokemon_mart pbPokemonMart
def pbPokemonMart(stock, speech_welcome = nil, cantsell = false,
                  speech_bye = nil, speech_what_else = nil)
  if Ironmon.item_randomization_active? && !cantsell
    stock = Ironmon.standard_mart_stock(stock)
  end
  return ironmon_item_original_pb_pokemon_mart(
    stock, speech_welcome, cantsell, speech_bye, speech_what_else
  )
end
