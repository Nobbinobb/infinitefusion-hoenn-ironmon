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

  def self.test_development_mutations
    original_trainer = $Trainer
    original_system = $PokemonSystem
    original_bag = $PokemonBag
    original_global = $PokemonGlobal
    original_battle_id = Ironmon.instance_variable_get(:@tracker_battle_id)
    trainer_type = GameData::TrainerType.keys.first
    $Trainer = Player.new("Development test", trainer_type)
    $PokemonSystem = PokemonSystem.new
    $PokemonBag = PokemonBag.new
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    pokemon = Pokemon.new(:PIKACHU, 20, $Trainer, false)
    $Trainer.party = [pokemon]

    base_state = Ironmon.tracker_development_state(true, false)
    assert(
      base_state["abilities"].is_a?(Array) &&
        base_state["moves"].is_a?(Array) &&
        base_state["items"].is_a?(Array) &&
        !base_state.key?("evolutions") &&
        !base_state.key?("devolutions"),
      "base development state omits Pokemon-specific evolution work"
    )
    compact_state = Ironmon.tracker_development_state(false, false)
    assert(
      compact_state.keys.sort == ["auto_revive_enabled", "player"],
      "development mutation state omits unrelated catalogs"
    )

    $PokemonSystem.instance_variable_set(:@no_reviving, true)
    pokemon.instance_variable_set(:@hp, 0)
    pokemon.instance_variable_set(:@status, :POISON)
    Ironmon.tracker_development_heal(pokemon)
    assert(
      pokemon.hp == pokemon.totalhp && pokemon.status == :NONE,
      "Full Heal bypasses the challenge revive lock only for the operation"
    )
    assert(
      $PokemonSystem.instance_variable_get(:@no_reviving) == true,
      "Full Heal restores the challenge revive lock"
    )

    pokemon.instance_variable_set(:@hp, pokemon.totalhp / 2)
    Ironmon.tracker_development_set_level(pokemon, 30)
    assert(
      pokemon.level == 30 && pokemon.hp > 0 && pokemon.hp < pokemon.totalhp,
      "level adjustment recalculates stats and preserves damage"
    )
    Ironmon.tracker_development_set_moves(
      pokemon, ["TACKLE", "GROWL"]
    )
    assert(
      pokemon.moves.map(&:id) == [:TACKLE, :GROWL],
      "move replacement preserves the requested order"
    )
    Ironmon.tracker_development_set_ability(pokemon, "WONDERGUARD")
    assert(
      pokemon.ability_id == :WONDERGUARD,
      "ability replacement accepts an unrestricted game ability"
    )
    assert_rejected("ability replacement rejects an empty ability") do
      Ironmon.tracker_development_set_ability(pokemon, nil)
    end
    Ironmon.tracker_development_give_item("POTION", 3)
    assert(
      $PokemonBag.pbQuantity(:POTION) == 3,
      "item grant stores the full quantity"
    )

    replacement = Ironmon.tracker_development_swap_pokemon(
      pokemon, "RAICHU:0"
    )
    expected = Pokemon.new(:RAICHU, 30, $Trainer, false)
    expected.reset_moves
    assert(
      $Trainer.party[0] == replacement && replacement.species == :RAICHU &&
        replacement.level == 30,
      "lookup swap replaces the usable party Pokemon at the same level"
    )
    assert(
      replacement.moves.map(&:id) == expected.moves.map(&:id),
      "lookup swap uses the trainer-style newest level-up moves"
    )

    Ironmon.instance_variable_set(:@tracker_battle_id, "battle-test")
    assert_rejected("persistent mutations are rejected during battle") do
      Ironmon.tracker_validate_development_mutation_boundary
    end
  ensure
    $Trainer = original_trainer
    $PokemonSystem = original_system
    $PokemonBag = original_bag
    $PokemonGlobal = original_global
    Ironmon.instance_variable_set(:@tracker_battle_id, original_battle_id)
  end

  def self.run
    original_debug = $DEBUG
    original_connection = Ironmon.instance_variable_get(:@tracker_connection)
    connection = Ironmon::TrackerConnection.new
    Ironmon.instance_variable_set(:@tracker_connection, connection)
    capabilities = Ironmon::TRACKER_DIAGNOSTIC_CAPABILITIES
    assert(capabilities.length == 26, "complete game capability catalog")

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

    original_services = Ironmon.instance_variable_get(
      :@tracker_obtainability_services
    )
    unavailable_service = Object.new
    def unavailable_service.tracker_closure_unavailable?
      return true
    end
    healthy_service = Object.new
    def healthy_service.tracker_closure_unavailable?
      return false
    end
    Ironmon.instance_variable_set(
      :@tracker_obtainability_services,
      { "unavailable" => unavailable_service, "healthy" => healthy_service }
    )
    connection.send(
      :handle_message, event("tracker_connected", ["run.seed"])
    )
    assert(
      Ironmon.tracker_obtainability_services ==
        { "healthy" => healthy_service },
      "tracker reconnect retries only cached worker-unavailable calculations"
    )
    Ironmon.instance_variable_set(
      :@tracker_obtainability_services, original_services
    )

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
      Ironmon::TRACKER_DEVELOPMENT_ACTION_CAPABILITIES.keys.sort ==
        ["devolve", "evolve", "full_heal", "give_item", "set_ability",
         "set_auto_revive", "set_level", "set_moves", "swap_pokemon"],
      "complete development action mapping"
    )
    connection.send(
      :handle_message,
      event("diagnostic_access_changed", ["development.auto_revive"])
    )
    Ironmon.instance_variable_set(:@tracker_auto_revive_enabled, true)
    assert(Ironmon.tracker_auto_revive_enabled?, "live AutoRevive grant")
    connection.send(
      :handle_message, event("diagnostic_access_changed", [])
    )
    assert(
      !Ironmon.tracker_auto_revive_enabled?,
      "removing the grant disables AutoRevive"
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

    test_development_mutations
    File.binwrite(OUTPUT_PATH, "diagnostic access runtime tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  ensure
    $DEBUG = original_debug
    Ironmon.instance_variable_set(:@tracker_connection, original_connection)
  end
end

IronmonDiagnosticAccessRuntimeTests.run
