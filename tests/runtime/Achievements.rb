module IronmonAchievementRuntimeTests
  class Connection
    attr_reader :sent
    def initialize; @sent = []; end
    def session_ready?; return true; end
    def achievement_notifications_supported?; return true; end
    def output_drained?; return true; end
    def update; end
    def send_event(event, payload); @sent << [event, payload]; return true; end
  end

  def self.assert(value, message)
    raise message if !value
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
    assert(probe.respond_to?(:raw_data=), "engine supports in-memory pixel uploads")
    probe.raw_data = pixels
    assert(probe.get_pixel(0, 0).red == 255 && probe.get_pixel(1, 0).alpha == 128, "RGBA channel order and partial transparency survive native upload")
    assert(probe.get_pixel(0, 1).alpha == 0, "transparent pixels stay transparent")
    probe.dispose
    fixture = $ironmon_achievement_icon_fixture || { "width" => 2, "height" => 2, "pixels" => [pixels].pack("m0") }
    notice = { "id" => "5d724f04-95a9-461e-a993-fcfa37fcbb74", "name" => "A Victory to Remember", "icon" => fixture }
    base = Ironmon::GameplayCollection.clock
    achievements.receive({ "unlocks" => [notice, notice, { "id" => "invalid" }] })
    assert(achievements.instance_variable_get(:@queue).length == 1, "duplicate unlocks queue once")
    achievements.tick(base)
    window = achievements.instance_variable_get(:@window)
    assert(window && !window.disposed?, "native toast was created: #{achievements.instance_variable_get(:@last_error)}")
    assert(window.x == -window.width && window.height == 64, "compact toast starts outside the left screen edge")
    assert(window.name == notice["name"], "toast retains the sample title")
    panel = window.instance_variable_get(:@sprite).bitmap
    assert(panel.get_pixel(70, 32).alpha.between?(200, 230) && panel.get_pixel(4, 32).alpha.between?(200, 230), "title and badge backgrounds remain slightly translucent")
    achievements.tick(base + achievements::SLIDE_SECONDS / 2)
    assert(window.x > -window.width && window.x < 8, "entry animation progresses from the left")
    achievements.tick(base + achievements::SLIDE_SECONDS + 0.01)
    assert(window.x == 8 && window.y == 8 && window.x + window.width <= Graphics.width, "held toast fits the top-left viewport")
    if $ironmon_achievement_capture_path
      capture = Graphics.snap_to_bitmap
      capture.to_file($ironmon_achievement_capture_path)
      capture.dispose
    end
    assert(connection.sent.none? { |event, _| event == "achievement_displayed" }, "no acknowledgement is sent before display completes")
    achievements.tick(base + achievements::SLIDE_SECONDS + achievements::HOLD_SECONDS + achievements::SLIDE_SECONDS / 2)
    assert(window.x < 8 && window.x > -window.width, "exit animation moves left")
    achievements.tick(base + achievements::DISPLAY_SECONDS + 0.01)
    assert(window.disposed?, "completed toast releases native bitmap and sprite")
    achievements.tick(base + achievements::DISPLAY_SECONDS + 0.02)
    ack = connection.sent.find { |event, _| event == "achievement_displayed" }
    assert(ack && ack[1]["ids"] == [notice["id"]], "only the fully displayed unlock is acknowledged")
    achievements.receive({ "unlocks" => [notice] })
    assert(achievements.instance_variable_get(:@queue).empty?, "retried notice does not reappear")
    achievements.tick(base + achievements::DISPLAY_SECONDS + 0.03)
    assert(connection.sent.count { |event, _| event == "achievement_displayed" } == 2, "lost display acknowledgement is retried")
    interrupted = notice.merge("id" => "1454e381-e8fd-4776-80a7-8a26fc97aac8")
    achievements.receive({ "unlocks" => [interrupted] })
    achievements.tick(base + 10)
    window = achievements.instance_variable_get(:@window)
    world.instance_variable_set(:@loaded, false)
    achievements.tick(base + 10.1)
    assert(window.disposed? && achievements.instance_variable_get(:@queue).length == 1, "reset retains interrupted real notices")
    assert(!achievements.preview(notice), "title-screen previews are rejected")
    world.instance_variable_set(:@loaded, true)
    achievements.tick(base + 11)
    achievements.tick(base + 11 + achievements::DISPLAY_SECONDS + 0.01)
    achievements.tick(base + 11 + achievements::DISPLAY_SECONDS + 0.02)
    count = connection.sent.count { |event, _| event == "achievement_displayed" }
    assert(achievements.preview(notice), "shared sample can be previewed without earning it")
    assert(!achievements.preview(notice), "repeated preview clicks are bounded")
    achievements.tick(base + 20)
    achievements.tick(base + 20 + achievements::DISPLAY_SECONDS + 0.01)
    achievements.tick(base + 20 + achievements::DISPLAY_SECONDS + 0.02)
    assert(connection.sent.count { |event, _| event == "achievement_displayed" } == count, "previews never acknowledge or create real unlocks")
    assert(achievements.preview(notice), "preview remains repeatable after animation completes")
    achievements.tick(base + 30)
    world.instance_variable_set(:@loaded, false)
    achievements.tick(base + 30.1)
    assert(achievements.instance_variable_get(:@queue).empty?, "interrupted previews are discarded")
    world.instance_variable_set(:@loaded, true)
    malformed = notice.merge("id" => "cc93d540-9561-46b7-b373-7911d939df64", "icon" => { "width" => 65, "height" => 64, "pixels" => "" })
    achievements.receive({ "unlocks" => [malformed] })
    assert(achievements.instance_variable_get(:@queue).empty?, "oversized or malformed icons are rejected without acknowledgement")
    long_notice = notice.merge("name" => "An unusually long achievement title that still has to fit inside this small notification")
    assert(achievements.preview(long_notice), "long names are accepted for compact layout")
    achievements.tick(base + 40)
    assert(achievements.instance_variable_get(:@window), "long names render without overflowing or failing")
    achievements.tick(base + 40 + achievements::SLIDE_SECONDS + 0.01)
    if $ironmon_achievement_capture_path
      capture = Graphics.snap_to_bitmap
      capture.to_file($ironmon_achievement_capture_path.sub(/\.png\z/, "-long-text.png"))
      capture.dispose
    end
    legacy = achievements.decode_notice(notice.merge("condition" => "Older tracker description"))
    assert(legacy && !legacy.key?("condition"), "older packets with descriptions retain only the title and artwork")
    world.instance_variable_set(:@loaded, false)
    achievements.tick(base + 41)
    world.instance_variable_set(:@loaded, true)
    route = Ironmon::TrackerConnection.allocate
    response = nil
    route.define_singleton_method(:success_response) { |id, payload, *_| { "request_id" => id, "payload" => payload } }
    route.define_singleton_method(:queue_message) { |message| response = message }
    route.send(:handle_request, { "request_id" => "preview-runtime-test", "command" => "achievement_preview", "payload" => notice })
    assert(response && response["payload"]["queued"] == true, "real tracker request routing accepts a harmless preview")
    assert(achievements.instance_variable_get(:@known).length == 2 && achievements.instance_variable_get(:@displayed).empty?, "request previews never enter real unlock acknowledgement state")
    return { "status" => "passed", "native_rgba" => true, "alpha" => true, "slide_animation" => true, "display_acknowledgement" => true, "duplicate_delivery" => true, "reset_interruption" => true, "preview_without_unlock" => true, "malformed_icon_bounds" => true }
  ensure
    probe.dispose if probe && !probe.disposed?
    capture.dispose if capture && !capture.disposed?
    window = achievements.instance_variable_get(:@window) if achievements
    window.dispose if window && !window.disposed?
    prior.each { |key, value| achievements.instance_variable_set(key, value) } if prior
    world.instance_variable_set(:@loaded, previous_loaded) if world
    Ironmon.define_singleton_method(:tracker_connection, previous_connection) if previous_connection
  end
end
