#===============================================================================
# Deterministic Pokemon move-access randomization
#===============================================================================

module Ironmon
  class MoveAccessRandomizationError < StandardError; end

  ORDINARY_TUTOR_SLOTS = [
    {
      :id => "map:006:event:099:page:001:slot:000",
      :map_id => 6, :event_id => 99, :page_index => 0,
      :original_move => :SWAGGER, :location => "Slateport City"
    }.freeze,
    {
      :id => "map:020:event:041:page:001:slot:000",
      :map_id => 20, :event_id => 41, :page_index => 0,
      :original_move => :ECHOEDVOICE, :location => "Route 104 (North)"
    }.freeze,
    {
      :id => "map:073:event:055:page:001:slot:000",
      :map_id => 73, :event_id => 55, :page_index => 0,
      :original_move => :ROLLOUT, :location => "Mauville City"
    }.freeze,
    {
      :id => "map:075:event:063:page:001:slot:000",
      :map_id => 75, :event_id => 63, :page_index => 0,
      :original_move => :SKILLSWAP,
      :location => "Mauville City Interiors"
    }.freeze
  ].freeze

  class MoveAccessGenerator
    SCHEMA_VERSION = 6
    MIGRATABLE_SCHEMA_VERSIONS = [1, 2, 3, 4, 5].freeze
    LEVEL_UP_RULES_VERSION = 2
    EGG_RULES_VERSION = 1
    TM_RULES_VERSION = 1
    TR_RULES_VERSION = 1
    TUTOR_COMPATIBILITY_RULES_VERSION = 1
    TUTOR_OFFERING_RULES_VERSION = 1
    FUSION_TUTOR_COMPATIBILITY_RULES_VERSION = 1
    FUSION_TUTOR_OFFERING_RULES_VERSION = 1
    POOL_RULES_VERSION = 1
    SOURCE_RULES_VERSION = 1
    EGG_SOURCE_RULES_VERSION = 1
    MACHINE_ROSTER_RULES_VERSION = 1
    MACHINE_SOURCE_RULES_VERSION = 1
    TUTOR_CATALOG_RULES_VERSION = 1
    TUTOR_SOURCE_RULES_VERSION = 1
    FUSION_TUTOR_CATALOG_RULES_VERSION = 1
    FUSION_TUTOR_SOURCE_RULES_VERSION = 1
    FNV_OFFSET_BASIS = 14_695_981_039_346_656_037
    FNV_PRIME = 1_099_511_628_211
    FNV_MASK = 0xFFFFFFFFFFFFFFFF

    EXCLUDED_MOVES = [:NONE, :STRUGGLE, :SHADOWRUSH].freeze

    EXACT_SPECIES_MOVE_RULES = {
      :DARKVOID => [:DARKRAI],
      :HYPERSPACEFURY => [:HOOPA]
    }.freeze

    CONTEXTUAL_MOVES = EXACT_SPECIES_MOVE_RULES.keys.freeze

    attr_reader :pool
    attr_reader :pool_fingerprint
    attr_reader :source_fingerprint

    def initialize(seed, pool, pool_fingerprint, source_fingerprint)
      @seed = seed.to_i
      @pool = pool
      @pool_fingerprint = pool_fingerprint
      @source_fingerprint = source_fingerprint
      @move_cache = {}
      @fusion_cache = {}
      @egg_move_cache = {}
      @fusion_egg_move_cache = {}
      @tm_move_cache = {}
      @tr_move_cache = {}
      @fusion_tm_move_cache = {}
      @fusion_tr_move_cache = {}
      @tutor_move_cache = {}
      @fusion_tutor_move_cache = {}
      @ordinary_tutor_offerings = nil
      @specialized_tutor_catalogs = {}
      @specialized_tutor_move_cache = {}
    end

    def moves_for(species_data)
      identity = Ironmon.move_access_species_identity(species_data)
      cached = @move_cache[identity]
      return cached if cached
      original = Ironmon.original_level_up_moves_for(species_data)
      schedule = level_one_safe_schedule(original)
      eligible_pool = Ironmon.eligible_level_up_move_pool(species_data)
      damaging_pool = Ironmon.eligible_damaging_level_up_move_pool(species_data)
      if schedule.length > eligible_pool.length
        raise MoveAccessRandomizationError,
              "the allowed move pool is too small for #{identity}"
      end
      if damaging_pool.empty?
        raise MoveAccessRandomizationError,
              "no damaging level-1 move is available for #{identity}"
      end
      level_one_indices = []
      schedule.each_with_index do |entry, index|
        level_one_indices << index if entry[0].to_i <= 1
      end
      damaging_index = level_one_indices.last
      used = {}
      generated = []
      schedule.each_with_index do |entry, index|
        level = entry[0].to_i
        selection_pool = index == damaging_index ? damaging_pool : eligible_pool
        move = select_unique_move(
          identity, :level_up, index, level, used, selection_pool, "species"
        )
        used[move] = true
        generated << [level, move].freeze
      end
      @move_cache[identity] = generated.freeze
      return @move_cache[identity]
    end

    def fusion_moves_for(species_data)
      identity = Ironmon.move_access_species_identity(species_data)
      cached = @fusion_cache[identity]
      return cached if cached
      entries = []
      append_component_entries(entries, species_data.body_pokemon, :body, 0)
      append_component_entries(entries, species_data.head_pokemon, :head, 1)
      entries.sort_by! do |entry|
        level = entry[0]
        level_order = level == 0 ? -1 : level
        [level_order, entry[3], entry[4]]
      end
      used = {}
      generated = []
      entries.each do |level, move, source, source_order, source_index|
        if Ironmon.contextual_level_up_move?(move)
          move = select_unique_move(
            identity, source, source_index, level, used, @pool, "fusion"
          )
        end
        next if used[move]
        used[move] = true
        generated << [level, move].freeze
      end
      ensure_fusion_level_one_damage(identity, generated, used)
      @fusion_cache[identity] = generated.freeze
      return @fusion_cache[identity]
    end

    def egg_moves_for(species_data)
      identity = Ironmon.move_access_species_identity(species_data)
      cached = @egg_move_cache[identity]
      return cached if cached
      original = Ironmon.original_egg_moves_for(species_data)
      eligible_pool = Ironmon.eligible_level_up_move_pool(species_data)
      if original.length > eligible_pool.length
        raise MoveAccessRandomizationError,
              "the allowed Egg move pool is too small for #{identity}"
      end
      used = {}
      generated = []
      original.each_index do |index|
        move = select_unique_move(
          identity, :egg, index, 0, used, eligible_pool, "species",
          EGG_RULES_VERSION
        )
        used[move] = true
        generated << move
      end
      @egg_move_cache[identity] = generated.freeze
      return @egg_move_cache[identity]
    end

    def fusion_egg_moves_for(species_data)
      identity = Ironmon.move_access_species_identity(species_data)
      cached = @fusion_egg_move_cache[identity]
      return cached if cached
      entries = []
      append_component_egg_entries(entries, species_data.body_pokemon, :body)
      append_component_egg_entries(entries, species_data.head_pokemon, :head)
      used = {}
      generated = []
      entries.each_with_index do |entry, index|
        move = entry[0]
        source = entry[1]
        if Ironmon.contextual_level_up_move?(move)
          move = select_unique_move(
            identity, source, index, 0, used, @pool, "fusion_egg",
            EGG_RULES_VERSION
          )
        end
        next if used[move]
        used[move] = true
        generated << move
      end
      @fusion_egg_move_cache[identity] = generated.freeze
      return @fusion_egg_move_cache[identity]
    end

    def tm_moves_for(species_data)
      return machine_moves_for(species_data, :tm)
    end

    def tr_moves_for(species_data)
      return machine_moves_for(species_data, :tr)
    end

    def fusion_tm_moves_for(species_data)
      return fusion_machine_moves_for(species_data, :tm)
    end

    def fusion_tr_moves_for(species_data)
      return fusion_machine_moves_for(species_data, :tr)
    end

    def tutor_moves_for(species_data)
      identity = Ironmon.move_access_species_identity(species_data)
      cached = @tutor_move_cache[identity]
      return cached if cached
      original = Ironmon.original_ordinary_tutor_moves_for(species_data)
      eligible_pool = Ironmon.eligible_level_up_move_pool(species_data)
      if original.length > eligible_pool.length
        raise MoveAccessRandomizationError,
              "the allowed tutor move pool is too small for #{identity}"
      end
      used = {}
      generated = []
      original.each_index do |index|
        move = select_unique_move(
          identity, :tutor_compatibility, index, 0, used, eligible_pool,
          "species", TUTOR_COMPATIBILITY_RULES_VERSION
        )
        used[move] = true
        generated << move
      end
      @tutor_move_cache[identity] = generated.freeze
      return @tutor_move_cache[identity]
    end

    def fusion_tutor_moves_for(species_data)
      identity = Ironmon.move_access_species_identity(species_data)
      cached = @fusion_tutor_move_cache[identity]
      return cached if cached
      body_moves = tutor_moves_for(species_data.body_pokemon)
      head_moves = tutor_moves_for(species_data.head_pokemon)
      @fusion_tutor_move_cache[identity] =
        (body_moves + head_moves).uniq.freeze
      return @fusion_tutor_move_cache[identity]
    end

    def ordinary_tutor_offerings
      return @ordinary_tutor_offerings if @ordinary_tutor_offerings
      used = {}
      offerings = {}
      Ironmon::ORDINARY_TUTOR_SLOTS.each_with_index do |slot, index|
        move = select_unique_move(
          "ordinary_tutor_catalog", :tutor_offering, index, 0, used, @pool,
          slot[:id], TUTOR_OFFERING_RULES_VERSION
        )
        used[move] = true
        offerings[slot[:id]] = move
      end
      @ordinary_tutor_offerings = offerings.freeze
      return @ordinary_tutor_offerings
    end

    def ordinary_tutor_offering_for(slot_id)
      return ordinary_tutor_offerings[slot_id]
    end

    def specialized_tutor_catalog(channel)
      cached = @specialized_tutor_catalogs[channel]
      return cached if cached
      source = Ironmon.original_specialized_tutor_catalog(channel)
      used = {}
      generated = []
      source.each_with_index do |source_move, index|
        move = select_unique_move(
          "specialized_tutor_catalog", "#{channel}_offering", index, 0,
          used, @pool, source_move,
          FUSION_TUTOR_OFFERING_RULES_VERSION
        )
        used[move] = true
        generated << move
      end
      @specialized_tutor_catalogs[channel] = generated.freeze
      return @specialized_tutor_catalogs[channel]
    end

    def specialized_tutor_moves_for(pokemon, channel)
      species_data = pokemon.species_data
      identity = Ironmon.move_access_species_identity(species_data)
      key = [identity, channel]
      cached = @specialized_tutor_move_cache[key]
      return cached if cached
      source_count = Ironmon.original_specialized_tutor_moves_for(
        pokemon, channel
      ).length
      catalog = specialized_tutor_catalog(channel)
      if source_count > catalog.length
        raise MoveAccessRandomizationError,
              "the #{channel} Fusion Tutor catalog is too small for #{identity}"
      end
      used = {}
      generated = []
      source_count.times do |index|
        move = select_unique_move(
          identity, "fusion_tutor_#{channel}_compatibility", index, 0,
          used, catalog, "fusion",
          FUSION_TUTOR_COMPATIBILITY_RULES_VERSION
        )
        used[move] = true
        generated << move
      end
      @specialized_tutor_move_cache[key] = generated.freeze
      return @specialized_tutor_move_cache[key]
    end

    def validate_source(identity, original)
      original.each_with_index do |entry, index|
        if !entry.is_a?(Array) || entry.length < 2
          raise MoveAccessRandomizationError,
                "#{identity} level-up entry #{index} is malformed"
        end
        level = entry[0]
        if !level.is_a?(Integer) || level < 0
          raise MoveAccessRandomizationError,
                "#{identity} level-up entry #{index} has invalid level #{level}"
        end
        if !Ironmon.registered_move?(entry[1])
          raise MoveAccessRandomizationError,
                "#{identity} level-up entry #{index} has an unknown move"
        end
      end
      return true
    end

    def validate_egg_source(identity, original)
      original.each_with_index do |move, index|
        if !Ironmon.registered_move?(move)
          raise MoveAccessRandomizationError,
                "#{identity} Egg move entry #{index} is unknown"
        end
      end
      return true
    end

    private

    def machine_moves_for(species_data, channel)
      identity = Ironmon.move_access_species_identity(species_data)
      cache = channel == :tr ? @tr_move_cache : @tm_move_cache
      cached = cache[identity]
      return cached if cached
      original = Ironmon.original_machine_moves_for(species_data, channel)
      eligible_pool = Ironmon.eligible_machine_move_pool(species_data, channel)
      if original.length > eligible_pool.length
        raise MoveAccessRandomizationError,
              "the allowed #{channel.to_s.upcase} move pool is too small for #{identity}"
      end
      rules_version = channel == :tr ? TR_RULES_VERSION : TM_RULES_VERSION
      used = {}
      generated = []
      original.each_index do |index|
        move = select_unique_move(
          identity, channel, index, 0, used, eligible_pool, "species",
          rules_version
        )
        used[move] = true
        generated << move
      end
      cache[identity] = generated.freeze
      return cache[identity]
    end

    def fusion_machine_moves_for(species_data, channel)
      identity = Ironmon.move_access_species_identity(species_data)
      cache = channel == :tr ? @fusion_tr_move_cache : @fusion_tm_move_cache
      cached = cache[identity]
      return cached if cached
      body_moves = machine_moves_for(species_data.body_pokemon, channel)
      head_moves = machine_moves_for(species_data.head_pokemon, channel)
      cache[identity] = (body_moves + head_moves).uniq.freeze
      return cache[identity]
    end

    def level_one_safe_schedule(original)
      schedule = original.map { |entry| [entry[0], entry[1]] }
      missing = 4 - schedule.count { |entry| entry[0].to_i <= 1 }
      return schedule if missing <= 0
      insertion_index = schedule.index { |entry| entry[0].to_i > 1 }
      insertion_index ||= schedule.length
      missing.times do
        schedule.insert(insertion_index, [1, nil])
        insertion_index += 1
      end
      return schedule
    end

    def ensure_fusion_level_one_damage(identity, generated, used)
      level_one_indices = []
      generated.each_with_index do |entry, index|
        level_one_indices << index if entry[0].to_i <= 1
      end
      if level_one_indices.length < 4
        raise MoveAccessRandomizationError,
              "#{identity} has fewer than four level-1 fusion moves"
      end
      starting_indices = level_one_indices.last(4)
      return if starting_indices.any? do |index|
        Ironmon.damaging_level_up_move?(generated[index][1])
      end
      replacement_index = starting_indices.last
      replaced_move = generated[replacement_index][1]
      used.delete(replaced_move)
      replacement = select_unique_move(
        identity, :level_one_damage, replacement_index,
        generated[replacement_index][0], used,
        Ironmon.damaging_level_up_move_pool, "fusion"
      )
      used[replacement] = true
      generated[replacement_index] = [
        generated[replacement_index][0], replacement
      ].freeze
    end

    def append_component_entries(entries, species_data, source, source_order)
      moves_for(species_data).each_with_index do |entry, index|
        entries << [entry[0], entry[1], source, source_order, index]
      end
    end

    def append_component_egg_entries(entries, species_data, source)
      egg_moves_for(species_data).each do |move|
        entries << [move, source]
      end
    end

    def select_unique_move(identity, channel, index, level, used, pool, scope,
                           rules_version = LEVEL_UP_RULES_VERSION)
      start = deterministic_value(
        rules_version, identity, channel, index, level, scope, "start"
      ) % pool.length
      pool.length.times do |offset|
        candidate = pool[(start + offset) % pool.length]
        return candidate if !used[candidate]
      end
      raise MoveAccessRandomizationError,
            "no unique move remains for #{identity} #{channel} entry #{index}"
    end

    def deterministic_value(rules_version, *parts)
      value = FNV_OFFSET_BASIS
      input = [rules_version, @seed, "move_access", *parts].join("|")
      input.each_byte do |byte|
        value ^= byte
        value = (value * FNV_PRIME) & FNV_MASK
      end
      return value
    end
  end

  def self.normal_move_access_species?(species_data)
    return false if !species_data
    return species_data.id_number > 0 && species_data.id_number <= NB_POKEMON
  end

  def self.fusion_move_access_species?(species_data)
    return false if !species_data
    return false if species_data.id_number <= NB_POKEMON
    return false if species_data.id_number >= Settings::ZAPMOLCUNO_NB
    return species_data.respond_to?(:body_pokemon) &&
      species_data.respond_to?(:head_pokemon)
  end

  def self.move_access_species_identity(species_data)
    return species_data.id.to_s
  end

  def self.move_access_base_species(species_data)
    return species_data.species if species_data.respond_to?(:species)
    return species_data.id
  end

  def self.registered_move?(move)
    data = GameData::Move.try_get(move)
    return data && data.id == move
  end

  def self.safe_global_move?(move_data)
    return false if !move_data
    return false if move_data.id_number < 0
    return false if MoveAccessGenerator::EXCLUDED_MOVES.include?(move_data.id)
    return false if MoveAccessGenerator::CONTEXTUAL_MOVES.include?(move_data.id)
    return false if !move_data.function_code
    return false if !move_data.type || !GameData::Type.try_get(move_data.type)
    return false if move_data.total_pp.to_i <= 0
    return false if !move_data.target
    return true
  end

  def self.allowed_level_up_move_pool
    return @allowed_level_up_move_pool if @allowed_level_up_move_pool
    pool = []
    GameData::Move.each do |move|
      next if !safe_global_move?(move)
      pool << move
    end
    pool.sort_by! { |move| [move.id_number, move.id.to_s] }
    pool.map! { |move| move.id }
    if pool.empty?
      raise MoveAccessRandomizationError, "the allowed move pool is empty"
    end
    @allowed_level_up_move_pool = pool.freeze
    @level_up_move_pool_fingerprint = level_up_move_pool_fingerprint_for(pool)
    @level_up_move_contextual_fingerprint =
      level_up_move_contextual_fingerprint_for
    return @allowed_level_up_move_pool
  end

  def self.eligible_level_up_move_pool(species_data)
    base_species = move_access_base_species(species_data)
    @eligible_level_up_move_pools ||= {}
    cached = @eligible_level_up_move_pools[base_species]
    return cached if cached
    contextual = []
    MoveAccessGenerator::EXACT_SPECIES_MOVE_RULES.each do |move, species|
      next if !species.include?(base_species)
      next if !registered_move?(move)
      contextual << move
    end
    if contextual.empty?
      @eligible_level_up_move_pools[base_species] = allowed_level_up_move_pool
      return allowed_level_up_move_pool
    end
    pool = allowed_level_up_move_pool + contextual
    pool.sort_by! do |move|
      data = GameData::Move.get(move)
      [data.id_number, data.id.to_s]
    end
    @eligible_level_up_move_pools[base_species] = pool.freeze
    return @eligible_level_up_move_pools[base_species]
  end

  def self.damaging_level_up_move?(move)
    data = GameData::Move.try_get(move)
    return data && data.base_damage.to_i > 0
  end

  def self.damaging_level_up_move_pool
    return @damaging_level_up_move_pool if @damaging_level_up_move_pool
    pool = allowed_level_up_move_pool.select do |move|
      damaging_level_up_move?(move)
    end
    if pool.empty?
      raise MoveAccessRandomizationError,
            "the damaging level-up move pool is empty"
    end
    @damaging_level_up_move_pool = pool.freeze
    return @damaging_level_up_move_pool
  end

  def self.eligible_damaging_level_up_move_pool(species_data)
    base_species = move_access_base_species(species_data)
    @eligible_damaging_level_up_move_pools ||= {}
    cached = @eligible_damaging_level_up_move_pools[base_species]
    return cached if cached
    eligible_pool = eligible_level_up_move_pool(species_data)
    if eligible_pool.equal?(allowed_level_up_move_pool)
      @eligible_damaging_level_up_move_pools[base_species] =
        damaging_level_up_move_pool
      return damaging_level_up_move_pool
    end
    pool = eligible_pool.select do |move|
      damaging_level_up_move?(move)
    end
    @eligible_damaging_level_up_move_pools[base_species] = pool.freeze
    return @eligible_damaging_level_up_move_pools[base_species]
  end

  def self.contextual_level_up_move?(move)
    return MoveAccessGenerator::CONTEXTUAL_MOVES.include?(move)
  end

  def self.machine_item_roster(channel)
    @machine_item_rosters ||= {}
    cached = @machine_item_rosters[channel]
    return cached if cached
    items = []
    GameData::Item.each do |item|
      matches = channel == :tr ? item.is_TR? : item.is_TM?
      next if !matches || !item.move || !registered_move?(item.move)
      items << item
    end
    items.sort_by! { |item| [item.id_number, item.id.to_s] }
    @machine_item_rosters[channel] = items.freeze
    return @machine_item_rosters[channel]
  end

  def self.raw_machine_move_roster(channel)
    @raw_machine_move_rosters ||= {}
    cached = @raw_machine_move_rosters[channel]
    return cached if cached
    moves = machine_item_roster(channel).map { |item| item.move }.uniq
    moves.sort_by! do |move|
      data = GameData::Move.get(move)
      [data.id_number, data.id.to_s]
    end
    @raw_machine_move_rosters[channel] = moves.freeze
    return @raw_machine_move_rosters[channel]
  end

  def self.raw_machine_move_lookup(channel)
    @raw_machine_move_lookups ||= {}
    cached = @raw_machine_move_lookups[channel]
    return cached if cached
    lookup = {}
    raw_machine_move_roster(channel).each { |move| lookup[move] = true }
    @raw_machine_move_lookups[channel] = lookup.freeze
    return @raw_machine_move_lookups[channel]
  end

  def self.machine_move_pool(channel)
    @machine_move_pools ||= {}
    cached = @machine_move_pools[channel]
    return cached if cached
    moves = raw_machine_move_roster(channel).select do |move|
      safe_global_move?(GameData::Move.get(move))
    end
    @machine_move_pools[channel] = moves.freeze
    return @machine_move_pools[channel]
  end

  def self.eligible_machine_move_pool(species_data, channel)
    @eligible_machine_move_pools ||= {}
    base_species = move_access_base_species(species_data)
    key = [channel, base_species]
    cached = @eligible_machine_move_pools[key]
    return cached if cached
    contextual = []
    MoveAccessGenerator::EXACT_SPECIES_MOVE_RULES.each do |move, species|
      next if !species.include?(base_species)
      next if !raw_machine_move_lookup(channel)[move]
      contextual << move
    end
    if contextual.empty?
      @eligible_machine_move_pools[key] = machine_move_pool(channel)
      return machine_move_pool(channel)
    end
    pool = machine_move_pool(channel) + contextual
    pool.sort_by! do |move|
      data = GameData::Move.get(move)
      [data.id_number, data.id.to_s]
    end
    @eligible_machine_move_pools[key] = pool.freeze
    return @eligible_machine_move_pools[key]
  end

  def self.machine_roster_fingerprint(channel)
    @machine_roster_fingerprints ||= {}
    cached = @machine_roster_fingerprints[channel]
    return cached if cached
    entries = [
      MoveAccessGenerator::MACHINE_ROSTER_RULES_VERSION,
      "machine_roster", channel
    ]
    machine_item_roster(channel).each do |item|
      entries << item.id
      entries << item.id_number
      entries << item.field_use
      entries << item.move
      entries.concat(move_fingerprint_entries(GameData::Move.get(item.move)))
    end
    @machine_roster_fingerprints[channel] =
      move_access_fingerprint(entries)
    return @machine_roster_fingerprints[channel]
  end

  def self.level_up_move_pool_fingerprint
    allowed_level_up_move_pool
    return @level_up_move_pool_fingerprint
  end

  def self.level_up_move_contextual_fingerprint
    allowed_level_up_move_pool
    return @level_up_move_contextual_fingerprint
  end

  def self.level_up_move_pool_fingerprint_for(pool)
    entries = [MoveAccessGenerator::POOL_RULES_VERSION]
    pool.each do |move|
      data = GameData::Move.get(move)
      entries.concat(move_fingerprint_entries(data))
    end
    MoveAccessGenerator::EXCLUDED_MOVES.each { |move| entries << move }
    return move_access_fingerprint(entries)
  end

  def self.level_up_move_contextual_fingerprint_for
    entries = [MoveAccessGenerator::POOL_RULES_VERSION]
    MoveAccessGenerator::EXACT_SPECIES_MOVE_RULES.keys.sort_by do |move|
      move.to_s
    end.each do |move|
      entries << move
      data = GameData::Move.try_get(move)
      entries.concat(move_fingerprint_entries(data)) if data
      MoveAccessGenerator::EXACT_SPECIES_MOVE_RULES[move].each do |species|
        entries << species
      end
    end
    return move_access_fingerprint(entries)
  end

  def self.move_fingerprint_entries(move)
    flags = if move.flags.is_a?(Array)
              move.flags.map { |flag| flag.to_s }.sort
            else
              move.flags.to_s.each_char.sort
            end
    return [
      move.id, move.id_number, move.function_code, move.base_damage,
      move.type, move.category, move.accuracy, move.total_pp,
      move.effect_chance, move.target, move.priority, *flags
    ]
  end

  def self.move_access_source_entries
    return @move_access_source_entries if @move_access_source_entries
    entries = []
    GameData::Species.each do |species|
      next if !normal_move_access_species?(species)
      identity = move_access_species_identity(species)
      moves = species.ironmon_unrandomized_moves
      copied_moves = moves.map { |entry| [entry[0], entry[1]].freeze }
      entries << [identity, copied_moves.freeze].freeze
    end
    entries.sort_by! { |identity, _moves| identity }
    @move_access_source_entries = entries.freeze
    return @move_access_source_entries
  end

  def self.move_access_source_fingerprint
    return @move_access_source_fingerprint if @move_access_source_fingerprint
    entries = [MoveAccessGenerator::SOURCE_RULES_VERSION]
    move_access_source_entries.each do |identity, moves|
      entries << identity
      moves.each do |entry|
        entries << entry[0]
        entries << entry[1]
      end
    end
    @move_access_source_fingerprint = move_access_fingerprint(entries)
    return @move_access_source_fingerprint
  end

  def self.egg_move_access_source_entries
    return @egg_move_access_source_entries if @egg_move_access_source_entries
    entries = []
    GameData::Species.each do |species|
      next if !normal_move_access_species?(species)
      identity = move_access_species_identity(species)
      moves = species.ironmon_unrandomized_egg_moves.dup.freeze
      entries << [identity, moves].freeze
    end
    entries.sort_by! { |identity, _moves| identity }
    @egg_move_access_source_entries = entries.freeze
    return @egg_move_access_source_entries
  end

  def self.egg_move_access_source_fingerprint
    return @egg_move_access_source_fingerprint if
      @egg_move_access_source_fingerprint
    entries = [MoveAccessGenerator::EGG_SOURCE_RULES_VERSION]
    egg_move_access_source_entries.each do |identity, moves|
      entries << identity
      moves.each { |move| entries << move }
    end
    @egg_move_access_source_fingerprint = move_access_fingerprint(entries)
    return @egg_move_access_source_fingerprint
  end

  def self.machine_move_access_source_entries(channel)
    @machine_move_access_source_entries ||= {}
    cached = @machine_move_access_source_entries[channel]
    return cached if cached
    if raw_machine_move_roster(channel).empty?
      @machine_move_access_source_entries[channel] = [].freeze
      return @machine_move_access_source_entries[channel]
    end
    roster = raw_machine_move_lookup(channel)
    entries = []
    GameData::Species.each do |species|
      next if !normal_move_access_species?(species)
      identity = move_access_species_identity(species)
      moves = species.ironmon_unrandomized_tutor_moves.select do |move|
        roster[move]
      end.uniq.freeze
      entries << [identity, moves].freeze
    end
    entries.sort_by! { |identity, _moves| identity }
    @machine_move_access_source_entries[channel] = entries.freeze
    return @machine_move_access_source_entries[channel]
  end

  def self.machine_move_access_source_fingerprint(channel)
    @machine_move_access_source_fingerprints ||= {}
    cached = @machine_move_access_source_fingerprints[channel]
    return cached if cached
    entries = [
      MoveAccessGenerator::MACHINE_SOURCE_RULES_VERSION,
      "machine_source", channel
    ]
    machine_move_access_source_entries(channel).each do |identity, moves|
      entries << identity
      moves.each { |move| entries << move }
    end
    @machine_move_access_source_fingerprints[channel] =
      move_access_fingerprint(entries)
    return @machine_move_access_source_fingerprints[channel]
  end

  def self.all_machine_move_lookup
    return @all_machine_move_lookup if @all_machine_move_lookup
    lookup = {}
    GameData::Item.each do |item|
      next if !item.is_machine? || !item.move
      lookup[item.move] = true
    end
    @all_machine_move_lookup = lookup.freeze
    return @all_machine_move_lookup
  end

  def self.ordinary_tutor_source_entries
    return @ordinary_tutor_source_entries if @ordinary_tutor_source_entries
    machine_moves = all_machine_move_lookup
    entries = []
    GameData::Species.each do |species|
      next if !normal_move_access_species?(species)
      identity = move_access_species_identity(species)
      moves = species.ironmon_unrandomized_tutor_moves.reject do |move|
        machine_moves[move]
      end.uniq.freeze
      entries << [identity, moves].freeze
    end
    entries.sort_by! { |identity, _moves| identity }
    @ordinary_tutor_source_entries = entries.freeze
    return @ordinary_tutor_source_entries
  end

  def self.ordinary_tutor_source_fingerprint
    return @ordinary_tutor_source_fingerprint if
      @ordinary_tutor_source_fingerprint
    entries = [
      MoveAccessGenerator::TUTOR_SOURCE_RULES_VERSION,
      "ordinary_tutor_source"
    ]
    ordinary_tutor_source_entries.each do |identity, moves|
      entries << identity
      moves.each { |move| entries << move }
    end
    @ordinary_tutor_source_fingerprint = move_access_fingerprint(entries)
    return @ordinary_tutor_source_fingerprint
  end

  def self.ordinary_tutor_catalog_fingerprint
    return @ordinary_tutor_catalog_fingerprint if
      @ordinary_tutor_catalog_fingerprint
    entries = [
      MoveAccessGenerator::TUTOR_CATALOG_RULES_VERSION,
      "ordinary_tutor_catalog"
    ]
    ORDINARY_TUTOR_SLOTS.each do |slot|
      entries << slot[:id]
      entries << slot[:map_id]
      entries << slot[:event_id]
      entries << slot[:page_index]
      entries << slot[:original_move]
      entries << slot[:location]
      entries.concat(
        move_fingerprint_entries(GameData::Move.get(slot[:original_move]))
      )
    end
    @ordinary_tutor_catalog_fingerprint = move_access_fingerprint(entries)
    return @ordinary_tutor_catalog_fingerprint
  end

  def self.specialized_tutor_channel(include_legendaries)
    return include_legendaries ? :legendary : :regular
  end

  def self.original_specialized_tutor_catalog(channel)
    @original_specialized_tutor_catalogs ||= {}
    cached = @original_specialized_tutor_catalogs[channel]
    return cached if cached
    service = FusionTutorService.new(nil)
    service.setShowList(true)
    moves = service.ironmon_move_access_original_get_compatible_moves(
      channel == :legendary
    ).uniq
    @original_specialized_tutor_catalogs[channel] = moves.freeze
    return @original_specialized_tutor_catalogs[channel]
  end

  def self.original_specialized_tutor_moves_for(pokemon, channel)
    return [] if !pokemon ||
      !fusion_move_access_species?(pokemon.species_data)
    @original_specialized_tutor_move_cache ||= {}
    identity = move_access_species_identity(pokemon.species_data)
    key = [identity, channel]
    cached = @original_specialized_tutor_move_cache[key]
    return cached if cached
    service = FusionTutorService.new(pokemon)
    moves = service.ironmon_move_access_original_get_compatible_moves(
      channel == :legendary
    )
    @original_specialized_tutor_move_cache[key] = moves.uniq.freeze
    return @original_specialized_tutor_move_cache[key]
  end

  def self.specialized_tutor_catalog_fingerprint
    return @specialized_tutor_catalog_fingerprint if
      @specialized_tutor_catalog_fingerprint
    entries = [
      MoveAccessGenerator::FUSION_TUTOR_CATALOG_RULES_VERSION,
      "specialized_tutor_catalog"
    ]
    [:regular, :legendary].each do |channel|
      entries << channel
      original_specialized_tutor_catalog(channel).each_with_index do |move, index|
        entries << index
        entries << move
        entries.concat(move_fingerprint_entries(GameData::Move.get(move)))
      end
    end
    @specialized_tutor_catalog_fingerprint = move_access_fingerprint(entries)
    return @specialized_tutor_catalog_fingerprint
  end

  def self.specialized_tutor_source_fingerprint
    return @specialized_tutor_source_fingerprint if
      @specialized_tutor_source_fingerprint
    entries = [
      MoveAccessGenerator::FUSION_TUTOR_SOURCE_RULES_VERSION,
      "specialized_tutor_source",
      specialized_tutor_catalog_fingerprint
    ]
    @specialized_tutor_source_fingerprint = move_access_fingerprint(entries)
    return @specialized_tutor_source_fingerprint
  end

  def self.move_access_fingerprint(entries)
    value = MoveAccessGenerator::FNV_OFFSET_BASIS
    entries.each do |entry|
      entry.to_s.each_byte do |byte|
        value ^= byte
        value = (value * MoveAccessGenerator::FNV_PRIME) &
          MoveAccessGenerator::FNV_MASK
      end
      value ^= 0
      value = (value * MoveAccessGenerator::FNV_PRIME) &
        MoveAccessGenerator::FNV_MASK
    end
    return sprintf("%016x", value)
  end

  def self.original_level_up_moves_for(species_data)
    return species_data.ironmon_unrandomized_moves.dup
  end

  def self.original_egg_moves_for(species_data)
    return species_data.ironmon_unrandomized_egg_moves.dup
  end

  def self.original_machine_moves_for(species_data, channel)
    roster = raw_machine_move_lookup(channel)
    return species_data.ironmon_unrandomized_tutor_moves.select do |move|
      roster[move]
    end.uniq
  end

  def self.original_ordinary_tutor_moves_for(species_data)
    machine_moves = all_machine_move_lookup
    return species_data.ironmon_unrandomized_tutor_moves.reject do |move|
      machine_moves[move]
    end.uniq
  end

  def self.generated_level_up_moves_for(species_data, generator = nil)
    generator ||= move_access_generator
    return generator.fusion_moves_for(species_data) if
      fusion_move_access_species?(species_data)
    return generator.moves_for(species_data) if
      normal_move_access_species?(species_data)
    return original_level_up_moves_for(species_data)
  end

  def self.generated_egg_moves_for(species_data, generator = nil)
    generator ||= move_access_generator
    return generator.fusion_egg_moves_for(species_data) if
      fusion_move_access_species?(species_data)
    return generator.egg_moves_for(species_data) if
      normal_move_access_species?(species_data)
    return original_egg_moves_for(species_data)
  end

  def self.generated_machine_moves_for(species_data, channel, generator = nil)
    generator ||= move_access_generator
    if fusion_move_access_species?(species_data)
      return generator.fusion_tr_moves_for(species_data) if channel == :tr
      return generator.fusion_tm_moves_for(species_data)
    end
    if normal_move_access_species?(species_data)
      return generator.tr_moves_for(species_data) if channel == :tr
      return generator.tm_moves_for(species_data)
    end
    return original_machine_moves_for(species_data, channel)
  end

  def self.generated_ordinary_tutor_moves_for(species_data, generator = nil)
    generator ||= move_access_generator
    return generator.fusion_tutor_moves_for(species_data) if
      fusion_move_access_species?(species_data)
    return generator.tutor_moves_for(species_data) if
      normal_move_access_species?(species_data)
    return original_ordinary_tutor_moves_for(species_data)
  end

  def self.supported_ordinary_tutor_moves_for(species_data, generator = nil)
    generator ||= move_access_generator
    compatible = generated_ordinary_tutor_moves_for(species_data, generator)
    offerings = generator.ordinary_tutor_offerings.values
    return offerings.select { |move| compatible.include?(move) }.freeze
  end

  def self.generated_specialized_tutor_catalog(channel, generator = nil)
    generator ||= move_access_generator
    return generator.specialized_tutor_catalog(channel)
  end

  def self.generated_specialized_tutor_moves_for(pokemon, channel,
                                                   generator = nil)
    return [] if !pokemon ||
      !fusion_move_access_species?(pokemon.species_data)
    generator ||= move_access_generator
    return generator.specialized_tutor_moves_for(pokemon, channel)
  end

  def self.validate_move_access_sources
    validation_key = [
      MoveAccessGenerator::SCHEMA_VERSION,
      level_up_move_pool_fingerprint,
      level_up_move_contextual_fingerprint,
      move_access_source_fingerprint,
      egg_move_access_source_fingerprint,
      machine_roster_fingerprint(:tm),
      machine_roster_fingerprint(:tr),
      machine_move_access_source_fingerprint(:tm),
      machine_move_access_source_fingerprint(:tr),
      ordinary_tutor_catalog_fingerprint,
      ordinary_tutor_source_fingerprint,
      specialized_tutor_catalog_fingerprint,
      specialized_tutor_source_fingerprint
    ]
    return true if @validated_move_access_source_key == validation_key
    validator = MoveAccessGenerator.new(
      0, allowed_level_up_move_pool, level_up_move_pool_fingerprint,
      move_access_source_fingerprint
    )
    if ORDINARY_TUTOR_SLOTS.map { |slot| slot[:id] }.uniq.length !=
       ORDINARY_TUTOR_SLOTS.length
      raise MoveAccessRandomizationError,
            "the ordinary tutor catalog contains duplicate slot IDs"
    end
    if ORDINARY_TUTOR_SLOTS.length > allowed_level_up_move_pool.length
      raise MoveAccessRandomizationError,
            "the ordinary tutor offering pool is too small"
    end
    ORDINARY_TUTOR_SLOTS.each do |slot|
      if !registered_move?(slot[:original_move])
        raise MoveAccessRandomizationError,
              "ordinary tutor slot #{slot[:id]} has an unknown source move"
      end
    end
    [:regular, :legendary].each do |channel|
      catalog = original_specialized_tutor_catalog(channel)
      if catalog.empty?
        raise MoveAccessRandomizationError,
              "the #{channel} Fusion Tutor catalog is empty"
      end
      if catalog.uniq.length != catalog.length
        raise MoveAccessRandomizationError,
              "the #{channel} Fusion Tutor catalog contains duplicates"
      end
      if catalog.length > allowed_level_up_move_pool.length
        raise MoveAccessRandomizationError,
              "the allowed Fusion Tutor move pool is too small"
      end
      catalog.each do |move|
        if !registered_move?(move)
          raise MoveAccessRandomizationError,
                "the #{channel} Fusion Tutor catalog has an unknown move"
        end
      end
    end
    move_access_source_entries.each do |identity, moves|
      validator.validate_source(identity, moves)
      species = GameData::Species.get(identity.to_sym)
      missing_level_one_entries = 4 - moves.count do |entry|
        entry[0].to_i <= 1
      end
      missing_level_one_entries = 0 if missing_level_one_entries < 0
      required_entries = moves.length + missing_level_one_entries
      if required_entries > eligible_level_up_move_pool(species).length
        raise MoveAccessRandomizationError,
              "the allowed move pool is too small for #{identity}"
      end
      if eligible_damaging_level_up_move_pool(species).empty?
        raise MoveAccessRandomizationError,
              "no damaging level-1 move is available for #{identity}"
      end
    end
    egg_move_access_source_entries.each do |identity, moves|
      species = GameData::Species.get(identity.to_sym)
      if moves.length > eligible_level_up_move_pool(species).length
        raise MoveAccessRandomizationError,
              "the allowed Egg move pool is too small for #{identity}"
      end
    end
    [:tm, :tr].each do |channel|
      machine_move_access_source_entries(channel).each do |identity, moves|
        species = GameData::Species.get(identity.to_sym)
        if moves.length > eligible_machine_move_pool(species, channel).length
          raise MoveAccessRandomizationError,
                "the allowed #{channel.to_s.upcase} move pool is too small for #{identity}"
        end
      end
    end
    ordinary_tutor_source_entries.each do |identity, moves|
      species = GameData::Species.get(identity.to_sym)
      if moves.length > eligible_level_up_move_pool(species).length
        raise MoveAccessRandomizationError,
              "the allowed tutor move pool is too small for #{identity}"
      end
    end
    @validated_move_access_source_key = validation_key.freeze
    return true
  end

  def self.prepare_move_access_randomization
    @move_access_randomization_ready = false
    raise MoveAccessRandomizationError, "run metadata is unavailable" if
      !$PokemonGlobal
    pool = allowed_level_up_move_pool
    validate_move_access_sources
    $PokemonGlobal.ironmon_move_access_generator_version =
      MoveAccessGenerator::SCHEMA_VERSION
    $PokemonGlobal.ironmon_move_pool_size = pool.length
    $PokemonGlobal.ironmon_move_pool_fingerprint =
      level_up_move_pool_fingerprint
    $PokemonGlobal.ironmon_move_contextual_restriction_fingerprint =
      level_up_move_contextual_fingerprint
    $PokemonGlobal.ironmon_move_source_fingerprint =
      move_access_source_fingerprint
    $PokemonGlobal.ironmon_egg_move_source_fingerprint =
      egg_move_access_source_fingerprint
    $PokemonGlobal.ironmon_tm_roster_size = machine_move_pool(:tm).length
    $PokemonGlobal.ironmon_tm_roster_fingerprint =
      machine_roster_fingerprint(:tm)
    $PokemonGlobal.ironmon_tm_source_fingerprint =
      machine_move_access_source_fingerprint(:tm)
    $PokemonGlobal.ironmon_tr_roster_size = machine_move_pool(:tr).length
    $PokemonGlobal.ironmon_tr_roster_fingerprint =
      machine_roster_fingerprint(:tr)
    $PokemonGlobal.ironmon_tr_source_fingerprint =
      machine_move_access_source_fingerprint(:tr)
    $PokemonGlobal.ironmon_tutor_catalog_size = ORDINARY_TUTOR_SLOTS.length
    $PokemonGlobal.ironmon_tutor_catalog_fingerprint =
      ordinary_tutor_catalog_fingerprint
    $PokemonGlobal.ironmon_tutor_source_fingerprint =
      ordinary_tutor_source_fingerprint
    $PokemonGlobal.ironmon_fusion_tutor_regular_catalog_size =
      original_specialized_tutor_catalog(:regular).length
    $PokemonGlobal.ironmon_fusion_tutor_legendary_catalog_size =
      original_specialized_tutor_catalog(:legendary).length
    $PokemonGlobal.ironmon_fusion_tutor_catalog_fingerprint =
      specialized_tutor_catalog_fingerprint
    $PokemonGlobal.ironmon_fusion_tutor_source_fingerprint =
      specialized_tutor_source_fingerprint
    reset_move_access_generator_cache
    move_access_generator
    @move_access_randomization_ready = true
    @move_access_randomization_error_message = nil
    patch_loaded_ordinary_tutor_events
    return true
  rescue MoveAccessRandomizationError => e
    @move_access_randomization_error_message = _INTL(
      "Ironmon could not prepare move-access randomization: {1}", e.message
    )
    echoln @move_access_randomization_error_message
    return false
  rescue Exception => e
    @move_access_randomization_error_message = _INTL(
      "Ironmon could not prepare move-access randomization because of an unexpected error: {1}",
      e.message
    )
    echoln @move_access_randomization_error_message
    return false
  end

  def self.current_move_access_randomization?
    return false if !$PokemonGlobal
    return false if $PokemonGlobal.ironmon_move_access_generator_version !=
      MoveAccessGenerator::SCHEMA_VERSION
    return false if $PokemonGlobal.ironmon_move_pool_size !=
      allowed_level_up_move_pool.length
    return false if $PokemonGlobal.ironmon_move_pool_fingerprint !=
      level_up_move_pool_fingerprint
    return false if
      $PokemonGlobal.ironmon_move_contextual_restriction_fingerprint !=
      level_up_move_contextual_fingerprint
    return false if $PokemonGlobal.ironmon_move_source_fingerprint !=
      move_access_source_fingerprint
    return false if $PokemonGlobal.ironmon_egg_move_source_fingerprint !=
      egg_move_access_source_fingerprint
    return false if $PokemonGlobal.ironmon_tm_roster_size !=
      machine_move_pool(:tm).length
    return false if $PokemonGlobal.ironmon_tm_roster_fingerprint !=
      machine_roster_fingerprint(:tm)
    return false if $PokemonGlobal.ironmon_tm_source_fingerprint !=
      machine_move_access_source_fingerprint(:tm)
    return false if $PokemonGlobal.ironmon_tr_roster_size !=
      machine_move_pool(:tr).length
    return false if $PokemonGlobal.ironmon_tr_roster_fingerprint !=
      machine_roster_fingerprint(:tr)
    return false if $PokemonGlobal.ironmon_tr_source_fingerprint !=
      machine_move_access_source_fingerprint(:tr)
    return false if $PokemonGlobal.ironmon_tutor_catalog_size !=
      ORDINARY_TUTOR_SLOTS.length
    return false if $PokemonGlobal.ironmon_tutor_catalog_fingerprint !=
      ordinary_tutor_catalog_fingerprint
    return false if $PokemonGlobal.ironmon_tutor_source_fingerprint !=
      ordinary_tutor_source_fingerprint
    return false if
      $PokemonGlobal.ironmon_fusion_tutor_regular_catalog_size !=
      original_specialized_tutor_catalog(:regular).length
    return false if
      $PokemonGlobal.ironmon_fusion_tutor_legendary_catalog_size !=
      original_specialized_tutor_catalog(:legendary).length
    return false if
      $PokemonGlobal.ironmon_fusion_tutor_catalog_fingerprint !=
      specialized_tutor_catalog_fingerprint
    return false if
      $PokemonGlobal.ironmon_fusion_tutor_source_fingerprint !=
      specialized_tutor_source_fingerprint
    return true
  rescue Exception
    return false
  end

  def self.migrate_move_access_randomization_metadata
    version = $PokemonGlobal.ironmon_move_access_generator_version
    return false if
      !MoveAccessGenerator::MIGRATABLE_SCHEMA_VERSIONS.include?(version)
    return false if $PokemonGlobal.ironmon_move_pool_size !=
      allowed_level_up_move_pool.length
    return false if $PokemonGlobal.ironmon_move_pool_fingerprint !=
      level_up_move_pool_fingerprint
    return false if
      $PokemonGlobal.ironmon_move_contextual_restriction_fingerprint !=
      level_up_move_contextual_fingerprint
    return false if $PokemonGlobal.ironmon_move_source_fingerprint !=
      move_access_source_fingerprint
    saved_egg_fingerprint =
      $PokemonGlobal.ironmon_egg_move_source_fingerprint
    return false if saved_egg_fingerprint &&
      saved_egg_fingerprint != egg_move_access_source_fingerprint
    saved_machine_metadata = [
      [$PokemonGlobal.ironmon_tm_roster_size, machine_move_pool(:tm).length],
      [$PokemonGlobal.ironmon_tm_roster_fingerprint,
       machine_roster_fingerprint(:tm)],
      [$PokemonGlobal.ironmon_tm_source_fingerprint,
       machine_move_access_source_fingerprint(:tm)],
      [$PokemonGlobal.ironmon_tr_roster_size, machine_move_pool(:tr).length],
      [$PokemonGlobal.ironmon_tr_roster_fingerprint,
       machine_roster_fingerprint(:tr)],
      [$PokemonGlobal.ironmon_tr_source_fingerprint,
       machine_move_access_source_fingerprint(:tr)],
      [$PokemonGlobal.ironmon_tutor_catalog_size,
       ORDINARY_TUTOR_SLOTS.length],
      [$PokemonGlobal.ironmon_tutor_catalog_fingerprint,
       ordinary_tutor_catalog_fingerprint],
      [$PokemonGlobal.ironmon_tutor_source_fingerprint,
       ordinary_tutor_source_fingerprint],
      [$PokemonGlobal.ironmon_fusion_tutor_regular_catalog_size,
       original_specialized_tutor_catalog(:regular).length],
      [$PokemonGlobal.ironmon_fusion_tutor_legendary_catalog_size,
       original_specialized_tutor_catalog(:legendary).length],
      [$PokemonGlobal.ironmon_fusion_tutor_catalog_fingerprint,
       specialized_tutor_catalog_fingerprint],
      [$PokemonGlobal.ironmon_fusion_tutor_source_fingerprint,
       specialized_tutor_source_fingerprint]
    ]
    mismatched_machine_metadata = saved_machine_metadata.any? do |saved, current|
      saved && saved != current
    end
    return false if mismatched_machine_metadata
    validate_move_access_sources
    $PokemonGlobal.ironmon_move_access_generator_version =
      MoveAccessGenerator::SCHEMA_VERSION
    $PokemonGlobal.ironmon_egg_move_source_fingerprint =
      egg_move_access_source_fingerprint
    $PokemonGlobal.ironmon_tm_roster_size = machine_move_pool(:tm).length
    $PokemonGlobal.ironmon_tm_roster_fingerprint =
      machine_roster_fingerprint(:tm)
    $PokemonGlobal.ironmon_tm_source_fingerprint =
      machine_move_access_source_fingerprint(:tm)
    $PokemonGlobal.ironmon_tr_roster_size = machine_move_pool(:tr).length
    $PokemonGlobal.ironmon_tr_roster_fingerprint =
      machine_roster_fingerprint(:tr)
    $PokemonGlobal.ironmon_tr_source_fingerprint =
      machine_move_access_source_fingerprint(:tr)
    $PokemonGlobal.ironmon_tutor_catalog_size = ORDINARY_TUTOR_SLOTS.length
    $PokemonGlobal.ironmon_tutor_catalog_fingerprint =
      ordinary_tutor_catalog_fingerprint
    $PokemonGlobal.ironmon_tutor_source_fingerprint =
      ordinary_tutor_source_fingerprint
    $PokemonGlobal.ironmon_fusion_tutor_regular_catalog_size =
      original_specialized_tutor_catalog(:regular).length
    $PokemonGlobal.ironmon_fusion_tutor_legendary_catalog_size =
      original_specialized_tutor_catalog(:legendary).length
    $PokemonGlobal.ironmon_fusion_tutor_catalog_fingerprint =
      specialized_tutor_catalog_fingerprint
    $PokemonGlobal.ironmon_fusion_tutor_source_fingerprint =
      specialized_tutor_source_fingerprint
    echoln _INTL(
      "Ironmon migrated move-access metadata from schema {1} to schema {2}.",
      version, MoveAccessGenerator::SCHEMA_VERSION
    )
    return true
  rescue Exception => e
    echoln _INTL(
      "Ironmon could not migrate move-access metadata: {1}", e.message
    )
    return false
  end

  def self.ensure_move_access_randomization
    @move_access_randomization_ready = false
    return false if !$PokemonGlobal
    metadata = [
      $PokemonGlobal.ironmon_move_access_generator_version,
      $PokemonGlobal.ironmon_move_pool_size,
      $PokemonGlobal.ironmon_move_pool_fingerprint,
      $PokemonGlobal.ironmon_move_contextual_restriction_fingerprint,
      $PokemonGlobal.ironmon_move_source_fingerprint,
      $PokemonGlobal.ironmon_egg_move_source_fingerprint,
      $PokemonGlobal.ironmon_tm_roster_size,
      $PokemonGlobal.ironmon_tm_roster_fingerprint,
      $PokemonGlobal.ironmon_tm_source_fingerprint,
      $PokemonGlobal.ironmon_tr_roster_size,
      $PokemonGlobal.ironmon_tr_roster_fingerprint,
      $PokemonGlobal.ironmon_tr_source_fingerprint,
      $PokemonGlobal.ironmon_tutor_catalog_size,
      $PokemonGlobal.ironmon_tutor_catalog_fingerprint,
      $PokemonGlobal.ironmon_tutor_source_fingerprint,
      $PokemonGlobal.ironmon_fusion_tutor_regular_catalog_size,
      $PokemonGlobal.ironmon_fusion_tutor_legendary_catalog_size,
      $PokemonGlobal.ironmon_fusion_tutor_catalog_fingerprint,
      $PokemonGlobal.ironmon_fusion_tutor_source_fingerprint
    ]
    if metadata.compact.empty?
      reset_move_access_generator_cache
      echoln "Ironmon retained original move access for a pre-Step-3.3 run."
      return true
    end
    migrate_move_access_randomization_metadata if
      $PokemonGlobal.ironmon_move_access_generator_version !=
      MoveAccessGenerator::SCHEMA_VERSION
    if !current_move_access_randomization?
      raise MoveAccessRandomizationError,
            "the saved move-access generator or source data is incompatible"
    end
    reset_move_access_generator_cache
    move_access_generator
    @move_access_randomization_ready = true
    patch_loaded_ordinary_tutor_events
    return true
  end

  def self.move_access_randomization_active?
    return false if !$PokemonGlobal
    return false if $PokemonGlobal.ironmon_mode != true
    return @move_access_randomization_ready == true
  end

  def self.move_access_generator
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    if !@move_access_generator || @move_access_generator_seed != seed
      @move_access_generator_seed = seed
      @move_access_generator = MoveAccessGenerator.new(
        seed, allowed_level_up_move_pool, level_up_move_pool_fingerprint,
        move_access_source_fingerprint
      )
    end
    return @move_access_generator
  end

  def self.reset_move_access_generator_cache
    @move_access_generator = nil
    @move_access_generator_seed = nil
  end

  def self.suspend_move_access_randomization
    @move_access_randomization_ready = false
    reset_move_access_generator_cache
  end

  def self.move_access_randomization_error_message
    return @move_access_randomization_error_message ||
      _INTL("Ironmon could not prepare move-access randomization.")
  end

  def self.machine_channel_for_item(item)
    item_data = GameData::Item.try_get(item)
    return nil if !item_data
    return :tr if item_data.is_TR?
    return :tm if item_data.is_TM?
    return nil
  end

  def self.machine_channel_for_move(move)
    in_tm = raw_machine_move_lookup(:tm)[move]
    in_tr = raw_machine_move_lookup(:tr)[move]
    return :machine if in_tm && in_tr
    return :tr if in_tr
    return :tm if in_tm
    return nil
  end

  def self.with_machine_compatibility(channel)
    previous = @machine_compatibility_channel
    @machine_compatibility_channel = channel
    return yield
  ensure
    @machine_compatibility_channel = previous
  end

  def self.machine_compatibility_channel
    return @machine_compatibility_channel
  end

  def self.generated_machine_compatible?(pokemon, move, channel = nil)
    species_data = pokemon.species_data
    channel ||= machine_compatibility_channel
    if channel == :machine
      return true if generated_machine_moves_for(species_data, :tm).include?(move)
      return generated_machine_moves_for(species_data, :tr).include?(move)
    end
    return false if channel != :tm && channel != :tr
    return generated_machine_moves_for(species_data, channel).include?(move)
  end

  def self.generated_ordinary_tutor_compatible?(pokemon, move)
    offerings = move_access_generator.ordinary_tutor_offerings.values
    return false if !offerings.include?(move)
    compatible = generated_ordinary_tutor_moves_for(pokemon.species_data)
    return compatible.include?(move)
  end

  def self.ordinary_tutor_slot_lookup
    return @ordinary_tutor_slot_lookup if @ordinary_tutor_slot_lookup
    lookup = {}
    ORDINARY_TUTOR_SLOTS.each do |slot|
      lookup[[slot[:map_id], slot[:event_id]]] = slot
    end
    @ordinary_tutor_slot_lookup = lookup.freeze
    return @ordinary_tutor_slot_lookup
  end

  def self.current_ordinary_tutor_slot
    interpreter = pbMapInterpreter
    return nil if !interpreter
    map_id = interpreter.instance_variable_get(:@map_id)
    event_id = interpreter.instance_variable_get(:@event_id)
    return ordinary_tutor_slot_lookup[[map_id, event_id]]
  end

  def self.ordinary_tutor_offering(slot, generator = nil)
    return nil if !slot
    generator ||= move_access_generator
    return generator.ordinary_tutor_offering_for(slot[:id])
  end

  def self.patch_ordinary_tutor_event(event, slot, generator = nil)
    generator ||= move_access_generator
    page = event.pages[slot[:page_index]]
    return false if !page
    backups = event.instance_variable_get(:@ironmon_tutor_page_backups)
    if !backups
      backups = {}
      event.instance_variable_set(:@ironmon_tutor_page_backups, backups)
    end
    backup = backups[slot[:page_index]]
    if !backup
      backup = page.list.map do |command|
        Marshal.dump(command.parameters)
      end
      backups[slot[:page_index]] = backup
    else
      page.list.each_with_index do |command, index|
        command.parameters.replace(Marshal.load(backup[index]))
      end
    end
    offering = ordinary_tutor_offering(slot, generator)
    original_token = ":#{slot[:original_move]}"
    offering_token = ":#{offering}"
    original_name = GameData::Move.get(slot[:original_move]).real_name
    offering_name = GameData::Move.get(offering).real_name
    page.list.each do |command|
      command.parameters.each do |parameter|
        next if !parameter.is_a?(String)
        if command.code == 355 || command.code == 655 || command.code == 111
          parameter.gsub!(/#{Regexp.escape(original_token)}\b/, offering_token)
        elsif command.code == 101 || command.code == 401
          parameter.gsub!(/#{Regexp.escape(original_name)}/i, offering_name)
        end
      end
    end
    return true
  end

  def self.patch_ordinary_tutor_map(map_id, map, generator = nil)
    return false if !move_access_randomization_active?
    patched = false
    ORDINARY_TUTOR_SLOTS.each do |slot|
      next if slot[:map_id] != map_id
      event = map.events[slot[:event_id]]
      next if !event
      patched = true if patch_ordinary_tutor_event(event, slot, generator)
    end
    return patched
  end

  def self.patch_loaded_ordinary_tutor_events
    return false if !$game_map || !$game_map.events
    patched = false
    $game_map.events.each_value do |game_event|
      slot = ordinary_tutor_slot_lookup[[$game_map.map_id, game_event.id]]
      next if !slot
      event = game_event.instance_variable_get(:@event)
      next if !event
      if patch_ordinary_tutor_event(event, slot)
        game_event.refresh
        patched = true
      end
    end
    return patched
  end
end

class GameData::Species
  alias ironmon_unrandomized_moves moves
  alias ironmon_unrandomized_egg_moves egg_moves
  alias ironmon_unrandomized_tutor_moves tutor_moves

  def moves
    return ironmon_unrandomized_moves if
      !Ironmon.move_access_randomization_active?
    return Ironmon.generated_level_up_moves_for(self)
  end

  def egg_moves
    return ironmon_unrandomized_egg_moves if
      !Ironmon.move_access_randomization_active?
    return Ironmon.generated_egg_moves_for(self)
  end
end

class Pokemon
  alias ironmon_move_access_original_compatible_with_move? compatible_with_move?

  def compatible_with_move?(move_id)
    move_data = GameData::Move.try_get(move_id)
    channel = Ironmon.machine_compatibility_channel
    if Ironmon.move_access_randomization_active? && move_data && channel
      if channel == :tutor
        return Ironmon.generated_ordinary_tutor_compatible?(
          self, move_data.id
        )
      end
      return Ironmon.generated_machine_compatible?(self, move_data.id, channel)
    end
    return ironmon_move_access_original_compatible_with_move?(move_id)
  end
end

alias ironmon_machine_original_pb_move_tutor_choose pbMoveTutorChoose
def pbMoveTutorChoose(move, movelist = nil, bymachine = false,
                      oneusemachine = false, selectedPokemonVariable = nil)
  if Ironmon.move_access_randomization_active? && bymachine
    channel = Ironmon.machine_compatibility_channel
    channel ||= Ironmon.machine_channel_for_move(GameData::Move.get(move).id)
    return Ironmon.with_machine_compatibility(channel) do
      ironmon_machine_original_pb_move_tutor_choose(
        move, movelist, bymachine, oneusemachine, selectedPokemonVariable
      )
    end
  end
  if Ironmon.move_access_randomization_active? && !bymachine
    slot = Ironmon.current_ordinary_tutor_slot
    if slot
      offering = Ironmon.ordinary_tutor_offering(slot)
      return Ironmon.with_machine_compatibility(:tutor) do
        ironmon_machine_original_pb_move_tutor_choose(
          offering, movelist, bymachine, oneusemachine,
          selectedPokemonVariable
        )
      end
    end
  end
  return ironmon_machine_original_pb_move_tutor_choose(
    move, movelist, bymachine, oneusemachine, selectedPokemonVariable
  )
end

alias ironmon_tutor_original_pb_move_tutor_battle pbMoveTutorBattle
def pbMoveTutorBattle(trainerID, trainerName, moves, scaleLevel = true,
                      event_id = nil, map_id = nil)
  if Ironmon.move_access_randomization_active?
    slot = Ironmon.current_ordinary_tutor_slot
    if slot
      offering = Ironmon.ordinary_tutor_offering(slot)
      if moves.is_a?(Array)
        moves = moves.map do |move|
          move == slot[:original_move] ? offering : move
        end
      elsif moves == slot[:original_move]
        moves = offering
      end
    end
  end
  return ironmon_tutor_original_pb_move_tutor_battle(
    trainerID, trainerName, moves, scaleLevel, event_id, map_id
  )
end

class FusionTutorService
  alias ironmon_move_access_original_get_compatible_moves getCompatibleMoves

  def getCompatibleMoves(includeLegendaries = false)
    if !Ironmon.move_access_randomization_active?
      return ironmon_move_access_original_get_compatible_moves(
        includeLegendaries
      )
    end
    channel = Ironmon.specialized_tutor_channel(includeLegendaries)
    if @show_full_list
      return Ironmon.generated_specialized_tutor_catalog(channel)
    end
    return Ironmon.generated_specialized_tutor_moves_for(@pokemon, channel)
  end
end

alias ironmon_move_access_original_rare_tutor_example showRandomRareMoveConditionExample
def showRandomRareMoveConditionExample(legendary = false)
  if Ironmon.move_access_randomization_active?
    category = legendary ? _INTL("legendary") : _INTL("regular")
    pbMessage(_INTL(
      "Each fusion keeps its original number of compatible {1} moves, but the moves themselves are randomized for this run.",
      category
    ))
    return
  end
  ironmon_move_access_original_rare_tutor_example(legendary)
end

alias ironmon_machine_original_pb_use_item pbUseItem
def pbUseItem(bag, item, bagscene = nil)
  channel = Ironmon.machine_channel_for_item(item)
  if Ironmon.move_access_randomization_active? && channel
    return Ironmon.with_machine_compatibility(channel) do
      ironmon_machine_original_pb_use_item(bag, item, bagscene)
    end
  end
  return ironmon_machine_original_pb_use_item(bag, item, bagscene)
end

alias ironmon_machine_original_pb_use_item_on_pokemon pbUseItemOnPokemon
def pbUseItemOnPokemon(item, pokemon, scene)
  channel = Ironmon.machine_channel_for_item(item)
  if Ironmon.move_access_randomization_active? && channel
    return Ironmon.with_machine_compatibility(channel) do
      ironmon_machine_original_pb_use_item_on_pokemon(item, pokemon, scene)
    end
  end
  return ironmon_machine_original_pb_use_item_on_pokemon(item, pokemon, scene)
end

class PokemonPartyScreen
  def pbUseItem(bag, pokemon)
    ret = nil
    pbFadeOutIn do
      scene = PokemonBag_Scene.new
      screen = PokemonBagScreen.new(scene, bag)
      ret = screen.pbChooseItemScreen(Proc.new do |item|
        item_data = GameData::Item.get(item)
        next false if !pbCanUseOnPokemon?(item_data)
        if item_data.is_machine?
          move = item_data.move
          compatible = Ironmon.with_machine_compatibility(
            Ironmon.machine_channel_for_item(item_data)
          ) { pokemon.compatible_with_move?(move) }
          next false if pokemon.hasMove?(move) || !compatible
        end
        next true
      end)
      yield if block_given?
    end
    return ret
  end
end

alias ironmon_machine_original_daycare_generate_egg pbDayCareGenerateEgg
def pbDayCareGenerateEgg
  if Ironmon.move_access_randomization_active?
    return Ironmon.with_machine_compatibility(:machine) do
      ironmon_machine_original_daycare_generate_egg
    end
  end
  return ironmon_machine_original_daycare_generate_egg
end

alias ironmon_machine_original_get_mapped_random_item getMappedRandomItem
def getMappedRandomItem(item)
  if Ironmon.active? && item && item.is_TM? &&
     $PokemonGlobal && $PokemonGlobal.randomTMsHash
    mapped = $PokemonGlobal.randomTMsHash[item.id]
    return GameData::Item.get(mapped) if mapped
  end
  return ironmon_machine_original_get_mapped_random_item(item)
end

Events.onMapCreate += proc do |_sender, event|
  map_id = event[0]
  map = event[1]
  Ironmon.patch_ordinary_tutor_map(map_id, map) if
    Ironmon.move_access_randomization_active?
end

Events.onWildPokemonCreate += proc do |_sender, event|
  pokemon = event[0]
  if Ironmon.move_access_randomization_active? && pokemon &&
     !pokemon.shadowPokemon?
    pokemon.reset_moves
  end
end

Events.onTrainerPartyLoad += proc do |_sender, event|
  trainer = event[0]
  if Ironmon.move_access_randomization_active? && trainer && trainer.party
    trainer.party.each do |pokemon|
      pokemon.reset_moves if !pokemon.shadowPokemon?
    end
  end
end

module Game
  class << self
    alias ironmon_move_access_original_load load
    def load(save_data)
      Ironmon.suspend_move_access_randomization
      result = ironmon_move_access_original_load(save_data)
      return result if Ironmon.checkpoint_reset_loading?
      Ironmon.ensure_move_access_randomization if Ironmon.active?
      return result
    end
  end
end
