module IronmonSpeedDiagnosticsTests
  def self.assert(condition, message)
    raise "Speed diagnostics validation failed: #{message}" if !condition
  end

  def self.wait_for(queue)
    deadline = Process.clock_gettime(Process::CLOCK_MONOTONIC) + 2.0
    sleep(0.001) while queue.empty? && Process.clock_gettime(Process::CLOCK_MONOTONIC) < deadline
    assert(!queue.empty?, "the owned diagnostic writer started")
    return queue.pop
  end

  def self.run
    service = Ironmon::SpeedDiagnostics
    output = File.join($ironmon_speed_up_acceptance_output, "diagnostic-test")
    original_directory = service.method(:directory)
    original_append = service.method(:append_record)
    assert(service.flush, "preexisting diagnostics drain before the test")
    main_thread = Thread.current.object_id
    service.define_singleton_method(:directory) { output }
    worker_before = service.instance_variable_get(:@worker)
    allocations_before = GC.stat(:total_allocated_objects)
    10_000.times { service.record(:between_updates, 0.001) }
    allocations = GC.stat(:total_allocated_objects) - allocations_before
    assert(service.instance_variable_get(:@worker).equal?(worker_before), "normal updates create no logger thread")
    assert(!File.file?(service.path), "normal updates perform no diagnostic file writes")
    assert(allocations < 50, "normal updates avoid per-update diagnostic allocations")

    entered = Queue.new
    release = Queue.new
    writer_threads = []
    held = true
    service.define_singleton_method(:append_record) do |event|
      writer_threads << Thread.current.object_id
      if held
        entered << true
        release.pop
      end
      original_append.call(event)
    end
    service.record(:native_graphics, 0.125)
    wait_for(entered)
    100.times do
      service.record(:collection_flush, 0.2, {
        :multiplier => 10, :in_battle => true, :gc_count => 7,
        :hook => Ironmon.graphics_update_hook_names.first, :source => "006_PokeBattle_Scene.rb:19:pbGraphicsUpdate",
        :pokemon => "PRIVATE", :run_id => "PRIVATE", :native_rate => Float::INFINITY })
    end
    assert(service.instance_variable_get(:@queue).length == service::MAXIMUM_QUEUE, "diagnostic backlog stays bounded")
    held = false
    release << true
    assert(service.flush, "bounded diagnostics finish writing")
    assert(writer_threads.all? { |thread| thread != main_thread }, "diagnostic writes stay off the game thread")
    rows = File.readlines(service.path).map { |line| Ironmon::GameplayCollection.decode(line) }
    assert(rows.length == 65, "the queue retains only its bounded pending records")
    assert(rows.last["component"] == "collection_flush" && rows.last["multiplier"] == 10,
      "slow diagnostics include the component and permitted numeric context")
    assert(rows.last["hook"] == Ironmon.graphics_update_hook_names.first.to_s &&
      rows.last["source"] == "006_PokeBattle_Scene.rb:19:pbGraphicsUpdate",
      "diagnostics identify registered callbacks and source locations without full paths")
    assert(rows.all? { |row| !row.values.include?("PRIVATE") && !row.key?("run_id") && !row.key?("pokemon") },
      "diagnostics omit identifiers and arbitrary caller data")
    assert(!rows.last.key?("native_rate"), "nonfinite diagnostic context cannot produce invalid JSON")

    File.binwrite(service.path, "x" * service::MAXIMUM_BYTES)
    service.record(:graphics_hooks, 0.15)
    assert(service.flush, "file-boundary diagnostics finish writing")
    assert(File.size(service.path) <= service::MAXIMUM_BYTES, "the diagnostic file remains bounded")
    assert(Ironmon::GameplayCollection.decode(File.readlines(service.path).last)["component"] == "graphics_hooks", "a full file retains valid newest diagnostics")

    service.define_singleton_method(:append_record) { |_event| raise IOError, "test diagnostic write failure" }
    assert(service.record(:input_update, 0.12) && service.flush, "diagnostic storage failures never escape into gameplay")
    assert(service.close, "the owned diagnostic writer can stop")
    service.instance_variable_set(:@closed, false)
    service.define_singleton_method(:append_record, original_append)
    assert(service.record(:input_update, 0.12) && service.flush,
      "a stopped diagnostic worker is restarted for a later session")
    original_hooks = Ironmon.instance_variable_get(:@graphics_update_hooks)
    Ironmon.instance_variable_set(:@graphics_update_hooks, [
      { :name => :diagnostic_slow_hook, :callback => proc { sleep(0.12); :hook_result } }
    ])
    assert(Ironmon.run_graphics_update_hooks == :hook_result && service.flush,
      "timing a slow graphics callback preserves its return value")
    last = Ironmon::GameplayCollection.decode(File.readlines(service.path).last)
    assert(last["component"] == "graphics_hook" && last["hook"] == "diagnostic_slow_hook",
      "the runtime identifies the individual callback responsible for a stall")
    puts "Speed diagnostics: threshold, allocation-free normal updates, bounded background queue/file, privacy, and storage failure passed"
  ensure
    Ironmon.instance_variable_set(:@graphics_update_hooks, original_hooks) if original_hooks
    held = false
    release << true if release
    service.close if service
    if service
      service.define_singleton_method(:directory, original_directory) if original_directory
      service.define_singleton_method(:append_record, original_append) if original_append
      service.instance_variable_set(:@worker, nil)
      service.instance_variable_set(:@closed, false)
    end
  end
end

IronmonSpeedDiagnosticsTests.run
