#===============================================================================
# Ironmon hooks for encounters that bypass the standard randomizer
#===============================================================================

module Ironmon
  def self.mark_wild_table_result(result)
    if active? && result
      result.instance_variable_set(:@ironmon_wild_table_result, true)
    end
    return result
  end

  def self.wild_table_result?(result)
    return result && result.instance_variable_get(
      :@ironmon_wild_table_result
    ) == true
  end

  def self.with_wild_table_battle
    previous = @wild_table_battle_active
    @wild_table_battle_active = true
    return yield
  ensure
    @wild_table_battle_active = previous
  end

  def self.wild_table_battle_active?
    return @wild_table_battle_active == true
  end

  def self.wild_battle_species_for(species, context)
    if wild_table_battle_active?
      species, accepted = prepare_wild_table_result(species)
      return species if accepted
    end
    return wild_species_for(species, context)
  end

  def self.with_wild_table_spawn(result)
    previous = @wild_table_spawn_active
    @wild_table_spawn_active = previous || wild_table_result?(result)
    return yield
  ensure
    @wild_table_spawn_active = previous
  end

  def self.wild_table_spawn_active?
    return @wild_table_spawn_active == true
  end

  def self.overworld_species_for(species, table_result, context)
    accepted = false
    if table_result
      species, accepted = prepare_wild_table_result(species)
      accepted = true if legacy_species_mappings?
    end
    return species if accepted
    return wild_species_for(species, context)
  end
end

alias ironmon_original_battle_on_step_taken pbBattleOnStepTaken
def pbBattleOnStepTaken(repel_active)
  return Ironmon.with_wild_table_battle do
    ironmon_original_battle_on_step_taken(repel_active)
  end
end

alias ironmon_original_pb_encounter pbEncounter
def pbEncounter(enc_type)
  return Ironmon.with_wild_table_battle do
    ironmon_original_pb_encounter(enc_type)
  end
end

class PokemonEncounters
  alias ironmon_original_setup setup
  def setup(map_id)
    result = ironmon_original_setup(map_id)
    return result if !Ironmon.active? || Ironmon.legacy_species_mappings?
    mode = getEncounterMode()
    data = mode.get(map_id, $PokemonGlobal.encounter_version)
    data = GameData::Encounter.get(
      map_id, $PokemonGlobal.encounter_version
    ) if !data
    return result if !data
    @encounter_tables.each do |encounter_type, entries|
      entries.each_with_index do |entry, slot|
        context = [:table, mode.name, data.map, data.version,
                   encounter_type, slot]
        entry[1] = Ironmon.wild_species_for(entry[1], context)
      end
    end
    return result
  end

  alias ironmon_original_choose_wild_pokemon choose_wild_pokemon
  def choose_wild_pokemon(enc_type, *arguments)
    result = ironmon_original_choose_wild_pokemon(enc_type, *arguments)
    return Ironmon.mark_wild_table_result(result)
  end
end

# The base game can fuse two selected encounter rows after selection. Once a
# slot policy can itself yield a fusion, feeding those results back into that
# path would attempt to create a fusion of fusions. Normal-only rows remain safe
# inputs; other policies already decide whether each authored slot is a fusion.
alias ironmon_original_is_fused_encounter isFusedEncounter
def isFusedEncounter
  if Ironmon.active? &&
     Ironmon.configuration.wild_policy !=
       Ironmon::Configuration::POLICY_NORMAL_ONLY
    return false
  end
  return ironmon_original_is_fused_encounter
end

alias ironmon_original_generate_wild_encounter generateWildEncounter
def generateWildEncounter(encounter_type)
  if !Ironmon.active? || Ironmon.legacy_species_mappings? ||
     Ironmon.configuration.wild_policy !=
       Ironmon::Configuration::POLICY_NORMAL_ONLY
    return ironmon_original_generate_wild_encounter(encounter_type)
  end
  encounter = getRegularEncounter(encounter_type)
  return if !encounter
  if isFusedEncounter
    fused_with = getRegularEncounter(encounter_type)
    encounter[0] = getFusionSpeciesSymbol(encounter[0], fused_with[0])
  end
  encounter[0] = getSpecies(encounter[0]) if encounter[0].is_a?(Integer)
  $game_switches[SWITCH_FORCE_FUSE_NEXT_POKEMON] = false
  return encounter
end

alias ironmon_original_hoenn_select_starter hoennSelectStarter
def hoennSelectStarter
  Ironmon.capture_checkpoint
  return Ironmon.with_starter_acquisition do
    ironmon_original_hoenn_select_starter
  end
end

alias ironmon_original_hoenn_select_custom_starter hoennSelectCustomStarter
def hoennSelectCustomStarter
  return Ironmon.with_starter_acquisition do
    ironmon_original_hoenn_select_custom_starter
  end
end

# Both fixed map encounters and the optional visible overworld encounters use
# this parent class. Mapping here ensures their sprite and their battle species
# agree before either StaticOverworldPokemonEvent or DynamicOverworldPokemonEvent
# performs its setup.
class OverworldPokemonEvent
  alias ironmon_original_setup_pokemon setup_pokemon
  def setup_pokemon(species, level, terrain = :Land, behavior_roaming = nil, behavior_noticed = nil)
    if !instance_variable_get(:@ironmon_randomized_species) && Ironmon.active?
      context = [:overworld, @map_id, @id]
      table_result = is_a?(DynamicOverworldPokemonEvent) &&
        Ironmon.wild_table_spawn_active?
      species = Ironmon.overworld_species_for(
        species, table_result, context
      )
      instance_variable_set(:@ironmon_randomized_species, true)
    end
    result = ironmon_original_setup_pokemon(
      species, level, terrain, behavior_roaming, behavior_noticed
    )
    if Ironmon.active? && @pokemon
      @pokemon.instance_variable_set(:@ironmon_wild_policy_mapped, true)
    end
    return result
  end

  alias ironmon_original_initialize_sprite initialize_sprite
  def initialize_sprite(terrain, species_data)
    appearance_data = species_data
    if Ironmon.active? && $Trainer &&
       species_data.id_number > NB_POKEMON &&
       species_data.id_number < Settings::ZAPMOLCUNO_NB &&
       $Trainer.seen?(species_data.id) &&
       species_data.respond_to?(:get_body_species_symbol)
      body_data = GameData::Species.try_get(
        species_data.get_body_species_symbol
      )
      appearance_data = body_data if body_data
    end
    return ironmon_original_initialize_sprite(terrain, appearance_data)
  end
end

alias ironmon_original_create_overworld_pokemon_event create_overworld_pokemon_event
def create_overworld_pokemon_event(pokemon, position, terrain,
                                   behavior_roaming = nil,
                                   behavior_noticed = nil)
  return Ironmon.with_wild_table_spawn(pokemon) do
    ironmon_original_create_overworld_pokemon_event(
      pokemon, position, terrain, behavior_roaming, behavior_noticed
    )
  end
end
