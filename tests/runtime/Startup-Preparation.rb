module IronmonStartupPreparationTests
  PLAYER_VARIABLES = %w[player_fusion_mapper player_fusion_mapper_seed player_fusion_mapper_state_id
    player_fusion_mapper_version player_fusion_preparation_mapper player_fusion_preparation_seed
    player_fusion_preparation_state_id player_fusion_preparation_version player_fusion_preparation_fiber
    player_fusion_preparation_last_work_at].freeze

  def self.assert(condition, message)
    raise "Startup preparation validation failed: #{message}" if !condition
  end

  class FakeScreen
    def initialize(state)
      @state = state
      @state[:screens] += 1
    end

    def open
      IronmonStartupPreparationTests.assert(@state[:events].first && @state[:events].first["phase"] == "loading_save",
        "the tracker receives the first loading stage before the screen opens")
      @state[:opened] += 1
      raise "startup screen open failure" if @state[:failure] == :open
    end

    def status(label)
      @state[:labels] << label
      return true
    end

    def progress(mapper, _force = false)
      IronmonStartupPreparationTests.assert(Ironmon::StartupPreparation.active?,
        "progress stays inside the noninteractive loading scope")
      @state[:phases][mapper.preparation_stage] = true if mapper
      @state[:progress] += 1
      Ironmon::SpeedClock.set_raw_frame_count(Ironmon::SpeedClock.raw_frame_count + 1)
      raise "startup progress failure" if @state[:failure] == :progress
      return true
    end

    def close
      @state[:closed] += 1
      raise "startup screen close failure" if @state[:failure] == :close
    end
  end

  def self.with_runtime(real_screen = false)
    original_global = $PokemonGlobal
    original_scene = $scene
    methods = [:tracker_ready?, :tracker_connection, :checkpoint_reset_loading?, :restoring_live_snapshot?,
      :custom_fusion_pool_numbers, :base_stat_generator, :player_fusion_generator_version, :resume_run_duration]
    originals = methods.map { |name| [name, Ironmon.method(name)] }.to_h
    saved = PLAYER_VARIABLES.map { |name| [name, Ironmon.instance_variable_get("@#{name}")] }.to_h
    clocks = Ironmon::SpeedClock.instance_variables.map { |name| [name, Ironmon::SpeedClock.instance_variable_get(name)] }.to_h
    pacing = Ironmon::SpeedUp.instance_variables.map { |name| [name, Ironmon::SpeedUp.instance_variable_get(name)] }.to_h
    original_hooks = Ironmon.instance_variable_get(:@graphics_update_hooks).dup
    original_warm = Ironmon::StartupPreparation.method(:warm_metadata)
    original_screen = Ironmon::StartupPreparation::Screen.method(:new)
    screen_had_new = Ironmon::StartupPreparation::Screen.singleton_methods(false).include?(:new)
    original_load = Game.method(:load)
    original_load = original_load.super_method until original_load.owner == Game.singleton_class
    entry_raw = Ironmon::SpeedClock.raw_frame_count
    state = { :screens => 0, :opened => 0, :closed => 0, :loads => 0, :progress => 0,
      :pumps => 0, :anchors => 0, :hooks => 0, :labels => [], :phases => {}, :events => [],
      :checkpoint => false, :rollback => false }
    PLAYER_VARIABLES.each { |name| Ironmon.instance_variable_set("@#{name}", nil) }
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_seed = 2_130_529_734
    $PokemonGlobal.ironmon_pivot_state = Ironmon::PivotState.new
    data = { :global_metadata => $PokemonGlobal }
    stats = Ironmon::BaseStatGenerator.new($PokemonGlobal.ironmon_seed, Ironmon.base_stat_source_fingerprint)
    connection = Object.new
    connection.define_singleton_method(:update_loading) { state[:pumps] += 1; true }
    connection.define_singleton_method(:send_startup_preparation_progress) do |payload|
      state[:events] << payload.dup
      true
    end
    Ironmon.define_singleton_method(:tracker_ready?) { true }
    Ironmon.define_singleton_method(:tracker_connection) { connection }
    Ironmon.define_singleton_method(:checkpoint_reset_loading?) { state[:checkpoint] }
    Ironmon.define_singleton_method(:restoring_live_snapshot?) { state[:rollback] }
    Ironmon.define_singleton_method(:custom_fusion_pool_numbers) { IronmonStartupPreparationTests.pool }
    Ironmon.define_singleton_method(:base_stat_generator) { stats }
    Ironmon.define_singleton_method(:player_fusion_generator_version) { Ironmon::PlayerFusionMapper::SCHEMA_VERSION }
    Ironmon.define_singleton_method(:resume_run_duration) { state[:anchors] += 1 }
    Ironmon.register_graphics_update_hook(:startup_preparation_fixture, proc { state[:hooks] += 1 })
    Ironmon::StartupPreparation.define_singleton_method(:warm_metadata) do |screen|
      report_phase(:world_data)
      screen.progress(nil, true)
      raise "startup metadata failure" if state[:failure] == :metadata
    end
    if !real_screen
      Ironmon::StartupPreparation::Screen.define_singleton_method(:new) { FakeScreen.new(state) }
    end
    Game.define_singleton_method(:load) do |_save_data|
      if Ironmon::StartupPreparation.active?
        IronmonStartupPreparationTests.assert(state[:events].last["phase"] == "loading_save",
          "the loading-save stage is emitted before heavy Game.load work starts")
      end
      state[:loads] += 1
      Ironmon.run_graphics_update_hooks if Ironmon::StartupPreparation.active?
      raise "startup underlying load failure" if state[:failure] == :load
      Ironmon::SpeedClock.set_raw_frame_count(12_345)
      Ironmon::SpeedClock.instance_variable_set(:@playtime_frames, 4_567.0)
      Ironmon::SpeedClock.rebase_native_frames
      $scene = Scene_Map.allocate
      $scene.define_singleton_method(:update) { raise "Startup loading advanced the map" }
      state[:during_load].call if state[:during_load]
      :loaded
    end
    yield state, data
  ensure
    Game.define_singleton_method(:load, original_load) if original_load
    originals.each { |name, callable| Ironmon.define_singleton_method(name, callable) } if originals
    saved.each { |name, value| Ironmon.instance_variable_set("@#{name}", value) } if saved
    Ironmon.instance_variable_set(:@graphics_update_hooks, original_hooks) if original_hooks
    if original_warm
      Ironmon::StartupPreparation.define_singleton_method(:warm_metadata, original_warm)
      if screen_had_new
        Ironmon::StartupPreparation::Screen.define_singleton_method(:new, original_screen)
      elsif Ironmon::StartupPreparation::Screen.singleton_methods(false).include?(:new)
        Ironmon::StartupPreparation::Screen.singleton_class.send(:remove_method, :new)
      end
    end
    $PokemonGlobal = original_global
    $scene = original_scene
    Ironmon::SpeedClock.set_raw_frame_count(entry_raw) if entry_raw
    clocks.each { |name, value| Ironmon::SpeedClock.instance_variable_set(name, value) } if clocks
    pacing.each { |name, value| Ironmon::SpeedUp.instance_variable_set(name, value) } if pacing
  end

  def self.pool
    return @pool
  end

  def self.full_pool
    return @full_pool
  end

  def self.test_loading_flow
    with_runtime do |state, data|
      assert(Game.load(data) == :loaded, "normal save loading preserves the underlying result")
      mapper = Ironmon.player_fusion_mapper
      assert(mapper.startup_prepared? && !Ironmon.instance_variable_get(:@player_fusion_preparation_fiber) &&
        mapper.instance_variable_get(:@work_checkpoint).nil?,
        "pairing and result lookup indexes finish before Game.load returns control")
      assert(state[:phases][:result_index] && state[:progress] > 1 && state[:pumps] > 0,
        "loading reports cooperative progress and pumps its restricted connection")
      assert(state[:screens] == 1 && state[:opened] == 1 && state[:closed] == 1 && state[:anchors] == 1 &&
        state[:hooks] == 0 && !Ironmon::StartupPreparation.active?,
        "loading owns one screen, suppresses gameplay hooks, reanchors elapsed play and clears its guard")
      assert(state[:events].first["status"] == "running" && state[:events].last["status"] == "complete" &&
        state[:events].last["phase"] == "complete" &&
        state[:events].all? { |event| event.keys.sort == %w[phase preparation_id status] },
        "tracker loading events contain only stage changes and always terminate")
      assert(Ironmon::SpeedClock.raw_frame_count == 12_345 && Ironmon::SpeedClock.playtime_frame_count == 4_567 &&
        Ironmon::SpeedUp.instance_variable_get(:@last_tick).nil? &&
        Ironmon::SpeedDiagnostics.instance_variable_get(:@last_frame_stamp).nil?,
        "loading renders do not advance saved clocks or appear as a later gameplay stall")
      baseline = Ironmon::PlayerFusionMapper.new($PokemonGlobal.ironmon_seed, pool, {}, {}, Ironmon.base_stat_generator)
      baseline.prepare
      baseline.prepare_startup_result_index
      assert(mapper.instance_variable_get(:@fusion_pairs) == baseline.instance_variable_get(:@fusion_pairs) &&
        mapper.instance_variable_get(:@result_bst_index) == baseline.instance_variable_get(:@result_bst_index),
        "startup warming preserves exact seeded pairings and lookup index contents")
      index = mapper.instance_variable_get(:@result_bst_index)
      Ironmon.finish_player_fusion_startup_preparation { |_current| }
      assert(Ironmon.player_fusion_mapper.equal?(mapper) && mapper.instance_variable_get(:@result_bst_index).equal?(index) &&
        !Ironmon.advance_player_fusion_pairing,
        "a fully warmed mapper is reused and leaves no passive calculation after the map opens")
    end
  end

  def self.test_skips_and_failures
    [:ordinary, :checkpoint, :rollback].each do |kind|
      with_runtime do |state, data|
        data[:global_metadata].ironmon_mode = false if kind == :ordinary
        state[:checkpoint] = true if kind == :checkpoint
        state[:rollback] = true if kind == :rollback
        assert(Game.load(data) == :loaded && state[:screens] == 0 && state[:progress] == 0 &&
          !Ironmon::StartupPreparation.active?, "#{kind} loads preserve their existing flow")
      end
    end
    [:open, :load, :metadata, :progress, :close].each do |failure|
      with_runtime do |state, data|
        state[:failure] = failure
        begin
          Game.load(data)
          raise "An injected startup failure was ignored"
        rescue RuntimeError => error
          assert(error.message.start_with?("startup "), "startup failures retain their original error")
        end
        assert(!Ironmon::StartupPreparation.active? && state[:closed] == 1,
          "#{failure} failure always closes the loading presentation and clears its guard")
        assert(state[:events].last["status"] == "failed" && state[:events].last["error"].start_with?("startup "),
          "#{failure} failure always sends a terminal tracker error")
      end
    end
    with_runtime do |_state, _data|
      original_state = Ironmon.pivot_state
      begin
        Ironmon.finish_player_fusion_startup_preparation do |_mapper|
          $PokemonGlobal.ironmon_pivot_state = Ironmon::PivotState.new
        end
        raise "A changed startup run was accepted"
      rescue Ironmon::PlayerFusionMappingError
      end
      assert(!Ironmon.instance_variable_get(:@player_fusion_mapper) &&
        !Ironmon.instance_variable_get(:@player_fusion_preparation_fiber) && original_state.fusion_mappings.empty?,
        "a changed run cannot publish stale startup work or create gameplay discoveries")
    end
  end

  def self.test_native_screen
    with_runtime(true) do |state, data|
      original_paint = Ironmon::StartupPreparation::Screen.instance_method(:paint)
      paints = 0
      Ironmon::StartupPreparation::Screen.send(:define_method, :paint) do
        paints += 1
        original_paint.bind(self).call
      end
      settings = [Ironmon::SpeedUp::NATIVE_RATE.call, Graphics.frameskip, Graphics.brightness,
        Ironmon::SpeedUp.frozen, Ironmon::SpeedUp.multiplier]
      assert(Game.load(data) == :loaded && Ironmon.player_fusion_mapper.startup_prepared?,
        "the real native black loading screen completes all startup work")
      assert(settings == [Ironmon::SpeedUp::NATIVE_RATE.call, Graphics.frameskip, Graphics.brightness,
        Ironmon::SpeedUp.frozen, Ironmon::SpeedUp.multiplier] && state[:hooks] == 0,
        "native loading restores presentation settings and never runs gameplay updates")
      assert(paints == 1, "the native game message is painted once without animation or stage redraws")
    ensure
      Ironmon::StartupPreparation::Screen.send(:define_method, :paint, original_paint) if original_paint
    end
  end

  def self.test_stage_only_events
    with_runtime do |state, data|
      state[:during_load] = proc do
        before = state[:events].length
        1_000.times { Ironmon::StartupPreparation.report_phase(:loading_save) }
        assert(state[:events].length == before, "repeated same-stage callbacks never produce wire updates")
      end
      2.times { Game.load(data) }
      starts = state[:events].select { |event| event["status"] == "running" && event["phase"] == "loading_save" }
      assert(starts.length == 2 && starts.map { |event| event["preparation_id"] }.uniq.length == 2,
        "each normal save load owns a unique preparation identity")
      state[:events].group_by { |event| event["preparation_id"] }.each_value do |events|
        running = events.select { |event| event["status"] == "running" }
        assert(running.each_cons(2).all? { |first, second| first["phase"] != second["phase"] } &&
          events.last["status"] == "complete", "only changed stages and one terminal result are sent per load")
      end
    end
    with_runtime do |state, data|
      begin
        raise "an unrelated caller exception"
      rescue RuntimeError
        Game.load(data)
      end
      assert(state[:events].last["status"] == "complete" && !state[:events].last.key?("error"),
        "a successful load inside a caller rescue cannot inherit that caller's error")
    end
  end

  def self.test_nested_graphics_update
    with_runtime do |state, data|
      original_stage = $GameSpeed
      begin
        $GameSpeed = 1
        Ironmon::SpeedClock.begin_tick
        depth = Ironmon::SpeedClock.instance_variable_get(:@tick_depth)
        sequence = Ironmon::SpeedClock.instance_variable_get(:@tick_sequence)
        state[:during_load] = proc { Graphics.update }
        Game.load(data)
        assert(Ironmon::SpeedClock.instance_variable_get(:@tick_depth) == depth &&
          Ironmon::SpeedClock.instance_variable_get(:@tick_sequence) == sequence && $GameSpeed == 1 && state[:hooks] == 0,
          "loading graphics neither consume an enclosing logical tick nor change the selected speed stage")
      ensure
        $GameSpeed = original_stage
      end
    end
  end

  def self.test_full_startup_helper
    with_runtime do |state, data|
      Ironmon.define_singleton_method(:custom_fusion_pool_numbers) { IronmonStartupPreparationTests.full_pool }
      started = Process.clock_gettime(Process::CLOCK_MONOTONIC)
      assert(Game.load(data) == :loaded, "the full native startup helper completes behind the loading scope")
      elapsed = Process.clock_gettime(Process::CLOCK_MONOTONIC) - started
      mapper = Ironmon.player_fusion_mapper
      index = mapper.instance_variable_get(:@result_bst_index)
      assert(elapsed < 30 && mapper.startup_prepared? && index.values.sum(&:length) == full_pool.length &&
        state[:phases][:result_index] && !Ironmon.advance_player_fusion_pairing,
        "every real custom fusion is indexed before loading returns, with no passive work left for gameplay")
      return { "seed" => $PokemonGlobal.ironmon_seed, "identities" => full_pool.length,
        "seconds" => elapsed, "progress_callbacks" => state[:progress], "phases" => state[:phases].keys.map(&:to_s),
        "index_entries" => index.values.sum(&:length), "background_pending" => false,
        "stage_events" => state[:events] }
    end
  end

  def self.run_cases
    @full_pool = Ironmon.custom_fusion_pool_numbers
    @pool = 8.times.map { |index| @full_pool[index * @full_pool.length / 8] }
    test_loading_flow
    test_skips_and_failures
    test_native_screen
    test_stage_only_events
    test_nested_graphics_update
    result = test_full_startup_helper
    output = $ironmon_speed_up_acceptance_output.to_s
    File.binwrite(File.join(output, "startup-preparation.json"), Ironmon.tracker_json_generate(result)) if !output.empty?
    puts "Startup preparation: native loading, exact seeded warmup, clocks, progress, guards and failure cleanup passed"
    puts Ironmon.tracker_json_generate(result)
  end

  def self.run
    profile_id = ENV["IRONMON_PLAYER_FUSION_PROFILE"]
    if profile_id
      Ironmon.with_generation_profile(profile_id) { run_cases }
    else
      run_cases
    end
  end
end

IronmonStartupPreparationTests.run
