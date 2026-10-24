# Temporary input ownership never modifies $PokemonSystem or native key bindings.
module Ironmon
  TRACKER_CONTROL_LEASE_SECONDS = 1.0
  TRACKER_CONTROL_KEYS = {
    "up" => Input::UP, "down" => Input::DOWN,
    "left" => Input::LEFT, "right" => Input::RIGHT,
    "confirm" => Input::C, "cancel" => Input::B, "action" => Input::A,
    "interact" => Input::C, "menu" => Input::B, "battle_run" => Input::B,
    "speed" => Input::X, "auxiliary" => Input::Y, "ready" => Input::Z,
    "page_up" => Input::L, "page_down" => Input::R,
    "screenshot" => Input::F8, "reset" => Input::F7,
    "quick_item_left" => :ironmon_quick_item_left,
    "quick_item_right" => :ironmon_quick_item_right,
    "quick_item_up" => :ironmon_quick_item_up,
    "quick_item_down" => :ironmon_quick_item_down,
    "quick_item_use" => :ironmon_quick_item_use
  }.freeze

  def self.clear_tracker_controls
    @tracker_control_deadline = nil
    @tracker_control_managed = []
    @tracker_control_held = []
    @tracker_control_pending = []
    @tracker_control_frames = {}
    @tracker_control_released = []
  end

  def self.receive_tracker_controls(payload)
    if !payload.is_a?(Hash) || payload["enabled"] != true
      clear_tracker_controls
      return
    end
    managed = payload["managed"]
    pressed = payload["pressed"]
    return if !managed.is_a?(Array) || !pressed.is_a?(Array)
    return if managed.length > TRACKER_CONTROL_KEYS.length ||
      pressed.length > TRACKER_CONTROL_KEYS.length
    return if (managed + pressed).any? { |key| !TRACKER_CONTROL_KEYS.key?(key) }
    clear_tracker_controls if !tracker_controls_active?
    @tracker_control_managed = managed.uniq
    next_held = pressed.uniq & @tracker_control_managed
    @tracker_control_pending |= next_held - @tracker_control_held
    @tracker_control_held = next_held
    @tracker_control_deadline = tracker_uptime_seconds + TRACKER_CONTROL_LEASE_SECONDS
  end

  def self.tracker_controls_active?
    return @tracker_control_deadline &&
      tracker_uptime_seconds < @tracker_control_deadline
  end

  def self.tracker_control_managed?(key)
    return tracker_controls_active? &&
      @tracker_control_managed.include?(tracker_control_action(key))
  end

  def self.with_tracker_control_context(context)
    previous = @tracker_control_context
    @tracker_control_context = context
    return yield
  ensure
    @tracker_control_context = previous
  end

  def self.tracker_control_field_context?
    return false if @tracker_control_context != :field
    return false if $game_temp && ($game_temp.in_menu || $game_temp.in_battle ||
      $game_temp.message_window_showing)
    return false if $game_map && pbMapInterpreterRunning?
    return true
  end

  def self.tracker_control_action(key)
    if key == Input::B && battle_cancel_run_hotkey_active?
      return "battle_run"
    end
    if tracker_control_field_context?
      return "interact" if key == Input::C
      return "menu" if key == Input::B
    end
    # Resolve the engine's aliases only after selecting their input context.
    return "confirm" if key == Input::C
    return "cancel" if key == Input::B
    return TRACKER_CONTROL_KEYS.key(key)
  end

  def self.update_tracker_controls
    if !tracker_controls_active?
      clear_tracker_controls
      return
    end
    pressed = @tracker_control_held | @tracker_control_pending
    @tracker_control_pending = []
    previous = @tracker_control_frames
    @tracker_control_frames = {}
    pressed.each { |key| @tracker_control_frames[key] = previous.fetch(key, 0) + 1 }
    @tracker_control_released = previous.keys - pressed
  end

  def self.tracker_control_frames(key)
    return (@tracker_control_frames || {}).fetch(tracker_control_action(key), 0)
  end

  def self.tracker_control_released?(key)
    return (@tracker_control_released || []).include?(tracker_control_action(key))
  end

  def self.tracker_control_action_triggered?(action, repeat = false)
    return false if action.start_with?("quick_item_") && !feature_enabled?("battle_quick_items")
    return false if action == "battle_run" && !feature_enabled?("quick_run")
    return false if !tracker_controls_active? ||
      !@tracker_control_managed.include?(action)
    frames = (@tracker_control_frames || {}).fetch(action, 0)
    return frames == 1 || (repeat && frames >= 24 && frames % 6 == 0)
  end

  def self.tracker_control_direction(diagonal = false)
    horizontal = (Input.press?(Input::RIGHT) ? 1 : 0) -
      (Input.press?(Input::LEFT) ? 1 : 0)
    vertical = (Input.press?(Input::DOWN) ? 1 : 0) -
      (Input.press?(Input::UP) ? 1 : 0)
    return 0 if horizontal == 0 && vertical == 0
    return 5 + horizontal - 3 * vertical if diagonal
    return vertical > 0 ? 2 : 8 if vertical != 0
    return horizontal > 0 ? 6 : 4
  end
end

module IronmonTrackerControlInputHooks
  def update
    Ironmon.update_tracker_controls
    return super
  end

  def press?(key)
    return Ironmon.tracker_control_frames(key) > 0 if Ironmon.tracker_control_managed?(key)
    return super
  end

  def trigger?(key)
    return Ironmon.tracker_control_frames(key) == 1 if Ironmon.tracker_control_managed?(key)
    return super
  end

  def repeat?(key)
    if Ironmon.tracker_control_managed?(key)
      frames = Ironmon.tracker_control_frames(key)
      return frames == 1 || (frames >= 24 && frames % 6 == 0)
    end
    return super
  end

  def release?(key)
    return Ironmon.tracker_control_released?(key) if
      Ironmon.tracker_control_managed?(key)
    return super
  end

  def time?(key)
    return Ironmon.tracker_control_frames(key) * 1000.0 / Graphics.frame_rate if
      Ironmon.tracker_control_managed?(key)
    return super
  end

  def dir4
    return Ironmon.tracker_control_direction if Ironmon.tracker_controls_active?
    return super
  end

  def dir8
    return Ironmon.tracker_control_direction(true) if Ironmon.tracker_controls_active?
    return super
  end
end

Input.singleton_class.prepend(IronmonTrackerControlInputHooks)

module IronmonTrackerControlMapHooks
  def update
    return Ironmon.with_tracker_control_context(:field) { super }
  end

  def call_menu
    return Ironmon.with_tracker_control_context(:menu) { super }
  end
end

module IronmonTrackerControlPlayerHooks
  def update_event_triggering
    return Ironmon.with_tracker_control_context(:field) { super }
  end
end

Scene_Map.prepend(IronmonTrackerControlMapHooks)
Game_Player.prepend(IronmonTrackerControlPlayerHooks)
