#===============================================================================
# Ironmon hooks for encounters that bypass the standard randomizer
#===============================================================================

module Ironmon
  WILD_FUSION_STANDARD_SAME_CHANCE = 10
  WILD_FUSION_STANDARD_CROSS_CHANCE = 5
  WILD_FUSION_OVERWORLD_CHANCE = 36

  def self.mark_wild_table_result(result, source = nil)
    if result
      result.instance_variable_set(:@ironmon_wild_table_result, true) if active?
      result.instance_variable_set(:@ironmon_wild_source, source) if source
    end
    return result
  end

  def self.wild_source_for(value)
    return nil if !value
    return value.instance_variable_get(:@ironmon_wild_source)
  end

  def self.attach_wild_source(result, entry_id)
    return result if !result || !entry_id
    match = entry_id.to_s.match(
      /\Aencounter:(\d+):(\d+):([^:]+):(\d+)\z/
    )
    return result if !match
    source = {
      :entry_id => entry_id.to_s,
      :map_id => match[1].to_i,
      :version => match[2].to_i,
      :encounter_type => match[3],
      :slot => match[4].to_i
    }
    return mark_wild_table_result(result, source)
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
    previous_result = @current_wild_table_spawn_result
    @wild_table_spawn_active = previous || wild_table_result?(result)
    @current_wild_table_spawn_result = result
    return yield
  ensure
    @wild_table_spawn_active = previous
    @current_wild_table_spawn_result = previous_result
  end

  def self.wild_table_spawn_active?
    return @wild_table_spawn_active == true
  end

  def self.current_wild_table_spawn_result
    return @current_wild_table_spawn_result
  end

  def self.overworld_species_for(species, table_result, context)
    accepted = false
    if table_result
      species, accepted = prepare_wild_table_result(species)
    end
    return species if accepted
    return wild_species_for(species, context)
  end

  def self.normal_wild_species?(species)
    data = GameData::Species.try_get(species)
    return data && data.id_number > 0 && data.id_number <= NB_POKEMON
  end

  def self.encounter_environment(encounter_type)
    value = encounter_type.to_s
    return :grass if value.start_with?("Land") || value == "TallGrass"
    return :cave if value.start_with?("Cave")
    return :water if value.start_with?("Water")
    return :fishing if value.end_with?("Rod")
    return :special
  end

  def self.overworld_encounter_environment?(encounter_type)
    return [:grass, :cave, :water].include?(
      encounter_environment(encounter_type)
    )
  end

  def self.wild_fusion_discovery_key(first, second, origin)
    first_source = wild_source_for(first)
    second_source = wild_source_for(second)
    return nil if !first_source || !second_source
    return nil if first_source[:map_id] != second_source[:map_id]
    return [
      "encounter_fusion", first_source[:map_id], origin,
      first_source[:version], first_source[:encounter_type],
      first_source[:slot], second_source[:version],
      second_source[:encounter_type], second_source[:slot]
    ].join(":")
  end

  def self.player_wild_fusion_species(body, head)
    return player_fusion_species(body, head)
  rescue PlayerFusionMappingError => e
    echoln "Ironmon could not map a derived wild fusion: #{e.message}"
    return nil
  end

  def self.fuse_wild_results(first, second, origin)
    return nil if !first || !second
    return nil if !normal_wild_species?(first[0]) ||
                  !normal_wild_species?(second[0])
    fusion = player_wild_fusion_species(first[0], second[0])
    return nil if !fusion
    first[0] = fusion
    key = wild_fusion_discovery_key(first, second, origin)
    queue_area_encounter_fusion(key) if key &&
      respond_to?(:queue_area_encounter_fusion)
    return first
  end

  def self.build_overworld_wild_fusion(first, second, origin)
    fusion = player_wild_fusion_species(second.species, first.species)
    return nil if !fusion
    level = (first.level + second.level) / 2
    pokemon = Pokemon.new(fusion, level)
    pokemon.instance_variable_set(:@ironmon_wild_policy_mapped, true)
    first_source = wild_source_for(first)
    second_source = wild_source_for(second)
    pokemon.instance_variable_set(
      :@ironmon_wild_sources, [first_source, second_source]
    )
    key = wild_fusion_discovery_key(first, second, origin)
    record_encountered_area_slots([key]) if key &&
      respond_to?(:record_encountered_area_slots)
    return pokemon
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
    return result if !Ironmon.active?
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

  def ironmon_choose_normal_partner(encounter_type, excluded_species,
                                    cross_environment)
    tables = @encounter_tables || {}
    types = if cross_environment
              tables.keys.select do |candidate|
                candidate != encounter_type &&
                  Ironmon.overworld_encounter_environment?(encounter_type) &&
                  Ironmon.overworld_encounter_environment?(candidate)
              end
            else
              [encounter_type]
            end
    eligible_tables = types.map do |candidate|
      entries = (tables[candidate] || []).select do |entry|
        Ironmon.normal_wild_species?(entry[1]) &&
          GameData::Species.get(entry[1]).id !=
            GameData::Species.get(excluded_species).id
      end
      [candidate, entries] if !entries.empty?
    end.compact
    return nil if eligible_tables.empty?
    _selected_type, entries = eligible_tables.sample
    total = entries.inject(0) { |sum, entry| sum + entry[0].to_i }
    return nil if total <= 0
    roll = rand(total)
    selected = entries.find do |entry|
      roll -= entry[0].to_i
      roll < 0
    end
    return nil if !selected
    result = [selected[1], rand(selected[2]..selected[3])]
    if selected.respond_to?(:area_entry_id)
      Ironmon.attach_wild_source(result, selected.area_entry_id)
      Ironmon.queue_area_encounter_entry(selected.area_entry_id) if
        Ironmon.respond_to?(:queue_area_encounter_entry)
    else
      Ironmon.mark_wild_table_result(result)
    end
    return result
  end
end

# Ironmon owns every derived wild-fusion roll. Visible overworld encounters
# fuse only when eligible spawned Pokemon enter one battle together; the base
# game's separate single-encounter random roll would bypass that boundary.
alias ironmon_original_is_fused_encounter isFusedEncounter
def isFusedEncounter
  return isFusionForced?() if Ironmon.active?
  return ironmon_original_is_fused_encounter
end

alias ironmon_original_generate_wild_encounter generateWildEncounter
def generateWildEncounter(encounter_type)
  if !Ironmon.active?
    return ironmon_original_generate_wild_encounter(encounter_type)
  end
  if $PokemonSystem && $PokemonSystem.overworld_encounters
    return ironmon_original_generate_wild_encounter(encounter_type)
  end
  encounter = getRegularEncounter(encounter_type)
  return if !encounter
  if Ironmon.normal_wild_species?(encounter[0]) &&
     !$game_switches[SWITCH_RANDOM_WILD_TO_FUSION]
    origin = nil
    cross_environment = false
    if isFusionForced?
      origin = :standard_same
    elsif Ironmon.overworld_encounter_environment?(encounter_type)
      roll = rand(100)
      if roll < Ironmon::WILD_FUSION_STANDARD_SAME_CHANCE
        origin = :standard_same
      elsif roll < Ironmon::WILD_FUSION_STANDARD_SAME_CHANCE +
                   Ironmon::WILD_FUSION_STANDARD_CROSS_CHANCE
        origin = :standard_cross
        cross_environment = true
      end
    end
    if origin
      fused_with = $PokemonEncounters.ironmon_choose_normal_partner(
        encounter_type, encounter[0], cross_environment
      )
      Ironmon.fuse_wild_results(encounter, fused_with, origin) if fused_with
    end
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
      source = Ironmon.wild_source_for(
        Ironmon.current_wild_table_spawn_result
      )
      @pokemon.instance_variable_set(:@ironmon_wild_source, source) if source
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

alias ironmon_original_trigger_overworld_wild_battle trigger_overworld_wild_battle
def trigger_overworld_wild_battle
  if !Ironmon.active?
    return ironmon_original_trigger_overworld_wild_battle
  end
  return if $PokemonTemp.overworld_wild_battle_triggered
  $PokemonTemp.overworld_wild_battle_triggered = true
  participants = $PokemonTemp.overworld_wild_battle_participants
  case participants.length
  when 0
    $PokemonTemp.overworld_wild_battle_triggered = false
    return
  when 2
    first = participants[0].pokemon
    second = participants[1].pokemon
    should_fuse = Ironmon.normal_wild_species?(first.species) &&
      Ironmon.normal_wild_species?(second.species) &&
      !first.shiny? && !second.shiny? &&
      rand(100) < Ironmon::WILD_FUSION_OVERWORLD_CHANCE
    if should_fuse
      first_source = Ironmon.wild_source_for(first)
      second_source = Ironmon.wild_source_for(second)
      same_environment = first_source && second_source &&
        first_source[:map_id] == second_source[:map_id] &&
        first_source[:version] == second_source[:version] &&
        first_source[:encounter_type] == second_source[:encounter_type]
      origin = same_environment ? :overworld_same : :overworld_cross
      fusion = Ironmon.build_overworld_wild_fusion(first, second, origin)
      if fusion
        fuse_wild_pokemon_animation(first, second)
        checkWildFusePokemonChallenge(first, second)
        pbWildBattleSpecific(fusion)
      else
        pb1v2WildBattleSpecific(first, second)
      end
    else
      pb1v2WildBattleSpecific(first, second)
    end
  when 3
    pb1v3WildBattleSpecific(
      participants[0].pokemon, participants[1].pokemon,
      participants[2].pokemon
    )
  when 1
    battler = participants[0].pokemon
    if isWeatherWind?()
      event_x = participants[0].x
      $PokemonTemp.recordBattleRule("windside", 1) if event_x &&
        event_x > $game_player.x
      $PokemonTemp.recordBattleRule("windside", 0) if event_x &&
        event_x < $game_player.x
    end
    pbWildBattleSpecific(battler)
  else
    pbWildBattleSpecific(participants[0].pokemon)
    participants.shift
  end
  participants.each { |participant| participant.despawn }
  $PokemonTemp.overworld_wild_battle_participants = []
  $PokemonTemp.overworld_wild_battle_triggered = false
end
