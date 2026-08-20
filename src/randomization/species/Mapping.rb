#===============================================================================
# Ironmon wild, trainer, and NPC species mapping behavior
#===============================================================================

module Ironmon
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
end
