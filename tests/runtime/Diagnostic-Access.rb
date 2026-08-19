module IronmonDiagnosticAccessRuntimeTests
  OUTPUT_PATH = $ironmon_diagnostic_access_test_output_path.to_s

  def self.assert(condition, message)
    raise "Diagnostic access runtime test failed: #{message}" if !condition
  end

  def self.event(name, capabilities)
    return {
      "schema_version" => Ironmon::TRACKER_SCHEMA_VERSION,
      "type" => "event",
      "event" => name,
      "payload" => {
        "debug_requested" => false,
        "diagnostic_capabilities" => capabilities
      }
    }
  end

  def self.assert_rejected(message)
    rejected = false
    begin
      yield
    rescue Ironmon::TrackerDebugError
      rejected = true
    end
    assert(rejected, message)
  end

  def self.run
    original_debug = $DEBUG
    connection = Ironmon::TrackerConnection.new
    capabilities = Ironmon::TRACKER_DIAGNOSTIC_CAPABILITIES
    assert(capabilities.length == 18, "complete game capability catalog")

    capabilities.each_with_index do |capability, index|
      connection.send(
        :handle_message, event("tracker_connected", [capability])
      )
      assert(
        connection.diagnostic_capability?(capability),
        "#{capability} allow path"
      )
      other = capabilities[(index + 1) % capabilities.length]
      assert(
        !connection.diagnostic_capability?(other),
        "#{capability} does not imply #{other}"
      )
      connection.send(:require_diagnostic_capabilities, [capability])
      assert_rejected("#{other} deny path") do
        connection.send(:require_diagnostic_capabilities, [other])
      end
    end

    connection.send(
      :handle_message,
      event("diagnostic_access_changed", ["run.seed", "run.seed"])
    )
    assert(
      !connection.diagnostic_capability?("run.seed"),
      "duplicate replacement fails closed"
    )
    connection.send(
      :handle_message,
      event("diagnostic_access_changed", ["unsupported.future"])
    )
    assert(
      !connection.diagnostic_capability?("unsupported.future"),
      "unknown replacement fails closed"
    )

    connection.send(
      :handle_message,
      event("diagnostic_access_changed", ["run.seed"])
    )
    assert(connection.diagnostic_capability?("run.seed"), "live grant")
    connection.send(
      :handle_message, event("diagnostic_access_changed", [])
    )
    assert(
      !connection.diagnostic_capability?("run.seed"),
      "removal or expiration clears the live grant"
    )

    assert(
      connection.send(:area_diagnostic_capability, "encounter") ==
        "world.wild_encounters",
      "encounter area mapping"
    )
    assert(
      connection.send(:area_diagnostic_capability, "trainer") ==
        "world.trainer_parties",
      "trainer area mapping"
    )
    assert(
      connection.send(:area_diagnostic_capability, "item") == "world.items",
      "item area mapping"
    )
    assert(
      connection.send(
        :pokemon_source_diagnostic_capability, { "target" => "player" }
      ) == "pokemon.current_player",
      "target-bound player mapping"
    )
    assert(
      connection.send(
        :pokemon_source_diagnostic_capability, { "target" => "enemy" }
      ) == "pokemon.current_enemies",
      "target-bound enemy mapping"
    )
    assert(
      connection.send(:pokemon_source_diagnostic_capability, {}) ==
        "pokemon.all_active",
      "target-free arbitrary mapping"
    )
    assert_rejected("party target is not current-player access") do
      connection.send(
        :inspection_diagnostic_capability, { "target" => "party" }
      )
    end
    assert(
      Ironmon.tracker_information_diagnostic_capabilities("evolutions") ==
        ["evolution.results", "evolution.candidates"],
      "split evolution surfaces"
    )

    $DEBUG = true
    connection.instance_variable_set(:@debug_requested, true)
    capabilities.each do |capability|
      assert(
        connection.diagnostic_capability?(capability),
        "developer override grants #{capability}"
      )
    end

    File.binwrite(OUTPUT_PATH, "diagnostic access runtime tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  ensure
    $DEBUG = original_debug
  end
end

IronmonDiagnosticAccessRuntimeTests.run
