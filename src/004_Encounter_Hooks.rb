#===============================================================================
# Ironmon hooks for encounters that bypass the standard randomizer
#===============================================================================

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
      table_result = false
      if is_a?(DynamicOverworldPokemonEvent) &&
         $PokemonTemp && $PokemonTemp.encounterType
        species, table_result = Ironmon.prepare_wild_table_result(species)
      end
      context = [:overworld, @map_id, @id]
      species = Ironmon.wild_species_for(species, context) if !table_result
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

module GameData
  class Trainer
    alias ironmon_original_replace_species_with_placeholder replace_species_with_placeholder
    def replace_species_with_placeholder(species)
      resolved_species = ironmon_original_replace_species_with_placeholder(species)
      if Ironmon.active? &&
         species == Settings::RIVAL_STARTER_PLACEHOLDER_SPECIES
        return resolved_species
      end
      return Ironmon.trainer_species_for(
        resolved_species, [:pbs_placeholder, self.id, species]
      )
    end

    alias ironmon_original_replace_species_to_randomized replace_species_to_randomized
    def replace_species_to_randomized(species, trainer_id, pokemon_index)
      if Ironmon.active?
        return Ironmon.trainer_species_for(
          species, [:pbs, trainer_id, pokemon_index]
        )
      end
      return ironmon_original_replace_species_to_randomized(
        species, trainer_id, pokemon_index
      )
    end
  end
end

Events.onTrainerPartyLoad += proc do |_sender, event_args|
  Ironmon.ensure_trainer_party_policy(event_args[0])
end

alias ironmon_original_pb_wild_battle pbWildBattle
def pbWildBattle(species, level, outcomeVar = 1, canRun = true, canLose = false)
  if Ironmon.active?
    table_result = false
    if $PokemonTemp && $PokemonTemp.encounterType
      species, table_result = Ironmon.prepare_wild_table_result(species)
    end
    context = Ironmon.wild_script_context(:single, 0)
    species = Ironmon.wild_species_for(species, context) if !table_result
    random_static = $game_switches[SWITCH_RANDOM_STATIC_ENCOUNTERS]
    begin
      $game_switches[SWITCH_RANDOM_STATIC_ENCOUNTERS] = false
      result = ironmon_original_pb_wild_battle(
        species, level, outcomeVar, canRun, canLose
      )
      Ironmon.synchronize_hoenn_starter_after_battle
      return result
    ensure
      $game_switches[SWITCH_RANDOM_STATIC_ENCOUNTERS] = random_static
    end
  end
  return ironmon_original_pb_wild_battle(species, level, outcomeVar, canRun, canLose)
end

alias ironmon_original_pb_wild_battle_specific pbWildBattleSpecific
def pbWildBattleSpecific(pokemon, outcomeVar = 1, canRun = true,
                         canLose = false)
  if Ironmon.active?
    Ironmon.prepare_wild_pokemon(
      pokemon, Ironmon.wild_script_context(:specific, 0)
    )
  end
  return ironmon_original_pb_wild_battle_specific(
    pokemon, outcomeVar, canRun, canLose
  )
end

alias ironmon_original_pb_1v2_wild_battle_specific pb1v2WildBattleSpecific
def pb1v2WildBattleSpecific(pokemon1, pokemon2, outcomeVar = 1,
                            canRun = true, canLose = false)
  if Ironmon.active?
    Ironmon.prepare_wild_pokemon(
      pokemon1, Ironmon.wild_script_context(:specific, 0)
    )
    Ironmon.prepare_wild_pokemon(
      pokemon2, Ironmon.wild_script_context(:specific, 1)
    )
  end
  return ironmon_original_pb_1v2_wild_battle_specific(
    pokemon1, pokemon2, outcomeVar, canRun, canLose
  )
end

alias ironmon_original_pb_1v3_wild_battle_specific pb1v3WildBattleSpecific
def pb1v3WildBattleSpecific(pokemon1, pokemon2, pokemon3, outcomeVar = 1,
                            canRun = true, canLose = false)
  if Ironmon.active?
    Ironmon.prepare_wild_pokemon(
      pokemon1, Ironmon.wild_script_context(:specific, 0)
    )
    Ironmon.prepare_wild_pokemon(
      pokemon2, Ironmon.wild_script_context(:specific, 1)
    )
    Ironmon.prepare_wild_pokemon(
      pokemon3, Ironmon.wild_script_context(:specific, 2)
    )
  end
  return ironmon_original_pb_1v3_wild_battle_specific(
    pokemon1, pokemon2, pokemon3, outcomeVar, canRun, canLose
  )
end

alias ironmon_original_get_randomized_to getRandomizedTo
def getRandomizedTo(species)
  if Ironmon.active?
    mapped = Ironmon.wild_species_for(
      species, Ironmon.wild_script_context(:randomized_lookup)
    )
    mapped_data = GameData::Species.try_get(mapped)
    return mapped_data ? mapped_data.id_number : species
  end
  return ironmon_original_get_randomized_to(species)
end

alias ironmon_original_try_randomize_gift_pokemon tryRandomizeGiftPokemon
def tryRandomizeGiftPokemon(pokemon, dontRandomize = false)
  if Ironmon.active? && !dontRandomize &&
     !$game_switches[SWITCH_DONT_RANDOMIZE]
    pokemon.species = Ironmon.wild_species_for(
      pokemon.species, Ironmon.wild_script_context(:gift)
    )
    pokemon.reset_moves if Ironmon.move_access_randomization_active? &&
      !pokemon.shadowPokemon?
    return
  end
  return ironmon_original_try_randomize_gift_pokemon(pokemon, dontRandomize)
end

alias ironmon_original_obtain_randomized_starter obtainRandomizedStarter
def obtainRandomizedStarter(starter_index)
  if Ironmon.active?
    source = [1, 4, 7][starter_index] || 7
    mapped = Ironmon.wild_species_for(source, [:starter, starter_index])
    return GameData::Species.get(mapped).id_number
  end
  return ironmon_original_obtain_randomized_starter(starter_index)
end

alias ironmon_original_set_rival_starter setRivalStarter
def setRivalStarter(starter_index1, starter_index2)
  if Ironmon.active?
    sources = [1, 4, 7]
    body = sources[starter_index1] || sources[0]
    head = sources[starter_index2] || sources[1]
    source = getFusionSpecies(body, head).id
    starter = GameData::Species.get(
      Ironmon.trainer_species_for(source, [:rival_starter, starter_index1,
                                           starter_index2])
    ).id_number
    pbSet(VAR_RIVAL_STARTER, starter)
    $game_switches[SWITCH_DEFINED_RIVAL_STARTER] = true
    return starter
  end
  return ironmon_original_set_rival_starter(starter_index1, starter_index2)
end

# Hoenn's persistent rival team later fuses its starter with other species.
# Keep a normal story source here so that operation never becomes a forbidden
# fusion of an already-fused species. The battle-boundary hook below applies
# the configured trainer policy to the result seen in battle.
alias ironmon_original_get_hoenn_rival_starter get_hoenn_rival_starter
def get_hoenn_rival_starter
  if Ironmon.active?
    case get_rival_starter_type
    when :GRASS then return GameData::Species.get(1)
    when :FIRE then return GameData::Species.get(4)
    when :WATER then return GameData::Species.get(7)
    end
    return GameData::Species.get(1)
  end
  return ironmon_original_get_hoenn_rival_starter
end

alias ironmon_original_custom_trainer_battle customTrainerBattle
def customTrainerBattle(trainerName, trainerType, party_array,
                        default_level = 50, endSpeech = "",
                        sprite_override = nil, custom_appearance = nil,
                        items = [], canLose = false)
  party_array = Ironmon.trainer_battle_party(
    party_array, [:custom, trainerType, trainerName]
  )
  return ironmon_original_custom_trainer_battle(
    trainerName, trainerType, party_array, default_level, endSpeech,
    sprite_override, custom_appearance, items, canLose
  )
end

alias ironmon_original_rematchable_trainer_battle rematchable_trainer_battle
def rematchable_trainer_battle(rematchable_trainers = [], default_level = 50,
                               canLose = true)
  if Ironmon.active?
    rematchable_trainers = rematchable_trainers.map do |trainer|
      mapped_trainer = trainer.clone
      mapped_trainer.currentTeam = Ironmon.trainer_battle_party(
        trainer.currentTeam, [:rematch, trainer.trainerType,
                              trainer.trainerName]
      )
      mapped_trainer
    end
  end
  return ironmon_original_rematchable_trainer_battle(
    rematchable_trainers, default_level, canLose
  )
end

alias ironmon_original_wally_fuse_pokemon wally_fuse_pokemon
def wally_fuse_pokemon(with_fusion_screen = true)
  return ironmon_original_wally_fuse_pokemon(with_fusion_screen) if
    !Ironmon.active?

  trainer = $PokemonGlobal.battledTrainers[BATTLED_TRAINER_WALLY_KEY]
  return if !trainer || trainer.currentTeam.length < 2
  body_pokemon = trainer.currentTeam[0]
  head_pokemon = trainer.currentTeam[1]

  begin
    fusion_species = Ironmon.npc_fusion_source(
      body_pokemon.species, head_pokemon.species
    )
    if with_fusion_screen
      preview_body = Ironmon.npc_fusion_input_clone(body_pokemon, :body)
      preview_head = Ironmon.npc_fusion_input_clone(head_pokemon, :head)
      npcTrainerFusionScreenPokemon(preview_head, preview_body)
    end
  rescue Ironmon::SpeciesGenerationError => e
    echoln "Ironmon skipped Wally's story fusion: #{e.message}"
    return
  end

  level = (body_pokemon.level + head_pokemon.level) / 2
  fused_pokemon = Pokemon.new(fusion_species, level)
  if body_pokemon.isShiny? || head_pokemon.isShiny?
    fused_pokemon.shiny = true
    if body_pokemon.radar_shiny || head_pokemon.radar_shiny
      fused_pokemon.radar_shiny = true
    end
    if !(body_pokemon.debug_shiny || head_pokemon.debug_shiny)
      fused_pokemon.natural_shiny = true if fused_pokemon.natural_shiny
    end
  end

  trainer.currentTeam.delete(body_pokemon)
  trainer.currentTeam.delete(head_pokemon)
  trainer.currentTeam.push(fused_pokemon)
  updateRebattledTrainerWithKey(BATTLED_TRAINER_WALLY_KEY, trainer)
end

alias ironmon_original_fuse_random_team_pokemon fuse_random_team_pokemon
def fuse_random_team_pokemon(trainer)
  return ironmon_original_fuse_random_team_pokemon(trainer) if
    !Ironmon.active?
  eligible_pokemon = trainer.list_team_unfused_pokemon
  return trainer if eligible_pokemon.length < 2

  pokemon_to_fuse = eligible_pokemon.sample(2)
  body_pokemon = pokemon_to_fuse[0]
  head_pokemon = pokemon_to_fuse[1]
  begin
    fusion_species = Ironmon.npc_fusion_source(
      body_pokemon.species, head_pokemon.species
    )
  rescue Ironmon::SpeciesGenerationError => e
    echoln "Ironmon skipped an NPC rematch fusion: #{e.message}"
    return trainer
  end
  level = (body_pokemon.level + head_pokemon.level) / 2
  original_trainer = pbLoadTrainer(
    trainer.trainerType, trainer.trainerName, 0
  )
  fused_pokemon = Pokemon.new(fusion_species, level, original_trainer)

  trainer.currentTeam.delete(body_pokemon)
  trainer.currentTeam.delete(head_pokemon)
  trainer.currentTeam.push(fused_pokemon)
  trainer.log_fusion_event(
    body_pokemon.species, head_pokemon.species, fusion_species
  )
  return trainer
end

alias ironmon_original_pb_double_wild_battle pbDoubleWildBattle
def pbDoubleWildBattle(species1, level1, species2, level2,
                       outcomeVar = 1, canRun = true, canLose = false)
  if Ironmon.active?
    table_result = $PokemonTemp && $PokemonTemp.encounterType
    if table_result
      species1, = Ironmon.prepare_wild_table_result(species1)
      species2, = Ironmon.prepare_wild_table_result(species2)
    else
      species1 = Ironmon.wild_species_for(
        species1, Ironmon.wild_script_context(:double, 0)
      )
      species2 = Ironmon.wild_species_for(
        species2, Ironmon.wild_script_context(:double, 1)
      )
    end
  end
  return ironmon_original_pb_double_wild_battle(
    species1, level1, species2, level2, outcomeVar, canRun, canLose
  )
end

alias ironmon_original_pb_triple_wild_battle pbTripleWildBattle
def pbTripleWildBattle(species1, level1, species2, level2, species3, level3,
                       outcomeVar = 1, canRun = true, canLose = false)
  if Ironmon.active?
    table_result = $PokemonTemp && $PokemonTemp.encounterType
    if table_result
      species1, = Ironmon.prepare_wild_table_result(species1)
      species2, = Ironmon.prepare_wild_table_result(species2)
      species3, = Ironmon.prepare_wild_table_result(species3)
    else
      species1 = Ironmon.wild_species_for(
        species1, Ironmon.wild_script_context(:triple, 0)
      )
      species2 = Ironmon.wild_species_for(
        species2, Ironmon.wild_script_context(:triple, 1)
      )
      species3 = Ironmon.wild_species_for(
        species3, Ironmon.wild_script_context(:triple, 2)
      )
    end
  end
  return ironmon_original_pb_triple_wild_battle(
    species1, level1, species2, level2, species3, level3,
    outcomeVar, canRun, canLose
  )
end

alias ironmon_original_pb_1v2_wild_battle pb1v2WildBattle
def pb1v2WildBattle(species1, level1, species2, level2,
                    outcomeVar = 1, canRun = true, canLose = false)
  if Ironmon.active?
    species1 = Ironmon.wild_species_for(
      species1, Ironmon.wild_script_context(:one_v_two, 0)
    )
    species2 = Ironmon.wild_species_for(
      species2, Ironmon.wild_script_context(:one_v_two, 1)
    )
  end
  return ironmon_original_pb_1v2_wild_battle(
    species1, level1, species2, level2, outcomeVar, canRun, canLose
  )
end

alias ironmon_original_pb_1v3_wild_battle pb1v3WildBattle
def pb1v3WildBattle(species1, level1, species2, level2, species3, level3,
                    outcomeVar = 1, canRun = true, canLose = false)
  if Ironmon.active?
    species1 = Ironmon.wild_species_for(
      species1, Ironmon.wild_script_context(:one_v_three, 0)
    )
    species2 = Ironmon.wild_species_for(
      species2, Ironmon.wild_script_context(:one_v_three, 1)
    )
    species3 = Ironmon.wild_species_for(
      species3, Ironmon.wild_script_context(:one_v_three, 2)
    )
  end
  return ironmon_original_pb_1v3_wild_battle(
    species1, level1, species2, level2, species3, level3,
    outcomeVar, canRun, canLose
  )
end
