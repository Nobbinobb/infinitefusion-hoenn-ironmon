module Ironmon
  module GameplayCollection
    module SourceContext
      def self.close_group(id)
        @item_consumption&.delete_if { |key, _quantity| key[0] == id }
      end
      def self.authored
        pair = begin Ironmon.current_area_event_reference; rescue StandardError; nil; end
        interpreter = begin pbMapInterpreter; rescue StandardError; nil; end
        return { "id" => "map:#{pair[0]}:event:#{pair[1]}:command:#{interpreter ? interpreter.instance_variable_get(:@index) : 0}", "map_id" => pair[0], "event_id" => pair[1] } if pair
        site = caller_locations.find { |entry| !entry.path.include?("Collection") && !entry.path.include?("collection/") }
        return { "id" => site ? "runtime:#{File.basename(site.path)}:#{site.lineno}" : "runtime:#{GameplayCollection.now_us}" }
      end

      def self.terrain
        return nil if !$game_player
        tag = $game_player.pbTerrainTag
        return Combat.native_id(tag) if tag.respond_to?(:id)
        return tag.to_s
      end

      def self.hit_pattern(invocation)
        move, actor = invocation[:move], invocation[:actor]
        function = Combat.read(move, :function).to_s
        fixed = { "0BD" => 2, "0BE" => 2, "0BF" => 3, "175" => 2 }
        planned = invocation[:planned_hits]
        minimum = maximum = 1
        kind, basis, override = "single", "intrinsic", nil
        if fixed[function]
          minimum = maximum = fixed[function]
          kind, basis = "fixed_multi", "fixed_multi"
        elsif function == "0C0"
          minimum, maximum, kind = 2, 5, "variable_multi"
          if Combat.native_id(move) == "WATERSHURIKEN" && Combat.read(actor, :form) == 2 && Combat.native_id(Combat.read(actor, :species)) == "GRENINJA"
            minimum = maximum = 3
            basis = "form_override"
          elsif (invocation[:native_hit_queries] || []).any? { |query| query[:method] == :hasActiveAbility? && query[:receiver].equal?(actor) && query[:arguments][0] == :SKILLLINK && query[:result] }
            minimum = maximum = 5
            basis = "ability_override"
            override = { "kind" => "ability", "mechanic_namespace" => "ability", "mechanic_id" => "SKILLLINK", "actor" => Combat.actor_ref(actor) }
          end
        elsif function == "0C1"
          minimum = maximum = planned || 0
          kind, basis = "variable_multi", "party_size"
        elsif planned == 2 && invocation[:parental_bond_at_hit_count].to_i > 0
          minimum = maximum = 2
          kind, basis = "parental_bond", "parental_bond"
          override = { "kind" => "ability", "mechanic_namespace" => "ability", "mechanic_id" => "PARENTALBOND", "actor" => Combat.actor_ref(actor) }
        elsif move && Combat.read(move, :multiHitMove?, false)
          minimum = maximum = planned if planned
          kind, basis = "fixed_multi", "fixed_multi"
        end
        intrinsic_minimum, intrinsic_maximum = function == "0C0" ? [2, 5] : (basis == "parental_bond" ? [1, 1] : [minimum, maximum])
        return { "hit_pattern_kind" => kind, "hit_pattern_range" => {
          "metadata_revision_id" => "native-hit-functions-v1", "intrinsic_min_hits" => intrinsic_minimum, "intrinsic_max_hits" => intrinsic_maximum,
          "effective_min_hits" => minimum, "effective_max_hits" => maximum, "range_basis" => basis, "override_source" => override } }
      end

      def self.owned_metadata(payload)
        pokemon = $Trainer && $Trainer.party.find { |entry| GameplayCollection.pokemon_instance_id(entry) == payload["owned_entity_id"] }
        return if !pokemon
        payload["species_identity"]["size_category"] = Combat.native_id(Combat.read(pokemon, :size_category))
        sprite = Combat.read(pokemon, :pif_sprite)
        payload["display_metadata"].merge!("sprite_variant_ref" => sprite ? {
          "type" => Combat.native_id(Combat.read(sprite, :type)), "head_id" => Combat.read(sprite, :head_id), "body_id" => Combat.read(sprite, :body_id),
          "alt_letter" => Combat.read(sprite, :alt_letter), "local_path" => Combat.read(sprite, :local_path) } : {
          "selection" => "not_materialized", "head_form" => Combat.read(pokemon, :spriteform_head), "body_form" => Combat.read(pokemon, :spriteform_body) },
          "sprite_catalogue_version" => Ironmon.respond_to?(:current_generation_profile_id) ? Ironmon.current_generation_profile_id : "native-sprite-selection-v1")
        payload["data_versions"]["generator_version"] = Ironmon.constants.grep(/Generator/).to_h do |name|
          entry = Ironmon.const_get(name)
          [name.to_s, entry.is_a?(Module) ? entry.constants(false).grep(/VERSION\z/).to_h { |key| [key.to_s, entry.const_get(key)] } : {}]
        end
        payload["data_versions"]["source_fingerprint"] = Ironmon.base_stat_source_fingerprint
        learning = pokemon.instance_variable_get(:@ironmon_collection_move_learning) || {}
        payload["moves"].each do |slot|
          source = learning[slot["move_id"].to_s]
          next if !source
          slot["learning_source"], slot["learning_level"] = source["source"], source["level"]
        end
      end

      def self.enrich(type, payload)
        local = GameplayCollection.current_context
        ["effect_source", "cause"].each do |key|
          source = payload[key]
          next if !source.is_a?(Hash) || ![nil, "unknown"].include?(source["kind"])
          reference = authored["id"]
          source.merge!("kind" => "script", "source_kind" => "script", "canonical_id" => reference, "source_id" => reference, "attribution" => "native_writer", "source_evidence_coverage" => "observed_native_writer")
        end
        if type == "inventory.quantity_changed" && payload["actual_delta"].to_i < 0 && local["source_kind"] == "item"
          @item_consumption ||= {}
          @item_consumption[[local["action_id"] || local["effect_group_id"], payload["item_id"]]] = -payload["actual_delta"]
        elsif type == "hp.loss_applied" || type == "substitute.changed"
          payload["pool"] = type == "hp.loss_applied" ? "pokemon" : "substitute"
        elsif type == "item.disposition_resolved"
          payload["actual_disposition"] = payload["disposition"]
          payload["consumer_role"] = payload.dig("consumer", "owner_role") || payload.dig("holder", "owner_role") || payload["current_controller"]
          payload["use_context"] ||= local["item_activation_kind"] || (local["forced_item_record"] ? "forced_effect" : (payload["holder"] ? "own_held" : (Combat.active_battle? ? "bag_battle" : "bag_outside_battle")))
        elsif type == "item.use_resolved" || type == "held_item.effect_resolved"
          holder = local["forced_item_holder"] || payload["holder"] || payload["user"]
          payload["actual_holder_role"] = holder && holder["owner_role"]
          payload["actual_holder_side"] = holder && holder["side"]
        elsif type == "shop.purchase_resolved" || type == "shop.sale_resolved"
          payload["classification_version"] = "native-handler-semantics-v1"
        elsif type == "capture.attempted"
          payload["actor"] ||= local["source_actor"] || { "side" => "player", "owner_role" => "player", "controller_ref" => "player_trainer" }
        elsif type == "capture.resolved"
          record = Combat.state && Combat.state[:throw]
          if record
            payload["completed_throw_context"]["capture_calculation"] = record[:native_calculation]
            payload["acquisition_ref"] = record[:acquisition_id]
            quantity = (@item_consumption || {})[[local["action_id"] || local["effect_group_id"], record[:ball]]]
            payload["completed_throw_context"]["owned_ball_consumed"] = !!local["reserved_resource_record"] || (quantity ? quantity > 0 : false)
          end
        elsif type == "world.story_state_changed"
          payload["authored_source_ref"] = authored["id"]
          kind, index = payload["story_fact_id"].split(":", 2)
          names = $data_system && (%w[switch variable].include?(kind) ? (kind == "switch" ? $data_system.switches : $data_system.variables) : nil)
          payload["authored_state_name"] = names[index.to_i] if names
          payload["related_transaction_ref"] = local["effect_group_id"] || local["ownership_transaction_id"]
          payload["classification_coverage"] = "native_authored_state"
        elsif type == "world.movement_completed"
          payload["terrain_kind"] = terrain
        elsif type == "world.map_changed"
          payload["source_event_ref"] = World.instance_variable_get(:@transfer_source) || authored["id"]
          payload["location_class"] = "native_map"
        elsif type == "world.travel_mode_changed"
          source = local["travel_source"] || World.instance_variable_get(:@travel_source) || {}
          payload["cause_kind"], payload["tool_or_move_id"], payload["field_action_ref"] = source["kind"] || "native_vehicle_state", source["id"], source["ref"]
        elsif type == "world.badge_state_changed"
          payload["awarding_source_ref"] = authored["id"]
        elsif type == "pokemon.owned_configuration_committed"
          owned_metadata(payload)
        elsif type == "pokemon.move_learning_resolved"
          payload["recipient_role_at_commit"] = "player"
          payload["learning_context"]["actual_new_or_replacement"] = payload["outcome"] == "learned" && payload["learning_context"]["new_to_current_moveset"] == true
          payload["learning_context"]["authored_tutor_ref"] = local["learning_source"] == "tutor" ? authored["id"] : nil
          payload["source_kind"] = local["learning_source"] || (local["source_kind"] == "item" ? "machine" : (local["battle_instance_id"] ? "level_up" : "script")) if payload["source_kind"].nil? || payload["source_kind"] == "unknown"
          if payload["source_kind"] == "machine"
            source_item = payload.dig("learning_context", "source_item_id")
            item = source_item && GameData::Item.try_get(source_item)
            payload["source_kind"] = item.is_TM? ? "tm" : (item.is_TR? ? "tr" : (item.is_HM? ? "hm" : "machine")) if item
          end
          pokemon = $Trainer && $Trainer.party.find { |entry| GameplayCollection.pokemon_instance_id(entry) == payload["pokemon_ref"] }
          if pokemon && payload["outcome"] == "learned"
            history = pokemon.instance_variable_get(:@ironmon_collection_move_learning) || {}
            history[payload["move_id"].to_s] = { "source" => payload["source_kind"], "level" => pokemon.level }
            pokemon.instance_variable_set(:@ironmon_collection_move_learning, history)
          end
        elsif type == "pokemon.evolution_resolved"
          pokemon = $Trainer && $Trainer.party.find { |entry| GameplayCollection.pokemon_instance_id(entry) == payload["pokemon_ref"] }
          if pokemon
            payload["source_owner_role"] = "player"
            payload["result_owner_role"] = "player"
            payload["result_usable_runner"] = Ownership.usable?(pokemon)
            payload["retained_after_commit"] = Ownership.owned?(pokemon)
          end
          candidates = pokemon && pokemon.instance_variable_get(:@ironmon_collection_evolution_methods)
          targets = [payload.dig("species_after_ref", "canonical_species_id")]
          ["fusion_body_id", "fusion_head_id"].each do |key|
            targets << payload.dig("species_after_ref", key) if payload.dig("species_after_ref", key) != payload.dig("species_before_ref", key)
          end
          evidence = candidates && candidates.reverse.find { |entry| targets.compact.map(&:to_s).include?(entry["target"].to_s) }
          if evidence
            payload["effective_method_id"], payload["trigger_kind"], payload["cause_ref"], payload["method_parameter"] = evidence.values_at("method", "trigger", "source", "parameter")
          end
        elsif type == "pokemon.training_attributes_changed"
          before, after = payload["before"], payload["after"]
          payload["effective_delta"] = if before.is_a?(Hash) && after.is_a?(Hash)
            (before.keys | after.keys).to_h { |key| [key, before[key].is_a?(Numeric) && after[key].is_a?(Numeric) ? after[key] - before[key] : { "before" => before[key], "after" => after[key] }] }
          elsif before.is_a?(Numeric) && after.is_a?(Numeric)
            after - before
          else
            { "before" => before, "after" => after }
          end
          payload["source_kind"] = local["source_kind"] || "native_training_mutation"
        elsif type == "pokemon.pivot_resolved"
          payload["returned_items_transaction_ref"] = local["returned_items_transaction_ref"]
        elsif type == "move.finished"
          invocation = Combat.current_move
          payload.merge!(hit_pattern(invocation)) if invocation
        elsif type == "battle.started"
          payload["encounter"]["encounter_source_id"] = authored["id"]
          if payload["battle_kind"] == "trainer" && payload["encounter"]["authored_entry_ids"].empty? && payload["encounter"]["encounter_source_id"].start_with?("map:")
            payload["encounter"]["authored_entry_ids"] << payload["encounter"]["encounter_source_id"]
          end
          payload["encounter"]["story_keys"] = payload["encounter"]["authored_entry_ids"].dup
        elsif type == "weather.changed"
          battle = Combat.current_battle
          payload["effective_weather"] = Combat.native_id(battle.pbWeather) if battle
        elsif type == "terrain.changed"
          battle = Combat.current_battle
          payload["affected_actors"] = Combat.active_actors(battle).select { |actor| actor.affectedByTerrain? }.map { |actor| Combat.actor_ref(actor) } if battle
        elsif type == "battle.ended"
          payload["terminal_side_states"].each do |entry|
            prefix = entry.dig("side", "side_id") == 0 ? "player" : "opponent"
            %w[engine_able_party_count active_able_count reserve_able_count].each { |key| payload["#{prefix}_#{key}"] = entry[key] }
          end
          payload["terminal_precleanup_active_hp_shape"] = payload["final_actor_states"].map { |entry| { "actor" => entry["actor"], "hp" => entry["hp"], "max_hp" => entry["max_hp"] } }
          payload["end_reason"] = { "win" => "team_fainted", "loss" => "team_fainted", "draw" => "team_fainted", "escape" => "escape", "capture" => "capture", "aborted" => "abort" }.fetch(payload["outcome"], "other") if payload["end_reason"].nil? || payload["end_reason"] == "unknown"
        end
        return payload
      end
    end
  end
end

module IronmonCollectionEvolutionMethodEvidence
  def check_evolution_internal(*args, &block)
    return super(*args, &block) if !Ironmon::GameplayCollection.enabled? || !block
    return super(*args) do |pokemon, species, method, parameter|
      result = block.call(pokemon, species, method, parameter)
      if result
        local = Ironmon::GameplayCollection.current_context
        pokemon.instance_variable_set(:@ironmon_collection_evolution_method, {
          "method" => method.to_s, "parameter" => Ironmon::GameplayCollection.stringify(parameter), "trigger" => local["evolution_trigger"] || "native_evolution_check",
          "source" => local["effect_group_id"] || Ironmon::GameplayCollection::SourceContext.authored["id"], "target" => result.to_s })
        candidates = pokemon.instance_variable_get(:@ironmon_collection_evolution_methods) || []
        candidates << pokemon.instance_variable_get(:@ironmon_collection_evolution_method)
        pokemon.instance_variable_set(:@ironmon_collection_evolution_methods, candidates)
      end
      result
    end
  end
end
Pokemon.prepend(IronmonCollectionEvolutionMethodEvidence)

module IronmonCollectionMapTransferEvidence
  def transfer_player(*args)
    Ironmon::GameplayCollection::World.instance_variable_set(:@transfer_source, Ironmon::GameplayCollection::SourceContext.authored["id"]) if Ironmon::GameplayCollection.enabled?
    return super
  end
end
Scene_Map.prepend(IronmonCollectionMapTransferEvidence)

module IronmonCollectionNativeCaptureEvidence
  def pbCaptureCalc(pokemon, battler, catch_rate, ball)
    collection = Ironmon::GameplayCollection
    return super if !collection::Combat.active_battle?(self)
    return collection::NativeEvidence.capture(:capture, self) do |frame|
      result = super
      record = collection::Combat.state(self)[:throw]
      if record && frame
        values = frame[:locals]
        record[:native_calculation] = [:catch_rate, :a, :b, :x, :y, :isOnLastBall, :c, :numShakes].to_h { |key| [key.to_s, values[key]] }
        if result == 4
          identity = collection.next_id("acquisition")
          record[:acquisition_id] = identity
          pokemon.instance_variable_set(:@ironmon_collection_capture_acquisition, identity)
          pokemon.instance_variable_set(:@ironmon_collection_capture_outcome, record[:id])
        end
      end
      result
    end
  end
end
PokeBattle_Battle.prepend(IronmonCollectionNativeCaptureEvidence)

module Ironmon
  module GameplayCollection
    class ObservedTrainingHash < Hash
      OWNERS = ObjectSpace::WeakMap.new

      def []=(key, value)
        owner = OWNERS[self]
        return super if !owner
        return Ownership.with_owned_mutation(owner, "native_training_hash") { super }
      end

      def replace(value)
        owner = OWNERS[self]
        return super if !owner
        return Ownership.with_owned_mutation(owner, "native_training_hash") { super }
      end
    end

    class ObservedBadgeArray < Array
      OWNERS = ObjectSpace::WeakMap.new

      def []=(*args)
        before = dup
        result = super
        World.badges_committed(before, self) if OWNERS[self].equal?($Trainer)
        return result
      end

      def replace(value)
        before = dup
        result = super
        World.badges_committed(before, self) if OWNERS[self].equal?($Trainer)
        return result
      end
    end
  end
end

module IronmonCollectionTrainingReaders
end
[:iv, :ev, :ivMaxed].each do |name|
  IronmonCollectionTrainingReaders.define_method(name) do
    result = super()
    collection = Ironmon::GameplayCollection
    if result.is_a?(Hash) && !result.frozen? && collection::Ownership.active?
      if !result.is_a?(collection::ObservedTrainingHash)
        replacement = collection::ObservedTrainingHash.new(result.default)
        replacement.replace(result)
        instance_variable_set("@#{name}", replacement)
        result = replacement
      end
      collection::ObservedTrainingHash::OWNERS[result] = self
    end
    result
  end
end
Pokemon.prepend(IronmonCollectionTrainingReaders)

module IronmonCollectionBadgeCommitEvidence
  def badges
    result = super
    collection = Ironmon::GameplayCollection
    return result if !equal?($Trainer) || !collection::World.collecting?
    if !result.is_a?(collection::ObservedBadgeArray)
      result = collection::ObservedBadgeArray.new(result)
      @badges = result
    end
    collection::ObservedBadgeArray::OWNERS[result] = self
    return result
  end

  def badges=(value)
    before = @badges ? @badges.dup : []
    result = super
    Ironmon::GameplayCollection::World.badges_committed(before, @badges) if equal?($Trainer)
    return result
  end
end
Player.prepend(IronmonCollectionBadgeCommitEvidence)

module IronmonCollectionYieldedActorEvidence
  def eachAlly(*args, &block)
    return super(*args, &block) if !block
    return super(*args) do |actor|
      previous = Thread.current[:ironmon_collection_yielded_actor]
      Thread.current[:ironmon_collection_yielded_actor] = actor
      begin
        block.call(actor)
      ensure
        Thread.current[:ironmon_collection_yielded_actor] = previous
      end
    end
  end
end
PokeBattle_Battler.prepend(IronmonCollectionYieldedActorEvidence)

module IronmonCollectionProgrammaticLearningEvidence
  def learn_move(move)
    collection = Ironmon::GameplayCollection
    ownership = collection::Ownership
    return super if !ownership.active? || !ownership.owned?(self) || collection.current_context["learning_id"]
    before = moves.map(&:id)
    result = super
    data = GameData::Move.try_get(move)
    return result if !data
    after = moves.map(&:id)
    learned = !before.include?(data.id) && after.include?(data.id)
    collection.emit("pokemon.move_learning_resolved", { "learning_id" => collection.next_id("learning"), "pokemon_ref" => collection.pokemon_instance_id(self),
      "move_id" => data.id.to_s, "source_kind" => collection.current_context["learning_source"] || "programmatic", "outcome" => learned ? "learned" : (after.include?(data.id) ? "already_known" : "failed"),
      "forgotten_move_id" => (before-after).first&.to_s, "slot_index" => after.index(data.id), "cause_ref" => collection.current_context["effect_group_id"], "offered_to_player" => false,
      "usable_runner" => ownership.usable?(self), "learning_context" => { "parent_transaction_ref" => collection.current_context["effect_group_id"], "offered_level" => level,
        "source_item_id" => collection.current_context["source_kind"] == "item" ? collection.current_context["source_id"] : nil, "authored_tutor_ref" => nil, "accepted" => learned, "new_to_current_moveset" => !before.include?(data.id) } })
    return result
  end
end
Pokemon.prepend(IronmonCollectionProgrammaticLearningEvidence)

module IronmonCollectionEvolutionTriggerEvidence
end
{ :check_evolution_on_level_up => "level_up", :check_evolution_on_use_item => "item", :check_evolution_on_trade => "trade" }.each do |method, trigger|
  IronmonCollectionEvolutionTriggerEvidence.define_method(method) do |*args, &block|
    collection = Ironmon::GameplayCollection
    return super(*args, &block) if !collection.enabled?
    instance_variable_set(:@ironmon_collection_evolution_methods, [])
    return collection.with_context("evolution_trigger" => trigger) { super(*args, &block) }
  end
end
Pokemon.prepend(IronmonCollectionEvolutionTriggerEvidence)

module IronmonCollectionVehicleSourceEvidence
end
{ :pbMountBike => "BICYCLE", :pbDismountBike => "BICYCLE", :pbStartSurfing => "SURF", :pbEndSurf => "SURF", :pbDive => "DIVE", :pbSurfacing => "DIVE" }.each do |method, identifier|
  IronmonCollectionVehicleSourceEvidence.define_method(method) do |*args, &block|
    collection = Ironmon::GameplayCollection
    return super(*args, &block) if !collection::World.collecting?
    local = collection.current_context
    source = { "kind" => local["source_kind"] || (identifier == "BICYCLE" ? "tool" : "native_field_move"), "id" => local["source_id"] || identifier,
      "ref" => local["field_action_ref"] || local["effect_group_id"] }
    return collection.with_context("travel_source" => source) do
      result = super(*args, &block)
      collection::World.sample_world
      result
    end
  end
  IronmonCollectionVehicleSourceEvidence.send(:private, method)
end
Object.prepend(IronmonCollectionVehicleSourceEvidence)

module IronmonCollectionNaturalCureEvidence
  def pbCureStatus(*args)
    collection = Ironmon::GameplayCollection
    frame = collection::NativeEvidence.frames.reverse.find { |entry| entry[:kind] == :move_gate && entry[:receiver].equal?(self) }
    if frame && ["SLEEP", "FROZEN"].include?(@status.to_s)
      return collection::Combat.with_context({ "source_kind" => "status", "source_id" => @status.to_s,
        "status_cure_reason" => @status.to_s == "SLEEP" ? "natural_wake" : "natural_thaw" }, @battle) { super }
    end
    return super
  end
end
PokeBattle_Battler.prepend(IronmonCollectionNaturalCureEvidence)

module IronmonCollectionStatRequestEvidence
end
[:pbRaiseStatStage, :pbRaiseStatStageByCause, :pbRaiseStatStageByAbility, :pbLowerStatStage, :pbLowerStatStageByCause, :pbLowerStatStageByAbility].each do |method|
  IronmonCollectionStatRequestEvidence.define_method(method) do |stat, amount, *args, &block|
    return Ironmon::GameplayCollection::Combat.with_context({ "requested_stat_delta" => amount * (method.to_s.include?("Raise") ? 1 : -1) }, @battle) { super(stat, amount, *args, &block) }
  end
end
PokeBattle_Battler.prepend(IronmonCollectionStatRequestEvidence)

module IronmonCollectionHealingGateEvidence
  def canHeal?
    result = super
    collection = Ironmon::GameplayCollection
    combat = collection::Combat
    local = collection.current_context
    if !result && combat.actual_resolution? && ["move", "item", "ability", "field", "volatile"].include?(local["source_kind"])
      reason = fainted? ? "fainted" : (@hp >= @totalhp ? "full_hp" : "HealBlock")
      invocation = combat.current_move(@battle)
      seen = invocation ? (invocation[:healing_preventions] ||= []) : []
      key = [combat.actor_ref(self), local["source_kind"], local["source_id"], reason]
      if !seen.include?(key)
        seen << key
        combat.emit("hp.healing_resolved", { "healer" => local["source_actor"], "recipient" => combat.actor_ref(self), "effect_source" => combat.cause, "initiating_cause" => combat.cause,
          "hp_before" => @hp, "hp_after" => @hp, "max_hp" => @totalhp, "nominal_hp" => nil, "healing_formula" => "native_precalculation_gate", "healing_amount_kind" => local["healing_amount_kind"] || "other",
          "requested_hp_before_cap" => nil, "actual_restored_hp" => 0, "capacity_excess_hp" => nil, "prevented_hp" => nil, "block_reason" => reason, "outcome" => "blocked" }, @battle)
      end
    end
    return result
  end
end
PokeBattle_Battler.prepend(IronmonCollectionHealingGateEvidence)

module IronmonCollectionQuestStateEvidence
  private

  def pbSetQuest(id, completed)
    collection = Ironmon::GameplayCollection
    quests = $Trainer && $Trainer.quests || []
    before = quests.find { |entry| entry.id.to_s == id.to_s }&.completed
    result = super
    after = ($Trainer && $Trainer.quests || []).find { |entry| entry.id.to_s == id.to_s }&.completed
    if before != after && !after.nil? && collection::World.collecting?
      collection.catalogue_ref("quest", id)
      collection::World.story_transition("quest:#{id}", before == true ? "completed" : "active", after == true ? "completed" : "active", after == true ? "completed" : "reopened")
    end
    return result
  end

  def pbAddQuest(id)
    collection = Ironmon::GameplayCollection
    before = ($Trainer && $Trainer.quests || []).any? { |entry| entry.id.to_s == id.to_s }
    result = super
    after = ($Trainer && $Trainer.quests || []).any? { |entry| entry.id.to_s == id.to_s }
    if !before && after && collection::World.collecting?
      collection.catalogue_ref("quest", id)
      collection::World.story_transition("quest:#{id}", "not_started", "active", "accepted")
    end
    return result
  end

  def pbSynchronizeQuestLog(*args, &block)
    return Ironmon::GameplayCollection.with_context("story_reconciliation" => true, "origin_channel" => "technical_baseline") { super(*args, &block) }
  end
end
Object.prepend(IronmonCollectionQuestStateEvidence)

module IronmonCollectionAllNativeHpSources
  def native_hp_context(battler, site)
    return Ironmon::GameplayCollection::NativeEvidence.hp_context(battler, site, super)
  end
end
Ironmon::GameplayCollection::Combat.singleton_class.prepend(IronmonCollectionAllNativeHpSources)

module IronmonCollectionDrainSource
  def pbRecoverHPFromDrain(amount, target, *args)
    collection = Ironmon::GameplayCollection
    site = caller_locations(1, 1)[0]
    origin = if site && File.basename(site.path) == "012_Battle_Phase_EndOfRound.rb" && site.lineno == 320
      retained = collection::Combat.state(@battle)&.dig(:effect_provenance, collection::Combat.effect_provenance_key("battler", target, PBEffects::LeechSeed)) || {}
      { "source_kind" => "volatile", "source_id" => "LeechSeed", "source_actor" => retained["source_actor"], "initiating_actor" => retained["initiating_actor"] }
    else
      {}
    end
    return collection::NativeEvidence.capture(:drain, self) do |frame|
      frame[:target] = target if frame
      collection::Combat.with_context(origin, @battle) { super(amount, target, *args) }
    end
  end
end
PokeBattle_Battler.prepend(IronmonCollectionDrainSource)

{ PokeBattle_ActiveField => "field", PokeBattle_ActiveSide => "side", PokeBattle_ActivePosition => "position" }.each do |owner, scope|
  adapter = Module.new do
    define_method(:effects=) do |array|
      combat = Ironmon::GameplayCollection::Combat
      battle = combat.current_battle
      before = @effects ? @effects.dup : []
      result = super(array)
      if combat.active_battle?(battle)
        subject = scope == "field" ? nil : (scope == "side" ? battle.sides.index(self) : battle.positions.index(self))
        combat.observe_array(array, battle, scope, subject)
        [before.length, array.length].max.times { |index| combat.effect_changed(battle, scope, subject, index, before[index], array[index]) }
      end
      result
    end
  end
  owner.prepend(adapter)
end
