#===============================================================================
# HM rewards replaced by permanent field tools
#===============================================================================

module Ironmon
  HM_TOOL_BY_ITEM = {
    :HM01 => :MACHETE,
    :HM02 => :TELEPORTER,
    :HM03 => :SURFBOARD,
    :HM04 => :LEVER,
    :HM05 => :JETPACK,
    :HM06 => :PICKAXE,
    :HM07 => :TELEPORTER,
    :HM08 => :SCUBAGEAR,
    :HM09 => :LANTERN,
    :HM10 => :CLIMBINGGEAR
  }.freeze

  def self.hm_replacement_item(item)
    item_data = GameData::Item.try_get(item)
    return nil if !item_data
    return HM_TOOL_BY_ITEM[item_data.id]
  end

  def self.hm_tool_already_owned?(item)
    return $PokemonBag && $PokemonBag.pbQuantity(item) > 0
  end

  def self.convert_owned_hms_to_tools
    @hm_tool_conversion_error = nil
    return true if !$PokemonBag
    staged_bag = Marshal.load(Marshal.dump($PokemonBag))
    conversions = []
    HM_TOOL_BY_ITEM.each do |hm, tool|
      quantity = staged_bag.pbQuantity(hm)
      next if quantity <= 0
      if staged_bag.pbQuantity(tool) <= 0 && !staged_bag.pbStoreItem(tool, 1)
        raise "The field tool #{tool} could not be stored."
      end
      hm_data = GameData::Item.get(hm)
      hm_pocket = staged_bag.pockets[hm_data.pocket]
      if !ItemStorageHelper.pbDeleteItem(hm_pocket, hm, quantity)
        raise "The HM #{hm} could not be removed."
      end
      conversions << [hm, tool]
    end
    $PokemonBag = staged_bag
    conversions.each do |hm, tool|
      echoln "Ironmon replaced #{hm} with the field tool #{tool}."
    end
    return true
  rescue StandardError => e
    @hm_tool_conversion_error = e.message
    echoln "Ironmon could not replace owned HMs with field tools: #{e.message}"
    return false
  end

  def self.hm_tool_conversion_error
    return @hm_tool_conversion_error
  end
end

alias ironmon_hm_original_pb_item_ball pbItemBall
def pbItemBall(item, quantity = 1, item_name = "", canRandom = true)
  replacement = Ironmon.hm_replacement_item(item) if Ironmon.active?
  if replacement
    return true if Ironmon.hm_tool_already_owned?(replacement)
    return ironmon_hm_original_pb_item_ball(replacement, 1, "", false)
  end
  return ironmon_hm_original_pb_item_ball(
    item, quantity, item_name, canRandom
  )
end

alias ironmon_hm_original_pb_receive_item pbReceiveItem
def pbReceiveItem(item, quantity = 1, item_name = "", music = nil,
                  canRandom = true)
  replacement = Ironmon.hm_replacement_item(item) if Ironmon.active?
  if replacement
    return true if Ironmon.hm_tool_already_owned?(replacement)
    return ironmon_hm_original_pb_receive_item(
      replacement, 1, "", music, false
    )
  end
  return ironmon_hm_original_pb_receive_item(
    item, quantity, item_name, music, canRandom
  )
end
