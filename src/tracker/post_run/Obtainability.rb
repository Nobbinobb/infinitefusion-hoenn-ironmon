#===============================================================================
# Ironmon completed-run obtainability proof service
#===============================================================================

module Ironmon
  TRACKER_OBTAINABILITY_DIAGNOSTIC_CAPABILITIES = [
    "evolution.results", "fusion.material_pairs", "pokemon.all_active",
    "world.items", "world.wild_encounters"
  ].freeze

  class TrackerObtainabilityService
    MAX_PLANS_PER_SPECIES = 8
    MAX_REQUESTED_SPECIES = 2_000
    MAX_REQUESTED_EVOLUTION_EDGES = 4_000
    BACKGROUND_MILLISECONDS = 6.0
    FOREGROUND_MILLISECONDS = 250.0
    BACKGROUND_LEASE_SECONDS = 1.0
    FOREGROUND_LEASE_SECONDS = 2.0
    PLAYER_FUSION_LEASE_SECONDS = 60.0
    EXCLUDED_MAP_NAME = /\A(?:EVENT_TEMPLATES|QUEST_TEMPLATES|testing)\z|\Aquest_/i
    SCRIPTED_ACQUISITION_METHODS = [
      "pbAddPokemon", "pbAddPokemonSilent", "pbAddToParty",
      "pbAddToPartySilent", "pbGenerateEgg", "pbAddEgg", "pbGenEgg",
      "pbStartTrade", "npcTrade", "pbWildBattle", "pbDoubleWildBattle",
      "pbTripleWildBattle"
    ].freeze
    DEFERRED_ACQUISITION_METHODS = [
      "pbAddForeignPokemon", "pbWildBattleSpecific"
    ].freeze
    SCRIPTED_RESOURCE_METHODS = [
      "pbItemBall", "pbReceiveItem", "pbPokemonMart", "pbStoreItem"
    ].freeze
    HOENN_MART_CITIES = [
      :OLDALE, :PETALBURG, :RUSTBORO, :DEWFORD, :SLATEPORT, :MAUVILLE,
      :VERDANTURF, :LAVARIDGE, :FALLARBOR, :FORTREE, :LILYCOVE,
      :MOSSDEEP, :SOOTOPOLIS, :EVERGRANDE, :PACIFIDLOG
    ].freeze
    ITEM_EVOLUTION_METHODS = [
      :HappinessHoldItem, :HoldItem, :HoldItemMale, :HoldItemFemale,
      :DayHoldItem, :NightHoldItem, :HoldItemHappiness,
      :Item, :ItemMale, :ItemFemale, :ItemDay, :ItemNight, :ItemHappiness
    ].freeze

    def initialize(recipe, deferred = false)
      @recipe = recipe
      @configuration = nil
      @plans = Hash.new { |hash, key| hash[key] = [] }
      @obtainable_count = 0
      @direct_caught_fusions = {}
      @direct_fusion_material_pairs = {}
      @direct_fusion_pair_offsets = []
      @unresolved_sources = []
      @unresolved_resources = []
      @authored_variable_species = Hash.new do |hash, key|
        hash[key] = []
      end
      @authored_variable_items = Hash.new do |hash, key|
        hash[key] = []
      end
      @resource_supply = Hash.new(0)
      @normal_evolution_queue = []
      @normal_evolution_work = nil
      @queued_normal_evolutions = {}
      @possible_evolution_edge_numbers = {}
      @phase = :prepare_generators
      @pair_count = 0
      @material_pairs_prepared = false
      @material_ids = []
      @excluded_material_pair_offsets = []
      @tracker_obtainable_fusion_words = nil
      @tracker_executable_evolution_edges = nil
      @fusion_mapping_complete = false
      @failure_reason = nil
      @normal_generator = nil
      @encounter_tables = nil
      @encounter_table_index = 0
      @authored_catalog_entries = nil
      @authored_catalog_index = 0
      @authored_sources_complete = false
      @foreground_until = 0.0
      @background_until = 0.0
      @player_fusion_until = 0.0
      @tracker_base_proof_snapshot = nil
      @tracker_resource_supply_snapshot = nil
      @caught_fusion_entries = nil
      @caught_fusion_index = 0
      @work_fiber = nil
      @work_deadline = nil
      advance_until_ready if !deferred
    end

    def advance_for_milliseconds(milliseconds)
      duration = [milliseconds.to_f, 0.1].max / 1000.0
      @work_deadline = Ironmon.tracker_uptime_seconds + duration
      @work_fiber = nil if @work_fiber && !@work_fiber.alive?
      @work_fiber ||= Fiber.new do
        while ![:complete, :failed].include?(@phase)
          advance_work_unit
          Fiber.yield if @phase == :player_fusions
          cooperative_checkpoint
        end
      end
      @work_fiber.resume if @work_fiber.alive?
      return @phase == :complete
    rescue Exception
      @work_fiber = nil
      raise
    ensure
      @work_deadline = nil
    end

    def request_foreground
      @foreground_until = Ironmon.tracker_uptime_seconds +
        FOREGROUND_LEASE_SECONDS
    end

    def request_background
      @background_until = Ironmon.tracker_uptime_seconds +
        BACKGROUND_LEASE_SECONDS
    end

    def foreground_requested?
      return scheduled_advance_allowed? &&
        Ironmon.tracker_uptime_seconds < @foreground_until
    end

    def foreground_deadline
      return @foreground_until
    end

    def background_deadline
      return @background_until
    end

    def complete?
      return @phase == :complete
    end

    def tracker_closure_unavailable?
      return @phase == :failed &&
        @failure_reason == :tracker_closure_unavailable
    end

    def background_advance_allowed?
      return ![:complete, :failed].include?(@phase)
    end

    def background_requested?
      return scheduled_advance_allowed? &&
        Ironmon.tracker_uptime_seconds < @background_until
    end

    def game_work_priority_requested?
      return false if !background_advance_allowed?
      now = Ironmon.tracker_uptime_seconds
      return true if @phase == :player_fusions &&
        now < @player_fusion_until
      return now < @foreground_until || now < @background_until
    end

    def scheduled_advance_allowed?
      return ![:player_fusions, :complete, :failed].include?(@phase)
    end

    def snapshot(species = nil, requested_species_ids = [], requested_edge_keys = [])
      raise_if_failed
      result = {
        "phase" => @phase.to_s,
        "complete" => requested_work_complete?(
          species, requested_species_ids, requested_edge_keys
        ),
        "background_complete" => !background_advance_allowed?,
        "processed_pairs" => @fusion_mapping_complete ? @pair_count : 0,
        "total_pairs" => @pair_count,
        "obtainable_count" => @obtainable_count,
        "unresolved_source_count" => @unresolved_sources.length,
        "unresolved_resource_count" => @unresolved_resources.length,
        "obtainable_species_ids" => requested_species_ids.map do |value|
          identity = value.to_s.split(":", 2)[0].to_s
          key = identity.to_sym
          next if identity.empty? || !obtainable_identity?(key)
          "#{identity}:0"
        end.compact.uniq,
        "obtainable_evolution_edge_keys" => requested_edge_keys.map do |value|
          key = value.to_s
          requested_evolution_edge_obtainable?(key) ? key : nil
        end.compact.uniq,
        "fusion_closure_work" => fusion_closure_work_snapshot
      }
      result["target"] = target_snapshot(species) if species
      return result
    end

    def apply_fusion_closure_result(result)
      return if !result
      if !result.is_a?(Hash) || result["job_id"].to_s != fusion_closure_job_id
        raise TrackerLookupError.new(
          "invalid_query", "The tracker fusion-closure job is stale."
        )
      end
      @work_fiber = nil
      @work_deadline = nil
      apply_tracker_closure_summary(result)
      @fusion_mapping_complete = true
      @phase = :complete
      return true
    end

    def apply_tracker_closure_summary(batch)
      words = batch["obtainable_fusion_words"]
      encoded_edges = batch["packed_executable_evolution_edges"]
      if !words.is_a?(Array) || !encoded_edges.is_a?(String)
        raise TrackerLookupError.new(
          "invalid_query", "The tracker closure summary is malformed."
        )
      end
      maximum_number = (NB_POKEMON * NB_POKEMON) + NB_POKEMON
      maximum_words = (maximum_number >> 5) + 1
      if words.length != maximum_words || words.any? do |word|
           !word.is_a?(Integer) || word < 0 || word > 0xFFFFFFFF
         end
        raise TrackerLookupError.new(
          "invalid_query", "The tracker obtainability bitset is malformed."
        )
      end
      @tracker_executable_evolution_edges =
        decode_tracker_executable_edges(encoded_edges)
      @tracker_obtainable_fusion_words = words
      obtainable_count = batch["obtainable_count"]
      if !obtainable_count.is_a?(Integer) || obtainable_count < 0 ||
         obtainable_count > maximum_number
        raise TrackerLookupError.new(
          "invalid_query", "The tracker obtainability count is malformed."
        )
      end
      @obtainable_count = obtainable_count
    end

    def decode_tracker_executable_edges(encoded)
      edges = []
      previous = 0
      value = 0
      shift = 0
      base64_value = 0
      base64_bits = 0
      encoded.each_byte do |character|
        break if character == 61
        sextet = if character >= 65 && character <= 90
                   character - 65
                 elsif character >= 97 && character <= 122
                   character - 71
                 elsif character >= 48 && character <= 57
                   character + 4
                 elsif character == 43
                   62
                 elsif character == 47
                   63
                  elsif character == 9 || character == 10 ||
                        character == 13 || character == 32
                   next
                 end
        if !sextet
          raise TrackerLookupError.new(
            "invalid_query", "The tracker executable-edge index is malformed."
          )
        end
        base64_value = (base64_value << 6) | sextet
        base64_bits += 6
        while base64_bits >= 8
          base64_bits -= 8
          byte = (base64_value >> base64_bits) & 0xFF
          value |= (byte & 0x7F) << shift
          if (byte & 0x80) == 0
            previous += value
            edges << previous
            value = 0
            shift = 0
          else
            shift += 7
            if shift > 63
              raise TrackerLookupError.new(
                "invalid_query", "The tracker executable-edge index is malformed."
              )
            end
          end
        end
        base64_value &= (1 << base64_bits) - 1
      end
      if shift != 0
        raise TrackerLookupError.new(
          "invalid_query", "The tracker executable-edge index is malformed."
        )
      end
      return edges
    end

    def report_tracker_closure_unavailable
      @phase = :failed if !@fusion_mapping_complete
      @failure_reason = :tracker_closure_unavailable if @phase == :failed
      @failure_message =
        "The parallel tracker obtainability worker is unavailable." if
        @phase == :failed
    end

    def prove_player_fusion_pair(body, head, fusion)
      return false if !@material_pairs_prepared
      body_species = GameData::Species.get(body)
      head_species = GameData::Species.get(head)
      fusion_species = GameData::Species.get(fusion)
      @plans[body_species.id].each do |body_plan|
        @plans[head_species.id].each do |head_plan|
          combination = player_fusion_plan_combination(body_plan, head_plan)
          next if !combination
          add_player_fusion_combination(
            fusion_species.id, body_species, head_species,
            body_plan, head_plan, combination
          )
        end
      end
      if !@plans[fusion_species.id].empty?
        return true
      end
      return false
    end

    def target_snapshot(species)
      identity = normalize_species_id(species)
      plan = best_plan(identity)
      if plan
        return {
          "status" => "obtainable",
          "reason" => plan[:reason],
          "path" => materialize_plan_path(plan),
          "required_items" => stringify_counts(plan[:items])
        }
      end
      if !@fusion_mapping_complete
        return {
          "status" => "calculating",
          "reason" => "This Pokemon's material and evolution paths are being checked.",
          "path" => [],
          "required_items" => {}
        }
      end
      if species.is_a?(GameData::FusedSpecies) &&
         tracker_fusion_obtainable_number?(species.id_number)
        return {
          "status" => "obtainable",
          "reason" => "The tracker proved a valid acquisition path.",
          "path" => [],
          "required_items" => {}
        }
      end
      return {
        "status" => "unobtainable",
        "reason" => "No permanent acquisition, material, or evolution path can produce this Pokemon.",
        "path" => [],
        "required_items" => {}
      }
    end

    def passive_target_snapshot(species)
      raise_if_failed
      return target_snapshot(species)
    end

    def passive_identity_status(identity, fusion_number = nil)
      raise_if_failed
      plans = @plans.key?(identity) ? @plans[identity] : nil
      return "obtainable" if plans && !plans.empty?
      return "calculating" if !@fusion_mapping_complete
      if fusion_number && tracker_fusion_obtainable_number?(fusion_number)
        return "obtainable"
      end
      return "unobtainable"
    end

    private

    def obtainable_identity?(identity)
      plans = @plans.key?(identity) ? @plans[identity] : nil
      return true if plans && !plans.empty?
      species = GameData::Species.try_get(identity)
      return false if !species || !species.is_a?(GameData::FusedSpecies)
      return tracker_fusion_obtainable_number?(species.id_number)
    end

    def tracker_fusion_obtainable_number?(number)
      return false if !@tracker_obtainable_fusion_words
      word = @tracker_obtainable_fusion_words[number >> 5].to_i
      return (word & (1 << (number & 31))) != 0
    end

    def raise_if_failed
      return if @phase != :failed
      raise TrackerLookupError.new(
        "obtainability_incomplete", @failure_message.to_s
      )
    end

    def requested_work_complete?(species, species_ids, edge_keys)
      requested = false
      if species
        requested = true
        identity = normalize_species_id(species)
        return false if !obtainable_identity?(identity) &&
          !@fusion_mapping_complete
      end
      species_ids.each do |value|
        identity = value.to_s.split(":", 2)[0].to_s
        next if identity.empty?
        requested = true
        species_data = GameData::Species.try_get(identity.to_sym)
        return false if species_data &&
          !obtainable_identity?(species_data.id) &&
          !@fusion_mapping_complete
      end
      edge_keys.each do |value|
        key = value.to_s
        next if key.empty?
        requested = true
        return false if !requested_evolution_edge_obtainable?(key) &&
          !@fusion_mapping_complete
      end
      return complete? if !requested
      return true
    end

    def fusion_closure_work_snapshot
      return nil if !@material_pairs_prepared ||
        @fusion_mapping_complete
      @player_fusion_until = Ironmon.tracker_uptime_seconds +
        PLAYER_FUSION_LEASE_SECONDS
      info = Ironmon.custom_fusion_pool_info
      return {
        "job_id" => fusion_closure_job_id,
        "source_catalog_fingerprint" =>
          Ironmon.tracker_obtainability_source_catalog["fingerprint"],
        "seed" => @recipe["seed"],
        "generator_version" => @recipe["player_fusion_generator_version"],
        "base_stat_source_fingerprint" =>
          @recipe["base_stat_source_fingerprint"],
        "custom_fusion_pool_version" => info[:schema_version],
        "custom_fusion_pool_size" => info[:size],
        "custom_fusion_pool_fingerprint" => info[:fingerprint],
        "material_ids" => @material_ids.map do |identity|
          GameData::Species.get(identity).id_number
        end,
        "total_pairs" => @pair_count,
        "excluded_pair_offsets" => @excluded_material_pair_offsets,
        "direct_pair_offsets" => @direct_fusion_pair_offsets,
        "reversible_fusion_ids" => @direct_caught_fusions.keys.map do |identity|
          GameData::Species.get(identity).id_number
        end.sort,
        "base_proofs" => tracker_base_proof_snapshot,
        "resource_supply" => tracker_resource_supply_snapshot,
        "fusion_evolution_generator_version" =>
          @recipe["fusion_evolution_generator_version"],
        "fusion_evolution_rules_version" =>
          @recipe["fusion_evolution_rules_version"],
        "evolution_source_fingerprint" =>
          @recipe["evolution_source_fingerprint"],
        "evolution_taxonomy_fingerprint" =>
          @recipe["evolution_taxonomy_fingerprint"],
        "evolution_method_fingerprint" =>
          @recipe["evolution_method_fingerprint"]
      }
    end

    def fusion_closure_job_id
      return @fusion_closure_job_id if @fusion_closure_job_id
      material_numbers = @material_ids.map do |identity|
        GameData::Species.get(identity).id_number
      end
      fingerprint = Ironmon.fnv1a_64_fingerprint(
        [material_numbers.length] + material_numbers +
          [@excluded_material_pair_offsets.length] +
          @excluded_material_pair_offsets +
          [@direct_fusion_pair_offsets.length] + @direct_fusion_pair_offsets +
          [@direct_caught_fusions.length] +
          @direct_caught_fusions.keys.map do |identity|
            GameData::Species.get(identity).id_number
          end.sort
      )
      @fusion_closure_job_id = [
        @recipe["run_id"], @recipe["seed"],
        @recipe["player_fusion_generator_version"], fingerprint
      ].join(":")
      return @fusion_closure_job_id
    end

    def tracker_base_proof_snapshot
      return @tracker_base_proof_snapshot if @tracker_base_proof_snapshot
      result = []
      @plans.each do |identity, plans|
        next if plans.empty?
        species = GameData::Species.try_get(identity)
        next if !species
        result << {
          "species_id" => species.id_number,
          "plans" => plans.map do |plan|
            {
              "items" => stringify_counts(plan[:items]),
              "constraints" => stringify_constraints(plan[:constraints]),
              "source_uses" => stringify_counts(plan[:source_uses]),
              "path_length" => plan[:path_length].to_i
            }
          end
        }
      end
      @tracker_base_proof_snapshot = result.sort_by do |entry|
        entry["species_id"]
      end
      return @tracker_base_proof_snapshot
    end

    def tracker_resource_supply_snapshot
      @tracker_resource_supply_snapshot ||= stringify_counts(@resource_supply)
      return @tracker_resource_supply_snapshot
    end

    def stringify_constraints(values)
      result = {}
      values.each do |key, value|
        result[key.to_s] = value.to_s
      end
      return result
    end

    def compatible_material_pair?(body, head)
      return @plans[body].any? do |body_plan|
        @plans[head].any? do |head_plan|
          player_fusion_plan_combination(body_plan, head_plan)
        end
      end
    end

    def cooperative_checkpoint
      return if !@work_deadline
      return if Ironmon.tracker_uptime_seconds < @work_deadline
      Fiber.yield
    end

    def advance_until_ready
      advance_work_unit while ![:player_fusions, :complete].include?(@phase)
    end

    def advance_work_unit
      case @phase
      when :prepare_generators
        Ironmon.sprite_credit_catalog(proc { cooperative_checkpoint })
        prepare_generators
      when :prepare_encounters
        prepare_encounter_tables
      when :encounter_sources
        if @encounter_table_index < @encounter_tables.length
          process_encounter_table(@encounter_tables[@encounter_table_index])
          @encounter_table_index += 1
        else
          @phase = :starter_sources
        end
      when :starter_sources
        seed_starter_sources
        prepare_authored_map_scan
        @phase = :authored_sources
      when :authored_sources
        advance_authored_source_work
      when :resources
        build_resource_supply
        @phase = :normal_graph
      when :normal_graph
        complete = @normal_generator.advance_graph(1)
        @phase = :normal_evolutions if complete
      when :normal_evolutions
        close_normal_evolutions(1)
        @phase = :caught_fusion_transformations if
          !normal_evolution_pending?
      when :caught_fusion_transformations
        complete = apply_caught_fusion_transformations(1)
        @phase = :transformation_evolutions if complete
      when :transformation_evolutions
        close_normal_evolutions(1)
        prepare_material_pairs if !normal_evolution_pending?
      when :player_fusions
        return
      end
    end

    def prepare_encounter_tables
      @encounter_tables = []
      mode = encounter_mode
      mode.each do |data|
        data.types.each do |encounter_type, entries|
          @encounter_tables << [mode.name, data.map, data.version,
                                encounter_type, entries]
        end
      end
      @phase = :encounter_sources
    end

    def prepare_generators
      @configuration ||= Configuration.from(@recipe["configuration"])
      @normal_generator ||= Ironmon.tracker_normal_evolution_generator(@recipe)
      @phase = :prepare_encounters
    end

    def requested_evolution_species_identity(value)
      identity = value.to_s.to_sym
      fusion = Ironmon.fusion_species_identity(identity)
      return fusion if fusion && Ironmon.custom_fusion_species?(fusion)
      species = GameData::Species.try_get(identity)
      return species ? species.id : nil
    end

    def requested_evolution_edge_obtainable?(key)
      source_text, target_text = key.to_s.split(">", 2)
      return false if !source_text || !target_text
      source = requested_evolution_species_identity(
        source_text.split(":", 2)[0]
      )
      target = requested_evolution_species_identity(
        target_text.split(":", 2)[0]
      )
      return false if !source || !target
      return possible_evolution_edge?(source, target)
    end

    def process_encounter_table(table)
      mode_name, map_id, version, encounter_type, entries = table
      generator = species_generator
      mapped = entries.each_with_index.map do |entry, slot|
        context = [:table, mode_name, map_id, version, encounter_type, slot]
        generator.map(entry[1], context)
      end
      mapped.each_with_index do |identity, slot|
        add_direct_source(
          identity, "Wild encounter",
          "Wild encounter on #{pbGetMapNameFromId(map_id)} " +
          "(#{encounter_type}, slot #{slot + 1})",
          nil, true
        )
      end
      return if @configuration.wild_policy !=
        Configuration::POLICY_NORMAL_ONLY
      mapped.each do |body|
        mapped.each do |head|
          body_species = GameData::Species.try_get(body)
          head_species = GameData::Species.try_get(head)
          next if !body_species || !head_species ||
            body_species.is_a?(GameData::FusedSpecies) ||
            head_species.is_a?(GameData::FusedSpecies)
          first = [body_species.id_number, head_species.id_number].min
          second = [body_species.id_number, head_species.id_number].max
          @direct_fusion_material_pairs[(first << 10) | second] = true
        end
      end
    end

    def seed_starter_sources
      generator = species_generator
      [1, 4, 7].each_with_index do |source, slot|
        identity = generator.map(source, [:starter, slot])
        constraint = { :starter => slot }
        add_direct_source(
          identity, "Starter choice", "Starter choice #{slot + 1}",
          constraint, false
        )
      end
    end

    def prepare_authored_map_scan
      catalog = Ironmon.tracker_obtainability_source_catalog
      @authored_catalog_entries = catalog["sources"].map do |entry|
        [:source, entry]
      end
      catalog["resources"].each do |entry|
        @authored_catalog_entries << [:resource, entry]
      end
      @authored_catalog_index = 0
    rescue Exception => e
      @unresolved_sources << "catalog:#{e.class}:#{e.message}"
      @authored_catalog_entries = []
    end

    def advance_authored_source_work
      if @authored_catalog_index < @authored_catalog_entries.length
        kind, entry = @authored_catalog_entries[@authored_catalog_index]
        if kind == :source
          apply_authored_source_catalog_entry(entry)
        else
          apply_authored_resource_catalog_entry(entry)
        end
        @authored_catalog_index += 1
        return
      end
      if !@unresolved_sources.empty? || !@unresolved_resources.empty?
        @phase = :failed
        @failure_reason = :unresolved_catalog_source
        first_unresolved = (@unresolved_sources + @unresolved_resources).first
        @failure_message =
          "Run obtainability could not classify every acquisition source " +
          "or evolution resource (#{first_unresolved})."
        raise_if_failed
      end
      @authored_sources_complete = true
      @phase = :resources
    end

    def apply_authored_source_catalog_entry(entry)
      identity = GameData::Species.get(entry["species_id"].to_i).id
      if entry["mapping_kind"] == "wild"
        identity = species_generator.map(identity, entry["mapping_context"])
      end
      constraints = entry["starter_slot"] ?
        { :starter => entry["starter_slot"].to_i } : nil
      add_direct_source(
        identity, entry["reason"], entry["detail"], constraints,
        entry["caught"] == true, entry["source_id"]
      )
    rescue Exception => e
      @unresolved_sources << "catalog:source:#{e.class}:#{e.message}"
    end

    def apply_authored_resource_catalog_entry(entry)
      quantity = entry["quantity"].to_i
      entry["item_ids"].each do |item_id|
        item = GameData::Item.get(item_id.to_sym)
        if entry["tm_gift"] == true && item.is_TM?
          generator = tracker_item_generator
          item = GameData::Item.get(
            Ironmon.resolve_tm_gift(item, entry["slot_id"], generator)
          ) if generator
        end
        if entry["repeatable"] == true
          @resource_supply[item.id] = MAX_REQUESTED_SPECIES
        else
          @resource_supply[item.id] += quantity
        end
      end
    rescue Exception => e
      @unresolved_resources << "catalog:resource:#{e.class}:#{e.message}"
    end

    def scan_command_list(commands, map_id, event_id, common_stack,
                          inherited_species = {}, inherited_items = {})
      chunks = script_chunks(commands)
      complete_script = chunks.map { |_index, script| script }.join("\n")
      variable_species = authored_variable_species_candidates(
        complete_script, inherited_species
      )
      variable_items = authored_variable_item_candidates(
        complete_script, inherited_items
      )
      chunks.each do |index, script|
        scripted_calls(script).each do |method_name, arguments|
          add_scripted_call(
            method_name, arguments, map_id, event_id, index, complete_script,
            variable_species
          )
        end
        scripted_resource_calls(script).each do |method_name, arguments|
          add_scripted_resource_call(
            method_name, arguments, map_id, event_id, index, complete_script,
            variable_items
          )
        end
      end
      commands.each do |command|
        next if command.code != 117
        common_id = command.parameters[0].to_i
        next if common_id < 1 || common_stack[common_id]
        @common_events ||= load_data("Data/CommonEvents.rxdata")
        common = @common_events[common_id]
        next if !common
        nested_stack = common_stack.dup
        nested_stack[common_id] = true
        scan_command_list(
          common.list, map_id, event_id, nested_stack, variable_species,
          variable_items
        )
      end
    end

    def script_chunks(commands)
      chunks = []
      index = 0
      while index < commands.length
        command = commands[index]
        if command.code == 355
          start_index = index
          parts = [command.parameters[0].to_s]
          while index + 1 < commands.length && commands[index + 1].code == 655
            index += 1
            parts << commands[index].parameters[0].to_s
          end
          chunks << [start_index, parts.join("\n")]
        elsif command.code == 111 && command.parameters[0].to_i == 12
          chunks << [index, command.parameters[1].to_s]
        end
        index += 1
      end
      return chunks
    end

    def scripted_calls(script)
      return parsed_script_calls(
        script, SCRIPTED_ACQUISITION_METHODS + DEFERRED_ACQUISITION_METHODS
      )
    end

    def scripted_resource_calls(script)
      return parsed_script_calls(script, SCRIPTED_RESOURCE_METHODS)
    end

    def parsed_script_calls(script, method_names)
      calls = []
      method_names.each do |method_name|
        offset = 0
        loop do
          match = script.match(/\b#{Regexp.escape(method_name)}\s*\(/, offset)
          break if !match
          opening = match.end(0) - 1
          closing = matching_parenthesis(script, opening)
          break if !closing
          calls << [method_name, split_arguments(script[(opening + 1)...closing])]
          offset = closing + 1
        end
      end
      return calls
    end

    def matching_parenthesis(text, opening)
      depth = 0
      quote = nil
      escaped = false
      (opening...text.length).each do |index|
        character = text[index]
        if quote
          if escaped
            escaped = false
          elsif character == "\\"
            escaped = true
          elsif character == quote
            quote = nil
          end
          next
        end
        if character == "\"" || character == "'"
          quote = character
        elsif character == "("
          depth += 1
        elsif character == ")"
          depth -= 1
          return index if depth == 0
        end
      end
      return nil
    end

    def split_arguments(text)
      result = []
      start = 0
      depth = 0
      quote = nil
      escaped = false
      text.length.times do |index|
        character = text[index]
        if quote
          if escaped
            escaped = false
          elsif character == "\\"
            escaped = true
          elsif character == quote
            quote = nil
          end
          next
        end
        if character == "\"" || character == "'"
          quote = character
        elsif ["(", "[", "{"].include?(character)
          depth += 1
        elsif [")", "]", "}"].include?(character)
          depth -= 1
        elsif character == "," && depth == 0
          result << text[start...index].strip
          start = index + 1
        end
      end
      result << text[start..-1].to_s.strip
      return result
    end

    def add_scripted_call(method_name, arguments, map_id, event_id, index,
                          script, variable_species)
      entries = authored_source_catalog_entries(
        method_name, arguments, map_id, event_id, index, script,
        variable_species
      )
      entries.each { |entry| apply_authored_source_catalog_entry(entry) }
    rescue Exception => e
      @unresolved_sources << e.message
    end

    def collect_authored_catalog_entries(commands, map_id, event_id,
                                         common_stack, sources, resources,
                                         inherited_species = {},
                                         inherited_items = {})
      chunks = script_chunks(commands)
      complete_script = chunks.map { |_index, script| script }.join("\n")
      variable_species = authored_variable_species_candidates(
        complete_script, inherited_species
      )
      variable_items = authored_variable_item_candidates(
        complete_script, inherited_items
      )
      chunks.each do |index, script|
        scripted_calls(script).each do |method_name, arguments|
          sources.concat(authored_source_catalog_entries(
            method_name, arguments, map_id, event_id, index, complete_script,
            variable_species
          ))
        end
        scripted_resource_calls(script).each do |method_name, arguments|
          resources.concat(authored_resource_catalog_entries(
            method_name, arguments, map_id, event_id, index, complete_script,
            variable_items
          ))
        end
      end
      commands.each do |command|
        next if command.code != 117
        common_id = command.parameters[0].to_i
        next if common_id < 1 || common_stack[common_id]
        @common_events ||= load_data("Data/CommonEvents.rxdata")
        common = @common_events[common_id]
        next if !common
        nested_stack = common_stack.dup
        nested_stack[common_id] = true
        collect_authored_catalog_entries(
          common.list, map_id, event_id, nested_stack, sources, resources,
          variable_species, variable_items
        )
      end
    end

    def authored_source_catalog_entries(method_name, arguments, map_id,
                                        event_id, index, script, variables)
      entries = []
      scripted_species_positions(method_name).each_with_index do |position, subslot|
        expression = arguments[position]
        identities = species_expression_candidates(expression, script, variables)
        if identities.empty?
          next if expression.to_s == "starter" ||
            expression.to_s.include?("VAR_PLAYER_STARTER_CHOICE")
          raise "unresolved authored source map:#{map_id}|event:#{event_id}|" +
            "index:#{index}|#{method_name}:#{expression}"
        end
        identities.each do |identity|
          species = GameData::Species.try_get(identity)
          next if !species || !Ironmon.tracker_lookup_species_available?(species)
          mapping_kind, context = authored_source_mapping(
            method_name, arguments, map_id, event_id, index, subslot
          )
          label = scripted_source_label(method_name)
          entries << {
            "species_id" => species.id_number,
            "mapping_kind" => mapping_kind,
            "mapping_context" => context,
            "reason" => label,
            "detail" => "#{label} on #{pbGetMapNameFromId(map_id)} " +
              "(event #{event_id})",
            "caught" => true,
            "source_id" => "authored:#{map_id}:#{event_id}"
          }
        end
      end
      return entries
    end

    def authored_source_mapping(method_name, arguments, map_id, event_id,
                                index, subslot)
      if method_name.include?("WildBattle")
        purpose = method_name == "pbDoubleWildBattle" ? "double" :
          (method_name == "pbTripleWildBattle" ? "triple" : "single")
        return ["wild", ["script", map_id, event_id, index, purpose, subslot]]
      end
      if ["pbAddPokemon", "pbAddToParty"].include?(method_name) &&
         arguments[3].to_s != "true"
        return ["wild", ["script", map_id, event_id, index, "gift", 0]]
      end
      return ["none", []]
    end

    def authored_resource_catalog_entries(method_name, arguments, map_id,
                                          event_id, index, script, variables)
      return [] if method_name == "pbItemBall"
      items = item_expression_candidates(arguments[0], script, variables)
      if items.empty?
        raise "unresolved authored resource map:#{map_id}|event:#{event_id}|" +
          "index:#{index}|#{method_name}:#{arguments[0]}"
      end
      return [{
        "item_ids" => items.map { |item| item.id.to_s },
        "quantity" => literal_quantity(arguments[1]),
        "repeatable" => method_name == "pbPokemonMart",
        "tm_gift" => method_name == "pbReceiveItem",
        "slot_id" => "map:#{map_id}|event:#{event_id}|command:#{index}"
      }]
    end

    def add_scripted_resource_call(method_name, arguments, map_id, event_id,
                                   index, script, variable_items)
      entries = authored_resource_catalog_entries(
        method_name, arguments, map_id, event_id, index, script,
        variable_items
      )
      entries.each { |entry| apply_authored_resource_catalog_entry(entry) }
    rescue Exception => e
      @unresolved_resources << e.message
    end

    def literal_item(expression)
      match = expression.to_s.strip.match(
        /\A(?::|PBItems::)([A-Za-z0-9_]+)\z/
      )
      return nil if !match
      return GameData::Item.try_get(match[1].upcase.to_sym)
    end

    def literal_quantity(expression)
      value = expression.to_s.strip
      return 1 if value.empty? || !value.match?(/\A\d+\z/)
      return [value.to_i, 1].max
    end

    def tracker_item_generator
      item_generator = @recipe["item_generator"]
      return nil if !item_generator.is_a?(Hash)
      return Ironmon.build_item_slot_generator(
        @recipe["seed"], item_generator["rules_version"]
      )
    end

    def scripted_species_positions(method_name)
      return [0, 2, 4] if method_name == "pbTripleWildBattle"
      return [0, 2] if method_name == "pbDoubleWildBattle"
      return [1] if method_name == "pbStartTrade"
      return [0]
    end

    def literal_species(expression)
      value = expression.to_s.strip
      match = value.match(/\A:([A-Za-z0-9_]+)\z/)
      return normalized_literal_species(match[1]) if match
      match = value.match(/\A(fusionOf|getFusionSpecies)\(\s*:([A-Za-z0-9_]+)\s*,\s*:([A-Za-z0-9_]+)\s*\)\z/)
      if match
        first = normalized_literal_species(match[2])
        second = normalized_literal_species(match[3])
        return nil if !first || !second
        body, head = match[1] == "fusionOf" ? [second, first] : [first, second]
        return GameData::Species.get(
          getFusedPokemonIdFromSymbols(body, head)
        ).id
      end
      match = value.match(/\A(?:GameData::Species\.get\()?\s*(\d+)\s*\)?\z/)
      return normalized_literal_species(match[1].to_i) if match
      return nil
    rescue Exception
      return nil
    end

    def normalized_literal_species(value)
      species = GameData::Species.try_get(
        value.is_a?(String) ? value.upcase.to_sym : value
      )
      return species ? species.id : nil
    end

    def scripted_source_label(method_name)
      return "Scripted wild encounter" if method_name.include?("WildBattle")
      return "Egg hatch" if ["pbGenerateEgg", "pbAddEgg", "pbGenEgg"].include?(method_name)
      return "NPC trade" if ["pbStartTrade", "npcTrade"].include?(method_name)
      return "Gift or static Pokemon"
    end

    def add_direct_source(identity, reason, detail, constraints, caught,
                          source_id = nil)
      species = GameData::Species.try_get(identity)
      return if !species || !Ironmon.tracker_lookup_species_available?(species)
      plan = {
        :items => {},
        :constraints => constraints || {},
        :source_uses => direct_source_uses(constraints, source_id),
        :reason => reason,
        :path => path_step(nil, nil, detail),
        :path_length => 1
      }
      added = add_plan(species.id, plan)
      if added && caught && species.is_a?(GameData::FusedSpecies)
        @direct_caught_fusions[species.id] ||= []
        @direct_caught_fusions[species.id] << plan
      end
    end

    def species_expression_candidates(expression, script, variables)
      literal = literal_species(expression)
      return [literal] if literal
      variable_key = pb_get_variable_key(expression)
      return variables[variable_key].to_a if variable_key
      local_name = expression.to_s.strip
      return [] if !local_name.match?(/\A[a-z_][A-Za-z0-9_]*\z/)
      return local_species_candidates(script)[local_name].to_a
    end

    def authored_variable_species_candidates(script, inherited)
      result = merge_candidate_hashes(
        @authored_variable_species, inherited
      )
      locals = local_species_candidates(script)
      parsed_script_calls(script, ["pbSet"]).each do |_method_name, arguments|
        key = game_variable_key(arguments[0])
        next if !key
        candidates = species_expression_candidates(
          arguments[1], script, result
        )
        candidates = locals[arguments[1].to_s.strip].to_a if
          candidates.empty?
        merge_candidates(result[key], candidates)
      end
      parsed_script_calls(script, ["pbConvertItemToPokemon"]).each do |_method_name, arguments|
        key = game_variable_key(arguments[0])
        next if !key
        candidates = arguments[1].to_s.scan(/:([A-Za-z0-9_]+)/).flatten.map do |value|
          normalized_literal_species(value)
        end.compact
        merge_candidates(result[key], candidates)
        merge_candidates(@authored_variable_species[key], candidates)
      end
      return result
    end

    def authored_variable_item_candidates(script, inherited)
      result = merge_candidate_hashes(@authored_variable_items, inherited)
      parsed_script_calls(script, ["pbSet", "pbChooseItemFromList"]).each do |method_name, arguments|
        key_position = method_name == "pbChooseItemFromList" ? 1 : 0
        value_position = method_name == "pbChooseItemFromList" ? 2 : 1
        key = game_variable_key(arguments[key_position])
        next if !key
        candidates = item_expression_candidates(
          arguments[value_position], script, result
        )
        merge_candidates(result[key], candidates)
        merge_candidates(@authored_variable_items[key], candidates)
      end
      return result
    end

    def local_species_candidates(script)
      result = Hash.new { |hash, key| hash[key] = [] }
      script.to_s.scan(/\b([a-z_][A-Za-z0-9_]*)\s*=\s*([^\n;]+)/) do |name, value|
        literal = literal_species(value.strip)
        result[name] << literal if literal && !result[name].include?(literal)
      end
      return result
    end

    def item_expression_candidates(expression, script, variables)
      item = literal_item(expression)
      return [item] if item
      if script.to_s.include?("get_mart_exclusive_items_hoenn")
        return HOENN_MART_CITIES.map do |city|
          get_mart_exclusive_items_hoenn(city)
        end.flatten.map do |item_id|
          GameData::Item.try_get(item_id)
        end.compact.uniq { |candidate| candidate.id }
      end
      variable_key = pb_get_variable_key(expression)
      return variables[variable_key].to_a if variable_key
      text = expression.to_s
      embedded_variable = text.match(/pbGet\(\s*([^\)]+)\s*\)/)
      if embedded_variable
        key = game_variable_key(embedded_variable[1])
        candidates = key ? variables[key].to_a : []
        return candidates if !candidates.empty?
      end
      local_name = text.strip
      if local_name.match?(/\A[a-z_][A-Za-z0-9_]*\z/)
        assignments = script.to_s.scan(
          /\b#{Regexp.escape(local_name)}\s*=\s*([^\n;]+)/
        ).flatten
        text = assignments.join("\n")
      end
      candidates = item_symbols(text)
      candidates = item_symbols(script) if candidates.empty? &&
        local_name.match?(/\A[a-z_][A-Za-z0-9_]*\z/)
      return candidates
    end

    def item_symbols(text)
      return text.to_s.scan(/(?::|PBItems::)([A-Za-z0-9_]+)/).flatten.map do |value|
        GameData::Item.try_get(value.upcase.to_sym)
      end.compact.uniq { |candidate| candidate.id }
    end

    def pb_get_variable_key(expression)
      match = expression.to_s.strip.match(/\ApbGet\(\s*([^\)]+)\s*\)\z/)
      return match ? game_variable_key(match[1]) : nil
    end

    def game_variable_key(expression)
      text = expression.to_s.strip
      return text.to_i if text.match?(/\A\d+\z/)
      return nil if !text.match?(/\A[A-Z][A-Za-z0-9_]*\z/)
      return Object.const_get(text).to_i if Object.const_defined?(text)
      return nil
    rescue Exception
      return nil
    end

    def merge_candidate_hashes(first, second)
      result = Hash.new { |hash, key| hash[key] = [] }
      [first, second].each do |source|
        source.each do |key, candidates|
          merge_candidates(result[key], candidates)
        end
      end
      return result
    end

    def merge_candidates(target, candidates)
      candidates.each do |candidate|
        target << candidate if candidate && !target.include?(candidate)
      end
    end

    def direct_source_uses(constraints, source_id)
      return { "starter:#{constraints[:starter]}" => 1 } if
        constraints && constraints[:starter]
      return source_id ? { source_id => 1 } : {}
    end

    def build_resource_supply
      Ironmon.tracker_area_catalog.each do |area|
        area["items"].each do |entry|
          Ironmon.tracker_area_resolved_item_ids(entry, @recipe).each do |item_id|
            @resource_supply[item_id.to_sym] += 1
          end
        end
      end
      if defined?(MiningGameScene) && MiningGameScene.const_defined?(:ITEMS)
        MiningGameScene::ITEMS.each do |entry|
          item = GameData::Item.try_get(entry[0])
          next if !item
          @resource_supply[item.id] = MAX_REQUESTED_SPECIES
        end
      end
      @resource_supply[:POKEBALL] += 1
    end

    def close_normal_evolutions(limit = nil)
      processed = 0
      while normal_evolution_pending? && (!limit || processed < limit)
        begin_normal_evolution_work if !@normal_evolution_work
        next if !@normal_evolution_work
        work = @normal_evolution_work
        branch = work[:branches][work[:branch_index]]
        plan = work[:plans][work[:plan_index]]
        next_plans = branch_plans(plan, branch, false)
        mark_possible_evolution(work[:source], branch) if !next_plans.empty?
        next_plans.each do |next_plan|
          add_plan(branch[:target_id], next_plan)
        end
        advance_normal_evolution_work
        processed += 1
      end
    end

    def normal_evolution_pending?
      @normal_evolution_queue ||= []
      return !!@normal_evolution_work || !@normal_evolution_queue.empty?
    end

    def begin_normal_evolution_work
      while !@normal_evolution_queue.empty?
        cooperative_checkpoint
        source = @normal_evolution_queue.shift
        @queued_normal_evolutions.delete(source)
        species = GameData::Species.get(source)
        branches = @normal_generator.branches_for(species)
        plans = @plans[source].dup
        next if branches.empty? || plans.empty?
        @normal_evolution_work = {
          :source => source,
          :branches => branches,
          :plans => plans,
          :branch_index => 0,
          :plan_index => 0
        }
        return
      end
      @normal_evolution_work = nil
    end

    def advance_normal_evolution_work
      work = @normal_evolution_work
      work[:plan_index] += 1
      if work[:plan_index] >= work[:plans].length
        work[:plan_index] = 0
        work[:branch_index] += 1
      end
      @normal_evolution_work = nil if
        work[:branch_index] >= work[:branches].length
    end

    def branch_plans(plan, branch, materialize_path = true)
      results = []
      branch[:effective_methods].each do |method|
        next_plan = clone_plan(plan)
        required_item = evolution_method_item(method)
        if required_item
          next_plan[:items][required_item] ||= 0
          next_plan[:items][required_item] += 1
          next if next_plan[:items][required_item] >
            @resource_supply[required_item]
        end
        next_plan[:reason] = "Evolution"
        if materialize_path
          target = GameData::Species.get(branch[:target_id])
          requirement = Ironmon.tracker_evolution_snapshot(
            method[:method], method[:parameter]
          )["requirement"]
          next_plan[:path] = path_step(
            plan[:path], nil, "Evolve into #{target.name}: #{requirement}"
          )
          next_plan.delete(:bulk_witness)
        else
          next_plan[:path] = nil
          next_plan[:bulk_witness] = [:evolution, plan, branch, method].freeze
        end
        next_plan[:path_length] = plan[:path_length].to_i + 1
        results << next_plan
      end
      return results
    end

    def evolution_method_item(method)
      return :POKEBALL if method[:method] == :Shedinja
      return nil if !ITEM_EVOLUTION_METHODS.include?(method[:method])
      item = GameData::Item.try_get(method[:parameter])
      return item ? item.id : nil
    end

    def mark_possible_evolution(source, branch)
      target = GameData::Species.get(branch[:target_id]).id
      source_number = GameData::Species.get(source).id_number
      target_number = GameData::Species.get(target).id_number
      mark_possible_evolution_numbers(source_number, target_number)
    end

    def mark_possible_evolution_numbers(source_number, target_number)
      @possible_evolution_edge_numbers[
        (source_number << 20) | target_number
      ] = true
    end

    def possible_evolution_edge?(source, target)
      source_number = GameData::Species.get(source).id_number
      target_number = GameData::Species.get(target).id_number
      return true if @possible_evolution_edge_numbers[
        (source_number << 20) | target_number
      ] == true
      if @tracker_executable_evolution_edges
        key = (source_number << 32) | target_number
        low = 0
        high = @tracker_executable_evolution_edges.length - 1
        while low <= high
          middle = (low + high) / 2
          value = @tracker_executable_evolution_edges[middle]
          return true if value == key
          if value < key
            low = middle + 1
          else
            high = middle - 1
          end
        end
        return false
      end
      return false
    end

    def apply_caught_fusion_transformations(limit = nil)
      if !@caught_fusion_entries
        @caught_fusion_entries = []
        @direct_caught_fusions.each do |identity, plans|
          plans.each { |plan| @caught_fusion_entries << [identity, plan] }
        end
      end
      processed = 0
      while @caught_fusion_index < @caught_fusion_entries.length &&
            (!limit || processed < limit)
        identity, plan = @caught_fusion_entries[@caught_fusion_index]
        apply_caught_fusion_transformation(identity, plan)
        @caught_fusion_index += 1
        processed += 1
      end
      return @caught_fusion_index >= @caught_fusion_entries.length
    end

    def apply_caught_fusion_transformation(identity, plan)
      species = GameData::Species.get(identity)
      components = [species.body_pokemon, species.head_pokemon]
      if @configuration.unfusion_setting !=
         Configuration::UNFUSION_PLAYER_CHOICE
        components.each do |component|
          component_plan = clone_plan(plan)
          choice_key = caught_unfusion_choice_key(identity, plan)
          component_plan[:constraints][choice_key] = component.id
          component_plan[:reason] = "Caught-fusion random-component unfusion"
          component_plan[:path] = path_step(
            plan[:path], nil,
            "Unfuse the caught fusion and keep #{component.name}"
          )
          component_plan[:path_length] = plan[:path_length].to_i + 1
          add_plan(component.id, component_plan)
        end
        return
      end
      components.each do |component|
        component_plan = clone_plan(plan)
        component_plan[:reason] = "Caught-fusion unfusion"
        component_plan[:path] = path_step(
          plan[:path], nil,
          "Unfuse the caught fusion and keep #{component.name}"
        )
        component_plan[:path_length] = plan[:path_length].to_i + 1
        add_plan(component.id, component_plan)
      end
    end

    def caught_unfusion_choice_key(identity, plan)
      sources = plan[:source_uses].keys.map(&:to_s).sort.join("|")
      return "caught_unfusion:#{identity}:#{sources}".to_sym
    end

    def prepare_material_pairs
      @material_ids = @plans.keys.select do |identity|
        species = GameData::Species.try_get(identity)
        species && !species.is_a?(GameData::FusedSpecies) &&
          !@plans[identity].empty?
      end.sort_by { |identity| GameData::Species.get(identity).id_number }
      @pair_count = (@material_ids.length * (@material_ids.length + 1)) / 2
      @excluded_material_pair_offsets = []
      @direct_fusion_pair_offsets = []
      pair_offset = 0
      @material_ids.each_with_index do |first, first_index|
        (first_index...@material_ids.length).each do |second_index|
          second = @material_ids[second_index]
          @excluded_material_pair_offsets << pair_offset if
            !compatible_material_pair?(first, second)
          first_number = GameData::Species.get(first).id_number
          second_number = GameData::Species.get(second).id_number
          pair_code = (first_number << 10) | second_number
          @direct_fusion_pair_offsets << pair_offset if
            @direct_fusion_material_pairs[pair_code]
          pair_offset += 1
        end
      end
      @material_pairs_prepared = true
      @phase = @pair_count > 0 ? :player_fusions : :complete
    end

    def player_fusion_plan_combination(body_plan, head_plan)
      return if !compatible_constraints?(
        body_plan[:constraints], head_plan[:constraints]
      )
      items = merge_counts(body_plan[:items], head_plan[:items])
      return if !within_supply?(items)
      source_uses = merge_counts(
        body_plan[:source_uses], head_plan[:source_uses]
      )
      return if source_uses.any? { |_source, count| count > 1 }
      return {
        :items => items,
        :constraints => body_plan[:constraints].merge(
          head_plan[:constraints]
        ),
        :source_uses => source_uses,
        :path_length => body_plan[:path_length].to_i +
          head_plan[:path_length].to_i + 1
      }
    end

    def add_player_fusion_combination(fusion, body, head, body_plan, head_plan,
                                      combination, materialize_path = true)
      path = nil
      bulk_witness = nil
      if materialize_path
        path = fusion_path_step(
          body_plan[:path], head_plan[:path], body.id, head.id
        )
      else
        bulk_witness = [
          :fusion, body.id, head.id, body_plan, head_plan
        ].freeze
      end
      return add_plan(fusion, {
        :items => combination[:items],
        :constraints => materialize_path ? combination[:constraints] : {},
        :source_uses => materialize_path ? combination[:source_uses] : {},
        :reason => "Player fusion",
        :path => path,
        :bulk_witness => bulk_witness,
        :path_length => combination[:path_length]
      })
    end

    def add_plan(identity, plan)
      fusion_identity = Ironmon.fusion_species_identity(identity)
      species = nil
      if fusion_identity
        key = fusion_identity
        fused = true
      else
        species = identity.respond_to?(:id) ? identity :
          GameData::Species.try_get(identity)
        return false if !species
        key = species.id
        fused = species.is_a?(GameData::FusedSpecies)
      end
      plans = @plans[key]
      newly_obtainable = plans.empty?
      return false if plans.any? { |existing| plan_dominates?(existing, plan) }
      plans.delete_if { |existing| plan_dominates?(plan, existing) }
      plans << plan
      if plans.length > 1
        plans.sort_by! do |entry|
          [entry[:items].values.inject(0, :+), entry[:constraints].length,
           entry[:path_length].to_i]
        end
      end
      added = true
      if plans.length > MAX_PLANS_PER_SPECIES
        plans.slice!(MAX_PLANS_PER_SPECIES, plans.length)
        added = plans.include?(plan)
      end
      @obtainable_count = @obtainable_count.to_i + 1 if
        added && newly_obtainable
      if added && !fused
        @queued_normal_evolutions ||= {}
        @normal_evolution_queue ||= []
        if !@queued_normal_evolutions[key]
          @queued_normal_evolutions[key] = true
          @normal_evolution_queue << key
        end
      end
      return added
    end

    def plan_dominates?(first, second)
      return false if first[:constraints] != second[:constraints]
      return false if first[:items].any? do |item_id, count|
        count.to_i > second[:items][item_id].to_i
      end
      return false if first[:source_uses].any? do |source_id, count|
        count.to_i > second[:source_uses][source_id].to_i
      end
      return true
    end

    def compatible_constraints?(first, second)
      return !first.any? do |key, value|
        second.key?(key) && second[key] != value
      end
    end

    def merge_counts(first, second)
      result = first.dup
      second.each do |item_id, count|
        result[item_id] = result[item_id].to_i + count.to_i
      end
      return result
    end

    def within_supply?(items)
      return items.all? do |item_id, count|
        count.to_i <= @resource_supply[item_id]
      end
    end

    def clone_plan(plan)
      return {
        :items => plan[:items].dup,
        :constraints => plan[:constraints].dup,
        :source_uses => plan[:source_uses].dup,
        :reason => plan[:reason],
        :path => plan[:path],
        :bulk_witness => plan[:bulk_witness],
        :path_length => plan[:path_length]
      }
    end

    def best_plan(identity)
      return nil if !@plans.key?(identity)
      plans = @plans[identity]
      return nil if !plans || plans.empty?
      return plans.min_by do |plan|
        [plan[:items].values.inject(0, :+), plan[:path_length].to_i]
      end
    end

    def path_step(first, second, text)
      return [first, second, text].freeze
    end

    def fusion_path_step(first, second, body_id, head_id)
      detail = [:fusion, body_id, head_id].freeze
      return [first, second, detail].freeze
    end

    def materialize_path(path)
      result = []
      seen_steps = {}
      pending = [[path, false]]
      until pending.empty?
        current, exiting = pending.pop
        next if !current
        if !exiting
          pending << [current, true]
          pending << [current[1], false] if current[1]
          pending << [current[0], false] if current[0]
          next
        end
        detail = current[2]
        step = if detail.is_a?(Array) && detail[0] == :fusion
                 body = GameData::Species.get(detail[1])
                 head = GameData::Species.get(detail[2])
                 "Fuse #{body.name} as Body with #{head.name} as Head"
               else
                 detail.to_s
               end
        next if step.empty? || seen_steps[step]
        seen_steps[step] = true
        result << step
      end
      return result
    end

    def materialize_plan_path(plan)
      return materialize_path(plan[:path]) if plan[:path]
      witness = plan[:bulk_witness]
      return [] if !witness
      if witness[0] == :fusion
        body = GameData::Species.get(witness[1])
        head = GameData::Species.get(witness[2])
        steps = materialize_plan_path(witness[3]) +
          materialize_plan_path(witness[4])
        steps << "Fuse #{body.name} as Body with #{head.name} as Head"
        return steps.uniq
      end
      if witness[0] == :evolution
        source_plan = witness[1]
        branch = witness[2]
        method = witness[3]
        target = GameData::Species.get(branch[:target_id])
        requirement = Ironmon.tracker_evolution_snapshot(
          method[:method], method[:parameter]
        )["requirement"]
        return (materialize_plan_path(source_plan) + [
          "Evolve into #{target.name}: #{requirement}"
        ]).uniq
      end
      return []
    end

    def species_generator
      return @species_generator if @species_generator
      @species_generator = SpeciesGenerator.new(
        @recipe["seed"], :wild,
        @configuration.wild_policy,
        Ironmon.normal_species_pool,
        Ironmon.custom_fusion_pool, {},
        @recipe["species_generator_version"]
      )
      return @species_generator
    end

    def encounter_mode
      if @recipe["data_mode"] == "remix" &&
         defined?(GameData::EncounterModern)
        return GameData::EncounterModern
      end
      return GameData::Encounter
    end

    def normalize_species_id(species)
      data = species.respond_to?(:id) ? species : GameData::Species.get(species)
      return data.id
    end

    def stringify_counts(counts)
      result = {}
      counts.each { |item_id, count| result[item_id.to_s] = count }
      return result
    end
  end

  def self.tracker_obtainability_services
    @tracker_obtainability_services ||= {}
  end

  def self.tracker_obtainability_service(recipe)
    key = recipe["run_id"]
    service = tracker_obtainability_services[key]
    return service if service
    service = TrackerObtainabilityService.new(recipe, true)
    tracker_store_bounded(tracker_obtainability_services, key, service, 4)
    return service
  end

  # Calculation access is public; target proofs and world discovery stay on
  # their existing authorized routes. Never forward arbitrary query fields.
  def self.tracker_run_lookup_preparation(payload, envelope_run_id)
    attempt = current_run_attempt
    if !active? || !$PokemonGlobal || !attempt || attempt["result"] != "active"
      raise TrackerLookupError.new(
        "run_unavailable", "No active Ironmon run is available for preparation."
      )
    end
    if envelope_run_id.to_s.empty? || envelope_run_id != attempt["run_id"]
      raise TrackerLookupError.new(
        "run_mismatch", "The request and active run identify different runs."
      )
    end
    payload ||= {}
    request = {}
    ["foreground", "fusion_closure_result", "fusion_closure_worker_unavailable"].each do |key|
      request[key] = payload[key] if payload.key?(key)
    end
    snapshot = tracker_obtainability_for_recipe(request, tracker_debug_active_recipe)
    response = {}
    ["phase", "complete", "background_complete", "processed_pairs",
     "total_pairs", "obtainable_count", "fusion_closure_work"].each do |key|
      response[key] = snapshot[key] if snapshot.key?(key)
    end
    return response
  end

  def self.tracker_obtainability(payload, envelope_run_id)
    payload ||= {}
    recipe = tracker_validate_completed_recipe(
      payload["recipe"], envelope_run_id
    )
    return tracker_obtainability_for_recipe(payload, recipe)
  end

  def self.tracker_obtainability_for_recipe(payload, recipe)
    payload ||= {}
    species_key = payload["species_id"].to_s.split(":", 2)[0].to_s
    species = species_key.empty? ? nil :
      GameData::Species.try_get(species_key.to_sym)
    if species_key != "" && (!species || !tracker_lookup_species_available?(species))
      raise TrackerLookupError.new(
        "pokemon_not_found", "The selected Pokemon is not available in this run."
      )
    end
    requested_species_ids = payload["species_ids"]
    requested_species_ids = [] if !requested_species_ids.is_a?(Array)
    if requested_species_ids.length >
       TrackerObtainabilityService::MAX_REQUESTED_SPECIES
      raise TrackerLookupError.new(
        "invalid_query", "Too many Pokemon were requested for obtainability."
      )
    end
    requested_edge_keys = payload["evolution_edge_keys"]
    requested_edge_keys = [] if !requested_edge_keys.is_a?(Array)
    if requested_edge_keys.length >
       TrackerObtainabilityService::MAX_REQUESTED_EVOLUTION_EDGES
      raise TrackerLookupError.new(
        "invalid_query", "Too many evolution connections were requested for obtainability."
      )
    end
    service = tracker_obtainability_service(recipe)
    service.report_tracker_closure_unavailable if
      payload["fusion_closure_worker_unavailable"] == true
    service.apply_fusion_closure_result(payload["fusion_closure_result"])
    service.request_background
    service.request_foreground if payload["foreground"] == true
    prefix = "#{recipe["run_id"]}|"
    sections = ["overview", "abilities", "stats", "moves", "evolutions"]
    tracker_lookup_cache.keys.each do |key|
      parts = key.to_s.split("|", 4)
      tracker_lookup_cache.delete(key) if key.to_s.start_with?(prefix) &&
        sections.include?(parts[2])
    end
    return service.snapshot(species, requested_species_ids, requested_edge_keys)
  end

  def self.tracker_obtainability_snapshot(species, recipe)
    service = tracker_obtainability_service(recipe)
    return service.passive_target_snapshot(species)
  end

  def self.tracker_obtainability_status(species, recipe)
    return tracker_obtainability_snapshot(species, recipe)["status"]
  end

  def self.tracker_obtainability_identity_status(species_id, recipe)
    identity = species_id.to_s.split(":", 2)[0]
    fusion = /\AB(\d+)H(\d+)\z/.match(identity)
    fusion_number = nil
    if fusion
      fusion_number = (fusion[1].to_i * NB_POKEMON) + fusion[2].to_i
    else
      species = GameData::Species.try_get(identity.to_sym)
      return nil if !species
      identity = species.id.to_s
    end
    service = tracker_obtainability_service(recipe)
    return service.passive_identity_status(identity.to_sym, fusion_number)
  end

  def self.update_tracker_obtainability
    return if !tracker_obtainability_runtime_ready?
    foreground = tracker_obtainability_services.values.select do |service|
      service.foreground_requested?
    end.max_by { |service| service.foreground_deadline }
    if foreground
      foreground.advance_for_milliseconds(
        TrackerObtainabilityService::FOREGROUND_MILLISECONDS
      )
      return
    end
    return if !tracker_obtainability_background_safe?
    service = tracker_obtainability_services.values.select do |candidate|
      candidate.background_requested?
    end.max_by { |candidate| candidate.background_deadline }
    return if !service
    service.advance_for_milliseconds(
      TrackerObtainabilityService::BACKGROUND_MILLISECONDS
    )
  rescue Exception => e
    echoln "Ironmon obtainability background update failed: #{e.message}"
  end

  def self.tracker_obtainability_game_work_pending?
    return false if !@tracker_obtainability_services
    return tracker_obtainability_services.values.any? do |service|
      service.game_work_priority_requested?
    end
  end

  def self.reset_tracker_obtainability_worker_failures
    return false if !@tracker_obtainability_services
    original_count = @tracker_obtainability_services.length
    @tracker_obtainability_services.delete_if do |_key, service|
      service.tracker_closure_unavailable?
    end
    return @tracker_obtainability_services.length != original_count
  end

  def self.mark_tracker_obtainability_map_ready(scene)
    @tracker_obtainability_ready_scene = scene
  end

  def self.pause_tracker_obtainability_for_map
    @tracker_obtainability_ready_scene = nil
  end

  def self.tracker_obtainability_runtime_ready?
    return false if !active? || !$PokemonGlobal || tracker_battle_id
    return false if !tracker_connection.connected?
    return false if instance_variable_get(:@reset_in_progress)
    return false if instance_variable_get(:@tracker_seed_import_pending)
    return false if !defined?($scene) || !defined?(Scene_Map) ||
      !$scene.is_a?(Scene_Map)
    return @tracker_obtainability_ready_scene.equal?($scene)
  end

  def self.tracker_obtainability_background_safe?
    return false if !tracker_obtainability_runtime_ready?
    return false if defined?(pbMapInterpreterRunning?) &&
      pbMapInterpreterRunning?
    return true
  end
end
