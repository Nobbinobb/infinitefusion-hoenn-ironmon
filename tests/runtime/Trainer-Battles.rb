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

  def self.with_trainer_policy(policy)
    configuration = Ironmon::Configuration.new(
      Ironmon::Configuration::POLICY_MIXED, policy
    )
    return with_singleton_method_stub(
      Ironmon, :configuration, proc { configuration }
    ) { yield }
  end

  def self.with_rival_story_pool_fixture
    with_singleton_method_stub(
      Ironmon, :normal_species_pool, proc { [:BULBASAUR, :IVYSAUR] }
    ) do
      with_singleton_method_stub(
        Ironmon, :custom_fusion_pool, proc { [:B1H2] }
      ) do
        return with_singleton_method_stub(
          Ironmon, :progression_random_value, proc { |_context, _index| 0 }
        ) { yield }
      end
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

  def self.test_rival_story_policy_split
    with_rival_story_pool_fixture do
      plan = Ironmon.rival_story_fusion_plan
      assert(
        plan == {
          :fusion => :B1H2, :body => :BULBASAUR, :head => :IVYSAUR
        },
        "the rival's story plan exposes the selected custom fusion materials"
      )
      expected = {
        Ironmon::Configuration::POLICY_NORMAL_ONLY => :BULBASAUR,
        Ironmon::Configuration::POLICY_MIXED => :BULBASAUR,
        Ironmon::Configuration::POLICY_CUSTOM_FUSIONS_ONLY => :B1H2
      }
      expected.each do |policy, species|
        with_trainer_policy(policy) do
          assert(
            Ironmon.rival_story_initial_species == species,
            "#{policy} selects the intended persistent rival starter"
          )
        end
      end
    end
  end

  def self.test_rival_story_generation_version_gate
    with_ironmon_active(true) do
      with_singleton_method_stub(
        Ironmon, :generation_profile_algorithm_version,
        proc { |_name| 1 }
      ) do
        assert(
          !Ironmon.rival_story_generation_enabled?,
          "version 1 retains the prior rival story path"
        )
      end
      with_singleton_method_stub(
        Ironmon, :generation_profile_algorithm_version,
        proc { |_name| 2 }
      ) do
        assert(
          Ironmon.rival_story_generation_enabled?,
          "version 2 enables the persistent policy-specific rival starter"
        )
      end
    end
  end

  def self.test_rival_story_starter_is_position_independent
    starter = Ironmon.mark_rival_story_starter(
      Pokemon.new(:BULBASAUR, 5)
    )
    trainer = Struct.new(:currentTeam).new([
      Pokemon.new(:RATTATA, 5), starter, Pokemon.new(:PIDGEY, 5)
    ])
    assert(
      Ironmon.rival_story_starter(trainer).equal?(starter),
      "the marked rival starter is found independently of its party slot"
    )
    assert(
      Ironmon.persistent_trainer_species?(starter) &&
        Ironmon.trainer_maturity_exception?(starter),
      "the rival starter persists and keeps ordinary level-based evolution"
    )
  end

  def self.test_rival_story_second_battle_policy_transition
    with_rival_story_pool_fixture do
      policies = {
        Ironmon::Configuration::POLICY_NORMAL_ONLY => :BULBASAUR,
        Ironmon::Configuration::POLICY_MIXED => :B1H2,
        Ironmon::Configuration::POLICY_CUSTOM_FUSIONS_ONLY => :B1H2
      }
      policies.each do |policy, expected_species|
        initial_species = if policy ==
                            Ironmon::Configuration::POLICY_CUSTOM_FUSIONS_ONLY
                            :B1H2
                          else
                            :BULBASAUR
                          end
        starter = Ironmon.mark_rival_story_starter(
          Pokemon.new(initial_species, 5)
        )
        trainer = Struct.new(:currentTeam).new([
          Pokemon.new(:RATTATA, 12), Pokemon.new(:PIDGEY, 15)
        ])
        with_trainer_policy(policy) do
          Ironmon.restore_rival_story_starter_after_second_battle(
            trainer, starter
          )
        end
        assert(
          trainer.currentTeam[-1].equal?(starter),
          "#{policy} carries the same rival starter instance forward"
        )
        assert(
          starter.species == expected_species && starter.level == 15,
          "#{policy} applies the intended second-battle fusion behavior"
        )
      end
    end
  end

  def self.test_rival_story_third_battle_carries_base_evolution
    starter = Ironmon.mark_rival_story_starter(
      Pokemon.new(:BULBASAUR, 15)
    )
    evolved_replacement = Pokemon.new(:IVYSAUR, 24)
    trainer = Struct.new(:currentTeam).new([
      Pokemon.new(:RATTATA, 22), Pokemon.new(:PIDGEY, 22),
      evolved_replacement
    ])
    Ironmon.restore_rival_story_starter_after_third_battle(trainer, starter)
    assert(
      trainer.currentTeam[-1].equal?(starter),
      "the third battle retains the marked rival starter instance"
    )
    assert(
      starter.species == :IVYSAUR && starter.level == 24,
      "the persistent rival starter receives the scripted evolution result"
    )
  end

  def self.test_dynamic_wally_party_receives_boss_additions
    trainer_class = Struct.new(:currentTeam, :trainerType, :trainerName)
    wally = trainer_class.new([
      Pokemon.new(:BULBASAUR, 10), Pokemon.new(:IVYSAUR, 12)
    ], :RIVAL2, "Wally")
    rival = trainer_class.new([
      Pokemon.new(:BULBASAUR, 10)
    ], :RIVAL1, "Rival")
    with_ironmon_active(true) do
      with_singleton_method_stub(
        Ironmon, :current_trainer_party_expansion_version, proc { 2 }
      ) do
        with_singleton_method_stub(
          Ironmon, :boss_trainer_species_for_slot,
          proc { |_trainer, _slot, _level, _version| :RATTATA }
        ) do
          Ironmon.expand_dynamic_boss_trainer_party(wally)
          Ironmon.expand_dynamic_boss_trainer_party(rival)
        end
      end
    end
    assert(wally.currentTeam.length == 5, "Wally receives three boss additions")
    assert(
      wally.currentTeam.last(3).map(&:level) == [15, 16, 17],
      "Wally's additions use the final scaled authored level range"
    )
    assert(
      wally.currentTeam.last(3).all? do |pokemon|
        pokemon.instance_variable_get(:@ironmon_level_scaled) == true
      end,
      "Wally's additions are not scaled a second time"
    )
    assert(
      rival.currentTeam.length == 1,
      "the Hoenn main rival receives no boss additions"
    )
  end

  def self.run
    test_scoped_one_against_two_rule
    test_story_partner_retains_two_against_two
    test_simultaneous_trainers_are_filtered
    test_authored_pair_retains_both_trainers
    test_battle_engine_accepts_one_against_two
    test_authored_pair_queues_both_area_entries
    test_rival_story_policy_split
    test_rival_story_generation_version_gate
    test_rival_story_starter_is_position_independent
    test_rival_story_second_battle_policy_transition
    test_rival_story_third_battle_carries_base_evolution
    test_dynamic_wally_party_receives_boss_additions
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
