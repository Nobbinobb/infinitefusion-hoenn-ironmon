module Ironmon
  module GameplayCollection
    module Combat
      def self.actual_resolution?
        return false if !active_battle?
        local = GameplayCollection.current_context
        return true if current_move
        return ["entry", "end_of_round", "battle_start", "battle_end", "attack"].include?(local["phase"])
      end

      def self.item_common(item, holder = nil)
        role = holder ? owner_role(holder) : "unknown"
        return {
          "item_id" => native_id(item), "resource_ref" => nil, "acquisition_origin" => "unknown",
          "battle_entry_owner" => holder ? holder.instance_variable_get(:@ironmon_collection_entry_owner_role) || role : "unknown",
          "current_controller" => role
        }
      end

      def self.status_committed(battler, before, after, requested_count = nil)
        return if before == after || !active_battle?(battler.battle)
        return if GameplayCollection.current_context["combat_initialization"]
        local = GameplayCollection.current_context
        if after && after != :NONE && after.to_s != "NONE"
          episode = GameplayCollection.next_id("status_episode")
          battler.instance_variable_set(:@ironmon_collection_status_episode, episode)
          battler.instance_variable_set(:@ironmon_collection_status_cause, cause)
          pokemon = read(battler, :pokemon)
          pokemon.instance_variable_set(:@ironmon_collection_status_episode, episode) if pokemon
          pokemon.instance_variable_set(:@ironmon_collection_status_cause, cause) if pokemon
          emit("status.major_application_resolved", {
            "source_actor" => local["source_actor"], "target" => actor_ref(battler),
            "effect_source" => cause, "status_requested" => native_id(after),
            "status_before" => native_id(before), "status_after_application" => native_id(after),
            "status_mode" => after.to_s == "POISON" && requested_count.to_i > 0 ? "bad_poison" : "normal",
            "native_status_count" => requested_count.nil? ? read(battler, :statusCount) : requested_count,
            "status_episode_id" => episode, "application_outcome" => "applied", "prevention_event_id" => nil
          }, battler.battle)
        else
          episode = battler.instance_variable_get(:@ironmon_collection_status_episode)
          cleanup = local["combat_cleanup"]
          kind = cleanup ? "faint_cleanup" : (local["status_cure_reason"] || local["source_kind"] || "unknown")
          emit("status.major_cure_resolved", {
            "curer" => local["source_actor"], "target" => actor_ref(battler), "effect_source" => cause,
            "status_episode_id" => episode, "status_before" => native_id(before), "status_after" => native_id(after),
            "cure_reason" => kind, "cure_outcome" => cleanup ? "cleanup_clear" : "cured",
            "item_id" => local["source_kind"] == "item" ? local["source_id"] : nil,
            "naturally_recovered" => ["natural_wake", "natural_thaw"].include?(kind)
          }, battler.battle)
          battler.instance_variable_set(:@ironmon_collection_status_episode, nil)
          battler.instance_variable_set(:@ironmon_collection_status_cause, nil)
          pokemon = read(battler, :pokemon)
          pokemon.instance_variable_set(:@ironmon_collection_status_episode, nil) if pokemon
          pokemon.instance_variable_set(:@ironmon_collection_status_cause, nil) if pokemon
        end
      end

      def self.move_pp_changed(move, before, after)
        return if before.nil? || before == after || !active_battle?(read(move, :battle) || move.instance_variable_get(:@battle))
        battle = move.instance_variable_get(:@battle)
        actor = active_actors(battle).find { |battler| read(battler, :moves, []).any? { |candidate| candidate.equal?(move) } }
        return if !actor
        slot = actor.moves.index(move)
        local = GameplayCollection.current_context
        return if local["combat_initialization"] || local["combat_cleanup"]
        mode = before < 0 || after < 0 ? "unlimited" : (read(move, :realMove) ? "finite" : "temporary")
        if after < before
          emit("pp.expenditure_resolved", {
            "move_owner" => actor_ref(actor), "drain_source" => local["source_actor"], "effect_source" => cause,
            "move_slot" => slot, "move_id" => native_id(move), "pp_mode" => mode,
            "pp_before" => before, "pp_after" => after,
            "requested_cost" => local["pp_requested_cost"], "actual_pp_lost" => mode == "unlimited" ? nil : before - after,
            "cost_kind" => local["pp_cost_kind"] || "unknown"
          }, battle)
        else
          requested_pp = local["pp_requested"] || (local["source_id"] == "LEPPABERRY" ? 10 : (local["pp_restoration_kind"] == "full" ? [read(move, :total_pp) - before, 0].max : nil))
          emit("pp.restoration_resolved", {
            "recipient" => actor_ref(actor), "effect_source" => cause,
            "slot_results" => [{
              "slot" => slot, "slot_revision" => actor_ref(actor)["appearance_id"],
              "pool_kind" => read(move, :realMove) ? "mirrored_persistent" : "temporary",
              "move_id" => native_id(move), "pp_mode" => mode,
              "pp_before" => before, "pp_after" => after, "max_pp" => read(move, :total_pp),
              "nominal_pp" => requested_pp, "requested_pp" => requested_pp, "actual_pp" => after - before,
              "capacity_excess_pp" => requested_pp ? [requested_pp - (after - before), 0].max : nil,
              "persistent_move_updated" => read(move, :realMove) ? true : false
            }],
            "actual_total_pp" => after - before, "successful_slot_count" => 1,
            "transaction_context" => local["source_kind"] == "item" ? "held" : "other",
            "restoration_transaction_id" => local["pp_transaction_id"] || local["action_id"],
            "outcome" => "success"
          }, battle)
        end
      end

      def self.handler(battle, family, args)
        return yield if !actual_resolution?
        registry_name = family.to_s.sub(/\Atrigger/, "")
        return yield if !BattleHandlers.const_defined?(registry_name, false)
        registry = BattleHandlers.const_get(registry_name)
        return yield if !registry.respond_to?(:[]) || !registry[args[0]]
        is_item = registry.is_a?(ItemHandlerHash)
        bearer, recipient, numeric_before = if defined?(NativeEvidence)
          NativeEvidence.handler_actors(family, args)
        else
          [args.find { |entry| entry.is_a?(PokeBattle_Battler) }, nil, nil]
        end
        identifier = native_id(args[0])
        activation_record = is_item && defined?(ItemIdentity) && bearer ? (GameplayCollection.current_context["forced_item_record"] || ItemIdentity.held(bearer.pokemon, identifier, nil, owner_role(bearer))) : nil
        activation_group = is_item ? GameplayCollection.open_group("held_item_activation", { "source_kind" => "item", "source_id" => identifier, "source_actor" => actor_ref(bearer) }) : nil
        hashes = args.select { |entry| entry.is_a?(Hash) }
        before = hashes.map { |entry| value(entry) }
        revision = state(battle)[:mutation_count]
        previous_observer = Thread.current[:ironmon_collection_handler_observer]
        observer = { :parent => previous_observer, :events => [] }
        Thread.current[:ironmon_collection_handler_observer] = observer
        begin
        result = with_context({
          "source_kind" => is_item ? "item" : "ability", "source_id" => identifier,
          "source_actor" => actor_ref(bearer), "pp_transaction_id" => GameplayCollection.next_id("resource_transaction"),
          "effect_group_id" => activation_group || GameplayCollection.current_context["effect_group_id"],
          "healing_amount_kind" => is_item && defined?(Resources) ? Resources.healing_model(args[0]) : "unknown"
        }, battle) { yield }
        ensure
          Thread.current[:ironmon_collection_handler_observer] = previous_observer
          GameplayCollection.close_group(activation_group, { "reason" => "native_exception" }, "interrupted") if activation_group && $!
        end
        changes = []
        hashes.each_with_index do |entry, index|
          value(entry).each do |key, after|
            previous = before[index][key]
            changes << { "key" => key, "before" => previous, "after" => after } if previous != after
          end
        end
        own_events = observer[:events].select do |event|
          source = event["payload"]["cause"] || event["payload"]["effect_source"] || {}
          (source["canonical_id"] || source["source_id"] || source["mechanic_id"]) == identifier && (source["source_actor"] || source["actor"]) == actor_ref(bearer)
        end
        capability = family.to_s.match?(/StatusCheck|StatusImmunity|MoveImmunity|MoveBlocking|StatLossImmunity/)
        changed = !changes.empty? || own_events.any? { |event| NativeEvidence.applied_fact?(event) } || (!capability && result == true) || (result.is_a?(Numeric) && numeric_before.is_a?(Numeric) && result != numeric_before)
        NativeEvidence.frames.each do |frame|
          frame[:handler_outcomes] ||= []
          frame[:handler_outcomes] << { :id => identifier, :bearer => bearer, :applied => changed }
        end
        raised = own_events.select { |event| event["type"] == "stat_stage.changed" && event["payload"]["delta"].to_i > 0 }.map do |event|
          entry = event["payload"]
          { "stat_id" => entry["stat_id"], "delta" => entry["delta"], "target" => entry["target"] }
        end
        if is_item
          payload = item_common(args[0], bearer).merge(
            "holder" => actor_ref(bearer), "recipient" => actor_ref(recipient || bearer), "activation_kind" => GameplayCollection.current_context["phase"] == "end_of_round" ? "end_of_round" : "own_held",
            "effect_family" => NativeEvidence.effect_family(family, own_events, true), "effect_succeeded" => changed, "consumed_quantity" => own_events.count { |event| event["type"] == "item.disposition_resolved" && event["payload"]["disposition"] == "consumed" },
            "effect_source" => { "kind" => "item", "canonical_id" => identifier, "actor" => actor_ref(bearer) }, "outcome" => changed ? "success" : "no_effect",
            "handler_family" => family.to_s, "modifier_changes" => changes,
            "effect_evidence_coverage" => "observed_native_effect", "consequence_event_ids" => own_events.map { |event| event["event_id"] }
          )
          pending = Thread.current[:ironmon_collection_item_effect_queue]
          if pending
            record = activation_record
            ItemIdentity.common(payload, record) if defined?(ItemIdentity)
            pending << { :payload => payload, :battle => battle, :record => record, :group => activation_group, :context => GameplayCollection.current_context.merge("effect_group_id" => activation_group, "activation_resource_record" => record) }
          else
            payload["consumed_quantity"] = activation_record && activation_record["quantity"] == 0 ? 1 : 0
            complete_item_activation(payload, battle, activation_record, activation_group)
          end
        else
          emit("ability.effect_resolved", {
            "bearer" => actor_ref(bearer), "target" => actor_ref(recipient), "ability_id" => identifier,
            "handler_family" => family.to_s, "effect_family" => NativeEvidence.effect_family(family, own_events), "effect_succeeded" => changed,
            "consequence_event_ids" => own_events.map { |event| event["event_id"] }, "suppressed_at_resolution" => false,
            "invocation_context" => "actual_resolution", "outcome" => changed ? "success" : "no_effect",
            "effect_evidence" => {
              "resolution_phase" => GameplayCollection.current_context["phase"] || "move",
              "applied" => changed, "modifier_changes" => changes, "numeric_before" => numeric_before, "numeric_after" => result.is_a?(Numeric) ? result : nil,
              "consequence_kinds" => own_events.map { |event| event["type"] }.uniq, "coverage" => "observed_native_effect",
              "positive_stages_gained" => raised.inject(0) { |total, entry| total + entry["delta"] },
              "raised_stat_components" => raised,
              "target_is_bearer" => !raised.empty? && raised.all? { |entry| entry["target"] == actor_ref(bearer) }
            }
          }, battle)
        end
        return result
      end

      def self.complete_item_activation(payload, battle, record, group)
        GameplayCollection.with_context("effect_group_id" => group, "activation_resource_record" => record) do
          emit("held_item.effect_resolved", payload, battle)
          context = GameplayCollection.current_context
          kind = context["item_activation_kind"] || "own_held"
          common = payload.dup
          emit("item.use_resolved", common.merge("user" => payload["holder"], "target" => payload["recipient"], "use_context" => "forced_effect", "activation_kind" => kind,
            "requested_quantity" => 1, "effect_families" => [payload["effect_family"]], "selected_move_slot" => nil), battle)
        end
        GameplayCollection.close_group(group, { "effect_succeeded" => payload["effect_succeeded"] }) if group
      end
    end
  end
end

module IronmonCollectionStatusHooks
  def pbInflictStatus(status, count = 0, message = nil, user = nil)
    combat = Ironmon::GameplayCollection::Combat
    return super if !combat.active_battle?(@battle)
    previous = @ironmon_collection_status_application
    @ironmon_collection_status_application = { :before => @status, :count => count, :committed => false, :emitted => false }
    return combat.with_context({ "source_actor" => combat.actor_ref(user) || Ironmon::GameplayCollection.current_context["source_actor"] }, @battle) { super }
  ensure
    @ironmon_collection_status_application = previous if defined?(previous)
  end

  def status=(status)
    before = @status
    combat = Ironmon::GameplayCollection::Combat
    result = combat.with_context({ "combat_resource_mutation" => true, "resource_mirror_mutation" => true }, @battle) { super }
    pending = @ironmon_collection_status_application
    if pending && !pending[:emitted]
      pending[:committed] = true
    else
      combat.status_committed(self, before, @status) if before
    end
    return result
  end

  def statusCount=(count)
    combat = Ironmon::GameplayCollection::Combat
    before = @statusCount
    result = combat.with_context({ "combat_resource_mutation" => true, "resource_mirror_mutation" => true }, @battle) { super }
    pending = @ironmon_collection_status_application
    if pending && pending[:committed] && !pending[:emitted]
      pending[:emitted] = true
      combat.status_committed(self, pending[:before], @status, @statusCount)
    elsif !pending && before && before != @statusCount && @status && @status != :NONE && combat.active_battle?(@battle)
      combat.emit("status.major_progressed", {
        "target" => combat.actor_ref(self), "status_episode_id" => @ironmon_collection_status_episode,
        "status_id" => combat.native_id(@status), "native_count_before" => before, "native_count_after" => @statusCount,
        "progress_kind" => "turn_check", "prevented_action_ref" => nil, "residual_hp_event_id" => nil
      }, @battle)
    end
    return result
  end

  def pbCanInflictStatus?(status, user, show_messages, *args)
    result = super
    combat = Ironmon::GameplayCollection::Combat
    if result == false && combat.actual_resolution? && (combat.current_move(@battle) || Ironmon::GameplayCollection.current_context["source_kind"])
      combat.emit("status.major_prevention_resolved", {
        "source_actor" => combat.actor_ref(user), "target" => combat.actor_ref(self), "effect_source" => combat.cause,
        "attempt_event_id" => Ironmon::GameplayCollection.next_id("status_attempt"), "status_requested" => combat.native_id(status),
        "prevention_kind" => "other", "preventer_id" => nil, "preventer_actor" => nil,
        "bypassed_preventers" => nil, "resolution_context" => "actual_gameplay_attempt",
        "classification_coverage" => "unknown"
      }, @battle)
    end
    return result
  end

  def pbContinueStatus(*args, &block)
    combat = Ironmon::GameplayCollection::Combat
    retained = @ironmon_collection_status_cause || {}
    return combat.with_context({
      "source_kind" => "status", "source_id" => combat.native_id(@status),
      "source_actor" => retained["source_actor"], "initiating_actor" => retained["initiating_actor"],
      "loss_kind" => "residual", "status_episode_id" => @ironmon_collection_status_episode
    }, @battle) { super(*args, &block) }
  end

  def pbSetPP(move, amount)
    combat = Ironmon::GameplayCollection::Combat
    return combat.with_context({ "combat_resource_mutation" => true, "resource_mirror_mutation" => true }, @battle) { super }
  end

  def pbReducePP(move)
    combat = Ironmon::GameplayCollection::Combat
    kind = Ironmon::GameplayCollection.current_context["pp_cost_kind"] || "move_use"
    return combat.with_context({ "pp_requested_cost" => 1, "pp_cost_kind" => kind }, @battle) { super }
  end

  def pbReducePPOther(move)
    combat = Ironmon::GameplayCollection::Combat
    return combat.with_context({ "pp_requested_cost" => 1, "pp_cost_kind" => "other_hostile_drain" }, @battle) { super }
  end

  def ability=(ability)
    before = @ability_id
    combat = Ironmon::GameplayCollection::Combat
    result = super
    if before != @ability_id && combat.active_battle?(@battle) && !Ironmon::GameplayCollection.current_context["combat_initialization"]
      combat.emit("ability.identity_changed", {
        "target" => combat.actor_ref(self), "source_actor" => Ironmon::GameplayCollection.current_context["source_actor"],
        "effect_source" => combat.cause, "ability_before" => combat.native_id(before), "ability_after" => combat.native_id(@ability_id),
        "generated_base_ability_id" => combat.native_id(@original_ability), "change_kind" => "other",
        "persistent_pokemon_updated" => false, "form_before" => combat.read(self, :form), "form_after" => combat.read(self, :form)
      }, @battle)
    end
    return result
  end

  def item=(item)
    before = @item_id
    combat = Ironmon::GameplayCollection::Combat
    result = combat.with_context({ "combat_resource_mutation" => true, "resource_mirror_mutation" => true }, @battle) { super }
    if before != @item_id && combat.active_battle?(@battle) && !Ironmon::GameplayCollection.current_context["combat_initialization"]
      combat.emit("held_item.assignment_resolved", combat.item_common(@item_id || before, self).merge(
        "from_actor" => before ? combat.actor_ref(self) : nil, "to_actor" => @item_id ? combat.actor_ref(self) : nil,
        "from_container_ref" => nil, "to_container_ref" => nil, "assignment_kind" => "other",
        "previous_target_item_id" => combat.native_id(before), "effect_source" => combat.cause, "outcome" => "success"
      ), @battle)
    end
    return result
  end

  def pbConsumeItem(recoverable = true, *args)
    combat = Ironmon::GameplayCollection::Combat
    item = @item_id
    result = combat.with_context({ "source_kind" => "item", "source_id" => combat.native_id(item) }, @battle) { super }
    if item && @item_id != item && combat.active_battle?(@battle)
      combat.emit("item.disposition_resolved", combat.item_common(item, self).merge(
        "holder" => combat.actor_ref(self), "consumer" => combat.actor_ref(self), "disposition" => Ironmon::GameplayCollection.current_context["item_disposition"] || "consumed",
        "quantity" => 1, "recoverable" => recoverable, "recovery_parent_resource_ref" => nil, "effect_source" => combat.cause
      ), @battle)
    end
    return result
  end
end

module IronmonCollectionMovePpHooks
  def pp=(amount)
    before = @pp
    result = Ironmon::GameplayCollection::Combat.with_context({ "resource_mirror_mutation" => true }, @battle) { super }
    Ironmon::GameplayCollection::Combat.move_pp_changed(self, before, @pp)
    return result
  end
end

module IronmonCollectionBattleItemHooks
end

[:pbUseItemOnPokemon, :pbUseItemOnBattler, :pbUseItemInBattle, :pbUsePokeBallInBattle].each do |method_name|
  IronmonCollectionBattleItemHooks.define_method(method_name) do |item, index, user, *args, &block|
    combat = Ironmon::GameplayCollection::Combat
    return super(item, index, user, *args, &block) if !combat.active_battle?(self)
    target = method_name == :pbUseItemOnPokemon ? pbFindBattler(index, user.index) : (@battlers[index] if index && index >= 0)
    combat.with_action(self, user, "item") do
      combat.with_context({
        "source_kind" => "item", "source_id" => combat.native_id(item), "source_actor" => combat.actor_ref(user),
        "reserved_resource_record" => combat.state(self).fetch(:bag_reservations, {})[user.index],
        "pp_transaction_id" => Ironmon::GameplayCollection.next_id("resource_transaction"),
        "healing_amount_kind" => defined?(Ironmon::GameplayCollection::Resources) ? Ironmon::GameplayCollection::Resources.healing_model(item) : "unknown"
      }, self) do
        common = combat.item_common(item, user).merge("user" => combat.actor_ref(user), "target" => combat.actor_ref(target), "use_context" => "bag_battle", "requested_quantity" => 1)
        combat.emit("item.use_attempted", common.merge("attempt_stage" => "submitted", "selected_move_slot" => @choices[user.index][3], "rejection_reason" => nil), self)
        before_revision = combat.state(self)[:mutation_count]
        result = super(item, index, user, *args, &block)
        consumed = @choices[user.index][1].nil?
        succeeded = combat.state(self)[:mutation_count] != before_revision
        combat.emit("item.use_resolved", common.merge(
          "consumed_quantity" => consumed ? 1 : 0, "effect_families" => [], "effect_succeeded" => succeeded,
          "outcome" => consumed ? "success" : "no_effect"
        ), self)
        result
      end
    end
  end
end

handler_hooks = Module.new
BattleHandlers.singleton_methods(false).grep(/^trigger.*(?:Ability|Item)/).each do |method_name|
  handler_hooks.define_method(method_name) do |*args, &block|
    combat = Ironmon::GameplayCollection::Combat
    battle = args.find { |entry| entry.is_a?(PokeBattle_Battle) }
    battle ||= args.find { |entry| entry.is_a?(PokeBattle_Battler) }&.battle
    battle ||= combat.current_battle
    combat.handler(battle, method_name, args) { super(*args, &block) }
  end
end

Ironmon::GameplayCollection::Combat.install(PokeBattle_Battler, IronmonCollectionStatusHooks)
Ironmon::GameplayCollection::Combat.install(PokeBattle_Move, IronmonCollectionMovePpHooks)
Ironmon::GameplayCollection::Combat.install(PokeBattle_Battle, IronmonCollectionBattleItemHooks)
Ironmon::GameplayCollection::Combat.install(BattleHandlers.singleton_class, handler_hooks)
