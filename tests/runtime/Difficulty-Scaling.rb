module IronmonDifficultyScalingRuntimeTests
  OUTPUT_PATH = $ironmon_difficulty_scaling_test_output_path.to_s

  EXPECTED_LEVELS = {
    1 => 2,
    2 => 3,
    10 => 15,
    11 => 16,
    12 => 18,
    13 => 20,
    25 => 38,
    40 => 60,
    41 => 62,
    42 => 63,
    43 => 64,
    66 => 99,
    67 => 100,
    100 => 100
  }.freeze

  def self.assert(condition, message)
    raise "Difficulty scaling runtime test failed: #{message}" if !condition
  end

  def self.with_ironmon_active(value)
    singleton = class << Ironmon; self; end
    singleton.send(
      :alias_method, :difficulty_scaling_test_original_active, :active?
    )
    singleton.send(:define_method, :active?) { value }
    return yield
  ensure
    if singleton && singleton.method_defined?(:difficulty_scaling_test_original_active)
      singleton.send(:alias_method, :active?, :difficulty_scaling_test_original_active)
      singleton.send(:remove_method, :difficulty_scaling_test_original_active)
    end
  end

  def self.with_terminal_fusion_pool_fixture
    original = Ironmon.instance_variable_get(:@custom_fusion_pool_service)
    terminal = Ironmon.fully_evolved_normal_species_pool.first(2).map do |species|
      GameData::Species.get(species).id_number
    end
    fusion = :"B#{terminal[0]}H#{terminal[1]}"
    service = Object.new
    service.define_singleton_method(:pool) { [fusion].freeze }
    Ironmon.instance_variable_set(:@custom_fusion_pool_service, service)
    Ironmon.instance_variable_set(:@fully_evolved_custom_fusion_pool, nil)
    Ironmon.instance_variable_set(:@fully_evolved_custom_fusion_index, nil)
    return yield
  ensure
    Ironmon.instance_variable_set(:@custom_fusion_pool_service, original)
    Ironmon.instance_variable_set(:@fully_evolved_custom_fusion_pool, nil)
    Ironmon.instance_variable_set(:@fully_evolved_custom_fusion_index, nil)
  end

  def self.test_scaled_level_rule
    assert(
      Ironmon::LEVEL_MULTIPLIER == 1.5,
      "level multiplier is 1.5"
    )
    EXPECTED_LEVELS.each do |source, expected|
      actual = Ironmon.scaled_level(source)
      assert(
        actual == expected,
        "level #{source} scales to #{expected}, got #{actual}"
      )
      assert(actual.is_a?(Integer), "scaled level #{actual} is an integer")
    end
  end

  def self.test_battle_pokemon_uses_rule_once
    pokemon = Pokemon.new(:PIKACHU, 10)
    with_ironmon_active(true) do
      Ironmon.scale_battle_pokemon(pokemon)
      assert(pokemon.level == 15, "battle Pokemon uses the scaled level")
      Ironmon.scale_battle_pokemon(pokemon)
      assert(pokemon.level == 15, "battle Pokemon is not scaled twice")
    end
  end

  def self.test_player_level_override_is_not_scaled_again
    saved_switches = $game_switches
    saved_variables = $game_variables
    $game_switches = Game_Switches.new
    $game_variables = Game_Variables.new
    $game_switches[Settings::OVERRIDE_BATTLE_LEVEL_SWITCH] = true
    $game_variables[Settings::OVERRIDE_BATTLE_LEVEL_VALUE_VAR] = 26
    pokemon = Pokemon.new(:PIKACHU, 26)
    with_ironmon_active(true) do
      Ironmon.scale_battle_pokemon(pokemon)
      assert(pokemon.level == 26, "player-matched battle level is not multiplied by 1.5")
      $game_switches[Settings::OVERRIDE_BATTLE_LEVEL_SWITCH] = false
      Ironmon.scale_battle_pokemon(pokemon)
      assert(pokemon.level == 26, "player-matched party remains exempt after its wrapper clears the override")
    end
  ensure
    $game_switches = saved_switches
    $game_variables = saved_variables
  end

  def self.test_boss_trainer_addition_levels
    first = 4.times.map do |index|
      Ironmon.boss_trainer_addition_level([20, 24], index)
    end
    second = 4.times.map do |index|
      Ironmon.boss_trainer_addition_level([20, 24], index)
    end
    assert(
      first == [20, 21, 22, 23],
      "gym additions fill the authored range below its highest level"
    )
    assert(first == second, "gym addition levels are deterministic")
    assert(
      first.all? { |level| level >= 20 && level < 24 },
      "gym additions include the lowest level but exclude the highest"
    )
    assert(
      Ironmon.boss_trainer_addition_level([20, 20], 0) == 19,
      "an equal-level boss party still excludes its highest level"
    )
  end

  def self.test_boss_party_size_rule
    trainer = Struct.new(:trainer_type).new(:LEADER_Roxanne)
    expected = { 1 => 4, 2 => 5, 3 => 6, 4 => 6, 5 => 6, 6 => 6 }
    expected.each do |authored_size, target_size|
      assert(
        Ironmon.trainer_party_expansion_target(trainer, authored_size) ==
          target_size,
        "a #{authored_size}-Pokemon boss party expands to #{target_size}"
      )
    end
    ordinary = Struct.new(:trainer_type).new(:YOUNGSTER)
    assert(
      Ironmon.trainer_party_expansion_target(ordinary, 2).nil?,
      "ordinary trainers do not receive boss additions"
    )
    rival = Struct.new(:trainer_type).new(:RIVAL1)
    assert(
      Ironmon.trainer_party_expansion_target(rival, 1).nil?,
      "the Hoenn main rival does not receive boss additions"
    )
    wally = Struct.new(:trainer_type).new(:RIVAL2)
    assert(
      Ironmon.trainer_party_expansion_target(wally, 1) == 4,
      "Wally remains eligible for three boss additions"
    )
    assert(
      Ironmon.trainer_party_expansion_target(trainer, 1, 1) == 6,
      "version-1 Gym Leaders retain the old fill-to-six rule"
    )
    champion = Struct.new(:trainer_type).new(:CHAMPION_Steven)
    assert(
      Ironmon.trainer_party_expansion_target(champion, 3, 1).nil?,
      "version-1 non-Gym bosses retain their unexpanded parties"
    )
  end

  def self.test_fully_evolved_level_rule
    assert(
      !Ironmon.trainer_requires_fully_evolved_species?(29),
      "level 29 trainer Pokemon retain the unrestricted pool"
    )
    assert(
      Ironmon.trainer_requires_fully_evolved_species?(30),
      "level 30 trainer Pokemon use the fully evolved pool"
    )
    pokemon = Pokemon.new(:PIKACHU, 30)
    Ironmon.mark_trainer_maturity_exception(pokemon)
    assert(
      Ironmon.trainer_maturity_exception?(pokemon),
      "an event-owned trainer Pokemon can retain normal evolution"
    )
  end

  def self.test_fully_evolved_species_pools
    assert(
      Ironmon.fully_evolved_normal_species_pool.all? do |species|
        Ironmon.fully_evolved_trainer_species?(species)
      end,
      "every fully evolved normal-pool entry is terminal"
    )
    with_terminal_fusion_pool_fixture do
      assert(
        Ironmon.fully_evolved_custom_fusion_pool.all? do |species|
          Ironmon.fully_evolved_trainer_species?(species)
        end,
        "every fully evolved fusion-pool entry has terminal components"
      )
      policies = [
        Ironmon::Configuration::POLICY_NORMAL_ONLY,
        Ironmon::Configuration::POLICY_CUSTOM_FUSIONS_ONLY,
        Ironmon::Configuration::POLICY_MIXED
      ]
      policies.each do |policy|
        generator = Ironmon::SpeciesGenerator.new(
          123, :trainer, policy, Ironmon.normal_species_pool,
          Ironmon.custom_fusion_pool, {}, Ironmon::SpeciesGenerator::SCHEMA_VERSION
        )
        10.times do |slot|
          mapped = generator.map(:BULBASAUR, [:terminal_test, slot], true)
          assert(
            Ironmon.fully_evolved_trainer_species?(mapped),
            "#{policy} level-30 mappings select only terminal species"
          )
        end
      end
      legacy = Ironmon::SpeciesGenerator.new(
        123, :trainer, Ironmon::Configuration::POLICY_NORMAL_ONLY,
        Ironmon.normal_species_pool, Ironmon.custom_fusion_pool, {}, 1
      )
      ordinary = legacy.map(:BULBASAUR, [:legacy_terminal_test], false)
      requested_terminal = legacy.map(
        :BULBASAUR, [:legacy_terminal_test], true
      )
      assert(
        ordinary == requested_terminal,
        "version-1 species mappings retain their original unrestricted rule"
      )
    end
  end

  def self.run
    test_scaled_level_rule
    test_battle_pokemon_uses_rule_once
    test_player_level_override_is_not_scaled_again
    test_boss_trainer_addition_levels
    test_boss_party_size_rule
    test_fully_evolved_level_rule
    test_fully_evolved_species_pools
    File.binwrite(OUTPUT_PATH, "difficulty scaling runtime tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonDifficultyScalingRuntimeTests.run
