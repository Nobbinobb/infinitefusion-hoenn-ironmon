module IronmonCollectionJournalRuntimeTests
  OUTPUT_PATH = $ironmon_collection_journal_test_output_path.to_s
  SCRATCH_PATH = File.expand_path($ironmon_collection_journal_test_scratch_path.to_s)
  RESULTS = []
  APPEND_LATENCIES = []

  class StalledPublicationJournal < Ironmon::GameplayCollection::Journal
    attr_accessor :publication_entered, :publication_release, :stall_next_publication

    def write_state(snapshot = nil)
      if @stall_next_publication
        @stall_next_publication = false
        @publication_entered << true
        @publication_release.pop
      end
      super(snapshot)
    end
  end

  def self.assert(condition, message)
    raise "Collection journal test failed: #{message}" if !condition
  end

  def self.event(number)
    return {
      "type" => "clock.interval_closed", "attempt_id" => "fixture-attempt", "branch_id" => "fixture-branch",
      "battle_id" => nil, "occurred_at_us" => number.to_s, "context" => {},
      "payload" => { "duration_seconds" => number, "description" => "quote\" slash\\ newline\n Pok\u00e9mon", "truth" => true, "nothing" => nil }
    }
  end

  def self.directory(name)
    path = File.expand_path(File.join(SCRATCH_PATH, name))
    assert(path.start_with?(SCRATCH_PATH + File::SEPARATOR), "fixture path stays beneath scratch root")
    return path
  end

  def self.ack(journal, batch, incarnation = "a" * 32)
    return {
      "profile_id" => journal.profile_id, "epoch_id" => journal.epoch_id,
      "stream_id" => batch["stream_id"], "durable_prefix" => batch["last_sequence"],
      "store_incarnation_id" => incarnation
    }
  end

  def self.wait_for(message)
    deadline = Ironmon::GameplayCollection.clock + 3.0
    until yield
      raise "Collection journal test timed out: #{message}" if Ironmon::GameplayCollection.clock > deadline
      sleep(0.01)
    end
  end

  def self.binding(path)
    candidates = ["binding-a.json", "binding-b.json"].filter_map do |name|
      file = File.join(path, name)
      next if !File.file?(file)
      envelope = Ironmon::GameplayCollection.decode(File.binread(file))
      Ironmon::GameplayCollection.decode(envelope["payload"])
    end
    return candidates.max_by { |state| state["generation"] }
  end

  def self.make_open(path, id)
    state = binding(path)
    state["streams"].find { |stream| stream["stream_id"] == id }["closed"] = false
    state["generation"] += 10
    payload = Ironmon::GameplayCollection.encode(state)
    bytes = Ironmon::GameplayCollection.encode({ "payload" => payload, "sha256" => Digest::SHA256.hexdigest(payload) })
    ["binding-a.json", "binding-b.json"].each { |name| File.binwrite(File.join(path, name), bytes) }
  end

  def self.run(name)
    yield
    RESULTS << name
  end

  def self.execute
    collection = Ironmon::GameplayCollection
    run("actual JSON decoder preserves escaping, nulls, booleans and wide sequence strings") do
      original = event(7)
      original["sequence"] = "18446744073709551615"
      original["payload"]["wide_actual"] = 2 ** 100
      assert(collection.decode(collection.encode(original)) == original, "real encode/decode is exact")
    end

    run("collection encoder preserves legacy bytes, key collisions, Unicode and numeric boundaries") do
      controls = (0..31).to_a.pack("C*").force_encoding(Encoding::UTF_8)
      unicode = [0x7f, 0x80, 0x85, 0x2028, 0x2029, 0x1f680, 0x10ffff].pack("U*")
      examples = [nil, true, false, 0, -1, -(2 ** 63), 2 ** 64 - 1, 2 ** 200, 0.0, -0.0, 1.0e-300, 1.0e300,
        Float::INFINITY, -Float::INFINITY, Float::NAN, :TACKLE, controls + unicode + "quote\" slash\\ line\n",
        (0..255).to_a.pack("C*"), (0..127).to_a.pack("C*"), { "move_id".b => "TACKLE".b, "message".b => "quote\" slash\\ line\n".b },
        "Pok\u00e9mon".encode(Encoding::UTF_16LE), "caf\u00e9".encode(Encoding::ISO_8859_1),
        { :same => "old", "same" => "new", "nil" => nil, "nested" => [{ "iv" => 31, "flag" => false }, Object.new] }]
      examples.each do |value|
        expected = Ironmon.tracker_json_generate(collection.stringify(value)).encode(Encoding::UTF_8)
        actual = collection.encode(value)
        assert(actual.b == expected.b && actual.encoding == Encoding::UTF_8, "optimized encoding matches original bytes and output encoding")
        assert(collection.decode(actual) == collection.decode(expected), "all exact values survive the decoder")
      end
      150.times do |number|
        value = { "name" => "mon-#{number}#{unicode}", "values" => [number ** 5, number.odd?, nil, "move\t#{number}", { :slot => number % 4 }] }
        assert(collection.encode(value).b == Ironmon.tracker_json_generate(collection.stringify(value)).encode(Encoding::UTF_8).b, "varied nested records match byte for byte")
      end
    end

    run("capture snapshots nested mutable event data before the caller changes it") do
      path = directory("immutable-capture")
      journal = collection::Journal.new(path)
      value = event(9)
      value["payload"]["pokemon"] = { "name" => "Original", "moves" => [{ "id" => "TACKLE", "pp" => 35 }], "iv" => { "hp" => 31 } }
      journal.append(value)
      expected = collection.decode(collection.encode(value))
      value["payload"]["pokemon"]["name"].replace("Changed")
      value["payload"]["pokemon"]["moves"][0]["id"].replace("GROWL")
      value["payload"]["pokemon"]["moves"] << { "id" => "SURF", "pp" => 15 }
      value["payload"]["pokemon"]["iv"]["hp"] = 0
      value["payload"]["description"].clear
      value["context"]["later"] = true
      journal.fence
      assert(collection.decode(journal.next_batch["json"])["events"].first == expected, "queued bytes retain the exact original mutable values")
      journal.close
      reopened = collection::Journal.new(path)
      assert(collection.decode(reopened.next_batch["json"])["events"].first == expected, "restart retains the same capture-time snapshot")
      reopened.close
    end

    run("UUID generation does not consume gameplay RNG") do
      srand(47931)
      expected = rand(1_000_000)
      srand(47931)
      20.times { collection.uuid }
      assert(rand(1_000_000) == expected, "OS UUID randomness does not advance Kernel.rand")
    end

    run("fence, duplicate ACK, incarnation race and pending-only segment deletion") do
      journal = collection::Journal.new(directory("basic"))
      10.times { |number| journal.append(event(number)) }
      journal.fence
      assert(journal.metrics["durable_sequence"] == "10", "fence publishes all appended records")
      batch = journal.next_batch
      events = collection.decode(batch["json"])["events"]
      assert(events.length == 10 && events[-1]["sequence"] == "10", "all pending records are transmitted")
      assert(journal.acknowledge(ack(journal, batch)), "first ACK accepted")
      begin
        journal.acknowledge(ack(journal, batch, "b" * 32))
        raise "The queued conflicting incarnation was accepted"
      rescue RuntimeError => error
        assert(error.message.include?("incarnation changed"), "conflicting queued incarnation fails closed")
      end
      assert(journal.acknowledge(ack(journal, batch)), "duplicate ACK is idempotent")
      wait_for("durable ACK compaction") { journal.metrics["pending_events"] == 0 && Dir.glob(File.join(directory("basic"), "streams", "*", "*.journal")).empty? }
      journal.append(event(11))
      journal.fence
      assert(collection.decode(journal.next_batch["json"])["events"][0]["sequence"] == "11", "writer continues after deleting acknowledged active segment")
      journal.close
    end

    run("literal Windows paths recover the exact unacknowledged tail after restart") do
      path = directory("windows path [literal]")
      native_path = File::ALT_SEPARATOR == "\\" ? path.tr("/", "\\") : path
      first = collection::Journal.new(native_path)
      10.times { |number| first.append(event(number)) }
      first.fence
      batch = first.next_batch
      prefix = batch.merge("last_sequence" => "5")
      first.acknowledge(ack(first, prefix))
      wait_for("partial Windows-path ACK") { first.acknowledged?(batch["stream_id"], "5") }
      profile = first.profile_id
      first.close
      reopened = collection::Journal.new(native_path)
      recovered = reopened.next_batch
      records = collection.decode(recovered["json"])["events"]
      assert(reopened.profile_id == profile, "restart preserves the existing collection profile")
      assert(recovered["stream_id"] == batch["stream_id"], "restart retains the predecessor stream identity")
      assert(records.map { |record| record["sequence"] } == %w[6 7 8 9 10], "restart replays only the five unacknowledged records")
      reopened.acknowledge(ack(reopened, recovered))
      wait_for("recovered Windows-path ACK") { reopened.metrics["pending_events"] == 0 }
      reopened.append(event(11))
      reopened.fence
      assert(collection.decode(reopened.next_batch["json"])["events"][0]["payload"]["duration_seconds"] == 11, "new gameplay is captured after recovery")
      reopened.close
    end

    run("normal close and predecessor replay preserve pending input") do
      path = directory("normal-close")
      first = collection::Journal.new(path)
      5.times { |number| first.append(event(number)) }
      first.close
      old_id = first.stream_id
      reopened = collection::Journal.new(path)
      assert(reopened.recovery_context["retirement_kind"] == "closed", "normal closure is distinguishable")
      batch = reopened.next_batch
      assert(batch["stream_id"] == old_id && batch["last_sequence"] == "5", "pending predecessor is offered before new input")
      reopened.append(event(6))
      reopened.fence
      reopened.acknowledge(ack(reopened, batch))
      wait_for("predecessor acknowledgement") { reopened.next_batch["stream_id"] == reopened.stream_id }
      next_batch = collection.decode(reopened.next_batch["json"])
      assert(next_batch["predecessor_stream_id"] == old_id && next_batch["predecessor_final_sequence"] == "5", "new stream retains exact predecessor barrier")
      reopened.close
    end

    run("old raw records and new compressed records retain the same pending facts") do
      path = directory("mixed-record-codecs")
      old = collection::Journal.new(path, compress: false)
      4.times { |number| old.append(event(number)) }
      old.close
      upgraded = collection::Journal.new(path)
      old_batch = upgraded.next_batch
      assert(collection.decode(old_batch["json"])["events"].length == 4, "legacy pending input survives compression enablement")
      upgraded.acknowledge(ack(upgraded, old_batch))
      wait_for("legacy prefix published") { upgraded.acknowledged?(old_batch["stream_id"], old_batch["last_sequence"]) }
      4.times { |number| upgraded.append(event(number + 10)) }
      upgraded.close
      final = collection::Journal.new(path)
      pending = collection.decode(final.next_batch["json"])["events"]
      assert(pending.map { |entry| entry["payload"]["duration_seconds"] } == [10, 11, 12, 13], "compressed predecessor replays exactly after restart")
      stored = Dir.glob(File.join(path, "streams", "*", "*.journal")).sum { |file| File.size(file) }
      assert(stored < pending.sum { |entry| collection.encode(entry).bytesize + collection::Journal::HEADER_BYTES }, "new journal records are smaller")
      prefix = collection::Journal::COMPRESSED_RECORD_MAGIC
      malformed = prefix + [1].pack("N") + Zlib::Deflate.deflate("large" * 100)
      begin
        final.decode_record(malformed)
        raise "Oversized expansion was accepted"
      rescue RuntimeError => error
        assert(error.message.include?("declared size"), "compressed journal expansion is bounded")
      end
      final.close
    end

    run("bounded batch preparation cancels safely after its selected prefix is acknowledged") do
      journal = collection::Journal.new(directory("bounded-preparation"))
      30.times { |number| journal.append(event(number)) }
      journal.fence
      partial = journal.start_batch
      assert(journal.advance_batch(partial, max_records: 3, budget_ms: nil).nil? && partial["position"] == 3, "one step reads only its bounded record slice")
      complete = journal.next_batch
      journal.acknowledge(ack(journal, complete))
      wait_for("prefix retired during preparation") { journal.acknowledged?(complete["stream_id"], complete["last_sequence"]) }
      assert(journal.advance_batch(partial).nil? && partial["cancelled"], "already acknowledged selection is discarded before reading removed files")
      assert(journal.start_batch.nil?, "no stale records are offered again")
      journal.append(event(31))
      journal.fence
      assert(journal.next_batch["last_sequence"] == "31", "new collection continues after cancelled preparation")
      journal.close
    end

    run("crash recovery accepts a valid tail and truncates only a torn uncommitted suffix") do
      path = directory("torn-tail")
      first = collection::Journal.new(path)
      first.append(event(1))
      first.close
      id = first.stream_id
      make_open(path, id)
      segment = Dir.glob(File.join(path, "streams", id, "*.journal")).last
      first_stored_bytes = File.binread(segment, 4).unpack("N")[0]
      second = event(2)
      second["sequence"] = "2"
      second["payload"]["sequence"] = "2"
      bytes = collection.encode(second)
      File.open(segment, "ab") do |file|
        file.write([bytes.bytesize].pack("N") + Digest::SHA256.digest(bytes) + bytes.b + "torn".b)
        file.flush
        file.fsync
      end
      reopened = collection::Journal.new(path)
      assert(reopened.recovery_context["retirement_kind"] == "recovered_interrupted", "unclean predecessor is explicit")
      assert(reopened.next_batch["last_sequence"] == "2", "valid recoverable tail retained")
      assert(File.size(segment) == 2 * collection::Journal::HEADER_BYTES + first_stored_bytes + bytes.bytesize, "torn suffix removed without valid records")
      reopened.close
    end

    run("committed corruption fails closed and constructor releases ownership lock") do
      path = directory("committed-corruption")
      first = collection::Journal.new(path)
      first.append(event(1))
      first.close
      segment = Dir.glob(File.join(path, "streams", first.stream_id, "*.journal")).last
      File.open(segment, "r+b") { |file| file.seek(collection::Journal::HEADER_BYTES); file.write("!") }
      begin
        collection::Journal.new(path)
        raise "Damaged committed record was accepted"
      rescue RuntimeError => error
        assert(error.message.include?("Damaged committed"), "committed corruption is not silently truncated")
      end
      lock = File.open(File.join(path, "producer.lock"), "a+b")
      assert(lock.flock(File::LOCK_EX | File::LOCK_NB), "failed constructor releases producer lock")
      lock.close
    end

    run("close failure releases ownership and leaves recovery metadata intact") do
      path = directory("close-error")
      journal = collection::Journal.new(path)
      journal.append(event(1))
      journal.fence
      journal.instance_variable_set(:@error, RuntimeError.new("fixture write failure"))
      begin
        journal.close
        raise "Injected journal error was ignored"
      rescue RuntimeError => error
        assert(error.message == "fixture write failure", "original error propagated")
      end
      reopened = collection::Journal.new(path)
      assert(reopened.next_batch["last_sequence"] == "1", "failed close retains pending input")
      reopened.close
    end

    run("independent runtime contexts survive reopen without becoming activity") do
      path = directory("runtime-context")
      first = collection::Journal.new(path)
      first.bind_runtime_context("attempt", { "attempt_id" => "context-only", "owned_baseline" => true })
      first.close
      reopened = collection::Journal.new(path)
      assert(reopened.runtime_context("attempt")["attempt_id"] == "context-only", "technical identity context survives")
      assert(reopened.next_batch.nil?, "technical context does not invent gameplay records")
      reopened.close
    end

    run("capture does not wait for a blocked background manifest publication") do
      journal = StalledPublicationJournal.new(directory("slow-publication"))
      journal.publication_entered = Queue.new
      journal.publication_release = Queue.new
      journal.stall_next_publication = true
      journal.append(event(1))
      fence_done = false
      fence_thread = Thread.new { journal.fence; fence_done = true }
      journal.publication_entered.pop
      assert(!fence_done, "fence waits for manifest durability")
      started = collection.clock
      journal.append(event(2))
      elapsed = collection.clock - started
      APPEND_LATENCIES << elapsed
      assert(elapsed < 0.05, "append remains below 50 ms while publication is stalled")
      assert(!fence_done, "append did not unblock the durability barrier")
      journal.publication_release << true
      fence_thread.join
      journal.close
      reopened = collection::Journal.new(directory("slow-publication"))
      assert(reopened.next_batch["last_sequence"] == "2", "concurrent append is not lost")
      reopened.close
    end

    run("published ACK frontier waits for durable metadata without blocking capture") do
      journal = StalledPublicationJournal.new(directory("slow-ack"))
      journal.append(event(1))
      journal.fence
      batch = journal.next_batch
      journal.publication_entered = Queue.new
      journal.publication_release = Queue.new
      journal.stall_next_publication = true
      journal.acknowledge(ack(journal, batch))
      journal.publication_entered.pop
      assert(!journal.acknowledged?(batch["stream_id"], "1"), "queued ACK is not published durable frontier")
      started = collection.clock
      journal.append(event(2))
      elapsed = collection.clock - started
      APPEND_LATENCIES << elapsed
      assert(elapsed < 0.05, "append remains below 50 ms while ACK persistence is stalled")
      journal.publication_release << true
      wait_for("published ACK frontier") { journal.acknowledged?(batch["stream_id"], "1") }
      journal.close
      reopened = collection::Journal.new(directory("slow-ack"))
      assert(reopened.next_batch["last_sequence"] == "2", "new capture remains pending after preceding ACK")
      reopened.close
    end

    run("controlled process exit fences pending input without creating an unused profile") do
      untouched = directory("unused-exit")
      collection.directory_override = untouched
      collection.instance_variable_set(:@journal, nil)
      collection.close_at_process_exit
      assert(!File.directory?(untouched), "exit does not initialize a profile")
      path = directory("controlled-exit")
      journal = collection::Journal.new(path)
      journal.append(event(1))
      collection.instance_variable_set(:@journal, journal)
      collection.close_at_process_exit
      collection.instance_variable_set(:@journal, nil)
      reopened = collection::Journal.new(path)
      records = collection.decode(reopened.next_batch["json"])["events"]
      assert(records[-1]["type"] == "collection.stream_closed", "exit writes explicit clean stream closure")
      assert(records.length == 2, "exit fences both pending activity and closure")
      reopened.close
      collection.directory_override = nil
    end

    run("world teardown callback failure cannot skip the final journal fence") do
      path = directory("failed-world-exit")
      journal = collection::Journal.new(path)
      journal.append(event(1))
      collection.instance_variable_set(:@journal, journal)
      temporary_world = !collection.const_defined?(:World, false)
      collection.const_set(:World, Module.new) if temporary_world
      original_leave = collection::World.method(:leave_loaded) if collection::World.respond_to?(:leave_loaded)
      collection::World.define_singleton_method(:leave_loaded) { |*args| raise IOError, "isolated world teardown failure" }
      begin
        collection.close_at_process_exit
        collection.instance_variable_set(:@journal, nil)
        reopened = collection::Journal.new(path)
        records = collection.decode(reopened.next_batch["json"])["events"]
        assert(records.length == 2 && records[-1]["type"] == "collection.stream_closed", "world callback failure still fences pending input and final stream closure")
        reopened.close
      ensure
        collection.send(:remove_const, :World) if temporary_world
        collection::World.define_singleton_method(:leave_loaded, original_leave) if !temporary_world && original_leave
        collection.instance_variable_set(:@disabled, false)
        collection.instance_variable_set(:@last_error, nil)
      end
    end

    File.binwrite(OUTPUT_PATH, collection.encode({ "status" => "passed", "runtime" => RUBY_DESCRIPTION, "cases" => RESULTS, "append_during_stalled_publication_seconds" => APPEND_LATENCIES, "scratch_path" => SCRATCH_PATH }))
  end
end

IronmonCollectionJournalRuntimeTests.execute
