#===============================================================================
# Deterministic wild and trainer species generators
#===============================================================================

module Ironmon
  class SpeciesGenerationError < StandardError; end

  class SpeciesGenerator
    SCHEMA_VERSION = 2
    LEGACY_SCHEMA_VERSION = 1
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

    def map(species, context)
      species_data = GameData::Species.try_get(species)
      return species if !species_data
      source_id = species_data.id_number
      return species if source_id <= 0
      return species if source_id >= Settings::ZAPMOLCUNO_NB
      mapped_id = map_id(source_id, context)
      mapped_species = GameData::Species.try_get(mapped_id)
      return mapped_species ? mapped_species.id : species
    rescue Exception => e
      echoln "Ironmon species mapping failed for #{species}: #{e.message}"
      return species
    end

    def map_id(source_id, context)
      key = mapping_key(source_id, context)
      stored = @mapping[key]
      if stored
        return stored if allowed_species_id?(stored)
        @mapping.delete(key)
      end
      pool = select_pool(source_id, context)
      if !pool || pool.empty?
        raise SpeciesGenerationError,
              "the #{@namespace} #{@policy} species pool is empty"
      end
      selected = pool[deterministic_value(source_id, context, "species") % pool.length]
      selected_data = GameData::Species.get(selected)
      @mapping[key] = selected_data.id_number
      return @mapping[key]
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

    def select_pool(source_id, context)
      case @policy
      when Configuration::POLICY_NORMAL_ONLY
        return @normal_pool
      when Configuration::POLICY_CUSTOM_FUSIONS_ONLY
        return @fusion_pool
      else
        category = deterministic_value(source_id, context, "category") % 2
        return category == 0 ? @normal_pool : @fusion_pool
      end
    end

    def mapping_key(source_id, context)
      normalized = context.is_a?(Array) ? context : [context]
      return [SCHEMA_VERSION, @namespace, *normalized, source_id]
    end

    def deterministic_value(source_id, context, purpose)
      value = FNV_OFFSET_BASIS
      normalized = context.is_a?(Array) ? context : [context]
      input = [SCHEMA_VERSION, @seed, @namespace, *normalized, source_id,
               purpose].join("|")
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

  def self.wild_species_for(species, context = nil)
    return species if !active?
    return legacy_species_for(species, :wild) if legacy_species_mappings?
    context ||= wild_script_context(:unspecified)
    return species_generator(:wild).map(species, context)
  end

  def self.wild_script_context(purpose, subslot = 0)
    interpreter = if $game_system && $game_system.respond_to?(:map_interpreter)
                    $game_system.map_interpreter
                  end
    if interpreter && interpreter.running?
      return [
        :script,
        interpreter.instance_variable_get(:@map_id),
        interpreter.instance_variable_get(:@event_id),
        interpreter.instance_variable_get(:@index),
        purpose,
        subslot
      ]
    end
    location = caller(1, 1)[0] rescue "unknown"
    map_id = $game_map ? $game_map.map_id : 0
    encounter_type = $PokemonTemp ? $PokemonTemp.encounterType : nil
    return [:call_site, map_id, encounter_type, location, purpose, subslot]
  end

  # Pokemon objects created by overworld encounters can reach battle through
  # pbWildBattleSpecific rather than the ordinary species/level entry point.
  # Mark mapped objects so visible overworld Pokemon are not mapped a second
  # time when the player touches them.
  def self.prepare_wild_pokemon(pokemon, context = nil)
    return pokemon if !active? || !pokemon
    return pokemon if pokemon.instance_variable_get(
      :@ironmon_wild_policy_mapped
    )
    mapped_species = pokemon.species
    mapped_fusion = false
    species_data = GameData::Species.try_get(pokemon.species)
    if !legacy_species_mappings? &&
       configuration.wild_policy == Configuration::POLICY_NORMAL_ONLY &&
       species_data && species_data.id_number > NB_POKEMON &&
       species_data.id_number < Settings::ZAPMOLCUNO_NB
      mapped_species, mapped_fusion = prepare_wild_table_result(pokemon.species)
    end
    mapped_species = wild_species_for(pokemon.species, context) if !mapped_fusion
    if pokemon.species != mapped_species
      pokemon.species = mapped_species
      pokemon.pif_sprite = nil if pokemon.respond_to?(:pif_sprite=)
      pokemon.reset_moves
      pokemon.calc_stats
    end
    pokemon.instance_variable_set(:@ironmon_wild_policy_mapped, true)
    return pokemon
  end

  def self.trainer_species_for(species, context = nil)
    return species if !active?
    return legacy_species_for(species, :trainer) if legacy_species_mappings?
    context ||= [:unspecified]
    return species_generator(:trainer).map(species, context)
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

  def self.wild_species_allowed?(species)
    species_data = GameData::Species.try_get(species)
    return false if !species_data
    policy = configuration.wild_policy
    if policy == Configuration::POLICY_NORMAL_ONLY
      return species_data.id_number <= NB_POKEMON
    end
    if policy == Configuration::POLICY_CUSTOM_FUSIONS_ONLY
      return custom_fusion_species?(species_data.id)
    end
    return species_data.id_number <= NB_POKEMON ||
      custom_fusion_species?(species_data.id)
  end

  def self.prepare_wild_table_result(species)
    return [species, false] if !active? || legacy_species_mappings?
    return [species, true] if wild_species_allowed?(species)
    return [species, false] if
      configuration.wild_policy != Configuration::POLICY_NORMAL_ONLY
    species_data = GameData::Species.try_get(species)
    return [species, false] if !species_data ||
      species_data.id_number <= NB_POKEMON ||
      species_data.id_number >= Settings::ZAPMOLCUNO_NB
    return [species, false] if
      !species_data.respond_to?(:get_body_species_symbol) ||
      !species_data.respond_to?(:get_head_species_symbol)
    mapped = player_fusion_species(
      species_data.get_body_species_symbol,
      species_data.get_head_species_symbol
    )
    return [mapped, true]
  rescue PlayerFusionMappingError => e
    echoln "Ironmon could not map a fused wild encounter: #{e.message}"
    return [species, false]
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
    trainer.party.each_with_index do |pokemon, slot|
      next if trainer_species_allowed?(pokemon.species)
      context = pokemon.instance_variable_get(:@ironmon_trainer_slot_context)
      if !context
        trainer_type = trainer.respond_to?(:trainer_type) ?
          trainer.trainer_type : :unknown
        trainer_name = trainer.respond_to?(:name) ? trainer.name : ""
        context = [:boundary, trainer_type, trainer_name, slot]
      end
      mapped_species = trainer_species_for(pokemon.species, context)
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
  def self.trainer_battle_party(party, party_context = [:dynamic])
    return party if !active?
    return party.each_with_index.map do |entry, slot|
      context = [*party_context, slot]
      if entry.is_a?(Pokemon)
        mapped = entry.clone
        mapped_species = trainer_species_for(entry.species, context)
        if mapped.species != mapped_species
          mapped.species = mapped_species
          mapped.pif_sprite = nil if mapped.respond_to?(:pif_sprite=)
          mapped.reset_moves
          mapped.calc_stats
        end
        mapped
      elsif entry.is_a?(Symbol) || entry.is_a?(Integer)
        trainer_species_for(entry, context)
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

  def self.patch_wally_gift_event
    return if @wally_gift_event_patched
    return if !$data_common_events
    $data_common_events.compact.each do |event|
      next if event.name != "Wally_partner_dialogues"
      event.list.each do |command|
        next if command.code != 111
        next if !command.parameters || command.parameters.length < 2
        if command.parameters[0] == 12 &&
           command.parameters[1] == "$Trainer.party.length >= 2"
          command.parameters[1] = "Ironmon.wally_gift_available?"
        elsif command.parameters[0] == 0 && command.parameters[1] == 2123
          command.parameters = [12, "Ironmon.wally_gift_story_ready?"]
        end
      end
      event.list.each_with_index do |command, index|
        next if command.code != 355
        next if !command.parameters || command.parameters.empty?
        next if command.parameters[0] != "pbChoosePokemon(1,2,"
        command.parameters[0] = "Ironmon.choose_wally_gift_pokemon(1,2)"
        3.times do |offset|
          continuation = event.list[index + offset + 1]
          continuation.parameters[0] = "" if continuation &&
            continuation.code == 655 && continuation.parameters
        end
        @wally_gift_event_patched = true
        return
      end
    end
  end

  def self.wally_gift_available?
    return true if active?
    return $Trainer && $Trainer.party && $Trainer.party.length >= 2
  end

  def self.wally_gift_story_ready?
    return true if active?
    return $game_switches && $game_switches[2123]
  end

  # Kept as a compatibility name for the Step 1.1 encounter hooks.
  def self.randomized_species_for(species)
    return wild_species_for(species)
  end

  def self.current_species_mappings?
    return false if !$PokemonGlobal
    versions = [SpeciesGenerator::LEGACY_SCHEMA_VERSION,
                SpeciesGenerator::SCHEMA_VERSION]
    return false if !versions.include?(
      $PokemonGlobal.ironmon_species_generator_version
    )
    return false if !$PokemonGlobal.ironmon_wild_species_map.is_a?(Hash)
    return false if !$PokemonGlobal.ironmon_trainer_species_map.is_a?(Hash)
    return true
  end

  def self.legacy_species_mappings?
    return $PokemonGlobal &&
      $PokemonGlobal.ironmon_species_generator_version ==
        SpeciesGenerator::LEGACY_SCHEMA_VERSION
  end

  def self.legacy_species_for(species, kind = :wild)
    return species if !$PokemonGlobal
    species_data = GameData::Species.try_get(species)
    return species if !species_data
    source_id = species_data.id_number
    mapping = kind == :wild ? $PokemonGlobal.ironmon_wild_species_map :
      $PokemonGlobal.ironmon_trainer_species_map
    return species if !mapping.is_a?(Hash)
    mapped_id = mapping[source_id]
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
    $PokemonGlobal.psuedoBSTHash = {}
    (1..NB_POKEMON).each do |species_id|
      $PokemonGlobal.psuedoBSTHash[species_id] = species_id
    end
    $PokemonGlobal.randomTrainersHash = {}
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
    return true if legacy_species_mappings?
    prepare_wild_encounter_slot_mappings
    refresh_trainer_slot_mappings
    return true
  end

  def self.prepare_wild_encounter_slot_mappings
    modes = [GameData::Encounter]
    modes << GameData::EncounterModern if defined?(GameData::EncounterModern)
    modes.each do |mode|
      mode.each do |data|
        data.types.each do |encounter_type, entries|
          entries.each_with_index do |entry, slot|
            species = GameData::Species.get(entry[1])
            context = [:table, mode.name, data.map, data.version,
                       encounter_type, slot]
            species_generator(:wild).map_id(species.id_number, context)
          end
        end
      end
    end
  end

  def self.refresh_trainer_slot_mappings
    generator = species_generator(:trainer)
    getTrainersDataMode.list_all.each do |_trainer_id, trainer|
      trainer.pokemon.each_with_index do |pokemon, slot|
        species = GameData::Species.get(pokemon[:species])
        generator.map_id(species.id_number, [:pbs, trainer.id, slot])
      end
    end
  end

  def self.species_generation_error_message
    return @species_generation_error_message || custom_fusion_pool_error_message
  end

  def self.generation_error_message
    return @ability_randomization_error_message if
      @ability_randomization_error_message
    return @base_stat_randomization_error_message if
      @base_stat_randomization_error_message
    return @move_access_randomization_error_message if
      @move_access_randomization_error_message
    return species_generation_error_message
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
      return result if Ironmon.checkpoint_reset_loading?
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
        Ironmon.enforce_party_limit
        Ironmon.convert_owned_hms_to_tools
      end
      return result
    end
  end
end
