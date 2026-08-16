#===============================================================================
# Ironmon deterministic item-slot randomization
#===============================================================================

module Ironmon
  class ItemRandomizationError < StandardError; end

  class ItemSlotGenerator
    SCHEMA_VERSION = 1
    POOL_RULES_VERSION = 2
    SHOP_POLICY_VERSION = 1
    FNV_OFFSET_BASIS = 14_695_981_039_346_656_037
    FNV_PRIME = 1_099_511_628_211
    FNV_MASK = 0xFFFFFFFFFFFFFFFF

    UNSUPPORTED_RESULTS = [
      :COVERFOSSIL, :PLUMEFOSSIL, :ACCURACYUP, :DAMAGEUP, :ANCIENTSTONE,
      :ODDKEYSTONE_FULL, :DEVOLUTIONSPRAY, :INVISIBALL, :DEBUGCANDY
    ].freeze
    BASE_RESULT_BANS = [
      :DNASPLICERS, :SUPERSPLICERS, :DNAREVERSER, :DYNAMITE
    ].freeze
    MAIL_RESULT_BANS = [
      :AIRMAIL, :BLOOMMAIL, :BRICKMAIL, :BUBBLEMAIL, :FLAMEMAIL, :GRASSMAIL,
      :HEARTMAIL, :MOSAICMAIL, :SNOWMAIL, :SPACEMAIL, :STEELMAIL, :TUNNELMAIL
    ].freeze
    APRICORN_RESULT_BANS = [
      :BLACKAPRICORN, :BLUEAPRICORN, :GREENAPRICORN, :PINKAPRICORN,
      :REDAPRICORN, :WHITEAPRICORN, :YELLOWAPRICORN
    ].freeze
    HM_TOOL_RESULT_BANS = [
      :MACHETE, :TELEPORTER, :SURFBOARD, :LEVER, :JETPACK, :PICKAXE,
      :SCUBAGEAR, :LANTERN, :CLIMBINGGEAR
    ].freeze
    RESULT_BANS_BY_RULES_VERSION = {
      1 => BASE_RESULT_BANS,
      2 => (BASE_RESULT_BANS + MAIL_RESULT_BANS + APRICORN_RESULT_BANS +
        [:EXPSHARE] + HM_TOOL_RESULT_BANS).freeze
    }.freeze
    REPEL_ITEMS = [:REPEL, :SUPERREPEL, :MAXREPEL, :FUSIONREPEL].freeze
    PROTECTED_HM_ITEMS = [
      :HM01, :HM02, :HM03, :HM04, :HM05, :HM06, :HM07, :HM08, :HM09, :HM10,
      :HM11
    ].freeze
    SPECIAL_GROUND_SLOTS = {
      :starter_rescue_reward => [
        "special:starter_rescue_reward", :POTION
      ].freeze
    }.freeze

    attr_reader :seed
    attr_reader :ground_pool
    attr_reader :tm_pool

    def initialize(seed, ground_pool, tm_pool)
      @seed = seed.to_i
      @ground_pool = ground_pool
      @tm_pool = tm_pool
    end

    def ground_item(slot_id)
      return select(@ground_pool, "ground", slot_id)
    end

    def tm_gift(slot_id)
      return select(@tm_pool, "tm_gift", slot_id)
    end

    def select(pool, channel, slot_id)
      identity = slot_id.to_s
      if identity.empty?
        raise ItemRandomizationError, "a stable item slot identity is required"
      end
      input = [SCHEMA_VERSION, @seed, channel, identity].join("|")
      return pool[hash_value(input) % pool.length]
    end

    def hash_value(value)
      hash = FNV_OFFSET_BASIS
      value.each_byte do |byte|
        hash ^= byte
        hash = (hash * FNV_PRIME) & FNV_MASK
      end
      return hash
    end
  end

  def self.item_result_bans(rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    bans = ItemSlotGenerator::RESULT_BANS_BY_RULES_VERSION[rules_version]
    if !bans
      raise ItemRandomizationError,
            "item pool rules version #{rules_version} is not supported"
    end
    return bans
  end

  def self.item_result_unsupported?(item)
    return ItemSlotGenerator::UNSUPPORTED_RESULTS.include?(item.id)
  end

  def self.item_result_banned?(item, rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    return item_result_bans(rules_version).include?(item.id)
  end

  def self.item_ground_pool_eligible?(item, rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    return false if !item
    return false if item.is_key_item?
    return false if ItemSlotGenerator::PROTECTED_HM_ITEMS.include?(item.id)
    return false if item_result_unsupported?(item)
    return false if item_result_banned?(item, rules_version)
    return item.is_TM? if item.is_machine?
    return true
  end

  def self.item_ground_source_randomizable?(item)
    return false if !item
    return true if item_result_banned?(item)
    return false if item.is_key_item?
    return false if ItemSlotGenerator::PROTECTED_HM_ITEMS.include?(item.id)
    return item.is_TM? if item.is_machine?
    return true
  end

  def self.item_tm_pool_eligible?(item, rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    return false if !item || !item.is_TM?
    return false if ItemSlotGenerator::PROTECTED_HM_ITEMS.include?(item.id)
    return false if item_result_unsupported?(item)
    return false if item_result_banned?(item, rules_version)
    return true
  end

  def self.item_ground_pool(rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    @item_ground_pools ||= {}
    return @item_ground_pools[rules_version] if @item_ground_pools[rules_version]
    pool = []
    GameData::Item.each do |item|
      pool << item.id if item_ground_pool_eligible?(item, rules_version)
    end
    pool.sort_by! { |item_id| item_id.to_s }
    if pool.empty?
      raise ItemRandomizationError, "the unified ground item pool is empty"
    end
    @item_ground_pools[rules_version] = pool.freeze
    return @item_ground_pools[rules_version]
  end

  def self.item_tm_pool(rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    @item_tm_pools ||= {}
    return @item_tm_pools[rules_version] if @item_tm_pools[rules_version]
    pool = []
    GameData::Item.each do |item|
      pool << item.id if item_tm_pool_eligible?(item, rules_version)
    end
    pool.sort_by! { |item_id| item_id.to_s }
    if pool.empty?
      raise ItemRandomizationError, "the TM gift pool is empty"
    end
    @item_tm_pools[rules_version] = pool.freeze
    return @item_tm_pools[rules_version]
  end

  def self.item_fingerprint(entries)
    value = ItemSlotGenerator::FNV_OFFSET_BASIS
    entries.each do |entry|
      entry.to_s.each_byte do |byte|
        value ^= byte
        value = (value * ItemSlotGenerator::FNV_PRIME) &
          ItemSlotGenerator::FNV_MASK
      end
      value ^= 0
      value = (value * ItemSlotGenerator::FNV_PRIME) &
        ItemSlotGenerator::FNV_MASK
    end
    return sprintf("%016x", value)
  end

  def self.item_ground_pool_fingerprint(rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    return item_fingerprint([rules_version, "ground"] + item_ground_pool(rules_version))
  end

  def self.item_tm_pool_fingerprint(rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    return item_fingerprint([rules_version, "tm_gift"] + item_tm_pool(rules_version))
  end

  def self.item_result_ban_fingerprint(rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    return item_fingerprint([rules_version, "result_bans"] + item_result_bans(rules_version))
  end

  def self.prepare_item_randomization
    raise ItemRandomizationError, "run metadata is unavailable" if !$PokemonGlobal
    rules = ItemSlotGenerator::POOL_RULES_VERSION
    ground_pool = item_ground_pool(rules)
    tm_pool = item_tm_pool(rules)
    $PokemonGlobal.ironmon_item_generator_version =
      ItemSlotGenerator::SCHEMA_VERSION
    $PokemonGlobal.ironmon_item_pool_rules_version = rules
    $PokemonGlobal.ironmon_item_ground_pool_size = ground_pool.length
    $PokemonGlobal.ironmon_item_ground_pool_fingerprint =
      item_ground_pool_fingerprint(rules)
    $PokemonGlobal.ironmon_item_tm_pool_size = tm_pool.length
    $PokemonGlobal.ironmon_item_tm_pool_fingerprint =
      item_tm_pool_fingerprint(rules)
    $PokemonGlobal.ironmon_item_result_ban_fingerprint =
      item_result_ban_fingerprint(rules)
    $PokemonGlobal.ironmon_item_shop_policy_version =
      ItemSlotGenerator::SHOP_POLICY_VERSION
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
    return false if $PokemonGlobal.ironmon_item_generator_version !=
      ItemSlotGenerator::SCHEMA_VERSION
    return false if
      !ItemSlotGenerator::RESULT_BANS_BY_RULES_VERSION.key?(rules)
    return false if $PokemonGlobal.ironmon_item_ground_pool_size !=
      item_ground_pool(rules).length
    return false if $PokemonGlobal.ironmon_item_ground_pool_fingerprint !=
      item_ground_pool_fingerprint(rules)
    return false if $PokemonGlobal.ironmon_item_tm_pool_size !=
      item_tm_pool(rules).length
    return false if $PokemonGlobal.ironmon_item_tm_pool_fingerprint !=
      item_tm_pool_fingerprint(rules)
    return false if $PokemonGlobal.ironmon_item_result_ban_fingerprint !=
      item_result_ban_fingerprint(rules)
    return false if $PokemonGlobal.ironmon_item_shop_policy_version !=
      ItemSlotGenerator::SHOP_POLICY_VERSION
    return true
  rescue Exception
    return false
  end

  def self.legacy_item_randomization?
    return false if !$PokemonGlobal
    return false if $PokemonGlobal.ironmon_item_generator_version
    item_map = $PokemonGlobal.randomItemsHash
    tm_map = $PokemonGlobal.randomTMsHash
    return (item_map.is_a?(Hash) && !item_map.empty?) ||
      (tm_map.is_a?(Hash) && !tm_map.empty?)
  end

  def self.ensure_item_randomization
    @item_randomization_ready = false
    return false if !$PokemonGlobal
    if legacy_item_randomization?
      reset_item_generator_cache
      return true
    end
    if !$PokemonGlobal.ironmon_item_generator_version
      disable_base_item_randomization
      reset_item_generator_cache
      return true
    end
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
      @item_slot_generator = ItemSlotGenerator.new(
        seed, item_ground_pool(rules), item_tm_pool(rules)
      )
    end
    return @item_slot_generator
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

module Game
  class << self
    alias ironmon_item_original_load load
    def load(save_data)
      Ironmon.suspend_item_randomization
      result = ironmon_item_original_load(save_data)
      return result if Ironmon.checkpoint_reset_loading?
      Ironmon.ensure_item_randomization if Ironmon.active?
      return result
    end
  end
end
