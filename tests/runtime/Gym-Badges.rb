module IronmonGymBadgeRuntimeTests
  def self.assert(condition, message)
    raise "Gym badge test failed: #{message}" if !condition
  end

  def self.run
    original_trainer = $Trainer
    original_badges = Ironmon.instance_variable_get(:@tracker_badges)
    originals = {}
    [:active?, :tracker_connection, :tracker_current_state].each do |name|
      originals[name] = Ironmon.method(name)
    end
    active = true
    events = []
    connection = Object.new
    connection.define_singleton_method(:send_event) do |name, payload|
      events << [name, payload]
    end
    Ironmon.define_singleton_method(:active?) { active }
    Ironmon.define_singleton_method(:tracker_connection) { connection }
    Ironmon.define_singleton_method(:tracker_current_state) do
      { "badges" => tracker_badge_snapshot }
    end
    $Trainer = nil
    assert(Ironmon.tracker_badge_snapshot.nil?, "missing trainer is unknown")
    $Trainer = Struct.new(:badges).new([true, false, true, nil, false, false, false, true, true])
    expected = [true, false, true, false, false, false, false, true]
    assert(Ironmon.tracker_badge_snapshot == expected, "eight flags preserve nonsequential ownership")
    Ironmon.instance_variable_set(:@tracker_badges, nil)
    Ironmon.update_tracker_badges
    Ironmon.update_tracker_badges
    assert(events.empty?, "unchanged ownership emits no event")
    $Trainer.badges[1] = true
    Ironmon.update_tracker_badges
    assert(events.length == 1, "earning a badge emits one event")
    assert(events.last[0] == "badges_changed", "stable event identifier")
    assert(events.last[1]["badges"][1], "event contains updated ownership")
    Ironmon.update_tracker_badges
    assert(events.length == 1, "update is not repeated each frame")
    $Trainer.badges[1] = false
    Ironmon.update_tracker_badges
    assert(events.length == 2 && !events.last[1]["badges"][1], "ownership removal is reflected")
    active = false
    Ironmon.update_tracker_badges
    assert(Ironmon.instance_variable_get(:@tracker_badges).nil?, "inactive mode clears cached ownership")
    assert(events.length == 2, "inactive mode emits no badge event")
    File.binwrite($ironmon_gym_badge_test_output_path, "Gym badge tests passed: 10 assertions")
  ensure
    $Trainer = original_trainer
    Ironmon.instance_variable_set(:@tracker_badges, original_badges)
    originals.each { |name, method| Ironmon.define_singleton_method(name, method) }
  end
end

IronmonGymBadgeRuntimeTests.run
