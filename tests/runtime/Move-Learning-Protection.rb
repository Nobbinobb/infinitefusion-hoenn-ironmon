module IronmonMoveLearningProtectionRuntimeTests
  class PickerScene < PokemonSummary_Scene
    attr_reader :choice_frame, :selected_at, :protected_start, :protected_end

    def initialize(action = :choose, fail = false)
      @action = action
      @fail = fail
      @choice_frame = nil
    end

    def pbStartForgetScene(*args)
      @protected_start = Ironmon::MoveLearningProtection.active?
      super
      @choice_frame = 0
    end

    def controls
      return ["confirm"] if !@choice_frame
      @choice_frame += 1
      raise "picker test timeout" if @choice_frame > 100
      return @choice_frame == 2 ? ["confirm"] : [] if @action == :browse
      return ["confirm"] if @choice_frame <= 12
      return @choice_frame.even? ? ["confirm"] : [] if @choice_frame <= 24
      return ["down"] if @action == :choose && [45, 47].include?(@choice_frame)
      return [@action == :cancel ? "cancel" : "confirm"] if @choice_frame == 49
      return []
    end

    def pbUpdate
      raise "test picker interruption" if @fail && @choice_frame && @choice_frame >= 3
      if @choice_frame == 40 && !$ironmon_move_learning_protection_output.to_s.empty?
        bitmap = Graphics.snap_to_bitmap
        bitmap.save_to_png(File.join($ironmon_move_learning_protection_output, "move-picker.png"))
        bitmap.dispose
      end
      super
    end

    def pbChooseMoveToForget(move)
      result = super
      @selected_at = @choice_frame
      return result
    end

    def pbEndScene
      @protected_end = Ironmon::MoveLearningProtection.active?
      @choice_frame = nil
      super
    end

    def dispose_after_interruption
      @choice_frame = nil
      pbEndScene if @viewport && !@viewport.disposed?
    end
  end

  def self.assert(condition, message)
    raise "Move-learning protection test failed: #{message}" if !condition
  end

  def self.send_controls(pressed)
    Ironmon.receive_tracker_controls({ "enabled" => true,
      "managed" => Ironmon::TRACKER_CONTROL_KEYS.keys, "pressed" => pressed })
  end

  def self.native_frame
    @native_frames += 1
  end

  def self.on_frame
    @hook_frames += 1
    if @scene
      pressed = @scene.controls
      pressed << "speed" if $PokemonSystem.speedup == 0
      send_controls(pressed)
    end
  end

  def self.configure_speed(mode, battle)
    @scene = nil
    send_controls([])
    Input.update
    $PokemonSystem.speedup = mode
    $PokemonSystem.speedup_speed = 9
    $PokemonSystem.speedup_speed_battles = 9
    $game_temp.in_battle = battle
    $CanToggle = true
    Graphics.update
    $GameSpeed = 3
    $frame = 0
    Graphics.update
  end

  def self.test_frame_pacing
    [0, 1].each do |mode|
      [false, true].each do |battle|
        configure_speed(mode, battle)
        send_controls(mode == 0 ? ["speed"] : [])
        Input.update
        $frame = 0
        before = @native_frames
        20.times { Graphics.update }
        expected = 20 * [Graphics.frame_rate * 10, Ironmon::SpeedUp.native_update_limit].min / (Graphics.frame_rate * 10)
        assert(@native_frames - before == expected, "ordinary 10x pacing uses the native frame budget")
        selected_stage = $GameSpeed
        if mode == 1
          send_controls(["speed"])
          Input.update
        end
        before = @native_frames
        hook_before = @hook_frames
        Ironmon::MoveLearningProtection.protect { 20.times { Graphics.update; Input.update } }
        assert(@native_frames - before == 20, "protected frames run at native 1x in both modes and contexts")
        assert(@hook_frames - hook_before == 20, "protection retains the ordinary graphics hooks")
        assert($GameSpeed == selected_stage && $CanToggle, "selected stage and speed lock survive protected frames")
        before = @native_frames
        20.times { Graphics.update }
        assert(@native_frames - before == expected, "10x pacing resumes after protection")
      end
    end
  end

  def self.test_picker(mode, battle, action = :choose)
    configure_speed(mode, battle)
    pokemon = Pokemon.new(:BULBASAUR, 20)
    pokemon.moves = [:TACKLE, :GROWL, :LEECHSEED, :RAZORLEAF].map { |id| Pokemon::Move.new(id) }
    before = pokemon.moves.map(&:id)
    @scene = PickerScene.new(action)
    screen = PokemonSummaryScreen.new(@scene)
    result = screen.pbStartForgetScreen([pokemon], 0, :VINEWHIP)
    assert(result == (action == :cancel ? -1 : 2), "fresh selection and cancellation preserve the native picker result")
    assert(@scene.selected_at == 49, "held and repeatedly tapped confirmation cannot overwrite the first move on entry")
    assert(@scene.protected_start && @scene.protected_end, "protection covers scene setup, choice, and fade-out")
    assert(!Ironmon::MoveLearningProtection.active? && $GameSpeed == 3 && $CanToggle, "picker restores the original speed and leaves protection")
    assert(pokemon.moves.map(&:id) == before, "the picker itself never edits moves")
  ensure
    @scene = nil
    send_controls([])
    Input.update
  end

  def self.test_browsing_and_interruption
    configure_speed(1, false)
    pokemon = Pokemon.new(:BULBASAUR, 20)
    @scene = PickerScene.new(:browse)
    result = PokemonSummaryScreen.new(@scene).pbStartForgetScreen([pokemon], 0, nil)
    assert(result == 0 && @scene.selected_at == 2 && !@scene.protected_start, "ordinary move selection is not slowed or gated")
    @scene = PickerScene.new(:choose, true)
    $CanToggle = false
    begin
      PokemonSummaryScreen.new(@scene).pbStartForgetScreen([pokemon], 0, :VINEWHIP)
      raise "Expected picker interruption"
    rescue RuntimeError => error
      raise if error.message != "test picker interruption"
    end
    assert(!Ironmon::MoveLearningProtection.active? && $GameSpeed == 3 && !$CanToggle, "an interrupted picker restores speed and preserves a pre-existing lock")
    @scene.dispose_after_interruption
    @scene = nil
    nested = Ironmon::MoveLearningProtection.protect do
      Ironmon::MoveLearningProtection.protect { :nested_result }
    end
    assert(nested == :nested_result && !Ironmon::MoveLearningProtection.active?, "nested protection restores its scope and result")
  ensure
    @scene = nil
    send_controls([])
    Input.update
    $CanToggle = true
  end

  def self.run
    original_hooks = Ironmon.instance_variable_get(:@graphics_update_hooks)
    native_owner = Graphics.singleton_class
    original_native_update = native_owner.instance_method(:update_KGC_SpecialTransition)
    original_native_update = original_native_update.super_method while original_native_update.owner != native_owner
    original_profile = Ironmon::ToggleSpeed.configuration
    original_speed = [$PokemonSystem.speedup, $PokemonSystem.speedup_speed,
      $PokemonSystem.speedup_speed_battles, $GameSpeed, $frame, $CanToggle, $game_temp.in_battle]
    hooks = original_hooks.reject { |hook| [:tracker, :status_overlays].include?(hook[:name]) }
    hooks << { :name => :move_learning_test, :callback => proc { on_frame } }
    Ironmon.instance_variable_set(:@graphics_update_hooks, hooks)
    native_owner.send(:define_method, :update_KGC_SpecialTransition) do
      IronmonMoveLearningProtectionRuntimeTests.native_frame
      original_native_update.bind(self).call
    end
    @native_frames = @hook_frames = 0
    profile = Ironmon::ToggleSpeed.default_configuration
    ["overworld", "battle"].each do |lane|
      prefix = lane == "overworld" ? "ow" : "bt"
      profile["lanes"][lane] << { "id" => "#{prefix}-ten", "nodes" => [{ "id" => "#{prefix}-10", "value" => 10 }] }
    end
    profile["links"] << { "id" => "ten", "overworld" => "ow-10", "battle" => "bt-10" }
    Ironmon::ToggleSpeed.configure(profile)
    test_frame_pacing
    [0, 1].each { |mode| [false, true].each { |battle| test_picker(mode, battle) } }
    test_picker(0, false, :cancel)
    test_picker(1, true, :cancel)
    test_browsing_and_interruption
    puts "Move-learning protection: native 10x/1x pacing, held input, mashing, selection, cancellation, browsing, and interruption passed"
  ensure
    Ironmon.instance_variable_set(:@graphics_update_hooks, original_hooks)
    native_owner.send(:define_method, :update_KGC_SpecialTransition, original_native_update) if original_native_update
    Ironmon::ToggleSpeed.configure(original_profile)
    $PokemonSystem.speedup, $PokemonSystem.speedup_speed,
      $PokemonSystem.speedup_speed_battles, $GameSpeed, $frame, $CanToggle, $game_temp.in_battle = original_speed
    Ironmon.clear_tracker_controls
  end
end

IronmonMoveLearningProtectionRuntimeTests.run
