module Ironmon
  module GameplayCollection
    module Combat
      NORMAL_EFFECTIVENESS = 8
      PHYSICAL_CATEGORY = 0
      SPECIAL_CATEGORY = 1
      STATUS_CATEGORY = 2

      def self.states
        @states ||= {}
      end

      def self.observe
        return yield
      rescue StandardError => error
        GameplayCollection.fail_collection(error)
        return nil
      end

      def self.state(battle = nil)
        battle ||= @current_battle
        return nil if !battle
        return states[battle.object_id]
      end

      def self.active_battle?(battle = nil)
        return false if !GameplayCollection.enabled?
        current = state(battle)
        return current && !current[:closed]
      end

      def self.current_battle
        return @current_battle
      end

      def self.read(object, name, fallback = nil)
        return fallback if !object || !object.respond_to?(name)
        return object.public_send(name)
      end

      def self.native_id(value)
        return nil if value.nil?
        return value.to_s if value.is_a?(Symbol) || value.is_a?(String)
        return value if value.is_a?(Integer)
        return native_id(value.id) if value.respond_to?(:id)
        return nil
      end

      def self.value(value)
        case value
        when NilClass, TrueClass, FalseClass, Numeric, String
          return value
        when Symbol
          return value.to_s
        when Array
          return value.map { |child| self.value(child) }
        when Hash
          result = {}
          value.each { |key, child| result[key.to_s] = self.value(child) }
          return result
        end
        id = native_id(value)
        return id if !id.nil?
        return { "value_kind" => "unsupported", "native_class" => value.class.to_s }
      end

      def self.instance_id(pokemon)
        return nil if !pokemon
        if GameplayCollection.respond_to?(:pokemon_instance_id)
          return GameplayCollection.pokemon_instance_id(pokemon)
        end
        identifier = pokemon.instance_variable_get(:@ironmon_collection_instance_id)
        if !identifier
          identifier = GameplayCollection.next_id("pokemon")
          pokemon.instance_variable_set(:@ironmon_collection_instance_id, identifier)
        end
        return identifier
      end

      def self.context(battle = nil)
        current = state(battle)
        return {} if !current
        return {
          "battle_instance_id" => current[:id],
          "round_number" => current[:round],
          "combat_resource_mutation" => true
        }
      end

      def self.emit(type, payload = {}, battle = nil)
        return nil if !active_battle?(battle)
        current = state(battle)
        current[:fact_count] += 1
        current[:mutation_count] += 1 if [
          "hp.loss_applied", "hp.healing_resolved", "stat_stage.changed",
          "status.major_application_resolved", "status.major_cure_resolved",
          "field.effect_changed", "substitute.changed", "pp.restoration_resolved",
          "pp.expenditure_resolved", "held_item.assignment_resolved", "ability.identity_changed"
        ].include?(type) && (!defined?(NativeEvidence) || NativeEvidence.applied_fact?({ "type" => type, "payload" => payload }))
        invocation = current[:moves].last
        if invocation && successful_move_effect?(type, payload)
          invocation[:effect_count] += 1
          invocation[:effect_categories] ||= []
          category = {
            "hp.loss_applied" => "damage", "hp.healing_resolved" => "healing", "stat_stage.changed" => "stat_stage",
            "status.major_application_resolved" => "major_status", "status.major_cure_resolved" => "status_cure",
            "field.effect_changed" => "field_effect", "substitute.changed" => "substitute", "pp.restoration_resolved" => "pp_restoration",
            "battler.identity_changed" => "identity", "ability.identity_changed" => "ability", "held_item.assignment_resolved" => "held_item",
            "weather.changed" => "weather", "terrain.changed" => "terrain"
          }[type]
          invocation[:effect_categories] << category if category && !invocation[:effect_categories].include?(category)
        end
        return GameplayCollection.emit(type, payload, context(battle))
      end

      # Engine bookkeeping is still collected, but it cannot prove that the
      # chosen move worked. Only committed mechanical changes under that move's
      # own cause qualify; berry/ability responses and cleanup have other causes.
      def self.successful_move_effect?(type, payload)
        local = GameplayCollection.current_context
        return false if local["source_kind"] != "move" || local["combat_cleanup"] || local["combat_initialization"]
        case type
        when "hp.loss_applied"
          return payload["actual_hp_lost"].to_i > 0 && !["recoil", "crash", "self_ko", "confusion_self_hit"].include?(payload["loss_kind"])
        when "hp.healing_resolved"
          return payload["actual_restored_hp"].to_i > 0
        when "stat_stage.changed"
          return payload["delta"].to_i != 0 && payload["change_kind"] != "reset"
        when "status.major_application_resolved"
          return payload["application_outcome"] == "applied"
        when "status.major_cure_resolved"
          return payload["cure_outcome"] == "cured"
        when "field.effect_changed"
          role = (payload["effect_metadata"] || {})["semantic_role"]
          return ["volatile_mechanic", "field_mechanic"].include?(role) && payload["lifecycle"] == "active"
        when "substitute.changed"
          return payload["operation"] != "cleared"
        when "pp.restoration_resolved"
          return payload["actual_total_pp"].to_i > 0
        when "battler.identity_changed", "ability.identity_changed", "held_item.assignment_resolved", "weather.changed", "terrain.changed"
          return true
        end
        return false
      end

      def self.with_context(values, battle = nil)
        return yield if !active_battle?(battle)
        exposure_checkpoint(battle || @current_battle, values["phase"]) if values["phase"] && respond_to?(:exposure_checkpoint)
        return GameplayCollection.with_context(context(battle).merge(values)) { yield }
      end

      def self.cause
        current = GameplayCollection.current_context || {}
        source_kind = current["source_kind"] || "unknown"
        source_actor = current["source_actor"]
        initiating_actor = current["initiating_actor"] || source_actor
        return {
          "kind" => source_kind, "mechanic_namespace" => source_kind,
          "mechanic_id" => current["source_id"], "canonical_id" => current["source_id"],
          "direct_source" => source_actor, "origin_instigator" => initiating_actor,
          "actor" => source_actor, "instigator" => initiating_actor,
          "source_kind" => source_kind,
          "source_id" => current["source_id"],
          "source_actor" => source_actor,
          "initiating_actor" => initiating_actor,
          "status_episode_id" => current["status_episode_id"], "effect_episode_id" => current["effect_episode_id"],
          "action_id" => current["action_id"],
          "move_invocation_id" => current["move_invocation_id"],
          "parent_action_id" => current["parent_action_id"],
          "effect_group_id" => current["effect_group_id"],
          "phase" => current["phase"],
          "attribution" => source_kind == "unknown" ? "unknown" : (source_actor || source_kind == "environment" ? "known" : "partial"),
          "source_evidence_coverage" => current["source_evidence_coverage"]
        }
      end

      def self.owner_role(battler)
        return "unknown" if !battler
        battle = read(battler, :battle)
        index = read(battler, :index, -1)
        return "player" if battle && battle.respond_to?(:pbOwnedByPlayer?) && battle.pbOwnedByPlayer?(index)
        return "opponent" if index.odd?
        return "ally"
      end

      def self.actor_ref(battler)
        return nil if !battler || !active_battle?(read(battler, :battle))
        current = state(battler.battle)
        index = read(battler, :index)
        pokemon = read(battler, :pokemon)
        return nil if index.nil? || !pokemon
        individual = instance_id(pokemon)
        if read(battler, :dummy, false)
          retained = current[:known_individuals][individual]
          reference = retained ? retained.merge("appearance_state" => "inactive_causal_actor") : {
            "battle_instance_id" => current[:id], "actor_id" => individual,
            "pokemon_instance_id" => individual, "party_instance_id" => individual,
            "owned_entity_id" => owner_role(battler) == "player" ? individual : nil,
            "battler_index" => index, "side_id" => index % 2,
            "owner_role" => owner_role(battler), "appearance_state" => "engine_private_delayed_actor"
          }
          return actor_aliases(reference, battler)
        end
        existing = current[:actors][index]
        if !existing || existing["party_instance_id"] != individual
          current[:appearances][index] = current[:appearances].fetch(index, 0) + 1
          existing = {
            "battle_instance_id" => current[:id],
            "actor_id" => GameplayCollection.next_id("actor"),
            "appearance_id" => GameplayCollection.next_id("appearance"),
            "party_instance_id" => individual,
            "pokemon_instance_id" => individual,
            "owned_entity_id" => owner_role(battler) == "player" ? individual : nil,
            "battler_index" => index,
            "slot_epoch" => current[:appearances][index],
            "side_id" => index % 2,
            "owner_role" => owner_role(battler),
            "party_index" => read(battler, :pokemonIndex)
          }
          current[:actors][index] = existing
          current[:known_individuals][individual] = existing.dup
        end
        return actor_aliases(existing.dup, battler)
      end

      def self.actor_aliases(reference, battler)
        role = reference["owner_role"]
        pokemon = read(battler, :pokemon)
        usable = role == "player" && Ironmon.respond_to?(:usable_party) && Ironmon.usable_party[0].equal?(pokemon)
        return reference.merge(
          "side" => role, "controller_ref" => "#{reference['battle_instance_id']}:#{role}",
          "usable_runner" => usable,
          "species_id" => native_id(read(battler, :species)),
          "species_id_if_observed" => read(battler, :dummy, false) ? nil : native_id(read(battler, :species)),
          "form_id" => native_id(read(battler, :form)), "level" => read(battler, :level)
        )
      end

      def self.actor_snapshot(battler)
        return nil if !battler
        return {
          "actor" => actor_ref(battler),
          "species_id" => native_id(read(battler, :species)),
          "form_id" => native_id(read(battler, :form)),
          "level" => read(battler, :level),
          "hp" => read(battler, :hp),
          "max_hp" => read(battler, :totalhp),
          "effective_type_ids" => value(read(battler, :pbTypes, [])),
          "stat_stages" => value(read(battler, :stages, {})),
          "status_id" => native_id(read(battler, :status)),
          "status_count" => read(battler, :statusCount),
          "status_ref" => battler.instance_variable_get(:@ironmon_collection_status_episode),
          "status_origin" => battler.instance_variable_get(:@ironmon_collection_status_cause),
          "active_effects" => effect_registry.filter_map do |(scope, index), metadata|
            next if scope != "battler" || metadata["semantic_role"] != "volatile_mechanic"
            actual = read(battler, :effects, [])[index]
            next if !effect_active?(actual, metadata)
            { "effect_id" => metadata["symbol"], "remaining_value" => actual == true ? 1 : (actual.is_a?(Integer) ? actual : nil), "counter_unit" => metadata["duration_unit"] }
          end,
          "observability" => owner_role(battler) == "player" ? "player_owned" : "visibly_entered"
        }
      end

      def self.identity_snapshot(battler)
        return {
          "species_id" => native_id(read(battler, :species)),
          "form_id" => native_id(read(battler, :form)),
          "effective_type_ids" => value(read(battler, :pbTypes, [])),
          "transformed" => effect(battler, :Transform) == true
        }
      end

      def self.battle_snapshot(battle)
        field = read(battle, :field)
        return {
          "weather" => native_id(read(field, :weather)),
          "weather_duration" => read(field, :weatherDuration),
          "terrain" => native_id(read(field, :terrain)),
          "terrain_duration" => read(field, :terrainDuration),
          "field_effects" => value(read(field, :effects, [])),
          "sides" => read(battle, :sides, []).map { |side| value(read(side, :effects, [])) }
        }
      end

      def self.active_actors(battle)
        return read(battle, :battlers, []).compact.select { |actor| read(actor, :pokemon) && !read(actor, :dummy, false) }
      end

      def self.effect(battler, name)
        return nil if !defined?(PBEffects) || !PBEffects.const_defined?(name, false)
        return read(battler, :effects, [])[PBEffects.const_get(name)]
      end

      def self.move_metadata(move, user = nil)
        return {} if !move
        category = read(move, :category)
        category = { 0 => "physical", 1 => "special", 2 => "status" }[category] || "other"
        return {
          "type_id" => native_id(read(move, :calcType)),
          "category" => category,
          "function_code" => read(move, :function).to_s,
          "charging" => user ? ![nil, false, 0].include?(effect(user, :TwoTurnAttack)) : false,
          "damaging" => read(move, :damagingMove?, false),
          "base_priority" => read(move, :priority, 0),
          "execution_origin" => GameplayCollection.current_context["execution_origin"] || "unknown"
        }
      end

      def self.current_move(battle = nil)
        current = state(battle)
        return current ? current[:moves].last : nil
      end

      def self.hit_index(battle = nil)
        current = state(battle)
        return current ? current[:hit_index].to_i : 0
      end

      def self.type_modifier(damage_state)
        return nil if !damage_state
        normal = defined?(Effectiveness::NORMAL_EFFECTIVE) ? Effectiveness::NORMAL_EFFECTIVE : NORMAL_EFFECTIVENESS
        return { "raw" => read(damage_state, :typeMod), "normal_constant" => normal }
      end

      def self.install(klass, mod)
        return if !klass || klass.ancestors.include?(mod)
        klass.prepend(mod)
      end
    end
  end
end
