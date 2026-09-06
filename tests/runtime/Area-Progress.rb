module IronmonAreaProgressRuntimeTests
  class DiscoveryConnection
    attr_reader :packages
    attr_reader :run_started_count

    def initialize(connected = true)
      @connected = connected
      @packages = []
      @run_started_count = 0
    end

    def connected?
      return @connected
    end

    def send_area_discovery(area_id, category, entry_keys)
      return false if !@connected
      @packages << [area_id, category, entry_keys]
      return true
    end

    def send_run_started
      @run_started_count += 1 if @connected
    end
  end

  def self.assert(condition, message)
    raise "Area discovery runtime test failed: #{message}" if !condition
  end

  def self.active_attempt
    return {
      "attempt_number" => 1,
      "run_id" => "runtime-test",
      "generation_profile_id" => Ironmon.current_generation_profile_id,
      "seed" => 1,
      "result" => "active",
      "active_seconds" => 0.0,
      "statistics" => nil
    }
  end

  def self.run
    original_global = $PokemonGlobal
    original_switches = $game_switches
    original_self_switches = $game_self_switches
    original_game_map = $game_map
    original_trainer = $Trainer
    original_encounters = $PokemonEncounters
    original_connection = Ironmon.instance_variable_get(:@tracker_connection)
    original_pool_service = Ironmon.instance_variable_get(
      :@custom_fusion_pool_service
    )
    begin
      $PokemonGlobal = PokemonGlobalMetadata.new
      $PokemonGlobal.ironmon_mode = true
      $PokemonGlobal.ironmon_seed = 1
      $PokemonGlobal.ironmon_run_id = "runtime-test"
      $PokemonGlobal.ironmon_generation_profile_id =
        Ironmon.current_generation_profile_id
      $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new(
        :normal_only, :normal_only
      )
      $PokemonGlobal.ironmon_species_generator_version =
        Ironmon::SpeciesGenerator::SCHEMA_VERSION
      $PokemonGlobal.encounter_version = 0
      $game_switches = []
      $game_self_switches = {}
      $game_map = Struct.new(:map_id).new(10)
      $Trainer = Object.new
      def $Trainer.first_pokemon
        return nil
      end
      def $Trainer.make_foreign_ID
        return 1
      end
      def $Trainer.name
        return "Runtime Test"
      end
      ledger = Ironmon.default_run_ledger
      ledger["current_attempt"] = active_attempt
      $PokemonGlobal.ironmon_run_ledger = ledger
      pool_service = Object.new
      def pool_service.pool
        return [:B1H2, :B2H1]
      end
      Ironmon.instance_variable_set(:@custom_fusion_pool_service, pool_service)
      Ironmon.reset_species_generator_cache

      trainer_entries = Ironmon.tracker_area_catalog.find do |area|
        area["map_ids"].include?(10)
      end["trainers"]
      trainer_entry = trainer_entries[0]["entry_id"]
      second_trainer_entry = trainer_entries[1]["entry_id"]
      item_entry = Ironmon.tracker_area_event_entry_id("items", 10, 19)
      assert(!trainer_entry.to_s.empty?, "trainer event lookup")
      assert(item_entry == "item:10:19", "item event lookup")

      connection = DiscoveryConnection.new
      Ironmon.instance_variable_set(:@tracker_connection, connection)
      Ironmon.refresh_tracker_run_after_load
      assert(
        connection.run_started_count == 1,
        "loading a save refreshes an already-connected tracker"
      )
      Ironmon.begin_area_trainer_event(trainer_entry)
      Ironmon.finish_area_trainer_event(false, true)
      assert(connection.packages.empty?, "waiting trainer is not discovered")
      Ironmon.begin_area_trainer_event(second_trainer_entry)
      Ironmon.finish_area_trainer_event(true, false)
      trainer_package = connection.packages.shift
      assert(trainer_package[0] == "area:10", "trainer area key")
      assert(trainer_package[1] == "trainer", "trainer category")
      assert(
        trainer_package[2].sort ==
          [trainer_entry, second_trainer_entry].sort,
        "simultaneous trainer discovery keys"
      )

      Ironmon.begin_area_item_event(item_entry)
      Ironmon.record_current_area_item(:POTION)
      Ironmon.finish_area_item_event(true, :ANTIDOTE)
      item_package = connection.packages.shift
      assert(item_package == ["area:10", "item", [item_entry]],
             "item discovery package contains keys only")
      assert(
        !Ironmon.current_run_attempt.key?("area_progress"),
        "game attempt does not persist area discoveries"
      )

      area = Ironmon.tracker_area_catalog.find do |candidate|
        candidate["map_ids"].include?(10)
      end
      $game_switches[SWITCH_RANDOM_TRAINERS] = true
      $game_switches[SWITCH_FIRST_RIVAL_BATTLE] = false
      $game_switches[SWITCH_IS_REMATCH] = false
      $game_switches[SWITCH_REVERSED_MODE] = false
      $game_switches[SWITCH_SINGLE_POKEMON_MODE] = false
      active_recipe = Ironmon.tracker_active_area_recipe(active_attempt)
      trainer_index = Ironmon.tracker_area_trainer_index(active_recipe)
      catalog_trainer = area["trainers"].find do |entry|
        trainer_data = trainer_index[[
          entry["trainer_type"], entry["trainer_name"], entry["party_id"]
        ]]
        trainer_data && !Ironmon.gym_leader?(trainer_data)
      end
      trainer_data = trainer_index[[
        catalog_trainer["trainer_type"], catalog_trainer["trainer_name"],
        catalog_trainer["party_id"]
      ]]
      runtime_party = trainer_data.to_trainer.party.map do |pokemon|
        [pokemon.species, pokemon.level]
      end
      lookup_party = Ironmon.tracker_area_trainer_party(
        trainer_data, active_recipe,
        Ironmon.tracker_area_species_generator(active_recipe, :trainer)
      ).map do |pokemon|
        [pokemon["species"].id, pokemon["level"]]
      end
      assert(
        lookup_party == runtime_party,
        "active trainer lookup matches the battle party transformation"
      )
      boss_data = trainer_index.values.find do |candidate|
        Ironmon.boss_trainer?(candidate) &&
          ![:RIVAL1, :RIVAL2].include?(candidate.trainer_type)
      end
      assert(boss_data, "the trainer catalog contains a Boss Trainer")
      runtime_boss = boss_data.to_trainer
      authored_boss_size = runtime_boss.party.length
      Ironmon.expand_boss_trainer_party(runtime_boss)
      lookup_boss = Ironmon.tracker_area_trainer_party(
        boss_data, active_recipe,
        Ironmon.tracker_area_species_generator(active_recipe, :trainer)
      )
      runtime_boss_party = runtime_boss.party.map do |pokemon|
        [pokemon.species, pokemon.level]
      end
      lookup_boss_party = lookup_boss.map do |pokemon|
        [pokemon["species"].id, pokemon["level"]]
      end
      assert(
        lookup_boss_party == runtime_boss_party,
        "active trainer lookup matches expanded boss parties"
      )
      assert(
        lookup_boss.length == [authored_boss_size + 3, 6].min,
        "trainer lookup adds three Boss Trainer Pokemon up to six"
      )
      assert(
        lookup_boss.all? do |pokemon|
          pokemon["level"] < Ironmon::TRAINER_FULLY_EVOLVED_LEVEL ||
            Ironmon.fully_evolved_trainer_species?(pokemon["species"])
        end,
        "trainer lookup exposes only terminal species at level 30 or above"
      )
      completed_trainer = trainer_entries[0]
      completed_item = area["items"].find do |entry|
        entry["entry_id"] == item_entry
      end
      $game_self_switches[[
        completed_trainer["map_id"], completed_trainer["event_id"], "A"
      ]] = true
      $game_self_switches[[
        completed_item["map_id"], completed_item["event_id"], "A"
      ]] = true
      trainer_summary = Ironmon.tracker_area_lookup_summary(
        { "category" => "trainer" }, "runtime-test"
      )["areas"]
      assert(
        trainer_summary.first["area_id"] == area["area_id"],
        "active summary places the current area first"
      )
      trainer_summary = trainer_summary.find do |entry|
        entry["area_id"] == area["area_id"]
      end
      item_summary = Ironmon.tracker_area_lookup_summary(
        { "category" => "item" }, "runtime-test"
      )["areas"].find { |entry| entry["area_id"] == area["area_id"] }
      assert(
        trainer_summary["trainer_defeated"] == 1,
        "summary reconciles a trainer defeated while disconnected"
      )
      assert(
        item_summary["items_collected"] == 1,
        "summary reconciles an item collected while disconnected"
      )
      encounter_entries = Ironmon.tracker_area_encounter_metadata(
        area, { "data_mode" => Ironmon.tracker_data_mode }
      )
      encounter_summary = Ironmon.tracker_area_lookup_summary(
        { "category" => "encounter" }, "runtime-test"
      )["areas"].find { |entry| entry["area_id"] == area["area_id"] }
      assert(
        encounter_summary["encounter_slot_total"] == encounter_entries.length &&
          encounter_summary["encounter_fusion_total"] >= 0 &&
          encounter_summary["encounter_total"] ==
            encounter_summary["encounter_slot_total"] +
            encounter_summary["encounter_fusion_total"],
        "summary separates authored slots from chance fusion possibilities"
      )
      {
        :POTION => "hp_recovery", :ANTIDOTE => "status_pp_recovery",
        :ETHER => "status_pp_recovery", :REPEL => "general_utility",
        :FIRESTONE => "evolution", :POKEBALL => "poke_ball",
        :TM24 => "tm", :XATTACK => "battle_consumable",
        :LEFTOVERS => "held_combat"
      }.each do |item_id, category|
        assert(
          Ironmon.tracker_area_item_category(GameData::Item.get(item_id)) == category,
          "lookup category matches item weighting for #{item_id}"
        )
      end
      concealed_items = Ironmon.tracker_area_item_entries(area, active_recipe, {}, false, true)
      assert(
        concealed_items.all? { |entry| entry["items"].empty? },
        "concealed pickups do not disclose identities or item categories"
      )
      occurrence_source = encounter_entries[0]
      occurrence_generator = Ironmon.tracker_area_species_generator(
        active_recipe, :wild
      )
      occurrence_target = occurrence_generator.map(
        occurrence_source["source_species"],
        [:table, occurrence_source["mode_name"],
         occurrence_source["map_id"], occurrence_source["version"],
         occurrence_source["encounter_type"].to_sym,
         occurrence_source["context_slot"]]
      )
      wild_occurrences = Ironmon.tracker_lookup_wild_occurrences(
        GameData::Species.get(occurrence_target), active_recipe, []
      )
      wild_occurrence = wild_occurrences.find do |entry|
        entry["map_id"] == occurrence_source["map_id"] &&
          entry["encounter_version"] == occurrence_source["version"] &&
          entry["encounter_type"] == occurrence_source["encounter_type"] &&
          entry["slot"] == occurrence_source["slot"]
      end
      assert(
        wild_occurrence &&
          wild_occurrence["minimum_level"] ==
            Ironmon.scaled_level(occurrence_source["minimum_level"]) &&
          wild_occurrence["maximum_level"] ==
            Ironmon.scaled_level(occurrence_source["maximum_level"]),
        "Pokemon lookup reports effective scaled wild levels: " +
          "source=#{occurrence_source.inspect}, " +
          "target=#{occurrence_target.inspect}, " +
          "occurrences=#{wild_occurrences.first(3).inspect}"
      )
      trainer_source = trainer_data.pokemon[0]
      trainer_generator = Ironmon.tracker_area_species_generator(
        active_recipe, :trainer
      )
      trainer_target = trainer_generator.map(
        trainer_source[:species], [:pbs, trainer_data.id, 0]
      )
      trainer_occurrence = Ironmon.tracker_lookup_trainer_occurrences(
        GameData::Species.get(trainer_target), active_recipe
      ).find do |entry|
        entry["trainer_id"] ==
          Ironmon.tracker_lookup_trainer_id(trainer_data) &&
          entry["slot"] == 1
      end
      assert(
        trainer_occurrence &&
          trainer_occurrence["level"] ==
            Ironmon.scaled_level(trainer_source[:level]),
        "Pokemon lookup reports effective scaled trainer levels: " +
          "source=#{trainer_source.inspect}, " +
          "occurrence=#{trainer_occurrence.inspect}"
      )
      independent_id = encounter_entries[0]["entry_id"]
      decoy_id = encounter_entries[1]["entry_id"]
      independent = Ironmon::AreaEncounterEntry.new(
        [20, :BULBASAUR, 3, 5], independent_id
      )
      decoy = Ironmon::AreaEncounterEntry.new(
        [20, :CHARMANDER, 3, 5], decoy_id
      )
      Ironmon.begin_area_encounter_sequence
      Ironmon.begin_area_encounter_selection
      decoy[1]
      independent[1]
      Ironmon.finish_area_encounter_selection([:BULBASAUR, 4])
      Ironmon.commit_area_encounter_sequence
      encounter_package = connection.packages.shift
      assert(
        encounter_package == ["area:10", "encounter", [independent_id]],
        "only selected encounter slot is discovered"
      )
      assert(
        !Ironmon.commit_area_encounter_sequence &&
          connection.packages.empty?,
        "additional overworld group members do not rediscover the slot"
      )

      first_id = encounter_entries[2]["entry_id"]
      second_id = encounter_entries[3]["entry_id"]
      [first_id, second_id].each do |entry_id|
        Ironmon.begin_area_encounter_sequence if !Ironmon.area_encounter_sequence_active?
        Ironmon.begin_area_encounter_selection
        Ironmon::AreaEncounterEntry.new(
          [20, :SQUIRTLE, 3, 5], entry_id
        )[1]
        Ironmon.finish_area_encounter_selection([:SQUIRTLE, 4])
      end
      Ironmon.commit_area_encounter_sequence
      fusion_package = connection.packages.shift
      assert(
        fusion_package == ["area:10", "encounter", [first_id, second_id]],
        "derived fusion sends both component slot keys"
      )

      Ironmon.instance_variable_set(
        :@tracker_connection, DiscoveryConnection.new(false)
      )
      assert(
        !Ironmon.record_defeated_area_trainer(trainer_entry),
        "discovery is not retained while tracker is disconnected"
      )

      tracker_connection = Ironmon::TrackerConnection.new
      tracker_connection.instance_variable_set(:@state, :connected)
      tracker_connection.instance_variable_set(:@output_buffer, "")
      assert(
        tracker_connection.send_area_discovery(
          "area:10", "trainer", [trainer_entry]
        ),
        "connected tracker discovery send"
      )
      pending = tracker_connection.instance_variable_get(
        :@pending_area_discoveries
      )
      package_id = pending.keys.first
      initial_output_length = tracker_connection.instance_variable_get(
        :@output_buffer
      ).length
      pending[package_id]["next_send_at"] = 0.0
      tracker_connection.send(:resend_area_discoveries)
      retried_output = tracker_connection.instance_variable_get(:@output_buffer)
      assert(
        retried_output.length > initial_output_length,
        "unacknowledged discovery is resent"
      )
      tracker_connection.send(
        :acknowledge_area_discovery,
        {
          "run_id" => Ironmon.ensure_tracker_run_id,
          "payload" => { "package_id" => package_id }
        }
      )
      assert(
        pending.empty?, "acknowledged discovery leaves retry queue"
      )

      encounters = PokemonEncounters.new
      encounters.setup(10)
      tables = encounters.instance_variable_get(:@encounter_tables)
      tagged_entries = tables.values.flat_map { |entries| entries }
      assert(!tagged_entries.empty?, "runtime encounter table")
      assert(
        tagged_entries.all? do |entry|
          entry.is_a?(Ironmon::AreaEncounterEntry) &&
            entry.area_entry_id.start_with?("encounter:10:")
        end,
        "runtime encounter slot identities"
      )

      encounter_type = tables.keys.first
      table_result = encounters.choose_wild_pokemon(encounter_type)
      assert(
        Ironmon.wild_table_result?(table_result),
        "selected encounter is marked as a table result"
      )
      scripted_context = [:script, 5, 2, 1, :single, 0]
      scripted_species = Ironmon.wild_species_for(
        :POOCHYENA, scripted_context
      )
      previous_encounter_type = $PokemonTemp.encounterType
      $PokemonTemp.encounterType = encounter_type
      assert(
        Ironmon.wild_battle_species_for(
          :POOCHYENA, scripted_context
        ) == scripted_species,
        "a retained encounter type does not bypass scripted randomization"
      )
      $PokemonTemp.encounterType = previous_encounter_type
      table_battle_species = Ironmon.with_wild_table_battle do
        assert(
          Ironmon.wild_table_battle_active?,
          "table battle origin remains active while the battle is prepared"
        )
        Ironmon.wild_battle_species_for(
          table_result[0], [:script, 10, 1, 1, :single, 0]
        )
      end
      assert(
        table_battle_species == table_result[0],
        "a mapped table battle is not randomized a second time"
      )
      assert(
        !Ironmon.wild_table_battle_active?,
        "table battle origin is cleared after battle preparation"
      )
      resolved_overworld_species = Ironmon.with_wild_table_spawn(
        table_result
      ) do
        assert(
          Ironmon.wild_table_spawn_active?,
          "overworld creation retains table-result origin"
        )
        Ironmon.overworld_species_for(
          table_result[0], true, [:overworld, 10, 999]
        )
      end
      assert(
        resolved_overworld_species == table_result[0],
        "overworld table result is not randomized a second time"
      )
      assert(
        !Ironmon.wild_table_spawn_active?,
        "overworld table-result marker is transient"
      )

      previous_tables = encounters.instance_variable_get(:@encounter_tables)
      $PokemonEncounters = encounters
      $game_switches[SWITCH_MODERN_MODE] = false
      Ironmon.refresh_loaded_wild_encounter_table
      refreshed_tables = encounters.instance_variable_get(:@encounter_tables)
      assert(
        !refreshed_tables.equal?(previous_tables),
        "run reset rebuilds the loaded encounter table"
      )
      assert(
        refreshed_tables.values.flat_map { |entries| entries }.all? do |entry|
          entry.is_a?(Ironmon::AreaEncounterEntry)
        end,
        "rebuilt encounter table retains slot tracking"
      )

      ["classic", "remix"].each do |data_mode|
        $game_switches[SWITCH_MODERN_MODE] = data_mode == "remix"
        lookup_catalog = Ironmon.tracker_area_encounter_catalog(
          { "data_mode" => data_mode }
        )
        Ironmon.tracker_area_catalog.each do |catalog_area|
          catalog_area["map_ids"].each do |map_id|
            runtime_encounters = PokemonEncounters.new
            runtime_encounters.setup(map_id)
            runtime_tables = runtime_encounters.instance_variable_get(
              :@encounter_tables
            )
            runtime_ids = runtime_tables.values.flat_map do |entries|
              entries.map do |entry|
                entry.is_a?(Ironmon::AreaEncounterEntry) ?
                  entry.area_entry_id : nil
              end
            end.compact
            lookup_ids = lookup_catalog[map_id].map do |entry|
              entry["entry_id"]
            end
            assert(
              (runtime_ids - lookup_ids).empty?,
              "#{data_mode} map #{map_id} runtime encounter identities"
            )
          end
        end
      end

      route_101 = Ironmon.tracker_area_catalog.find do |catalog_area|
        catalog_area["name"] == "Route 101"
      end
      route_101_metadata = Ironmon.tracker_area_encounter_metadata(
        route_101, { "data_mode" => Ironmon.tracker_data_mode }
      )
      route_101_environments = Ironmon.tracker_area_encounter_environment_index(
        route_101_metadata
      ).map { |entry| entry["key"] }
      assert(
        route_101_environments.include?("grass") &&
          route_101_environments.include?("cross"),
        "Route 101 exposes grass and cross-table lookup groups"
      )

      $PokemonGlobal.ironmon_wild_species_map = {}
      Ironmon.reset_species_generator_cache
      paged_encounter_entries = Ironmon.tracker_area_encounter_metadata(
        area, { "data_mode" => Ironmon.tracker_data_mode }
      )
      debug_environment = Ironmon.tracker_area_encounter_environment(
        paged_encounter_entries[0]["encounter_type"]
      )
      environment_entries = paged_encounter_entries.select do |entry|
        Ironmon.tracker_area_encounter_environment(
          entry["encounter_type"]
        ) == debug_environment
      end
      fusion_work_before = (
        Ironmon.instance_variable_get(:@tracker_area_fusion_work) || {}
      ).dup
      sprites_before = Ironmon.tracker_sprite_paths.dup
      debug_index = Ironmon.tracker_area_lookup_detail(
        {
          "area_id" => area["area_id"],
          "category" => "encounter",
          "discovery_keys" => [],
          "offset" => 0,
          "limit" => 1
        },
        "runtime-test", true
      )
      debug_generator = Ironmon.tracker_area_species_generator(
        active_recipe, :wild
      )
      has_debug_environment = debug_index["encounter_environments"].any? do |entry|
        entry["key"] == debug_environment
      end
      assert(
        debug_index["encounters"].empty? &&
          debug_index["encounter_fusions"].empty? && has_debug_environment &&
          debug_generator.mapping.length == paged_encounter_entries.length,
        "encounter index counts source slots without disclosing identities"
      )
      assert(
        (Ironmon.instance_variable_get(:@tracker_area_fusion_work) || {}) ==
          fusion_work_before && Ironmon.tracker_sprite_paths == sprites_before,
        "encounter index does not resolve fusion results or sprites"
      )
      $PokemonGlobal.ironmon_wild_species_map = {}
      Ironmon.reset_species_generator_cache
      debug_page = Ironmon.tracker_area_lookup_detail(
        {
          "area_id" => area["area_id"],
          "category" => "encounter",
          "encounter_environment" => debug_environment,
          "discovery_keys" => [],
          "offset" => 0,
          "limit" => 1
        },
        "runtime-test", true
      )
      debug_generator = Ironmon.tracker_area_species_generator(
        active_recipe, :wild
      )
      assert(
        debug_page["encounters"].length == 1 &&
          debug_page["total_count"] >= environment_entries.length &&
          debug_generator.mapping.length == environment_entries.length,
        "authorized encounter lookup generates only the requested environment"
      )
      debug_source = paged_encounter_entries[0]
      debug_encounter = debug_page["encounters"][0]
      assert(
        debug_encounter["minimum_level"] ==
          Ironmon.scaled_level(debug_source["minimum_level"]) &&
          debug_encounter["maximum_level"] ==
            Ironmon.scaled_level(debug_source["maximum_level"]),
        "area lookup reports effective scaled wild levels"
      )
      $PokemonGlobal.ironmon_wild_species_map = {}
      Ironmon.reset_species_generator_cache
      sprites_before = Ironmon.tracker_sprite_paths.dup
      hidden_detail = Ironmon.tracker_area_lookup_detail(
        {
          "area_id" => area["area_id"],
          "category" => "encounter",
          "encounter_environment" => debug_environment,
          "discovery_keys" => [],
          "offset" => 0,
          "limit" => 10
        },
        "runtime-test", false
      )
      hidden_generator = Ironmon.tracker_area_species_generator(
        active_recipe, :wild
      )
      assert(
        hidden_generator.mapping.length == environment_entries.length,
        "concealed lookup counts only the requested environment's source slots"
      )
      assert(
        (Ironmon.instance_variable_get(:@tracker_area_fusion_work) || {}) ==
          fusion_work_before && Ironmon.tracker_sprite_paths == sprites_before,
        "concealed lookup does not resolve fusion results or sprites"
      )
      assert(
        hidden_detail["encounters"].all? do |entry|
          !entry["details_revealed"] && !entry["independent_fusion"] &&
            !entry.key?("species_id") && !entry.key?("species_name") &&
            !entry.key?("sprite_path")
        end,
        "concealed encounter rows expose metadata only"
      )
      detail = Ironmon.tracker_area_lookup_detail(
        {
          "area_id" => area["area_id"],
          "category" => "encounter",
          "encounter_environment" => debug_environment,
          "discovery_keys" => [independent_id],
          "offset" => 0,
          "limit" => 10
        },
        "runtime-test", false
      )
      revealed = detail["encounters"].find do |entry|
        entry["entry_id"] == independent_id
      end
      hidden = detail["encounters"].find do |entry|
        entry["entry_id"] == decoy_id
      end
      assert(
        hidden_generator.mapping.length == environment_entries.length,
        "disclosing a row reuses the environment's counted source slots"
      )
      assert(revealed["details_revealed"], "requested discovery is revealed")
      assert(!hidden["details_revealed"], "unknown live slot remains hidden")
      trainer_detail = Ironmon.tracker_area_lookup_detail(
        {
          "area_id" => area["area_id"],
          "category" => "trainer",
          "discovery_keys" => [catalog_trainer["entry_id"]]
        },
        "runtime-test", false
      )["trainers"].find do |entry|
        entry["entry_id"] == catalog_trainer["entry_id"]
      end
      assert(
        trainer_detail["party"].all? do |pokemon|
          pokemon["sprite_path"].is_a?(String) &&
            !pokemon["sprite_path"].empty?
        end,
        "revealed trainer Pokemon include local sprite paths"
      )
      begin
        Ironmon.tracker_area_lookup_detail(
          {
            "area_id" => area["area_id"],
            "category" => "encounter",
            "discovery_keys" => ["encounter:999:0:Land:1"],
            "offset" => 0,
            "limit" => 10
          },
          "runtime-test", false
        )
        assert(false, "invalid discovery key must be rejected")
      rescue Ironmon::TrackerLookupError => e
        assert(e.code == "invalid_area_discovery",
               "invalid discovery key error code")
      end

      hidden_item = Ironmon.tracker_area_hidden_items(12).first
      assert(hidden_item, "hidden item marker catalog lookup")
      marker_event = Object.new
      marker_event.instance_variable_set(:@active, true)
      def marker_event.active?
        return @active
      end
      marker_key = [
        hidden_item["map_id"].to_i,
        hidden_item["event_id"].to_i,
        "A"
      ]
      $game_self_switches[marker_key] = false
      assert(
        Ironmon.hidden_area_item_marker_visible?(
          marker_event, hidden_item["entry_id"]
        ),
        "uncollected active hidden item marker"
      )
      $game_self_switches[marker_key] = true
      assert(
        !Ironmon.hidden_area_item_marker_visible?(
          marker_event, hidden_item["entry_id"]
        ),
        "collected hidden item marker disappears while its empty page is active"
      )
      $game_self_switches[marker_key] = false
      marker_event.instance_variable_set(:@active, false)
      assert(
        !Ironmon.hidden_area_item_marker_visible?(
          marker_event, hidden_item["entry_id"]
        ),
        "base event state hides collected marker"
      )

      File.binwrite(
        "#{$ironmon_area_catalog_output_path}.tests",
        "area discovery runtime tests passed\n"
      )
    ensure
      Ironmon.instance_variable_set(:@tracker_connection, original_connection)
      Ironmon.instance_variable_set(
        :@custom_fusion_pool_service, original_pool_service
      )
      Ironmon.reset_species_generator_cache
      $PokemonGlobal = original_global
      $game_switches = original_switches
      $game_self_switches = original_self_switches
      $game_map = original_game_map
      $Trainer = original_trainer
      $PokemonEncounters = original_encounters
    end
  end
end

IronmonAreaProgressRuntimeTests.run
