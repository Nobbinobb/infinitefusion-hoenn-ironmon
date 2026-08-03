#===============================================================================
# Deterministic wild and trainer species generators
#===============================================================================

module Ironmon
  class SpeciesGenerationError < StandardError; end

  class SpeciesGenerator
    SCHEMA_VERSION = 1
    FNV_OFFSET_BASIS = 14_695_981_039_346_656_037
    FNV_PRIME = 1_099_511_628_211
    FNV_MASK = 0xFFFFFFFFFFFFFFFF

    attr_reader :mapping

    def initialize(seed, namespace, policy, normal_pool, fusion_pool, mapping)
      @seed = seed.to_i
      @namespace = namespace.to_s
      @policy = policy
      @normal_pool = normal_pool
      @fusion_pool = fusion_pool
      @mapping = mapping || {}
    end

    def map(species)
      species_data = GameData::Species.try_get(species)
      return species if !species_data
      source_id = species_data.id_number
      return species if source_id <= 0
      return species if source_id >= Settings::ZAPMOLCUNO_NB
      mapped_id = map_id(source_id)
      mapped_species = GameData::Species.try_get(mapped_id)
      return mapped_species ? mapped_species.id : species
    rescue Exception => e
      echoln "Ironmon species mapping failed for #{species}: #{e.message}"
      return species
    end

    def map_id(source_id)
      stored = @mapping[source_id]
      if stored
        return stored if allowed_species_id?(stored)
        @mapping.delete(source_id)
      end
      pool = select_pool(source_id)
      if !pool || pool.empty?
        raise SpeciesGenerationError,
              "the #{@namespace} #{@policy} species pool is empty"
      end
      selected = pool[deterministic_value(source_id, "species") % pool.length]
      selected_data = GameData::Species.get(selected)
      @mapping[source_id] = selected_data.id_number
      return @mapping[source_id]
    end

    private

    def allowed_species_id?(species_id)
      case @policy
      when Configuration::POLICY_NORMAL_ONLY
        @normal_pool_index ||= pool_index(@normal_pool)
        return @normal_pool_index[species_id] == true
      when Configuration::POLICY_CUSTOM_FUSIONS_ONLY
        @fusion_pool_index ||= pool_index(@fusion_pool)
        return @fusion_pool_index[species_id] == true
      else
        @normal_pool_index ||= pool_index(@normal_pool)
        @fusion_pool_index ||= pool_index(@fusion_pool)
        return @normal_pool_index[species_id] == true ||
               @fusion_pool_index[species_id] == true
      end
    end

    def pool_index(pool)
      index = {}
      pool.each do |species|
        match = /\AB(\d+)H(\d+)\z/.match(species.to_s)
        species_id = if match
                       (match[1].to_i * NB_POKEMON) + match[2].to_i
                     else
                       GameData::Species.get(species).id_number
                     end
        index[species_id] = true
      end
      return index
    end

    def select_pool(source_id)
      case @policy
      when Configuration::POLICY_NORMAL_ONLY
        return @normal_pool
      when Configuration::POLICY_CUSTOM_FUSIONS_ONLY
        return @fusion_pool
      else
        category = deterministic_value(source_id, "category") % 2
        return category == 0 ? @normal_pool : @fusion_pool
      end
    end

    def deterministic_value(source_id, purpose)
      value = FNV_OFFSET_BASIS
      input = [SCHEMA_VERSION, @seed, @namespace, source_id, purpose].join("|")
      input.each_byte do |byte|
        value ^= byte
        value = (value * FNV_PRIME) & FNV_MASK
      end
      return value
    end
  end

  def self.normal_species_pool
    if !@normal_species_pool
      pool = []
      (1..NB_POKEMON).each do |species_id|
        species = GameData::Species.try_get(species_id)
        pool << species.id if species && species.id_number == species_id
      end
      if pool.empty?
        raise SpeciesGenerationError, "the normal species pool is empty"
      end
      @normal_species_pool = pool.freeze
    end
    return @normal_species_pool
  end

  def self.stored_species_mapping(kind)
    return {} if !$PokemonGlobal
    if kind == :wild
      mapping = $PokemonGlobal.ironmon_wild_species_map
      if !mapping.is_a?(Hash)
        mapping = {}
        $PokemonGlobal.ironmon_wild_species_map = mapping
      end
      return mapping
    end
    mapping = $PokemonGlobal.ironmon_trainer_species_map
    if !mapping.is_a?(Hash)
      mapping = {}
      $PokemonGlobal.ironmon_trainer_species_map = mapping
    end
    return mapping
  end

  def self.species_generator(kind)
    configuration_value = configuration
    policy = kind == :wild ? configuration_value.wild_policy :
      configuration_value.trainer_policy
    variable = kind == :wild ? :@wild_species_generator :
      :@trainer_species_generator
    generator = instance_variable_get(variable)
    if !generator
      generator = SpeciesGenerator.new(
        $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0,
        kind,
        policy,
        normal_species_pool,
        custom_fusion_pool,
        stored_species_mapping(kind)
      )
      instance_variable_set(variable, generator)
    end
    return generator
  end

  def self.wild_species_for(species)
    return species if !active?
    return legacy_species_for(species) if !current_species_mappings?
    return species_generator(:wild).map(species)
  end

  # Pokemon objects created by overworld encounters can reach battle through
  # pbWildBattleSpecific rather than the ordinary species/level entry point.
  # Mark mapped objects so visible overworld Pokemon are not mapped a second
  # time when the player touches them.
  def self.prepare_wild_pokemon(pokemon)
    return pokemon if !active? || !pokemon
    return pokemon if pokemon.instance_variable_get(
      :@ironmon_wild_policy_mapped
    )
    mapped_species = wild_species_for(pokemon.species)
    if pokemon.species != mapped_species
      pokemon.species = mapped_species
      pokemon.pif_sprite = nil if pokemon.respond_to?(:pif_sprite=)
      pokemon.reset_moves
      pokemon.calc_stats
    end
    pokemon.instance_variable_set(:@ironmon_wild_policy_mapped, true)
    return pokemon
  end

  def self.trainer_species_for(species)
    return species if !active?
    return legacy_species_for(species) if !current_species_mappings?
    return species_generator(:trainer).map(species)
  end

  def self.custom_fusion_species?(species)
    species_data = GameData::Species.try_get(species)
    return false if !species_data
    if !@custom_fusion_species_index
      @custom_fusion_species_index = {}
      custom_fusion_pool.each do |custom_species|
        @custom_fusion_species_index[custom_species] = true
      end
    end
    return @custom_fusion_species_index[species_data.id] == true
  end

  def self.trainer_species_allowed?(species)
    species_data = GameData::Species.try_get(species)
    return false if !species_data
    policy = configuration.trainer_policy
    if policy == Configuration::POLICY_NORMAL_ONLY
      return species_data.id_number <= NB_POKEMON
    end
    if policy == Configuration::POLICY_CUSTOM_FUSIONS_ONLY
      return custom_fusion_species?(species_data.id)
    end
    return species_data.id_number <= NB_POKEMON ||
      custom_fusion_species?(species_data.id)
  end

  # This is a final boundary check for trainer paths owned by the base game.
  # Most parties have already been mapped, so policy-valid entries are left
  # untouched. Any original or story-created species which bypassed that path
  # is repaired before the battle begins.
  def self.ensure_trainer_party_policy(trainer)
    return trainer if !active? || !trainer || !trainer.party
    trainer.party.each do |pokemon|
      next if trainer_species_allowed?(pokemon.species)
      mapped_species = trainer_species_for(pokemon.species)
      next if pokemon.species == mapped_species
      pokemon.species = mapped_species
      pokemon.pif_sprite = nil if pokemon.respond_to?(:pif_sprite=)
      pokemon.reset_moves
      pokemon.calc_stats
    end
    return trainer
  end

  # Dynamic Hoenn trainers (the rival, Wally, and rematch trainers) keep a
  # story-owned team which may catch, fuse, unfuse, reverse, or evolve between
  # battles. Map clones at the battle boundary so those story operations remain
  # intact while every species actually battled obeys the trainer policy.
  def self.trainer_battle_party(party)
    return party if !active?
    return party.map do |entry|
      if entry.is_a?(Pokemon)
        mapped = entry.clone
        mapped_species = trainer_species_for(entry.species)
        if mapped.species != mapped_species
          mapped.species = mapped_species
          mapped.pif_sprite = nil if mapped.respond_to?(:pif_sprite=)
          mapped.reset_moves
          mapped.calc_stats
        end
        mapped
      elsif entry.is_a?(Symbol) || entry.is_a?(Integer)
        trainer_species_for(entry)
      else
        entry
      end
    end
  end

  # NPC story scripts sometimes combine Pokemon which Ironmon has already
  # presented as fusions. Reduce each input to the component matching its role
  # so the story always creates one legal two-base fusion, never a fusion of
  # fusions. The trainer generator still controls what is shown in battle.
  def self.npc_fusion_component(species, role)
    species_data = GameData::Species.try_get(species)
    if !species_data
      raise SpeciesGenerationError, "an NPC fusion input is invalid"
    end
    return species_data.id if species_data.id_number <= NB_POKEMON
    if species_data.id_number >= Settings::ZAPMOLCUNO_NB
      raise SpeciesGenerationError,
            "an NPC fusion input is a special fusion species"
    end
    if role == :body &&
       species_data.respond_to?(:get_body_species_symbol)
      return species_data.get_body_species_symbol
    end
    if role == :head &&
       species_data.respond_to?(:get_head_species_symbol)
      return species_data.get_head_species_symbol
    end
    raise SpeciesGenerationError,
          "an NPC fusion input has no usable #{role} component"
  end

  def self.npc_fusion_source(body_species, head_species)
    body = npc_fusion_component(body_species, :body)
    head = npc_fusion_component(head_species, :head)
    fusion = getFusedPokemonIdFromSymbols(body, head)
    fusion_data = GameData::Species.try_get(fusion)
    if !fusion_data || fusion_data.id_number <= NB_POKEMON ||
       fusion_data.id_number >= Settings::ZAPMOLCUNO_NB
      raise SpeciesGenerationError,
            "an NPC story fusion did not produce a valid two-species fusion"
    end
    return fusion_data.id
  end

  def self.npc_fusion_input_clone(pokemon, role)
    clone = pokemon.clone
    component = npc_fusion_component(pokemon.species, role)
    if clone.species != component
      clone.species = component
      clone.pif_sprite = nil if clone.respond_to?(:pif_sprite=)
      clone.reset_moves
      clone.calc_stats
    end
    return clone
  end

  # The unmodified Wally event rejects fused gifts after allowing the player to
  # select them. Custom Fusions Only could therefore loop forever. Its event
  # command is redirected here at startup; non-Ironmon modes retain the exact
  # original rejection behavior.
  def self.wally_rejects_gift?(pokemon)
    return pokemon.isFusion? if !active?
    return !utility_slave?(pokemon)
  end

  def self.patch_wally_gift_event
    return if @wally_gift_event_patched
    return if !$data_common_events
    $data_common_events.compact.each do |event|
      next if event.name != "Wally_partner_dialogues"
      event.list.each do |command|
        next if command.code != 355 && command.code != 655
        next if !command.parameters || command.parameters.empty?
        next if command.parameters[0] != "pbSet(3,pokemon.isFusion?)"
        command.parameters[0] =
          "pbSet(3,Ironmon.wally_rejects_gift?(pokemon))"
        @wally_gift_event_patched = true
        return
      end
    end
  end

  # Kept as a compatibility name for the Step 1.1 encounter hooks.
  def self.randomized_species_for(species)
    return wild_species_for(species)
  end

  def self.current_species_mappings?
    return false if !$PokemonGlobal
    return false if $PokemonGlobal.ironmon_species_generator_version !=
      SpeciesGenerator::SCHEMA_VERSION
    return false if !$PokemonGlobal.ironmon_wild_species_map.is_a?(Hash)
    return false if !$PokemonGlobal.ironmon_trainer_species_map.is_a?(Hash)
    return true
  end

  def self.legacy_species_for(species)
    return species if !$PokemonGlobal || !$PokemonGlobal.psuedoBSTHash
    species_data = GameData::Species.try_get(species)
    return species if !species_data
    source_id = species_data.id_number
    return species if source_id > NB_POKEMON
    mapped_id = $PokemonGlobal.psuedoBSTHash[source_id]
    mapped = GameData::Species.try_get(mapped_id)
    return mapped ? mapped.id : species
  end

  def self.generate_species_mappings
    raise SpeciesGenerationError, "run metadata is unavailable" if !$PokemonGlobal
    raise SpeciesGenerationError, "the custom fusion pool is unavailable" if
      !prepare_custom_fusion_pool
    $PokemonGlobal.ironmon_species_generator_version =
      SpeciesGenerator::SCHEMA_VERSION
    $PokemonGlobal.ironmon_wild_species_map = {}
    $PokemonGlobal.ironmon_trainer_species_map = {}
    @wild_species_generator = nil
    @trainer_species_generator = nil

    wild_generator = species_generator(:wild)
    (1..NB_POKEMON).each { |species_id| wild_generator.map_id(species_id) }
    $PokemonGlobal.psuedoBSTHash = wild_generator.mapping

    trainer_generator = species_generator(:trainer)
    (1..NB_POKEMON).each { |species_id| trainer_generator.map_id(species_id) }
    trainer_parties = {}
    getTrainersDataMode.list_all.each do |_trainer_id, trainer|
      trainer_parties[trainer.id] = trainer.pokemon.map do |pokemon|
        species = GameData::Species.get(pokemon[:species])
        trainer_generator.map_id(species.id_number)
      end
    end
    $PokemonGlobal.randomTrainersHash = trainer_parties
    return true
  end

  def self.prepare_species_mappings
    generate_species_mappings
    @species_generation_error_message = nil
    return true
  rescue SpeciesGenerationError => e
    @species_generation_error_message = _INTL(
      "Ironmon could not generate its species mappings: {1}", e.message
    )
    echoln @species_generation_error_message
    return false
  rescue Exception => e
    @species_generation_error_message = _INTL(
      "Ironmon could not generate its species mappings because of an unexpected error: {1}",
      e.message
    )
    echoln @species_generation_error_message
    return false
  end

  def self.refresh_invalid_species_mappings
    return false if !$PokemonGlobal
    [:wild, :trainer].each do |kind|
      generator = species_generator(kind)
      (1..NB_POKEMON).each { |species_id| generator.map_id(species_id) }
    end
    return true
  end

  def self.species_generation_error_message
    return @species_generation_error_message || custom_fusion_pool_error_message
  end

  def self.reset_species_generator_cache
    @wild_species_generator = nil
    @trainer_species_generator = nil
    @custom_fusion_species_index = nil
  end
end

module Game
  class << self
    alias ironmon_species_original_initialize initialize
    def initialize
      result = ironmon_species_original_initialize
      Ironmon.patch_wally_gift_event
      return result
    end

    alias ironmon_original_load load
    def load(save_data)
      Ironmon.reset_species_generator_cache
      result = ironmon_original_load(save_data)
      if Ironmon.active? && !Ironmon.current_species_mappings?
        if !Ironmon.prepare_species_mappings
          raise Ironmon::SpeciesGenerationError,
                Ironmon.species_generation_error_message
        end
        Ironmon.record_custom_fusion_pool_metadata
        echoln "Ironmon migrated legacy species mappings to the current generator."
      end
      if Ironmon.active?
        Ironmon.prepare_player_fusion_pairing
        Ironmon.refresh_invalid_species_mappings
        Ironmon.record_custom_fusion_pool_metadata
      end
      return result
    end
  end
end
