#===============================================================================
# Deterministic expanded boss-trainer parties
#===============================================================================

module Ironmon
  LEGACY_GYM_LEADER_SCHEMA_VERSION = 1
  BOSS_TRAINER_SCHEMA_VERSION = 2
  GYM_LEADER_TYPES_BY_GAME_VERSION = {
    "6.8.2" => [
      :LEADER_Roxanne,
      :LEADER_Brawly,
      :LEADER_Wattson,
      :LEADER_Flannery,
      :LEADER_Norman,
      :LEADER_Winona,
      :LEADER_Tate,
      :LEADER_Liza,
      :LEADER_Wallace,
      :LEADER_Juan
    ].freeze
  }.freeze
  BOSS_TRAINER_TYPES_BY_GAME_VERSION = {
    "6.8.2" => [
      :RIVAL2,
      :LEADER_Roxanne,
      :LEADER_Brawly,
      :LEADER_Wattson,
      :LEADER_Flannery,
      :LEADER_Norman,
      :LEADER_Winona,
      :LEADER_Tate,
      :LEADER_Liza,
      :LEADER_Wallace,
      :LEADER_Juan,
      :ELITEFOUR_Sidney,
      :ELITEFOUR_Phoebe,
      :ELITEFOUR_Glacia,
      :ELITEFOUR_Drake,
      :CHAMPION_Steven,
      :TEAM_AQUA_BOSS,
      :TEAM_MAGMA_BOSS
    ].freeze
  }.freeze

  def self.gym_leader_types
    version = if defined?(Settings::GAME_VERSION_NUMBER)
                Settings::GAME_VERSION_NUMBER
              else
                nil
              end
    return GYM_LEADER_TYPES_BY_GAME_VERSION[version] || []
  end

  def self.gym_leader?(trainer)
    return false if !trainer || !trainer.respond_to?(:trainer_type)
    return gym_leader_types.include?(trainer.trainer_type)
  end

  def self.boss_trainer_types
    version = if defined?(Settings::GAME_VERSION_NUMBER)
                Settings::GAME_VERSION_NUMBER
              end
    return BOSS_TRAINER_TYPES_BY_GAME_VERSION[version] || []
  end

  def self.boss_trainer?(trainer)
    return boss_trainer_types.include?(boss_trainer_type(trainer))
  end

  def self.boss_trainer_type(trainer)
    return nil if !trainer
    return trainer.trainer_type if trainer.respond_to?(:trainer_type)
    return trainer.trainerType if trainer.respond_to?(:trainerType)
    return nil
  end

  def self.boss_trainer_name(trainer)
    return "" if !trainer
    return trainer.name if trainer.respond_to?(:name)
    return trainer.trainerName if trainer.respond_to?(:trainerName)
    return ""
  end

  def self.trainer_party_expansion_target(trainer, authored_size,
                                          algorithm_version = 2)
    if algorithm_version.to_i <= 1
      return gym_leader?(trainer) ? 6 : nil
    end
    return nil if !boss_trainer?(trainer)
    return [authored_size.to_i + 3, 6].min
  end

  def self.current_trainer_party_expansion_version
    return generation_profile_algorithm_version("gym_party_expansion")
  rescue Exception
    return BOSS_TRAINER_SCHEMA_VERSION
  end

  def self.gym_leader_team_store
    return {} if !$PokemonGlobal
    store = $PokemonGlobal.ironmon_gym_leader_teams
    if !store.is_a?(Hash)
      store = {}
      $PokemonGlobal.ironmon_gym_leader_teams = store
    end
    return store
  end

  def self.boss_trainer_team_key(trainer, algorithm_version)
    return [
      algorithm_version, boss_trainer_type(trainer), boss_trainer_name(trainer)
    ]
  end

  def self.boss_trainer_source_species(trainer, slot, algorithm_version)
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    return boss_trainer_source_species_for(
      seed, boss_trainer_type(trainer), boss_trainer_name(trainer), slot,
      algorithm_version
    )
  end

  def self.boss_trainer_source_species_for(seed, trainer_type, name, slot,
                                           algorithm_version = 2)
    schema_version = algorithm_version.to_i <= 1 ?
      LEGACY_GYM_LEADER_SCHEMA_VERSION : BOSS_TRAINER_SCHEMA_VERSION
    value = fnv1a_64_joined([
      schema_version, seed, trainer_type, name, slot
    ])
    return 1 + (value % NB_POKEMON)
  end

  def self.boss_trainer_species_for_slot(trainer, slot, level,
                                         algorithm_version)
    key = boss_trainer_team_key(trainer, algorithm_version)
    stored_team = gym_leader_team_store[key]
    if !stored_team.is_a?(Hash)
      stored_team = {}
      gym_leader_team_store[key] = stored_team
    end
    species = stored_team[slot]
    return species if GameData::Species.try_get(species)

    source = boss_trainer_source_species(trainer, slot, algorithm_version)
    purpose = algorithm_version.to_i <= 1 ? :gym_addition : :boss_addition
    species = trainer_species_for(
      source, [purpose, boss_trainer_type(trainer),
               boss_trainer_name(trainer), slot], level
    )
    stored_team[slot] = species
    return species
  end

  def self.boss_trainer_addition_level(levels, addition_index)
    party_levels = levels.map { |level| level.to_i }.select { |level| level > 0 }
    return 1 if party_levels.empty?
    lowest = party_levels.min
    highest = party_levels.max
    highest_excluded = [highest - 1, 1].max
    return highest_excluded if highest_excluded < lowest
    level_count = highest_excluded - lowest + 1
    return lowest + (addition_index.to_i % level_count)
  end

  def self.expand_boss_trainer_party(trainer)
    return trainer if !active? || !trainer || !trainer.party
    authored_levels = trainer.party.map { |pokemon| pokemon.level }
    authored_party_size = trainer.party.length
    algorithm_version = current_trainer_party_expansion_version
    target_size = trainer_party_expansion_target(
      trainer, authored_party_size, algorithm_version
    )
    return trainer if !target_size || authored_party_size >= target_size

    while trainer.party.length < target_size
      slot = trainer.party.length
      addition_index = slot - authored_party_size
      level = boss_trainer_addition_level(authored_levels, addition_index)
      species = boss_trainer_species_for_slot(
        trainer, slot, level, algorithm_version
      )
      pokemon = Pokemon.new(species, level, trainer)
      # Existing members have already passed Step 1.6's boundary scaler. The
      # added member is created directly in their displayed level range.
      pokemon.instance_variable_set(:@ironmon_level_scaled, true)
      trainer.party << pokemon
    end
    return trainer
  end

  def self.expand_dynamic_boss_trainer_party(trainer)
    return trainer if !active? || !trainer || !trainer.currentTeam
    authored_party_size = trainer.currentTeam.length
    algorithm_version = current_trainer_party_expansion_version
    target_size = trainer_party_expansion_target(
      trainer, authored_party_size, algorithm_version
    )
    return trainer if !target_size || authored_party_size >= target_size
    authored_levels = trainer.currentTeam.map do |pokemon|
      trainer_pokemon_effective_level(pokemon)
    end

    while trainer.currentTeam.length < target_size
      slot = trainer.currentTeam.length
      addition_index = slot - authored_party_size
      level = boss_trainer_addition_level(authored_levels, addition_index)
      species = boss_trainer_species_for_slot(
        trainer, slot, level, algorithm_version
      )
      pokemon = Pokemon.new(species, level)
      pokemon.instance_variable_set(:@ironmon_level_scaled, true)
      trainer.currentTeam << pokemon
    end
    return trainer
  end
end

Events.onTrainerPartyLoad += proc do |_sender, event_args|
  Ironmon.expand_boss_trainer_party(event_args[0])
end
