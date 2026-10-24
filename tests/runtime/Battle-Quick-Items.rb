module IronmonBattleQuickItemRuntimeTests
  OUTPUT = $ironmon_quick_items_output

  def self.assert(condition, message)
    raise "Battle quick-item test failed: #{message}" if !condition
  end

  def self.preferences(category = "balls", items = {})
    return { "category" => category, "items" => items }
  end

  def self.test_navigation
    state = preferences("balls", { "balls" => "POKEBALL", "healing" => "POTION" })
    selector = IronmonBattleQuickItems.new(state)
    assert(selector.categories == Ironmon::QUICK_ITEM_CATEGORIES, "all six categories use native item metadata")
    assert(selector.item.id == :POKEBALL, "initial saved ball is selected")
    selector.move_item(-1)
    assert(selector.item.id == :GREATBALL, "left wraps to the Great Ball")
    selector.move_category(1)
    assert(selector.item.id == :POTION, "HP category has its own selection")
    selector.move_item(1)
    assert(selector.item.id == :SUPERPOTION, "right selects Super Potion")
    selector.move_category(-1)
    assert(selector.item.id == :GREATBALL, "returning to balls remembers Great Ball")
    selector.move_category(1)
    assert(selector.item.id == :SUPERPOTION, "returning to healing remembers Super Potion")
    $PokemonGlobal.ironmon_battle_quick_items = state
    restored = Marshal.load(Marshal.dump($PokemonGlobal))
    next_battle = IronmonBattleQuickItems.new(restored.ironmon_battle_quick_items)
    assert(next_battle.category == "healing" && next_battle.item.id == :SUPERPOTION, "new battle and save reload retain category and per-category identities")
    $PokemonBag.pbDeleteItem(:SUPERPOTION, 1)
    next_battle.refresh
    assert(next_battle.item.id == :SUPERPOTION && next_battle.quantity == 1, "partial use keeps the selection and updates count")
    $PokemonBag.pbDeleteItem(:SUPERPOTION, 1)
    next_battle.refresh
    assert(next_battle.item.id == :POTION, "depleted selected item falls back to a remaining stack")
    $PokemonBag.pbDeleteItem(:POTION, 99)
    next_battle.refresh
    assert(!next_battle.categories.include?("healing"), "empty category disappears")
    assert(next_battle.item && next_battle.quantity > 0, "empty current category moves to an available stack")
    $PokemonBag.pockets.each { |pocket| pocket.clear if pocket }
    next_battle.refresh
    assert(next_battle.item.nil? && next_battle.quantity == 0, "empty bag has no stale selection")
    assert(!next_battle.move_item(1) && !next_battle.move_category(-1), "empty selection accepts navigation safely")
    $PokemonBag.pbStoreItem(:SUPERPOTION, 2)
    next_battle.refresh
    assert(next_battle.category == "healing" && next_battle.item.id == :SUPERPOTION, "newly acquired items appear without restarting")
  end

  def self.fill_bag
    $PokemonBag = PokemonBag.new
    { :POKEBALL => 4, :GREATBALL => 2, :POTION => 3, :SUPERPOTION => 2,
      :ANTIDOTE => 2, :ETHER => 2, :XATTACK => 2, :POKEDOLL => 1 }.each do |id, count|
      $PokemonBag.pbStoreItem(id, count)
    end
  end

  def self.create_scene
    pokemon = Pokemon.new(:BULBASAUR, 20, nil, false)
    enemy = Pokemon.new(:PIDGEY, 20, nil, false)
    pokemon.moves.replace([Pokemon::Move.new(:TACKLE), Pokemon::Move.new(:GROWL)])
    pokemon.hp = pokemon.totalhp / 2
    scene = PokeBattle_Scene.new
    trainer = Player.new("Quick items", :POKEMONTRAINER_Red)
    trainer.party = [pokemon]
    $Trainer = trainer
    battle = PokeBattle_Battle.new(scene, [pokemon], [enemy], trainer, nil)
    battle.backdrop = "indoora"
    battle.define_singleton_method(:pbOwnedByPlayer?) { |index| index.even? }
    [pokemon, enemy].each_with_index do |individual, index|
      battle.positions[index] = PokeBattle_ActivePosition.new
      battler = PokeBattle_Battler.new(battle, index)
      battle.battlers[index] = battler
      battler.pbInitialize(individual, 0)
      battle.choices[index] = [:None, 0, nil, -1]
    end
    scene.define_singleton_method(:pbBattleIntroAnimation) { }
    scene.define_singleton_method(:pbCreateTrainerBackSprite) { |*_args| }
    scene.pbStartBattle(battle)
    scene.pbChangePokemon(0, pokemon)
    scene.sprites["pokemon_0"].visible = true
    scene.sprites["pokemon_0"].tone = Tone.new(0, 0, 0)
    scene.sprites["dataBox_0"].visible = true
    scene.sprites["dataBox_1"].visible = true
    Ironmon.instance_variable_set(:@tracker_battle, battle)
    Ironmon.instance_variable_set(:@tracker_battle_id, "quick-items-runtime")
    Ironmon.instance_variable_set(:@tracker_player_pokemon, pokemon)
    return scene, battle
  end

  def self.capture(scene, filename)
    scene.ironmon_refresh_quick_items
    scene.pbFrameUpdate
    Graphics.update
    image = Graphics.snap_to_bitmap
    image.save_to_png(File.join(OUTPUT, filename))
    image.dispose
  end

  def self.test_native_menus(scene, battle)
    Ironmon.clear_tracker_controls
    frames = 0
    scene.define_singleton_method(:pbInputUpdate) do
      frames += 1
      raise "Native quick-item menu did not finish" if frames > 30
      Ironmon.receive_tracker_controls({ "enabled" => true,
        "managed" => Ironmon::TRACKER_CONTROL_KEYS.keys,
        "pressed" => frames == 2 ? ["quick_item_use"] : [] })
      super()
    end
    result = scene.pbCommandMenu(0, true)
    assert(result == 1 && Ironmon.tracker_battle_item_pending?, "real command loop returns Bag for quick confirmation")
    assert(["ironmonQuickItemIcon", "ironmonQuickItemBox", "ironmonQuickItemCount"].all? { |key| !scene.sprites[key].visible }, "real command menu hides the icon, box, and count on exit")
    assert(!Ironmon.tracker_battle_item_menu_active?, "real command menu clears its interrupt scope")
    Ironmon.consume_tracker_battle_item
    frames = 0
    Ironmon.clear_tracker_controls
    yielded = false
    result = scene.pbFightMenu(0) { |_move| yielded = true; true }
    assert(result == -1 && !yielded && Ironmon.tracker_battle_item_pending?, "real move loop returns without selecting the highlighted move")
    assert(["ironmonQuickItemIcon", "ironmonQuickItemBox", "ironmonQuickItemCount"].all? { |key| !scene.sprites[key].visible }, "real move menu hides the icon, box, and count on exit")
    Ironmon.consume_tracker_battle_item
    scene.singleton_class.remove_method(:pbInputUpdate)
    scene.instance_variable_set(:@ironmon_quick_item_battler, 0)
    scene.pbShowWindow(PokeBattle_Scene::COMMAND_BOX)
  end

  def self.test_scene
    fill_bag
    scene, battle = create_scene
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new
    $PokemonGlobal.ironmon_battle_quick_items = preferences("balls", { "balls" => "GREATBALL" })
    scene.instance_variable_set(:@ironmon_quick_item_battler, 0)
    scene.pbShowWindow(PokeBattle_Scene::COMMAND_BOX)
    scene.sprites["commandWindow"].setTexts(["What will Bulbasaur do?", "Fight", "Bag", "Pokémon", "Run"])
    scene.sprites["commandWindow"].setIndexAndMode(0, 0)
    capture(scene, "command-menu.png")
    icon = scene.sprites["ironmonQuickItemIcon"]
    box = scene.sprites["ironmonQuickItemBox"]
    count = scene.sprites["ironmonQuickItemCount"]
    icon_left = icon.x - icon.ox * icon.zoom_x
    icon_top = icon.y - icon.oy * icon.zoom_y
    icon_right = icon_left + icon.width * icon.zoom_x
    icon_bottom = icon_top + icon.height * icon.zoom_y
    assert(icon.item == :GREATBALL, "display uses the actual selected item icon")
    assert(!scene.sprites.key?("ironmonQuickItemPanel"), "no name, category, or separate text panel is created")
    assert(box.x == 0, "item box sits flush against the left edge of the encounter scene")
    assert(box.y + box.bitmap.height == scene.sprites["cmdBar_bg"].y, "item box meets the text and move panels without a margin")
    assert(icon_left >= box.x && icon_top >= box.y && icon_right <= box.x + box.bitmap.width, "scaled item fits inside its box")
    assert(icon.x == box.x + box.bitmap.width / 2 && icon.y == box.y + box.bitmap.height / 2, "item is centered horizontally and vertically inside the box")
    assert(icon_bottom <= box.y + box.bitmap.height, "centered item fits within the box height")
    assert(box.bitmap.width < 48 && box.bitmap.height < 48, "boxed widget is smaller than the previous unframed icon")
    assert(box.z < icon.z && icon.z < count.z && count.z < scene.sprites["pokemon_0"].z, "count draws above the centered icon while the widget stays behind the player's Pokemon")
    connection = Ironmon.tracker_connection
    connection.apply_feature_settings({ "battle_quick_items" => false })
    scene.pbInputUpdate
    assert(!icon.visible && !box.visible && !count.visible, "disabled quick select removes every HUD sprite")
    assert(!scene.ironmon_quick_items_enabled?, "disabled quick select cannot accept navigation or use")
    scene.ironmon_use_quick_item
    assert(!Ironmon.tracker_battle_item_pending?, "disabled direct quick use queues no action")
    connection.apply_feature_settings({})
    scene.pbInputUpdate
    assert(icon.visible && box.visible && count.visible && icon.item == :GREATBALL, "re-enabling restores the saved item and HUD")
    selection = scene.instance_variable_get(:@ironmon_quick_items)
    selection.move_category(1)
    selection.move_item(1)
    capture(scene, "healing-menu.png")
    selector_state = $PokemonGlobal.ironmon_battle_quick_items
    selector_state["category"] = "healing"
    selector_state["items"]["healing"] = "POTION"
    Ironmon.begin_tracker_battle_command(battle, 0, true, 1)
    Ironmon.receive_tracker_controls({ "enabled" => true,
      "managed" => Ironmon::TRACKER_CONTROL_KEYS.keys, "pressed" => ["quick_item_use"] })
    result = catch(:ironmon_tracker_battle_item) { scene.pbInputUpdate; nil }
    assert(result == 1, "quick confirm enters Bag instead of confirming Fight")
    assert(!scene.sprites["commandWindow"].index || scene.sprites["commandWindow"].index == 0, "quick confirm preserves highlighted command")
    Ironmon.end_tracker_battle_command
    assert(battle.pbItemMenu(0, true), "selected Potion registers through ordinary battle item handling")
    assert(battle.choices[0][0] == :UseItem && battle.choices[0][1] == :POTION, "the chosen action is the selected item")
    assert($PokemonBag.pbQuantity(:POTION) == 2, "native registration consumes exactly one Potion")
    battle.pbCancelChoice(0)
    assert($PokemonBag.pbQuantity(:POTION) == 3, "native cancellation restores a reserved item")
    test_native_menus(scene, battle)
    scene.instance_variable_set(:@ironmon_quick_item_battler, 0)
    Ironmon.receive_tracker_controls({ "enabled" => true,
      "managed" => Ironmon::TRACKER_CONTROL_KEYS.keys, "pressed" => [] })
    Input.update
    Ironmon.begin_tracker_battle_command(battle, 0, false, -1)
    scene.pbShowWindow(PokeBattle_Scene::FIGHT_BOX)
    scene.sprites["fightWindow"].battler = battle.battlers[0]
    scene.sprites["fightWindow"].setIndexAndMode(0, 0)
    capture(scene, "move-menu.png")
    Ironmon.receive_tracker_controls({ "enabled" => true,
      "managed" => Ironmon::TRACKER_CONTROL_KEYS.keys, "pressed" => ["quick_item_use"] })
    result = catch(:ironmon_tracker_battle_item) { scene.pbInputUpdate; nil }
    assert(result == -1 && Ironmon.tracker_battle_item_pending?, "quick confirm cancels move selection before using the item")
    Ironmon.consume_tracker_battle_item
    Ironmon.end_tracker_battle_command
    Ironmon.clear_tracker_controls
    scene.pbShowWindow(PokeBattle_Scene::MESSAGE_BOX)
    scene.pbInputUpdate
    assert(!icon.visible && !box.visible && !count.visible, "messages and attack frames hide quick access")
    scene.pbShowWindow(PokeBattle_Scene::COMMAND_BOX)
    battle.internalBattle = false
    assert(!scene.ironmon_quick_items_enabled?, "external battles cannot use quick items")
    battle.internalBattle = true
    battle.battlers[0].effects[PBEffects::SkyDrop] = 1
    assert(!scene.ironmon_quick_items_enabled?, "Sky Drop cannot queue a Bag action rejected by the command loop")
    battle.battlers[0].effects[PBEffects::SkyDrop] = -1
    $PokemonGlobal.ironmon_mode = false
    assert(!scene.ironmon_quick_items_enabled?, "ordinary game mode does not enable quick access")
    $PokemonGlobal.ironmon_mode = true
    Ironmon.begin_tracker_battle_command(battle, 0, true, 1)
    scene.define_singleton_method(:pbShowCommands) do |_message, choices, cancel|
      raise "PP picker has no cancel entry" if cancel != choices.length - 1
      raise "PP picker permits another item interrupt" if Ironmon.tracker_battle_item_menu_active?
      cancel
    end
    assert(scene.ironmon_quick_item_payload(GameData::Item.get(:ETHER)).nil?, "PP prompt can be cancelled without queuing an item")
    assert(scene.sprites["commandWindow"].visible && Ironmon.tracker_battle_item_menu_active?, "cancel restores the original action menu and command context")
    scene.define_singleton_method(:pbShowCommands) { |_message, _choices, _cancel| 1 }
    payload = scene.ironmon_quick_item_payload(GameData::Item.get(:ETHER))
    assert(payload["move_index"] == 1, "PP selection carries the explicit persistent move index")
    scene.define_singleton_method(:pbChooseTarget) { |_index, _target| -1 }
    battle.define_singleton_method(:pbOpposingBattlerCount) { |_index = nil| 2 }
    assert(scene.ironmon_quick_item_payload(GameData::Item.get(:GREATBALL)).nil?, "opponent target selection can be cancelled")
    scene.define_singleton_method(:pbChooseTarget) { |_index, _target| 3 }
    payload = scene.ironmon_quick_item_payload(GameData::Item.get(:GREATBALL))
    assert(payload["target_position"] == 3, "multiple opponents have an explicit ball target")
    battle.internalBattle = true
    selector_state["category"] = "healing"
    selector_state["items"]["healing"] = "SUPERPOTION"
    capture(scene, "preview.png")
    $PokemonBag.pbStoreItem(:SUPERPOTION, 997)
    capture(scene, "large-count.png")
    assert(selection.quantity == 999, "large stack count follows the native inventory")
    assert(count.bitmap.text_size("999").width <= count.bitmap.width - 6, "three-digit count fits the bottom-right overlay")
    $PokemonBag.pockets.each { |pocket| pocket.clear if pocket }
    capture(scene, "empty-bag.png")
    assert(!icon.visible && !box.visible && !count.visible, "empty inventory hides the icon, box, and count")
  ensure
    Ironmon.clear_tracker_controls
    Ironmon.end_tracker_battle_command
    Ironmon.consume_tracker_battle_item
    scene.pbDisposeSprites if scene
    scene.viewport.dispose if scene && scene.viewport && !scene.viewport.disposed?
  end

  def self.run
    $PokemonSystem = PokemonSystem.new
    $game_system = Game_System.new
    $game_switches = Game_Switches.new
    $game_variables = Game_Variables.new
    $game_temp = Game_Temp.new
    $game_temp.in_battle = true
    $PokemonTemp = PokemonTemp.new
    $PokemonGlobal = PokemonGlobalMetadata.new
    $game_map = Game_Map.new
    Graphics.resize_screen(Settings::SCREEN_WIDTH, Settings::SCREEN_HEIGHT)
    fill_bag
    test_navigation
    test_scene
    File.binwrite(File.join(OUTPUT, "result.txt"), "PASS: category and item navigation, per-category memory, save reload, depletion, empty bags, native command and move-menu item handoff, native consumption and cancellation, PP and opponent prompts, restrictions, and battle HUD rendering.\n")
  end
end

IronmonBattleQuickItemRuntimeTests.run
