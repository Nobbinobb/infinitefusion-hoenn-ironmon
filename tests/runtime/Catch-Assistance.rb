module IronmonCatchAssistanceRuntimeTests
  OUTPUT_PATH = $ironmon_catch_assistance_test_output_path.to_s

  def self.assert(condition, message)
    raise "Catch assistance runtime test failed: #{message}" if !condition
  end

  def self.assert_close(expected, actual, tolerance, message)
    assert((expected - actual).abs <= tolerance,
           "#{message}: expected #{expected}, got #{actual}")
  end

  def self.run
    original_global = $PokemonGlobal
    begin
      $PokemonGlobal = PokemonGlobalMetadata.new
      $PokemonGlobal.ironmon_mode = true
      $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new
      wild_battle = Object.new
      def wild_battle.wildBattle?
        return true
      end
      trainer_battle = Object.new
      def trainer_battle.wildBattle?
        return false
      end
      assert(
        Ironmon.catch_assistance_battle?(wild_battle),
        "active wild battles receive assistance"
      )
      assert(
        !Ironmon.catch_assistance_battle?(trainer_battle),
        "trainer battles do not receive assistance"
      )
      assert_close(
        45.0, Ironmon.assisted_catch_rate(45, 100, 100), 0.001,
        "full-health encounters receive no assistance"
      )
      assisted_rates = [3, 30, 45, 90, 190, 255].map do |rate|
        Ironmon.assisted_catch_rate(rate, 100, 30)
      end
      assert(
        assisted_rates.each_cons(2).all? { |pair| pair[0] < pair[1] },
        "assistance preserves catch-rate ordering"
      )
      assert_close(
        90.2, assisted_rates[2], 0.1,
        "rate 45 reaches the calibrated assisted rate"
      )
      assert_close(
        255.0, assisted_rates[5], 0.001,
        "maximum catch rate remains unchanged"
      )
      assert(
        assisted_rates[4] - 190 < assisted_rates[3] - 90,
        "the bonus diminishes for higher base rates"
      )
      chance = Ironmon.capture_success_chance_percent(
        assisted_rates[2], 100, 30, :NONE
      )
      assert_close(
        38.7, chance, 0.05,
        "ordinary Poke Ball chance matches the calibrated curve"
      )
      File.binwrite(OUTPUT_PATH, "catch assistance runtime tests passed\n")
    ensure
      $PokemonGlobal = original_global
    end
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonCatchAssistanceRuntimeTests.run
