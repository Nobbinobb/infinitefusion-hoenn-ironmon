module Ironmon
  module GameplayCollection
    module Combat
      DECISION_OUTCOMES = { 0 => "aborted", 1 => "win", 2 => "loss", 3 => "escape", 4 => "capture", 5 => "draw" }

      def self.admit(battle)
        return if !GameplayCollection.enabled?
        previous = @current_battle
        @current_battle = battle
        identifier = GameplayCollection.next_id("battle")
        authored = (Ironmon.instance_variable_get(:@pending_area_trainer_entries) || []).dup
        authored << Ironmon.instance_variable_get(:@current_area_trainer_entry)
        states[battle.object_id] = {
          :id => identifier, :previous => previous, :closed => false, :ended => false,
          :round => 0, :round_open => false, :phases => [], :actors => {}, :known_individuals => {}, :appearances => {},
          :choices => {}, :order_version => 0, :moves => [], :actions => [],
          :hit_index => nil, :hit_applications => {}, :hp_loss_refs => [], :last_hp_loss => {},
          :faints => {}, :effect_episodes => {}, :effect_provenance => {}, :retired_effect_provenance => {}, :fact_count => 0, :mutation_count => 0,
          :capture_count => 0, :capture_failures => 0, :captures => [],
          :authored => authored.compact.uniq, :departed => {}
        }
        field = read(battle, :field)
        field.instance_variable_set(:@ironmon_collection_battle, battle) if field
        observe_array(read(field, :effects), battle, "field", nil)
        read(battle, :sides, []).each_with_index { |side, index| observe_array(read(side, :effects), battle, "side", index) }
        read(battle, :positions, []).each_with_index { |position, index| observe_array(read(position, :effects), battle, "position", index) }
        trainers = read(battle, :opponent, []) || []
        emit("battle.started", {
          "battle_kind" => read(battle, :trainerBattle?, false) ? "trainer" : (read(battle, :wildBattle?, false) ? "wild" : "other"),
          "format" => {
            "player_slots" => read(battle, :sideSizes, [0, 0])[0], "opponent_slots" => read(battle, :sideSizes, [0, 0])[1],
            "player_trainers" => (read(battle, :player, []) || []).length, "opponent_trainers" => trainers.length
          },
          "encounter" => {
            "authored_entry_ids" => state(battle)[:authored], "map_id" => defined?($game_map) && $game_map ? $game_map.map_id : nil,
            "encounter_source_id" => nil, "trainer_class_ids" => trainers.map { |trainer| native_id(read(trainer, :trainer_type)) }, "story_keys" => []
          },
          "eligibility" => {
            "internal" => read(battle, :internalBattle, false), "tutorial" => false,
            "debug" => read(battle, :debug, false), "recorded_replay" => defined?(PokeBattle_BattlePlayerModule) && battle.is_a?(PokeBattle_BattlePlayerModule) ? true : false,
            "attempt_active" => !!(Ironmon.current_run_attempt && Ironmon.current_run_attempt["result"] == "active"), "policy_version" => 1
          },
          "initial_field" => battle_snapshot(battle)
        }, battle)
        return identifier
      end

      def self.entered(battler)
        battle = battler.battle
        return if !active_battle?(battle) || read(battler, :dummy, false)
        current = state(battle)
        actor = actor_ref(battler)
        return if !actor
        current[:departed].delete(actor["actor_id"])
        battler.instance_variable_set(:@ironmon_collection_entry_owner_role, owner_role(battler))
        observe_array(read(battler, :effects), battle, "battler", battler)
        observe_stages(read(battler, :stages), battler)
        pokemon = read(battler, :pokemon)
        battler.instance_variable_set(:@ironmon_collection_status_episode, pokemon.instance_variable_get(:@ironmon_collection_status_episode)) if pokemon
        battler.instance_variable_set(:@ironmon_collection_status_cause, pokemon.instance_variable_get(:@ironmon_collection_status_cause)) if pokemon
        snapshot = actor_snapshot(battler)
        emit("battler.entered", {
          "actor" => actor, "entry_kind" => current[:round] == 0 ? "initial" : (GameplayCollection.current_context["entry_kind"] || "replacement"),
          "party_instance_id" => actor["party_instance_id"], "entry_snapshot" => snapshot,
          "seen_disclosure" => owner_role(battler) == "player" ? "player_owned" : "visibly_entered"
        }, battle)
        if defined?(Ownership)
          kind = owner_role(battler) == "player" ? "owned_battle_entry" : (read(battle, :trainerBattle?, false) ? "trainer_battle_entry" : "wild_battle_entry")
          Ownership.disclosed(read(battler, :pokemon), kind, actor["appearance_id"])
        end
      end

      def self.left(battler, kind = "other")
        return if !battler || !active_battle?(read(battler, :battle))
        actor = actor_ref(battler)
        return if !actor || state(battler.battle)[:departed][actor["actor_id"]]
        state(battler.battle)[:departed][actor["actor_id"]] = true
        emit("battler.left", {
          "actor" => actor, "exit_kind" => kind,
          "destination" => kind == "recalled" ? "bench" : (kind == "captured" ? "caught_pending" : "none"),
          "exit_snapshot" => actor_snapshot(battler), "cause" => cause
        }, battler.battle)
      end

      def self.start_round(battle)
        current = state(battle)
        return if !current || current[:round_open]
        current[:round] = read(battle, :turnCount, 0) + 1
        current[:round_open] = true
        current[:phases] = []
        current[:field_exposure_segments] = []
        current[:round_damage] = {}
        exposure_checkpoint(battle, "round_start")
        emit("round.started", {
          "round_number" => current[:round], "active_actors" => active_actors(battle).map { |actor| actor_ref(actor) },
          "field_state_ref" => battle_snapshot(battle)
        }, battle)
      end

      def self.finish_round(battle, completion)
        current = state(battle)
        return if !current || !current[:round_open]
        snapshots = active_actors(battle).map { |actor| actor_snapshot(actor) }
        (current[:round_damage] || {}).each do |identity, entry|
          snapshot = snapshots.find { |candidate| candidate.dig("actor", "appearance_id") == identity }
          snapshot ||= { "actor" => entry["actor"], "participation_kind" => "source_only" }.tap { |candidate| snapshots << candidate }
          snapshot["actual_damage_dealt"] = entry["total"]
        end
        snapshots.each { |entry| entry["actual_damage_dealt"] ||= 0 }
        emit("round.finished", {
          "round_number" => current[:round], "completed_phases" => current[:phases].dup,
          "completion" => completion, "actor_state_summaries" => snapshots,
          "field_exposure_segments" => current[:field_exposure_segments] || []
        }, battle)
        current[:round_open] = false
      end

      def self.exposure_checkpoint(battle, phase = nil)
        current = state(battle)
        return if !current || !current[:round_open]
        segment = { "weather" => native_id(read(read(battle, :field), :weather)), "terrain" => native_id(read(read(battle, :field), :terrain)),
          "phase" => phase || GameplayCollection.current_context["phase"] || "resolution", "count" => 1 }
        current[:field_exposure_segments] ||= []
        current[:field_exposure_segments] << segment if current[:field_exposure_segments].last != segment
      end

      def self.round_damage(battle, target, amount, damage_cause)
        current = state(battle)
        source = damage_cause["origin_instigator"] || damage_cause["source_actor"]
        return if !current || !current[:round_open] || !source || source["owner_role"] == target["owner_role"] || amount <= 0
        identity = source["appearance_id"] || source["actor_id"]
        current[:round_damage] ||= {}
        current[:round_damage][identity] ||= { "actor" => source, "total" => 0 }
        current[:round_damage][identity]["total"] += amount
      end

      def self.terminal_sides(battle)
        return [0, 1].map do |side|
          party = battle.pbParty(side)
          able = party.compact.count { |pokemon| read(pokemon, :hp, 0) > 0 && !read(pokemon, :egg?, false) }
          active = active_actors(battle).count { |actor| actor.index % 2 == side && actor.hp > 0 }
          {
            "side" => { "side_id" => side }, "engine_able_party_count" => able,
            "active_able_count" => active, "reserve_able_count" => [able - active, 0].max,
            "engine_all_fainted" => battle.pbAllFainted?(side), "counting_scope" => "all_engine_side_party",
            "observability" => "engine_private"
          }
        end
      end

      def self.finish_battle(battle, snapshots, side_states, native_decision)
        return if !active_battle?(battle)
        current = state(battle)
        finish_round(battle, "battle_ended_mid_round")
        decision = read(battle, :decision, native_decision)
        terminal = defined?(World) && World.instance_variable_get(:@terminal_battle)
        emit("battle.ended", {
          "event_id" => current[:end_event_id],
          "engine_decision" => decision, "native_terminal_decision" => native_decision,
          "outcome" => DECISION_OUTCOMES.fetch(decision, "other"), "end_reason" => current[:end_reason] || "unknown",
          "final_actor_states" => snapshots, "winning_action_ref" => current[:last_action_id],
          "run_completion_ref" => terminal && terminal["battle_id"] == current[:id] ? terminal["run_completion_ref"] : nil, "authored_entry_ids" => current[:authored], "terminal_side_states" => side_states
        }, battle)
        current[:ended] = true
      end

      def self.release_battle(battle, interrupted, reason = "engine_abort")
        current = state(battle)
        return if !current
        if interrupted || !current[:ended]
          finish_round(battle, "interrupted")
          emit("battle.interrupted", {
            "reason" => reason, "last_durable_sequence" => GameplayCollection.instance_variable_get(:@journal)&.metrics&.fetch("durable_sequence", "0") || "0",
            "completion_state" => current[:ended] ? "terminal_result_committed" : "no_result", "partial_summary_ref" => nil
          }, battle)
        end
        current[:closed] = true
        @current_battle = current[:previous]
        states.delete(battle.object_id)
      end

      def self.interrupt_current(reason = "save_load")
        battle = @current_battle
        current = state(battle)
        return if !current
        current[:moves].dup.reverse_each do |invocation|
          with_context({ "move_invocation_id" => invocation[:id] }, battle) { finish_move(invocation[:actor], invocation, false) }
        end
        current[:actions].dup.reverse_each do |identifier|
          GameplayCollection.close_group(identifier, { "reason" => reason }, "interrupted")
        end
        current[:actions].clear
        release_battle(battle, true, reason)
      end

      def self.usable_balls
        return nil if !defined?($PokemonBag) || !$PokemonBag || !$PokemonBag.respond_to?(:pockets)
        total = 0
        $PokemonBag.pockets.each do |pocket|
          pocket.each do |item, quantity|
            data = GameData::Item.try_get(item)
            total += quantity.to_i if data && read(data, :is_poke_ball?, false)
          end
        end
        return total
      end
    end
  end
end

module IronmonCollectionBattleHooks
  def pbStartBattleCore(*args)
    combat = Ironmon::GameplayCollection::Combat
    return super if !Ironmon::GameplayCollection.enabled?
    combat.observe { combat.admit(self) }
    return super if !combat.active_battle?(self)
    completed = false
    begin
      result = combat.with_context({ "phase" => "battle_start" }, self) { super }
      completed = true
      return result
    ensure
      combat.release_battle(self, !completed)
    end
  end

  def pbEndOfBattle(*args)
    combat = Ironmon::GameplayCollection::Combat
    return super if !combat.active_battle?(self)
    snapshots = combat.active_actors(self).map { |actor| combat.actor_snapshot(actor) }
    side_states = combat.terminal_sides(self)
    terminal_decision = @decision
    current = combat.state(self)
    current[:end_event_id] ||= Ironmon::GameplayCollection.next_id("battle_end")
    losing_role = [2, 5].include?(terminal_decision) ? "player" : "opponent"
    losses = current[:last_hp_loss].values.select { |entry| entry["hp_after"] == 0 && entry.dig("target", "owner_role") == losing_role }
    losses.sort_by! { |entry| entry["sequence"].to_i }
    Ironmon::GameplayCollection::World.instance_variable_set(:@terminal_battle, {
      "event_id" => current[:end_event_id], "battle_id" => current[:id], "attempt_id" => Ironmon::GameplayCollection.attempt_id,
      "native_decision" => terminal_decision, "cause" => losses.last && losses.last["cause"] })
    combat.active_actors(self).each { |actor| combat.left(actor, "battle_end") }
    result = combat.with_context({ "phase" => "battle_end", "combat_cleanup" => true }, self) { super }
    combat.finish_battle(self, snapshots, side_states, terminal_decision)
    return result
  end

  def pbCommandPhase(*args)
    combat = Ironmon::GameplayCollection::Combat
    return super if !combat.active_battle?(self)
    combat.start_round(self)
    result = combat.with_context({ "phase" => "command" }, self) { super }
    combat.state(self)[:phases] << "command"
    @choices.each_with_index { |_choice, index| combat.choice_committed(self, index) }
    return result
  end

  def pbAttackPhase(*args)
    combat = Ironmon::GameplayCollection::Combat
    result = combat.with_context({ "phase" => "attack" }, self) { super }
    combat.state(self)[:phases] << "attack" if combat.active_battle?(self)
    return result
  end

  def pbEndOfRoundPhase(*args)
    combat = Ironmon::GameplayCollection::Combat
    result = combat.with_context({ "phase" => "end_of_round", "source_kind" => "environment", "source_id" => nil, "source_actor" => nil }, self) { super }
    if combat.active_battle?(self)
      combat.state(self)[:phases] << "end_of_round"
      combat.finish_round(self, "complete")
    end
    return result
  end

  def pbReplace(index, *args)
    combat = Ironmon::GameplayCollection::Combat
    combat.left(@battlers[index], "recalled") if combat.active_battle?(self)
    return combat.with_context({ "entry_kind" => "replacement" }, self) { super }
  end

  def pbOnActiveOne(battler)
    combat = Ironmon::GameplayCollection::Combat
    return combat.with_context({ "phase" => "entry", "loss_kind" => "hazard" }, self) { super }
  end

  def pbRun(index, during_battle = false)
    combat = Ironmon::GameplayCollection::Combat
    return super if !combat.active_battle?(self)
    actor = @battlers[index]
    return combat.with_action(self, actor, "run") do
      result = super
      kind = if result == 1
        trainerBattle? ? "forfeited" : "escaped"
      elsif result == -1
        "failed"
      else
        trainerBattle? ? "cancelled" : "not_permitted"
      end
      combat.state(self)[:end_reason] = trainerBattle? ? "forfeit" : "escape" if result == 1
      combat.emit("escape.resolved", {
        "actor" => combat.actor_ref(actor), "request_kind" => trainerBattle? ? "forfeit" : "run", "result" => kind,
        "during_replacement" => during_battle, "cause" => combat.cause,
        "request_id" => Ironmon::GameplayCollection.next_id("escape_request")
      }, self)
      result
    end
  end

  def pbShowAbilitySplash(battler, *args)
    result = super
    combat = Ironmon::GameplayCollection::Combat
    if combat.active_battle?(self)
      actor = combat.actor_ref(battler)
      revealed = battler.instance_variable_get(:@ironmon_collection_revealed_abilities) || {}
      key = [actor && actor["appearance_id"], combat.native_id(battler.ability_id)]
      combat.emit("ability.revealed", {
        "bearer" => actor, "ability_id" => key[1], "reveal_kind" => "splash",
        "was_already_revealed" => revealed[key] == true, "source_effect_event_id" => nil
      }, self)
      revealed[key] = true
      battler.instance_variable_set(:@ironmon_collection_revealed_abilities, revealed)
    end
    return result
  end
end

module IronmonCollectionBattlerLifecycleHooks
  def pbInitialize(*args)
    combat = Ironmon::GameplayCollection::Combat
    result = combat.with_context({ "combat_initialization" => true }, @battle) { super }
    combat.entered(self)
    return result
  end

  def pbFaint(*args)
    combat = Ironmon::GameplayCollection::Combat
    if combat.active_battle?(@battle) && @hp <= 0 && !@fainted
      current = combat.state(@battle)
      actor = combat.actor_ref(self)
      if actor && !current[:faints][actor["actor_id"]]
        identifier = Ironmon::GameplayCollection.next_id("faint")
        loss = current[:last_hp_loss][actor["actor_id"]]
        current[:faints][actor["actor_id"]] = { :id => identifier, :hit_id => loss && loss["hit_ref"] }
        source = loss && loss["cause"] && loss["cause"]["source_actor"]
        credit = source && source["owner_role"] == "player" ? (loss["loss_kind"] == "move_hit" ? "player_direct" : "player_indirect") : "unknown"
        combat.emit("battler.fainted", {
          "target" => actor, "faint_instance_id" => identifier,
          "terminal_hp_loss_ref" => loss && loss["sequence"], "pre_cleanup_snapshot" => combat.actor_snapshot(self),
          "source_credit" => credit, "cause" => loss ? loss["cause"] : combat.cause
        }, @battle)
        combat.left(self, "fainted")
      end
    end
    return combat.with_context({ "combat_cleanup" => true }, @battle) { super }
  end

  def pbReset(*args)
    combat = Ironmon::GameplayCollection::Combat
    combat.left(self, "other") if combat.active_battle?(@battle)
    return combat.with_context({ "combat_cleanup" => true }, @battle) { super }
  end
end

module IronmonCollectionCaptureHooks
  def pbThrowPokeBall(index, ball, *args)
    combat = Ironmon::GameplayCollection::Combat
    return super if !combat.active_battle?(self)
    current = combat.state(self)
    previous = current[:throw]
    current[:capture_count] += 1
    record = {
      :id => Ironmon::GameplayCollection.next_id("capture"), :ball => combat.native_id(ball),
      :ordinal => current[:capture_count], :failed => current[:capture_failures],
      :usable_balls => combat.usable_balls, :calculation => false,
      :shakes => nil, :target => nil, :snapshot => nil
    }
    reserved = Ironmon::GameplayCollection.current_context["reserved_resource_record"]
    if reserved && reserved["item_id"] == record[:ball] && reserved["quantity"].to_i > 0 && record[:usable_balls]
      record[:usable_balls] += reserved["quantity"]
    end
    current[:throw] = record
    completed = false
    begin
      result = super
      completed = true
      return result
    ensure
      if !record[:calculation]
        combat.emit("capture.attempted", {
          "actor" => nil, "target" => record[:target], "throw_id" => record[:id], "ball_resource_ref" => nil,
          "target_hp" => record.dig(:snapshot, "hp"), "target_max_hp" => record.dig(:snapshot, "max_hp"), "target_status_ref" => record.dig(:snapshot, "status_ref"),
          "attempt_ordinal_in_battle" => record[:ordinal], "usable_balls_before" => record[:usable_balls],
          "gate_outcome" => record[:gate] || "other"
        }, self)
      end
      outcome = !completed ? "interrupted" : (record[:shakes] == 4 ? "caught" : (record[:calculation] ? "escaped" : "blocked"))
      snapshot = record[:snapshot] || {}
      combat.emit("capture.resolved", {
        "throw_id" => record[:id], "target" => record[:target], "result" => outcome, "shakes" => record[:shakes],
        "critical_capture" => record[:calculation] ? @criticalCapture : nil,
        "prior_failed_eligible_throws" => record[:failed],
        "captured_species_id" => outcome == "caught" ? snapshot["species_id"] : nil, "acquisition_ref" => nil,
        "completed_throw_context" => {
          "battle_kind" => trainerBattle? ? "trainer" : "wild", "ball_id" => record[:ball],
          "target_species_id" => snapshot["species_id"], "target_hp" => snapshot["hp"], "target_max_hp" => snapshot["max_hp"],
          "eligible_throw_ordinal" => record[:calculation] ? record[:failed] + 1 : nil,
          "prior_failed_eligible_throws" => record[:failed], "usable_balls_before" => record[:usable_balls],
          "owned_ball_consumed" => nil, "ordinary_capture" => !trainerBattle?, "result" => outcome
        }
      }, self)
      current[:capture_failures] += 1 if outcome == "escaped"
      current[:throw] = previous
    end
  end

  def pbCaptureCalc(pokemon, battler, catch_rate, ball)
    combat = Ironmon::GameplayCollection::Combat
    return super if !combat.active_battle?(self)
    record = combat.state(self)[:throw]
    return super if !record
    record[:calculation] = true
    record[:target] = combat.actor_ref(battler)
    record[:snapshot] = combat.actor_snapshot(battler)
    combat.emit("capture.attempted", {
      "actor" => nil, "target" => record[:target], "throw_id" => record[:id], "ball_resource_ref" => nil,
      "target_hp" => battler.hp, "target_max_hp" => battler.totalhp, "target_status_ref" => record[:snapshot]["status_ref"],
      "attempt_ordinal_in_battle" => record[:ordinal], "usable_balls_before" => record[:usable_balls], "gate_outcome" => "calculation_reached"
    }, self)
    result = super
    record[:shakes] = result
    combat.left(battler, "captured") if result == 4
    return result
  end
end

module IronmonCollectionFieldAttributeHooks
  def weather=(weather)
    before = @weather
    result = super
    combat = Ironmon::GameplayCollection::Combat
    if before != @weather && combat.active_battle?(@ironmon_collection_battle)
      combat.exposure_checkpoint(@ironmon_collection_battle)
      combat.state(@ironmon_collection_battle)[:weather_cause] = combat.cause
      combat.emit("weather.changed", {
        "before_weather" => combat.native_id(before), "after_weather" => combat.native_id(@weather),
        "effective_weather" => nil, "duration" => @weatherDuration,
        "transition_kind" => @weather == :None ? "ended" : "applied", "cause" => combat.cause
      }, @ironmon_collection_battle)
    end
    return result
  end

  def terrain=(terrain)
    before = @terrain
    result = super
    combat = Ironmon::GameplayCollection::Combat
    if before != @terrain && combat.active_battle?(@ironmon_collection_battle)
      combat.exposure_checkpoint(@ironmon_collection_battle)
      combat.emit("terrain.changed", {
        "before_terrain" => combat.native_id(before), "after_terrain" => combat.native_id(@terrain),
        "duration" => @terrainDuration, "affected_actors" => nil, "cause" => combat.cause
      }, @ironmon_collection_battle)
    end
    return result
  end
end

Ironmon::GameplayCollection::Combat.install(PokeBattle_Battle, IronmonCollectionBattleHooks)
Ironmon::GameplayCollection::Combat.install(PokeBattle_Battler, IronmonCollectionBattlerLifecycleHooks)
Ironmon::GameplayCollection::Combat.install(PokeBattle_ActiveField, IronmonCollectionFieldAttributeHooks)
Ironmon::GameplayCollection::Combat.install(PokeBattle_Battle, IronmonCollectionCaptureHooks)
