module IronmonBattleItemRuntimeTests
  OUTPUT_PATH = $ironmon_battle_item_test_output_path.to_s
  TestItem = Struct.new(:id, :battle_use, :name, :description)
  TestBag = Struct.new(:pockets) do
    def pbQuantity(item_id)
      entry = pockets.flatten(1).find { |item| item[0] == item_id }
      return entry ? entry[1] : 0
    end
  end

  def self.assert(condition, message)
    raise "Battle item runtime test failed: #{message}" if !condition
  end

  def self.run
    original_bag = $PokemonBag
    original_battle = Ironmon.instance_variable_get(:@tracker_battle)
    original_battle_id = Ironmon.instance_variable_get(:@tracker_battle_id)
    original_command = Ironmon.instance_variable_get(:@tracker_battle_command)
    original_pending = Ironmon.instance_variable_get(:@tracker_pending_battle_item)
    item_data_singleton = class << GameData::Item; self; end
    begin
      item_ids = [:POTION, :ETHER, :MAXELIXIR, :ANTIDOTE, :XATTACK, :POKEBALL]
      test_items = {}
      item_ids.each do |item_id|
        battle_use = [:ETHER].include?(item_id) ? 2 : 1
        test_items[item_id] = TestItem.new(
          item_id, battle_use, item_id.to_s, "Test item"
        )
      end
      item_data_singleton.alias_method(
        :ironmon_battle_item_test_original_try_get, :try_get
      )
      item_data_singleton.define_method(:try_get) do |item_id|
        test_items[item_id]
      end
      $PokemonBag = TestBag.new([item_ids.map { |item_id| [item_id, 1] }])
      items = Ironmon.tracker_battle_items(100)
      categories = {}
      items.each { |item| categories[item["id"]] = item["category"] }
      assert(categories["POTION"] == "healing", "Potion is a heal")
      assert(categories["ETHER"] == "pp_restore", "Ether is a PP heal")
      assert(categories["MAXELIXIR"] == "pp_restore", "Max Elixir is a PP heal")
      assert(categories["ANTIDOTE"] == "status", "Antidote is a status item")
      assert(categories["XATTACK"] == "combat_stat", "X Attack is a battle-stat item")
      assert(categories["POKEBALL"] == "other", "Poke Ball is in the fallback category")
      snapshot = Ironmon.tracker_healing_snapshot(100)
      assert(snapshot["item_count"] == 1, "healing summary still counts only HP heals")
      assert(snapshot["items"].length == 6, "inventory includes every battle-usable test item")
      rejected = Ironmon.request_tracker_battle_item(
        { "item_id" => "POTION" }, nil
      )
      assert(!rejected["accepted"], "item use is rejected outside an action menu")
      battle = Object.new
      Ironmon.instance_variable_set(:@tracker_battle, battle)
      Ironmon.instance_variable_set(:@tracker_battle_id, "battle-test")
      Ironmon.begin_tracker_battle_command(battle, 0, true, -1)
      accepted = Ironmon.request_tracker_battle_item(
        { "item_id" => "POTION" }, "battle-test"
      )
      assert(accepted["accepted"], "item use is accepted from move selection")
      assert(
        Ironmon.tracker_battle_item_interrupt_result == -1,
        "move selection cancels without selecting its highlighted move"
      )
      Ironmon.consume_tracker_battle_item
      Ironmon.end_tracker_battle_command
      Ironmon.begin_tracker_battle_command(battle, 0, true, 1)
      accepted = Ironmon.request_tracker_battle_item(
        { "item_id" => "POTION" }, "battle-test"
      )
      assert(accepted["accepted"], "item use remains accepted from the command menu")
      assert(
        Ironmon.tracker_battle_item_interrupt_result == 1,
        "the command menu enters the native Bag action"
      )
      Ironmon.consume_tracker_battle_item
      Ironmon.end_tracker_battle_command
      Ironmon.begin_tracker_battle_command(battle, 0, true, -1, false)
      rejected = Ironmon.request_tracker_battle_item(
        { "item_id" => "POTION" }, "battle-test"
      )
      assert(
        !rejected["accepted"],
        "a non-cancellable nested menu rejects item selection"
      )
      accepted = nil
      interrupt_result = nil
      Ironmon.with_tracker_battle_item_interrupt do
        accepted = Ironmon.request_tracker_battle_item(
          { "item_id" => "POTION" }, "battle-test"
        )
        interrupt_result = Ironmon.tracker_battle_item_interrupt_result
      end
      assert(
        accepted["accepted"],
        "cancellable Bag and Pokemon layers accept item selection"
      )
      assert(
        interrupt_result == -1,
        "Bag and Pokemon layers return their native cancellation result"
      )
      Ironmon.consume_tracker_battle_item
      Ironmon.end_tracker_battle_command
      File.binwrite(OUTPUT_PATH, "battle item runtime tests passed\n")
    ensure
      $PokemonBag = original_bag
      if item_data_singleton.method_defined?(:ironmon_battle_item_test_original_try_get)
        item_data_singleton.alias_method(
          :try_get, :ironmon_battle_item_test_original_try_get
        )
        item_data_singleton.remove_method(
          :ironmon_battle_item_test_original_try_get
        )
      end
      Ironmon.instance_variable_set(:@tracker_battle, original_battle)
      Ironmon.instance_variable_set(:@tracker_battle_id, original_battle_id)
      Ironmon.instance_variable_set(:@tracker_battle_command, original_command)
      Ironmon.instance_variable_set(:@tracker_pending_battle_item, original_pending)
    end
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonBattleItemRuntimeTests.run
