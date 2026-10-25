module IronmonArchivePokemonHistoryRuntimeTests
  def self.assert(condition, message)
    raise "Archive Pokemon history regression: #{message}" if !condition
  end

  def self.run
    collection = Ironmon::GameplayCollection
    journal = Object.new
    journal.define_singleton_method(:epoch_id) { "a" * 32 }
    journal.define_singleton_method(:stream_id) { "b" * 32 }
    collection.define_singleton_method(:journal) { journal }
    captured = []
    collection.define_singleton_method(:emit) { |type, payload, *_rest| captured << [type, payload] }
    journal.define_singleton_method(:sequence) { captured.length }
    collection.define_singleton_method(:enabled?) { true }
    collection.instance_variable_set(:@attempt_id, "c" * 32)
    collection.instance_variable_set(:@branch_id, "d" * 32)
    $PokemonSystem = PokemonSystem.new
    $PokemonSystem.alt_sprite_substitutions = {}
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    $PokemonTemp = PokemonTemp.new
    $game_system = Game_System.new
    $game_switches = Game_Switches.new
    $game_variables = Game_Variables.new
    $game_temp = Game_Temp.new
    $game_map = Game_Map.new
    $Trainer = Player.new("Archive history regression", GameData::TrainerType.keys.first)
    ownership = collection::Ownership
    ownership.instance_variable_set(:@records, {})
    ownership.instance_variable_set(:@revisions, {})
    ownership.start_history_branch(true)
    original_resolver = ownership.method(:archived_sprite_path)
    resolutions = 0
    ownership.define_singleton_method(:archived_sprite_path) do |pokemon|
      resolutions += 1
      original_resolver.call(pokemon)
    end
    records = []
    [[:PIKACHU, 16], [:RAICHU, 19], [:B117H339, 27]].each_with_index do |(species, level), index|
      pokemon = Pokemon.new(species, level, $Trainer, false)
      pokemon.pif_sprite = BattleSpriteLoader.new.get_pif_sprite_from_species(species)
      Ironmon.tracker_resolved_sprite_path(pokemon.pif_sprite)
      $Trainer.party = [pokemon]
      collection.pokemon_instance_id(pokemon)
      ownership.snapshot(pokemon)
      Kernel.srand(81281)
      ownership.snapshot(pokemon)
      expected_random = Array.new(8) { Kernel.rand(1_000_000) }
      Kernel.srand(81281)
      ownership.snapshot(pokemon)
      control_random = Array.new(8) { Kernel.rand(1_000_000) }
      assert(control_random == expected_random, "the test runtime can reset its gameplay random stream")
      Kernel.srand(81281)
      before = resolutions
      record = ownership.commit(pokemon, "acquisition", [index == 0 ? "starter" : "fuse"], { "source_kind" => index == 0 ? "starter" : "capture" }, true)
      assert(Array.new(8) { Kernel.rand(1_000_000) } == expected_random, "archive metadata adds no gameplay random draws beyond existing collection capture")
      identity = record["species_identity"]
      assert("#{identity['canonical_species_id']}:#{identity['form_id']}" == "#{species}:0", "retained lookup identity includes its form")
      assert(record["display_metadata"]["nickname"] == pokemon.name, "recorded name belongs to the actual retained Pokemon")
      sprite = record["display_metadata"]["sprite_path"]
      assert(sprite && File.file?(sprite), "recorded selected sprite is available for the archive")
      assert(resolutions == before + 1, "a committed configuration resolves its selected sprite once")
      duplicate = ownership.commit(pokemon)
      assert(duplicate.nil? && resolutions == before + 1, "unchanged ownership polling performs no sprite resolution")
      records << record
    end
    assert(captured.length == 3, "only retained meaningful configurations are emitted")
    substitutions = Marshal.dump($PokemonSystem.alt_sprite_substitutions)
    Kernel.srand(81881)
    expected = Array.new(8) { Kernel.rand(1_000_000) }
    Kernel.srand(81881)
    sprites = Ironmon.tracker_pokemon_sprites({ "species_ids" => ["PIKACHU:0", "RAICHU:0", "B117H339:0", "../../bad:0"] })["sprites"]
    assert(Array.new(8) { Kernel.rand(1_000_000) } == expected, "legacy artwork resolution does not consume gameplay randomness")
    assert(Marshal.dump($PokemonSystem.alt_sprite_substitutions) == substitutions, "legacy artwork does not change the current game's selected sprites")
    assert(sprites["PIKACHU:0"] && File.file?(sprites["PIKACHU:0"]), "legacy normal artwork resolves")
    assert(sprites["B117H339:0"] && File.file?(sprites["B117H339:0"]), "legacy fusion artwork resolves")
    assert(sprites["../../bad:0"].nil?, "invalid artwork identities are rejected")
    ownership.start_history_branch
    assert(ownership.instance_variable_get(:@history_parent).nil?, "an unsaved run starts a fresh branch")
    assert(ownership.instance_variable_get(:@history_ancestry_known) == false, "a legacy populated save does not invent an exact ancestor checkpoint")
    ownership.prepare_history_save
    saved = Marshal.load(Marshal.dump($PokemonGlobal.instance_variable_get(:@ironmon_collection_history_checkpoint)))
    assert(saved["branch_id"] == "d" * 32 && saved["sequence"].to_i > 0, "saving persists the precise current history cursor")
    collection.instance_variable_set(:@branch_id, "e" * 32)
    ownership.start_history_branch
    assert(ownership.instance_variable_get(:@history_parent) == saved, "reloading resumes the saved history parent")
    ownership.commit($Trainer.party[0], "terminal", ["loss"], nil, true)
    assert(captured.last[1]["history_context"]["saved_parent"] == saved, "future ownership records carry the saved ancestry")
    hook = Ironmon.instance_variable_get(:@game_save_hooks).find { |entry| entry[:name] == :gameplay_collection }
    save_state = hook[:before].call("ArchiveFixture", false, true)
    prepared = Marshal.load(Marshal.dump($PokemonGlobal.instance_variable_get(:@ironmon_collection_history_checkpoint)))
    assert(prepared["branch_id"] == "e" * 32, "the actual save hook prepares the new cursor before serialization")
    hook[:after].call("ArchiveFixture", false, true, false, save_state)
    assert($PokemonGlobal.instance_variable_get(:@ironmon_collection_history_checkpoint) == saved, "a failed save restores the previous history checkpoint")
    collection.instance_variable_set(:@attempt_id, "f" * 32)
    ownership.start_history_branch
    assert(ownership.instance_variable_get(:@history_parent).nil?, "a different attempt cannot inherit another run's history")
    File.binwrite($ironmon_archive_history_fixture, Ironmon.tracker_json_generate({ "game_root" => Dir.pwd, "records" => records }))
    File.binwrite($ironmon_archive_history_result, "archive Pokemon history runtime tests passed\n")
  ensure
    ownership.define_singleton_method(:archived_sprite_path, original_resolver) if ownership && original_resolver
  end
end

IronmonArchivePokemonHistoryRuntimeTests.run
