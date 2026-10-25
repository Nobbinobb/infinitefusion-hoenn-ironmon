module IronmonFeatureSettingsRuntimeTests
  def self.assert(condition, message)
    raise "Feature settings validation failed: #{message}" if !condition
  end

  def self.update(connection, features, extra = {})
    payload = { "features" => features, "auto_select_starter" => true,
      "maximum_starter_base_stat_total" => 400,
      "favorite_species_ids" => ["BULBASAUR:0"] }.merge(extra)
    connection.send(:handle_request, { "request_id" => "features",
      "command" => "update_settings", "payload" => payload })
  end

  def self.test_transport(connection, messages)
    assert(Ironmon::TRACKER_FEATURE_NAMES.all? { |name| connection.feature_enabled?(name) }, "all feature switches default on")
    assert(connection.auto_select_starter?, "automatic starter selection defaults on")
    disabled = Ironmon::TRACKER_FEATURE_NAMES.each_with_object({}) { |name, result| result[name] = false }
    update(connection, disabled)
    result = messages.last["payload"]
    assert(messages.last["success"] && result["features"] == disabled, "settings acknowledge every disabled feature")
    assert(result["maximum_starter_base_stat_total"] == 400 && result["favorite_species_ids"] == ["BULBASAUR:0"], "disabled rules echo retained configuration")
    assert(connection.maximum_starter_base_stat_total.nil? && connection.favorite_species_ids.empty?, "disabled rules have no effective ceiling or favorites")
    update(connection, nil)
    assert(connection.feature_settings == disabled, "unrelated settings retain all disabled switches")
    update(connection, {})
    assert(connection.maximum_starter_base_stat_total == 400 && connection.favorite_species_ids == ["BULBASAUR:0"], "re-enabling restores saved rule values")
    update(connection, {}, { "maximum_starter_base_stat_total" => 0 })
    assert(connection.maximum_starter_base_stat_total.nil?, "zero means unlimited even with enforcement enabled")
    update(connection, {})
  end

  def self.test_starters(connection)
    pokemon = [:BULBASAUR, :CHARMANDER, :SQUIRTLE].map { |species| Pokemon.new(species, 5) }
    pokemon.each_with_index { |candidate, index| candidate.define_singleton_method(:baseStats) { { :HP => 350 + index * 100 } } }
    Ironmon.instance_variable_set(:@tracker_starter_selection, {
      :selection_id => "feature-starters", :auto_select => true,
      :pokemon => pokemon, :revealed => [true, true, true],
      :sprites => [nil, nil, nil], :accepting => true
    })
    Ironmon.refresh_tracker_starter_rules
    selection = Ironmon.instance_variable_get(:@tracker_starter_selection)
    assert(selection[:ceiling] == 400 && selection[:favorites] == [true, false, false], "configured starter rules are active")
    assert(!Ironmon.tracker_starter_eligible?(1) && selection[:random_pick_index] == 0, "enforced ceiling constrains manual and random choices")
    update(connection, { "favorite_clause" => false, "enforce_bst_limit" => false })
    assert(selection[:ceiling].nil? && selection[:favorites] == [false, false, false], "open starter selection refreshes disabled rules")
    assert(Ironmon.tracker_starter_eligible?(1), "over-ceiling starter is eligible while enforcement is off")
    assert(selection[:random_pick_index] == Ironmon.tracker_starter_random_pick(3), "unlimited deterministic pick uses the whole roster")
    update(connection, {})
    assert(selection[:ceiling] == 400 && selection[:favorites][0] && !Ironmon.tracker_starter_eligible?(1), "re-enabling restores the open selection's original constraints")
    test_native_starter_dialog(connection, pokemon)
  ensure
    Ironmon.instance_variable_set(:@tracker_starter_selection, nil)
  end

  def self.test_native_starter_dialog(connection, pokemon)
    (1..100).each do |seed|
      $PokemonGlobal.ironmon_seed = seed
      break if Ironmon.tracker_starter_random_pick(3) == 2
    end
    Ironmon.refresh_tracker_starter_rules
    scene = StartersSelectionScene.allocate
    scene.instance_variable_set(:@starter_pokemon, pokemon)
    scene.define_singleton_method(:updateOpenPokeballPosition) {}
    scene.define_singleton_method(:updateStarterSelectionGraphics) {}
    scene.define_singleton_method(:ironmon_tracker_finalize_starter) { @index }
    frame = 0
    graphics_patch = Module.new
    graphics_patch.define_method(:update) do
      frame += 1
      IronmonFeatureSettingsRuntimeTests.update(connection, { "favorite_clause" => false, "enforce_bst_limit" => false }) if frame == 1
    end
    input_patch = Module.new
    input_patch.define_method(:update) {}
    input_patch.define_method(:trigger?) { |key| key == Input::USE && frame == 1 }
    clock_patch = Module.new
    clock_patch.define_method(:tracker_uptime_seconds) { frame.to_f }
    Graphics.singleton_class.prepend(graphics_patch)
    Input.singleton_class.prepend(input_patch)
    Ironmon.singleton_class.prepend(clock_patch)
    chosen = scene.ironmon_tracker_choose_random_or_favorite([0])
    assert(chosen == 2, "native starter menu updates its commands when toggles change the allowed pick")
  ensure
    graphics_patch.send(:remove_method, :update) if graphics_patch
    input_patch.send(:remove_method, :update) if input_patch
    input_patch.send(:remove_method, :trigger?) if input_patch
    clock_patch.send(:remove_method, :tracker_uptime_seconds) if clock_patch
  end

  def self.test_overlays(connection)
    $scene = Scene_Map.new
    $PokemonGlobal.repel = 100
    Ironmon::ToggleSpeed.reset
    $PokemonSystem.speedup = 1
    Ironmon::ToggleSpeed.before_update
    $GameSpeed = 1
    Ironmon::ToggleSpeed.after_update
    Ironmon::StatusOverlays.update
    speed = Ironmon::StatusOverlays.instance_variable_get(:@speed)
    repel = Ironmon::StatusOverlays.instance_variable_get(:@repel)
    assert(speed && speed.sprite.visible && repel.sprite.visible, "both status overlays are initially visible")
    connection.apply_feature_settings({ "speed_indicator" => false })
    Ironmon::StatusOverlays.update
    assert(!speed.sprite.visible && repel.sprite.visible && repel.sprite.y == Ironmon::OverlayStyle::MARGIN, "hidden speed leaves no empty gap above Repel")
    connection.apply_feature_settings({ "repel_counter" => false })
    Ironmon::StatusOverlays.update
    assert(!repel.sprite.visible && speed.sprite.visible, "Repel and speed switches are independent")
    connection.apply_feature_settings({ "repel_counter" => false, "speed_indicator" => false })
    Ironmon::StatusOverlays.update
    assert(Ironmon::StatusOverlays.instance_variable_get(:@viewport).nil?, "both hidden indicators dispose their graphics")
    assert($PokemonGlobal.repel == 100 && $GameSpeed == 1, "hidden indicators do not alter Repel or speed")
    connection.apply_feature_settings({})
    Ironmon::StatusOverlays.update
    assert(Ironmon::StatusOverlays.instance_variable_get(:@speed).sprite.visible && Ironmon.repel_overlay_visible?, "re-enabling restores both indicators")
  ensure
    Ironmon::StatusOverlays.dispose
    $scene = nil
  end

  def self.test_shortcuts(connection)
    wild = Object.new
    wild.define_singleton_method(:trainerBattle?) { false }
    assert(Ironmon.battle_cancel_run_hotkey?(wild, 0), "wild Run shortcut starts enabled")
    connection.apply_feature_settings({ "quick_run" => false, "battle_quick_items" => false })
    assert(!Ironmon.battle_cancel_run_hotkey?(wild, 0), "disabled Run shortcut does not alter the native menu")
    actions = %w[battle_run quick_item_left quick_item_right quick_item_up quick_item_down quick_item_use]
    Ironmon.receive_tracker_controls({ "enabled" => true, "managed" => Ironmon::TRACKER_CONTROL_KEYS.keys, "pressed" => actions })
    Ironmon.update_tracker_controls
    assert(actions.none? { |action| Ironmon.tracker_control_action_triggered?(action) }, "every disabled shortcut is rejected despite pressed input")
    scene = PokeBattle_Scene.allocate
    scene.instance_variable_set(:@ironmon_quick_item_battler, 0)
    assert(!scene.ironmon_quick_items_enabled?, "disabled quick items reject the battle selector")
    assert(scene.ironmon_use_quick_item.nil?, "direct use cannot queue a disabled quick item")
    connection.apply_feature_settings({})
    assert(Ironmon.battle_cancel_run_hotkey?(wild, 0), "Run shortcut returns after re-enabling")
  ensure
    Ironmon.clear_tracker_controls
  end

  def self.test_achievements(connection)
    achievements = Ironmon::Achievements
    first = "11111111-1111-1111-1111-111111111111"
    second = "22222222-2222-2222-2222-222222222222"
    achievements.instance_variable_set(:@queue, [{ "id" => first }])
    achievements.instance_variable_set(:@known, { first => :queued })
    achievements.instance_variable_set(:@displayed, {})
    connection.apply_feature_settings({ "achievement_popups" => false })
    achievements.suppress_notifications
    achievements.receive({ "unlocks" => [{ "id" => second }] })
    assert(achievements.instance_variable_get(:@queue).empty?, "disabled achievements create no toast queue")
    assert(achievements.instance_variable_get(:@displayed).keys.sort == [first, second], "suppressed notices are acknowledged without losing unlocks")
    connection.apply_feature_settings({})
    achievements.receive({ "unlocks" => [{ "id" => second }] })
    assert(achievements.instance_variable_get(:@queue).empty?, "re-enabling does not replay suppressed notices")
  end

  def self.run
    $PokemonSystem = PokemonSystem.new
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new
    $PokemonGlobal.ironmon_seed = 123
    $PokemonTemp = PokemonTemp.new
    $game_temp = Game_Temp.new
    $game_system = Game_System.new
    $game_switches = Game_Switches.new
    $game_variables = Game_Variables.new
    connection = Ironmon::TrackerConnection.new
    Ironmon.instance_variable_set(:@tracker_connection, connection)
    messages = []
    connection.define_singleton_method(:queue_message) { |message| messages << message }
    test_transport(connection, messages)
    test_starters(connection)
    test_overlays(connection)
    test_shortcuts(connection)
    test_achievements(connection)
    File.binwrite($ironmon_feature_settings_result, "PASS: defaults, live transport, retained BST/favorites, open starter rules, independent status indicators, disabled battle inputs, and suppressed achievement acknowledgements.\n")
  end
end

IronmonFeatureSettingsRuntimeTests.run
