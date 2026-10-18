module IronmonCollectionResourceRuntimeTests
  class MemoryJournal
    attr_reader :epoch_id

    def initialize
      @epoch_id = "66666666666666666666666666666666"
    end

    def sequence
      return 0
    end
  end

  class QuietScene
    def method_missing(_name, *_args)
      return nil
    end

    def respond_to_missing?(_name, _private = false)
      return true
    end
  end

  def self.assert(value, message)
    raise "Gameplay collection resource runtime test failed: #{message}" if !value
    @checks += 1
  end

  def self.resolutions(facts, item)
    return facts.select { |fact| fact["type"] == "item.use_resolved" && fact["payload"]["item_id"] == item.to_s }
  end

  def self.run
    collection = Ironmon::GameplayCollection
    original_globals = [$Trainer, $PokemonGlobal, $PokemonSystem, $PokemonBag, $PokemonTemp, $game_switches, $game_variables, $game_temp, $game_map]
    original_items = GameData::Item::DATA.dup
    original_catalogue = collection.instance_variable_get(:@catalogue_entries)
    original_error = collection.instance_variable_get(:@last_error)
    original_disabled = collection.instance_variable_get(:@disabled)
    original_groups = collection.instance_variable_get(:@group_contexts)
    overrides = []
    globals = []
    replace = proc do |object, name, implementation|
      overrides << [object.singleton_class, name, object.method(name)]
      object.singleton_class.define_method(name, implementation)
    end
    replace_global = proc do |name, implementation|
      globals << [name, Object.instance_method(name), Object.private_method_defined?(name)]
      Object.send(:define_method, name, implementation)
      Object.send(:private, name)
    end
    facts = []
    counter = 0
    journal = MemoryJournal.new
    @checks = 0
    replace.call(collection, :enabled?, proc { true })
    replace.call(collection, :journal, proc { journal })
    replace.call(collection, :next_id, proc { |kind| counter += 1; "resource-fixture:#{kind}:#{counter}" })
    replace.call(collection, :emit, proc do |type, payload = {}, context = {}|
      inherited = collection.current_context.merge(collection.stringify(context))
      captured = collection.stringify(payload)
      captured["event_id"] ||= collection.next_id("event")
      captured["tracking_epoch_id"] ||= journal.epoch_id
      captured["attempt_instance_id"] ||= inherited["attempt_id"]
      captured["battle_instance_id"] ||= inherited["battle_instance_id"]
      captured["cause_event_id"] ||= inherited["cause_event_id"]
      facts << { "type" => type, "sequence" => (facts.length + 1).to_s, "payload" => captured, "context" => inherited }
      facts.length
    end)
    replace.call(collection::Ownership, :active?, proc { false })
    replace_global.call(:pbMessage, proc { |*_args, &_block| nil })
    confirm = true
    replace_global.call(:pbConfirmMessage, proc { |*_args, &_block| confirm })
    replace_global.call(:promptRegisterItem, proc { |*_args| nil })
    replace_global.call(:updatePinkanBerryDisplay, proc { nil })
    sweet_scent_calls = 0
    radar_calls = 0
    replace_global.call(:pbSweetScent, proc { sweet_scent_calls += 1; true })
    replace_global.call(:pbCanUsePokeRadar?, proc { true })
    replace_global.call(:pbUsePokeRadar, proc { radar_calls += 1; 1 })
    trainer_type = nil
    GameData::TrainerType.each { |entry| trainer_type = entry.id; break }
    $game_temp = Game_Temp.new
    $PokemonTemp = PokemonTemp.new
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonSystem = PokemonSystem.new
    $PokemonBag = PokemonBag.new
    $game_switches = Game_Switches.new
    $game_variables = Game_Variables.new
    $game_map = nil
    $Trainer = Player.new("ResourceFixture", trainer_type)
    pokemon = Pokemon.new(:BULBASAUR, 20, $Trainer)
    pokemon.moves = [Pokemon::Move.new(:TACKLE)]
    pokemon.define_singleton_method(:compatible_with_move?) { |_move| true }
    $Trainer.party = [pokemon]
    tutor_calls = 0
    receiver = Object.new
    replace_global.call(:pbMoveTutorChoose, proc do |move, *_args|
      tutor_calls += 1
      receiver.send(:pbLearnMove, pokemon, move, false, true)
    end)
    machine = nil
    GameData::Item.each do |entry|
      next if !entry.is_TM? || !entry.move || entry.move == :TACKLE
      machine = entry
      break
    end
    assert(machine, "a native machine fixture is available")
    GameData::Item.register(:id => :COLLECTION_TEST_RECORD, :id_number => 2_000_000_001,
      :name => "Collection test record", :pocket => machine.pocket, :field_use => 6, :move => :GROWL)
    begin
      $PokemonBag.pbStoreItem(:HONEY, 1)
      first = facts.length
      result = receiver.send(:pbUseItem, $PokemonBag, :HONEY)
      honey = resolutions(facts[first..-1], :HONEY)
      assert(result == 2 && $PokemonBag.pbQuantity(:HONEY) == 0, "native Honey route preserves close-bag and actual consumption semantics")
      assert(honey.length == 1 && honey[0]["payload"]["native_handler_result"] == 4, "raw deferred consume routing code is retained")
      assert(honey[0]["payload"]["effect_succeeded"].nil? && honey[0]["payload"]["resolution_stage"] == "deferred_to_field", "deferred Honey route cannot claim an applied field effect")
      result = ItemHandlers.triggerUseInField(:HONEY)
      honey = resolutions(facts[first..-1], :HONEY)
      assert(result == 3 && sweet_scent_calls == 1, "native Honey field callback runs exactly once and preserves its consume-code result")
      assert(honey.count { |fact| fact["payload"]["effect_succeeded"] == true } == 1, "deferred Honey has exactly one completed successful use")

      first = facts.length
      result = ItemHandlers.triggerUseFromBag(:POKERADAR)
      assert(result == 2 && resolutions(facts[first..-1], :POKERADAR)[0]["payload"]["effect_succeeded"].nil?, "native radar code 2 remains a deferred key-item route")
      ItemHandlers.triggerUseInField(:POKERADAR)
      radar = resolutions(facts[first..-1], :POKERADAR)
      assert(radar_calls == 1 && radar.count { |fact| fact["payload"]["effect_succeeded"] == true } == 1, "native radar route plus field callback count one successful use")

      $PokemonGlobal.repel = 0
      $PokemonBag.pbStoreItem(:REPEL, 1)
      first = facts.length
      result = receiver.send(:pbUseItem, $PokemonBag, :REPEL)
      repel = resolutions(facts[first..-1], :REPEL)
      assert(result == 1 && $PokemonGlobal.repel == 100 && $PokemonBag.pbQuantity(:REPEL) == 0, "native fallback field handler actually applies and consumes Repel")
      assert(repel.length == 1 && repel[0]["payload"]["effect_succeeded"] == true, "a fallback UseInField handler is not mistaken for a deferred route")

      $PokemonBag.pbStoreItem(machine.id, 1)
      first = facts.length
      result = receiver.send(:pbUseItem, $PokemonBag, machine.id)
      learned = resolutions(facts[first..-1], machine.id)
      assert(result == 1 && pokemon.hasMove?(machine.move) && tutor_calls == 1, "actual native bag-machine branch calls native learning once")
      assert(learned.length == 1 && learned[0]["payload"]["effect_succeeded"] == true && learned[0]["payload"]["consumed_quantity"] == 0, "reusable machine use is captured without false consumption")
      confirm = false
      first = facts.length
      result = receiver.send(:pbUseItem, $PokemonBag, machine.id)
      declined = resolutions(facts[first..-1], machine.id)
      assert(result == 0 && tutor_calls == 1 && $PokemonBag.pbQuantity(machine.id) == 1, "native declined machine leaves bag and learning unchanged")
      assert(declined.length == 1 && declined[0]["payload"]["effect_succeeded"] == false, "declined machine cannot count as a successful use")

      confirm = true
      $PokemonBag.pbStoreItem(:COLLECTION_TEST_RECORD, 1)
      first = facts.length
      result = receiver.send(:pbUseItemOnPokemon, :COLLECTION_TEST_RECORD, pokemon, QuietScene.new)
      record = resolutions(facts[first..-1], :COLLECTION_TEST_RECORD)
      assert(result == true && pokemon.hasMove?(:GROWL) && $PokemonBag.pbQuantity(:COLLECTION_TEST_RECORD) == 0, "actual native party-machine branch learns and consumes a TR")
      assert(record.length == 1 && record[0]["payload"]["consumed_quantity"] == 1, "TR machine outcome retains its actual bag decrement")

      replace.call(Ironmon, :current_area_event_reference, proc { [1, 2] })
      replace.call(Ironmon, :tracker_area_hidden_items, proc { |_map| [{ "event_id" => 2 }] })
      replace.call(Ironmon, :current_area_event_entry_id, proc { |_kind| nil })
      first = facts.length
      before = $PokemonBag.pbQuantity(:POTION)
      result = receiver.send(:pbItemBall, :POTION, 1, "", false)
      pickup = facts[first..-1]
      lot = pickup.find { |fact| fact["type"] == "item.provenance_registered" }
      outcome = pickup.find { |fact| fact["type"] == "pickup.resolved" }
      assert(result == true && $PokemonBag.pbQuantity(:POTION) == before + 1, "actual native hidden pickup acquires exactly one item")
      assert(lot && lot["payload"]["acquisition_origin"] == "hidden" && outcome["payload"]["source_kind"] == "hidden", "hidden provenance and pickup outcome agree before acquisition")
      assert(outcome["payload"]["authored_source_ref"] == "1:2" && outcome["payload"]["authored_source_tuple"] == [1, 2], "authored event has a stable scalar reference and its original map/event tuple")
      inventory = pickup.find { |fact| fact["type"] == "inventory.quantity_changed" }["payload"]
      assert(inventory["reason"] == "acquisition" && inventory["raw_reason"] == "visible_ground", "canonical inventory reason retains the precise native source")

      replace.call(Ironmon, :tracker_area_hidden_items, proc { |_map| raise "optional metadata unavailable" })
      first = facts.length
      before = $PokemonBag.pbQuantity(:POTION)
      result = receiver.send(:pbItemBall, :POTION, 1, "", false)
      pickup = facts[first..-1]
      lot = pickup.find { |fact| fact["type"] == "item.provenance_registered" }
      assert(result == true && $PokemonBag.pbQuantity(:POTION) == before + 1, "optional metadata failure never retries or blocks the native pickup")
      assert(lot && lot["payload"]["acquisition_origin"] == "unknown", "unavailable authored metadata remains explicitly unknown")
      assert(collection.last_error == original_error, "optional classification failure does not disable unrelated collection")
      File.binwrite(File.join($ironmon_collection_test_root, "resources.json"), collection.encode({ "status" => "passed", "assertions" => @checks, "facts" => facts.length }))
      File.binwrite(File.join($ironmon_collection_test_root, "resources.batches.json"), collection.encode([{ "stream_id" => "resource-native-fixture", "events" => facts }]))
    ensure
      globals.reverse_each do |name, original, private_method|
        Object.send(:define_method, name, original)
        Object.send(:private, name) if private_method
      end
      overrides.reverse_each { |singleton, name, original| singleton.define_method(name, original) }
      GameData::Item::DATA.replace(original_items)
      collection.instance_variable_set(:@catalogue_entries, original_catalogue)
      collection.instance_variable_set(:@last_error, original_error)
      collection.instance_variable_set(:@disabled, original_disabled)
      collection.instance_variable_set(:@group_contexts, original_groups)
      $Trainer, $PokemonGlobal, $PokemonSystem, $PokemonBag, $PokemonTemp, $game_switches, $game_variables, $game_temp, $game_map = original_globals
    end
  end
end

IronmonCollectionResourceRuntimeTests.run
