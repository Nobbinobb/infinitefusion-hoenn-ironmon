#===============================================================================
# Observes the exact native branch and local calculation without repeating it.
# Native definitions are verified before observational calls are inserted. Calls
# stay on their original source line so existing causal call-site IDs are stable.
#===============================================================================
module Ironmon
  module GameplayCollection
    module NativeEvidence
      def self.wallet(reason, battle)
        return yield if !Combat.active_battle?(battle)
        return Combat.with_context({ "inventory_reason" => reason, "source_kind" => reason == "money_move_reward" ? "move" : "battle", "source_id" => reason == "money_move_reward" ? "PAYDAY" : Combat.state(battle)[:id] }, battle) { yield }
      end
      def self.frames
        return Thread.current[:ironmon_collection_native_evidence] ||= []
      end

      def self.observe_fact(type, payload, sequence)
        observer = Thread.current[:ironmon_collection_handler_observer]
        while observer
          observer[:events] << { "type" => type, "event_id" => payload["event_id"], "sequence" => sequence, "payload" => payload }
          observer = observer[:parent]
        end
      end

      def self.handler_actors(family, args)
        @handler_parameters ||= {}
        names = @handler_parameters[family] ||= begin
          callable = BattleHandlers.method(family)
          callable = callable.super_method while callable && callable.parameters.none? { |kind, _name| [:req, :opt].include?(kind) }
          callable ? callable.parameters.select { |kind, _name| [:req, :opt].include?(kind) }.map { |_kind, name| name.to_s } : (HANDLER_PARAMETERS[family.to_s] || [])
        end
        values = names.zip(args).to_h
        yielded = Thread.current[:ironmon_collection_yielded_actor]
        bearer = values["bearer"]
        bearer ||= yielded if family.to_s.include?("Ally")
        bearer ||= values["target"] if family.to_s.include?("Target")
        bearer ||= values["battler"] || values["user"] || args.find { |entry| entry.is_a?(PokeBattle_Battler) }
        target = values["target"] || values["battler"]
        target ||= values["user"] if !values["user"].equal?(bearer)
        baseline = names.each_with_index.reverse_each.find { |_name, index| args[index].is_a?(Numeric) }
        return [bearer, target, baseline && args[baseline[1]]]
      end

      def self.effect_family(family, events, item = false)
        types = events.map { |event| event["type"] }
        return item ? "hp" : "hp_change" if types.include?("hp.healing_resolved") || types.include?("hp.loss_applied")
        return item ? "stat" : "stat_change" if types.include?("stat_stage.changed")
        return "status" if types.any? { |type| type.start_with?("status.") || type.start_with?("volatile.") }
        return item ? "pp" : "pp_change" if types.any? { |type| type.start_with?("pp.") }
        return item ? "other" : "weather" if types.include?("weather.changed")
        return item ? "other" : "field" if types.include?("field.effect_changed") || types.include?("terrain.changed")
        return item ? "other" : "transfer" if types.include?("held_item.assignment_resolved")
        return "prevention" if family.to_s.match?(/Immunity|Blocking|Prevent|Check/)
        return "damage_modifier" if item && family.to_s.match?(/Damage|Accuracy|Critical/)
        return "other"
      end

      def self.applied_fact?(event)
        payload = event["payload"]
        case event["type"]
        when "hp.healing_resolved" then return payload["actual_restored_hp"].to_i > 0
        when "hp.loss_applied" then return payload["actual_hp_lost"].to_i > 0
        when "stat_stage.changed" then return payload["delta"].to_i != 0
        when "pp.restoration_resolved" then return payload["actual_total_pp"].to_i > 0
        when "pp.expenditure_resolved" then return payload["actual_pp_lost"].to_i > 0
        when "status.major_application_resolved" then return payload["application_outcome"] == "applied"
        when "status.major_cure_resolved" then return payload["cure_outcome"] == "cured"
        when "item.disposition_resolved" then return %w[consumed destroyed flung transferred].include?(payload["disposition"])
        when "field.effect_changed", "volatile.application_resolved", "volatile.removal_resolved", "ability.identity_changed", "held_item.assignment_resolved", "weather.changed", "terrain.changed", "battler.identity_changed"
          return true
        end
        return false
      end

      def self.hazard(name, battler)
        battle = battler.battle
        return yield if !Combat.active_battle?(battle)
        index = PBEffects.const_get(name)
        side = battler.index % 2
        before = battler.pbOwnSide.effects[index]
        return yield if before == false || before.nil? || before == 0
        retained = Combat.state(battle)[:effect_provenance][Combat.effect_provenance_key("side", side, index)] || {}
        previous = Thread.current[:ironmon_collection_handler_observer]
        observer = { :parent => previous, :events => [] }
        Thread.current[:ironmon_collection_handler_observer] = observer
        frame = nil
        result = capture(:hazard, battler) do |evidence|
          frame = evidence
          Combat.with_context({ "source_kind" => "hazard", "source_id" => name, "hazard_id" => name, "hazard_observation" => true,
            "source_actor" => retained["source_actor"], "initiating_actor" => retained["initiating_actor"], "loss_kind" => "hazard" }, battle) { yield }
        end
        return result
      ensure
        if observer
          Thread.current[:ironmon_collection_handler_observer] = previous
          own = observer[:events].select { |event| source = event["payload"]["cause"] || event["payload"]["effect_source"] || {}; source["source_kind"] == "hazard" && source["source_id"] == name }
          after = battler.pbOwnSide.effects[index]
          removed = before != after && (after == 0 || after == false)
          applied = own.any? { |entry| ["hp.loss_applied", "status.major_application_resolved", "stat_stage.changed"].include?(entry["type"]) }
          airborne = frame && frame[:queries].any? { |query| query[:method] == :airborne? && query[:result] }
          Combat.emit("hazard.resolved", { "target" => Combat.actor_ref(battler), "hazard_id" => name, "layers" => before == true ? 1 : before,
            "outcome" => removed && name == "ToxicSpikes" ? "absorbed" : (applied ? "applied" : (airborne ? "airborne" : "immune")),
            "consequence_refs" => own.map { |entry| entry["event_id"] }, "cause" => retained.merge("kind" => "hazard", "mechanic_namespace" => "hazard", "mechanic_id" => name),
            "entry_actor_ref" => Combat.actor_ref(battler), "native_branch_evidence" => frame ? frame[:queries].map { |query| { "method" => query[:method].to_s, "result" => query[:result] } } : [] }, battle)
        end
      end

      def self.position_resolution(name, battler)
        battle = battler.battle
        return yield if !Combat.active_battle?(battle)
        retained = Combat.state(battle)[:effect_provenance][Combat.effect_provenance_key("position", battler.index, PBEffects.const_get(name))] || {}
        return Combat.with_context({ "source_kind" => "field", "source_id" => name, "source_actor" => retained["source_actor"], "initiating_actor" => retained["initiating_actor"],
          "healing_amount_kind" => "full", "pp_restoration_kind" => "full" }, battle) { yield }
      end

      def self.capture(kind, receiver)
        return yield(nil) if !GameplayCollection.enabled?
        frame = { :kind => kind, :receiver => receiver, :queries => [], :branch => nil, :locals => {} }
        frames << frame
        return yield(frame)
      ensure
        frames.pop if frame
      end

      def self.local(binding, key)
        return binding.local_variable_defined?(key) ? binding.local_variable_get(key) : nil
      end

      def self.branch(result, receiver, kind, reason, native_binding)
        frame = frames.reverse.find { |entry| entry[:kind] == kind && entry[:receiver].equal?(receiver) }
        return result if !frame
        frame[:branch] = reason if reason != "rule:move_unavailable" || !frame[:branch]
        frame[:native_binding] = native_binding
        if kind == :status && Combat.actual_resolution?
          frame[:bypassed] = []
          if receiver.battle.moldBreaker && local(native_binding, :immuneByAbility) == false && !local(native_binding, :selfInflicted)
            candidates = [[receiver, :StatusImmunityAbility]]
            receiver.eachAlly { |actor| candidates << [actor, :StatusImmunityAllyAbility] }
            candidates.each do |actor, family|
              identifier = Combat.native_id(Combat.read(actor, :ability_id))
              registry = BattleHandlers.const_get(family)
              next if !identifier || !registry[identifier.to_sym]
              frame[:bypassed] << { "mechanic_namespace" => "ability", "mechanic_id" => identifier, "bearer" => Combat.actor_ref(actor),
                "handler_family" => family.to_s, "reason" => "mold_breaker", "eligibility_evaluated" => false }
            end
          end
          current = Combat.state(receiver.battle)
          current[:status_gates] ||= {}
          current[:status_gates][[Combat.actor_ref(receiver)["actor_id"], local(native_binding, :newStatus).to_s]] = frame[:bypassed]
        end
        if result == false && Combat.actual_resolution? && (reason.start_with?("ability:") || reason == "reflection:MAGICBOUNCE")
          evidence = mechanic(frame, frame[:target])
          already_applied = (frame[:handler_outcomes] || []).any? { |entry| entry[:id] == evidence["mechanic_id"] && entry[:applied] }
          if !already_applied && !frame[:ability_prevention_recorded]
            frame[:ability_prevention_recorded] = true
            Combat.emit("ability.effect_resolved", {
              "bearer" => evidence["bearer"], "target" => frame[:target] ? Combat.actor_ref(frame[:target]) : evidence["bearer"],
              "ability_id" => evidence["mechanic_id"], "handler_family" => "executed_native_prevention", "effect_family" => "prevention",
              "effect_succeeded" => true, "consequence_event_ids" => [], "suppressed_at_resolution" => false,
              "invocation_context" => "actual_resolution", "outcome" => "success", "effect_evidence" => {
                "resolution_phase" => GameplayCollection.current_context["phase"] || "move", "applied" => true,
                "modifier_changes" => [], "consequence_kinds" => ["prevention"], "coverage" => "executed_native_branch",
                "positive_stages_gained" => 0, "raised_stat_components" => [], "target_is_bearer" => true }
            }, receiver.respond_to?(:battle) ? receiver.battle : nil)
          end
        end
        return result
      rescue StandardError => error
        GameplayCollection.fail_collection(error)
        return result
      end

      def self.accuracy(roll, threshold, move, user, target)
        frame = frames.reverse.find { |entry| entry[:kind] == :accuracy && entry[:receiver].equal?(move) }
        if frame
          frame[:roll] = roll
          frame[:threshold] = threshold
          frame[:user] = user
          frame[:target] = target
        end
        return roll < threshold
      end

      def self.capture_locals(receiver, kind, native_binding)
        frame = frames.reverse.find { |entry| entry[:kind] == kind && entry[:receiver].equal?(receiver) }
        return if !frame
        frame[:native_binding] = native_binding
        frame[:locals] = native_binding.local_variables.to_h { |key| [key, local(native_binding, key)] }
      end

      def self.query(receiver, method, arguments, result)
        frame = frames.last
        return result if !frame
        frame[:queries] << { :receiver => receiver, :method => method, :arguments => arguments, :result => result }
        return result
      end

      def self.redirect(reason, target)
        invocation = Combat.current_move(target.battle)
        invocation[:redirect_evidence] = { "reason" => reason, "actor" => Combat.actor_ref(target) } if invocation
      end

      def self.capture_gate(reason, target)
        current = Combat.state(target.battle)
        return if !current || !current[:throw]
        current[:throw][:gate] = reason
        current[:throw][:target] = Combat.actor_ref(target)
        current[:throw][:snapshot] = Combat.actor_snapshot(target)
      end

      def self.mankey_miss(result, receiver)
        frame = frames.reverse.find { |entry| entry[:kind] == :per_hit && entry[:receiver].equal?(receiver) }
        frame[:mankey_miss] = result if frame
        return result
      end

      def self.mechanic(frame, target = nil)
        reason = frame && frame[:branch]
        return nil if !reason
        namespace, identifier = reason.split(":", 2)
        namespace = "ability" if reason == "reflection:MAGICBOUNCE"
        bearer = target || frame[:receiver]
        if namespace == "ability"
          native = frame[:native_binding]
          bearer = local(native, :immAlly) || target || frame[:receiver] if native
          bearer = local(native, :b) || bearer if identifier == "ally" && native
          identifier = Combat.native_id(Combat.read(bearer, :ability_id)) if ["actual", "ally"].include?(identifier)
        elsif namespace == "item" && identifier == "actual"
          identifier = Combat.native_id(Combat.read(bearer, :item_id))
        elsif namespace == "type" && identifier == "immunity"
          query = frame[:queries].reverse.find { |entry| entry[:method] == :pbHasType? && entry[:receiver].equal?(bearer) && entry[:result] }
          identifier = query[:arguments][0].to_s if query
        end
        return { "mechanic_namespace" => namespace, "mechanic_id" => identifier,
          "bearer" => bearer.is_a?(PokeBattle_Battler) ? Combat.actor_ref(bearer) : nil,
          "prevention_kind" => reason, "evidence" => "executed_native_branch" }
      end

      def self.hp_context(battler, site, existing)
        return existing if !site || existing["source_evidence_coverage"] == "verified_source_callsite"
        definition = HP_CALLSITES[File.basename(site.path)]
        entry = definition && definition["lines"][site.lineno.to_s]
        return existing if !entry
        path = File.join(Dir.pwd, "Data", "Scripts", definition["path"])
        @hp_source_hashes ||= {}
        verified = @hp_source_hashes.fetch(path) { @hp_source_hashes[path] = Digest::SHA256.hexdigest(File.binread(path)) == definition["sha256"] }
        if !verified
          GameplayCollection.fail_collection(RuntimeError.new("Gameplay HP source changed: #{path}"))
          return existing.merge("source_evidence_coverage" => "unsupported_revision")
        end
        current = GameplayCollection.current_context
        answer = existing.merge("source_evidence_coverage" => "verified_source_callsite", "native_mechanic" => entry["owner"] + ":" + entry["method"])
        answer["loss_kind"] = entry["loss_kind"] if !current["loss_kind"] || entry["loss_kind"] != "other"
        if current["source_kind"].nil? || current["source_kind"] == "unknown"
          answer.merge!("source_kind" => "script", "source_id" => "native:#{definition['path']}:#{entry['method']}")
        end
        if entry["method"] == "pbHeldItemTriggered"
          answer.merge!("source_kind" => "ability", "source_id" => "CHEEKPOUCH", "source_actor" => Combat.actor_ref(battler), "initiating_actor" => Combat.actor_ref(battler))
        elsif entry["method"] == "pbRecoverHPFromDrain"
          drain = frames.reverse.find { |frame| frame[:kind] == :drain }
          if drain && drain[:queries].any? { |query| query[:method] == :hasActiveAbility? && query[:arguments][0] == :LIQUIDOOZE && query[:result] }
            answer.merge!("source_kind" => "ability", "source_id" => "LIQUIDOOZE", "source_actor" => Combat.actor_ref(drain[:target]), "initiating_actor" => Combat.actor_ref(drain[:target]), "loss_kind" => "retaliation")
          end
        elsif entry["owner"] == "PokeBattle_Move_160" && site.lineno == 1979
          invocation = Combat.current_move(battler.battle)
          actor = invocation && invocation[:targets].first
          answer.merge!("source_kind" => "ability", "source_id" => "LIQUIDOOZE", "source_actor" => actor, "initiating_actor" => actor)
        elsif entry["method"] == "pbEffectsAfterMove" && site.lineno == 93
          index = Combat.effect(battler, :DestinyBondTarget)
          source = index && battler.battle.battlers[index]
          answer.merge!("source_kind" => "volatile", "source_id" => "DestinyBond", "source_actor" => Combat.actor_ref(source), "initiating_actor" => Combat.actor_ref(source), "loss_kind" => "linked_faint")
        elsif entry["method"] == "pbUseMove" && site.lineno == 332
          retained = Combat.state(battler.battle)[:effect_provenance][Combat.effect_provenance_key("battler", battler, PBEffects::Powder)] || {}
          answer.merge!("source_kind" => "volatile", "source_id" => "Powder", "source_actor" => retained["source_actor"], "initiating_actor" => retained["initiating_actor"], "loss_kind" => "residual")
        end
        answer["healing_formula"] = "native:#{definition['path']}:#{entry['method']}"
        answer["healing_amount_kind"] = current["source_kind"] == "item" ? Resources.healing_model(current["source_id"]) : "other"
        return answer
      end

      def self.install_definition(owner, path, definition)
        source = File.binread(path).force_encoding("UTF-8")
        raise "Native collection evidence source changed: #{path}" if Digest::SHA256.hexdigest(source) != definition["sha256"]
        start = definition["start_line"] - 1
        start = nil if !source.lines[start]&.match?(/^  def #{Regexp.escape(definition["method"])}(?:\(|;|\s|$)/)
        raise "Native collection method is missing: #{definition['method']}" if !start
        target_method = definition["install_as"] || definition["method"]
        original = owner.instance_method(target_method)
        original = original.super_method while original && original.owner != owner
        raise "Native collection definition #{definition['owner']}.#{definition['method']} was replaced by #{original && original.source_location.inspect}." if !original || File.basename(original.source_location[0]) != File.basename(path)
        lines = source.lines
        finish = lines[start].match?(/\bend\s*$/) ? start : ((start + 1)...lines.length).find { |index| lines[index].match?(/^  end\s*$/) }
        raise "Native collection method boundary is missing." if !finish
        body = lines[start..finish]
        (definition["edits"] || []).each do |edit|
          index = edit["line"] - start - 1
          needle, replacement = edit.values_at("needle", "replacement").map { |text| text.dup.force_encoding("UTF-8") }
          raise "Native source observation changed." if !body[index].include?(needle)
          body[index] = body[index].sub(needle, replacement)
        end
        definition["branches"].each do |entry|
          relative = entry["line"] - start - 1
          original = body.fetch(relative)
          replacement = "Ironmon::GameplayCollection::NativeEvidence.branch(#{entry['value']},self,:#{definition['kind']},#{entry['reason'].inspect},binding)"
          raise "Native collection branch changed." if !original.include?("return #{entry['value']}")
          body[relative] = original.sub("return #{entry['value']}", "return #{replacement}")
        end
        if definition["accuracy"]
          body.map! do |line|
            line.sub('return @battle.pbRandom(100) < modifiers[:base_accuracy] * accuracy / evasion',
              'return Ironmon::GameplayCollection::NativeEvidence.accuracy(@battle.pbRandom(100),modifiers[:base_accuracy] * accuracy / evasion,self,user,target)')
          end
        end
        if definition["ohko_accuracy"]
          body.map! { |line| line.sub('return @battle.pbRandom(100) < acc', 'return Ironmon::GameplayCollection::NativeEvidence.accuracy(@battle.pbRandom(100),acc,self,user,target)') }
        end
        if definition["locals"]
          body.map! do |line|
            stripped = line.lstrip
            next line if !stripped.start_with?("return ")
            indent = line[/\A\s*/]
            indent + "Ironmon::GameplayCollection::NativeEvidence.capture_locals(self,:#{definition['kind']},binding); " + stripped
          end
        end
        compiled = body.join
        compiled = compiled.sub("def #{definition['method']}", "def #{target_method}") if target_method != definition["method"]
        owner.class_eval(compiled, path, start + 1)
      end

      def self.install
        NATIVE_DEFINITIONS.each do |definition|
          owner = Object.const_get(definition["owner"])
          path = File.join(Dir.pwd, "Data", "Scripts", definition["path"])
          install_definition(owner, path, definition)
        end
        SOURCE_OBSERVATIONS.each do |definition|
          path = File.join(Dir.pwd, "Data", "Scripts", definition["path"])
          raw = File.binread(path).force_encoding("UTF-8")
          raise "Native item source changed: #{path}" if Digest::SHA256.hexdigest(raw) != definition["sha256"]
          registry = BattleHandlers.const_get(definition["registry"])
          original = registry[definition["identifier"].to_sym]
          raise "Native item callback was replaced." if !original || File.basename(original.source_location[0]) != File.basename(path)
          start = definition["start_line"] - 1
          body = raw.lines[start...definition["end_line"]]
          definition["edits"].each do |edit|
            index = edit["line"] - start - 1
            needle, replacement = edit.values_at("needle", "replacement").map { |text| text.dup.force_encoding("UTF-8") }
            raise "Native item observation changed." if !body[index].include?(needle)
            body[index] = body[index].sub(needle, replacement)
          end
          registry.add(definition["identifier"].to_sym, eval(body.join, TOPLEVEL_BINDING, path, start + 1))
        end
        @installed = true
      rescue StandardError => error
        GameplayCollection.fail_collection(error)
      end

      def self.accuracy_call(move, user, target)
        return capture(:accuracy, move) do |frame|
          result = yield
          invocation = Combat.current_move(move.instance_variable_get(:@battle))
          if invocation && frame && (frame.key?(:roll) || frame[:branch] || !invocation[:native_accuracy])
            invocation[:native_accuracy] = { "check_kind" => frame.key?(:roll) ? "rolled" : "automatic", "effective_accuracy" => frame[:threshold] || 100,
              "reason" => frame[:branch] || "native_accuracy_roll", "native_accuracy_roll" => frame[:roll] }
          end
          result
        end
      end

      def self.enrich(type, payload)
        { "item_id" => "item", "move_id" => "move", "ability_id" => "ability" }.each do |key, namespace|
          identifier = payload[key]
          GameplayCollection.catalogue_ref(namespace, identifier) if identifier.is_a?(String) || identifier.is_a?(Symbol) || identifier.is_a?(Integer)
        end
        ItemIdentity.enrich(type, payload) if defined?(ItemIdentity)
        SourceContext.enrich(type, payload) if defined?(SourceContext)
        battle = Combat.current_battle
        move = battle && Combat.current_move(battle)
        if type == "move.attempted" && payload["attempt_outcome"] == "blocked" && move
          frame = frames.reverse.find { |entry| entry[:kind] == :move_gate }
          evidence = mechanic(frame || move[:native_move_gate])
          payload["block_cause_ref"] = Combat.cause.merge(evidence || {})
        elsif type == "move.target_gate_resolved" && move && move[:native_target_gate]
          frame = move[:native_target_gate]
          evidence = mechanic(frame, frame[:target])
          if evidence && payload["gate_result"] != "allowed"
            namespace, identifier = frame[:branch].split(":", 2)
            payload["gate_result"] = { "ability" => "ability_immune", "terrain" => "terrain_blocked", "move" => "move_specific_failure",
              "substitute" => "substitute_blocked", "type" => "type_immune", "reflection" => "reflected", "protection" => "protected",
              "airborne" => "airborne_avoidance", "rule" => "other" }.fetch(namespace, "other")
            payload["gate_mechanic_context"].merge!(evidence)
          end
        elsif type == "move.targets_resolved" && move
          evidence = move[:redirect_evidence]
          payload["redirect_reason"] = evidence ? evidence["reason"] : "none"
          payload["redirect_source_ref"] = evidence && evidence["actor"]
        elsif type == "move.accuracy_resolved" && move
          if move[:native_accuracy]
            payload.merge!(move[:native_accuracy])
          else
            frame = frames.reverse.find { |entry| entry[:kind] == :per_hit }
            reason = frame && frame[:branch]
            payload.merge!("check_kind" => payload["passed"] ? "automatic" : "semi_invulnerable", "effective_accuracy" => payload["passed"] ? 100 : 0, "reason" => frame && frame[:mankey_miss] ? "item:MANKEYPAW" : (reason || "semi_invulnerable"))
            payload.merge!("check_kind" => "override", "effective_accuracy" => nil, "native_item_check" => { "mechanic_id" => "MANKEYPAW", "roll" => 1, "range" => 2 }) if frame && frame[:mankey_miss]
          end
        elsif type == "substitute.changed" && payload["operation"] == "created"
          recent = Combat.state && Combat.state[:last_hp_loss][payload["target"]["actor_id"]]
          payload["owner_cost_ref"] = recent && recent["sequence"]
        elsif type == "hit.damage_calculated" && payload["calculation_kind"] == "counter"
          actor = Combat.active_actors(battle).find { |entry| Combat.actor_ref(entry) == payload["actor"] }
          if actor
            function = move && Combat.read(move[:move], :function).to_s
            basis = function == "071" ? Combat.effect(actor, :Counter) : (function == "072" ? Combat.effect(actor, :MirrorCoat) : Combat.read(actor, :lastHPLostFromFoe))
            last = Combat.state(battle)[:last_hp_loss][payload["actor"]["actor_id"]]
            payload["retaliation_basis"] = { "basis_damage" => basis.to_i, "source_action_id" => last && last["cause"]["action_id"], "source_actor" => last && last["cause"]["source_actor"] }
          end
        elsif type == "hp.healing_resolved"
          payload["prevented_hp"] = 0 if payload["outcome"] == "success" || payload["capacity_excess_hp"]
          payload["healing_formula"] ||= payload["healing_amount_kind"] == "full" ? "full_missing_hp" : "native_requested_amount"
        elsif type == "status.major_application_resolved"
          payload["bypassed_preventers"] = Combat.state&.fetch(:status_gates, {})&.fetch([payload["target"]["actor_id"], payload["status_requested"].to_s], []) || []
        elsif type == "status.major_prevention_resolved"
          frame = frames.reverse.find { |entry| entry[:kind] == :status }
          evidence = mechanic(frame)
          if evidence
            namespace = evidence["mechanic_namespace"]
            payload["prevention_kind"] = { "ability" => "ability", "item" => "item", "type" => "type", "status" => "existing_status",
              "substitute" => "substitute", "terrain" => "terrain", "safeguard" => "safeguard", "rule" => "rule" }.fetch(namespace, "other")
            payload["preventer_id"] = evidence["mechanic_id"]
            payload["preventer_actor"] = evidence["bearer"]
            payload["bypassed_preventers"] = frame[:bypassed] || []
            payload["classification_coverage"] = "executed_native_branch"
          end
          payload["real_attempt"] = true
          payload["attempt_source_owner_role"] = payload.dig("source_actor", "owner_role")
          payload["blocker_owner_role"] = payload.dig("preventer_actor", "owner_role")
          payload["blocker_ability_id"] = payload["prevention_kind"] == "ability" ? payload["preventer_id"] : nil
          payload["outcome"] = "prevented"
        end
        if type == "move.target_gate_resolved"
          context = payload["gate_mechanic_context"] || {}
          context["bearer_owner_role"] = context.dig("bearer", "owner_role")
          payload["damaging_move"] = context["actual_move_damaging"]
          payload["gate_outcome"] = payload["gate_result"]
          payload["blocker_namespace"] = context["mechanic_namespace"]
          payload["blocker_id"] = context["mechanic_id"]
          payload["blocker_bearer_role"] = context.dig("bearer", "owner_role") || context["bearer_owner_role"]
        elsif type == "stat_stage.attempt_resolved"
          context = payload["prevention"] || {}
          context["bearer_owner_role"] = context.dig("bearer", "owner_role")
          payload["preventing_ability_id"] = context["mechanic_namespace"] == "ability" ? context["mechanic_id"] : nil
          payload["preventing_ability_bearer_role"] = context.dig("bearer", "owner_role") || context["bearer_owner_role"]
          payload["observation_grain"] = "component_request"
          payload["prevented_component_requests"] = ["blocked", "prevented", "ability_prevented"].include?(payload["outcome"]) ? 1 : 0
          if move
            groups = move[:stat_prevention_groups] ||= []
            key = [payload["actor"], payload["target"], context["mechanic_namespace"], context["mechanic_id"]]
            payload["prevented_group_count"] = payload["prevented_component_requests"] > 0 && !groups.include?(key) ? 1 : 0
            groups << key if payload["prevented_group_count"] > 0
          else
            payload["prevented_group_count"] = payload["prevented_component_requests"]
          end
        end
        return payload
      end
    end
  end
end

module IronmonCollectionNativeBattlerEvidence
  def pbTryUseMove(*args)
    evidence = Ironmon::GameplayCollection::NativeEvidence
    return evidence.capture(:move_gate, self) do |frame|
      result = super
      invocation = Ironmon::GameplayCollection::Combat.current_move(@battle)
      invocation[:native_move_gate] = frame if invocation && frame
      result
    end
  end

  def pbSuccessCheckAgainstTarget(move, user, target)
    evidence = Ironmon::GameplayCollection::NativeEvidence
    return evidence.capture(:target_gate, self) do |frame|
      frame[:target] = target if frame
      invocation = Ironmon::GameplayCollection::Combat.current_move(@battle)
      invocation[:native_target_gate] = frame if invocation && frame
      super
    end
  end

  def pbSuccessCheckPerHit(*args)
    evidence = Ironmon::GameplayCollection::NativeEvidence
    return evidence.capture(:per_hit, self) do |frame|
      invocation = Ironmon::GameplayCollection::Combat.current_move(@battle)
      invocation.delete(:native_accuracy) if invocation
      result = super
      result
    end
  end

  def pbCanInflictStatus?(*args)
    return Ironmon::GameplayCollection::NativeEvidence.capture(:status, self) { |_frame| super }
  end
end

module IronmonCollectionNativeMoveAccuracy
  def pbAccuracyCheck(user, target)
    return Ironmon::GameplayCollection::NativeEvidence.accuracy_call(self, user, target) { super }
  end
end

module IronmonCollectionNativeStatGateEvidence
end
[:pbCanRaiseStatStage?, :pbCanLowerStatStage?].each do |method_name|
  IronmonCollectionNativeStatGateEvidence.define_method(method_name) do |stat, *args|
    collection = Ironmon::GameplayCollection
    evidence = collection::NativeEvidence
    outermost = !evidence.frames.any? { |entry| entry[:kind] == :stat_gate }
    evidence.capture(:stat_gate, self) do |frame|
      result = super(stat, *args)
      if frame
        parent = evidence.frames[0...-1].reverse.find { |entry| entry[:kind] == :stat_gate }
        parent[:delegated] = frame if parent
      end
      if result == false && frame && outermost && collection::Combat.actual_resolution?
        actual = frame[:branch] ? frame : frame[:delegated]
        mechanic = evidence.mechanic(actual) || {}
        direction = method_name == :pbCanRaiseStatStage? ? "raise" : "lower"
        invocation = collection::Combat.current_move(@battle)
        key = [collection::Combat.actor_ref(self), stat.to_s, direction, collection.current_context["source_kind"], collection.current_context["source_id"]]
        seen = invocation ? (invocation[:stat_preventions] ||= []) : []
        if !seen.include?(key)
          seen << key
          native_move = args[1].is_a?(PokeBattle_Move) ? args[1] : (invocation && invocation[:move])
          components = native_move && native_move.instance_variable_get(direction == "raise" ? :@statUp : :@statDown)
          component = components.is_a?(Array) && components.each_slice(2).find { |identifier, _amount| identifier.to_s == stat.to_s }
          requested = collection.current_context["requested_stat_delta"] || (component && component[1].is_a?(Numeric) ? component[1] * (direction == "raise" ? 1 : -1) : nil)
          collection::Combat.emit("stat_stage.attempt_resolved", {
            "actor" => collection.current_context["source_actor"] || { "coverage" => "unavailable", "reason" => "actorless_native_effect" }, "target" => collection::Combat.actor_ref(self), "stat_id" => stat.to_s,
            "requested_delta" => requested || { "coverage" => "unavailable", "reason" => "native_prevention_precedes_amount_evaluation" },
            "requested_direction" => direction, "outcome" => mechanic["mechanic_namespace"] == "stat" ? "at_cap" : "blocked",
            "cause" => collection::Combat.cause, "request_id" => collection.next_id("stat_request"),
            "prevention" => { "mechanic_namespace" => mechanic["mechanic_namespace"] || "rule", "mechanic_id" => mechanic["mechanic_id"] || "native_gate", "bearer" => mechanic["bearer"], "reason" => actual && actual[:branch] }
          }, @battle)
        end
      end
      result
    end
  end
end
PokeBattle_Battler.prepend(IronmonCollectionNativeStatGateEvidence)

PokeBattle_Battler.prepend(IronmonCollectionNativeBattlerEvidence)
PokeBattle_Move.prepend(IronmonCollectionNativeMoveAccuracy)
Ironmon::GameplayCollection::NativeEvidence.install
Ironmon::GameplayCollection::NativeEvidence::NATIVE_DEFINITIONS.each do |definition|
  next if definition["method"] != "pbAccuracyCheck" || definition["owner"] == "PokeBattle_Move"
  adapter = Module.new do
    define_method(:pbAccuracyCheck) do |user, target|
      Ironmon::GameplayCollection::NativeEvidence.accuracy_call(self, user, target) { super(user, target) }
    end
  end
  Object.const_get(definition["owner"]).prepend(adapter)
end

module IronmonCollectionNativeQueryEvidence
end
[:hasActiveAbility?, :hasActiveItem?, :abilityActive?, :itemActive?, :airborne?, :takesIndirectDamage?, :affectedByTerrain?, :pbHasType?].each do |method_name|
  IronmonCollectionNativeQueryEvidence.define_method(method_name) do |*args, &block|
    result = super(*args, &block)
    Ironmon::GameplayCollection::NativeEvidence.query(self, method_name, args, result)
  end
end
PokeBattle_Battler.prepend(IronmonCollectionNativeQueryEvidence)
