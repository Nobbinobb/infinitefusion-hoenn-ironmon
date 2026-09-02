#===============================================================================
# Ironmon item-randomization readiness and saved-run compatibility
#===============================================================================

module Ironmon
  def self.prepare_item_randomization
    raise ItemRandomizationError, "run metadata is unavailable" if !$PokemonGlobal
    rules = ItemSlotGenerator::POOL_RULES_VERSION
    ground_pool = item_ground_pool(rules)
    tm_pool = item_tm_pool(rules)
    record_generator_metadata({
      :ironmon_item_generator_version => ItemSlotGenerator::SCHEMA_VERSION,
      :ironmon_item_pool_rules_version => rules,
      :ironmon_item_ground_pool_size => ground_pool.length,
      :ironmon_item_ground_pool_fingerprint =>
        item_ground_pool_fingerprint(rules),
      :ironmon_item_tm_pool_size => tm_pool.length,
      :ironmon_item_tm_pool_fingerprint => item_tm_pool_fingerprint(rules),
      :ironmon_item_result_ban_fingerprint =>
        item_result_ban_fingerprint(rules),
      :ironmon_item_shop_policy_version =>
        ItemSlotGenerator::SHOP_POLICY_VERSION
    })
    $PokemonGlobal.randomItemsHash = {} if
      $PokemonGlobal.respond_to?(:randomItemsHash=)
    $PokemonGlobal.randomTMsHash = {} if
      $PokemonGlobal.respond_to?(:randomTMsHash=)
    disable_base_item_randomization
    reset_item_generator_cache
    item_slot_generator
    @item_randomization_ready = true
    @item_randomization_error_message = nil
    return true
  rescue Exception => e
    @item_randomization_ready = false
    @item_randomization_error_message = _INTL(
      "Ironmon could not prepare item randomization: {1}", e.message
    )
    echoln @item_randomization_error_message
    return false
  end

  def self.current_item_randomization?
    return false if !$PokemonGlobal
    rules = $PokemonGlobal.ironmon_item_pool_rules_version
    return false if
      !ItemSlotGenerator::RESULT_BANS_BY_RULES_VERSION.key?(rules)
    return generator_metadata_matches?([
      ["generator schema", $PokemonGlobal.ironmon_item_generator_version,
       ItemSlotGenerator::SCHEMA_VERSION],
      ["ground pool size", $PokemonGlobal.ironmon_item_ground_pool_size,
       item_ground_pool(rules).length],
      ["ground pool catalog",
       $PokemonGlobal.ironmon_item_ground_pool_fingerprint,
       item_ground_pool_fingerprint(rules)],
      ["TM pool size", $PokemonGlobal.ironmon_item_tm_pool_size,
       item_tm_pool(rules).length],
      ["TM pool catalog", $PokemonGlobal.ironmon_item_tm_pool_fingerprint,
       item_tm_pool_fingerprint(rules)],
      ["result-ban catalog",
       $PokemonGlobal.ironmon_item_result_ban_fingerprint,
       item_result_ban_fingerprint(rules)],
      ["shop policy", $PokemonGlobal.ironmon_item_shop_policy_version,
       ItemSlotGenerator::SHOP_POLICY_VERSION]
    ])
  rescue Exception
    return false
  end

  def self.ensure_item_randomization
    @item_randomization_ready = false
    return false if !$PokemonGlobal
    if !current_item_randomization?
      raise ItemRandomizationError,
            "the saved item generator or item pools are incompatible"
    end
    reset_item_generator_cache
    item_slot_generator
    disable_base_item_randomization
    @item_randomization_ready = true
    return true
  end

  def self.item_randomization_active?
    return active? && @item_randomization_ready == true
  end

  def self.item_slot_generator
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    rules = if $PokemonGlobal
              $PokemonGlobal.ironmon_item_pool_rules_version
            else
              ItemSlotGenerator::POOL_RULES_VERSION
            end
    rules ||= ItemSlotGenerator::POOL_RULES_VERSION
    if !@item_slot_generator || @item_slot_generator_seed != seed ||
       @item_slot_generator_rules != rules
      @item_slot_generator_seed = seed
      @item_slot_generator_rules = rules
      @item_slot_generator = build_item_slot_generator(seed, rules)
    end
    return @item_slot_generator
  end

  def self.build_item_slot_generator(seed, rules)
    return ItemSlotGenerator.new(
      seed, item_ground_pool(rules), item_tm_pool(rules),
      item_ground_weights(rules)
    )
  end

  def self.reset_item_generator_cache
    @item_slot_generator = nil
    @item_slot_generator_seed = nil
    @item_slot_generator_rules = nil
  end

  def self.suspend_item_randomization
    @item_randomization_ready = false
    reset_item_generator_cache
  end

  def self.item_randomization_error_message
    return @item_randomization_error_message ||
      _INTL("Ironmon could not prepare item randomization.")
  end

  def self.disable_base_item_randomization
    return if !$game_switches
    [
      SWITCH_RANDOM_ITEMS_GENERAL, SWITCH_RANDOM_ITEMS,
      SWITCH_RANDOM_FOUND_ITEMS, SWITCH_RANDOM_GIVEN_ITEMS,
      SWITCH_RANDOM_ITEMS_MAPPED, SWITCH_RANDOM_ITEMS_DYNAMIC,
      SWITCH_RANDOM_TMS, SWITCH_RANDOM_FOUND_TMS, SWITCH_RANDOM_GIVEN_TMS,
      SWITCH_RANDOM_SHOP_ITEMS, SWITCH_RANDOM_HELD_ITEMS
    ].each { |switch_id| $game_switches[switch_id] = false }
  end

  def self.item_generator_recipe
    return nil if !$PokemonGlobal ||
      !$PokemonGlobal.ironmon_item_generator_version
    rules = $PokemonGlobal.ironmon_item_pool_rules_version
    return {
      "version" => $PokemonGlobal.ironmon_item_generator_version,
      "rules_version" => rules,
      "ground_pool_size" => $PokemonGlobal.ironmon_item_ground_pool_size,
      "ground_pool_fingerprint" =>
        $PokemonGlobal.ironmon_item_ground_pool_fingerprint,
      "ground_total_weight" => item_ground_total_weight(rules),
      "tm_pool_size" => $PokemonGlobal.ironmon_item_tm_pool_size,
      "tm_pool_fingerprint" =>
        $PokemonGlobal.ironmon_item_tm_pool_fingerprint,
      "result_bans" => item_result_bans(rules).map { |item| item.to_s },
      "result_ban_fingerprint" =>
        $PokemonGlobal.ironmon_item_result_ban_fingerprint,
      "shop_policy_version" =>
        $PokemonGlobal.ironmon_item_shop_policy_version
    }
  end
end

Ironmon.register_game_load_hook(
  :item_randomization,
  proc { |_save_data| Ironmon.suspend_item_randomization },
  proc do |_save_data, _result|
    next if Ironmon.checkpoint_reset_loading?
    Ironmon.ensure_item_randomization if Ironmon.active?
  end
)
