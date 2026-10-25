module IronmonStarterRivalTests
  def self.assert(condition, message)
    raise "Starter/rival acceptance failed: #{message}" if !condition
    @assertions = (@assertions || 0) + 1
  end

  def self.stub(owner, name, implementation)
    previous = owner.method(name)
    owner.define_singleton_method(name, &implementation)
    yield
  ensure
    owner.define_singleton_method(name, previous) if previous
  end

  def self.rules(trainer = :mixed, starter = :mixed, wild = :mixed)
    $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new(wild, trainer,
      :random_component, false, starter)
  end

  def self.test_configuration
    rules(:custom_fusions_only, :normal_only, :mixed)
    copy = Ironmon::Configuration.from(Ironmon.configuration.to_h)
    assert(copy.starter_policy == :normal_only && copy.trainer_policy == :custom_fusions_only,
      "separate starter rules survive serialization independently")
    legacy = Ironmon::Configuration.from({ "schema_version" => 1, "wild_policy" => "normal_only",
      "trainer_policy" => "mixed", "unfusion_setting" => "random_component", "automatic_reset" => false })
    assert(legacy.current? && legacy.starter_policy == :normal_only && !legacy.to_h.key?(:starter_policy),
      "legacy rules retain their exact serialized shape and wild-controlled starters")
    $PokemonGlobal.ironmon_configuration = legacy
    assert(!Ironmon.rival_lab_choice_enabled?, "legacy runs retain their original rival progression")
    rules
    valid = Ironmon.configuration.to_h.each_with_object({}) { |(key, value), hash| hash[key.to_s] = value.is_a?(Symbol) ? value.to_s : value }
    assert(Ironmon.validate_seed_import_configuration(valid).nil?, "seed imports accept the new starter rule")
    valid.delete("starter_policy")
    assert(!Ironmon.validate_seed_import_configuration(valid).nil?, "new seed tokens cannot silently lose starter rules")
  end

  def self.test_starter_policies
    [:mixed, :normal_only, :custom_fusions_only].each do |policy|
      [:normal_only, :custom_fusions_only, :mixed].each do |trainer|
        rules(trainer, policy, :normal_only)
        (0..2).each do |slot|
          species = Ironmon.starter_offer_species(slot)
          number = GameData::Species.get(species).id_number
          assert(species == Ironmon.starter_offer_species(slot), "starter #{slot} is reproducible")
          assert(number <= NB_POKEMON, "normal-only starter stays normal") if policy == :normal_only
          assert(Ironmon.custom_fusion_species?(species), "fusion-only starter has an eligible custom sprite") if policy == :custom_fusions_only
        end
      end
    end
    rules(:mixed, :mixed, :normal_only)
    expected = (0..2).map { |slot| Ironmon.starter_offer_species(slot) }
    rules(:custom_fusions_only, :mixed, :custom_fusions_only)
    assert(expected == (0..2).map { |slot| Ironmon.starter_offer_species(slot) },
      "wild and trainer settings do not change the three starter offers")
    $PokemonGlobal.ironmon_starter_species = [:BULBASAUR, :CHARMANDER, :SQUIRTLE]
    (0..2).each do |selection|
      pbSet(VAR_HOENN_CHOSEN_STARTER_INDEX, selection)
      assert(Ironmon.rival_story_initial_species == $PokemonGlobal.ironmon_starter_species[(selection + 1) % 3],
        "rival takes the actual original counter slot for player choice #{selection}")
    end
    pbSet(VAR_HOENN_CHOSEN_STARTER_INDEX, 0)
  end

  def self.matrix_cases
    return [
      [:mixed, :BULBASAUR, :CHARMANDER, :fuse, :B1H4],
      [:mixed, :BULBASAUR, :B7H1, :swap, :B7H1],
      [:mixed, :B1H4, :SQUIRTLE, :swap, :SQUIRTLE],
      [:mixed, :B1H4, :B7H1, :swap, :B7H1],
      [:custom_fusions_only, :BULBASAUR, :CHARMANDER, :fuse, :B1H4],
      [:custom_fusions_only, :BULBASAUR, :B7H1, :swap, :B7H1],
      [:custom_fusions_only, :B1H4, :SQUIRTLE, :keep, :B1H4],
      [:custom_fusions_only, :B1H4, :B7H1, :swap, :B7H1],
      [:normal_only, :MAGIKARP, :MEWTWO, :swap, :MEWTWO],
      [:normal_only, :MEWTWO, :MAGIKARP, :keep, :MEWTWO],
      [:normal_only, :BULBASAUR, :B7H1, :keep, :BULBASAUR],
      [:normal_only, :B1H4, :SQUIRTLE, :unfuse, :BULBASAUR],
      [:normal_only, :B1H4, :B7H1, :unfuse, :BULBASAUR]
    ]
  end

  def self.test_matrix
    mapper = Object.new
    mapper.define_singleton_method(:species) { |_body, _head| :B1H4 }
      mapper.define_singleton_method(:species_number) { |_body, _head| NB_POKEMON + 4 }
    stub(Ironmon, :player_fusion_mapper, proc { mapper }) do
      matrix_cases.each do |policy, current, remaining, action, species|
        rules(policy)
        decision = Ironmon.rival_lab_decision(current, remaining)
        assert(decision[:action] == action && decision[:species] == species,
          "#{policy}: #{current} + #{remaining} resolves to #{action} #{species}")
      end
    end
    rules(:normal_only)
    assert(Ironmon.rival_lab_decision(:BULBASAUR, :BULBASAUR)[:action] == :keep,
      "equal BST keeps the selected normal")
    data = GameData::Species.get(:MAGIKARP)
    stub(data, :base_stats, proc { { :HP => 999 } }) do
      assert(Ironmon.rival_lab_decision(:MEWTWO, :MAGIKARP)[:species] == :MAGIKARP,
        "rival compares the run's displayed base stats rather than canonical BST")
    end
  end

  def self.test_opening_exception
    [[:normal_only, :B1H4], [:custom_fusions_only, :BULBASAUR]].each do |policy, species|
      rules(policy)
      $PokemonGlobal.ironmon_rival_lab_decision = nil
      starter = Ironmon.mark_rival_story_starter(Pokemon.new(species, 5))
      trainer = NPCTrainer.new("Rival", :RIVAL1)
      trainer.party = Ironmon.trainer_battle_party([starter])
      Ironmon.ensure_trainer_party_policy(trainer)
      assert(trainer.party[0].species == species, "opening starter survives conflicting #{policy} trainer rules")
      assert(Ironmon.trainer_maturity_exception?(trainer.party[0]), "story partner retains generated evolution")
    end
  end

  def self.setup_lab
    $scene.dispose if $scene && $scene.respond_to?(:map_renderer) && $scene.map_renderer
    rules
    $PokemonGlobal.ironmon_rival_lab_decision = nil
    $PokemonGlobal.ironmon_starter_species = [:SQUIRTLE, :BULBASAUR, :CHARMANDER]
    pbSet(VAR_HOENN_CHOSEN_STARTER_INDEX, 0)
    $game_switches[Ironmon::EARLY_GAME_SWITCH_STARTER_RECEIVED] = false
    $game_switches[Ironmon::EARLY_GAME_SWITCH_RIVAL_DEFEATED] = false
    $game_switches[Ironmon::EARLY_GAME_SWITCH_LAB_RETURN] = false
    $game_switches[Ironmon::EARLY_GAME_SWITCH_OPENING_COMPLETE] = false
    $game_switches[Ironmon::EARLY_GAME_SWITCH_FIRST_RIVAL_WON] = false
    $game_switches[SWITCH_RIVAL_BATTLE_2] = false
    authored = load_data("Data/Map018.rxdata")
    condition = authored.events[Ironmon::EARLY_GAME_LAB_PROFESSOR_EVENT_ID].pages[0].condition
    $game_switches[condition.switch1_id] = true if condition.switch1_valid
    $game_switches[condition.switch2_id] = true if condition.switch2_valid
    $MapFactory = PokemonMapFactory.new(Ironmon::EARLY_GAME_LAB_MAP_ID)
    $game_map.events.each_value do |event|
      event.list.replace([RPG::EventCommand.new(0, 0, [])]) if event.list
      event.through = true if event.character_name.empty?
      event.instance_variable_set(:@move_type, 0)
    end
    $game_player.moveto(9, 8)
    $game_player.character_name = "walk"
    $game_player.through = false
    $game_player.transparent = false
    $game_player.instance_variable_set(:@opacity, 255)
    $scene = Scene_Map.new
    $scene.createSpritesets
    setupStartingOutfit
    rival = $game_map.events[Ironmon::EARLY_GAME_LAB_RIVAL_EVENT_ID]
    appearance = Ironmon.normalized_early_game_rival_appearance(GENDER_FEMALE, nil)
    rival.character_name = Ironmon.materialize_early_game_rival_character(GENDER_FEMALE, appearance)
    rival.moveto(*Ironmon::RIVAL_LAB_EXIT)
    rival.opacity = 255
    rival.direction_fix = false
    rival.through = false
    assert(Marshal.dump($game_map.data) == Marshal.dump(authored.data), "the lab retains its original furniture and tiles")
    return rival
  end

  def self.advance_frames(count)
    count.times do
      $game_map.events.each_value(&:update)
      $scene.spriteset.update
      $scene.spritesetGlobal.update
      Graphics.update
      @frames = (@frames || 0) + 1
      raise "Movement timed out" if @frames > 20_000
    end
  end

  def self.capture(name)
    advance_frames(2)
    bitmap = Graphics.snap_to_bitmap
    bitmap.save_to_png(File.join(@output, "#{name}.png"))
    bitmap.dispose
  end

  def self.test_native_movement
    rival = setup_lab
    capture("01-rival-enters")
    trace = []
    { :move_down => 2, :move_left => 4, :move_right => 6, :move_up => 8 }.each do |name, direction|
      original = rival.method(name)
      rival.define_singleton_method(name) do |*args|
        IronmonStarterRivalTests.assert(self.direction == direction,
          "rival faces #{direction} before stepping from #{[x,y].inspect}")
        trace << [x, y, direction]
        original.call(*args)
      end
    end
    stubs = { :pbMessage => proc { |*_args| 0 }, :pbCallBub => proc { |*_args| },
      :pbWait => proc { |frames| IronmonStarterRivalTests.advance_frames(frames) } }
    originals = stubs.keys.to_h { |name| [name, Object.instance_method(name)] }
    stubs.each { |name, implementation| Object.send(:define_method, name, implementation) }
    Ironmon.position_rival_for_lab_battle(rival)
    assert(rival.x == 8 && rival.y == 8 && rival.direction == 6, "rival returns beside the player and faces them for battle")
    capture("02-ready-for-battle")
    mapper = Object.new
    mapper.define_singleton_method(:species) { |_body, _head| :B1H4 }
    mapper.define_singleton_method(:species_number) { |_body, _head| NB_POKEMON + 4 }
    stub(Ironmon, :player_fusion_mapper, proc { mapper }) do
      stub(Ironmon, :player_fusion_pairing_ready?, proc { true }) do
      stub(Ironmon, :show_rival_lab_fusion, proc do |_body, _head, _species|
        birch = $game_map.events[Ironmon::EARLY_GAME_LAB_PROFESSOR_EVENT_ID]
        IronmonStarterRivalTests.assert(rival.direction == 6 && [rival.x, rival.y] == [birch.x - 1, birch.y],
          "fusion animation starts only after approaching and facing Birch")
      end) do
        matrix_cases.each_with_index do |(policy, current, remaining, action, species), index|
          rules(policy)
          $PokemonGlobal.ironmon_starter_species = [:SQUIRTLE, current, remaining]
          $PokemonGlobal.ironmon_rival_lab_decision = nil
          trainer = initializeRivalBattledTrainer
          $PokemonGlobal.battledTrainers = { BATTLED_TRAINER_RIVAL_KEY => trainer }
          initial = Ironmon.rival_story_starter(trainer)
          rival.moveto(8, 8)
          rival.opacity = 255
          rival.direction_fix = true
          Ironmon.resolve_rival_lab_choice(rival)
          partner = Ironmon.rival_story_starter(trainer)
          assert(partner.species == species && $PokemonGlobal.ironmon_rival_lab_decision[:action] == action,
            "native #{policy} event commits #{action} #{species}")
          assert((action == :swap) != partner.equal?(initial), "only a swap replaces the rival's individual Pokemon")
          repeat = partner.personalID
          Ironmon.resolve_rival_lab_choice(rival)
          assert(partner.personalID == repeat, "repeated event resolution cannot change the selected partner again")
          capture("03-#{index}-#{action}")
          updateRivalTeamForSecondBattle
          assert(Ironmon.rival_story_starter(trainer).equal?(partner) && partner.species == species,
            "second story battle preserves the post-lab choice without another fusion")
          updateRivalTeamForThirdBattle
          assert(Ironmon.rival_story_starter(trainer).equal?(partner), "third story battle carries the marked partner through evolution")
          restored = Marshal.load(Marshal.dump(trainer))
          assert(Ironmon.rival_story_starter(restored), "the selected story partner survives save serialization")
          Ironmon.leave_rival_lab(rival)
          assert([rival.x, rival.y] == Ironmon::RIVAL_LAB_EXIT && rival.direction == 2 && rival.opacity == 0,
            "#{action} exits at the doorway facing down")
        end
      end
      end
    end
    assert(trace.any? { |step| step[2] == 8 } && trace.any? { |step| step[2] == 2 }, "native routes exercise arrival and departure turns")
    assert(!trace.any? { |x,y,d| step = Ironmon::RIVAL_LAB_STEPS[d]; [x + step[0], y + step[1]] == [$game_player.x, $game_player.y] },
      "no route walks through the player")
    File.binwrite(File.join(@output, "movement.txt"), trace.map(&:inspect).join("\n"))
  ensure
    originals.to_h.each { |name, method| Object.send(:define_method, name, method) } if originals
    $scene.disposeSpritesets if $scene && $scene.respond_to?(:disposeSpritesets)
  end

  def self.run(output)
    @output = output
    if ENV["IRONMON_RIVAL_TRACKER_PORT"]
      test_native_tracker_fusion
      test_real_fusion_animation
      File.binwrite(File.join(output, "starter-rival-result.txt"), "Passed #{@assertions} native worker/animation assertions.\n")
      return
    end
    test_configuration
    test_starter_policies
    test_matrix
    test_worker_reply_contract
    test_readonly_fusion_preview
    test_opening_exception
    test_native_movement
    test_story_preview_after_reload
    test_complete_lab_sequence
    File.binwrite(File.join(output, "starter-rival-result.txt"), "Passed #{@assertions} assertions, #{@frames} native movement frames.\n")
  end

  def self.test_story_preview_after_reload
    rules(:mixed, :normal_only)
    pbSet(VAR_HOENN_CHOSEN_STARTER_INDEX, 0)
    $PokemonGlobal.ironmon_starter_species = [:SQUIRTLE, :BULBASAUR, :CHARMANDER]
    $PokemonGlobal.ironmon_rival_lab_decision = nil
    trainer = initializeRivalBattledTrainer
    $PokemonGlobal.battledTrainers = { BATTLED_TRAINER_RIVAL_KEY => trainer }
    decision = Ironmon.rival_lab_decision(:BULBASAUR, :CHARMANDER, false)
    decision[:species] = :B1H4
    Ironmon.apply_rival_lab_decision(decision)
    before = Marshal.dump($PokemonGlobal)
    entries = (1..3).map { |stage| { "battle_kind" => "rival", "entry_id" => stage.to_s, "story_stage" => stage } }
    Ironmon.instance_variable_set(:@tracker_story_trainer_preview_cache, {})
    stub(Ironmon, :tracker_area_catalog, proc { [{ "trainers" => entries }] }) do
      stub(Ironmon, :player_fusion_pairing_ready?, proc { false }) do
        stub(Ironmon, :player_fusion_mapper, proc { raise "Story preview must reuse the saved rival decision" }) do
          result = Ironmon.tracker_current_story_trainer_previews
          assert(result["1"] && result["1"].first["species_id"] == "BULBASAUR:0", "opening story preview shows the counter offer")
          assert(result["2"] && result["2"].last["species_id"] == "B1H4:0", "later story preview reuses the saved fusion before pairing is ready")
        end
      end
    end
    assert(Marshal.dump($PokemonGlobal) == before, "story previews do not change the saved rival or run state")
  end

  def self.test_readonly_fusion_preview
    mappings = {}
    pool = [2 * NB_POKEMON + 5, 3 * NB_POKEMON + 6]
    mapper = Ironmon::PlayerFusionMapper.new(42, pool, mappings, {}, Ironmon.base_stat_generator, 2)
    mapper.prepare
    preview = mapper.preview_species(:BULBASAUR, :CHARMANDER)
    assert(mappings.empty?, "fusion previews do not add material mappings to the saved run")
    assert(mapper.species(:BULBASAUR, :CHARMANDER) == preview, "a preview matches the committed fusion result")
  end

  def self.test_complete_lab_sequence
    originals = nil
    mapper = Object.new
    mapper.define_singleton_method(:species) { |_body, _head| :B1H4 }
    mapper.define_singleton_method(:species_number) { |_body, _head| NB_POKEMON + 4 }
    cases = [
      [:normal_only, :BULBASAUR, :B7H1, :keep],
      [:normal_only, :MAGIKARP, :MEWTWO, :swap],
      [:mixed, :BULBASAUR, :CHARMANDER, :fuse],
      [:mixed, :B1H4, :SQUIRTLE, :swap],
      [:normal_only, :B1H4, :B7H1, :unfuse]
    ]
    cases << [:mixed, :BULBASAUR, :CHARMANDER, nil]
    cases.each_with_index do |(policy, current, remaining, action), index|
      rival = setup_lab
      rules(policy)
      $PokemonGlobal.ironmon_starter_species = [:SQUIRTLE, current, remaining]
      pbSet(VAR_HOENN_STARTER, Pokemon.new(:SQUIRTLE, 5))
      rewards = []
      announcement = []
      stubs = { :pbMessage => proc { |*_args| 0 }, :pbCallBub => proc { |*_args| },
        :pbWait => proc { |frames| IronmonStarterRivalTests.advance_frames(frames) },
        :pbAddPokemon => proc { |*_args| true }, :pbUnlockDex => proc {},
        :pbReceiveItem => proc { |*args| rewards << args; true },
        :hoennRivalBattle => proc do |*_args|
          trainer = initializeRivalBattledTrainer
          $PokemonGlobal.battledTrainers = { BATTLED_TRAINER_RIVAL_KEY => trainer }
          IronmonStarterRivalTests.assert(Ironmon.rival_story_starter(trainer).species == current,
            "the complete event battles with the counter offer")
          IronmonStarterRivalTests.assert(rival.direction == 6 && $game_player.direction == 4,
            "both characters face each other at the complete event's battle boundary")
          !action.nil?
        end }
      originals = stubs.keys.to_h { |name| [name, Object.instance_method(name)] }
      stubs.each { |name, implementation| Object.send(:define_method, name, implementation) }
      stub(Ironmon, :player_fusion_mapper, proc { mapper }) do
        stub(Ironmon, :player_fusion_pairing_ready?, proc { true }) do
          stub(Ironmon, :show_rival_lab_fusion, proc { |*_args| }) do
            stub(Ironmon, :announce_tracker_active_run_preparation_ready, proc { announcement << true }) do
              Ironmon.run_first_rival_lab_sequence
            end
          end
        end
      end
      if action.nil?
        assert($PokemonGlobal.ironmon_rival_lab_decision.nil?, "a lost opening battle does not change the rival's partner")
        assert(rewards.empty? && announcement.empty?, "a lost opening battle grants no rewards or preparation announcement")
        assert(!$game_switches[Ironmon::EARLY_GAME_SWITCH_OPENING_COMPLETE], "a loss does not complete the opening")
      else
      assert($PokemonGlobal.ironmon_rival_lab_decision[:action] == action, "complete lab event performs #{action}")
      assert($game_switches[Ironmon::EARLY_GAME_SWITCH_OPENING_COMPLETE] && announcement.length == 1,
        "#{action} completes the opening and announces preparation once")
      assert(rewards == [[:POKEBALL, 5]], "#{action} retains the professor's one-time Poke Ball reward")
      assert([rival.x, rival.y] == Ironmon::RIVAL_LAB_EXIT && rival.direction == 2,
        "complete #{action} branch exits from Birch through the real doorway")
      end
      capture("04-complete-#{index}-#{action}")
      originals.each { |name, method| Object.send(:define_method, name, method) }
      originals = nil
    end
  ensure
    originals.to_h.each { |name, method| Object.send(:define_method, name, method) } if originals
  end

  def self.test_real_fusion_animation
    rival = setup_lab
    rules
    Ironmon.prepare_base_stat_randomization
    decision = Ironmon.rival_lab_decision(:BULBASAUR, :CHARMANDER, false)
    frames = 0
    if @native_fusion_species
      decision[:species] = @native_fusion_species
    else
      Ironmon.finish_rival_lab_fusion(decision)
    end
    assert(Ironmon.custom_fusion_species?(decision[:species]), "real player-fusion mapping produces an eligible rival fusion")
    Ironmon.approach_birch_with_rival(rival)
    capture("05-before-native-fusion")
    body = Pokemon.new(:BULBASAUR, 5)
    head = Pokemon.new(:CHARMANDER, 5)
    before = Marshal.dump([$Trainer.party, $Trainer.pokedex, body, head])
    capture_patch = Module.new
    output = @output
    animation_started = nil
    first_frame_at = nil
    capture_patch.define_method(:update) do
      super()
      frames += 1
      first_frame_at ||= Ironmon.tracker_uptime_seconds if animation_started
      if [80, 200, 320].include?(frames)
        bitmap = Graphics.snap_to_bitmap
        bitmap.save_to_png(File.join(output, "05-native-fusion-#{frames}.png"))
        bitmap.dispose
      end
    end
    Graphics.singleton_class.prepend(capture_patch)
    started = Ironmon.tracker_uptime_seconds
    animation_started = started
    Ironmon.show_rival_lab_fusion(body, head, decision[:species])
    duration = Ironmon.tracker_uptime_seconds - started
    File.binwrite(File.join(@output, "fusion-animation-timing.txt"), "#{duration.round(3)} seconds; #{frames} frames; first frame #{((first_frame_at-started)*1000).round(1)} ms\n")
    assert(first_frame_at - started < 1.0, "the visible fusion starts within one second after receiving its result")
    assert(Marshal.dump([$Trainer.party, $Trainer.pokedex, body, head]) == before,
      "NPC animation leaves the player's party and Pokedex and both materials unchanged")
    assert(frames > 200, "the actual native fusion animation played through its movement frames")
    assert(frames < Graphics.frame_rate * 20, "fusion ends with its visible Pokemon tracks instead of the 50-second splicer tail")
    capture("05-after-native-fusion")
    Ironmon.apply_rival_lab_decision(decision)
    Ironmon.leave_rival_lab(rival)
    assert([rival.x, rival.y] == Ironmon::RIVAL_LAB_EXIT && rival.direction == 2 && rival.opacity == 0,
      "the event continues through committing the fusion and leaving the lab after the real animation")
  ensure
    capture_patch.send(:remove_method, :update) if capture_patch
  end

  def self.test_native_tracker_fusion
    setup_lab
    $PokemonGlobal.ironmon_generation_profile_id = Ironmon.current_generation_profile_id
    pool = Ironmon::ProfileCustomFusionPool.new(Ironmon.generation_profile_component_path("custom_fusion_pool"))
    Ironmon.instance_variable_set(:@custom_fusion_pool_service, pool)
    assert(Ironmon.prepare_base_stat_randomization, "native test prepares authoritative base-stat metadata")
    assert(Ironmon.prepare_evolution_randomization, "native test prepares authoritative evolution metadata")
    Ironmon.prepare_rival_lab_resources
    Ironmon.send(:remove_const, :TRACKER_PORT)
    Ironmon.const_set(:TRACKER_PORT, Integer(ENV.fetch("IRONMON_RIVAL_TRACKER_PORT")))
    connection = Ironmon::TrackerConnection.new
    connection.define_singleton_method(:log_error) do |error|
      File.binwrite(File.join(IronmonStarterRivalTests.instance_variable_get(:@output), "connection-error.txt"), "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")
      super(error)
    end
    Ironmon.instance_variable_set(:@tracker_connection, connection)
    deadline = Ironmon.tracker_uptime_seconds + 10.0
    until connection.session_ready?
      connection.update
      Graphics.update
      raise "Native tracker handshake timed out" if Ironmon.tracker_uptime_seconds > deadline
    end
    # Complete current-state recovery before requesting work for this run.
    10.times { connection.update; Graphics.update }
    assert(connection.rival_fusion_worker_supported?, "native tracker negotiates single-pair fusion support")
    timings = []
    stub(Ironmon, :player_fusion_pairing_ready?, proc { false }) do
      stub(Ironmon, :player_fusion_mapper, proc { raise "Rival must not build the Ruby fusion catalogue" }) do
        [:cold, :warm].each do |mode|
          if mode == :warm
            assert(Ironmon.prepare_tracker_rival_fusion, "starter preparation sends metadata without material IDs")
            40.times { connection.update; Graphics.update }
          end
          decision = Ironmon.rival_lab_decision(:BULBASAUR, :CHARMANDER, false)
          started = Ironmon.tracker_uptime_seconds
          Ironmon.finish_rival_lab_fusion(decision)
          elapsed = Ironmon.tracker_uptime_seconds - started
          timings << "#{mode}: #{(elapsed * 1000).round(1)} ms"
          assert(elapsed < 1.0, "#{mode} full game/tracker round trip completes below one second (#{elapsed.round(3)}s)")
          assert(Ironmon.custom_fusion_species?(decision[:species]), "native result belongs to the run's custom pool")
          assert(!@native_fusion_species || @native_fusion_species == decision[:species], "cold and retained requests produce the same exact fusion")
          @native_fusion_species = decision[:species]
        end
      end
    end
    File.binwrite(File.join(@output, "fusion-worker-timing.txt"), timings.join("\n"))
    connection.send(:disconnect)
    trainer = initializeRivalBattledTrainer
    $PokemonGlobal.battledTrainers = { BATTLED_TRAINER_RIVAL_KEY => trainer }
  end

  def self.test_worker_reply_contract
    expected = { "session_id" => Ironmon.game_session_id, "request_id" => "rival-contract-test",
      "body_id" => 1, "head_id" => 4 }
    run_id = Ironmon.ensure_tracker_run_id
    number = Ironmon.custom_fusion_pool_service.numbers.first
    payload = expected.merge("species_number" => number)
    ["session_id", "request_id", "body_id", "head_id"].each do |key|
      request = { :payload => expected, :run_id => run_id }
      Ironmon.instance_variable_set(:@tracker_rival_fusion_request, request)
      Ironmon.receive_tracker_rival_fusion({ "run_id" => run_id, "payload" => payload.merge(key => "stale") })
      assert(!request.key?(:species) && !request.key?(:error), "a mismatched #{key} cannot complete the rival request")
    end
    request = { :payload => expected, :run_id => run_id }
    Ironmon.instance_variable_set(:@tracker_rival_fusion_request, request)
    Ironmon.receive_tracker_rival_fusion({ "run_id" => "previous-run", "payload" => payload })
    assert(!request.key?(:species), "a reply from a previous run is ignored")
    Ironmon.receive_tracker_rival_fusion({ "run_id" => run_id, "payload" => payload.merge("species_number" => 1) })
    assert(request[:error] && !request.key?(:species), "a normal or out-of-pool result is rejected")
    request = { :payload => expected, :run_id => run_id }
    Ironmon.instance_variable_set(:@tracker_rival_fusion_request, request)
    Ironmon.receive_tracker_rival_fusion({ "run_id" => run_id, "payload" => payload })
    assert(Ironmon.fusion_species_identity(number) == request[:species], "a matching exact-pool result resolves directly to its identity")
  ensure
    Ironmon.instance_variable_set(:@tracker_rival_fusion_request, nil)
  end
end
