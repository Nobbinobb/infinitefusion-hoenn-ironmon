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

  def self.test_owned_ivs
    original_global = $PokemonGlobal
    original_player = Ironmon.instance_variable_get(:@tracker_player_pokemon)
    original_enemies = Ironmon.instance_variable_get(:@tracker_enemy_battlers)
    originals = {}
    [:tracker_debug_active_recipe, :tracker_pokemon_lookup_for_recipe].each do |name|
      originals[name] = Ironmon.method(name)
    end
    original_enabled = Ironmon::GameplayCollection.method(:enabled?)
    original_journal = Ironmon::GameplayCollection.method(:journal)
    connection = Ironmon.tracker_connection
    original_iv_capability = connection.method(:diagnostic_capability?)
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    pokemon = Pokemon.new(:PIKACHU, 20, nil, false)
    pokemon.iv[:HP] = 0
    pokemon.iv[:ATTACK] = 31
    pokemon.ivMaxed[:HP] = true
    Ironmon.instance_variable_set(:@tracker_player_pokemon, pokemon)
    Ironmon.instance_variable_set(:@tracker_enemy_battlers, { 0 => Struct.new(:pokemon).new(pokemon) })
    Ironmon.define_singleton_method(:tracker_debug_active_recipe) { {} }
    Ironmon.define_singleton_method(:tracker_pokemon_lookup_for_recipe) { |*_arguments| {} }
    stats = Ironmon.tracker_debug_inspect_pokemon({ "target" => "player", "section" => "stats" })["stats"]
    assert(stats["ivs"]["hp"] == 0 && stats["ivs"]["attack"] == 31, "owned IVs preserve zero and 31")
    assert(stats["effective_ivs"]["hp"] == 31, "effective IVs include Hyper Training")
    overview = Ironmon.tracker_debug_inspect_pokemon({ "target" => "player", "section" => "overview" })
    assert(overview["ivs"]["hp"] == 0 && overview["effective_ivs"]["hp"] == 31, "the IV tab is discoverable before opening Stats")
    assert(!overview.key?("stats"), "IV discovery does not calculate or transmit unrelated base stats")
    connection.define_singleton_method(:diagnostic_capability?) do |capability|
      capability != "pokemon.base_stats" && original_iv_capability.call(capability)
    end
    restricted = Ironmon.tracker_debug_inspect_pokemon({ "target" => "player", "section" => "overview" })
    assert(!restricted.key?("ivs") && !restricted.key?("effective_ivs"), "overview access alone does not reveal owned IVs")
    connection.define_singleton_method(:diagnostic_capability?, original_iv_capability)
    enemy = Ironmon.tracker_debug_inspect_pokemon({ "target" => "enemy", "enemy_position" => 0, "section" => "stats" })["stats"]
    assert(!enemy.key?("ivs") && !enemy.key?("effective_ivs"), "encountered targets do not expose IVs")
    enemy_overview = Ironmon.tracker_debug_inspect_pokemon({ "target" => "enemy", "enemy_position" => 0, "section" => "overview" })
    assert(!enemy_overview.key?("ivs") && !enemy_overview.key?("effective_ivs"), "encountered overview pages do not expose IVs")

    attempt_id = "a" * 32
    bindings = {
      "one" => { "source_run_id" => "selected", "attempt_number" => 1, "attempt_id" => attempt_id },
      "duplicate" => { "source_run_id" => "selected", "attempt_number" => 1, "attempt_id" => attempt_id },
      "other" => { "source_run_id" => "another", "attempt_number" => 1, "attempt_id" => "b" * 32 },
      "replayed_seed" => { "source_run_id" => "selected", "attempt_number" => 2, "attempt_id" => "b" * 32 },
      "invalid" => { "source_run_id" => "selected", "attempt_number" => 1, "attempt_id" => "invalid" }
    }
    journal = Object.new
    journal.define_singleton_method(:runtime_context) { |_key| bindings }
    journal.define_singleton_method(:epoch_id) { "test-epoch" }
    Ironmon::GameplayCollection.define_singleton_method(:enabled?) { true }
    Ironmon::GameplayCollection.define_singleton_method(:journal) { journal }
    recipe = { "run_id" => "selected", "result" => "lost", "collection_attempt_ids" => [], "statistics" => { "attempt_number" => 1 } }
    assert(Ironmon.tracker_owned_iv_attempt_ids(recipe).empty?, "old archives never infer an individual attempt from a replayable seed")
    assert(recipe["collection_attempt_ids"].empty?, "recovering bindings does not mutate archived recipes")
    assert(Ironmon.tracker_owned_iv_attempt_ids(Ironmon.tracker_flatten_completed_recipe(recipe)).empty?, "validated old archives do not guess collection associations")
    bindings["ambiguous"] = { "source_run_id" => "selected", "attempt_number" => 1, "attempt_id" => "c" * 32 }
    assert(Ironmon.tracker_owned_iv_attempt_ids(recipe).empty?, "ambiguous legacy run bindings never combine distinct attempts")
    $PokemonGlobal.instance_variable_set(:@ironmon_collection_attempt_association, { "epoch_id" => "test-epoch", "token" => "one" })
    assert(Ironmon.tracker_owned_iv_attempt_ids(recipe, true) == [attempt_id], "new archives capture their actual save association even when seed and attempt number collide")
    $PokemonGlobal.instance_variable_set(:@ironmon_collection_attempt_association, nil)
    assert(Ironmon.tracker_owned_iv_attempt_ids(recipe, true).empty?, "missing save associations do not fall back to another attempt")
    active = recipe.merge("active_run" => true)
    assert(Ironmon.tracker_owned_iv_attempt_ids(active).empty?, "species lookup during an active run omits archival ownership IDs")
    Ironmon::GameplayCollection.define_singleton_method(:enabled?) { false }
    persisted = recipe.merge("collection_attempt_ids" => [attempt_id])
    assert(Ironmon.tracker_owned_iv_attempt_ids(persisted) == [attempt_id], "new archives retain associations independently of the current save")
    flattened = Ironmon.tracker_flatten_completed_recipe(persisted)
    assert(flattened["collection_attempt_ids"] == [attempt_id], "validated archive recipes preserve collection associations")
  ensure
    $PokemonGlobal = original_global
    Ironmon.instance_variable_set(:@tracker_player_pokemon, original_player)
    Ironmon.instance_variable_set(:@tracker_enemy_battlers, original_enemies)
    originals.each { |name, method| Ironmon.define_singleton_method(name, method) } if originals
    Ironmon::GameplayCollection.define_singleton_method(:enabled?, original_enabled) if original_enabled
    Ironmon::GameplayCollection.define_singleton_method(:journal, original_journal) if original_journal
    connection.define_singleton_method(:diagnostic_capability?, original_iv_capability) if connection && original_iv_capability
  end

  def self.test_prepared_development_evolutions
    original_trainer = $Trainer
    original_global = $PokemonGlobal
    originals = {}
    [:tracker_debug_active_recipe, :tracker_active_fusion_assignment_recipe, :tracker_lookup_evolution_targets,
     :tracker_lookup_evolution_predecessor_page].each do |name|
      originals[name] = Ironmon.method(name)
    end
    $Trainer = Player.new("Development evolution test", GameData::TrainerType.keys.first)
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    pokemon = Pokemon.new(:B117H339, 5, $Trainer, false)
    $Trainer.party = [pokemon]
    target_assignments = []
    predecessor_assignments = []
    calls = []
    connection = Ironmon.tracker_connection
    original_capability = connection.method(:diagnostic_capability?)
    prepared_recipe = { "seed" => 1689 }
    Ironmon.define_singleton_method(:tracker_debug_active_recipe) { {} }
    Ironmon.define_singleton_method(:tracker_active_fusion_assignment_recipe) do |allow_unprepared|
      raise "Development did not request early assignment inputs" if !allow_unprepared
      calls << :recipe
      prepared_recipe
    end
    Ironmon.define_singleton_method(:tracker_lookup_evolution_targets) do |species, _recipe, obtainability, assignments|
      raise "Development evolution loading attempted a runtime scan" if
        species.id != pokemon.species || obtainability || !assignments.equal?(target_assignments)
      calls << :targets
      { :normal => [], :head => [], :body => [] }
    end
    Ironmon.define_singleton_method(:tracker_lookup_evolution_predecessor_page) do |species, _recipe, offset, limit, obtainability, assignments|
      raise "Development devolution loading attempted a runtime scan" if
        species.id != pokemon.species || offset != 0 || limit != 50 ||
        obtainability || !assignments.equal?(predecessor_assignments)
      calls << :predecessors
      { "matches" => [], "continuation" => "complete", "next_offset" => nil }
    end
    payload = {
      "use_native_evolution_assignments" => true,
      "pokemon_id" => pokemon.personalID.to_s,
      "species_id" => Ironmon.tracker_species_id(pokemon),
      "evolution_assignments" => target_assignments,
      "predecessor_assignments" => predecessor_assignments
    }
    result = Ironmon.tracker_development_state(false, true, payload)
    assert(result["evolutions"] == [] && result["devolutions"] == [],
           "empty prepared directions are complete and never trigger a fusion scan")
    assert(calls == [:targets, :predecessors],
           "development forwards both prepared assignment lists")
    calls.clear
    preflight = Ironmon.tracker_development_state(false, false, payload)
    assert(preflight["fusion_assignments"] == prepared_recipe && calls == [:recipe],
           "an explicit authorized development preflight can prepare an early fusion")
    calls.clear
    ordinary = Ironmon.tracker_development_state(false, false)
    assert(!ordinary.key?("fusion_assignments") && calls.empty?,
           "ordinary development state never adds internal generation inputs")
    connection.define_singleton_method(:diagnostic_capability?) do |capability|
      capability != "development.evolution" && original_capability.call(capability)
    end
    restricted = Ironmon.tracker_development_state(false, false, payload)
    assert(!restricted.key?("fusion_assignments") && calls.empty?,
           "other mutation grants never expose evolution preparation inputs")
    connection.define_singleton_method(:diagnostic_capability?, original_capability)
    assert_rejected("prepared choices reject a replaced individual") do
      Ironmon.tracker_development_state(false, true, payload.merge("pokemon_id" => "stale"))
    end
    assert_rejected("prepared choices reject an evolved or swapped species") do
      Ironmon.tracker_development_state(false, true, payload.merge("species_id" => "PIKACHU:0"))
    end
    assert_rejected("unprepared outgoing assignments fail before scanning") do
      Ironmon.tracker_development_state(false, true, payload.merge("evolution_assignments" => nil))
    end
    assert_rejected("unprepared incoming assignments fail before scanning") do
      Ironmon.tracker_development_state(false, true, payload.merge("predecessor_assignments" => nil))
    end
    assert(calls.empty?, "rejected assignments never enter evolution lookup")
  ensure
    $Trainer = original_trainer
    $PokemonGlobal = original_global
    originals.each { |name, method| Ironmon.define_singleton_method(name, method) } if originals
    connection.define_singleton_method(:diagnostic_capability?, original_capability) if connection && original_capability
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

    test_owned_ivs
    test_prepared_development_evolutions
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
