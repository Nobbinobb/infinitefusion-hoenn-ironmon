module IronmonStartupTransportTests
  class << self
    attr_accessor :loading, :messages, :normal_ticks, :handshake_mutations
  end

  def self.assert(condition, message)
    raise "Startup transport failed: #{message}" if !condition
  end

  def self.event(name, payload = {})
    return { "schema_version" => Ironmon::TRACKER_SCHEMA_VERSION,
      "type" => "event", "event" => name, "payload" => payload }
  end

  def self.write(connection, peer, messages)
    peer.write(messages.map { |message| Ironmon.tracker_json_generate(message) + "\n" }.join)
    socket = connection.instance_variable_get(:@socket)
    assert(IO.select([socket], nil, nil, 1.0), "fixture packets reach the owned loading socket")
  end

  def self.with_connection
    server = TCPServer.new(Ironmon::TRACKER_HOST, 0)
    client = TCPSocket.new(Ironmon::TRACKER_HOST, server.addr[1])
    peer = server.accept
    connection = Ironmon::TrackerConnection.new
    connection.instance_variable_set(:@state, :connected)
    connection.instance_variable_set(:@socket, client)
    connection.instance_variable_set(:@session_ready, true)
    connection.instance_variable_set(:@wardrobe_protocol_version, 1)
    connection.instance_variable_set(:@wardrobe_achievement_rewards_version, 1)
    yield connection, peer
  ensure
    connection.send(:disconnect) if connection
    [peer, client, server].each { |socket| socket.close if socket && !socket.closed? }
  end

  def self.test_quarantine_and_replay
    self.loading = true
    self.messages = []
    self.normal_ticks = 0
    with_connection do |connection, peer|
      connection.instance_variable_set(:@last_heartbeat, 0.0)
      Ironmon.receive_tracker_controls({ "enabled" => true,
        "managed" => ["confirm"], "pressed" => ["confirm"] })
      write(connection, peer, [event("tracker_heartbeat", { "session_id" => "foreign" })])
      connection.update_loading
      assert(connection.instance_variable_get(:@last_heartbeat) == 0.0,
        "foreign-session heartbeat is not authenticated")
      request = { "schema_version" => Ironmon::TRACKER_SCHEMA_VERSION,
        "type" => "request", "request_id" => "startup-settings", "command" => "update_settings",
        "payload" => { "automatic_reset" => false } }
      write(connection, peer, [
        event("tracker_heartbeat", { "session_id" => Ironmon.game_session_id }),
        event("controls_state", { "enabled" => true, "managed" => ["confirm"], "pressed" => ["confirm"] }),
        event("achievement_unlocks", { "id" => "first" }), request,
        event("wardrobe_achievement_rewards", { "id" => "third" })
      ])
      connection.update
      assert(connection.instance_variable_get(:@last_heartbeat) > 0,
        "matching-session heartbeat remains live during loading")
      assert(!Ironmon.tracker_controls_active?, "loading controls are discarded instead of replayed")
      assert(messages.empty? && connection.automatic_reset?, "noninput messages cannot mutate gameplay during loading")
      assert(normal_ticks == 0, "loading update skips collection and reward retry work")
      assert(connection.instance_variable_get(:@startup_messages).length == 3,
        "rewards and settings are retained while controls and heartbeats are consumed")
      self.loading = false
      connection.update
      assert(messages == ["first", "settings", "third"], "first normal update replays all messages in wire order")
      assert(!connection.automatic_reset?, "retained settings apply after loading")
      assert(connection.instance_variable_get(:@startup_messages).empty? &&
        connection.instance_variable_get(:@startup_message_bytes) == 0,
        "normal replay releases queued messages and byte accounting")
      assert(normal_ticks > 0, "normal collection and reward retries resume after loading")
    end
  end

  def self.test_handshake
    self.loading = true
    self.messages = []
    self.handshake_mutations = 0
    with_connection do |connection, peer|
      connection.instance_variable_set(:@session_ready, false)
      payload = { "management_protocol_version" => Ironmon::GAME_MANAGEMENT_PROTOCOL,
        "session_id" => Ironmon.game_session_id, "game_root" => File.expand_path("."),
        "automatic_reset" => false, "toggle_speed" => Ironmon::ToggleSpeed.configuration }
      write(connection, peer, [event("tracker_connected", payload.merge("session_id" => "foreign"))])
      connection.update_loading
      assert(!connection.session_ready?, "foreign handshake cannot establish loading-session readiness")
      write(connection, peer, [event("tracker_connected", payload)])
      connection.update_loading
      assert(connection.session_ready?, "matching transport handshake can establish readiness while loading")
      assert(connection.automatic_reset? && handshake_mutations == 0,
        "loading handshake defers settings, starter rules and preparation callbacks")
      self.loading = false
      connection.update
      assert(!connection.automatic_reset? && handshake_mutations > 0,
        "full handshake behavior resumes in normal replay")
    end
  end

  def self.test_bounds_and_disconnect
    self.loading = true
    self.messages = []
    with_connection do |connection, peer|
      write(connection, peer, Array.new(Ironmon::StartupTransport::MAXIMUM_QUEUED_MESSAGES + 1) {
        event("achievement_unlocks", { "id" => "bounded" }) })
      connection.update_loading
      error = connection.instance_variable_get(:@last_error).to_s
      assert(!connection.connected? && error.include?("startup loading queue"),
        "queue-count overflow disconnects with an explicit error")
      assert(connection.instance_variable_get(:@startup_messages).empty? &&
        connection.instance_variable_get(:@startup_message_bytes) == 0,
        "disconnect clears queued messages and byte accounting")
      assert(messages.empty?, "overflow never dispatches queued rewards during loading")
    end
    with_connection do |connection, _peer|
      large = event("achievement_unlocks", { "id" => "x" * (Ironmon::StartupTransport::MAXIMUM_QUEUED_BYTES / 2) })
      connection.send(:handle_message, large)
      rejected = false
      begin
        connection.send(:handle_message, large)
      rescue StandardError => error
        rejected = error.message.include?("startup loading queue")
      end
      assert(rejected, "serialized byte limit applies before the message-count limit")
      assert(connection.instance_variable_get(:@startup_message_bytes) <= Ironmon::StartupTransport::MAXIMUM_QUEUED_BYTES,
        "rejected message does not grow retained queue bytes")
      connection.send(:disconnect)
      assert(connection.instance_variable_get(:@startup_messages).empty?, "explicit disconnect clears retained messages")
    end
  end

  def self.test_progress_without_run_context
    original_global = $PokemonGlobal
    originals = [:ensure_tracker_run_id, :next_tracker_sequence].map { |name| [name, Ironmon.method(name)] }.to_h
    originals.each_key do |name|
      Ironmon.define_singleton_method(name) { raise "Startup progress touched an unloaded run" }
    end
    context = PokemonGlobalMetadata.new
    context.ironmon_run_id = nil
    context.ironmon_tracker_sequence = 17
    $PokemonGlobal = context
    with_connection do |connection, peer|
      [context, nil].each_with_index do |global, index|
        $PokemonGlobal = global
        payload = { "preparation_id" => "startup-envelope-#{index}", "status" => "running", "phase" => "loading_save" }
        assert(connection.send_startup_preparation_progress(payload),
          "startup progress can be flushed before a save or run exists")
        assert(IO.select([peer], nil, nil, 1.0), "the initial progress event reaches the tracker before loading work")
        envelope = connection.send(:normalize_json, HTTPLite::JSON.parse(peer.gets))
        assert(envelope["event"] == "startup_preparation_progress" && envelope["payload"] == payload &&
          envelope["run_id"] == "" && envelope["battle_id"].nil? && envelope["sequence"] == 0,
          "startup progress has an independent event envelope and stage-only payload")
      end
      assert(context.ironmon_run_id.nil? && context.ironmon_tracker_sequence == 17,
        "preload progress cannot assign a run ID or increment the saved event sequence")
    end
  ensure
    $PokemonGlobal = original_global
    originals.each { |name, callable| Ironmon.define_singleton_method(name, callable) } if originals
  end

  def self.run
    raise "Startup loading preparation module was not loaded" if !defined?(Ironmon::StartupPreparation)
    originals = []
    replace = proc do |owner, name, body|
      originals << [owner, name, owner.method(name)]
      owner.define_singleton_method(name, &body)
    end
    replace.call(Ironmon::StartupPreparation, :active?, proc { IronmonStartupTransportTests.loading })
    replace.call(Ironmon, :refresh_tracker_starter_rules, proc {
      IronmonStartupTransportTests.handshake_mutations = IronmonStartupTransportTests.handshake_mutations.to_i + 1
      IronmonStartupTransportTests.messages << "settings"
    })
    replace.call(Ironmon, :prepare_tracker_rival_fusion, proc { })
    replace.call(Ironmon, :reset_tracker_obtainability_worker_failures, proc { })
    replace.call(Ironmon, :disable_tracker_auto_revive, proc { })
    replace.call(Ironmon::Achievements, :receive, proc { |payload| IronmonStartupTransportTests.messages << payload["id"] })
    replace.call(Ironmon::Cosmetics, :receive_achievement_rewards, proc { |payload, _connection| IronmonStartupTransportTests.messages << payload["id"] })
    replace.call(Ironmon::Cosmetics, :retry_achievement_rewards, proc { |_connection|
      IronmonStartupTransportTests.normal_ticks = IronmonStartupTransportTests.normal_ticks.to_i + 1
    })
    replace.call(Ironmon::GameplayCollection, :transport_tick, proc { |_connection|
      IronmonStartupTransportTests.normal_ticks = IronmonStartupTransportTests.normal_ticks.to_i + 1
    })
    test_quarantine_and_replay
    test_handshake
    test_bounds_and_disconnect
    test_progress_without_run_context
    puts "Startup transport: bounded loading IO, heartbeat authentication, input quarantine, ordered reward/request replay and overflow cleanup passed"
  ensure
    originals.reverse_each { |owner, name, method| owner.define_singleton_method(name, method) } if originals
    Ironmon.clear_tracker_controls
  end
end

IronmonStartupTransportTests.run
