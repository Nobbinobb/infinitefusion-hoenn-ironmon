module IronmonTrainerBattleRuntimeTests
  OUTPUT_PATH = $ironmon_trainer_battle_test_output_path.to_s

  def self.assert(condition, message)
    raise "Trainer battle runtime test failed: #{message}" if !condition
  end

  def self.with_ironmon_active(value)
    singleton = class << Ironmon; self; end
    singleton.send(
      :alias_method, :trainer_battle_test_original_active, :active?
    )
    singleton.send(:define_method, :active?) { value }
    return yield
  ensure
    if singleton && singleton.method_defined?(:trainer_battle_test_original_active)
      singleton.send(:alias_method, :active?, :trainer_battle_test_original_active)
      singleton.send(:remove_method, :trainer_battle_test_original_active)
    end
  end

  def self.with_object_method_stubs(stubs)
    backups = []
    stubs.each do |name, implementation|
      backup = :"trainer_battle_test_original_#{name}"
      Object.send(:alias_method, backup, name)
      Object.send(:define_method, name, implementation)
      Object.send(:private, name)
      backups << [name, backup]
    end
    return yield
  ensure
    backups.to_a.reverse_each do |name, backup|
      Object.send(:alias_method, name, backup)
      Object.send(:remove_method, backup)
      Object.send(:private, name)
    end
  end

  def self.with_singleton_method_stub(owner, name, implementation)
    singleton = class << owner; self; end
    backup = :"trainer_battle_test_original_#{name}"
    singleton.send(:alias_method, backup, name)
    singleton.send(:define_method, name, implementation)
    return yield
  ensure
    if singleton && singleton.method_defined?(backup)
      singleton.send(:alias_method, name, backup)
      singleton.send(:remove_method, backup)
    end
  end

  def self.test_scoped_one_against_two_rule
    original_temp = $PokemonTemp
    $PokemonTemp = PokemonTemp.new
    setBattleRule("double")
    assert(
      $PokemonTemp.battleRules["size"] == "double",
      "ordinary double rule is unchanged outside the scoped policy"
    )
    $PokemonTemp.clearBattleRules
    Ironmon.with_trainer_battle_format_policy(true) do
      setBattleRule("double")
    end
    assert(
      $PokemonTemp.battleRules["size"] == "1v2",
      "authored double rule becomes an explicit 1v2"
    )
  ensure
    $PokemonTemp = original_temp
  end

  def self.test_story_partner_retains_two_against_two
    original_temp = $PokemonTemp
    original_global = $PokemonGlobal
    $PokemonTemp = PokemonTemp.new
    $PokemonGlobal = Struct.new(:partner).new([:IRONMON_TEST_PARTNER])
    Ironmon.with_trainer_battle_format_policy(true) do
      setBattleRule("double")
    end
    assert(
      $PokemonTemp.battleRules["size"] == "double",
      "a story partner keeps the authored battle at 2v2"
    )
    assert(
      !$PokemonTemp.battleRules["noPartner"],
      "the trainer policy does not suppress the story partner"
    )
  ensure
    $PokemonTemp = original_temp
    $PokemonGlobal = original_global
  end

  def self.test_simultaneous_trainers_are_filtered
    player = Game_Player.allocate
    result = Ironmon.with_trainer_battle_format_policy do
      player.pbTriggeredTrainerEvents([2], false)
    end
    assert(result == [], "the double-trainer recruitment query is filtered")
    assert(
      !Ironmon.separate_simultaneous_trainers?,
      "the simultaneous-trainer filter is restored after the battle call"
    )
  end

  def self.test_authored_pair_retains_both_trainers
    original_temp = $PokemonTemp
    original_map = $game_map
    $PokemonTemp = PokemonTemp.new
    $game_map = nil
    pre_battle = []
    double_arguments = []
    rematch_arguments = []
    trainer_data = Object.new
    stubs = {
      :displayPreBattleText => proc { |data| pre_battle << data },
      :pbDoubleTrainerBattle => proc do |*arguments|
        double_arguments << arguments
        setBattleRule("double")
        true
      end,
      :updateRematchableTrainer => proc do |*arguments|
        rematch_arguments << arguments
      end
    }
    trainers = [
      [:IRONMON_TEST_LEFT, "Left", 12],
      [:IRONMON_TEST_RIGHT, "Right", 13]
    ]
    get_stub = proc { |_type, _name, _version| trainer_data }
    result = with_ironmon_active(true) do
      with_singleton_method_stub(GameData::Trainer, :get, get_stub) do
        with_object_method_stubs(stubs) do
          pbMultiTrainerBattle(trainers, true, 7)
        end
      end
    end
    assert(result, "authored pair returns the shared battle result")
    assert(pre_battle == [trainer_data], "first trainer introduction is retained")
    assert(double_arguments.length == 1, "one shared trainer battle starts")
    assert(
      double_arguments[0] == [
        :IRONMON_TEST_LEFT, "Left", 0, nil,
        :IRONMON_TEST_RIGHT, "Right", 0, nil,
        true, 7
      ],
      "both authored trainers and battle options reach the shared battle"
    )
    assert(
      $PokemonTemp.battleRules["size"] == "1v2",
      "the shared battle receives the asymmetric field size"
    )
    if Settings::GAME_ID == :IF_HOENN
      assert(
        rematch_arguments == [
          [:IRONMON_TEST_LEFT, "Left", 0, 12, 13],
          [:IRONMON_TEST_RIGHT, "Right", 0, 13, 12]
        ],
        "both linked trainer events retain their completion bookkeeping"
      )
    end
  ensure
    $PokemonTemp = original_temp
    $game_map = original_map
    Ironmon.cancel_area_trainer_event
  end

  def self.test_battle_engine_accepts_one_against_two
    original_system = $PokemonSystem
    $PokemonSystem = PokemonSystem.new
    trainer_type = nil
    GameData::TrainerType.each do |entry|
      trainer_type = entry.id
      break
    end
    player = Player.new("Solo", trainer_type)
    left = NPCTrainer.new("Left", trainer_type)
    right = NPCTrainer.new("Right", trainer_type)
    player.party << Pokemon.new(:PIKACHU, 20, player)
    left.party << Pokemon.new(:RATTATA, 20, left)
    right.party << Pokemon.new(:PIDGEY, 20, right)
    battle = PokeBattle_Battle.new(
      Object.new, player.party, left.party + right.party,
      player, [left, right]
    )
    battle.party1starts = [0]
    battle.party2starts = [0, left.party.length]
    battle.setBattleMode("1v2")
    battle.pbEnsureParticipants
    assert(
      [battle.pbSideSize(0), battle.pbSideSize(1)] == [1, 2],
      "battle engine keeps one player slot and two opposing slots"
    )
    assert(battle.opponent.length == 2, "both opposing trainers remain attached")
  ensure
    $PokemonSystem = original_system
  end

  def self.test_authored_pair_queues_both_area_entries
    original_map = $game_map
    $game_map = nil
    references = []
    queued_entries = []
    trainers = [
      [:IRONMON_TEST_LEFT, "Left", 12],
      [:IRONMON_TEST_RIGHT, "Right", 13]
    ]
    reference_stub = proc do |category, event_id, map_id|
      references << [category, event_id, map_id]
      "#{category}:#{event_id}"
    end
    queue_stub = proc { |entry_ids| queued_entries << entry_ids }
    inner_stub = proc { |_trainers, _can_lose, _outcome_var| true }
    result = with_ironmon_active(true) do
      with_singleton_method_stub(
        Ironmon, :current_area_event_entry_id, reference_stub
      ) do
        with_singleton_method_stub(
          Ironmon, :queue_area_trainer_entries, queue_stub
        ) do
          with_object_method_stubs(
            :ironmon_area_progress_original_pb_multi_trainer_battle =>
              inner_stub
          ) do
            pbMultiTrainerBattle(trainers, false, 1)
          end
        end
      end
    end
    assert(result, "area-progress wrapper returns the shared battle result")
    assert(
      references == [
        ["trainers", 12, nil],
        ["trainers", 13, nil]
      ],
      "both authored trainer event identities are resolved"
    )
    assert(
      queued_entries == [["trainers:12", "trainers:13"]],
      "both authored trainer entries are queued for shared victory"
    )
  ensure
    $game_map = original_map
  end

  def self.run
    test_scoped_one_against_two_rule
    test_story_partner_retains_two_against_two
    test_simultaneous_trainers_are_filtered
    test_authored_pair_retains_both_trainers
    test_battle_engine_accepts_one_against_two
    test_authored_pair_queues_both_area_entries
    File.binwrite(OUTPUT_PATH, "trainer battle runtime tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonTrainerBattleRuntimeTests.run
