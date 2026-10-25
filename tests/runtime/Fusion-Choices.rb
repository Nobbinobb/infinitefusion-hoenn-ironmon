module IronmonFusionChoiceTestInput
  def self.start(frames)
    @frames = frames
    @index = -1
  end

  def self.stop
    @frames = nil
  end

  def self.next_frame
    return if !@frames
    @index += 1
    raise "Native fusion input did not complete" if @index >= @frames.length
  end

  def self.pressed?(key)
    return nil if !@frames
    return @index >= 0 && @frames[@index].include?(key)
  end

  def update
    super
    IronmonFusionChoiceTestInput.next_frame
  end

  def trigger?(key)
    value = IronmonFusionChoiceTestInput.pressed?(key)
    return value.nil? ? super : value
  end

  def repeat?(key)
    value = IronmonFusionChoiceTestInput.pressed?(key)
    return value.nil? ? super : value
  end
end

Input.singleton_class.prepend(IronmonFusionChoiceTestInput)

module IronmonFusionChoiceRuntimeTests
  def self.assert(condition, message)
    raise "Fusion choice runtime test failed: #{message}" if !condition
  end

  def self.stat_hash(values)
    return Hash[Ironmon::BaseStatGenerator::STAT_ORDER.zip(values)]
  end

  def self.run
    previous_global = $PokemonGlobal
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    rows = JSON.parse(File.binread($ironmon_fusion_fixture_path))
    rows.each do |row|
      row = row.each_with_object({}) { |(key, value), result| result[key.to_s] = value }
      $PokemonGlobal.ironmon_seed = row["seed"]
      Ironmon.reset_base_stat_generator_cache
      assert(Ironmon.prepare_base_stat_randomization, "base stats prepare")
      body = stat_hash(row["bodyStats"])
      head = stat_hash(row["headStats"])
      legacy = Ironmon::BaseStatGenerator.new(row["seed"], nil, 1)
      current = Ironmon::BaseStatGenerator.new(row["seed"], nil, 2)
      expected = stat_hash(row["stats"])
      assert(current.fuse(body, head, row["resultId"]) == expected,
        "fixed inheritance matches the separately validated simulation")
      assert(legacy.fuse(body, head, row["resultId"]) == legacy.fuse(body, head),
        "historical profiles retain native blending")
      species = GameData::Species.get(row["resultId"])
      assert(current.fusion_stats_for(species) == expected,
        "displayed components determine the complete fusion vector")
      [species.body_pokemon, species.head_pokemon].each do |component|
        assert(current.stats_for(component) == legacy.stats_for(component),
          "normal vectors are unchanged by the fusion formula")
      end
      pokemon = Pokemon.new(row["resultId"], 30)
      pokemon.hp = 1
      clue = Ironmon.fusion_stat_clue(pokemon)
      stat = Ironmon::BaseStatGenerator::STAT_ORDER[row["reveal"]]
      assert(clue[:stat] == stat,
        "the fixed reveal matches the independently validated seed choice")
      stat_method = [:totalhp, :attack, :defense, :spatk, :spdef, :speed][row["reveal"]]
      assert(clue[:value] == pokemon.public_send(stat_method),
        "the revealed value is the current calculated stat, with maximum HP")
      assert(clue[:label] == Ironmon::FUSION_SPREAD_LABELS[row["label"]],
        "spread label matches the calibrated reference")
      pokemon.nature = :ADAMANT
      pokemon.ability_index = 1
      pokemon.level = 80
      pokemon.calc_stats
      updated_clue = Ironmon.fusion_stat_clue(pokemon)
      assert(updated_clue[:stat] == clue[:stat] && updated_clue[:label] == clue[:label],
        "level, nature, ability and repeated inspection cannot reroll the revealed stat or spread")
      assert(updated_clue[:value] == pokemon.public_send(stat_method),
        "the revealed amount follows level and nature changes")
      assert(Ironmon.fusion_stat_clue(pokemon.clone) == updated_clue,
        "reconstructing the result retains the same clue")
    end
    assert(Ironmon.base_stat_source_fingerprint(1) !=
      Ironmon.base_stat_source_fingerprint(2), "formula fingerprints differ")
    test_boundaries
    test_concealed_panel
    assert(Ironmon.prepare_ability_randomization, "abilities prepare")
    test_choice_flow
    test_nature_inheritance
    test_native_move_screen
    test_historical_profile
  ensure
    $PokemonGlobal = previous_global
    Ironmon.suspend_base_stat_randomization
    Ironmon.suspend_ability_randomization
  end

  def self.test_choice_flow
    first = Pokemon.new(75744, 30)
    second = Pokemon.new(:GALLADE, 18)
    first.personalID = 21
    second.personalID = 42
    first.nature = :ADAMANT
    second.nature = :MODEST
    first.ability_index = 1
    GameData::Stat.each_main do |stat|
      first.iv[stat.id] = 31
      second.iv[stat.id] = 0
    end
    first.moves = [:TACKLE, :WATERGUN, :GROWL, :CONFUSION].map { |id| Pokemon::Move.new(id) }
    second.moves = [:SLASH, :LEER, :PSYBEAM, :SWORDSDANCE].map { |id| Pokemon::Move.new(id) }
    expected_nature = Ironmon.inherited_fusion_nature(first, second)
    expected_ability = first.ability_id
    noninteractive = first.clone
    automatic = first.clone
    automatic.moves = first.moves.first(1)
    automatic_head = second.clone
    automatic_head.moves = second.moves.first(1)
    seen_clue = nil
    screen_count = 0
    screen_class = PokemonOptionScreen
    original_run = screen_class.instance_method(:pbStartScreen)
    screen_class.send(:define_method, :pbStartScreen) do |*args|
      IronmonFusionChoiceRuntimeTests.assert(@scene.is_a?(FusionMovesOptionsScene),
        "fusion must not open an ability or nature selection screen")
      screen_count += 1
      seen_clue = Ironmon.fusion_stat_clue(@scene.instance_variable_get(:@fused_pokemon))
      IronmonFusionChoiceRuntimeTests.script_scene_input(@scene, [[Input::USE], [Input::USE]])
      original_run.bind(self).call(*args)
    end
    result = Ironmon.apply_normal_fusion_instance_data(first, second, true)
    assert(result.nature.id == expected_nature, "nature is randomly inherited before move selection")
    assert(result.ability_index == 1 && result.ability_id == expected_ability,
      "the existing generated ability assignment is preserved")
    assert(result.level == 26 && result.iv.values.all? { |value| value == 15 },
      "the displayed fusion uses its final level and averaged IVs")
    assert(result.moves.map(&:id) == second.moves.map(&:id),
      "ordinary move inheritance survives the concealed choice phase")
    assert(Ironmon.fusion_stat_clue(result) == seen_clue,
      "the current stat shown before move selection matches the finished fusion")
    Ironmon.apply_normal_fusion_instance_data(noninteractive, second, false)
    assert(noninteractive.nature.id == expected_nature && noninteractive.ability_id == expected_ability,
      "noninteractive fusions use the same nature and ability assignments")
    Ironmon.apply_normal_fusion_instance_data(automatic, automatic_head, true)
    assert(automatic.nature.id == expected_nature && screen_count == 1,
      "automatically combining a small move pool opens no additional screen")
  ensure
    screen_class.send(:define_method, :pbStartScreen, original_run) if original_run
    IronmonFusionChoiceTestInput.stop
  end

  def self.test_nature_inheritance
    first = Pokemon.new(75744, 30)
    second = Pokemon.new(:GALLADE, 30)
    first.nature = :ADAMANT
    second.nature = :MODEST
    seen = []
    64.times do |index|
      first.personalID = index
      nature = Ironmon.inherited_fusion_nature(first, second)
      assert([:ADAMANT, :MODEST].include?(nature), "nature always comes from one consumed material")
      assert(Ironmon.inherited_fusion_nature(first.clone, second.clone) == nature,
        "reconstructing the same fusion cannot reroll nature")
      seen << nature
    end
    assert(seen.uniq.sort == [:ADAMANT, :MODEST].sort, "either material can supply the inherited nature")
    second.nature = :ADAMANT
    assert(Ironmon.inherited_fusion_nature(first, second) == :ADAMANT,
      "identical material natures are retained")
  end

  def self.script_scene_input(scene, frames)
    original_options = scene.method(:pbOptions)
    scene.define_singleton_method(:pbOptions) do
      IronmonFusionChoiceTestInput.start(frames)
      begin
        original_options.call
      ensure
        IronmonFusionChoiceTestInput.stop
      end
    end
  end

  def self.test_native_move_screen
    first = Pokemon.new(75744, 30)
    second = Pokemon.new(:GALLADE, 30)
    first.nature = :ADAMANT
    second.nature = :MODEST
    first.moves = [:TACKLE, :WATERGUN, :GROWL, :CONFUSION].map { |id| Pokemon::Move.new(id) }
    second.moves = [:TACKLE, :LEER, :SLASH, :SWORDSDANCE].map { |id| Pokemon::Move.new(id) }
    clue = Ironmon.fusion_stat_clue(first)
    revealed_method = Ironmon::FUSION_STAT_METHODS.fetch(clue[:stat])
    ([:totalhp, :attack, :defense, :spatk, :spdef, :speed] - [revealed_method]).each do |method|
      first.define_singleton_method(method) { raise "concealed stat accessed" }
    end
    scene = FusionMovesOptionsScene.new(first, second)
    scene.pbStartScene
    sprites = scene.instance_variable_get(:@sprites)
    assert(sprites["textbox"].text.include?(clue[:label]), "the native description includes the spread")
    sprites["option"].index = 1
    scene.updateDescription(1)
    assert(sprites["bg_moves"].visible, "native move details remain available")
    sprites["option"].index = 0
    sprites["option"][0] = 1
    scene.updateDescription(0)
    assert(sprites["textbox"].text.include?(clue[:label]), "returning to stats preserves the clue")
    scene.pbEndScene
    scene = nil
    assert(Ironmon.fusion_stat_clue(first) == clue, "native menus cannot reroll the clue")
  ensure
    scene.pbEndScene if scene
  end

  def self.test_historical_profile
    profile = Ironmon.current_generation_profile
    manifest = Marshal.load(Marshal.dump(profile["manifest"]))
    manifest["algorithms"].find { |entry| entry["name"] == "base_stats" }["version"] = 1
    legacy_id = Ironmon::GenerationProfile.fingerprint(manifest)
    profiles = Ironmon.instance_variable_get(:@generation_profiles)
    Ironmon.instance_variable_set(:@generation_profiles, (profiles || {}).merge(
      legacy_id => { "profile_id" => legacy_id, "manifest" => manifest,
        "root" => profile["root"] }.freeze))
    current_fingerprint = Ironmon.base_stat_source_fingerprint
    species = GameData::Species.get(75744)
    current_normal = Ironmon.base_stat_generator.stats_for(species.body_pokemon)
    Ironmon.with_generation_profile(legacy_id) do
      assert(Ironmon.base_stat_formula_version == 1,
        "historical profiles select historical stat inheritance")
      assert(Ironmon.base_stat_source_fingerprint != current_fingerprint,
        "historical metadata cannot be accepted as current formula metadata")
      generator = Ironmon.base_stat_generator
      historical = generator.fuse(generator.stats_for(species.body_pokemon),
        generator.stats_for(species.head_pokemon))
      assert(generator.fusion_stats_for(species) == historical,
        "historical gameplay reproduces the complete original fusion vector")
      assert(generator.stats_for(species.body_pokemon) == current_normal,
        "profile activation preserves normal stat vectors")
    end
    assert(Ironmon.base_stat_formula_version == 2 &&
      Ironmon.base_stat_source_fingerprint == current_fingerprint,
      "leaving an archived lookup restores current formula metadata")
  ensure
    Ironmon.instance_variable_set(:@generation_profiles, profiles)
  end

  def self.test_boundaries
    [20, 35, 50, 65].each_with_index do |cutoff, index|
      [cutoff - 1, cutoff, cutoff + 1].each do |spread|
        values = [100 - spread] * 3 + [100 + spread] * 3
        expected = spread < cutoff ? index : index + 1
        assert(Ironmon.fusion_spread_label(stat_hash(values)) ==
          Ironmon::FUSION_SPREAD_LABELS[expected], "inclusive CV boundary #{cutoff}")
        assert(Ironmon.fusion_spread_label(stat_hash(values.reverse.map { |v| v * 2 })) ==
          Ironmon::FUSION_SPREAD_LABELS[expected], "permutation and scale invariance")
      end
    end
  end

  def self.test_concealed_panel
    pokemon = Pokemon.new(75744, 30)
    clue = Ironmon.fusion_stat_clue(pokemon)
    revealed_method = Ironmon::FUSION_STAT_METHODS.fetch(clue[:stat])
    ([:totalhp, :attack, :defense, :spatk, :spdef, :speed] - [revealed_method]).each do |method|
      pokemon.define_singleton_method(method) { raise "concealed stat accessed" }
    end
    scene = FusionMovesOptionsScene.allocate
    overlay = Sprite.new
    overlay.bitmap = Bitmap.new(Graphics.width, Graphics.height)
    pbSetSystemFont(overlay.bitmap)
    sprites = { "overlay" => overlay, "bg_stats" => Sprite.new,
      "bg_moves" => Sprite.new, "title" => Struct.new(:text).new("") }
    scene.instance_variable_set(:@sprites, sprites)
    scene.instance_variable_set(:@fused_pokemon, pokemon)
    captured = nil
    scene.define_singleton_method(:pbDrawTextPositions) do |_bitmap, positions|
      captured = positions
    end
    scene.draw_pokemon_info
    texts = captured.map { |position| position[0] }
    assert(texts.grep(/\A\d+\z/) == [clue[:value].to_s],
      "the panel contains exactly one numeric current stat")
    assert(texts.count("?") == 5 && captured.any? { |position|
      position[0] == clue[:value].to_s &&
        position[2] == 78 + Ironmon::BaseStatGenerator::STAT_ORDER.index(clue[:stat]) * 32
    },
      "the original six-stat panel hides exactly five values")
    assert(captured.all? { |position| position[1] == 20 || position[1] == 158 },
      "the stat panel retains the original native columns")
  ensure
    bitmap = overlay.bitmap if overlay
    sprites.each_value { |sprite| sprite.dispose if sprite.respond_to?(:dispose) } if sprites
    bitmap.dispose if bitmap && !bitmap.disposed?
  end
end

IronmonFusionChoiceRuntimeTests.run
