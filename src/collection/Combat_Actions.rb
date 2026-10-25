module Ironmon
  module GameplayCollection
    module Combat
      CHOICE_KINDS = {
        :UseMove => "move", :UseItem => "item", :SwitchOut => "switch",
        :Run => "run", :Shift => "shift", :Call => "call"
      }

      def self.choice_committed(battle, index)
        return if !active_battle?(battle)
        actor = read(battle, :battlers, [])[index]
        choice = read(battle, :choices, [])[index]
        return if !actor || !choice || choice[0] == :None
        current = state(battle)
        previous = current[:choices][index]
        identity = [choice[0], choice[1], native_id(choice[2]), choice[3]]
        return if previous && previous[:identity] == identity
        choice_cancelled(battle, index, "other") if previous
        identifier = GameplayCollection.next_id("choice")
        current[:choices][index] = { :id => identifier, :identity => identity }
        target_index = choice[3]
        target = target_index.is_a?(Integer) && target_index >= 0 ? read(battle, :battlers, [])[target_index] : nil
        emit("action.choice_committed", {
          "actor" => actor_ref(actor), "choice_id" => identifier,
          "choice_kind" => CHOICE_KINDS.fetch(choice[0], "other"),
          "chosen_move_id" => choice[0] == :UseMove ? native_id(choice[2]) : nil,
          "chosen_target" => actor_ref(target),
          "selection_kind" => owner_role(actor) == "player" ? "player" : "ai",
          "resource_ref" => nil
        }, battle)
      end

      def self.choice_cancelled(battle, index, reason = "unknown")
        return if !active_battle?(battle)
        previous = state(battle)[:choices].delete(index)
        return if !previous
        emit("action.choice_cancelled", {
          "actor" => actor_ref(read(battle, :battlers, [])[index]),
          "choice_id" => previous[:id], "replacement_choice_id" => nil,
          "reason" => reason
        }, battle)
      end

      def self.priority_context(battle, actor)
        entry = read(battle, :priority, nil)
        entry ||= battle.instance_variable_get(:@priority)
        row = (entry || []).find { |candidate| candidate && candidate[0].equal?(actor) }
        choice = read(battle, :choices, [])[actor.index]
        selected = choice && choice[0] == :UseMove ? choice[2] : nil
        return {
          "order_version" => state(battle)[:order_version],
          "effective_move_priority" => row ? row[3] : nil,
          "effective_priority" => row ? row[3] : nil, "base_priority" => read(selected, :priority),
          "subpriority" => row ? row[2] : nil,
          "effective_speed" => row ? row[1] : nil,
          "tie_breaker" => row ? row[4] : nil,
          "coverage" => row ? "known" : "unknown"
        }
      end

      def self.with_action(battle, actor, kind, origin = "chosen")
        return yield if !active_battle?(battle)
        current = state(battle)
        parent = current[:actions].last
        identifier = GameplayCollection.open_group("action", context(battle))
        chosen = current[:choices][read(actor, :index)]
        priority = priority_context(battle, actor)
        values = {
          "action_id" => identifier, "parent_action_id" => parent,
          "effect_group_id" => identifier,
          "source_actor" => actor_ref(actor), "initiating_actor" => actor_ref(actor),
          "execution_origin" => origin, "priority_context" => priority
        }
        current[:actions] << identifier
        current[:last_action_id] = identifier
        succeeded = false
        result = with_context(values, battle) do
          emit("action.started", {
            "actor" => actor_ref(actor), "action_kind" => kind,
            "choice_id" => chosen ? chosen[:id] : nil,
            "execution_origin" => origin,
            "effective_priority" => priority["effective_move_priority"],
            "priority_context" => priority, "state_refs" => {}
          }, battle)
          answer = yield
          succeeded = true
          answer
        end
        return result
      ensure
        if identifier
          with_context(values, battle) do
            completion = succeeded ? "completed" : "interrupted"
            emit("action.finished", {
              "actor" => actor_ref(actor), "action_kind" => kind,
              "completion" => completion, "root_closed" => parent.nil?,
              "duration_ticks" => nil, "state_refs" => {}
            }, battle)
            GameplayCollection.close_group(identifier, { "completion" => completion }, completion)
          end
          current[:actions].pop
          current[:choices].delete(read(actor, :index)) if origin == "chosen"
        end
      end

      def self.begin_move(battler, choice, special_usage)
        current = state(battler.battle)
        parent = current[:moves].last
        identifier = GameplayCollection.next_id("move_invocation")
        origin = read(battler.battle, :futureSight, false) ? "delayed" : (parent ? "called" : (GameplayCollection.current_context["execution_origin"] || "chosen"))
        invocation = {
          :id => identifier, :actor => battler, :chosen_move_id => native_id(choice && choice[2]),
          :move => choice && choice[2], :special_usage => special_usage,
          :origin => origin, :parent => parent ? parent[:id] : nil,
          :children => 0, :targets => [], :hits => [], :effect_count => 0,
          :attempted => false, :executed => false, :planned_hits => nil
        }
        parent[:children] += 1 if parent
        current[:moves] << invocation
        return invocation
      end

      def self.invocation_context(invocation)
        return {
          "move_invocation_id" => invocation[:id], "parent_move_invocation_id" => invocation[:parent],
          "execution_origin" => invocation[:origin], "special_usage" => invocation[:special_usage],
          "invocation_kind" => invocation[:origin],
          "chosen_move_id" => invocation[:chosen_move_id],
          "mechanical_move_id" => native_id(invocation[:move])
        }
      end

      def self.attempt_result(battler, move, result, reason = nil)
        invocation = current_move(battler.battle)
        return if !invocation || invocation[:attempted]
        invocation[:attempted] = true
        invocation[:move] = move
        emit("move.attempted", {
          "actor" => actor_ref(battler), "attempted_move_id" => native_id(move),
          "chosen_move_id" => invocation[:chosen_move_id], "attempt_outcome" => result,
          "block_cause_ref" => reason, "special_usage" => invocation[:special_usage],
          "invocation_context" => invocation_context(invocation)
        }, battler.battle)
      end

      def self.executed(battler, move, user)
        invocation = current_move(battler.battle)
        return if !invocation || invocation[:executed]
        invocation[:executed] = true
        invocation[:move] = move
        emit("move.executed", {
          "actor" => actor_ref(user), "move_id" => native_id(move),
          "original_actor" => actor_ref(battler), "effective_move" => move_metadata(move, user),
          "pp_context_ref" => nil, "calls_another_move" => read(move, :callsAnotherMove?, false)
        }, battler.battle)
      end

      def self.finish_move(battler, invocation, completed)
        return if !active_battle?(battler.battle)
        attempt_result(battler, invocation[:move], invocation[:move] ? "unknown" : "no_move") if !invocation[:attempted]
        move = invocation[:move]
        metadata = move_metadata(move, battler)
        result = if !completed
          "interrupted"
        elsif invocation[:children] > 0
          "delegated"
        elsif !invocation[:executed]
          "blocked"
        elsif metadata["charging"]
          "charging"
        elsif read(battler, :lastMoveFailed, nil) == true
          "executed_no_effect"
        elsif invocation[:effect_count] > 0
          "executed_effect"
        else
          "executed_no_effect"
        end
        hits = invocation[:hits]
        positive_hp = hits.select { |hit| hit["result"] == "real_hp_damage" }.map { |hit| hit["target"] }.uniq
        positive_substitute = hits.select { |hit| hit["result"] == "substitute_damage" }.map { |hit| hit["target"] }.uniq
        per_target = hits.group_by { |hit| hit["target"] }.map do |target, target_hits|
          counts = {}
          target_hits.each { |hit| counts[hit["result"]] = counts.fetch(hit["result"], 0) + 1 }
          {
            "target" => target, "considered_iteration_count" => target_hits.length,
            "resolved_iteration_count" => target_hits.length,
            "affected_not_missed_iteration_indexes" => target_hits.reject { |hit| ["missed", "blocked"].include?(hit["result"]) }.map { |hit| hit["hit_index"] },
            "outcome_counts" => counts, "positive_real_hp_hit_count" => counts.fetch("real_hp_damage", 0),
            "substitute_damage_hit_count" => counts.fetch("substitute_damage", 0)
          }
        end
        emit("move.finished", {
          "actor" => actor_ref(battler), "move_id" => native_id(move), "result" => result,
          "native_last_move_failed" => read(battler, :lastMoveFailed, nil),
          "targets_attempted" => invocation[:targets].length, "hits_resolved" => hits.length,
          "positive_hp_targets" => positive_hp.length, "positive_substitute_targets" => positive_substitute.length,
          "effect_categories" => invocation[:effect_categories] || [], "effect_categories_coverage" => "observed_committed_effects",
          "resolved_move_metadata" => metadata.merge("execution_origin" => invocation[:origin]),
          "invocation_context" => invocation_context(invocation),
          "child_actions" => { "count" => invocation[:children], "origins" => { "called" => invocation[:children] } },
          "hit_iterations_planned" => invocation[:planned_hits],
          "hit_iterations_executed" => hits.map { |hit| hit["hit_index"] }.uniq.length,
          "hit_pattern_kind" => invocation[:planned_hits] == 1 ? "single" : "unknown",
          "hit_pattern_range" => {
            "metadata_revision_id" => "native-observed-1", "intrinsic_min_hits" => nil,
            "intrinsic_max_hits" => nil, "effective_min_hits" => nil, "effective_max_hits" => nil,
            "range_basis" => "unknown", "override_source" => nil
          },
          "per_target_hit_outcomes" => per_target
        }, battler.battle)
        state(battler.battle)[:moves].pop
      end
    end
  end
end

module IronmonCollectionChoiceHooks
  def pbClearChoice(index)
    Ironmon::GameplayCollection::Combat.choice_cancelled(self, index)
    return super
  end

  def pbCalculatePriority(*args)
    result = super
    combat = Ironmon::GameplayCollection::Combat
    if combat.active_battle?(self)
      current = combat.state(self)
      current[:order_version] += 1
      entries = (@priority || []).each_with_index.map do |entry, rank|
        {
          "actor" => combat.actor_ref(entry[0]), "effective_speed" => entry[1],
          "move_priority" => entry[3], "subpriority" => entry[2], "rank" => rank + 1,
          "tie_breaker" => entry[4]
        }
      end
      combat.emit("battle.order_resolved", {
        "order_version" => current[:order_version], "reason" => args[0] ? "round_start" : "speed_change",
        "ordered_entries" => entries, "reversed_speed_order" => @priorityTrickRoom == true
      }, self)
    end
    return result
  end
end

[:pbRegisterMove, :pbRegisterTarget, :pbRegisterSwitch, :pbRegisterItem, :pbRegisterShift, :pbRegisterCall].each do |method_name|
  IronmonCollectionChoiceHooks.define_method(method_name) do |*args, &block|
    result = super(*args, &block)
    Ironmon::GameplayCollection::Combat.choice_committed(self, args[0]) if result != false
    result
  end
end

module IronmonCollectionMoveHooks
  def pbUseMove(choice, special_usage = false)
    combat = Ironmon::GameplayCollection::Combat
    return super if !combat.active_battle?(@battle)
    parent = combat.current_move(@battle)
    operation = proc do
      invocation = combat.begin_move(self, choice, special_usage)
      completed = false
      begin
        result = combat.with_context({
          "move_invocation_id" => invocation[:id], "source_kind" => "move",
          "source_id" => invocation[:chosen_move_id], "source_actor" => combat.actor_ref(self),
          "execution_origin" => invocation[:origin]
        }, @battle) { super(choice, special_usage) }
        completed = true
        result
      ensure
        combat.with_context({
          "move_invocation_id" => invocation[:id], "source_kind" => "move",
          "source_id" => combat.native_id(invocation[:move]), "source_actor" => combat.actor_ref(self),
          "execution_origin" => invocation[:origin]
        }, @battle) { combat.finish_move(self, invocation, completed) }
      end
    end
    return combat.with_action(@battle, self, "move", parent ? "called" : "chosen") { operation.call }
  end

  def pbTryUseMove(choice, move, special_usage, skip_accuracy_check)
    result = super
    combat = Ironmon::GameplayCollection::Combat
    combat.attempt_result(self, choice[2] || move, result ? "proceed" : "blocked", result ? nil : combat.cause) if combat.active_battle?(@battle)
    return result
  end

  def pbFindTargets(choice, move, user)
    combat = Ironmon::GameplayCollection::Combat
    combat.executed(self, move, user) if combat.active_battle?(@battle)
    result = super
    invocation = combat.current_move(@battle)
    invocation[:selected_targets] = result.map { |target| combat.actor_ref(target) } if invocation
    return result
  end

  def pbChangeTargets(move, user, targets)
    result = super
    combat = Ironmon::GameplayCollection::Combat
    invocation = combat.current_move(@battle)
    if invocation
      invocation[:targets] = result.map { |target| combat.actor_ref(target) }
      selected = invocation[:selected_targets] || []
      combat.emit("move.targets_resolved", {
        "actor" => combat.actor_ref(user), "selected_targets" => selected,
        "resolved_targets" => invocation[:targets],
        "redirect_reason" => selected == invocation[:targets] ? "none" : "unknown",
        "cause" => combat.cause
      }, @battle)
    end
    return result
  end

  def pbSuccessCheckAgainstTarget(move, user, target)
    result = super
    combat = Ironmon::GameplayCollection::Combat
    if combat.current_move(@battle)
      damage = target.damageState
      kind = if result
        "allowed"
      elsif damage.protected
        "protected"
      elsif damage.magicCoat || damage.magicBounce
        "reflected"
      elsif damage.typeMod == 0
        "type_immune"
      else
        "unknown"
      end
      combat.emit("move.target_gate_resolved", {
        "actor" => combat.actor_ref(user), "target" => combat.actor_ref(target),
        "gate_result" => kind, "type_modifier" => combat.type_modifier(damage),
        "cause" => combat.cause,
        "gate_mechanic_context" => {
          "mechanic_namespace" => nil, "mechanic_id" => nil, "bearer" => nil,
          "prevention_kind" => kind, "actual_move_category" => combat.move_metadata(move)["category"],
          "actual_move_damaging" => combat.read(move, :damagingMove?, false)
        }
      }, @battle)
    end
    return result
  end

  def pbSuccessCheckPerHit(move, user, target, skip_accuracy_check)
    result = super
    combat = Ironmon::GameplayCollection::Combat
    if combat.current_move(@battle)
      combat.emit("move.accuracy_resolved", {
        "actor" => combat.actor_ref(user), "target" => combat.actor_ref(target),
        "hit_index" => combat.hit_index(@battle), "check_kind" => skip_accuracy_check ? "skipped" : "unknown",
        "passed" => result == true, "effective_accuracy" => nil, "reason" => nil
      }, @battle)
    end
    return result
  end
end

Ironmon::GameplayCollection::Combat.install(PokeBattle_Battle, IronmonCollectionChoiceHooks)
Ironmon::GameplayCollection::Combat.install(PokeBattle_Battler, IronmonCollectionMoveHooks)
