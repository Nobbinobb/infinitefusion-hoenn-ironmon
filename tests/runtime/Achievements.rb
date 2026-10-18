module IronmonAchievementRuntimeTests
  class Connection
    attr_reader :sent

    def initialize
      @sent = []
    end

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
    previous_system, previous_temp = $PokemonSystem, $PokemonTemp
    $PokemonSystem ||= PokemonSystem.new
    $PokemonTemp ||= PokemonTemp.new
    achievements = Ironmon::Achievements
    world = Ironmon::GameplayCollection::World
    previous_loaded = world.instance_variable_get(:@loaded)
    previous_connection = Ironmon.method(:tracker_connection)
    connection = Connection.new
    Ironmon.define_singleton_method(:tracker_connection) { connection }
    world.instance_variable_set(:@loaded, true)
    variables = %i[@queue @known @displayed @window @current @shown_at @last_poll]
    prior = variables.to_h { |key| [key, achievements.instance_variable_get(key)] }
    variables.each { |key| achievements.instance_variable_set(key, nil) }
    notice = { "id" => "5d724f04-95a9-461e-a993-fcfa37fcbb74", "name" => "Achievement 001", "condition" => "This deliberately long placeholder condition verifies that the earned notification fits inside the game viewport and wraps without stopping the battle." }
    achievements.receive({ "unlocks" => [notice, notice, { "id" => "invalid" }] })
    assert(achievements.instance_variable_get(:@queue).length == 1, "duplicate unlocks must queue once")
    achievements.tick
    window = achievements.instance_variable_get(:@window)
    assert(window && !window.disposed?, "native notification window was created: #{achievements.instance_variable_get(:@last_error)}")
    assert(window.x >= 0 && window.x + window.width <= Graphics.width, "notification fits horizontal viewport")
    assert(window.y >= 0 && window.y + window.height <= Graphics.height, "notification fits vertical viewport")
    assert(connection.sent.none? { |event, _| event == "achievement_displayed" }, "queued notice must not be acknowledged before display")
    Graphics.update
    achievements.instance_variable_set(:@shown_at, Ironmon::GameplayCollection.clock - achievements::DISPLAY_SECONDS - 0.01)
    achievements.tick
    assert(window.disposed?, "completed notification releases its native window")
    achievements.tick
    ack = connection.sent.find { |event, _| event == "achievement_displayed" }
    assert(ack && ack[1]["ids"] == [notice["id"]], "only actually displayed identity was acknowledged")
    achievements.receive({ "unlocks" => [notice] })
    assert(achievements.instance_variable_get(:@queue).empty?, "retried displayed notice does not reappear in this process")
    achievements.tick
    assert(connection.sent.count { |event, _| event == "achievement_displayed" } == 2, "lost display acknowledgement is safely retried")
    interrupted = notice.merge("id" => "1454e381-e8fd-4776-80a7-8a26fc97aac8")
    achievements.receive({ "unlocks" => [interrupted] })
    achievements.tick
    window = achievements.instance_variable_get(:@window)
    world.instance_variable_set(:@loaded, false)
    achievements.tick
    assert(window.disposed? && achievements.instance_variable_get(:@queue).length == 1, "a reset hides the window and retains its undisplayed notice")
    assert(connection.sent.count { |event, _| event == "achievement_displayed" } == 2, "an interrupted display is not acknowledged")
    world.instance_variable_set(:@loaded, true)
    achievements.tick
    assert(achievements.instance_variable_get(:@window), "interrupted notice resumes after the save loads")
    return { "status" => "passed", "native_window" => true, "display_acknowledgement" => true, "duplicate_delivery" => true, "reset_interruption" => true }
  ensure
    window = achievements.instance_variable_get(:@window) if achievements
    window.dispose if window && !window.disposed?
    prior.each { |key, value| achievements.instance_variable_set(key, value) } if prior
    world.instance_variable_set(:@loaded, previous_loaded) if world
    Ironmon.define_singleton_method(:tracker_connection, previous_connection) if previous_connection
    $PokemonSystem, $PokemonTemp = previous_system, previous_temp
  end
end
