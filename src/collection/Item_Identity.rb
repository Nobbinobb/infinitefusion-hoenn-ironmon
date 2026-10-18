# Collection-owned unit identities are saved with the actual inventory. Fungible
# bag units use an explicit FIFO allocation policy, never item-ID ownership guesses.
module Ironmon
  module GameplayCollection
    module ItemIdentity
      POLICY = "source_unit_fifo_v1"

      def self.state
        return nil if !$PokemonGlobal
        value = $PokemonGlobal.instance_variable_get(:@ironmon_collection_item_units)
        if !value || value["epoch_id"] != GameplayCollection.journal.epoch_id
          value = { "epoch_id" => GameplayCollection.journal.epoch_id, "bag" => {}, "detached" => {}, "balances" => {}, "recent_uses" => {} }
          $PokemonGlobal.instance_variable_set(:@ironmon_collection_item_units, value)
        end
        value["recent_uses"] ||= {}
        return value
      end

      def self.close_group(id)
        return if !GameplayCollection.enabled? || !state
        state["recent_uses"].delete_if { |key, _record| key.start_with?("#{id}/") }
      end

      def self.unit(item, origin, quantity, reason, parents = [])
        return { "resource_ref" => GameplayCollection.next_id("item_unit"), "item_id" => item.to_s, "acquisition_origin" => origin,
          "quantity" => quantity, "parent_resource_refs" => parents, "provenance_quality" => quantity == 1 ? "exact_unit" : "exact_lot",
          "registration_reason" => reason, "current_controller" => "player", "battle_entry_owner" => "player", "identity_policy" => POLICY }
      end

      def self.publish(record, quantity = nil)
        GameplayCollection.emit("item.provenance_registered", record.merge("quantity" => quantity.nil? ? record["quantity"] : quantity, "represented_quantity" => record["quantity"], "identity_registered" => true))
      end

      def self.ensure_bag(item, actual_quantity, origin = "unknown")
        value = state
        return [] if !value
        lots = value["bag"][item.to_s] ||= []
        missing = actual_quantity - lots.sum { |entry| entry["quantity"] }
        if missing > 0
          record = unit(item, origin, missing, "reconciled")
          record["provenance_quality"] = "unknown" if origin == "unknown"
          lots << record
          publish(record, 0)
        end
        return lots
      end

      def self.take_bag(item, quantity, actual_before)
        lots = ensure_bag(item, actual_before)
        portions = []
        while quantity > 0 && !lots.empty?
          parent = lots[0]
          amount = [quantity, parent["quantity"]].min
          if amount == parent["quantity"]
            portions << lots.shift
          else
            child = unit(item, parent["acquisition_origin"], amount, "split", [parent["resource_ref"]])
            child["battle_entry_owner"] = parent["battle_entry_owner"]
            parent["quantity"] -= amount
            parent["location_kind"], parent["location_ref"] = "bag", "player_bag"
            publish(parent, 0)
            portions << child
            publish(child, 0)
          end
          quantity -= amount
        end
        state["detached"][item.to_s] = portions
        return portions
      end

      def self.pokemon(actor)
        return nil if !actor
        identity = actor["pokemon_instance_id"] || actor["party_instance_id"] || actor["owned_entity_id"]
        battle = Combat.current_battle
        candidates = ($Trainer ? $Trainer.party : []) + (battle ? Combat.active_actors(battle).map { |entry| Combat.read(entry, :pokemon) } : [])
        return candidates.compact.find { |entry| GameplayCollection.pokemon_instance_id(entry) == identity }
      end

      def self.held(pokemon, item = nil, origin = nil, owner = "player")
        return nil if !pokemon
        item ||= Combat.native_id(Combat.read(pokemon, :item_id))
        return nil if !item
        existing = pokemon.instance_variable_get(:@ironmon_collection_held_unit)
        return existing if existing && existing["item_id"] == item.to_s && existing["epoch_id"] == GameplayCollection.journal.epoch_id
        record = unit(item, origin || (owner == "player" ? "starting_held" : "trainer_held"), 1, "battle_entry")
        record["epoch_id"] = GameplayCollection.journal.epoch_id
        record["current_controller"] = record["battle_entry_owner"] = owner
        pokemon.instance_variable_set(:@ironmon_collection_held_unit, record)
        publish(record, 0)
        return record
      end

      def self.common(payload, record)
        return if !record
        %w[resource_ref acquisition_origin provenance_quality battle_entry_owner current_controller identity_policy parent_resource_refs location_kind location_ref].each { |key| payload[key] = record[key] }
      end

      def self.prepare_next(from, to, kind)
        return if !GameplayCollection.enabled? || !from || !to
        donor, recipient = from.pokemon, to.pokemon
        record = held(donor, nil, nil, Combat.owner_role(from))
        return if !record
        recipient.instance_variable_set(:@ironmon_collection_incoming_unit, { :record => record, :from => Combat.actor_ref(from), :kind => kind })
        donor.instance_variable_set(:@ironmon_collection_outgoing_unit, record)
      end

      def self.prepare_recovery(from, to, kind)
        return if !GameplayCollection.enabled? || !from || !to
        parent = from.pokemon.instance_variable_get(:@ironmon_collection_last_consumed_unit)
        return if !parent
        record = unit(parent["item_id"], parent["acquisition_origin"], 1, "recovered", [parent["resource_ref"]])
        record["battle_entry_owner"] = parent["battle_entry_owner"]
        to.pokemon.instance_variable_set(:@ironmon_collection_incoming_unit, { :record => record, :from => Combat.actor_ref(from), :kind => kind })
        publish(record, 0)
      end

      def self.enrich(type, payload)
        return payload if !GameplayCollection.enabled? || !state
        local = GameplayCollection.current_context
        if type == "item.provenance_registered"
          if !payload["identity_registered"] && payload["registration_reason"] == "acquired"
            item = payload["item_id"].to_s
            quantity = payload["quantity"]
            before = $PokemonBag ? [$PokemonBag.pbQuantity(item.to_sym) - quantity, 0].max : 0
            lots = ensure_bag(item, before)
            payload["acquisition_origin"] = SourceContext.authored["map_id"] ? "gift" : "generated" if payload["acquisition_origin"] == "unknown"
            payload["identity_policy"] = POLICY
            payload["provenance_quality"] = quantity == 1 ? "exact_unit" : "exact_lot"
            payload["represented_quantity"] = quantity
            payload["location_kind"], payload["location_ref"] = "bag", "player_bag"
            lots << payload.dup
          end
          item, origin, controller = payload.values_at("item_id", "acquisition_origin", "current_controller")
          payload["current_known_lineage_balance"] = balance(item.to_s, origin, controller)
        elsif type == "inventory.quantity_changed" && !Thread.current[:ironmon_collection_shop]
          item = payload["item_id"].to_s
          if payload["actual_delta"].to_i < 0
            portions = take_bag(item, -payload["actual_delta"], payload["quantity_before"])
            payload["resource_portions"] = portions.map(&:dup)
          elsif payload["actual_delta"].to_i > 0 && local["inventory_reason"] == "pivot_commit"
            records = (local["returned_item_records"] || []).select { |entry| entry["item_id"] == item }
            records.each do |record|
              record["location_kind"], record["location_ref"], record["current_controller"] = "bag", "player_bag", "player"
              state["bag"][item] ||= []
              state["bag"][item] << record
              publish(record, 0)
            end
            payload["resource_portions"] = records
          elsif payload["actual_delta"].to_i > 0 && local["returned_resource_record"]
            record = local["returned_resource_record"]
            record["location_kind"], record["location_ref"] = "bag", "player_bag"
            state["bag"][item] ||= []
            state["bag"][item] << record
            state["detached"][item]&.delete_if { |entry| entry["resource_ref"] == record["resource_ref"] }
            publish(record, 0)
          elsif payload["actual_delta"].to_i > 0 && local["inventory_reason"] == "held_assignment"
            actor = pokemon(local["item_transfer_actor"])
            record = actor && actor.instance_variable_get(:@ironmon_collection_held_unit)
            record = nil if record && record["item_id"] != item
            record ||= state["detached"][item]&.shift
            if record
              state["bag"][item] ||= []
              state["bag"][item] << record
              if actor && actor.instance_variable_get(:@ironmon_collection_held_unit).equal?(record)
                actor.instance_variable_set(:@ironmon_collection_held_unit, nil)
                actor.instance_variable_set(:@ironmon_collection_detached_unit, record)
              end
              payload["resource_portions"] = [record.dup]
            end
          end
        elsif type == "inventory.snapshot_reconciled"
          origin = payload["snapshot_kind"] == "attempt_started" ? "starting_bag" : "unknown"
          payload["item_quantities"].each { |entry| ensure_bag(entry["item_id"], entry["quantity"], origin) }
          payload["held_assignments"].each { |entry| common(entry, held(pokemon(entry["holder"]), entry["item_id"], origin == "starting_bag" ? "starting_held" : "unknown")) }
          payload["lineage_complete"] = state["bag"].values.flatten.none? { |entry| entry["acquisition_origin"] == "unknown" }
          payload["identity_policy"] = POLICY
          payload["item_lots"] = state["bag"].values.flatten.map(&:dup)
        elsif type == "battler.entered"
          actor = payload["actor"]
          individual = pokemon(actor)
          owner = actor["owner_role"]
          battle = Combat.current_battle
          origin = owner == "player" ? "starting_held" : (battle && battle.wildBattle? ? "wild_held" : "trainer_held")
          record = held(individual, nil, origin, owner)
          if record
            record["battle_entry_owner"] = owner
            if individual.instance_variable_get(:@ironmon_collection_initial_unit_battle_id) != Combat.state(battle)[:id]
              individual.instance_variable_set(:@ironmon_collection_battle_initial_unit, record.dup)
              individual.instance_variable_set(:@ironmon_collection_initial_unit_battle_id, Combat.state(battle)[:id])
            end
            record["location_kind"], record["location_ref"] = "held", GameplayCollection.pokemon_instance_id(individual)
            publish(record, 0)
          end
        elsif type == "held_item.assignment_resolved"
          assign(payload)
        elsif type == "item.disposition_resolved"
          actor = payload["holder"] && pokemon(payload["holder"])
          record = Thread.current[:ironmon_collection_consumed_unit]
          record ||= actor && (actor.instance_variable_get(:@ironmon_collection_detached_unit) || actor.instance_variable_get(:@ironmon_collection_held_unit))
          record ||= state["detached"][payload["item_id"].to_s]&.first
          common(payload, record)
          if record
            pending = Thread.current[:ironmon_collection_field_item_results]
            deferred = pending && pending.find { |entry| entry[:payload]["item_id"].to_s == payload["item_id"].to_s && entry[:payload]["effect_succeeded"] == true && !entry[:context]["activation_resource_record"] }
            deferred[:context]["activation_resource_record"] = record if deferred && payload["disposition"] == "consumed"
            key = [local["action_id"] || local["effect_group_id"], payload["item_id"].to_s].join("/")
            state["recent_uses"][key] = record
            state["detached"][payload["item_id"].to_s]&.delete_if { |entry| entry["resource_ref"] == record["resource_ref"] }
            terminal = %w[consumed destroyed flung discarded].include?(payload["disposition"])
            record["quantity"], record["location_kind"], record["location_ref"] = terminal ? 0 : 1, payload["disposition"], nil
            payload["represented_quantity"], payload["location_kind"] = record["quantity"], payload["disposition"]
            publish(record, 0)
            if !actor
              (state["detached"][payload["item_id"].to_s] || []).each do |portion|
                portion["quantity"], portion["location_kind"], portion["location_ref"] = 0, payload["disposition"], nil
                publish(portion, 0)
              end
              state["detached"][payload["item_id"].to_s] = []
            end
          end
          if actor && record && %w[consumed flung].include?(payload["disposition"])
            actor.instance_variable_set(:@ironmon_collection_last_consumed_unit, record)
            actor.instance_variable_set(:@ironmon_collection_held_unit, nil) if actor.instance_variable_get(:@ironmon_collection_held_unit).equal?(record)
          end
        elsif ["item.use_attempted", "item.use_resolved", "held_item.effect_resolved", "capture.attempted"].include?(type)
          item = payload["item_id"] || (Combat.state && Combat.state[:throw] && Combat.state[:throw][:ball])
          key = [local["action_id"] || local["effect_group_id"], item.to_s].join("/")
          holder = payload["holder"] && pokemon(payload["holder"])
          record = local["forced_item_record"] || local["activation_resource_record"] || local["reserved_resource_record"]
          record ||= holder && holder.instance_variable_get(:@ironmon_collection_held_unit)
          record ||= holder && holder.instance_variable_get(:@ironmon_collection_last_consumed_unit) if holder && type == "held_item.effect_resolved"
          record ||= state["recent_uses"][key] || state["bag"][item.to_s]&.first || state["detached"][item.to_s]&.first if !holder
          common(payload, record)
          if type == "held_item.effect_resolved" && local["forced_item_record"]
            payload["holder"] = local["forced_item_holder"]
            payload["activation_kind"] = local["item_activation_kind"]
          end
          payload["ball_resource_ref"] = record && record["resource_ref"] if type == "capture.attempted"
          if type == "item.use_resolved" && record && local["reserved_resource_record"] && payload["consumed_quantity"].to_i > 0
            record["quantity"], record["location_kind"], record["location_ref"] = 0, "consumed", nil
            state["detached"][item.to_s]&.delete_if { |entry| entry["resource_ref"] == record["resource_ref"] }
            publish(record, 0)
          end
        elsif type == "shop.sale_resolved" && payload["outcome"] == "success"
          take_bag(payload["item_id"], payload["removed_quantity"], ($PokemonBag ? $PokemonBag.pbQuantity(payload["item_id"].to_sym) : 0) + payload["removed_quantity"]).each do |record|
            record["quantity"], record["location_kind"], record["location_ref"] = 0, "sold", nil
            publish(record, 0)
          end
          state["detached"][payload["item_id"].to_s] = []
        elsif type == "pokemon.acquisition_resolved" && payload["retained_result_ref"]
          individual = $Trainer && $Trainer.party.find { |entry| GameplayCollection.pokemon_instance_id(entry) == payload["retained_result_ref"] }
          record = individual && held(individual)
          if record
            record["current_controller"], record["location_kind"], record["location_ref"] = "player", "held", GameplayCollection.pokemon_instance_id(individual)
            publish(record, 0)
          end
        end
        return payload
      end

      def self.assign(payload)
        item = payload["item_id"].to_s
        from, to = pokemon(payload["from_actor"]), pokemon(payload["to_actor"])
        if from && !to
          outgoing = from.instance_variable_get(:@ironmon_collection_outgoing_unit)
          if outgoing && outgoing["item_id"] == item
            from.instance_variable_set(:@ironmon_collection_outgoing_unit, nil)
            common(payload, outgoing)
            payload["represented_quantity"] = outgoing["quantity"]
            payload["assignment_kind"] = "other"
            return
          end
          record = from.instance_variable_get(:@ironmon_collection_detached_unit)
          record = nil if record && record["item_id"] != item
          record ||= held(from, item)
          from.instance_variable_set(:@ironmon_collection_detached_unit, record)
          from.instance_variable_set(:@ironmon_collection_held_unit, nil)
          in_bag = (state["bag"][item] || []).any? { |entry| entry["resource_ref"] == record["resource_ref"] }
          state["detached"][item] = [record].compact if !in_bag
          common(payload, record)
          payload["assignment_kind"] = "unequip"
          payload["represented_quantity"], payload["location_kind"], payload["location_ref"] = 1, in_bag ? "bag" : "detached", in_bag ? "player_bag" : nil
          record["location_kind"], record["location_ref"] = payload["location_kind"], payload["location_ref"]
          publish(record, 0)
        elsif to
          incoming = to.instance_variable_get(:@ironmon_collection_incoming_unit)
          incoming = nil if incoming && incoming[:record]["item_id"] != item
          to.instance_variable_set(:@ironmon_collection_incoming_unit, nil)
          old = to.instance_variable_get(:@ironmon_collection_held_unit)
          if old && old["item_id"] != item && !to.instance_variable_get(:@ironmon_collection_outgoing_unit).equal?(old)
            state["detached"][old["item_id"]] ||= []
            state["detached"][old["item_id"]] << old
            to.instance_variable_set(:@ironmon_collection_detached_unit, old)
          end
          initial = to.instance_variable_get(:@ironmon_collection_battle_initial_unit)
          restoration = GameplayCollection.current_context["combat_cleanup"] && initial && initial["item_id"] == item
          detached = GameplayCollection.current_context["item_transfer_actor"] ? state["detached"][item]&.shift : nil
          record = restoration ? initial : (incoming ? incoming[:record] : detached)
          payload["assignment_kind"] = "battle_restore" if restoration
          if incoming
            payload["from_actor"], payload["assignment_kind"] = incoming[:from], { "harvest" => "recycle", "pickup" => "pickup_recovery" }.fetch(incoming[:kind], incoming[:kind])
            payload["recovery_parent_resource_ref"] = record["parent_resource_refs"].first
          end
          if !record && ["HARVEST", "RECYCLE"].include?(GameplayCollection.current_context["source_id"].to_s)
            parent = to.instance_variable_get(:@ironmon_collection_last_consumed_unit)
            if parent && parent["item_id"] == item
              record = unit(item, parent["acquisition_origin"], 1, "recovered", [parent["resource_ref"]])
              record["battle_entry_owner"] = parent["battle_entry_owner"]
              record["location_kind"], record["location_ref"] = "held", GameplayCollection.pokemon_instance_id(to)
              publish(record, 0)
              payload["recovery_parent_resource_ref"] = parent["resource_ref"]
              payload["assignment_kind"] = "recycle"
            end
          end
          record ||= held(to, item)
          record["quantity"] = 1
          record["current_controller"] = (payload["to_actor"] || {})["owner_role"] || "player"
          record["epoch_id"] = GameplayCollection.journal.epoch_id
          record["location_kind"], record["location_ref"] = "held", GameplayCollection.pokemon_instance_id(to)
          to.instance_variable_set(:@ironmon_collection_held_unit, record)
          common(payload, record)
          payload["assignment_kind"] ||= payload["from_actor"] ? "swap" : "equip"
          payload["represented_quantity"], payload["location_kind"], payload["location_ref"] = 1, "held", GameplayCollection.pokemon_instance_id(to)
          publish(record, 0)
        end
      end

      def self.local_actor_matches?(actor)
        return Combat.actor_ref(actor) == GameplayCollection.current_context["source_actor"]
      end

      def self.balance(item, origin, controller)
        bag = state["bag"][item] || []
        quantity = controller == "player" ? bag.select { |entry| entry["acquisition_origin"] == origin }.sum { |entry| entry["quantity"] } : 0
        party = ($Trainer ? $Trainer.party : []) + (Combat.current_battle ? Combat.active_actors(Combat.current_battle).map { |actor| Combat.read(actor, :pokemon) } : [])
        party.compact.uniq.each do |pokemon|
          record = pokemon.instance_variable_get(:@ironmon_collection_held_unit)
          quantity += 1 if record && record["quantity"].to_i > 0 && record["item_id"] == item && record["acquisition_origin"] == origin && record["current_controller"] == controller && record["location_ref"] == GameplayCollection.pokemon_instance_id(pokemon)
        end
        return quantity
      end
    end
  end
end

module IronmonCollectionHeldTransferIdentity
  private

  def pbGiveItemToPokemon(item, pokemon, *args, &block)
    collection = Ironmon::GameplayCollection
    return super if !collection.enabled?
    collection::ItemIdentity.held(pokemon)
    return collection.with_group("held_item_transfer", { "inventory_reason" => "held_assignment", "item_transfer_actor" => collection.owned_actor_ref(pokemon), "source_kind" => "script", "source_id" => "equip" }) { super }
  end

  def pbTakeItemFromPokemon(pokemon, *args, &block)
    collection = Ironmon::GameplayCollection
    return super if !collection.enabled?
    collection::ItemIdentity.held(pokemon)
    return collection.with_group("held_item_transfer", { "inventory_reason" => "held_assignment", "item_transfer_actor" => collection.owned_actor_ref(pokemon), "source_kind" => "script", "source_id" => "unequip" }) { super }
  end
end
Object.prepend(IronmonCollectionHeldTransferIdentity)

{ "0F1" => [:pbEffectAfterAllHits, "steal"], "0F2" => [:pbEffectAgainstTarget, "swap"], "0F3" => [:pbEffectAgainstTarget, "bestow"] }.each do |function, (method, kind)|
  adapter = Module.new do
    define_method(method) do |user, target, *args|
      identity = Ironmon::GameplayCollection::ItemIdentity
      identity.prepare_next(target, user, kind) if function != "0F3"
      identity.prepare_next(user, target, kind) if function != "0F1"
      begin
        super(user, target, *args)
      ensure
        [user, target].each do |actor|
          actor.pokemon.instance_variable_set(:@ironmon_collection_incoming_unit, nil)
          actor.pokemon.instance_variable_set(:@ironmon_collection_outgoing_unit, nil)
        end
      end
    end
  end
  Object.const_get("PokeBattle_Move_#{function}").prepend(adapter)
end

module IronmonCollectionRecycleUnitIdentity
  def pbEffectGeneral(user)
    Ironmon::GameplayCollection::ItemIdentity.prepare_recovery(user, user, "recycle")
    return super
  end
end
PokeBattle_Move_0F6.prepend(IronmonCollectionRecycleUnitIdentity)

module IronmonCollectionConsumedUnitIdentity
  def pbConsumeItem(*args)
    identity = Ironmon::GameplayCollection::ItemIdentity
    return super if !Ironmon::GameplayCollection.enabled?
    record = identity.held(pokemon, nil, nil, Ironmon::GameplayCollection::Combat.owner_role(self))
    previous = Thread.current[:ironmon_collection_consumed_unit]
    Thread.current[:ironmon_collection_consumed_unit] = record
    pokemon.instance_variable_set(:@ironmon_collection_last_consumed_unit, record)
    begin
      Ironmon::GameplayCollection::Combat.with_context({ "item_disposition" => Ironmon::GameplayCollection.current_context["source_id"] == "FLING" ? "flung" : "consumed" }, @battle) { super }
    ensure
      Thread.current[:ironmon_collection_consumed_unit] = previous
    end
  end
end
PokeBattle_Battler.prepend(IronmonCollectionConsumedUnitIdentity)

module IronmonCollectionBagUnitReservations
  def pbConsumeItemInBag(item, index)
    collection = Ironmon::GameplayCollection
    return super if !collection::Combat.active_battle?(self) || !pbOwnedByPlayer?(index) || !item
    before = $PokemonBag.pbQuantity(item)
    result = collection.with_context("inventory_reason" => "transfer") { super }
    if $PokemonBag.pbQuantity(item) < before
      record = collection::ItemIdentity.state["detached"][item.to_s]&.last
      if record
        collection::Combat.state(self)[:bag_reservations] ||= {}
        collection::Combat.state(self)[:bag_reservations][index] = record
        record["location_kind"], record["location_ref"] = "reserved_for_battle_action", collection::Combat.state(self)[:id]
        collection::ItemIdentity.publish(record, 0)
      end
    end
    return result
  end

  def pbReturnUnusedItemToBag(item, index)
    collection = Ironmon::GameplayCollection
    reservations = collection::Combat.state(self)&.fetch(:bag_reservations, {}) || {}
    record = reservations[index]
    return super if !record || record["item_id"] != item.to_s
    result = collection.with_context("inventory_reason" => "transfer", "returned_resource_record" => record) { super }
    reservations.delete(index)
    return result
  end
end
PokeBattle_Battle.prepend(IronmonCollectionBagUnitReservations)

module IronmonCollectionPivotReturnedUnitIdentity
  def apply_normal_fusion_instance_data(body, head, *args)
    collection = Ironmon::GameplayCollection
    return super if !collection.enabled?
    records = [body, head].filter_map { |pokemon| collection::ItemIdentity.held(pokemon) }
    result = super
    body.instance_variable_set(:@ironmon_collection_pivot_return_units, records)
    return result
  end
end
Ironmon.singleton_class.prepend(IronmonCollectionPivotReturnedUnitIdentity)

module IronmonCollectionItemActivationEvidence
end
[:pbHeldItemTriggerCheck, :pbItemHPHealCheck, :pbItemStatusCureCheck, :pbItemEndOfMoveCheck, :pbItemStatRestoreCheck].each do |method|
  IronmonCollectionItemActivationEvidence.define_method(method) do |*args, &block|
    collection = Ironmon::GameplayCollection
    return super(*args, &block) if !collection.enabled? || Thread.current[:ironmon_collection_item_effect_queue]
    pending = []
    Thread.current[:ironmon_collection_item_effect_queue] = pending
    begin
      result = super(*args, &block)
    ensure
      Thread.current[:ironmon_collection_item_effect_queue] = nil
      pending.each do |entry|
        payload, record = entry.values_at(:payload, :record)
        payload["consumed_quantity"] = entry[:resolved_consumption_quantity] || (record && record["quantity"] == 0 && ["consumed", "flung"].include?(record["location_kind"]) ? 1 : 0)
        collection.with_context(entry[:context]) { collection::Combat.complete_item_activation(payload, entry[:battle], record, entry[:group]) }
      end
    end
    result
  end
end
PokeBattle_Battler.prepend(IronmonCollectionItemActivationEvidence)

module IronmonCollectionActualItemTriggerEvidence
  def setInitialItem(item)
    result = super
    collection = Ironmon::GameplayCollection
    if collection::Combat.active_battle?(@battle)
      record = pokemon.instance_variable_get(:@ironmon_collection_held_unit)
      record = nil if !item || !record || record["item_id"] != collection::Combat.native_id(item).to_s
      pokemon.instance_variable_set(:@ironmon_collection_battle_initial_unit, record && record.dup)
    end
    return result
  end

  def pbHeldItemTriggered(item, own = true, fling = false)
    collection = Ironmon::GameplayCollection
    queue = Thread.current[:ironmon_collection_item_effect_queue]
    entry = queue && queue.reverse.find { |candidate| candidate[:payload]["item_id"].to_s == collection::Combat.native_id(item).to_s && candidate[:payload]["recipient"] == collection::Combat.actor_ref(self) }
    result = if entry
      collection::Combat.with_context({ "effect_group_id" => entry[:group], "source_kind" => "item", "source_id" => collection::Combat.native_id(item) }, @battle) { super }
    else
      super
    end
    if queue
      queue.each do |entry|
        payload = entry[:payload]
        next if payload["item_id"].to_s != collection::Combat.native_id(item).to_s || payload["recipient"] != collection::Combat.actor_ref(self)
        entry[:resolved_consumption_quantity] = 1
      end
    end
    return result
  end

  def pbRemoveItem(permanent = true)
    collection = Ironmon::GameplayCollection
    return super if !collection::Combat.active_battle?(@battle)
    record = collection::ItemIdentity.held(pokemon, nil, nil, collection::Combat.owner_role(self))
    transfer = pokemon.instance_variable_get(:@ironmon_collection_outgoing_unit)
    result = super
    if record && !transfer && !Thread.current[:ironmon_collection_consumed_unit]
      function = collection::Combat.current_move(@battle)&.dig(:move)&.function.to_s
      kind = function == "0F4" ? "consumed" : (permanent ? "destroyed" : "temporarily_removed")
      collection::Combat.emit("item.disposition_resolved", { "item_id" => record["item_id"], "resource_ref" => record["resource_ref"], "holder" => collection::Combat.actor_ref(self),
        "consumer" => function == "0F4" ? collection.current_context["source_actor"] : nil, "disposition" => kind, "quantity" => 1, "recoverable" => !permanent,
        "recovery_parent_resource_ref" => nil, "effect_source" => collection::Combat.cause, "acquisition_origin" => record["acquisition_origin"], "battle_entry_owner" => record["battle_entry_owner"], "current_controller" => record["current_controller"] }, @battle)
    end
    return result
  end
end
PokeBattle_Battler.prepend(IronmonCollectionActualItemTriggerEvidence)

{ "0F4" => :pbEffectAfterAllHits, "0F7" => :pbEffectAgainstTarget }.each do |function, method|
  adapter = Module.new do
    define_method(method) do |user, target, *args|
      collection = Ironmon::GameplayCollection
      return super(user, target, *args) if !collection.enabled?
      holder = function == "0F4" ? target : user
      record = collection::ItemIdentity.held(holder.pokemon, nil, nil, collection::Combat.owner_role(holder))
      return collection::Combat.with_context({ "forced_item_record" => record, "forced_item_holder" => collection::Combat.actor_ref(holder), "item_activation_kind" => function == "0F4" ? "stolen_berry" : "flung_item" }, @battle) { super(user, target, *args) }
    end
  end
  Object.const_get("PokeBattle_Move_#{function}").prepend(adapter)
end
