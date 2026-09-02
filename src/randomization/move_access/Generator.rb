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

  MOVE_ACCESS_METADATA_FIELDS = [
    :ironmon_move_access_generator_version,
    :ironmon_move_pool_size,
    :ironmon_move_pool_fingerprint,
    :ironmon_move_contextual_restriction_fingerprint,
    :ironmon_move_source_fingerprint,
    :ironmon_egg_move_source_fingerprint,
    :ironmon_tm_roster_size,
    :ironmon_tm_roster_fingerprint,
    :ironmon_tm_source_fingerprint,
    :ironmon_tr_roster_size,
    :ironmon_tr_roster_fingerprint,
    :ironmon_tr_source_fingerprint,
    :ironmon_tutor_catalog_size,
    :ironmon_tutor_catalog_fingerprint,
    :ironmon_tutor_source_fingerprint,
    :ironmon_fusion_tutor_regular_catalog_size,
    :ironmon_fusion_tutor_legendary_catalog_size,
    :ironmon_fusion_tutor_catalog_fingerprint,
    :ironmon_fusion_tutor_source_fingerprint
  ].freeze

  class MoveAccessGenerator
    SCHEMA_VERSION = 1
    LEVEL_UP_RULES_VERSION = 1
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
      return Ironmon.fnv1a_64_joined(
        [rules_version, @seed, "move_access", *parts]
      )
    end
  end
end
