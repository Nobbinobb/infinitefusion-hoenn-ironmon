#===============================================================================
# Ironmon species mapping readiness and load lifecycle
#===============================================================================

module Ironmon
  def self.current_species_mappings?
    return false if !$PokemonGlobal
    return false if !SpeciesGenerator::SUPPORTED_SCHEMA_VERSIONS.include?(
      $PokemonGlobal.ironmon_species_generator_version
    )
    return false if !$PokemonGlobal.ironmon_wild_species_map.is_a?(Hash)
    return false if !$PokemonGlobal.ironmon_trainer_species_map.is_a?(Hash)
    return true
  end

  def self.generate_species_mappings
    raise SpeciesGenerationError, "run metadata is unavailable" if !$PokemonGlobal
    raise SpeciesGenerationError, "the custom fusion pool is unavailable" if
      !prepare_custom_fusion_pool
    record_generator_metadata({
      :ironmon_species_generator_version => SpeciesGenerator::SCHEMA_VERSION,
      :ironmon_wild_species_map => {},
      :ironmon_trainer_species_map => {}
    })
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
    return @preset_generation_error_message if
      @preset_generation_error_message
    return @ability_randomization_error_message if
      @ability_randomization_error_message
    return @base_stat_randomization_error_message if
      @base_stat_randomization_error_message
    return @evolution_randomization_error_message if
      @evolution_randomization_error_message
    return @move_access_randomization_error_message if
      @move_access_randomization_error_message
    return @item_randomization_error_message if
      @item_randomization_error_message
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
  end
end

Ironmon.register_game_load_hook(
  :species_randomization,
  proc { |_save_data| Ironmon.reset_species_generator_cache },
  proc do |_save_data, _result|
    next if Ironmon.checkpoint_reset_loading?
    if Ironmon.active? && !Ironmon.current_species_mappings?
      raise Ironmon::SpeciesGenerationError,
            "the saved species generator is incompatible"
    end
    if Ironmon.active?
      Ironmon.prepare_player_fusion_pairing
      Ironmon.refresh_invalid_species_mappings
      Ironmon.record_custom_fusion_pool_metadata
      Ironmon.refresh_loaded_wild_encounter_table
      Ironmon.enforce_party_limit
      Ironmon.convert_owned_hms_to_tools
    end
  end
)
