module Ironmon
  module GameplayCollection
    module World
      CLOCK_POLICY = "loaded-save-monotonic-1"
      CATALOG_REVISION = "tracker-area-catalog-1"

      def self.hydrating?
        return @hydrating == true || !@transition_pending.nil?
      end

      def self.observe
        return yield
      rescue StandardError => error
        GameplayCollection.fail_collection(error)
        return nil
      end

      def self.loaded?
        return @loaded == true
      end

      def self.collecting?
        return GameplayCollection.enabled? && !hydrating?
      end

      def self.leave_loaded(reason = "title")
        return if !loaded?
        close_clock(reason)
        persist_attempt
        GameplayCollection.emit("session.state_changed", {
          "game_session_id" => @session_id, "state_before" => "loaded", "state_after" => "unloaded",
          "reason" => reason, "game_version" => nil, "tracker_version" => nil, "management_protocol" => nil
        }) if GameplayCollection.enabled?
        activity("loaded", "unloaded", reason) if GameplayCollection.enabled?
        GameplayCollection.flush
        @loaded = false
        @clock_anchor = nil
        @last_world = nil
      end

      def self.source_run_id
        attempt = Ironmon.current_run_attempt
        return attempt ? attempt["run_id"].to_s : nil
      end

      def self.area_id(map_id = nil)
        map_id ||= defined?($game_map) && $game_map ? $game_map.map_id : nil
        return nil if !map_id || !Ironmon.respond_to?(:tracker_area_id_for_map)
        return Ironmon.tracker_area_id_for_map(map_id)
      end

      def self.movement_mode
        return "unknown" if !$PokemonGlobal
        return "surfing" if Combat.read($PokemonGlobal, :surfing, false)
        return "diving" if Combat.read($PokemonGlobal, :diving, false)
        return "cycling" if Combat.read($PokemonGlobal, :bicycle, false)
        return "walking"
      end

      def self.world_context
        return {
          "map_id" => defined?($game_map) && $game_map ? $game_map.map_id : nil,
          "lookup_area_id" => area_id, "movement_mode" => movement_mode,
          "badge_count" => $Trainer ? Combat.read($Trainer, :badge_count, 0) : 0,
          "source_run_id" => source_run_id
        }
      end

      def self.bind_attempt(new_attempt = false, kind = "load", prepared = nil)
        return if !GameplayCollection.enabled?
        source = source_run_id
        return if !source || source.empty?
        attempt = Ironmon.current_run_attempt || {}
        journal = GameplayCollection.journal
        bindings = journal.runtime_context("attempt_bindings") || {}
        association = $PokemonGlobal.instance_variable_get(:@ironmon_collection_attempt_association)
        valid = association.is_a?(Hash) && association["epoch_id"] == journal.epoch_id && /\A[0-9a-f]{32}\z/.match?(association["token"].to_s)
        if prepared
          association = { "epoch_id" => journal.epoch_id, "token" => prepared[:token] }
        elsif new_attempt || !valid
          association = { "epoch_id" => journal.epoch_id, "token" => GameplayCollection.uuid }
        end
        binding_key = association["token"]
        previous = bindings[binding_key]
        if !new_attempt && !prepared && previous && (previous["source_run_id"] != source || previous["attempt_number"] != attempt["attempt_number"])
          association = { "epoch_id" => journal.epoch_id, "token" => GameplayCollection.uuid }
          binding_key = association["token"]
          previous = nil
        end
        $PokemonGlobal.instance_variable_set(:@ironmon_collection_attempt_association, association)
        identifier = prepared ? prepared[:attempt_id] : (!new_attempt && previous ? previous["attempt_id"] : GameplayCollection.uuid)
        branch = GameplayCollection.uuid
        @terminal_battle = nil
        @attempt_seconds = !new_attempt && previous ? previous.fetch("tracked_seconds", 0).to_f : 0.0
        @current_source = source
        @current_binding_key = binding_key
        @completed = !new_attempt && previous ? previous["completed"] == true : false
        GameplayCollection.instance_variable_set(:@attempt_id, identifier)
        GameplayCollection.instance_variable_set(:@branch_id, branch)
        GameplayCollection.instance_variable_set(:@save_lineage_id, binding_key)
        bindings[binding_key] = {
          "attempt_id" => identifier, "attempt_number" => attempt["attempt_number"],
          "tracked_seconds" => @attempt_seconds, "completed" => @completed, "source_run_id" => source
        }
        journal.bind_runtime_context("attempt_bindings", bindings)
        journal.bind_runtime_context("current_attempt", {
          "source_run_id" => source, "association_token" => binding_key, "attempt_id" => identifier, "branch_id" => branch
        })
        if new_attempt
          GameplayCollection.emit("attempt.started", {
            "attempt_id" => identifier, "attempt_number" => attempt["attempt_number"],
            "save_lineage_id" => binding_key, "start_kind" => kind,
            "rules_ref" => "ironmon-current", "generation_profile_ref" => attempt["generation_profile_id"] || (Ironmon.respond_to?(:run_generation_profile_id) && Ironmon.run_generation_profile_id) || { "coverage" => "unknown", "reason" => "native_attempt_has_no_profile_reference" },
            "start_world_ref" => nil, "source_run_id" => source,
            "baseline_history_included" => false
          })
        elsif !previous
          GameplayCollection.emit("collection.attempt_context_initialized", {
            "attempt_id" => identifier, "source_run_id" => source, "association_token" => binding_key,
            "baseline_history_included" => false, "reason" => "tracking_started_mid_attempt"
          }, { "origin_channel" => "technical_baseline" })
        end
        return identifier
      end

      def self.persist_attempt
        return if !@current_binding_key || !GameplayCollection.enabled?
        bindings = GameplayCollection.journal.runtime_context("attempt_bindings") || {}
        entry = bindings[@current_binding_key]
        return if !entry
        entry = entry.merge("tracked_seconds" => @attempt_seconds || 0, "completed" => @completed == true)
        bindings[@current_binding_key] = entry
        GameplayCollection.journal.bind_runtime_context("attempt_bindings", bindings)
      end

      def self.close_clock(reason = "periodic")
        now = GameplayCollection.clock
        anchor = @clock_anchor
        @clock_anchor = now if loaded? && !hydrating?
        return if !anchor || !loaded? || !GameplayCollection.enabled?
        duration = [now - anchor, 0.0].max
        return if duration <= 0
        @attempt_seconds = (@attempt_seconds || 0.0) + duration
        GameplayCollection.emit("clock.interval_closed", {
          "interval_id" => GameplayCollection.next_id("clock_interval"),
          "clock_domain" => "game_loaded_save_elapsed", "duration_seconds" => duration,
          "activity_state" => "loaded", "reason" => reason,
          "start_boundary_ref" => nil, "end_boundary_ref" => nil,
          "save_loaded" => true, "clock_policy_revision" => CLOCK_POLICY
        })
      end

      def self.before_load
        previous = {
          :loaded => @loaded, :current_source => @current_source,
          :attempt_id => GameplayCollection.attempt_id, :branch_id => GameplayCollection.branch_id,
          :seconds => @attempt_seconds, :completed => @completed,
          :binding_key => @current_binding_key, :save_lineage_id => GameplayCollection.instance_variable_get(:@save_lineage_id)
        }
        observe do
          close_clock("load")
          persist_attempt
          Combat.interrupt_current("save_load") if Combat.active_battle?
          GameplayCollection.flush
          activity("loaded", "hydrating", "save_load") if loaded? && GameplayCollection.enabled?
        end
        @hydrating = true
        @pending_load = previous
        @clock_anchor = nil
        return previous
      end

      def self.after_load(result, previous)
        @hydrating = false
        @pending_load = nil
        if @transition_pending
          @loaded = true
          @clock_anchor = nil
          return
        end
        if result == false || !GameplayCollection.enabled?
          restore_load(previous)
          return
        end
        @loaded = true
        @session_id ||= GameplayCollection.next_id("session")
        destination = bind_attempt(@pending_new_attempt == true, @pending_start_kind || "new_game")
        @pending_new_attempt = false
        @pending_start_kind = nil
        @clock_anchor = GameplayCollection.clock
        @last_world = world_context
        @starting_badges = @last_world["badge_count"]
        @badge_state = $Trainer ? Combat.read($Trainer, :badges, []).dup : []
        @encounter_mode = movement_protection["encounter_mode"]
        GameplayCollection.emit("session.state_changed", {
          "game_session_id" => @session_id, "state_before" => previous[:loaded] ? "loaded" : "unloaded",
          "state_after" => "loaded", "reason" => "save_loaded",
          "game_version" => defined?(Settings::GAME_VERSION) ? Settings::GAME_VERSION.to_s : nil,
          "tracker_version" => nil, "management_protocol" => nil
        })
        GameplayCollection.emit("attempt.transition_resolved", {
          "transition_id" => GameplayCollection.next_id("transition"), "kind" => "save_load", "outcome" => "success",
          "source_attempt_id" => previous[:attempt_id], "destination_attempt_id" => destination,
          "source_save_ref" => previous[:current_source], "destination_save_ref" => @current_source, "failure_stage" => nil,
          "new_branch_id" => GameplayCollection.branch_id
        })
        GameplayCollection.emit("save.operation_resolved", {
          "operation_id" => GameplayCollection.next_id("save_operation"), "operation" => "load", "outcome" => "success",
          "save_lineage_id" => @current_source, "slot_before" => nil,
          "slot_after" => $Trainer ? Combat.read($Trainer, :save_slot).to_s : nil,
          "world_revision" => nil, "hydration_mode" => "baseline_only"
        })
        activity("hydrating", "loaded", "save_loaded")
        Ownership.baseline("load") if defined?(Ownership)
        GameplayCollection.flush
      end

      def self.restore_load(previous)
        @loaded = previous[:loaded]
        @current_source = previous[:current_source]
        @attempt_seconds = previous[:seconds]
        @completed = previous[:completed]
        @current_binding_key = previous[:binding_key]
        @pending_new_attempt = false
        @pending_start_kind = nil
        GameplayCollection.instance_variable_set(:@attempt_id, previous[:attempt_id])
        GameplayCollection.instance_variable_set(:@branch_id, previous[:branch_id])
        GameplayCollection.instance_variable_set(:@save_lineage_id, previous[:save_lineage_id])
        @clock_anchor = loaded? ? GameplayCollection.clock : nil
      end

      def self.abort_load
        return if !@pending_load
        previous = @pending_load
        @pending_load = nil
        @hydrating = false
        restore_load(previous)
        GameplayCollection.emit("save.operation_resolved", {
          "operation_id" => GameplayCollection.next_id("save_operation"), "operation" => "load", "outcome" => "failed",
          "save_lineage_id" => @current_source, "slot_before" => nil, "slot_after" => nil,
          "world_revision" => nil, "hydration_mode" => "failed_load"
        }) if GameplayCollection.enabled?
        GameplayCollection.flush
      end

      def self.new_attempt(kind = "new_attempt", prepared = nil, committed_transition = false)
        return if !GameplayCollection.enabled?
        if @transition_pending && !committed_transition
          @staged_attempt = { :attempt_id => GameplayCollection.uuid, :token => GameplayCollection.uuid, :kind => kind }
          $PokemonGlobal.instance_variable_set(:@ironmon_collection_attempt_association, {
            "epoch_id" => GameplayCollection.journal.epoch_id, "token" => @staged_attempt[:token]
          })
          return
        end
        if @hydrating == true
          @pending_new_attempt = true
          @pending_start_kind = kind
          return
        end
        close_clock("new_attempt")
        persist_attempt
        GameplayCollection.flush
        source = GameplayCollection.attempt_id
        destination = bind_attempt(true, kind, prepared)
        @loaded = true
        @session_id ||= GameplayCollection.next_id("session")
        @clock_anchor = GameplayCollection.clock
        @last_world = world_context
        @starting_badges = @last_world["badge_count"]
        @badge_state = $Trainer ? Combat.read($Trainer, :badges, []).dup : []
        @encounter_mode = movement_protection["encounter_mode"]
        GameplayCollection.emit("attempt.transition_resolved", {
          "transition_id" => GameplayCollection.next_id("transition"), "kind" => kind, "outcome" => "success",
          "source_attempt_id" => source, "destination_attempt_id" => destination,
          "source_save_ref" => nil, "destination_save_ref" => @current_source, "failure_stage" => nil
        })
        Ownership.baseline("new_attempt") if defined?(Ownership)
        GameplayCollection.flush
      end

      def self.complete(result, source = nil, binding_key = nil, committed_transition = false)
        restore_identity = false
        return if !GameplayCollection.enabled? || (@hydrating == true) || (hydrating? && !committed_transition)
        source ||= @current_source
        bindings = GameplayCollection.journal.runtime_context("attempt_bindings") || {}
        binding_key ||= @current_binding_key if source == @current_source
        if !binding_key
          candidates = bindings.select { |_key, value| value["source_run_id"] == source }
          binding_key = candidates.keys[0] if candidates.length == 1
        end
        entry = bindings[binding_key]
        return if !entry || entry["completed"]
        close_clock("attempt_completed") if binding_key == @current_binding_key
        previous_id = GameplayCollection.attempt_id
        restore_identity = true
        GameplayCollection.instance_variable_set(:@attempt_id, entry["attempt_id"])
        completion_id = GameplayCollection.next_id("attempt_completion")
        terminal = @terminal_battle && @terminal_battle["attempt_id"] == entry["attempt_id"] ? @terminal_battle : nil
        terminal = nil if terminal && !((result.to_s == "lost" && [2, 5].include?(terminal["native_decision"])) || (result.to_s == "won" && terminal["native_decision"] == 1))
        terminal["run_completion_ref"] = completion_id if terminal
        GameplayCollection.emit("attempt.completed", {
          "event_id" => completion_id, "result" => result.to_s, "completion_kind" => "game_authoritative", "cause_ref" => terminal && terminal["event_id"],
          "terminal_battle_ref" => terminal && terminal["battle_id"], "terminal_cause" => terminal && terminal["cause"],
          "final_statistics_ref" => nil, "active_seconds" => binding_key == @current_binding_key ? @attempt_seconds : entry["tracked_seconds"],
          "last_world_context" => @transition_pending && binding_key == @transition_pending[:source_binding_key] ? @transition_pending[:world] : world_context,
          "terminal_revision" => 1, "source_run_id" => source
        })
        entry["completed"] = true
        bindings[binding_key] = entry
        GameplayCollection.journal.bind_runtime_context("attempt_bindings", bindings)
        @completed = true if binding_key == @current_binding_key
        Ownership.poll("run_end", true) if defined?(Ownership) && binding_key == @current_binding_key && !hydrating?
        GameplayCollection.flush
      ensure
        GameplayCollection.instance_variable_set(:@attempt_id, previous_id) if restore_identity
      end

      def self.tick
        if !collecting?
          @clock_anchor = nil if !GameplayCollection.enabled?
          return
        end
        return if defined?(Scene_Intro) && defined?($scene) && $scene.is_a?(Scene_Intro)
        if !loaded?
          return if !$PokemonGlobal || !Ironmon.current_run_attempt
          @loaded = true
          bind_attempt(false, "tracking_started")
          @clock_anchor = GameplayCollection.clock
          @last_world = world_context
          @starting_badges = @last_world["badge_count"]
          @badge_state = $Trainer ? Combat.read($Trainer, :badges, []).dup : []
          Ownership.baseline("tracking_started") if defined?(Ownership)
        end
        now = GameplayCollection.clock
        close_clock if @clock_anchor && now - @clock_anchor >= GameplayCollection::FLUSH_SECONDS
        sample_world
        Ownership.poll if defined?(Ownership) && (!@ownership_poll_at || now >= @ownership_poll_at)
        @ownership_poll_at = now + 0.5 if !@ownership_poll_at || now >= @ownership_poll_at
      rescue StandardError => error
        GameplayCollection.fail_collection(error)
      end

      def self.stage_transition(kind, transaction)
        return if !GameplayCollection.enabled? || !transaction
        close_clock(kind)
        persist_attempt
        GameplayCollection.flush
        @transition_pending = {
          :kind => kind, :source_binding_key => @current_binding_key,
          :source_attempt_id => GameplayCollection.attempt_id, :source_branch_id => GameplayCollection.branch_id,
          :source_run_id => @current_source, :seconds => @attempt_seconds, :world => world_context
        }
        transaction["collection_source_binding_key"] = @current_binding_key
        @staged_attempt = nil
        @clock_anchor = nil
      end

      def self.commit_transition(transaction)
        return if !@transition_pending
        pending = @transition_pending
        staged = @staged_attempt
        statistics = transaction["completed_recipe"].is_a?(Hash) ? transaction["completed_recipe"]["statistics"] : nil
        result = statistics.is_a?(Hash) ? statistics["result"] : "abandoned"
        complete(result || "abandoned", pending[:source_run_id], pending[:source_binding_key], true)
        @transition_pending = nil
        @staged_attempt = nil
        new_attempt(pending[:kind], staged, true)
      end

      def self.cancel_transition(reason = "failed")
        pending = @transition_pending
        return if !pending
        @transition_pending = nil
        @staged_attempt = nil
        @current_source = pending[:source_run_id]
        @current_binding_key = pending[:source_binding_key]
        @attempt_seconds = pending[:seconds]
        GameplayCollection.instance_variable_set(:@attempt_id, pending[:source_attempt_id])
        GameplayCollection.instance_variable_set(:@branch_id, pending[:source_branch_id])
        GameplayCollection.instance_variable_set(:@save_lineage_id, pending[:source_binding_key])
        if $PokemonGlobal && pending[:source_binding_key]
          $PokemonGlobal.instance_variable_set(:@ironmon_collection_attempt_association, {
            "epoch_id" => GameplayCollection.journal.epoch_id, "token" => pending[:source_binding_key]
          })
        end
        @clock_anchor = loaded? ? GameplayCollection.clock : nil
        GameplayCollection.emit("attempt.transition_resolved", {
          "transition_id" => GameplayCollection.next_id("transition"), "kind" => pending[:kind], "outcome" => "failed",
          "source_attempt_id" => pending[:source_attempt_id], "destination_attempt_id" => nil,
          "source_save_ref" => pending[:source_binding_key], "destination_save_ref" => nil, "failure_stage" => reason
        })
        GameplayCollection.flush
      end

      def self.sample_world
        current = world_context
        previous = @last_world || current
        if previous["map_id"] != current["map_id"]
          GameplayCollection.emit("world.map_changed", {
            "map_before" => previous["map_id"], "map_after" => current["map_id"], "entry_kind" => "travel",
            "source_event_ref" => nil, "map_catalog_revision" => CATALOG_REVISION, "location_class" => nil
          })
        end
        if previous["lookup_area_id"] != current["lookup_area_id"]
          GameplayCollection.emit("world.area_presence_changed", {
            "area_before" => previous["lookup_area_id"], "area_after" => current["lookup_area_id"], "map_id" => current["map_id"],
            "catalog_revision" => CATALOG_REVISION, "entry_kind" => "travel", "playable" => true
          })
        end
        if previous["movement_mode"] != current["movement_mode"]
          GameplayCollection.emit("world.travel_mode_changed", {
            "mode_before" => previous["movement_mode"], "mode_after" => current["movement_mode"],
            "cause_kind" => "game_state", "field_action_ref" => nil, "tool_or_move_id" => nil, "map_id" => current["map_id"]
          })
        end
        @last_world = current
        badges = $Trainer ? Combat.read($Trainer, :badges, []).dup : []
        before_badges = @badge_state || badges
        badges_committed(before_badges, badges)
        encounter = movement_protection["encounter_mode"]
        if @encounter_mode && @encounter_mode != encounter
          GameplayCollection.emit("world.encounter_mode_changed", {
            "mode_before" => @encounter_mode, "mode_after" => encounter, "cause_kind" => "committed_game_state", "rules_ref" => "native-repel-incense-radar-1"
          })
        end
        @encounter_mode = encounter
      end

      def self.badges_committed(before_badges, badges)
        return if !collecting? || !loaded?
        [before_badges.length, badges.length].max.times do |index|
          before_owned = before_badges[index] == true
          after_owned = badges[index] == true
          next if before_owned == after_owned
          GameplayCollection.emit("world.badge_state_changed", {
            "badge_id" => index + 1, "owned_before" => before_owned, "owned_after" => after_owned,
            "transition_kind" => after_owned ? "awarded" : "removed",
            "badge_count_before" => before_badges.count(true), "badge_count_after" => badges.count(true),
            "awarding_source_ref" => nil, "starting_badge_baseline" => @starting_badges || 0,
            "award_ordinal" => after_owned ? index + 1 : nil,
            "post_epoch_award_count" => badges.count(true) - (@starting_badges || 0)
          })
        end
        @badge_state = badges
        @badge_state = badges.dup
      end

      def self.activity(before, after, reason)
        GameplayCollection.emit("attempt.activity_changed", {
          "state_before" => before, "state_after" => after, "reason" => reason,
          "monotonic_at" => GameplayCollection.clock, "clock_interval_ref" => nil, "policy_revision" => CLOCK_POLICY
        })
      end

      def self.field_tool_changed(item, before, after)
        return if !collecting? || before == after
        data = GameData::Item.try_get(item)
        return if !data || !data.is_key_item?
        local = GameplayCollection.current_context
        GameplayCollection.emit("world.field_tool_state_changed", {
          "tool_id" => GameplayCollection.catalogue_ref("item", item), "quantity_before" => before, "quantity_after" => after,
          "source_kind" => local["inventory_reason"] || local["source_kind"] || "unknown",
          "outcome" => before <= 0 && after > 0 ? "acquired" : "removed",
          "replacement_from_item_id" => nil, "item_transaction_ref" => local["resource_ref"], "authored_source_ref" => local["authored_source_ref"]
        })
      end

      def self.story_changed(kind, index, before, after)
        return if !collecting? || before == after
        GameplayCollection.emit("world.story_state_changed", {
          "story_fact_id" => "#{kind}:#{index}", "stage_before" => story_stage(before),
          "stage_after" => story_stage(after), "transition_kind" => "committed_assignment",
          "authored_source_ref" => { "coverage" => "unknown", "reason" => "unclassified_native_assignment" }, "related_transaction_ref" => nil,
          "native_value_before" => GameplayCollection.stringify(before), "native_value_after" => GameplayCollection.stringify(after),
          "world_revision" => GameplayCollection.next_id("world_revision"),
          "map_context" => world_context, "classification_coverage" => "native_state"
        })
        if kind == "switch" && index == Ironmon::DEVON_SWITCH_PEEKO_RESCUED && after == true && before != true && !GameplayCollection.current_context["story_reconciliation"]
          story_transition("story:peeko_rescued", "incomplete", "completed", "completed")
        end
      end

      def self.story_transition(identity, before, after, kind)
        return if !collecting? || !loaded? || before == after || GameplayCollection.current_context["story_reconciliation"]
        GameplayCollection.emit("world.story_state_changed", { "story_fact_id" => identity, "stage_before" => before, "stage_after" => after,
          "transition_kind" => kind, "authored_source_ref" => SourceContext.authored["id"], "related_transaction_ref" => GameplayCollection.current_context["effect_group_id"],
          "world_revision" => GameplayCollection.next_id("world_revision") })
      end

      def self.story_stage(value)
        return value if value.nil? || value.is_a?(String) || value.is_a?(Integer)
        return value.to_s if value == true || value == false
        return { "coverage" => "unsupported", "reason" => "native_value_is_not_a_scalar_stage" }
      end

      def self.step(protection = nil)
        return if !collecting? || !loaded? || !$game_map || !$game_player
        stamp = [Graphics.frame_count, $game_map.map_id, $game_player.x, $game_player.y]
        return if @last_step_stamp == stamp
        @last_step_stamp = stamp
        protection ||= @pending_step || movement_protection
        remaining = $PokemonGlobal ? Combat.read($PokemonGlobal, :repel, 0).to_i : 0
        GameplayCollection.emit("world.movement_completed", {
          "segment_id" => GameplayCollection.next_id("movement"), "map_id" => $game_map.map_id,
          "lookup_area_id" => area_id, "movement_mode" => movement_mode,
          "completed_steps" => 1, "tile_distance" => 1,
          "control_source" => Combat.read($game_player, :move_route_forcing, false) ? "scripted" : "player",
          "protection" => protection.merge("remaining_after" => remaining), "terrain_kind" => nil
        })
      end

      def self.movement_protection
        remaining = $PokemonGlobal ? Combat.read($PokemonGlobal, :repel, 0).to_i : 0
        temporary = $PokemonGlobal && Combat.read($PokemonGlobal, :tempRepel, false) == true
        incense = defined?(SWITCH_USED_AN_INCENSE) && $game_switches && $game_switches[SWITCH_USED_AN_INCENSE]
        radar = $PokemonTemp && Combat.read($PokemonTemp, :pokeradar) && $PokemonSystem && !Combat.read($PokemonSystem, :overworld_encounters, false)
        source = if incense
          "incense"
        elsif remaining > 0
          temporary ? "radar_temporary" : "standard_repel"
        else
          "none"
        end
        return {
          "source_kind" => source,
          "active_before_step" => !incense && (remaining > 0 || !!radar), "remaining_before" => remaining, "item_use_ref" => source == "standard_repel" && $PokemonGlobal ? $PokemonGlobal.instance_variable_get(:@ironmon_collection_repel_source) : nil,
          "temporary_counter" => temporary, "classic_radar_active" => !!radar,
          "encounter_mode" => radar ? "radar_active" : source,
          "standard_repel_counter_active" => !incense && remaining > 0 && !temporary
        }
      end
    end
  end
end

module IronmonCollectionRunLifecycleHooks
  def begin_run_attempt(*args)
    previous = current_run_attempt
    result = super
    Ironmon::GameplayCollection::World.observe { Ironmon::GameplayCollection::World.new_attempt("new_attempt") } if result && !result.equal?(previous)
    return result
  end

  def complete_run(result)
    completed = super
    Ironmon::GameplayCollection::World.observe { Ironmon::GameplayCollection::World.complete(result) } if completed
    return completed
  end

  def complete_reset_tracker_transition(transaction)
    Ironmon::GameplayCollection::World.observe { Ironmon::GameplayCollection::World.commit_transition(transaction) }
    return super
  end

  def start_checkpoint_reset(automatic)
    result = super
    if result
      Ironmon::GameplayCollection::World.observe do
        Ironmon::GameplayCollection::World.stage_transition(automatic ? "automatic_reset" : "manual_reset", @reset_transaction)
      end
    end
    return result
  end

  def start_seed_import
    result = super
    Ironmon::GameplayCollection::World.observe { Ironmon::GameplayCollection::World.stage_transition("seed_import", @seed_import_transaction) } if result
    return result
  end

  def finish_seed_import
    transaction = @seed_import_transaction
    result = super
    Ironmon::GameplayCollection::World.observe { Ironmon::GameplayCollection::World.commit_transition(transaction) } if result && transaction
    return result
  end

  def fail_checkpoint_reset_transaction(error)
    result = super
    Ironmon::GameplayCollection::World.observe { Ironmon::GameplayCollection::World.cancel_transition("generation_or_save") }
    return result
  end

  def fail_seed_import_transaction(error)
    result = super
    Ironmon::GameplayCollection::World.observe { Ironmon::GameplayCollection::World.cancel_transition("generation_or_save") }
    return result
  end
end

module IronmonCollectionLoadFailureHooks
  def load(*args)
    completed = false
    begin
      result = super
      completed = true
      return result
    ensure
      Ironmon::GameplayCollection::World.abort_load if !completed
    end
  end
end

module IronmonCollectionSwitchHooks
  def []=(index, value)
    before = self[index]
    result = super
    Ironmon::GameplayCollection::World.observe { Ironmon::GameplayCollection::World.story_changed("switch", index, before, self[index]) }
    return result
  end
end

module IronmonCollectionVariableHooks
  def []=(index, value)
    before = self[index]
    result = super
    Ironmon::GameplayCollection::World.observe { Ironmon::GameplayCollection::World.story_changed("variable", index, before, self[index]) }
    return result
  end
end

Ironmon.singleton_class.prepend(IronmonCollectionRunLifecycleHooks)
Game.singleton_class.prepend(IronmonCollectionLoadFailureHooks)
Game_Switches.prepend(IronmonCollectionSwitchHooks)
Game_Variables.prepend(IronmonCollectionVariableHooks)

module IronmonCollectionTitleHooks
  def main(*args)
    Ironmon::GameplayCollection::World.observe { Ironmon::GameplayCollection::World.leave_loaded("title") }
    return super
  end
end
Scene_Intro.prepend(IronmonCollectionTitleHooks)

Ironmon.register_game_load_hook(:gameplay_collection,
  proc { |_save_data| Ironmon::GameplayCollection::World.before_load },
  proc { |_save_data, result, previous| Ironmon::GameplayCollection::World.observe { Ironmon::GameplayCollection::World.after_load(result, previous) } })
Ironmon.register_game_save_hook(:gameplay_collection,
  proc do |slot, _auto, _safe|
    world = Ironmon::GameplayCollection::World
    world.close_clock("save")
    Ironmon::GameplayCollection.flush
    { :slot => slot, :operation_id => Ironmon::GameplayCollection.enabled? ? Ironmon::GameplayCollection.next_id("save_operation") : nil }
  end,
  proc do |slot, _auto, _safe, result, previous|
    if previous[:operation_id]
      Ironmon::GameplayCollection.emit("save.operation_resolved", {
        "operation_id" => previous[:operation_id], "operation" => "save", "outcome" => result == false ? "failed" : "success",
        "save_lineage_id" => Ironmon::GameplayCollection::World.source_run_id,
        "slot_before" => previous[:slot].to_s, "slot_after" => slot.to_s,
        "world_revision" => nil, "hydration_mode" => "none"
      })
      Ironmon::GameplayCollection.flush
    end
  end)
Ironmon.register_graphics_update_hook(:gameplay_collection_world, proc { Ironmon::GameplayCollection::World.tick })

alias ironmon_collection_original_on_step_taken pbOnStepTaken
def pbOnStepTaken(*args)
  world = Ironmon::GameplayCollection::World
  previous = world.instance_variable_get(:@pending_step)
  world.instance_variable_set(:@pending_step, world.movement_protection)
  return ironmon_collection_original_on_step_taken(*args)
ensure
  world.instance_variable_set(:@pending_step, previous) if world
end

Events.onStepTaken += proc { |_sender, *_args| Ironmon::GameplayCollection::World.observe { Ironmon::GameplayCollection::World.step } }
Events.onStepTakenFieldMovement += proc do |_sender, event|
  actor = event.is_a?(Array) ? event[0] : event
  Ironmon::GameplayCollection::World.observe { Ironmon::GameplayCollection::World.step } if actor.equal?($game_player)
end

module IronmonCollectionHiddenMoveHooks
  def triggerUseMove(move, pokemon)
    collection = Ironmon::GameplayCollection
    identifier = collection.enabled? ? collection.next_id("field_action") : nil
    result = collection.with_context("source_kind" => "move", "source_id" => move.to_s, "field_action_ref" => identifier) { super }
    Ironmon::GameplayCollection::World.observe do
      if Ironmon::GameplayCollection::World.collecting?
        Ironmon::GameplayCollection.emit("world.field_action_resolved", {
          "event_id" => identifier,
          "action_kind" => Ironmon::GameplayCollection::Combat.native_id(move), "outcome" => result ? "success" : "failed",
          "source_kind" => "move", "source_id" => Ironmon::GameplayCollection.catalogue_ref("move", move),
          "map_event_ref" => nil, "obstacle_or_target_kind" => nil, "world_change_ref" => nil, "item_transaction_ref" => nil,
          "pokemon_ref" => pokemon ? Ironmon::GameplayCollection.pokemon_instance_id(pokemon) : nil
        })
      end
    end
    return result
  end
end
HiddenMoveHandlers.singleton_class.prepend(IronmonCollectionHiddenMoveHooks)
