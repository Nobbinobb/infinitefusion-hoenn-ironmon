module Ironmon
  def self.story_trainer_parties
    store = $PokemonGlobal && $PokemonGlobal.ironmon_story_trainer_parties
    return {} if !store.is_a?(Hash) || store["run_id"] != $PokemonGlobal.ironmon_run_id
    return store["parties"] || {}
  end

  def self.record_story_trainer_party(entry_id, party)
    return if !active? || !entry_id || !party
    store = $PokemonGlobal.ironmon_story_trainer_parties
    if !store.is_a?(Hash) || store["run_id"] != $PokemonGlobal.ironmon_run_id
      store = { "run_id" => $PokemonGlobal.ironmon_run_id, "parties" => {} }
      $PokemonGlobal.ironmon_story_trainer_parties = store
    end
    store["parties"][entry_id] ||= party
  end

  def self.tracker_story_party_snapshot(party)
    return party.first(Settings::MAX_PARTY_SIZE).map do |pokemon|
      {
        "species_id" => "#{pokemon.species_data.id}:0",
        "level" => pokemon.level, "ability_id" => pokemon.ability_id.to_s,
        "move_ids" => pokemon.moves.map { |move| move.id.to_s }
      }
    end
  end

  def self.capture_area_story_trainer_party(trainer)
    ids = (@pending_area_trainer_entries || []) + [@current_area_trainer_entry]
    return if !active? || ids.compact.empty?
    entry = tracker_area_catalog.flat_map { |area| area["trainers"] }.
      find do |candidate|
        ids.include?(candidate["entry_id"]) && candidate["battle_kind"] &&
          candidate["trainer_type"] == trainer.trainer_type.to_s &&
          (candidate["trainer_name"] == trainer.name || candidate["battle_kind"] == "rival")
      end
    return if !entry
    @pending_area_story_trainer_parties ||= {}
    @pending_area_story_trainer_parties[entry["entry_id"]] = tracker_story_party_snapshot(trainer.party)
  end

  def self.tracker_live_wally_party(entry)
    return nil if !$PokemonGlobal || !$game_self_switches ||
      !$game_self_switches[[entry["map_id"], entry["event_id"], "A"]]
    trainers = $PokemonGlobal.battledTrainers
    wally = trainers && trainers[BATTLED_TRAINER_WALLY_KEY]
    return nil if !wally || wally.currentTeam.length != 2 ||
      wally.currentTeam.map(&:level).sort != [16, 19]
    copy = Marshal.load(Marshal.dump(wally))
    copy.currentTeam = trainer_battle_party(
      copy.currentTeam, [:rematch, copy.trainerType, copy.trainerName]
    )
    expand_dynamic_boss_trainer_party(copy)
    trainer = NPCTrainer.new(copy.trainerName, copy.trainerType)
    trainer.party = copy.currentTeam
    if move_access_randomization_active?
      trainer.party.each { |pokemon| pokemon.reset_moves if !pokemon.shadowPokemon? }
    end
    ensure_trainer_party_policy(trainer)
    scale_trainer_party(trainer)
    expand_boss_trainer_party(trainer)
    snapshot = tracker_story_party_snapshot(trainer.party)
    snapshot.each { |pokemon| pokemon["ability_id"] = "" }
    return snapshot
  end

  def self.tracker_story_trainer_recipe
    tracker_area_catalog.each do |area|
      area["trainers"].each do |entry|
        next if entry["battle_kind"] != "wally" ||
          story_trainer_parties.key?(entry["entry_id"]) ||
          !tracker_area_event_completed?(entry)
        record_story_trainer_party(entry["entry_id"], tracker_live_wally_party(entry))
      end
    end
    return Marshal.load(Marshal.dump(story_trainer_parties))
  end

  def self.tracker_area_story_trainer_entry(entry, recipe, discoveries, full_details,
                                            archived, abilities_revealed, moves_revealed)
    parties = archived ? (recipe["story_trainer_parties"] || {}) : story_trainer_parties
    snapshot = parties[entry["entry_id"]]
    defeated = !!snapshot || discoveries.key?(entry["entry_id"]) ||
      (!archived && tracker_area_event_completed?(entry))
    if !snapshot && !archived && entry["battle_kind"] == "wally" && (defeated || full_details)
      snapshot = tracker_live_wally_party(entry)
      record_story_trainer_party(entry["entry_id"], snapshot) if defeated
    end
    if !snapshot && full_details
      previews = recipe["story_trainer_previews"] || {}
      previews = tracker_current_story_trainer_previews(entry["entry_id"]) if !previews[entry["entry_id"]] &&
        tracker_story_recipe_matches_current_run?(recipe)
      snapshot = previews[entry["entry_id"]]
      snapshot ||= tracker_archived_opening_rival_preview(recipe, entry) if archived
    end
    version = generation_profile_algorithm_version("gym_party_expansion")
    trainer = Struct.new(:trainer_type).new(entry["trainer_type"].to_sym)
    source = tracker_area_trainer_index(recipe)[[entry["trainer_type"], entry["trainer_name"], entry["party_id"]]]
    size = entry["base_party_size"] || (source ? source.pokemon.length : 0)
    size = trainer_party_expansion_target(trainer, size, version) || size
    if entry["battle_kind"] == "wally"
      size = trainer_party_expansion_target(trainer, size, version) || size
    end
    revealed = !!snapshot && (full_details || defeated)
    trainer_type = GameData::TrainerType.get(entry["trainer_type"].to_sym)
    return {
      "entry_id" => entry["entry_id"], "map_id" => entry["map_id"],
      "trainer_type" => trainer_type.name, "trainer_name" => entry["trainer_name"],
      "party_size" => snapshot ? snapshot.length : size,
      "defeated" => defeated, "details_revealed" => revealed,
      "party" => revealed ? tracker_area_story_trainer_members(
        snapshot, abilities_revealed, moves_revealed, recipe
      ) : []
    }
  end

  def self.tracker_area_story_trainer_members(snapshot, abilities_revealed, moves_revealed, recipe)
    return snapshot.each_with_index.map do |pokemon, slot|
      species = GameData::Species.get(pokemon["species_id"].split(":")[0].to_sym)
      member = {
        "slot" => slot + 1, "species_id" => pokemon["species_id"],
        "species_name" => species.name, "level" => pokemon["level"],
        "sprite_path" => tracker_lookup_sprite_path(species),
        "abilities_revealed" => abilities_revealed, "moves_revealed" => moves_revealed
      }
      if abilities_revealed
        ability = GameData::Ability.try_get(pokemon["ability_id"].to_sym)
        member["abilities"] = ability ? [tracker_ability_snapshot(ability)] :
          tracker_area_trainer_abilities(species, {}, recipe)
      end
      if moves_revealed
        member["moves"] = pokemon["move_ids"].map do |id|
          move = GameData::Move.get(id.to_sym)
          { "id" => move.id.to_s, "name" => move.name, "type" => move.type.to_s,
            "category" => tracker_move_category(move), "description" => move.description }
        end
      end
      member
    end
  end
end
