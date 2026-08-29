module IronmonRuntimeHookTests
  OUTPUT_PATH = $ironmon_runtime_hook_test_output_path.to_s

  class TrackerLifecycleConnection
    attr_reader :events

    def initialize
      @events = []
    end

    def send_event(name, payload)
      @events << [name, payload]
    end
  end

  class ModeAvailabilityTrainer
    attr_accessor :new_game_plus_unlocked

    def initialize(new_game_plus_unlocked)
      @new_game_plus_unlocked = new_game_plus_unlocked
    end
  end

  def self.assert(condition, message)
    raise "Runtime-hook test failed: #{message}" if !condition
  end

  def self.test_registered_hook_order
    assert(
      Ironmon.game_load_hook_names == [
        :ability_randomization,
        :base_stat_randomization,
        :evolution_randomization,
        :item_randomization,
        :move_access_randomization,
        :run_lifecycle,
        :species_randomization,
        :difficulty_enforcement,
        :diagnostics,
        :tracker
      ],
      "game-load callbacks retain their established unwind order"
    )
    assert(
      Ironmon.game_save_hook_names == [:checkpoint, :run_lifecycle],
      "game-save callbacks retain their established wrapper order"
    )
    assert(
      Ironmon.graphics_update_hook_names == [
        :run_lifecycle,
        :challenge_statistics,
        :early_game_loss_reset,
        :tracker,
        :starter_bst_reset
      ],
      "frame callbacks retain their established execution order"
    )
  end

  def self.test_fresh_game_mode_availability
    original_trainer = $Trainer
    $Trainer = nil
    assert(
      Ironmon.mode_available?,
      "Ironmon is available when no save file or Trainer exists"
    )
    assert(
      Ironmon.randomized_mode_available? == Settings::KANTO,
      "the base randomized mode retains its region-specific fresh-game gate"
    )

    $Trainer = ModeAvailabilityTrainer.new(false)
    assert(
      Ironmon.mode_available?,
      "Ironmon is available before New Game Plus is unlocked"
    )
    assert(
      !Ironmon.randomized_mode_available?,
      "Hoenn Randomized Mode remains locked before New Game Plus"
    ) if Settings::HOENN
  ensure
    $Trainer = original_trainer
  end

  def self.test_uninitialized_map_scene_spriteset
    scene = Scene_Map.allocate
    assert(
      scene.spriteset.nil?,
      "an uninitialized reset map scene has no spriteset yet"
    )
  end

  def self.test_fresh_game_mode_entry
    map = load_data("Data/Map295.rxdata")
    assert(
      Ironmon.patch_mode_entry_map(295, map),
      "the authored Hoenn fresh-start map exposes game-mode selection"
    )
    commands = map.events[1].pages[0].list
    selection_index = commands.index do |command|
      command.code == 355 && command.parameters[0] == "select_game_mode"
    end
    condition = commands[selection_index - 1]
    assert(
      condition.parameters == [12, Ironmon::MODE_ENTRY_CONDITION_SCRIPT],
      "fresh-start mode selection no longer depends on an existing save"
    )
    assert(
      !Ironmon.patch_mode_entry_map(1, map),
      "the mode-entry patch does not alter unrelated maps"
    )
  end

  def self.test_defeat_start_over_suppression
    singleton = class << Ironmon; self; end
    singleton.send(
      :alias_method, :runtime_hook_original_active,
      :active?
    )
    singleton.send(:define_method, :active?) { true }
    start_over_calls = []
    define_singleton_method(:ironmon_failure_original_start_over) do |gameover|
      start_over_calls << gameover
    end

    assert(
      !Ironmon.defeat_start_over_suppressed?,
      "ordinary overworld start-over behavior is initially available"
    )
    Ironmon.with_defeat_start_over_suppressed(2) do
      assert(
        Ironmon.defeat_start_over_suppressed?,
        "a battle loss suppresses the native blackout revival"
      )
      pbStartOver
    end
    Ironmon.with_defeat_start_over_suppressed(5) do
      assert(
        Ironmon.defeat_start_over_suppressed?,
        "a battle draw suppresses the native blackout revival"
      )
    end
    Ironmon.with_defeat_start_over_suppressed(1) do
      assert(
        !Ironmon.defeat_start_over_suppressed?,
        "a battle victory retains ordinary start-over behavior"
      )
    end
    assert(
      start_over_calls.empty?,
      "the loss path does not reach the native 1 HP safeguard"
    )
  ensure
    if singleton && singleton.method_defined?(:runtime_hook_original_active)
      singleton.send(:alias_method, :active?, :runtime_hook_original_active)
      singleton.send(:remove_method, :runtime_hook_original_active)
    end
    singleton_class.send(
      :remove_method, :ironmon_failure_original_start_over
    ) if respond_to?(:ironmon_failure_original_start_over, true)
    Ironmon.instance_variable_set(:@defeat_start_over_suppressed, nil)
  end

  def self.test_encounter_hook_source_ownership
    source_location = Ironmon.method(:overworld_species_for).source_location
    trainer_location = Object.instance_method(:customTrainerBattle).source_location
    wild_location = Object.instance_method(:pbWildBattleSpecific).source_location
    gift_location = Object.instance_method(:tryRandomizeGiftPokemon).source_location
    assert(
      source_location &&
        File.basename(source_location[0]) == "004_Encounter_Hooks.rb",
      "encounter-source hooks stay in the core encounter source"
    )
    assert(
      trainer_location &&
        File.basename(trainer_location[0]) ==
          "004_Encounter_Hooks_1_Trainers.rb",
      "trainer hooks stay in the trainer integration source"
    )
    assert(
      wild_location &&
        File.basename(wild_location[0]) ==
          "004_Encounter_Hooks_2_Wild_Battles.rb",
      "scripted wild battle hooks stay in the wild battle source"
    )
    assert(
      gift_location &&
        File.basename(gift_location[0]) ==
          "004_Encounter_Hooks_3_Gifts_And_Starters.rb",
      "gift hooks stay in the gift and starter source"
    )
  end

  def self.test_statistics_source_ownership
    schema_location = Ironmon.method(:default_attempt_statistics).source_location
    battle_location = Ironmon.method(:begin_statistics_battle).source_location
    item_location = Ironmon.method(:record_consumed_item).source_location
    runtime_location = IronmonStatisticsBattlerHooks.instance_method(
      :pbFaint
    ).source_location
    assert(
      schema_location &&
        File.basename(schema_location[0]) == "008_Challenge_Statistics.rb",
      "statistics schema stays in the core statistics source"
    )
    assert(
      battle_location &&
        File.basename(battle_location[0]) ==
          "008_Challenge_Statistics_1_Battles.rb",
      "battle aggregation stays in the battle statistics source"
    )
    assert(
      item_location &&
        File.basename(item_location[0]) ==
          "008_Challenge_Statistics_2_Items.rb",
      "item accounting stays in the item statistics source"
    )
    assert(
      runtime_location &&
        File.basename(runtime_location[0]) ==
          "008_Challenge_Statistics_3_Runtime_Integration.rb",
      "statistics engine hooks stay in the runtime integration source"
    )
  end

  def self.test_pivot_source_ownership
    policy_location = Ironmon.method(:intercept_acquisition).source_location
    runtime_location = Object.instance_method(:pbAddPokemonSilent).source_location
    storage_location = PokemonStorageScreen.instance_method(:pbWithdraw).source_location
    assert(
      policy_location &&
        File.basename(policy_location[0]) == "010_Pivot_Acquisition.rb",
      "pivot transaction policy stays in the acquisition source"
    )
    assert(
      runtime_location &&
        File.basename(runtime_location[0]) ==
          "010_Pivot_Acquisition_1_Runtime_Integration.rb",
      "acquisition wrappers stay in the runtime integration source"
    )
    assert(
      storage_location &&
        File.basename(storage_location[0]) ==
          "010_Pivot_Acquisition_2_Storage_Integration.rb",
      "PC restrictions stay in the storage integration source"
    )
  end

  def self.test_item_randomization_source_ownership
    generation_location = Ironmon.method(:item_ground_pool).source_location
    readiness_location = Ironmon.method(:prepare_item_randomization).source_location
    assert(
      generation_location &&
        File.basename(generation_location[0]) ==
          "003_Item_Randomization_Generation.rb",
      "item selection and pool policy stay in the generation source"
    )
    assert(
      readiness_location &&
        File.basename(readiness_location[0]) ==
          "003_Item_Randomization_Readiness.rb",
      "item saved-run compatibility stays in the readiness source"
    )
  end

  def self.test_base_stat_source_ownership
    generation_location = Ironmon.method(:generated_base_stats_for).source_location
    readiness_location = Ironmon.method(:prepare_base_stat_randomization).source_location
    runtime_location = GameData::Species.instance_method(:base_stats).source_location
    assert(
      generation_location &&
        File.basename(generation_location[0]) ==
          "003_Base_Stat_Randomization.rb",
      "base-stat generation stays in the generator source"
    )
    assert(
      readiness_location &&
        File.basename(readiness_location[0]) ==
          "003_Base_Stat_Randomization_Readiness.rb",
      "base-stat saved-run compatibility stays in the readiness source"
    )
    assert(
      runtime_location &&
        File.basename(runtime_location[0]) ==
          "003_Base_Stat_Randomization_Runtime_Integration.rb",
      "base-stat engine overrides stay in the runtime integration source"
    )
  end

  def self.test_area_progress_source_ownership
    state_location = Ironmon.method(:begin_area_trainer_event).source_location
    runtime_location = PokemonEncounters.instance_method(:setup).source_location
    assert(
      state_location &&
        File.basename(state_location[0]) == "022_Area_Progress_Hooks.rb",
      "area-progress state stays in the core progress source"
    )
    assert(
      runtime_location &&
        File.basename(runtime_location[0]) ==
          "022_Area_Progress_Runtime_Integration.rb",
      "area-progress engine hooks stay in the runtime integration source"
    )
  end

  def self.with_synthetic_hooks
    original_load_hooks = Ironmon.instance_variable_get(:@game_load_hooks)
    original_save_hooks = Ironmon.instance_variable_get(:@game_save_hooks)
    original_update_hooks = Ironmon.instance_variable_get(
      :@graphics_update_hooks
    )
    return yield
  ensure
    Ironmon.instance_variable_set(:@game_load_hooks, original_load_hooks)
    Ironmon.instance_variable_set(:@game_save_hooks, original_save_hooks)
    Ironmon.instance_variable_set(
      :@graphics_update_hooks, original_update_hooks
    )
  end

  def self.test_load_hook_execution
    with_synthetic_hooks do
      events = []
      Ironmon.instance_variable_set(:@game_load_hooks, [
        {
          :name => :inner,
          :before => proc { |_data| events << :before_inner; :inner_state },
          :after => proc do |_data, result, state|
            events << [:after_inner, result, state]
          end
        },
        {
          :name => :outer,
          :before => proc { |_data| events << :before_outer; :outer_state },
          :after => proc do |_data, result, state|
            events << [:after_outer, result, state]
          end
        }
      ])
      result = Ironmon.run_game_load_hooks(:save_data) do
        events << :base_load
        :load_result
      end
      assert(result == :load_result, "game-load dispatch returns the base result")
      assert(
        events == [
          :before_outer,
          :before_inner,
          :base_load,
          [:after_inner, :load_result, :inner_state],
          [:after_outer, :load_result, :outer_state]
        ],
        "game-load pre-hooks reverse and post-hooks unwind forward"
      )
    end
  end

  def self.test_save_hook_execution
    with_synthetic_hooks do
      events = []
      Ironmon.instance_variable_set(:@game_save_hooks, [
        {
          :name => :inner,
          :before => proc do |slot, auto, safe|
            events << [:before_inner, slot, auto, safe]
            :previous_slot
          end,
          :after => proc do |slot, auto, safe, result, state|
            events << [:after_inner, slot, auto, safe, result, state]
          end
        },
        {
          :name => :outer,
          :before => proc { |_slot, _auto, _safe| events << :before_outer },
          :after => nil
        }
      ])
      result = Ironmon.run_game_save_hooks("File C", false, true) do
        events << :base_save
        true
      end
      assert(result, "game-save dispatch returns the base result")
      assert(
        events == [
          :before_outer,
          [:before_inner, "File C", false, true],
          :base_save,
          [:after_inner, "File C", false, true, true, :previous_slot]
        ],
        "game-save dispatch preserves arguments, ordering, and hook state"
      )
    end
  end

  def self.test_hook_failure_semantics
    with_synthetic_hooks do
      events = []
      Ironmon.instance_variable_set(:@game_load_hooks, [
        {
          :name => :failing,
          :before => nil,
          :after => proc do |_data, _result, _state|
            events << :failing
            raise "injected hook failure"
          end
        },
        {
          :name => :unreached,
          :before => nil,
          :after => proc { |_data, _result, _state| events << :unreached }
        }
      ])
      begin
        Ironmon.run_game_load_hooks(:save_data) { events << :base_load }
        assert(false, "a load-hook failure should propagate")
      rescue RuntimeError => error
        assert(
          error.message == "injected hook failure",
          "the original load-hook error propagates"
        )
      end
      assert(
        events == [:base_load, :failing],
        "a failed nested hook prevents later outer callbacks"
      )
    end
  end

  def self.test_graphics_hook_execution
    with_synthetic_hooks do
      events = []
      Ironmon.instance_variable_set(:@graphics_update_hooks, [
        { :name => :first, :callback => proc { events << :first; 1 } },
        { :name => :second, :callback => proc { events << :second; 2 } }
      ])
      result = Ironmon.run_graphics_update_hooks
      assert(
        events == [:first, :second] && result == 2,
        "frame hooks execute forward and retain the outer wrapper return value"
      )
    end
  end

  def self.test_tracker_reused_enemy_battler
    singleton = class << Ironmon; self; end
    method_names = [
      :active?,
      :register_statistics_battler_item,
      :record_trainer_species_encounter,
      :tracker_enemy_snapshot
    ]
    original_methods = {}
    method_names.each do |name|
      original_methods[name] = singleton.instance_method(name)
    end
    variable_names = [
      :@tracker_connection,
      :@tracker_battle_id,
      :@tracker_move_menu_pokemon_id,
      :@tracker_enemy_battlers,
      :@tracker_enemy_pokemon_ids,
      :@tracker_enemy_json,
      :@tracker_enemy_move_signatures,
      :@tracker_enemy_abilities
    ]
    missing = Object.new
    original_variables = {}
    variable_names.each do |name|
      original_variables[name] = if Ironmon.instance_variable_defined?(name)
                                   Ironmon.instance_variable_get(name)
                                 else
                                   missing
                                 end
    end

    singleton.send(:define_method, :active?) { true }
    singleton.send(:define_method, :register_statistics_battler_item) { |_battler| }
    singleton.send(:define_method, :record_trainer_species_encounter) { |_battler| }
    singleton.send(:define_method, :tracker_enemy_snapshot) do |battler|
      {
        "enemy_id" => tracker_enemy_id(battler.pokemon),
        "position" => battler.index
      }
    end
    connection = TrackerLifecycleConnection.new
    Ironmon.instance_variable_set(:@tracker_connection, connection)
    Ironmon.instance_variable_set(:@tracker_battle_id, "battle-runtime-test")
    Ironmon.instance_variable_set(:@tracker_move_menu_pokemon_id, nil)
    Ironmon.instance_variable_set(:@tracker_enemy_battlers, {})
    Ironmon.instance_variable_set(:@tracker_enemy_pokemon_ids, {})
    Ironmon.instance_variable_set(:@tracker_enemy_json, {})
    Ironmon.instance_variable_set(:@tracker_enemy_move_signatures, {})
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, {})
    pokemon = Struct.new(:personalID)
    battler = Struct.new(:index, :pokemon).new(1, pokemon.new(101))

    Ironmon.tracker_enemy_sent_out(battler)
    Ironmon.tracker_player_move_menu_opened(
      Struct.new(:pokemon).new(pokemon.new(201))
    )
    battler.pokemon = pokemon.new(102)
    Ironmon.tracker_enemy_sent_out(battler)
    Ironmon.tracker_player_move_menu_opened(
      Struct.new(:pokemon).new(pokemon.new(201))
    )

    assert(
      connection.events.map { |event| event[0] } == [
        "enemy_sent_out",
        "player_move_menu_opened",
        "enemy_sent_out",
        "player_move_menu_opened"
      ],
      "a reused enemy battle slot resets repeated Fight-menu navigation"
    )
  ensure
    if original_methods
      original_methods.each do |name, implementation|
        singleton.send(:define_method, name, implementation)
      end
    end
    if original_variables
      original_variables.each do |name, value|
        if value.equal?(missing)
          Ironmon.send(:remove_instance_variable, name) if
            Ironmon.instance_variable_defined?(name)
        else
          Ironmon.instance_variable_set(name, value)
        end
      end
    end
  end

  def self.run
    test_registered_hook_order
    test_fresh_game_mode_availability
    test_uninitialized_map_scene_spriteset
    test_fresh_game_mode_entry
    test_defeat_start_over_suppression
    test_encounter_hook_source_ownership
    test_statistics_source_ownership
    test_pivot_source_ownership
    test_item_randomization_source_ownership
    test_base_stat_source_ownership
    test_area_progress_source_ownership
    test_load_hook_execution
    test_save_hook_execution
    test_hook_failure_semantics
    test_graphics_hook_execution
    test_tracker_reused_enemy_battler
    File.binwrite(OUTPUT_PATH, "runtime-hook tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonRuntimeHookTests.run
