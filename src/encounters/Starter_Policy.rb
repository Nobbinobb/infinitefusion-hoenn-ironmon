module Ironmon
  def self.separate_starter_policy?
    return active? && configuration.schema_version >= Configuration::SCHEMA_VERSION
  end

  def self.starter_species_generator(seed = nil, rules = nil, version = nil)
    rules ||= configuration
    seed ||= $PokemonGlobal.ironmon_seed
    version ||= $PokemonGlobal.ironmon_species_generator_version || SpeciesGenerator::SCHEMA_VERSION
    return SpeciesGenerator.new(seed, :starter, rules.starter_policy,
      normal_species_pool, custom_fusion_pool, {}, version)
  end

  def self.starter_offer_species(index)
    source = [1, 4, 7].fetch(index)
    return wild_species_for(source, [:starter, index]) if !separate_starter_policy?
    return starter_species_generator.map(source, [:starter, index])
  end

  def self.remember_starter_offers(pokemon)
    return if !separate_starter_policy? || pokemon.length != 3
    $PokemonGlobal.ironmon_starter_species = pokemon.map(&:species)
  end

  def self.rival_lab_choice_enabled?
    return separate_starter_policy? && rival_story_generation_enabled?
  end

  def self.rival_starter_slots
    chosen = pbGet(VAR_HOENN_CHOSEN_STARTER_INDEX).to_i
    return [(chosen + 1) % 3, (chosen + 2) % 3]
  end

  def self.rival_starter_offers
    stored = $PokemonGlobal.ironmon_starter_species
    return stored if stored.is_a?(Array) && stored.length == 3
    return (0..2).map { |index| starter_offer_species(index) }
  end

  def self.rival_opening_starter?(pokemon)
    return rival_lab_choice_enabled? && rival_story_starter?(pokemon) &&
      !$PokemonGlobal.ironmon_rival_lab_decision
  end

  def self.rival_species_bst(species)
    return GameData::Species.get(species).base_stats.values.inject(0, :+)
  end

  def self.rival_lab_decision(current_species, remaining_species, resolve_fusion = true)
    current = GameData::Species.get(current_species)
    remaining = GameData::Species.get(remaining_species)
    current_fused = current.id_number > NB_POKEMON
    remaining_fused = remaining.id_number > NB_POKEMON
    policy = configuration.trainer_policy
    action = :keep
    target = current.id
    if policy == Configuration::POLICY_NORMAL_ONLY
      if current_fused
        body = GameData::Species.get(current.get_body_species_symbol).id
        head = GameData::Species.get(current.get_head_species_symbol).id
        target = rival_species_bst(head) > rival_species_bst(body) ? head : body
        action = :unfuse
      elsif !remaining_fused && rival_species_bst(remaining.id) > rival_species_bst(current.id)
        action = :swap
        target = remaining.id
      end
    elsif !current_fused && !remaining_fused
      action = :fuse
      target = resolve_fusion ? player_fusion_mapper.species(current.id, remaining.id) : nil
    elsif remaining_fused || policy == Configuration::POLICY_MIXED
      action = :swap
      target = remaining.id
    end
    return { :action => action, :species => target, :initial_species => current.id,
      :remaining_species => remaining.id, :remaining_slot => rival_starter_slots[1] }
  end

  def self.pending_rival_lab_decision(resolve_fusion = true)
    stored = $PokemonGlobal.ironmon_rival_lab_decision
    return stored if stored
    trainer = $PokemonGlobal.battledTrainers[BATTLED_TRAINER_RIVAL_KEY]
    starter = rival_story_starter(trainer)
    raise SpeciesGenerationError, "the rival's opening starter is unavailable" if !starter
    return rival_lab_decision(starter.species, rival_starter_offers[rival_starter_slots[1]], resolve_fusion)
  end

  def self.apply_rival_lab_decision(decision)
    return $PokemonGlobal.ironmon_rival_lab_decision if $PokemonGlobal.ironmon_rival_lab_decision
    trainer = $PokemonGlobal.battledTrainers[BATTLED_TRAINER_RIVAL_KEY]
    starter = rival_story_starter(trainer)
    raise SpeciesGenerationError, "the rival's story starter is unavailable" if !starter
    slot = trainer.currentTeam.index(starter)
    if decision[:action] == :swap
      starter = Pokemon.new(decision[:species], starter.level, trainer.trainerName)
    elsif starter.species != decision[:species]
      starter.species = decision[:species]
      starter.pif_sprite = nil
      starter.reset_moves
      starter.calc_stats
    end
    trainer.currentTeam[slot] = mark_rival_story_starter(starter)
    $PokemonGlobal.ironmon_rival_lab_decision = decision
    return decision
  end
end
