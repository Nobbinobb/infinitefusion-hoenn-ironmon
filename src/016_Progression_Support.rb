#===============================================================================
# Generated progression Pokemon for required gifts and trades
#===============================================================================

module Ironmon
  PROGRESSION_TRADE_EXCLUSION = :progression_trade
  PROGRESSION_POKEMON_NAMESPACE = "progression_pokemon"

  def self.progression_trade_pokemon?(pokemon)
    return pokemon &&
      pokemon.ironmon_party_exclusion == PROGRESSION_TRADE_EXCLUSION
  rescue StandardError
    return false
  end

  def self.generate_progression_pokemon(options = {})
    options ||= {}
    level = options[:level] ||
      (usable_party[0] ? usable_party[0].level : 5)
    level = [[level.to_i, 1].max, Settings::MAXIMUM_LEVEL].min
    species = options[:species]
    filter = options[:filter]
    fusion = options[:fusion]
    context = options[:context] || :required_progression

    if species
      species_data = GameData::Species.try_get(species)
      if !species_data || species_data.id_number <= 0 ||
         species_data.id_number >= Settings::ZAPMOLCUNO_NB
        raise PivotTransactionError,
              "The required progression species is invalid."
      end
      pokemon = Pokemon.new(species_data.id, level)
      if filter && !filter.call(pokemon)
        raise PivotTransactionError,
              "The required progression species does not meet the trade rule."
      end
      return mark_progression_trade_pokemon(pokemon)
    end

    pools = if fusion == true
              [custom_fusion_pool]
            elsif fusion == false
              [normal_species_pool]
            else
              [normal_species_pool, custom_fusion_pool]
            end
    pools.each_with_index do |pool, pool_index|
      next if !pool || pool.empty?
      start = progression_random_value(context, pool_index) % pool.length
      pool.length.times do |offset|
        candidate_species = pool[(start + offset) % pool.length]
        pokemon = Pokemon.new(candidate_species, level)
        next if filter && !filter.call(pokemon)
        return mark_progression_trade_pokemon(pokemon)
      end
    end
    raise PivotTransactionError,
          "No Pokemon satisfies the required progression rule."
  end

  def self.mark_progression_trade_pokemon(pokemon)
    pokemon.ironmon_party_exclusion = PROGRESSION_TRADE_EXCLUSION
    pokemon.obtain_method = 0
    pokemon.record_first_moves
    return pokemon
  end

  def self.prepare_progression_trade_pokemon(options = {})
    raise PivotTransactionError, "Player party data is unavailable." if
      !$Trainer || !$Trainer.party
    cleanup_progression_trade_pokemon
    pokemon = generate_progression_pokemon(options)
    if $Trainer.party.length >= Settings::MAX_PARTY_SIZE
      raise PivotTransactionError,
            "There is no temporary party space for the required Pokemon."
    end
    $Trainer.party << pokemon
    return pokemon
  end

  def self.cleanup_progression_trade_pokemon
    return if !$Trainer || !$Trainer.party
    $Trainer.party.delete_if do |pokemon|
      progression_trade_pokemon?(pokemon)
    end
  end

  def self.choose_wally_gift_pokemon(variable_number, name_variable_number)
    if !active?
      return pbChoosePokemon(
        variable_number, name_variable_number,
        proc { |pokemon| !pokemon.egg? }
      )
    end
    pokemon = prepare_progression_trade_pokemon({
      :fusion => false,
      :context => :wally_gift
    })
    index = $Trainer.party.index(pokemon)
    pbSet(variable_number, index)
    pbSet(name_variable_number, pokemon.name)
    return index
  end

  def self.progression_random_value(context, pool_index)
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    return fnv1a_64_joined([
      seed, PROGRESSION_POKEMON_NAMESPACE, context.inspect, pool_index
    ])
  end
end

alias ironmon_progression_original_choose_for_trade pbChoosePokemonForTrade
def pbChoosePokemonForTrade(variable_number, name_variable_number, wanted)
  return ironmon_progression_original_choose_for_trade(
    variable_number, name_variable_number, wanted
  ) if !Ironmon.active?
  pokemon = Ironmon.prepare_progression_trade_pokemon({
    :species => wanted,
    :context => [:species_trade, wanted]
  })
  index = $Trainer.party.index(pokemon)
  pbSet(variable_number, index)
  pbSet(name_variable_number, pokemon.name)
  return index
end

alias ironmon_progression_original_npc_trade npcTrade
def npcTrade(npc_species, nickname, trainer_name, player_pokemon_proc)
  return ironmon_progression_original_npc_trade(
    npc_species, nickname, trainer_name, player_pokemon_proc
  ) if !Ironmon.active?
  pokemon = Ironmon.prepare_progression_trade_pokemon({
    :filter => player_pokemon_proc,
    :context => [:npc_trade, npc_species, trainer_name]
  })
  index = $Trainer.party.index(pokemon)
  return pbStartTrade(index, npc_species, nickname, trainer_name, 0)
end
