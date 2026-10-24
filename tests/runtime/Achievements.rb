module IronmonAchievementRuntimeTests
  class Connection
    attr_reader :sent
    attr_accessor :overlay_enabled, :popups_enabled
    def initialize; @sent = []; end
    def feature_enabled?(_name); return @popups_enabled != false; end
    def session_ready?; return true; end
    def achievement_notifications_supported?; return true; end
    def output_drained?; return true; end
    def achievement_overlay_supported?; return @overlay_enabled == true; end
    def update; end
    def send_event(event, payload); @sent << [event, payload]; return true; end
  end

  def self.assert(value, message)
    raise message if !value
  end

  def self.test_overlay(achievements, notice, connection)
    connection.overlay_enabled = true
    base = Ironmon::GameplayCollection.clock + 50_000
    decoded = achievements.decode_notice(notice).merge("id" => notice["id"])
    window = achievements::OverlayToast.new(decoded, base)
    assert(connection.sent.last[0] == "achievement_overlay_start", "earned overlay uses the normal game event channel")
    assert(connection.sent.last[1].keys.sort == ["achievement_id", "presentation_id"], "overlay start contains only the earned identity and presentation")
    window.accept_overlay({ "presentation_id" => window.presentation_id, "ready" => true, "visible" => true }, base + 0.01)
    window.update(base + 0.02)
    window.update(base + 0.11)
    elapsed = window.instance_variable_get(:@elapsed)
    assert((elapsed - 0.09).abs < 0.0001, "game clock controls the entry animation")
    window.accept_overlay({ "presentation_id" => window.presentation_id, "ready" => true, "visible" => false }, base + 0.2)
    window.update(base + 0.2)
    assert(window.instance_variable_get(:@elapsed) == elapsed, "hidden notifications retain their remaining visible duration")
    window.dispose
    assert(connection.sent.last[0] == "achievement_overlay_stop", "completed presentations release the desktop window")
    missing = achievements::OverlayToast.new(decoded, base + 10)
    missing.update(base + 10.8)
    assert(missing.instance_variable_get(:@fallback).is_a?(achievements::Toast), "missing renderer acknowledgement uses the native fallback")
    assert(!missing.update(base + 10.9), "fallback waits for the full display")
    assert(missing.update(base + 10.8 + achievements::DISPLAY_SECONDS + 0.01), "fallback finishes after the full animation")
    missing.dispose
    rejected = achievements::OverlayToast.new(decoded, base + 20)
    rejected.accept_overlay({ "presentation_id" => rejected.presentation_id, "ready" => false, "visible" => false }, base + 20.01)
    rejected.update(base + 20.02)
    assert(rejected.instance_variable_get(:@fallback).is_a?(achievements::Toast), "renderer failures use the native fallback")
    rejected.dispose
    earned = notice.merge("id" => "f2783e44-d44e-4bec-99e4-2c97194885fa")
    achievements.receive({ "unlocks" => [earned] })
    achievements.tick(base + 30)
    active = achievements.instance_variable_get(:@window)
    assert(active.is_a?(achievements::OverlayToast), "earned notification dispatch selects the desktop renderer")
    32.times do |index|
      now = base + 30.01 + index * 0.1
      active.accept_overlay({ "presentation_id" => active.presentation_id, "ready" => true, "visible" => true }, now)
      achievements.tick(now)
    end
    assert(achievements.instance_variable_get(:@known)[earned["id"]] == :displayed, "earned desktop notification completes and becomes acknowledged")
    return true
  ensure
    window.dispose if window && !window.disposed?
    missing.dispose if missing && !missing.disposed?
    rejected.dispose if rejected && !rejected.disposed?
    connection.overlay_enabled = false
  end

  def self.test_catalogue_rendering(achievements)
    catalogue = $ironmon_achievement_catalogue_fixture || []
    window = nil
    catalogue.each do |entry|
      decoded = achievements.decode_notice(entry)
      assert(decoded, "installed catalogue icon decodes")
      window = achievements::Toast.new(decoded, 0)
      bitmap = window.instance_variable_get(:@sprite).bitmap
      lines = window.wrapped_lines(bitmap, entry["name"], window.width - achievements::Toast::TEXT_X - 14, 2)
      assert(lines.join(" ") == entry["name"].gsub(/\s+/, " ").strip, "complete title fits the native fallback: #{entry['name']}")
      window.dispose
    end
    return catalogue.length
  ensure
    window.dispose if window && !window.disposed?
  end

  def self.test_removed_controls(achievements, notice)
    assert(!defined?(IronmonAchievementPreviewInput), "the achievement F6 input hook is absent")
    [:preview, :start_preview_sweep, :stop_preview_sweep, :receive_preview, :pause_preview].each do |method|
      assert(!achievements.respond_to?(method), "temporary achievement method #{method} is absent")
    end
    route = Ironmon::TrackerConnection.allocate
    response = nil
    route.define_singleton_method(:queue_message) { |message| response = message }
    ["achievement_preview", "achievement_preview_all"].each do |command|
      route.send(:handle_request, { "request_id" => "removed-command", "command" => command, "payload" => notice })
      assert(response["success"] == false, "removed achievement request #{command} is rejected")
    end
  end

  def self.run
    achievements = Ironmon::Achievements
    world = Ironmon::GameplayCollection::World
    previous_loaded = world.instance_variable_get(:@loaded)
    previous_connection = Ironmon.method(:tracker_connection)
    connection = Connection.new
    Ironmon.define_singleton_method(:tracker_connection) { connection }
    world.instance_variable_set(:@loaded, true)
    variables = %i[@queue @known @displayed @window @current @shown_at @last_poll @last_error @retry_at]
    prior = variables.to_h { |key| [key, achievements.instance_variable_get(key)] }
    variables.each { |key| achievements.instance_variable_set(key, nil) }
    pixels = [255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0, 255, 255, 255, 255].pack("C*")
    probe = Bitmap.new(2, 2)
    probe.raw_data = pixels
    assert(probe.get_pixel(0, 0).red == 255 && probe.get_pixel(1, 0).alpha == 128, "RGBA channels and partial transparency survive native upload")
    assert(probe.get_pixel(0, 1).alpha == 0, "transparent pixels stay transparent")
    probe.dispose
    display_pixels = Array.new(50 * 50) { |index| [index % 50 * 5, index / 50 * 5, index * 17 % 256, 255].pack("C*") }.join
    pixel_notice = achievements.decode_notice({ "name" => "Native pixel probe", "icon" => { "width" => 50, "height" => 50, "pixels" => [display_pixels].pack("m0") } })
    pixel_window = achievements::Toast.new(pixel_notice, 0)
    pixel_panel = pixel_window.instance_variable_get(:@sprite).bitmap
    50.times do |y|
      50.times do |x|
        color = pixel_panel.get_pixel(7 + x, 7 + y)
        assert(color.red == x * 5 && color.green == y * 5 && color.blue == (y * 50 + x) * 17 % 256 && color.alpha == 255, "all approved display pixels remain unchanged")
      end
    end
    pixel_window.dispose
    fixture = $ironmon_achievement_icon_fixture || { "width" => 2, "height" => 2, "pixels" => [pixels].pack("m0") }
    notice = { "id" => "5d724f04-95a9-461e-a993-fcfa37fcbb74", "name" => "A Victory to Remember", "icon" => fixture }
    base = Ironmon::GameplayCollection.clock
    achievements.receive({ "unlocks" => [notice, notice, { "id" => "invalid" }] })
    assert(achievements.instance_variable_get(:@queue).length == 1, "duplicate unlocks queue once")
    achievements.tick(base)
    window = achievements.instance_variable_get(:@window)
    assert(window && !window.disposed?, "native earned toast is created")
    assert(window.x == -window.width && window.height == 64, "toast starts outside the screen")
    panel = window.instance_variable_get(:@sprite).bitmap
    assert(panel.get_pixel(70, 32).alpha == 191 && panel.get_pixel(4, 32).alpha == 191, "panel keeps its approved opacity")
    achievements.tick(base + achievements::SLIDE_SECONDS / 2)
    assert(window.x > -window.width && window.x < 8, "entry animation moves right")
    achievements.tick(base + achievements::SLIDE_SECONDS + 0.01)
    assert(window.x == 8 && window.y == 8 && window.x + window.width <= Graphics.width, "held toast fits the viewport")
    if $ironmon_achievement_capture_path
      capture = Graphics.snap_to_bitmap
      capture.to_file($ironmon_achievement_capture_path)
      capture.dispose
    end
    assert(connection.sent.none? { |event, _| event == "achievement_displayed" }, "unfinished display never acknowledges an unlock")
    achievements.tick(base + achievements::SLIDE_SECONDS + achievements::HOLD_SECONDS + achievements::SLIDE_SECONDS / 2)
    assert(window.x < 8, "exit animation moves left")
    achievements.tick(base + achievements::DISPLAY_SECONDS + 0.01)
    assert(window.disposed?, "completed toast releases its bitmap and sprite")
    achievements.tick(base + achievements::DISPLAY_SECONDS + 0.02)
    assert(connection.sent.any? { |event, payload| event == "achievement_displayed" && payload["ids"] == [notice["id"]] }, "only completed unlocks are acknowledged")
    achievements.receive({ "unlocks" => [notice] })
    assert(achievements.instance_variable_get(:@queue).empty?, "retried notices are not displayed again")
    interrupted = notice.merge("id" => "1454e381-e8fd-4776-80a7-8a26fc97aac8")
    achievements.receive({ "unlocks" => [interrupted] })
    achievements.tick(base + 10)
    window = achievements.instance_variable_get(:@window)
    world.instance_variable_set(:@loaded, false)
    achievements.tick(base + 10.1)
    assert(window.disposed? && achievements.instance_variable_get(:@queue).length == 1, "unloading retains interrupted earned notices")
    world.instance_variable_set(:@loaded, true)
    achievements.tick(base + 11)
    achievements.tick(base + 11 + achievements::DISPLAY_SECONDS + 0.01)
    achievements.tick(base + 11 + achievements::DISPLAY_SECONDS + 0.02)
    suppressed = notice.merge("id" => "99999999-1111-2222-3333-cccccccccccc")
    achievements.receive({ "unlocks" => [suppressed] })
    achievements.tick(base + 20)
    window = achievements.instance_variable_get(:@window)
    connection.popups_enabled = false
    achievements.tick(base + 20.1)
    assert(window.disposed? && achievements.instance_variable_get(:@queue).empty?, "feature switch hides an active notification")
    assert(achievements.instance_variable_get(:@known)[suppressed["id"]] == :displayed, "suppressed earned notice is acknowledged")
    connection.popups_enabled = true
    achievements.receive({ "unlocks" => [suppressed] })
    assert(achievements.instance_variable_get(:@queue).empty?, "re-enabling does not replay suppressed notices")
    malformed = notice.merge("id" => "cc93d540-9561-46b7-b373-7911d939df64", "icon" => { "width" => 65, "height" => 64, "pixels" => "" })
    achievements.receive({ "unlocks" => [malformed] })
    assert(achievements.instance_variable_get(:@queue).empty?, "malformed icons are rejected")
    test_removed_controls(achievements, notice)
    catalogue_count = test_catalogue_rendering(achievements)
    overlay = test_overlay(achievements, notice, connection)
    return { "status" => "passed", "native_rgba" => true, "native_50px" => true, "alpha" => true, "slide_animation" => true, "display_acknowledgement" => true, "duplicate_delivery" => true, "reset_interruption" => true, "feature_suppression" => true, "removed_test_controls" => true, "catalogue_rendering_checks" => catalogue_count, "desktop_overlay_game_clock" => overlay, "desktop_overlay_fallback" => overlay }
  ensure
    probe.dispose if probe && !probe.disposed?
    pixel_window.dispose if pixel_window && !pixel_window.disposed?
    capture.dispose if capture && !capture.disposed?
    window = achievements.instance_variable_get(:@window) if achievements
    window.dispose if window && !window.disposed?
    prior.each { |key, value| achievements.instance_variable_set(key, value) } if prior
    world.instance_variable_set(:@loaded, previous_loaded) if world
    Ironmon.define_singleton_method(:tracker_connection, previous_connection) if previous_connection
  end
end
