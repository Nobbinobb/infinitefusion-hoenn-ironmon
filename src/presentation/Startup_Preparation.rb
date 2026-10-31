module Ironmon
  module StartupPreparation
    PUMP_INTERVAL_SECONDS = 1.0 / 15

    def self.active?
      return @active == true
    end

    def self.required?(save_data)
      return !active? && Ironmon.saved_ironmon?(save_data) &&
        !Ironmon.checkpoint_reset_loading? && !Ironmon.restoring_live_snapshot?
    end

    def self.now
      return Process.clock_gettime(Process::CLOCK_MONOTONIC)
    end

    def self.native_method(name, receiver = Graphics)
      method = receiver.method(name)
      method = method.super_method until !method || method.owner == receiver.singleton_class
      raise "The native loading renderer is unavailable: #{name}" if !method
      return method
    end

    def self.pump_connection
      connection = Ironmon.tracker_connection
      connection.update_loading if connection.respond_to?(:update_loading)
    end

    def self.begin_progress
      @progress_sequence = @progress_sequence.to_i + 1
      @preparation_id = Digest::SHA256.hexdigest("#{Process.pid}|#{now}|#{@progress_sequence}")
      @progress_phase = nil
      report_phase(:loading_save)
      pump_connection
    end

    def self.report_phase(phase)
      return false if !@preparation_id || phase == @progress_phase
      @progress_phase = phase
      return publish_progress("running", phase.to_s)
    end

    def self.publish_progress(status, phase, error = nil)
      return false if !@preparation_id
      payload = { "preparation_id" => @preparation_id, "status" => status, "phase" => phase }
      payload["error"] = error.message.to_s[0, 500] if error
      connection = Ironmon.tracker_connection
      return connection.send_startup_preparation_progress(payload) if
        connection.respond_to?(:send_startup_preparation_progress)
      return false
    rescue StandardError
      return false
    end

    def self.finish_progress(error = nil)
      status = error ? "failed" : "complete"
      phase = error ? (@progress_phase || :loading_save).to_s : "complete"
      publish_progress(status, phase, error)
    ensure
      @preparation_id = nil
      @progress_phase = nil
    end

    def self.render_frame
      @screen.update_message_visibility if @screen && @screen.respond_to?(:update_message_visibility)
      return native_method(:update_KGC_SpecialTransition).call
    end

    def self.drain_input
      native_input = native_method(:update_KGC_ScreenCapture, Input)
      Ironmon::ResponsiveInput.reset
      2.times do
        native_input.call
        Ironmon::ResponsiveInput.update
      end
    end

    def self.warm_metadata(screen)
      report_phase(:world_data)
      screen.progress(nil, true)
      pump_connection
      pbGetMapNameFromId($game_map.map_id) if $game_map
      Ironmon.tracker_search_fusion_name_parts
      Ironmon.tracker_move_power_catalog
    end

    def self.load(save_data)
      return yield if !required?(save_data)
      started = now
      @active = true
      entry_frame_count = Ironmon::SpeedClock.raw_frame_count
      begin_progress
      screen = Screen.new
      @screen = screen
      Ironmon.clear_tracker_controls
      screen.open
      result = yield
      loaded_frame_count = Ironmon::SpeedClock.raw_frame_count
      warm_metadata(screen)
      Ironmon.finish_player_fusion_startup_preparation do |mapper|
        phase = mapper.preparation_stage
        report_phase(phase) if phase != :prepared && phase != :complete
        pump_connection if screen.progress(mapper)
      end
      pump_connection
      Ironmon.resume_run_duration
      return result
    rescue Exception => failure
      raise
    ensure
      if started
        begin
          screen.close if screen
        rescue Exception => error
          cleanup_failure = error
          raise
        ensure
          begin
            Ironmon::SpeedClock.set_raw_frame_count(loaded_frame_count || entry_frame_count)
            Ironmon::SpeedClock.rebase_native_frames
            Ironmon::SpeedUp.resume_after_loading
            Ironmon::SpeedDiagnostics.reset_frame_summary
            pump_connection if loaded_frame_count
            Ironmon.clear_tracker_controls
            drain_input
          rescue Exception => error
            cleanup_failure = error
            raise
          ensure
            begin
              finish_progress(cleanup_failure || failure)
            ensure
              @active = false
              @screen = nil
              if defined?(Ironmon::SpeedDiagnostics)
                Ironmon::SpeedDiagnostics.record_work(:calculation_prepare, now - started,
                  { "operation" => "startup_preparation" })
              end
            end
          end
        end
      end
    end

    class Screen
      def initialize
        @opened = false
        @last_pump = nil
      end

      def open
        @native_update = StartupPreparation.native_method(:update_KGC_SpecialTransition)
        @native_transition = StartupPreparation.native_method(:transition_KGC_SpecialTransition)
        @native_input = StartupPreparation.native_method(:update_KGC_ScreenCapture, Input)
        @native_rate = Ironmon::SpeedUp::NATIVE_RATE.call
        @frameskip = Graphics.frameskip
        @brightness = Graphics.brightness
        @frozen = Ironmon::SpeedUp.frozen
        @opened = true
        Ironmon::SpeedUp::SET_NATIVE_RATE.call(15)
        Ironmon::SpeedUp::SET_NATIVE_FRAMESKIP.call(false)
        Graphics.brightness = 255
        @viewport = Viewport.new(0, 0, Graphics.width, Graphics.height)
        @viewport.z = 999_999
        @canvas = BitmapSprite.new(Graphics.width, Graphics.height, @viewport)
        Ironmon::OverlayStyle.set_font(@canvas.bitmap, 24)
        paint
        @native_transition.call(0)
        Ironmon::SpeedUp.frozen = false
        Graphics.frame_reset
        progress(nil, true)
      end

      def update_message_visibility
        @viewport.visible = !($game_temp && $game_temp.message_window_showing) if @viewport
      end

      def progress(_mapper, force = false)
        stamp = StartupPreparation.now
        return false if !force && @last_pump && stamp - @last_pump < PUMP_INTERVAL_SECONDS
        update_message_visibility
        @native_update.call
        @native_input.call
        @last_pump = StartupPreparation.now
        return true
      end

      def paint
        bitmap = @canvas.bitmap
        bitmap.fill_rect(0, 0, bitmap.width, bitmap.height, Color.new(0, 0, 0))
        middle = bitmap.height / 2
        bitmap.font.size = 26
        bitmap.draw_text(16, middle - 42, bitmap.width - 32, 36, "Preparing your game", 1)
        bitmap.font.size = 20
        bitmap.font.color = Color.new(183, 197, 211)
        bitmap.draw_text(16, middle + 4, bitmap.width - 32, 30,
          "Please wait.", 1)
        bitmap.font.color = Color.new(245, 248, 252)
      end

      def close
        return if !@opened
        begin
          if @canvas && !@canvas.disposed?
            @canvas.bitmap.fill_rect(0, 0, Graphics.width, Graphics.height, Color.new(0, 0, 0))
            @native_update.call
            Graphics.freeze if @frozen
          end
        ensure
          @canvas.dispose if @canvas && !@canvas.disposed?
          @viewport.dispose if @viewport && !@viewport.disposed?
          Ironmon::SpeedUp::SET_NATIVE_RATE.call(@native_rate)
          Ironmon::SpeedUp::SET_NATIVE_FRAMESKIP.call(@frameskip)
          Graphics.brightness = @brightness
          Ironmon::SpeedUp.frozen = @frozen
          Graphics.frame_reset
          @opened = false
        end
      end
    end

    module LoadGate
      def load(save_data)
        return Ironmon::StartupPreparation.load(save_data) { super }
      end
    end
  end
end

Game.singleton_class.prepend(Ironmon::StartupPreparation::LoadGate)
