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

  def self.test_gym_leader_addition_levels
    first = 4.times.map do |index|
      Ironmon.gym_leader_addition_level([20, 24], index)
    end
    second = 4.times.map do |index|
      Ironmon.gym_leader_addition_level([20, 24], index)
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
      Ironmon.gym_leader_addition_level([20, 20], 0) == 19,
      "an equal-level authored party still excludes its highest level"
    )
  end

  def self.run
    test_scaled_level_rule
    test_battle_pokemon_uses_rule_once
    test_gym_leader_addition_levels
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
