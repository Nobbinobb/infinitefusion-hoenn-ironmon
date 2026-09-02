module IronmonItemRandomizationRuntimeTests
  OUTPUT_PATH = $ironmon_item_randomization_test_output_path.to_s

  def self.assert(condition, message)
    raise "Item randomization runtime test failed: #{message}" if !condition
  end

  def self.run
    original_global = $PokemonGlobal
    original_switches = $game_switches
    begin
      $PokemonGlobal = PokemonGlobalMetadata.new
      $PokemonGlobal.ironmon_mode = true
      $PokemonGlobal.ironmon_seed = 12_345
      $game_switches = []
      ground_pool = Ironmon.item_ground_pool
      tm_pool = Ironmon.item_tm_pool
      assert(!ground_pool.empty?, "ground pool is populated")
      assert(!tm_pool.empty?, "TM pool is populated")
      assert(ground_pool == ground_pool.sort_by { |item| item.to_s },
             "ground pool has stable identifier ordering")
      assert(tm_pool == tm_pool.sort_by { |item| item.to_s },
             "TM pool has stable identifier ordering")
      Ironmon.item_result_bans.each do |item|
        assert(!ground_pool.include?(item), "#{item} is banned from results")
      end
      assert(!Ironmon.item_ground_pool(1).include?(:AIRMAIL),
             "rules version 1 uses the current result bans")
      assert(!ground_pool.include?(:AIRMAIL), "Mail is excluded")
      assert(!ground_pool.include?(:REDAPRICORN), "Apricorns are excluded")
      assert(!ground_pool.include?(:EXPSHARE), "Exp. Share is excluded")
      assert(Ironmon.item_result_category(GameData::Item.get(:POTION)) ==
               :hp_recovery, "Potion is classified as HP recovery")
      assert(Ironmon.item_result_category(GameData::Item.get(:ETHER)) ==
               :status_pp_recovery, "Ether is classified as recovery")
      assert(Ironmon.item_result_category(GameData::Item.get(:LEFTOVERS)) ==
               :held_combat, "Leftovers is classified as held combat")
      assert(Ironmon.item_result_category(GameData::Item.get(:TM01)) == :tm,
             "TM01 is classified as a TM")
      assert(Ironmon.item_result_weight(GameData::Item.get(:POTION)) == 32,
             "Potion receives 32 tickets")
      assert(Ironmon.item_result_weight(GameData::Item.get(:LEFTOVERS)) == 1,
             "Leftovers receives one ticket")
      assert(Ironmon.item_result_category(GameData::Item.get(:BURNDRIVE)) ==
               :held_combat, "direct held effects are classified as combat")
      GameData::Item.each do |item|
        if item.is_mail? || item.is_apricorn?
          assert(Ironmon.item_result_banned?(item),
                 "#{item.id} category is covered by the versioned bans")
        end
      end
      Ironmon::ItemSlotGenerator::HM_TOOL_RESULT_BANS.each do |tool|
        assert(!ground_pool.include?(tool), "#{tool} HM tool is excluded")
        assert(Ironmon.item_ground_source_randomizable?(GameData::Item.get(tool)),
               "#{tool} ground sources randomize away")
      end
      Ironmon::HM_TOOL_BY_ITEM.each do |hm, tool|
        assert(!ground_pool.include?(hm), "#{hm} is excluded")
        assert(!ground_pool.include?(tool), "#{tool} is excluded")
      end
      tm_pool.each do |item|
        assert(GameData::Item.get(item).is_TM?, "TM pool contains only TMs")
        assert(ground_pool.include?(item), "TM pool is part of the ground pool")
      end

      generator = Ironmon.build_item_slot_generator(
        12_345, Ironmon::ItemSlotGenerator::POOL_RULES_VERSION
      )
      first = generator.ground_item("map:1|event:2")
      assert(first == generator.ground_item("map:1|event:2"),
             "a slot is deterministic")
      reverse_generator = Ironmon.build_item_slot_generator(
        12_345, Ironmon::ItemSlotGenerator::POOL_RULES_VERSION
      )
      reverse_generator.ground_item("map:9|event:9")
      assert(first == reverse_generator.ground_item("map:1|event:2"),
             "slot resolution is order independent")
      results = (1..100).map do |event_id|
        generator.ground_item("map:1|event:#{event_id}")
      end
      assert(results.uniq.length > 1, "different slots can produce different items")
      second_seed_results = (1..100).map do |event_id|
        Ironmon.build_item_slot_generator(
          54_321, Ironmon::ItemSlotGenerator::POOL_RULES_VERSION
        ).ground_item("map:1|event:#{event_id}")
      end
      assert(results != second_seed_results, "a new seed rerolls item slots")
      ticket_generator = Ironmon::ItemSlotGenerator.new(
        9_876, [:LIGHT, :HEAVY], [:TM01], [1, 8]
      )
      ticket_results = (1..10_000).map do |event_id|
        ticket_generator.ground_item("weighted:#{event_id}")
      end
      assert(ticket_results.count(:HEAVY) > ticket_results.count(:LIGHT) * 7,
             "integer tickets increase deterministic selection frequency")
      begin
        generator.ground_item(nil)
        assert(false, "a missing slot identity must fail")
      rescue Ironmon::ItemRandomizationError
      end
      assert(
        Ironmon.resolve_ground_reward(:POTION, "map:1|event:2", generator) ==
          Ironmon.resolve_ground_reward(:ANTIDOTE, "map:1|event:2", generator),
        "the authored ordinary item does not influence a slot"
      )
      assert(Ironmon.resolve_ground_reward(:HM01, "map:1|event:2", generator) == :HM01,
             "an HM source remains protected")
      banned_source_result = Ironmon.resolve_ground_reward(
        :DNASPLICERS, "map:1|event:4", generator
      )
      assert(!Ironmon.item_result_bans.include?(banned_source_result),
             "a banned ground source randomizes to an allowed result")
      tool_source_result = Ironmon.resolve_ground_reward(
        :MACHETE, "map:99|event:8", generator
      )
      assert(!Ironmon.item_result_bans.include?(tool_source_result),
             "an authored HM-tool ground source randomizes away")
      starter_slot = Ironmon::ItemSlotGenerator::SPECIAL_GROUND_SLOTS[
        :starter_rescue_reward
      ]
      assert(
        Ironmon.resolve_ground_reward(starter_slot[1], starter_slot[0], generator) ==
          generator.ground_item(starter_slot[0]),
        "the starter rescue reward uses its stable ground slot"
      )
      assert(Ironmon.resolve_tm_gift(:TM01, "map:1|event:3", generator),
             "a TM gift resolves")
      assert(
        GameData::Item.get(
          Ironmon.resolve_tm_gift(:TM01, "map:1|event:3", generator)
        ).is_TM?,
        "a TM gift stays a TM"
      )
      mart = [:POTION, :POKEBALL, :SUPERREPEL, :FUSIONREPEL, :ESCAPEROPE]
      assert(
        Ironmon.standard_mart_stock(mart) ==
          [:POKEBALL, :SUPERREPEL, :FUSIONREPEL],
        "standard marts retain only existing balls and repels"
      )

      historical_rules = 1
      $PokemonGlobal.ironmon_item_generator_version =
        Ironmon::ItemSlotGenerator::SCHEMA_VERSION
      $PokemonGlobal.ironmon_item_pool_rules_version = historical_rules
      $PokemonGlobal.ironmon_item_ground_pool_size =
        Ironmon.item_ground_pool(historical_rules).length
      $PokemonGlobal.ironmon_item_ground_pool_fingerprint =
        Ironmon.item_ground_pool_fingerprint(historical_rules)
      $PokemonGlobal.ironmon_item_tm_pool_size =
        Ironmon.item_tm_pool(historical_rules).length
      $PokemonGlobal.ironmon_item_tm_pool_fingerprint =
        Ironmon.item_tm_pool_fingerprint(historical_rules)
      $PokemonGlobal.ironmon_item_result_ban_fingerprint =
        Ironmon.item_result_ban_fingerprint(historical_rules)
      $PokemonGlobal.ironmon_item_shop_policy_version =
        Ironmon::ItemSlotGenerator::SHOP_POLICY_VERSION
      assert(Ironmon.current_item_randomization?,
             "rules version 1 save metadata remains valid")

      assert(Ironmon.prepare_item_randomization, "metadata preparation succeeds")
      assert(Ironmon.current_item_randomization?, "saved metadata validates")
      assert($PokemonGlobal.randomItemsHash.empty?, "legacy item map is cleared")
      assert($PokemonGlobal.randomTMsHash.empty?, "legacy TM map is cleared")
      recipe = Ironmon.item_generator_recipe
      assert(recipe["ground_pool_size"] == ground_pool.length,
             "recipe records the ground pool")
      assert(recipe["ground_total_weight"] ==
               Ironmon.item_ground_weights.sum,
             "recipe records the ground ticket total")
      assert(recipe["tm_pool_size"] == tm_pool.length,
             "recipe records the TM pool")
      area = Ironmon.tracker_area_catalog.find do |candidate|
        candidate["items"].any? do |entry|
          !entry["authored_item_ids"].empty?
        end
      end
      entry = area["items"].find do |candidate|
        !candidate["authored_item_ids"].empty?
      end
      lookup_recipe = {
        "seed" => $PokemonGlobal.ironmon_seed,
        "item_generator" => recipe
      }
      lookup = Ironmon.tracker_area_item_entries(
        { "items" => [entry] }, lookup_recipe, {}, true, false
      ).first
      assert(lookup["details_revealed"],
             "full item lookup reveals generated details")
      assert(lookup["items"].length == 1,
             "full item lookup returns the generated item")
      catalog_items = Ironmon.tracker_area_catalog.flat_map do |candidate|
        candidate["items"]
      end
      assert(
        catalog_items.all? do |catalog_item|
          !catalog_item["authored_item_ids"].empty?
        end,
        "the item catalog excludes empty hidden-item decoys"
      )
      debug_items = Ironmon.tracker_area_item_entries(
        { "items" => catalog_items }, lookup_recipe, {}, true, false
      )
      assert(
        debug_items.all? do |debug_item|
          debug_item["details_revealed"] && !debug_item["items"].empty?
        end,
        "full item debug access reveals every catalogued item"
      )
      trick_house_tool = debug_items.find do |debug_item|
        debug_item["entry_id"] == "item:99:8"
      end
      assert(trick_house_tool && trick_house_tool["items"].length == 1,
             "the Trick House tool slot resolves to one generated result")
      assert(
        trick_house_tool["items"].none? do |resolved|
          Ironmon.item_result_bans.include?(resolved["item_id"].to_sym)
        end,
        "Lookup does not expose an authored HM tool as the slot result"
      )
      $PokemonGlobal.ironmon_item_ground_pool_fingerprint = "incompatible"
      assert(!Ironmon.current_item_randomization?,
             "incompatible saved fingerprints are rejected")
      File.binwrite(OUTPUT_PATH, "item randomization runtime tests passed\n")
    ensure
      Ironmon.suspend_item_randomization
      $PokemonGlobal = original_global
      $game_switches = original_switches
    end
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonItemRandomizationRuntimeTests.run
