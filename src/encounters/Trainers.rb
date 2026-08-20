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
