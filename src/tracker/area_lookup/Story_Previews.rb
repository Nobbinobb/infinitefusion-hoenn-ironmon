module Ironmon
  def self.tracker_opening_rival_context(result)
    return nil if result.to_s != "lost" || !rival_lab_choice_enabled? ||
      !$Trainer || !$game_variables || !$game_switches || $game_switches[EARLY_GAME_SWITCH_FIRST_RIVAL_WON]
    offers = rival_starter_offers
    counter, remaining = rival_starter_slots
    decision = rival_lab_decision(offers[counter], offers[remaining], false)
    if decision[:action] == :fuse && player_fusion_pairing_ready?
      decision[:species] = player_fusion_mapper.preview_species(offers[counter], offers[remaining])
    end
    context = {
      "schema_version" => 1,
      "chosen_starter_index" => $game_variables[VAR_HOENN_CHOSEN_STARTER_INDEX].to_i,
      "trainer_gender" => $game_variables[VAR_TRAINER_GENDER].to_i,
      "rival_name" => $game_variables[VAR_RIVAL_NAME].to_s,
      "selected_difficulty" => $Trainer.selected_difficulty,
      "lowest_difficulty" => $Trainer.lowest_difficulty,
      "starter_species_ids" => offers.map(&:to_s)
    }
    context["lab_species_id"] = decision[:species].to_s if decision[:species]
    return context
  end

  def self.tracker_valid_story_species_id?(id)
    return false if !id.is_a?(String) || id.length > 64 || !/\A[A-Z][A-Z0-9_]*\z/.match?(id)
    fusion = /\AB(\d+)H(\d+)\z/.match(id)
    return fusion ? fusion.captures.all? { |part| (1..NB_POKEMON).include?(part.to_i) } :
      GameData::Species::DATA.key?(id.to_sym)
  end

  def self.tracker_validate_opening_rival_context(context)
    return nil if context.nil?
    valid = context.is_a?(Hash) && context["schema_version"] == 1 &&
      context["chosen_starter_index"].is_a?(Integer) &&
      (0..2).include?(context["chosen_starter_index"]) &&
      ["trainer_gender", "selected_difficulty", "lowest_difficulty"].all? do |key|
        context[key].is_a?(Integer) && (0..2).include?(context[key])
      end
    valid &&= context["rival_name"].is_a?(String) && context["rival_name"].length <= 128
    species = valid ? context["starter_species_ids"] : nil
    valid &&= species.is_a?(Array) && species.length == 3 && species.all? do |id|
      tracker_valid_story_species_id?(id)
    end
    valid &&= context["lab_species_id"].nil? || tracker_valid_story_species_id?(context["lab_species_id"])
    raise TrackerLookupError.new("invalid_recipe", "The opening rival context is invalid.") if !valid
    return context
  end

  # Only explicit archive inspection replays future teams. Defeat records the
  # three offers instead, so generated evolution preparation cannot delay reset.
  def self.tracker_archived_opening_rival_preview(recipe, entry)
    context = recipe["opening_rival_context"]
    return nil if !context || entry["battle_kind"] != "rival"
    tracker_validate_opening_rival_context(context)
    @tracker_archived_rival_preview_cache ||= {}
    key = [recipe["run_id"], recipe["seed"], recipe["generation_profile_id"],
      recipe["configuration"], recipe["data_mode"], context, entry["entry_id"]]
    return @tracker_archived_rival_preview_cache[key] if @tracker_archived_rival_preview_cache.key?(key)
    originals = [$PokemonGlobal, $Trainer, $game_variables, $game_switches]
    runtime = instance_variables.to_h { |name| [name, instance_variable_get(name)] }
    collection_suspended = GameplayCollection.capture_suspended
    begin
      GameplayCollection.capture_suspended = true
      $PokemonGlobal = PokemonGlobalMetadata.new
      trainer_type = nil
      GameData::TrainerType.each { |type| trainer_type = type.id; break }
      $Trainer = Player.new("Unnamed", trainer_type)
      $Trainer.selected_difficulty = context["selected_difficulty"]
      $Trainer.lowest_difficulty = context["lowest_difficulty"]
      $game_variables = Game_Variables.new
      $game_switches = Game_Switches.new
      $game_variables[VAR_HOENN_CHOSEN_STARTER_INDEX] = context["chosen_starter_index"]
      $game_variables[VAR_TRAINER_GENDER] = context["trainer_gender"]
      $game_variables[VAR_RIVAL_NAME] = context["rival_name"]
      $game_switches[SWITCH_GAME_DIFFICULTY_HARD] = true
      $PokemonGlobal.ironmon_mode = true
      $PokemonGlobal.ironmon_run_id = recipe["run_id"]
      $PokemonGlobal.ironmon_seed = recipe["seed"]
      $PokemonGlobal.ironmon_generation_profile_id = recipe["generation_profile_id"]
      $PokemonGlobal.ironmon_configuration = Configuration.from(recipe["configuration"])
      $PokemonGlobal.ironmon_species_generator_version = recipe["species_generator_version"]
      $PokemonGlobal.ironmon_starter_species = context["starter_species_ids"].map(&:to_sym)
      $PokemonGlobal.battledTrainers = {}
      @tracker_story_trainer_preview_cache = {}
      @pending_generated_evolutions, @committed_generated_evolutions = {}, {}
      reset_species_generator_cache
      reset_ability_generator_cache
      reset_base_stat_generator_cache
      reset_evolution_generator_cache
      reset_move_access_generator_cache
      @ability_randomization_ready = @base_stat_randomization_ready = true
      @evolution_randomization_ready = @move_access_randomization_ready = true
      if entry["story_stage"].to_i > 1
        counter, remaining = rival_starter_slots
        offers = rival_starter_offers
        decision = rival_lab_decision(offers[counter], offers[remaining], false)
        if decision[:action] == :fuse
          return nil if !context["lab_species_id"]
          decision[:species] = context["lab_species_id"].to_sym
        end
        $PokemonGlobal.ironmon_rival_lab_decision = decision
      end
      result = tracker_current_story_trainer_previews(entry["entry_id"])[entry["entry_id"]]
    ensure
      GameplayCollection.capture_suspended = collection_suspended
      $PokemonGlobal, $Trainer, $game_variables, $game_switches = originals
      (instance_variables - runtime.keys).each { |name| remove_instance_variable(name) }
      runtime.each { |name, value| instance_variable_set(name, value) }
    end
    tracker_store_bounded(@tracker_archived_rival_preview_cache, key, result, 8)
    return result
  end

  def self.tracker_story_recipe_matches_current_run?(recipe)
    return false if !$PokemonGlobal || !$Trainer ||
      recipe["run_id"] != $PokemonGlobal.ironmon_run_id ||
      recipe["seed"] != $PokemonGlobal.ironmon_seed ||
      recipe["generation_profile_id"] != $PokemonGlobal.ironmon_generation_profile_id
    ledger = $PokemonGlobal.ironmon_run_ledger
    attempt = ledger && ledger["current_attempt"]
    return false if !attempt || attempt["run_id"] != recipe["run_id"] ||
      attempt["seed"] != recipe["seed"] ||
      attempt["generation_profile_id"] != recipe["generation_profile_id"]
    return attempt["result"] == "active" if recipe["active_run"] == true
    number = recipe.dig("statistics", "attempt_number")
    return !!(number && number == attempt["attempt_number"])
  end

  def self.tracker_preview_story_party(trainer, rematch = false)
    copy = Marshal.load(Marshal.dump(trainer))
    context = [rematch ? :rematch : :custom, copy.trainerType, copy.trainerName]
    copy.currentTeam = trainer_battle_party(copy.currentTeam, context)
    expand_dynamic_boss_trainer_party(copy) if rematch
    npc = NPCTrainer.new(copy.trainerName, copy.trainerType)
    npc.party = copy.currentTeam
    if move_access_randomization_active?
      npc.party.each { |pokemon| pokemon.reset_moves if !pokemon.shadowPokemon? }
    end
    ensure_trainer_party_policy(npc)
    scale_trainer_party(npc)
    expand_boss_trainer_party(npc)
    result = tracker_story_party_snapshot(npc.party)
    result.each { |member| member["ability_id"] = "" }
    return result
  end

  def self.tracker_preview_wally_party
    wally = $PokemonGlobal.battledTrainers[BATTLED_TRAINER_WALLY_KEY]
    return nil if !wally
    if wally.currentTeam.length == 1
      wally_update_team_mauville
      wally = $PokemonGlobal.battledTrainers[BATTLED_TRAINER_WALLY_KEY]
    elsif wally.currentTeam.length != 2 || wally.currentTeam.map(&:level).sort != [16, 19]
      return nil
    end
    return tracker_preview_story_party(wally, true)
  rescue StandardError => error
    echoln "Ironmon Wally preview is unavailable: #{error.message}"
    return nil
  end

  def self.tracker_current_story_trainer_previews(entry_id = nil)
    return {} if !active? || !$PokemonGlobal || !$Trainer ||
      !$game_variables || !$game_switches || !rival_story_generation_enabled?
    requested = tracker_area_catalog.flat_map { |area| area["trainers"] }.
      find { |entry| entry["entry_id"] == entry_id } if entry_id
    return {} if entry_id && !requested
    rival_requested = !requested || requested["battle_kind"] == "rival"
    last_stage = requested ? requested["story_stage"].to_i : 3
    if rival_requested && last_stage > 1 && rival_lab_choice_enabled? && !$PokemonGlobal.ironmon_rival_lab_decision &&
       configuration.trainer_policy != Configuration::POLICY_NORMAL_ONLY &&
       !player_fusion_pairing_ready?
      offers = rival_starter_offers
      return {} if rival_starter_slots.all? { |slot| GameData::Species.get(offers[slot]).id_number <= NB_POKEMON }
    end
    originals = [$PokemonGlobal, $game_variables, $game_switches]
    teams = $PokemonGlobal.battledTrainers || {}
    signature = teams.map do |key, trainer|
      [key, trainer.currentTeam.map { |pokemon| [pokemon.species, pokemon.level, pokemon.personalID] }]
    end
    cache_key = [$PokemonGlobal.ironmon_run_id, $PokemonGlobal.ironmon_seed,
      active_generation_profile_id, signature,
      $game_variables[VAR_HOENN_CHOSEN_STARTER_INDEX], $game_variables[VAR_TRAINER_GENDER], configuration.trainer_policy,
      configuration.starter_policy, $PokemonGlobal.ironmon_starter_species, $PokemonGlobal.ironmon_rival_lab_decision, entry_id]
    @tracker_story_trainer_preview_cache ||= {}
    return @tracker_story_trainer_preview_cache[cache_key] if
      @tracker_story_trainer_preview_cache.key?(cache_key)
    post_lab_decision = $PokemonGlobal.ironmon_rival_lab_decision
    if rival_requested && last_stage > 1 && rival_lab_choice_enabled? && !post_lab_decision
      counter, remaining = rival_starter_slots
      offers = rival_starter_offers
      post_lab_decision = rival_lab_decision(offers[counter], offers[remaining], false)
      if post_lab_decision[:action] == :fuse
        post_lab_decision[:species] = player_fusion_mapper.preview_species(offers[counter], offers[remaining])
      end
    end
    original_generators = [@wild_species_generator, @trainer_species_generator]
    original_evolutions = [@pending_generated_evolutions, @committed_generated_evolutions]
    begin
      $PokemonGlobal, $game_variables, $game_switches = Marshal.load(Marshal.dump(originals))
      @wild_species_generator = @trainer_species_generator = nil
      @pending_generated_evolutions, @committed_generated_evolutions = {}, {}
      $PokemonGlobal.battledTrainers ||= {}
      stages = {}
      if rival_requested
        original_rival = $PokemonGlobal.battledTrainers[BATTLED_TRAINER_RIVAL_KEY]
        $PokemonGlobal.ironmon_rival_lab_decision = nil if rival_lab_choice_enabled?
        rival = initializeRivalBattledTrainer
        $PokemonGlobal.battledTrainers[BATTLED_TRAINER_RIVAL_KEY] = rival
        stages[1] = tracker_preview_story_party(rival)
        if last_stage > 1
          apply_rival_lab_decision(post_lab_decision) if rival_lab_choice_enabled?
          updateRivalTeamForSecondBattle
          stages[2] = tracker_preview_story_party($PokemonGlobal.battledTrainers[BATTLED_TRAINER_RIVAL_KEY])
        end
        if last_stage > 2
          if original_rival && original_rival.currentTeam.map(&:level).sort == [22, 22, 24]
            $PokemonGlobal.battledTrainers[BATTLED_TRAINER_RIVAL_KEY] = original_rival
          else
            updateRivalTeamForThirdBattle
          end
          stages[3] = tracker_preview_story_party($PokemonGlobal.battledTrainers[BATTLED_TRAINER_RIVAL_KEY])
        end
      end
      wally = tracker_preview_wally_party if !requested || requested["battle_kind"] == "wally"
      result = {}
      tracker_area_catalog.each do |area|
        area["trainers"].each do |entry|
          next if entry_id && entry["entry_id"] != entry_id
          if entry["battle_kind"] == "rival"
            result[entry["entry_id"]] = stages[entry["story_stage"]]
          elsif entry["battle_kind"] == "wally" && wally
            result[entry["entry_id"]] = wally
          end
        end
      end
      tracker_store_bounded(@tracker_story_trainer_preview_cache, cache_key, result, 8)
      return result
    rescue StandardError => error
      echoln "Ironmon story preview is unavailable: #{error.message}"
      return {}
    ensure
      $PokemonGlobal, $game_variables, $game_switches = originals
      @wild_species_generator, @trainer_species_generator = original_generators
      @pending_generated_evolutions, @committed_generated_evolutions = original_evolutions
    end
  end
end
