module Ironmon
  module GameplayCollection
    module Combat
      RETALIATION_FUNCTIONS = ["071", "072", "073"]
      NATIVE_HP_CALLSITES = {
        "006_Battle_Action_Switching.rb" => {
          :sha256 => "ae1fde7ab579b1a55ff6f3f4f5c1691b809b3693ff6bf5a242152cb9da541f36",
          :lines => { 340 => ["position", "HealingWish"], 348 => ["position", "LunarDance"], 362 => ["side", "StealthRock"], 375 => ["side", "Spikes"] }
        },
        "012_Battle_Phase_EndOfRound.rb" => {
          :sha256 => "51ea059b09f937d131311a9325ad8b877928223e3483c5dd0df0d4b4d75d52d7",
          :lines => {
            84 => ["weather", "Sandstorm"], 91 => ["weather", "Hail"], 98 => ["weather", "ShadowSky"],
            256 => ["position", "Wish"], 271 => ["side", "SeaOfFire"], 284 => ["ability", "DRYSKIN"],
            298 => ["battler", "AquaRing"], 307 => ["battler", "Ingrain"], 319 => ["battler", "LeechSeed"],
            333 => ["self", "HyperMode"], 350 => ["ability", "POISONHEAL"],
            383 => ["battler", "Nightmare"], 393 => ["battler", "Curse"],
            423 => ["battler", "Trapping"], 488 => ["battler", "PerishSong"]
          }
        }
      }

      def self.effect_provenance_key(scope, subject, index)
        identity = scope == "battler" ? (actor_ref(subject) || {})["actor_id"] : subject
        return [scope, identity, index]
      end

      def self.native_hp_context(battler, site)
        return {} if !site || !active_battle?(battler.battle)
        filename = File.basename(site.path)
        registry = NATIVE_HP_CALLSITES[filename]
        entry = registry && registry[:lines][site.lineno]
        return {} if !entry
        source_path = site.path
        if !File.file?(source_path)
          source_path = File.join("Data", "Scripts", "011_Battle", "003_Battle", filename)
        end
        @verified_native_sources ||= {}
        verified = @verified_native_sources.fetch(source_path) do
          @verified_native_sources[source_path] = File.file?(source_path) && Digest::SHA256.hexdigest(File.binread(source_path)) == registry[:sha256]
        end
        return { "source_evidence_coverage" => "unsupported_revision" } if !verified
        scope, name = entry
        current = state(battler.battle)
        retained = nil
        if ["battler", "position", "side"].include?(scope) && PBEffects.const_defined?(name, false)
          subject = scope == "battler" ? battler : (scope == "side" ? battler.index % 2 : battler.index)
          provenance_key = effect_provenance_key(scope, subject, PBEffects.const_get(name))
          retained = current[:effect_provenance][provenance_key] || (current[:retired_effect_provenance] || {})[provenance_key]
        elsif scope == "weather"
          retained = current[:weather_cause]
        end
        source_actor = retained && retained["source_actor"]
        source_actor = actor_ref(battler) if ["self", "ability"].include?(scope)
        hazard = scope == "side" && ["StealthRock", "Spikes", "ToxicSpikes", "StickyWeb"].include?(name)
        source_kind = { "battler" => "volatile", "side" => "field", "position" => "field", "weather" => "weather", "ability" => "ability", "self" => "self" }[scope]
        source_kind = "hazard" if hazard
        return {
          "source_kind" => source_kind, "source_id" => name,
          "source_actor" => source_actor, "initiating_actor" => retained && retained["initiating_actor"],
          "effect_episode_id" => retained && retained["effect_episode_id"], "status_episode_id" => GameplayCollection.current_context["status_episode_id"],
          "source_evidence_coverage" => "verified_source_callsite",
          "loss_kind" => hazard ? "hazard" : "residual",
          "hazard_id" => hazard ? name : nil,
          "healing_amount_kind" => "other"
        }
      end

      def self.calculation_kind(move)
        return "unknown" if !move
        function = read(move, :function).to_s
        return "ohko" if function == "070"
        return "counter" if RETALIATION_FUNCTIONS.include?(function)
        return "fixed" if defined?(PokeBattle_FixedDamageMove) && move.is_a?(PokeBattle_FixedDamageMove)
        return "formula"
      end

      def self.observe_hp(battler, before, after)
        return if before == after || !active_battle?(battler.battle)
        current = state(battler.battle)
        local = GameplayCollection.current_context
        actor = actor_ref(battler)
        return if !actor || local["combat_initialization"] || local["combat_cleanup"]
        if after < before
          requested = local["damage_requested"]
          loss_kind = local["loss_kind"] || (local["phase"] == "end_of_round" ? "residual" : "unknown")
          resolved_cause = cause
          sequence = emit("hp.loss_applied", {
            "target" => actor, "hp_before" => before, "hp_after" => after,
            "max_hp" => read(battler, :totalhp), "actual_hp_lost" => before - after,
            "loss_kind" => loss_kind, "hit_ref" => local["hit_ref"],
            "damage_requested" => requested, "cause" => resolved_cause,
            "native_callsite" => local["native_callsite"]
          }, battler.battle)
          current[:last_hp_loss][actor["actor_id"]] = {
            "target" => actor,
            "sequence" => sequence, "cause" => resolved_cause, "loss_kind" => loss_kind,
            "hp_before" => before, "hp_after" => after, "max_hp" => read(battler, :totalhp),
            "hit_ref" => local["hit_ref"]
          }
          current[:hp_loss_refs] << sequence if sequence
          round_damage(battler.battle, actor, before - after, resolved_cause)
          if local["hazard_id"] && !local["hazard_observation"]
            emit("hazard.resolved", {
              "target" => actor, "hazard_id" => local["hazard_id"], "layers" => nil,
              "outcome" => "applied", "consequence_refs" => sequence ? [sequence] : [], "cause" => cause
            }, battler.battle)
          end
        else
          requested = local["healing_requested"]
          actual = after - before
          emit("hp.healing_resolved", {
            "healer" => local["source_actor"], "recipient" => actor,
            "effect_source" => cause, "initiating_cause" => cause,
            "hp_before" => before, "hp_after" => after, "max_hp" => read(battler, :totalhp),
            "nominal_hp" => requested, "healing_formula" => local["healing_formula"],
            "healing_amount_kind" => local["healing_amount_kind"] || "unknown",
            "requested_hp_before_cap" => requested, "actual_restored_hp" => actual,
            "capacity_excess_hp" => requested ? [requested.round - actual, 0].max : nil,
            "prevented_hp" => nil, "block_reason" => nil,
            "outcome" => "success"
          }, battler.battle)
        end
      end

      def self.calculated_hit(move, user, target)
        current = state(target.battle)
        return if !current
        damage = target.damageState
        hit = {
          :id => GameplayCollection.next_id("hit"), :move => move,
          :actor => actor_ref(user), :target => actor_ref(target),
          :hp_before => target.hp, :substitute_before => effect(target, :Substitute).to_i,
          :calculated => read(damage, :calcDamage), :critical => read(damage, :critical),
          :type_modifier => type_modifier(damage), :kind => calculation_kind(move),
          :metadata => move_metadata(move, user), :hp_loss_refs => []
        }
        current[:hit_applications][target.object_id] = hit
        hit[:calculation_ref] = emit("hit.damage_calculated", {
          "actor" => hit[:actor], "target" => hit[:target], "hit_index" => hit_index(target.battle),
          "calculation_kind" => hit[:kind], "calculated_damage" => hit[:calculated],
          "target_hp_before" => hit[:hp_before], "critical" => hit[:critical],
          "effective_type" => hit[:metadata]["type_id"], "effective_category" => hit[:metadata]["category"],
          "type_modifier" => hit[:type_modifier], "retaliation_basis" => nil, "cause" => cause
        }, target.battle)
        return hit
      end

      def self.applied_hit(move, target, before_hp, before_substitute)
        current = state(target.battle)
        return if !current
        hit = current[:hit_applications][target.object_id]
        return if !hit
        damage = target.damageState
        pool = read(damage, :substitute, false) ? "substitute" : "pokemon"
        pool = "none" if read(damage, :disguise, false)
        after_hp = target.hp
        after_substitute = effect(target, :Substitute).to_i
        pool_before = pool == "substitute" ? before_substitute : before_hp
        pool_after = pool == "substitute" ? after_substitute : after_hp
        lost = pool == "none" ? 0 : [pool_before - pool_after, 0].max
        hit[:application_snapshot] = {
          "hp_before" => before_hp, "hp_after" => after_hp, "max_hp" => target.totalhp,
          "hp_before_equals_max" => before_hp == target.totalhp,
          "hp_after_is_zero" => after_hp == 0,
          "pool" => pool, "pool_hp_before" => pool_before, "pool_hp_after" => pool_after,
          "actual_pool_hp_lost" => lost, "calculated_damage" => hit[:calculated],
          "mechanical_move_id" => native_id(move), "effective_type" => hit[:metadata]["type_id"],
          "mechanics_function_family" => calculation_kind(move), "mechanics_function_code" => read(move, :function).to_s, "mechanics_catalogue_version" => "native-hit-functions-v1",
          "effective_category" => hit[:metadata]["category"], "critical" => hit[:critical],
          "type_modifier" => hit[:type_modifier] && hit[:type_modifier]["raw"],
          "type_modifier_normal" => hit[:type_modifier] && hit[:type_modifier]["normal_constant"],
          "calculation_kind" => hit[:kind]
        }
        hit[:result] = if pool == "none"
          "absorbed"
        elsif lost > 0
          pool == "substitute" ? "substitute_damage" : "real_hp_damage"
        else
          "zero_damage"
        end
        hit[:survival] = [:endured, :sturdy, :focusSash, :focusBand].select { |key| read(damage, key, false) }.map(&:to_s)
        hit[:survival] << (read(move, :function).to_s == "0E9" ? "non_lethal_move" : "unclassified_one_hp_clamp") if lost < hit[:calculated].to_i && after_hp == 1 && hit[:survival].empty? && pool == "pokemon"
      end

      def self.finish_hit_iteration(user, move, target, before_hp, hit_number)
        current = state(user.battle)
        return if !current
        application = current[:hit_applications].delete(target.object_id)
        damage = target.damageState
        result = if read(damage, :missed, false)
          "missed"
        elsif read(damage, :unaffected, false)
          "blocked"
        elsif application && application[:result]
          application[:result]
        elsif read(move, :statusMove?, false)
          "status_effect"
        else
          "other"
        end
        snapshot = application && application[:application_snapshot]
        snapshot ||= {
          "hp_before" => before_hp, "hp_after" => target.hp, "max_hp" => target.totalhp,
          "pool" => "none", "pool_hp_before" => before_hp, "pool_hp_after" => target.hp,
          "actual_pool_hp_lost" => 0, "calculated_damage" => nil,
          "mechanical_move_id" => native_id(move), "effective_type" => native_id(read(move, :calcType)),
          "effective_category" => move_metadata(move)["category"], "critical" => nil,
          "type_modifier" => read(damage, :typeMod), "type_modifier_normal" => NORMAL_EFFECTIVENESS,
          "calculation_kind" => nil
        }
        target_ref = application ? application[:target] : actor_ref(target)
        return if !target_ref
        faint = current[:faints][target_ref["actor_id"]]
        lethal = snapshot["pool"] == "pokemon" && snapshot["hp_after"] == 0 && snapshot["actual_pool_hp_lost"] > 0
        payload = {
          "hit_id" => application && application[:id],
          "actor" => application ? application[:actor] : actor_ref(user), "target" => target_ref, "hit_index" => hit_number,
          "result" => result, "hp_loss_refs" => application ? application[:hp_loss_refs] : [],
          "positive_actual_hp_loss" => snapshot["actual_pool_hp_lost"].to_i > 0,
          "positive_actual_real_hp_loss" => snapshot["pool"] == "pokemon" && snapshot["actual_pool_hp_lost"].to_i > 0,
          "mechanics_class" => snapshot["calculation_kind"],
          "effectiveness_ratio" => snapshot["type_modifier"].is_a?(Numeric) && snapshot["type_modifier_normal"].to_i > 0 ? { "numerator" => snapshot["type_modifier"], "denominator" => snapshot["type_modifier_normal"] } : nil,
          "calculation_ref" => application && application[:calculation_ref],
          "survival_adjustments" => application ? application[:survival] || [] : [],
          "immediate_target_hp" => snapshot["hp_after"], "target_max_hp" => target.totalhp,
          "state_refs" => { "application_ref" => application && application[:id] }, "application_snapshot" => snapshot,
          "terminal_faint_link" => {
            "faint_occurred" => !faint.nil?, "faint_instance_id" => faint && faint[:id],
            "lethal_application" => lethal,
            "relation" => faint ? (lethal && faint[:hit_id] == (application && application[:id]) ? "this_hit" : "other") : "none"
          }
        }
        emit("hit.resolved", payload, user.battle)
        invocation = current_move(user.battle)
        invocation[:hits] << payload if invocation
      end
    end
  end
end

module IronmonCollectionHpHooks
  def hp=(amount)
    combat = Ironmon::GameplayCollection::Combat
    before = @hp
    site = caller_locations(1, 1)[0]
    local = Ironmon::GameplayCollection.current_context
    source = local["native_callsite"] ? {} : combat.native_hp_context(self, site)
    source.merge!("combat_resource_mutation" => true, "resource_mirror_mutation" => true)
    if before && amount < before && !local["damage_requested"]
      source["damage_requested"] = before - amount
    elsif before && amount > before && !local["healing_requested"]
      source["healing_requested"] = amount - before
    end
    result = nil
    combat.with_context(source, @battle) do
      result = super
      combat.observe_hp(self, before, @hp) if before && combat.active_battle?(@battle)
    end
    return result
  end

  def pbReduceHP(amount, *args)
    combat = Ironmon::GameplayCollection::Combat
    site = caller_locations(1, 1)[0]
    values = combat.native_hp_context(self, site).merge({
      "damage_requested" => amount,
      "native_callsite" => site ? { "path" => site.path, "line" => site.lineno, "method" => site.base_label } : nil
    })
    return combat.with_context(values, @battle) { super }
  end

  def pbRecoverHP(amount, *args)
    combat = Ironmon::GameplayCollection::Combat
    before = @hp
    site = caller_locations(1, 1)[0]
    result = combat.with_context(combat.native_hp_context(self, site).merge("healing_requested" => amount), @battle) { super }
    if combat.active_battle?(@battle) && before == @hp && !Ironmon::GameplayCollection.current_context["combat_cleanup"]
      combat.emit("hp.healing_resolved", {
        "healer" => Ironmon::GameplayCollection.current_context["source_actor"],
        "recipient" => combat.actor_ref(self), "effect_source" => combat.cause,
        "initiating_cause" => combat.cause, "hp_before" => before, "hp_after" => @hp,
        "max_hp" => @totalhp, "nominal_hp" => amount, "healing_formula" => nil,
        "healing_amount_kind" => Ironmon::GameplayCollection.current_context["healing_amount_kind"] || "unknown",
        "requested_hp_before_cap" => amount, "actual_restored_hp" => 0,
        "capacity_excess_hp" => [amount.round, 0].max, "prevented_hp" => nil,
        "block_reason" => nil, "outcome" => "no_effect"
      }, @battle)
    end
    return result
  end

  def pbConfusionDamage(*args)
    combat = Ironmon::GameplayCollection::Combat
    return combat.with_context({ "loss_kind" => "confusion_self_hit", "source_kind" => "volatile", "source_id" => "Confusion" }, @battle) { super }
  end

  def pbProcessMoveHit(move, user, targets, hit_number, skip_accuracy_check)
    combat = Ironmon::GameplayCollection::Combat
    return super if !combat.active_battle?(@battle)
    current = combat.state(@battle)
    previous = current[:hit_index]
    current[:hit_index] = hit_number
    before = targets.map { |target| [target, target.hp] }
    completed = false
    begin
      result = combat.with_context({
        "source_actor" => combat.actor_ref(user), "source_kind" => "move", "source_id" => combat.native_id(move),
        "hit_index" => hit_number
      }, @battle) { super }
      completed = true
      return result
    ensure
      before.each { |target, hp| combat.finish_hit_iteration(user, move, target, hp, hit_number) } if completed
      current[:hit_index] = previous
    end
  end
end

module IronmonCollectionMoveDamageHooks
  def pbReduceDamage(user, target)
    combat = Ironmon::GameplayCollection::Combat
    combat.calculated_hit(self, user, target) if combat.active_battle?(@battle)
    return super
  end

  def pbInflictHPDamage(target)
    combat = Ironmon::GameplayCollection::Combat
    return super if !combat.active_battle?(@battle)
    hit = combat.state(@battle)[:hit_applications][target.object_id]
    before_hp = target.hp
    before_substitute = combat.effect(target, :Substitute).to_i
    first_loss = combat.state(@battle)[:hp_loss_refs].length
    result = combat.with_context({
      "loss_kind" => "move_hit", "hit_ref" => hit ? hit[:id] : nil,
      "damage_requested" => hit ? hit[:calculated] : nil
    }, @battle) { super }
    hit[:hp_loss_refs] = combat.state(@battle)[:hp_loss_refs][first_loss..-1] if hit
    combat.applied_hit(self, target, before_hp, before_substitute)
    return result
  end
end

Ironmon::GameplayCollection::Combat.install(PokeBattle_Battler, IronmonCollectionHpHooks)
Ironmon::GameplayCollection::Combat.install(PokeBattle_Move, IronmonCollectionMoveDamageHooks)

ObjectSpace.each_object(Class) do |klass|
  next if !(klass <= PokeBattle_Move)
  methods = klass.instance_methods(false)
  [:pbNumHits, :pbSelfKO, :pbCrashDamage].each do |method_name|
    next if !methods.include?(method_name)
    adapter = Module.new
    adapter.define_method(method_name) do |*args, &block|
      combat = Ironmon::GameplayCollection::Combat
      if method_name == :pbNumHits
        result = Ironmon::GameplayCollection::NativeEvidence.capture(:hit_count, self) do |frame|
          value = super(*args, &block)
          invocation = combat.current_move(@battle)
          if invocation
            invocation[:planned_hits] = value
            invocation[:native_hit_queries] = frame ? frame[:queries] : []
            invocation[:parental_bond_at_hit_count] = combat.effect(args[0], :ParentalBond)
          end
          value
        end
        result
      else
        combat.with_context({ "loss_kind" => method_name == :pbSelfKO ? "self_ko" : "recoil" }, @battle) { super(*args, &block) }
      end
    end
    klass.prepend(adapter)
  end
end
