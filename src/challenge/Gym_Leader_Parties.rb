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

  def self.gym_leader_addition_level(levels, addition_index)
    party_levels = levels.map { |level| level.to_i }.select { |level| level > 0 }
    return 1 if party_levels.empty?
    lowest = party_levels.min
    highest = party_levels.max
    highest_excluded = [highest - 1, 1].max
    return highest_excluded if highest_excluded < lowest
    level_count = highest_excluded - lowest + 1
    return lowest + (addition_index.to_i % level_count)
  end

  def self.expand_gym_leader_party(trainer)
    return trainer if !active? || !gym_leader?(trainer)
    return trainer if !trainer.party || trainer.party.length >= 6
    authored_levels = trainer.party.map { |pokemon| pokemon.level }
    authored_party_size = trainer.party.length

    while trainer.party.length < 6
      slot = trainer.party.length
      species = gym_leader_species_for_slot(trainer, slot)
      addition_index = slot - authored_party_size
      level = gym_leader_addition_level(authored_levels, addition_index)
      pokemon = Pokemon.new(species, level, trainer)
      # Existing members have already passed Step 1.6's boundary scaler. The
      # added member is created directly in their displayed level range.
      pokemon.instance_variable_set(:@ironmon_level_scaled, true)
      trainer.party << pokemon
    end
    return trainer
  end
end

Events.onTrainerPartyLoad += proc do |_sender, event_args|
  Ironmon.expand_gym_leader_party(event_args[0])
end
