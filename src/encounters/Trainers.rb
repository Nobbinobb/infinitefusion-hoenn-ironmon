#===============================================================================
# Ironmon trainer-party and scripted trainer battle integration
#===============================================================================

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

module Ironmon
  def self.with_trainer_battle_format_policy(one_against_two = false)
    previous_separate_trainers = @separate_simultaneous_trainers
    previous_one_against_two = @one_against_two_trainer_battle
    @separate_simultaneous_trainers = true
    @one_against_two_trainer_battle = (
      previous_one_against_two || one_against_two
    )
    return yield
  ensure
    @separate_simultaneous_trainers = previous_separate_trainers
    @one_against_two_trainer_battle = previous_one_against_two
  end

  def self.separate_simultaneous_trainers?
    return !!@separate_simultaneous_trainers
  end

  def self.one_against_two_trainer_battle?
    return !!@one_against_two_trainer_battle
  end

  def self.show_wally_tutorial_fusion(body_pokemon, head_pokemon,
                                       fusion_species)
    fusion = GameData::Species.get(fusion_species)
    target_head = GameData::Species.get(
      fusion.get_head_species_symbol
    ).id_number
    target_body = GameData::Species.get(
      fusion.get_body_species_symbol
    ).id_number
    target_sprite = BattleSpriteLoader.new.obtain_fusion_pif_sprite(
      target_head, target_body
    )
    scene = PokemonFusionScene.new
    if scene.pbStartScreen(
      body_pokemon, head_pokemon, fusion.id_number, :DNASPLICERS,
      target_sprite
    )
      scene.pbFusionScreen(false, false, false, false)
      scene.pbEndScreen
    end
  end
end

class PokemonTemp
  alias ironmon_original_trainer_record_battle_rule recordBattleRule
  def recordBattleRule(rule, var = nil)
    if Ironmon.one_against_two_trainer_battle? &&
       ["double", "2v2"].include?(rule.to_s.downcase) &&
       (!$PokemonGlobal || !$PokemonGlobal.partner)
      return ironmon_original_trainer_record_battle_rule("1v2", var)
    end
    return ironmon_original_trainer_record_battle_rule(rule, var)
  end
end

class Game_Player
  alias ironmon_original_triggered_trainer_events pbTriggeredTrainerEvents
  def pbTriggeredTrainerEvents(triggers, checkIfRunning = true)
    # This exact query is only used while a trainer battle is trying to recruit
    # a second independently triggered event into the same battle.
    if Ironmon.separate_simultaneous_trainers? &&
       triggers == [2] && !checkIfRunning
      return []
    end
    return ironmon_original_triggered_trainer_events(
      triggers, checkIfRunning
    )
  end
end

alias ironmon_original_trainer_battle_format_policy pbTrainerBattle
def pbTrainerBattle(trainerID, trainerName, endSpeech = nil,
                    doubleBattle = false, trainerPartyID = 0,
                    canLose = false, outcomeVar = 1, name_override = nil,
                    trainer_type_overide = nil, event_id = nil, map_id = nil)
  if Ironmon.active?
    return Ironmon.with_trainer_battle_format_policy(doubleBattle) do
      ironmon_original_trainer_battle_format_policy(
        trainerID, trainerName, endSpeech, doubleBattle, trainerPartyID,
        canLose, outcomeVar, name_override, trainer_type_overide, event_id,
        map_id
      )
    end
  end
  return ironmon_original_trainer_battle_format_policy(
    trainerID, trainerName, endSpeech, doubleBattle, trainerPartyID, canLose,
    outcomeVar, name_override, trainer_type_overide, event_id, map_id
  )
end

alias ironmon_original_multi_trainer_battle pbMultiTrainerBattle
def pbMultiTrainerBattle(trainers_array, canLose = false, outcomeVar = 1)
  if !Ironmon.active? || trainers_array.size != 2
    return ironmon_original_multi_trainer_battle(
      trainers_array, canLose, outcomeVar
    )
  end

  trainer_1 = trainers_array[0]
  trainer_2 = trainers_array[1]
  trainer_1_data = GameData::Trainer.get(trainer_1[0], trainer_1[1], 0)
  displayPreBattleText(trainer_1_data)
  result = Ironmon.with_trainer_battle_format_policy(true) do
    pbDoubleTrainerBattle(
      trainer_1[0], trainer_1[1], 0, nil,
      trainer_2[0], trainer_2[1], 0, nil,
      canLose, outcomeVar
    )
  end
  if Settings::GAME_ID == :IF_HOENN
    updateRematchableTrainer(
      trainer_1[0], trainer_1[1], 0, trainer_1[2], trainer_2[2]
    )
    updateRematchableTrainer(
      trainer_2[0], trainer_2[1], 0, trainer_2[2], trainer_1[2]
    )
  end
  return result
end

alias ironmon_original_battle_challenge_battle pbBattleChallengeBattle
def pbBattleChallengeBattle
  if Ironmon.active?
    pbMessage(_INTL("Battle Frontier challenges are unavailable during an Ironmon run."))
    return false
  end
  return ironmon_original_battle_challenge_battle
end

alias ironmon_original_organized_battle_ex pbOrganizedBattleEx
def pbOrganizedBattleEx(opponent, challengedata, endspeech, endspeechwin)
  if Ironmon.active?
    pbMessage(_INTL("Organized challenge battles are unavailable during an Ironmon run."))
    return false
  end
  return ironmon_original_organized_battle_ex(
    opponent, challengedata, endspeech, endspeechwin
  )
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
  return if !Ironmon.wally_tutorial_uses_fusion?

  trainer = $PokemonGlobal.battledTrainers[BATTLED_TRAINER_WALLY_KEY]
  return if !trainer || trainer.currentTeam.length < 2
  body_pokemon = trainer.currentTeam[0]
  head_pokemon = trainer.currentTeam[1]

  begin
    fusion_plan = Ironmon.wally_tutorial_fusion_plan
    if with_fusion_screen
      preview_body = body_pokemon.clone
      preview_head = head_pokemon.clone
      preview_body.species = fusion_plan[:body]
      preview_head.species = fusion_plan[:head]
      preview_body.pif_sprite = nil if preview_body.respond_to?(:pif_sprite=)
      preview_head.pif_sprite = nil if preview_head.respond_to?(:pif_sprite=)
      preview_body.reset_moves
      preview_head.reset_moves
      preview_body.calc_stats
      preview_head.calc_stats
      Ironmon.show_wally_tutorial_fusion(
        preview_body, preview_head, fusion_plan[:fusion]
      )
    end
  rescue Ironmon::SpeciesGenerationError => e
    echoln "Ironmon skipped Wally's story fusion: #{e.message}"
    return
  end

  fused_pokemon = Ironmon.build_wally_tutorial_fusion(
    body_pokemon, head_pokemon, fusion_plan
  )
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
