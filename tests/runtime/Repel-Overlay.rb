module IronmonRepelOverlayRuntimeTests
  OUTPUT_PATH = $ironmon_repel_overlay_test_output_path.to_s

  def self.assert(condition, message)
    raise "Repel overlay runtime test failed: #{message}" if !condition
  end

  def self.run
    original_global = $PokemonGlobal
    overlay = nil
    begin
      $PokemonGlobal = PokemonGlobalMetadata.new
      $PokemonGlobal.ironmon_mode = true
      $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new
      $PokemonGlobal.repel = 25
      $PokemonGlobal.tempRepel = false
      assert(Ironmon.repel_steps_remaining == 25, "remaining steps use the native counter")
      assert(Ironmon.repel_overlay_visible?, "active Ironmon Repel is visible")
      assert(Ironmon.repel_overlay_text == "Repel: 25 steps", "plural duration is displayed")
      overlay = IronmonRepelOverlay.new(nil)
      assert(!overlay.disposed?, "overlay sprite is created")
      overlay.update
      $PokemonGlobal.repel = 1
      assert(Ironmon.repel_overlay_text == "Repel: 1 step", "singular duration is displayed")
      $PokemonGlobal.tempRepel = true
      assert(!Ironmon.repel_overlay_visible?, "temporary Poke Radar Repel is hidden")
      $PokemonGlobal.tempRepel = false
      $PokemonGlobal.repel = 0
      assert(!Ironmon.repel_overlay_visible?, "expired Repel is hidden")
      $PokemonGlobal.repel = 10
      $PokemonGlobal.ironmon_mode = false
      assert(!Ironmon.repel_overlay_visible?, "non-Ironmon play is hidden")
      File.binwrite(OUTPUT_PATH, "repel overlay runtime tests passed\n")
    ensure
      overlay.dispose if overlay && !overlay.disposed?
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

IronmonRepelOverlayRuntimeTests.run
