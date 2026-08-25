#===============================================================================
# Deterministic six-Pokemon Gym Leader parties
#===============================================================================

module Ironmon
  GYM_LEADER_SCHEMA_VERSION = 1
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

  def self.gym_leader_team_store
    return {} if !$PokemonGlobal
    store = $PokemonGlobal.ironmon_gym_leader_teams
    if !store.is_a?(Hash)
      store = {}
      $PokemonGlobal.ironmon_gym_leader_teams = store
    end
    return store
  end

  def self.gym_leader_team_key(trainer)
    name = trainer.respond_to?(:name) ? trainer.name : ""
    return [GYM_LEADER_SCHEMA_VERSION, trainer.trainer_type, name]
  end

  def self.gym_leader_source_species(trainer, slot)
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    name = trainer.respond_to?(:name) ? trainer.name : ""
    return gym_leader_source_species_for(
      seed, trainer.trainer_type, name, slot
    )
  end

  def self.gym_leader_source_species_for(seed, trainer_type, name, slot)
    value = fnv1a_64_joined([
      GYM_LEADER_SCHEMA_VERSION, seed, trainer_type, name, slot
    ])
    return 1 + (value % NB_POKEMON)
  end

  def self.gym_leader_species_for_slot(trainer, slot)
    key = gym_leader_team_key(trainer)
    stored_team = gym_leader_team_store[key]
    if !stored_team.is_a?(Hash)
      stored_team = {}
      gym_leader_team_store[key] = stored_team
    end
    species = stored_team[slot]
    return species if GameData::Species.try_get(species)

    source = gym_leader_source_species(trainer, slot)
    species = trainer_species_for(
      source, [:gym_addition, trainer.trainer_type,
               trainer.respond_to?(:name) ? trainer.name : "", slot]
    )
    stored_team[slot] = species
    return species
  end

  def self.expand_gym_leader_party(trainer)
    return trainer if !active? || !gym_leader?(trainer)
    return trainer if !trainer.party || trainer.party.length >= 6
    leader_level = trainer.party.map { |pokemon| pokemon.level }.max || 1

    while trainer.party.length < 6
      slot = trainer.party.length
      species = gym_leader_species_for_slot(trainer, slot)
      pokemon = Pokemon.new(species, leader_level, trainer)
      # Existing members have already passed Step 1.6's boundary scaler. The
      # added member is created directly at that displayed leader level.
      pokemon.instance_variable_set(:@ironmon_level_scaled, true)
      trainer.party << pokemon
    end
    return trainer
  end
end

Events.onTrainerPartyLoad += proc do |_sender, event_args|
  Ironmon.expand_gym_leader_party(event_args[0])
end
