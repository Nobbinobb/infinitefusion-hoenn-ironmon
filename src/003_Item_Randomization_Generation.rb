#===============================================================================
# Ironmon deterministic item-slot randomization
#===============================================================================

module Ironmon
  class ItemRandomizationError < StandardError; end

  class ItemSlotGenerator
    SCHEMA_VERSION = 1
    POOL_RULES_VERSION = 3
    WEIGHTED_POOL_RULES_VERSION = 3
    SHOP_POLICY_VERSION = 1
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
    CURRENT_RESULT_BANS = (BASE_RESULT_BANS + MAIL_RESULT_BANS +
      APRICORN_RESULT_BANS + [:EXPSHARE] + HM_TOOL_RESULT_BANS).freeze
    RESULT_BANS_BY_RULES_VERSION = {
      1 => BASE_RESULT_BANS,
      2 => CURRENT_RESULT_BANS,
      3 => CURRENT_RESULT_BANS
    }.freeze
    ITEM_CATEGORY_WEIGHTS = {
      :hp_recovery => 32,
      :status_pp_recovery => 20,
      :general_utility => 16,
      :evolution => 16,
      :poke_ball => 12,
      :tm => 8,
      :battle_consumable => 6,
      :held_combat => 1
    }.freeze
    HELD_COMBAT_SUPPLEMENTS = [
      :BURNDRIVE, :CHILLDRIVE, :DOUSEDRIVE, :SHOCKDRIVE,
      :PROTECTIVEPADS, :SAFETYGOGGLES
    ].freeze
    HP_RECOVERY_ITEMS = [
      :POTION, :BERRYJUICE, :SWEETHEART, :SUPERPOTION,
      :HYPERPOTION, :MAXPOTION, :FRESHWATER, :SODAPOP, :LEMONADE,
      :MOOMOOMILK, :ORANBERRY, :SITRUSBERRY, :FULLRESTORE, :ENERGYPOWDER,
      :ENERGYROOT
    ].freeze
    STATUS_RECOVERY_ITEMS = [
      :ANTIDOTE, :AWAKENING, :ASPEARBERRY, :BIGMALASADA, :BLUEFLUTE,
      :BURNHEAL, :CASTELIACONE, :CHERIBERRY, :CHESTOBERRY, :FULLHEAL,
      :HEALPOWDER, :ICEHEAL, :LAVACOOKIE, :LUMIOSEGALETTE, :OLDGATEAU,
      :LUMBERRY, :PARALYZEHEAL, :PARLYZHEAL, :PECHABERRY, :PERSIMBERRY,
      :RAGECANDYBAR, :RAWSTBERRY, :REDFLUTE, :SHALOURSABLE, :YELLOWFLUTE
    ].freeze
    PP_RECOVERY_ITEMS = [
      :ELIXIR, :ETHER, :LEPPABERRY, :MAXELIXIR, :MAXETHER
    ].freeze
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

    def initialize(seed, ground_pool, tm_pool, ground_weights = nil)
      @seed = seed.to_i
      @ground_pool = ground_pool
      @tm_pool = tm_pool
      @ground_weights = validate_weights(ground_pool, ground_weights)
      @ground_total_weight = @ground_weights ? @ground_weights.sum : nil
    end

    def ground_item(slot_id)
      return select(@ground_pool, "ground", slot_id, @ground_weights,
                    @ground_total_weight)
    end

    def tm_gift(slot_id)
      return select(@tm_pool, "tm_gift", slot_id)
    end

    def select(pool, channel, slot_id, weights = nil, total_weight = nil)
      identity = slot_id.to_s
      if identity.empty?
        raise ItemRandomizationError, "a stable item slot identity is required"
      end
      input = [SCHEMA_VERSION, @seed, channel, identity].join("|")
      value = hash_value(input)
      return pool[value % pool.length] if !weights
      ticket = value % total_weight
      cumulative = 0
      weights.each_with_index do |weight, index|
        cumulative += weight
        return pool[index] if ticket < cumulative
      end
      raise ItemRandomizationError, "weighted item selection exceeded its pool"
    end

    def validate_weights(pool, weights)
      return nil if !weights
      if weights.length != pool.length || weights.any? { |weight| weight.to_i < 1 }
        raise ItemRandomizationError, "item weights must cover the complete pool"
      end
      return weights.map { |weight| weight.to_i }.freeze
    end

    def hash_value(value)
      return Ironmon.fnv1a_64(value)
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

  def self.item_result_category(item, rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    return :excluded if !item_ground_pool_eligible?(item, rules_version)
    return :uniform if rules_version < ItemSlotGenerator::WEIGHTED_POOL_RULES_VERSION
    return :hp_recovery if
      ItemSlotGenerator::HP_RECOVERY_ITEMS.include?(item.id)
    recovery_items = ItemSlotGenerator::STATUS_RECOVERY_ITEMS +
      ItemSlotGenerator::PP_RECOVERY_ITEMS
    return :status_pp_recovery if recovery_items.include?(item.id)
    return :held_combat if item_held_combat?(item)
    return :tm if item.is_TM?
    return :poke_ball if item.is_poke_ball?
    return :evolution if item.is_evolution_stone?
    return :battle_consumable if item.has_battle_use?
    return :general_utility
  end

  def self.item_held_combat?(item)
    if !Object.const_defined?(:HELD_ITEMS) || !defined?(BattleHandlers) ||
       !defined?(ItemHandlerHash)
      raise ItemRandomizationError, "the held combat item catalogs are unavailable"
    end
    return true if Object.const_get(:HELD_ITEMS).include?(item.id)
    return true if ItemSlotGenerator::HELD_COMBAT_SUPPLEMENTS.include?(item.id)
    BattleHandlers.constants.each do |constant_name|
      handler = BattleHandlers.const_get(constant_name)
      next if !handler.is_a?(ItemHandlerHash)
      return true if handler[item.id]
    end
    return false
  end

  def self.item_result_weight(item, rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    category = item_result_category(item, rules_version)
    return 0 if category == :excluded
    return 1 if category == :uniform
    weight = ItemSlotGenerator::ITEM_CATEGORY_WEIGHTS[category]
    if !weight
      raise ItemRandomizationError, "item category #{category} has no weight"
    end
    return weight
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

  def self.item_ground_weights(rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    return nil if rules_version < ItemSlotGenerator::WEIGHTED_POOL_RULES_VERSION
    @item_ground_weights ||= {}
    return @item_ground_weights[rules_version] if @item_ground_weights[rules_version]
    weights = item_ground_pool(rules_version).map do |item_id|
      item_result_weight(GameData::Item.get(item_id), rules_version)
    end
    @item_ground_weights[rules_version] = weights.freeze
    return @item_ground_weights[rules_version]
  end

  def self.item_category_summary(rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    summary = Hash.new do |hash, category|
      hash[category] = { :item_count => 0, :total_weight => 0 }
    end
    item_ground_pool(rules_version).each do |item_id|
      item = GameData::Item.get(item_id)
      category = item_result_category(item, rules_version)
      summary[category][:item_count] += 1
      summary[category][:total_weight] += item_result_weight(item, rules_version)
    end
    return summary
  end

  def self.item_ground_total_weight(rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    weights = item_ground_weights(rules_version)
    return weights ? weights.sum : item_ground_pool(rules_version).length
  end

  def self.item_fingerprint(entries)
    return fnv1a_64_fingerprint(entries)
  end

  def self.item_ground_pool_fingerprint(rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    pool = item_ground_pool(rules_version)
    entries = [rules_version, "ground"] + pool
    if rules_version >= ItemSlotGenerator::WEIGHTED_POOL_RULES_VERSION
      weighted_entries = pool.flat_map do |item_id|
        item = GameData::Item.get(item_id)
        [item_id, item_result_category(item, rules_version),
         item_result_weight(item, rules_version)]
      end
      entries += ["weighted"] + weighted_entries
    end
    return item_fingerprint(entries)
  end

  def self.item_tm_pool_fingerprint(rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    return item_fingerprint([rules_version, "tm_gift"] + item_tm_pool(rules_version))
  end

  def self.item_result_ban_fingerprint(rules_version = ItemSlotGenerator::POOL_RULES_VERSION)
    return item_fingerprint([rules_version, "result_bans"] + item_result_bans(rules_version))
  end

end
