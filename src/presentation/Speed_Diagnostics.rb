module Ironmon
  module SpeedDiagnostics
    THRESHOLD_SECONDS = 0.1
    WORK_THRESHOLD_SECONDS = 0.016
    WORK_INTERVAL_SECONDS = 1.0
    CONTEXT_INTERVAL_SECONDS = 0.25
    SUMMARY_INTERVAL_SECONDS = 5.0
    MAXIMUM_BYTES = 1_048_576
    MAXIMUM_QUEUE = 64
    FILE_NAME = "speed-up-stalls.jsonl"
    COMPONENTS = [:native_graphics, :between_updates, :graphics_hooks, :graphics_hook,
      :collection_flush, :input_update, :calculation_chunk, :calculation_request,
      :calculation_prepare, :speed_selection, :pacing_reconfigure,
      :calculation_state, :frame_summary].freeze
    CONTEXT_COMPONENTS = [:speed_selection, :pacing_reconfigure, :calculation_state].freeze
    DETAIL_KEYS = ["multiplier", "previous_multiplier", "native_rate", "desired_native_rate",
      "base_rate", "target_rate", "in_battle", "gc_count", "gc_count_delta", "gc_time_ms",
      "stage_index", "reconfigurations", "defer_duration_ms", "budget_ms", "processed",
      "remaining", "rows", "cache_hit", "logical_updates", "maximum_gap_ms", "hook_work_ms",
      "maximum_hook_ms", "slow_hooks"].freeze
    PLAYER_FUSION_PHASES = %w[initialized fusion_pool native_pairing target_data prepared
      strength_order strength_pairing strength_validation strength_repair strength_repair_chain
      maximum_range result_index complete].freeze
    PHASES = (%w[prepare_generators prepare_encounters encounter_sources derived_encounter_sources
      starter_sources authored_sources resources normal_graph normal_evolutions
      caught_fusion_transformations transformation_evolutions derived_caught_fusion_transformations
      derived_transformation_evolutions player_fusions tracker_closure_result complete failed] +
      PLAYER_FUSION_PHASES).uniq.freeze
    SOURCE_PATTERN = /\A[A-Za-z0-9_ .-]+\.rb:\d+:[A-Za-z0-9_!?=<> ]+\z/
    TOKEN_PATTERN = /\A[a-z][a-z0-9_]{0,63}\z/
    WRAPPER_SOURCE_PATTERN = /\A(?:\d+_)?(?:Speed_Up(?:_.*)?|Speed_Diagnostics|Speed_Transitions|Runtime_Hooks|Responsive_Input|Tracker_Controls(?:_.*)?)\.rb\z/
    @mutex ||= Mutex.new
    @changed ||= ConditionVariable.new
    @queue ||= []
    @closed = false if @closed.nil?
    @writing = false if @writing.nil?
    @rate_limits ||= {}

    def self.directory
      return File.join(System.data_directory, "performance")
    end

    def self.path
      return File.join(directory, FILE_NAME)
    end

    def self.now
      return Process.clock_gettime(Process::CLOCK_MONOTONIC)
    end

    def self.gc_time_nanoseconds
      return GC.respond_to?(:total_time) ? GC.total_time : 0
    end

    def self.caller_source
      caller_locations(1, 16).each do |entry|
        filename = File.basename(entry.path)
        next if WRAPPER_SOURCE_PATTERN.match?(filename)
        source = "#{filename}:#{entry.lineno}:#{entry.base_label}"
        return source if SOURCE_PATTERN.match?(source)
      end
      return nil
    end

    def self.runtime_details
      details = { "in_battle" => !!($game_temp && $game_temp.in_battle) }
      if defined?(Ironmon::SpeedUp)
        details["multiplier"] = Ironmon::SpeedUp.multiplier
        details["base_rate"] = Ironmon::SpeedUp.base_rate
        details["native_rate"] = Ironmon::SpeedUp::NATIVE_RATE.call
      end
      return details
    end

    def self.record(component, seconds, details = nil)
      return false if seconds < THRESHOLD_SECONDS || !seconds.finite?
      return false if !COMPONENTS.include?(component)
      return enqueue(component, seconds, details, "stall")
    rescue Exception
      return false
    end

    def self.record_work(component, seconds, details = nil)
      return false if !seconds.finite?
      budget = details.is_a?(Hash) ? details["budget_ms"] || details[:budget_ms] : nil
      threshold = WORK_THRESHOLD_SECONDS
      threshold = budget / 1000.0 if budget.is_a?(Numeric) && budget > 0 && budget < threshold * 1000
      return false if seconds < threshold || !COMPONENTS.include?(component)
      return record(component, seconds, details) if seconds >= THRESHOLD_SECONDS
      suppressed, maximum = rate_limit(component, details, WORK_INTERVAL_SECONDS, seconds)
      return false if suppressed.nil?
      return enqueue(component, seconds, details, "budget", suppressed, maximum)
    rescue Exception
      return false
    end

    def self.record_context(component, details = nil)
      return false if !CONTEXT_COMPONENTS.include?(component)
      suppressed, maximum = rate_limit(component, details, CONTEXT_INTERVAL_SECONDS, 0.0)
      return false if suppressed.nil?
      return enqueue(component, 0.0, details, "context", suppressed, maximum)
    rescue Exception
      return false
    end

    def self.measure_work(component, details = nil)
      started = now
      before_gc = GC.count
      before_gc_time = gc_time_nanoseconds
      return yield
    ensure
      elapsed = now - started if started
      if elapsed && elapsed >= WORK_THRESHOLD_SECONDS
        values = details.is_a?(Hash) ? details.dup : {}
        values["gc_count_delta"] = GC.count - before_gc
        values["gc_time_ms"] = (gc_time_nanoseconds - before_gc_time) / 1_000_000.0
        record_work(component, elapsed, values)
      end
    end

    def self.rate_limit(component, details, interval, seconds)
      operation = details.is_a?(Hash) ? details["operation"] || details[:operation] : nil
      phase = details.is_a?(Hash) ? details["phase"] || details[:phase] : nil
      hook = details.is_a?(Hash) ? details["hook"] || details[:hook] : nil
      key = TOKEN_PATTERN.match?(operation.to_s) ? operation.to_s : "default"
      key += ":#{phase}" if PHASES.include?(phase.to_s)
      key += ":#{hook}" if hook && Ironmon.graphics_update_hook_names.include?(hook)
      stamp = now
      @mutex.synchronize do
        group = (@rate_limits[component] ||= {})
        state = group[key]
        if state && stamp - state[:last] < interval
          state[:suppressed] += 1
          state[:maximum] = seconds if seconds > state[:maximum]
          return [nil, nil]
        end
        group.shift if !state && group.length >= MAXIMUM_QUEUE
        suppressed = state ? state[:suppressed] : 0
        maximum = state && state[:maximum] > seconds ? state[:maximum] : seconds
        group[key] = { :last => stamp, :suppressed => 0, :maximum => 0.0 }
        return [suppressed, maximum]
      end
    end

    def self.note_frame
      stamp = now
      if !@summary_started
        @summary_started = stamp
        @summary_gc_count = GC.count
        @summary_gc_time = gc_time_nanoseconds
        @summary_frames = @summary_slow_hooks = 0
        @summary_hook_seconds = @summary_max_hook = @summary_max_gap = 0.0
      end
      if @last_frame_stamp
        gap = stamp - @last_frame_stamp
        @summary_max_gap = gap if gap > @summary_max_gap
      end
      @last_frame_stamp = stamp
      @summary_frames += 1
      elapsed = stamp - @summary_started
      return if elapsed < SUMMARY_INTERVAL_SECONDS
      details = runtime_details
      details["logical_updates"] = @summary_frames
      details["maximum_gap_ms"] = @summary_max_gap * 1000
      details["hook_work_ms"] = @summary_hook_seconds * 1000
      details["maximum_hook_ms"] = @summary_max_hook * 1000
      details["slow_hooks"] = @summary_slow_hooks
      details["gc_count_delta"] = GC.count - @summary_gc_count
      details["gc_time_ms"] = (gc_time_nanoseconds - @summary_gc_time) / 1_000_000.0
      enqueue(:frame_summary, elapsed, details, "summary")
      @summary_started = nil
    rescue Exception
      @summary_started = nil
    end

    def self.note_hook(seconds)
      return if !@summary_started
      @summary_hook_seconds += seconds
      @summary_max_hook = seconds if seconds > @summary_max_hook
      @summary_slow_hooks += 1 if seconds >= WORK_THRESHOLD_SECONDS
    rescue Exception
      @summary_started = nil
    end

    def self.reset_frame_summary
      @summary_started = nil
      @last_frame_stamp = nil
    end

    def self.enqueue(component, seconds, details, kind, suppressed = 0, maximum = 0.0)
      event = { "timestamp_utc" => Time.now.utc.strftime("%Y-%m-%dT%H:%M:%SZ"),
        "monotonic_seconds" => now, "kind" => kind,
        "component" => component.to_s, "duration_ms" => (seconds * 1000).round(3) }
      event["suppressed_events"] = suppressed if suppressed > 0
      event["maximum_duration_ms"] = (maximum * 1000).round(3) if suppressed > 0
      if details.is_a?(Hash)
        details.each do |key, value|
          if key.to_s == "hook"
            event["hook"] = value.to_s if Ironmon.graphics_update_hook_names.include?(value)
            next
          elsif key.to_s == "source"
            event["source"] = value if value.is_a?(String) && value.length <= 160 && SOURCE_PATTERN.match?(value)
            next
          elsif key.to_s == "phase"
            event["phase"] = value.to_s if PHASES.include?(value.to_s)
            next
          elsif key.to_s == "operation" || key.to_s == "reason"
            event[key.to_s] = value.to_s if TOKEN_PATTERN.match?(value.to_s)
            next
          end
          next if !DETAIL_KEYS.include?(key.to_s)
          valid = value == true || value == false || value.is_a?(Integer) || (value.is_a?(Float) && value.finite?)
          event[key.to_s] = value if valid
        end
      end
      if !event["source"] && (kind == "stall" || kind == "budget")
        source = caller_source
        event["source"] = source if source
      end
      @mutex.synchronize do
        return false if @closed
        @queue.shift if @queue.length >= MAXIMUM_QUEUE
        @queue << event
        if !@worker || !@worker.alive?
          @writing = false
          @worker = Thread.new { writer_loop }
        end
        @changed.signal
      end
      return true
    rescue Exception
      return false
    end

    def self.ensure_directory(value)
      return if File.directory?(value)
      parent = File.dirname(value)
      ensure_directory(parent) if parent != value && !File.directory?(parent)
      Dir.mkdir(value) if !File.directory?(value)
    end

    def self.append_record(event)
      output = path
      ensure_directory(File.dirname(output))
      bytes = if defined?(Ironmon::GameplayCollection) && Ironmon::GameplayCollection.respond_to?(:encode)
                Ironmon::GameplayCollection.encode(event) + "\n"
              else
                Ironmon.tracker_json_generate(event) + "\n"
              end
      return if bytes.bytesize > MAXIMUM_BYTES
      mode = File.file?(output) && File.size(output) + bytes.bytesize > MAXIMUM_BYTES ? "wb" : "ab"
      File.open(output, mode) { |file| file.write(bytes) }
    end

    def self.writer_loop
      Thread.current.report_on_exception = false if Thread.current.respond_to?(:report_on_exception=)
      loop do
        event = @mutex.synchronize do
          @changed.wait(@mutex) while @queue.empty? && !@closed
          next nil if @queue.empty? && @closed
          @writing = true
          @queue.shift
        end
        break if !event
        begin
          append_record(event)
        rescue Exception
          # Diagnostic storage is optional; a failed write must not affect play.
        ensure
          @mutex.synchronize { @writing = false; @changed.broadcast }
        end
      end
    ensure
      @mutex.synchronize { @writing = false; @changed.broadcast }
    end

    def self.flush(timeout = 5.0)
      deadline = Process.clock_gettime(Process::CLOCK_MONOTONIC) + timeout
      @mutex.synchronize do
        until @queue.empty? && !@writing
          remaining = deadline - Process.clock_gettime(Process::CLOCK_MONOTONIC)
          return false if remaining <= 0
          @changed.wait(@mutex, [remaining, 0.05].min)
        end
      end
      return true
    rescue Exception
      return false
    end

    def self.close(timeout = 5.0)
      worker = @mutex.synchronize { @closed = true; @changed.broadcast; @worker }
      return true if !worker
      return !worker.join(timeout).nil?
    rescue Exception
      return false
    end
  end
end
