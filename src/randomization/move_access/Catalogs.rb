#===============================================================================
# Ironmon move-access source catalogs and deterministic fingerprints
#===============================================================================

module Ironmon
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
    return fnv1a_64_fingerprint(entries)
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
end
