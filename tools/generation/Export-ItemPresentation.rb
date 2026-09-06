begin
  IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:997|998|999)/])
  IronmonScriptLoader.load_manifest(
    $ironmon_item_source, File.join($ironmon_item_source, "load_order.json")
  )
  GameData.load_all
  $game_temp = Game_Temp.new
  entries = {}
  GameData::Item.each do |item|
    category = Ironmon.item_result_category(item)
    category = :general_utility if [:excluded, :uniform].include?(category)
    entries[item.id.to_s] = { "Name" => item.name, "Category" => category.to_s }
  end
  raise "the item catalog is empty" if entries.empty?
  { "POTION" => "hp_recovery", "ETHER" => "status_pp_recovery",
    "XATTACK" => "battle_consumable", "LEFTOVERS" => "held_combat" }.each do |id, category|
    raise "unexpected category for #{id}" if entries[id]["Category"] != category
  end
  File.binwrite($ironmon_item_output, Ironmon.tracker_json_generate(entries.sort.to_h))
  exit! 0
rescue Exception => error
  File.binwrite("#{$ironmon_item_output}.error", "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")
  exit! 1
end
