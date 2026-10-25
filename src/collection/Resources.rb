#===============================================================================
# Actual inventory, outside-battle resources, money and wardrobe transactions.
#===============================================================================
module Ironmon
  module GameplayCollection
    module Resources
      FULL_HEAL_ITEMS = [:MAXPOTION, :FULLRESTORE, :MAXREVIVE, :REVIVALHERB].freeze
      LIMITED_HEAL_ITEMS = [:POTION, :SUPERPOTION, :HYPERPOTION, :FRESHWATER, :SODAPOP, :LEMONADE, :MOOMOOMILK, :ENERGYPOWDER, :ENERGYROOT, :SWEETHEART, :ORANBERRY, :SITRUSBERRY, :BERRYJUICE, :FIGYBERRY, :WIKIBERRY, :MAGOBERRY, :AGUAVBERRY, :IAPAPABERRY, :ENIGMABERRY].freeze

      def self.healing_model(item)
        id = item.respond_to?(:id) ? item.id.to_s : item.to_s
        return "full" if FULL_HEAL_ITEMS.any? { |entry| entry.to_s == id }
        return "limited" if LIMITED_HEAL_ITEMS.any? { |entry| entry.to_s == id }
        return "limited" if native_item_tags(item).include?("hp_restore")
        return "unknown"
      end

      def self.native_item_tags(item)
        data = GameData::Item.try_get(item)
        return [] if !data
        @native_item_tags ||= {}
        return @native_item_tags[data.id] if @native_item_tags.key?(data.id)
        tags = []
        { :is_TM? => "TM", :is_HM? => "HM", :is_TR? => "TR", :is_berry? => "berry", :is_poke_ball? => "poke_ball", :is_key_item? => "key_item", :is_evolution_stone? => "evolution_stone" }.each do |method, tag|
          tags << tag if data.public_send(method)
        end
        handler = ItemHandlers::UseOnPokemon[data.id]
        families = {
          "hp_restore" => FULL_HEAL_ITEMS + LIMITED_HEAL_ITEMS + [:REVIVE] + (Settings::RAGE_CANDY_BAR_CURES_STATUS_PROBLEMS ? [] : [:RAGECANDYBAR]),
          "pp_restore" => [:ETHER, :MAXETHER, :ELIXIR, :MAXELIXIR, :LEPPABERRY],
          "status_restore" => [:ANTIDOTE, :AWAKENING, :BURNHEAL, :ICEHEAL, :PARALYZEHEAL, :FULLHEAL, :FULLRESTORE, :HEALPOWDER],
          "pp_capacity" => [:PPUP, :PPMAX]
        }
        families.each do |tag, examples|
          matches = examples.filter_map { |id| ItemHandlers::UseOnPokemon[id] }
          tags << tag if handler && matches.any? { |candidate| candidate.equal?(handler) }
        end
        if data.id == :RAGECANDYBAR && Settings::RAGE_CANDY_BAR_CURES_STATUS_PROBLEMS
          tags.delete("hp_restore")
        end
        held_healing = BattleHandlers::HPHealItem[data.id] || BattleHandlers::EORHealingItem[data.id]
        shell_bell = BattleHandlers::UserItemAfterMoveUse[:SHELLBELL]
        held_healing ||= shell_bell && BattleHandlers::UserItemAfterMoveUse[data.id].equal?(shell_bell)
        if held_healing
          tags << "hp_restore"
          tags << "held_hp_restore"
        end
        tags << "hp_restore" if data.id == :SACREDASH
        @native_item_tags[data.id] = tags.uniq
        return @native_item_tags[data.id]
      end

      def self.active?
        return GameplayCollection.enabled? && !(defined?(World) && World.hydrating?) && !GameplayCollection.current_context["resource_mirror_mutation"]
      end

      def self.observe
        return if !active?
        yield
      rescue StandardError => error
        GameplayCollection.fail_collection(error)
      end

      def self.owned?(pokemon)
        return $Trainer && $Trainer.party.any? { |entry| entry.equal?(pokemon) }
      end

      def self.actor(pokemon)
        return nil if !pokemon
        return GameplayCollection.owned_actor_ref(pokemon) if GameplayCollection.respond_to?(:owned_actor_ref)
        return { "pokemon_instance_id" => GameplayCollection.pokemon_instance_id(pokemon), "side" => "player", "owner_role" => "player", "usable_runner" => Ironmon.respond_to?(:usable_party) && Ironmon.usable_party[0].equal?(pokemon) }
      end

      def self.source
        local = GameplayCollection.current_context
        return { "kind" => local["source_kind"] || "unknown", "canonical_id" => local["source_id"], "actor" => local["source_actor"], "root_cause_event_id" => local["cause_event_id"], "reflected" => false }
      end

      def self.item_common(item)
        local = GameplayCollection.current_context
        origin = local["acquisition_origin"] || { "visible_ground" => "ground", "script_gift" => "gift", "shop_purchase" => "shop" }[local["inventory_reason"]] || "unknown"
        return { "item_id" => GameplayCollection.catalogue_ref("item", item), "resource_ref" => local["resource_ref"], "acquisition_origin" => origin, "battle_entry_owner" => "player", "current_controller" => "player" }
      end

      def self.register_acquired(item, quantity, reconciled = false)
        common = item_common(item)
        GameplayCollection.emit("item.provenance_registered", common.merge(
          "resource_ref" => GameplayCollection.next_id("item_lot"), "quantity" => quantity,
          "parent_resource_refs" => [], "provenance_quality" => !reconciled && common["acquisition_origin"] != "unknown" ? "exact_lot" : "unknown",
          "registration_reason" => reconciled ? "reconciled" : "acquired"
        ))
      end

      def self.wallet_changed(wallet, before, after, requested, maximum)
        return if before.nil? || (before == after && requested == before)
        attempt = Ironmon.current_run_attempt
        GameplayCollection.emit("economy.balance_changed", {
          "wallet_id" => wallet, "transaction_id" => GameplayCollection.next_id("wallet"), "operation" => "set",
          "balance_before" => before, "requested_delta" => requested - before, "requested_balance" => requested,
          "balance_after" => after, "actual_delta" => after - before, "wallet_limits" => { "minimum" => 0, "maximum" => maximum },
          "unapplied_amount" => { "at_cap" => [requested - maximum, 0].max, "at_floor" => [0 - requested, 0].max, "other_blocked" => 0 },
          "reason" => GameplayCollection.current_context["inventory_reason"] || "unknown",
          "source" => source.merge("mechanic_id" => source["canonical_id"], "authored_entry_ids" => Combat.state ? Combat.state[:authored] : [], "shop_id" => nil, "item_id" => source["kind"] == "item" ? source["canonical_id"] : nil, "quantity" => nil, "parent_transaction_ref" => GameplayCollection.current_context["effect_group_id"]),
          "eligibility" => { "attempt_active" => !!(attempt && attempt["result"] == "active"), "debug" => $DEBUG == true, "reconciliation" => false, "policy_version" => 1 },
          "context_refs" => GameplayCollection.current_context
        })
      end

      def self.snapshot(kind = "reconciliation")
        observe do
          return if !$PokemonBag
          quantities = []
          $PokemonBag.pockets.each do |pocket|
            pocket.each { |entry| quantities << { "item_id" => GameplayCollection.catalogue_ref("item", entry[0]), "quantity" => entry[1] } }
          end
          held = $Trainer.party.filter_map do |pokemon|
            next if !pokemon.item_id
            { "holder" => actor(pokemon), "item_id" => GameplayCollection.catalogue_ref("item", pokemon.item_id), "resource_ref" => nil, "provenance_quality" => "unknown_baseline" }
          end
          GameplayCollection.emit("inventory.snapshot_reconciled", {
            "snapshot_kind" => kind, "container_ref" => "player_bag", "item_quantities" => quantities,
            "held_assignments" => held, "lineage_complete" => false, "prior_sequence" => GameplayCollection.journal.sequence.to_s
          })
        end
      end

      def self.inventory(bag, item, before, after, requested)
        return if before == after || !$PokemonBag || !bag.equal?($PokemonBag)
        local = GameplayCollection.current_context
        pickup = Thread.current[:ironmon_collection_pickup]
        pickup["received"][item] = pickup["received"].fetch(item, 0) + after - before if pickup && after > before
        raw_reason = local["inventory_reason"] || "unknown"
        reason = {
          "visible_ground" => "acquisition", "script_gift" => "acquisition", "item_use" => "use",
          "shop_purchase" => "shop_buy", "shop_sale" => "shop_sell", "pivot_commit" => "held_assignment"
        }[raw_reason]
        reason ||= %w[acquisition use held_assignment transfer shop_buy shop_sell discard storage_move rollback baseline reconcile unknown].include?(raw_reason) ? raw_reason : "unknown"
        GameplayCollection.emit("inventory.quantity_changed", {
          "item_id" => GameplayCollection.catalogue_ref("item", item), "container_ref" => "player_bag",
          "quantity_before" => before, "quantity_after" => after, "requested_delta" => requested,
          "actual_delta" => after - before, "reason" => reason, "raw_reason" => raw_reason, "outcome" => "success"
        })
        World.field_tool_changed(item, before, after) if defined?(World) && World.respond_to?(:field_tool_changed)
        if after < before && local["inventory_reason"] == "item_use"
          GameplayCollection.emit("item.disposition_resolved", item_common(item).merge(
            "holder" => nil, "consumer" => local["source_actor"], "disposition" => "consumed",
            "quantity" => before - after, "recoverable" => false, "recovery_parent_resource_ref" => nil, "effect_source" => source
          ))
        end
        return if after <= before
        return if Thread.current[:ironmon_collection_shop]
        return if ["held_assignment", "pivot_commit"].include?(local["inventory_reason"])
        return if local["returned_resource_record"]
        register_acquired(item, after - before, local["inventory_reason"] == "pivot_commit")
      end

      def self.hp_changed(pokemon, before, after)
        return if before.nil? || before == after || !owned?(pokemon)
        local = GameplayCollection.current_context
        if after > before
          requested = local["healing_requested"]
          kind = local["healing_amount_kind"] || "unknown"
          GameplayCollection.emit("hp.healing_resolved", {
            "healer" => local["source_actor"], "recipient" => actor(pokemon), "effect_source" => source,
            "initiating_cause" => { "instigator" => local["source_actor"], "source_kind" => source["kind"], "source_id" => source["canonical_id"], "attribution" => source["kind"] == "unknown" ? "unknown" : "known", "unknown_reason" => source["kind"] == "unknown" ? "unclassified_outside_battle_write" : nil },
            "hp_before" => before, "hp_after" => after, "max_hp" => pokemon.totalhp,
            "nominal_hp" => kind == "limited" ? requested : nil,
            "healing_formula" => kind == "full" ? "full_missing_hp" : (requested ? "fixed" : nil),
            "healing_amount_kind" => kind, "requested_hp_before_cap" => requested,
            "actual_restored_hp" => after - before,
            "capacity_excess_hp" => requested ? [requested.to_i - (after - before), 0].max : nil,
            "prevented_hp" => nil, "block_reason" => nil, "outcome" => "success"
          })
        else
          GameplayCollection.emit("hp.loss_applied", {
            "target" => actor(pokemon), "hp_before" => before, "hp_after" => after, "max_hp" => pokemon.totalhp,
            "actual_hp_lost" => before - after, "loss_kind" => local["loss_kind"] || "unknown",
            "hit_ref" => nil, "damage_requested" => local["damage_requested"], "cause" => source
          })
        end
        local["resource_effects"] << "hp" if local["resource_effects"].is_a?(Array)
      end

      def self.status_changed(pokemon, before, after)
        return if !before || before == after || !owned?(pokemon)
        is_cure = after.nil? || after == :NONE
        cleanup = GameplayCollection.current_context["resource_cleanup"]
        episode = pokemon.instance_variable_get(:@ironmon_collection_status_episode)
        if !is_cure
          episode = GameplayCollection.next_id("status_episode")
          pokemon.instance_variable_set(:@ironmon_collection_status_episode, episode)
          pokemon.instance_variable_set(:@ironmon_collection_status_cause, source)
        end
        payload = { "target" => actor(pokemon), "effect_source" => source, "status_before" => before, "status_episode_id" => episode }
        if is_cure
          GameplayCollection.emit("status.major_cure_resolved", payload.merge("curer" => nil, "status_after" => after, "cure_reason" => cleanup ? "faint_cleanup" : source["kind"], "cure_outcome" => cleanup ? "cleanup_clear" : "cured", "item_id" => source["kind"] == "item" ? source["canonical_id"] : nil, "naturally_recovered" => false))
          pokemon.instance_variable_set(:@ironmon_collection_status_episode, nil)
          pokemon.instance_variable_set(:@ironmon_collection_status_cause, nil)
        else
          GameplayCollection.emit("status.major_application_resolved", payload.merge("source_actor" => nil, "status_requested" => after, "status_after_application" => after, "status_mode" => "other", "native_status_count" => pokemon.statusCount, "application_outcome" => "applied", "prevention_event_id" => nil))
        end
        effects = GameplayCollection.current_context["resource_effects"]
        effects << "status" if effects.is_a?(Array)
      end

      def self.pp_changed(move, before, after)
        return if before.nil? || before == after || !$Trainer
        pokemon = $Trainer.party.find { |entry| entry.moves.any? { |candidate| candidate.equal?(move) } }
        return if !pokemon
        slot = pokemon.moves.index(move)
        local = GameplayCollection.current_context
        if after > before
          requested = local["pp_requested"]
          GameplayCollection.emit("pp.restoration_resolved", {
            "recipient" => actor(pokemon), "effect_source" => source,
            "slot_results" => [{ "slot" => slot, "slot_revision" => GameplayCollection.pokemon_instance_id(pokemon), "pool_kind" => "persistent", "move_id" => GameplayCollection.catalogue_ref("move", move.id), "pp_mode" => move.total_pp <= 0 ? "unlimited" : "finite", "pp_before" => before, "pp_after" => after, "max_pp" => move.total_pp, "nominal_pp" => requested, "requested_pp" => requested, "actual_pp" => after - before, "capacity_excess_pp" => requested ? [requested - (after - before), 0].max : nil, "persistent_move_updated" => true }],
            "actual_total_pp" => after - before, "successful_slot_count" => 1, "transaction_context" => source["kind"] == "item" ? "bag" : (source["kind"] == "script" ? "script" : "other"), "outcome" => "success"
          })
        else
          GameplayCollection.emit("pp.expenditure_resolved", {
            "move_owner" => actor(pokemon), "drain_source" => local["source_actor"], "effect_source" => source,
            "move_slot" => slot, "move_id" => GameplayCollection.catalogue_ref("move", move.id), "pp_mode" => move.total_pp <= 0 ? "unlimited" : "finite",
            "pp_before" => before, "pp_after" => after, "requested_cost" => nil, "actual_pp_lost" => before - after, "cost_kind" => "unknown"
          })
        end
        local["resource_effects"] << "pp" if local["resource_effects"].is_a?(Array)
      end

      def self.item_handler(item, pokemon, use_context, handler_stage = "effect")
        return yield if !active? || GameplayCollection.current_context["collecting_item_handler"]
        routes_to_field = false
        observe { routes_to_field = handler_stage == "from_bag" && !!ItemHandlers::UseFromBag[item] }
        return yield if !active?
        GameplayCollection.with_group("item_use", {
          "source_kind" => "item", "source_id" => GameplayCollection.catalogue_ref("item", item),
          "inventory_reason" => "item_use", "collecting_item_handler" => true, "healing_amount_kind" => healing_model(item),
          "resource_effects" => []
        }) do
          next yield if !active?
          common = item_common(item).merge("user" => nil, "target" => actor(pokemon), "use_context" => use_context, "requested_quantity" => 1)
          GameplayCollection.emit("item.use_attempted", common.merge("attempt_stage" => "submitted", "selected_move_slot" => nil, "rejection_reason" => nil, "native_handler_stage" => handler_stage))
          repel_before = $PokemonGlobal ? $PokemonGlobal.repel.to_i : 0
          result = yield
          deferred = routes_to_field && [2, 4].include?(result)
          success = result == true || (result.is_a?(Integer) && result > 0)
          if success && !deferred && $PokemonGlobal && $PokemonGlobal.repel.to_i > repel_before && !$PokemonGlobal.tempRepel
            $PokemonGlobal.instance_variable_set(:@ironmon_collection_repel_source, GameplayCollection.current_context["effect_group_id"])
          end
          effects = GameplayCollection.current_context["resource_effects"] || []
          effects = ["field"] if effects.empty? && success && !deferred && handler_stage != "on_pokemon"
          resolved = common.merge(
            "consumed_quantity" => nil, "effect_families" => effects.uniq,
            "effect_succeeded" => deferred ? nil : success, "outcome" => deferred ? "unknown" : (success ? "success" : "no_effect"),
            "native_handler_result" => Combat.value(result), "native_handler_stage" => handler_stage,
            "resolution_stage" => deferred ? "deferred_to_field" : "effect_resolved"
          )
          pending = Thread.current[:ironmon_collection_field_item_results]
          if pending
            pending << { :payload => resolved, :context => GameplayCollection.current_context, :group => GameplayCollection.current_context["effect_group_id"] }
          else
            GameplayCollection.emit("item.use_resolved", resolved)
          end
          result
        end
      end

      def self.bag_session(item, pokemon)
        previous = Thread.current[:ironmon_collection_field_item_results]
        return yield if previous
        pending = []
        before = $PokemonBag ? $PokemonBag.pbQuantity(item) : nil
        GameplayCollection.with_group("bag_session", "source_kind" => "item", "source_id" => GameplayCollection.catalogue_ref("item", item), "source_actor" => actor(pokemon), "inventory_reason" => "item_use") do
          Thread.current[:ironmon_collection_field_item_results] = pending
          begin
            result = yield
          ensure
            Thread.current[:ironmon_collection_field_item_results] = previous
            consumed = before && $PokemonBag ? [before - $PokemonBag.pbQuantity(item), 0].max : nil
            pending.each do |entry|
              payload = entry[:payload]
              quantity = consumed && payload["effect_succeeded"] == true && consumed > 0 ? 1 : 0
              payload["consumed_quantity"] = consumed ? quantity : nil
              consumed -= quantity if consumed
              GameplayCollection.with_context(entry[:context]) { GameplayCollection.emit("item.use_resolved", payload) }
              closure = entry[:closure] || [{}, "interrupted"]
              GameplayCollection.close_group(entry[:group], closure[0], closure[1])
            end
          end
          result
        end
      end

      def self.machine_use(item, pokemon, use_context)
        return yield if !active?
        GameplayCollection.with_group("item_use", {
          "source_kind" => "item", "source_id" => GameplayCollection.catalogue_ref("item", item),
          "inventory_reason" => "item_use", "learning_source" => "machine", "collecting_item_handler" => true
        }) do
          next yield if !active?
          before = nil
          observe { before = $PokemonBag.pbQuantity(item) if $PokemonBag }
          common = item_common(item).merge("user" => nil, "target" => actor(pokemon), "use_context" => use_context, "requested_quantity" => 1)
          GameplayCollection.emit("item.use_attempted", common.merge("attempt_stage" => "submitted", "selected_move_slot" => nil, "rejection_reason" => nil, "native_handler_stage" => "machine"))
          result = yield
          observe do
            success = result == true || result == 1
            consumed = before && $PokemonBag ? [before - $PokemonBag.pbQuantity(item), 0].max : nil
            GameplayCollection.emit("item.use_resolved", common.merge(
              "consumed_quantity" => consumed, "effect_families" => success ? ["move_learning"] : [],
              "effect_succeeded" => success, "outcome" => success ? "success" : "no_effect",
              "native_handler_result" => Combat.value(result), "native_handler_stage" => "machine"
            ))
          end
          result
        end
      end

      def self.shop_flush(state)
        return if !state
        if !state.has_key?("balance_delta")
          state["items"] = []
          return
        end
        delta = state["balance_delta"]
        item = state["selected_item"]
        paid = state["items"].select { |entry| entry[0] == item && entry[2] != "bonus" }
        quantity = paid.sum { |entry| entry[1] }.abs
        if item && quantity > 0
          price = state["prices"][item]
          payload = { "shop_ref" => state["shop_ref"], "item_id" => GameplayCollection.catalogue_ref("item", item), "requested_quantity" => quantity, "currency_id" => "money", "outcome" => "success" }
          if state["kind"] == "shop_purchase"
            bonus = state["items"].select { |entry| entry[2] == "bonus" && entry[1] > 0 }.group_by { |entry| entry[0] }.map { |id, rows| { "item_id" => GameplayCollection.catalogue_ref("item", id), "quantity" => rows.sum { |entry| entry[1] } } }
            payload.merge!("delivered_paid_quantity" => quantity, "unit_price" => price, "quoted_total" => price ? price * quantity : nil, "actual_debit" => [0 - delta, 0].max, "bonus_items" => bonus)
            GameplayCollection.emit("shop.purchase_resolved", payload)
            register_acquired(item, quantity)
            bonus.each { |entry| register_acquired(entry["item_id"], entry["quantity"]) }
          else
            payload.merge!("removed_quantity" => quantity, "unit_sale_price" => price, "quoted_credit" => price ? price * quantity : nil, "actual_credit" => [delta, 0].max)
            GameplayCollection.emit("shop.sale_resolved", payload)
          end
        end
        state["items"] = []
        state.delete("balance_delta")
        state.delete("money_set")
      end
    end
  end
end

module IronmonCollectionBagResources
  def pbStoreItem(item, quantity = 1, *args)
    before = pbQuantity(item)
    result = super
    Ironmon::GameplayCollection::Resources.observe { Ironmon::GameplayCollection::Resources.inventory(self, GameData::Item.get(item).id, before, pbQuantity(item), quantity) }
    return result
  end

  def pbDeleteItem(item, quantity = 1, *args)
    before = pbQuantity(item)
    result = super
    Ironmon::GameplayCollection::Resources.observe { Ironmon::GameplayCollection::Resources.inventory(self, GameData::Item.get(item).id, before, pbQuantity(item), -quantity) }
    return result
  end
end
PokemonBag.prepend(IronmonCollectionBagResources)

module IronmonCollectionResourceBaselines
  def new_attempt(*args)
    result = super
    Ironmon::GameplayCollection::Resources.snapshot("attempt_started") if !hydrating?
    return result
  end

  def after_load(*args)
    result = super
    Ironmon::GameplayCollection::Resources.snapshot("save_loaded")
    return result
  end
end
Ironmon::GameplayCollection::World.singleton_class.prepend(IronmonCollectionResourceBaselines)

module IronmonCollectionCommittedInventory
  def commit_pending_result(*args)
    resources = Ironmon::GameplayCollection::Resources
    return super if !resources.active?
    before = {}
    $PokemonBag.pockets.each { |pocket| pocket.each { |item, quantity| before[item] = quantity } } if $PokemonBag
    transaction = Ironmon::GameplayCollection.current_context["ownership_transaction_id"] || Ironmon::GameplayCollection.next_id("ownership_transaction")
    result_pokemon = args[1]
    returned = result_pokemon && result_pokemon.instance_variable_get(:@ironmon_collection_pivot_return_units) || []
    result = Ironmon::GameplayCollection.with_context("ownership_transaction_id" => transaction, "returned_items_transaction_ref" => returned.empty? ? nil : transaction) { super }
    if result && $PokemonBag
      resources.observe do
        after = {}
        $PokemonBag.pockets.each { |pocket| pocket.each { |item, quantity| after[item] = quantity } }
        Ironmon::GameplayCollection.with_context("inventory_reason" => "pivot_commit", "ownership_transaction_id" => transaction, "returned_item_records" => returned) do
          (before.keys | after.keys).each { |item| resources.inventory($PokemonBag, item, before.fetch(item, 0), after.fetch(item, 0), after.fetch(item, 0) - before.fetch(item, 0)) }
          resources.snapshot("reconciliation")
        end
      end
      result_pokemon.remove_instance_variable(:@ironmon_collection_pivot_return_units) if result_pokemon && result_pokemon.instance_variable_defined?(:@ironmon_collection_pivot_return_units)
    end
    return result
  end
end
Ironmon.singleton_class.prepend(IronmonCollectionCommittedInventory)

module IronmonCollectionPokemonResources
  def hp=(value)
    before = @hp
    collection = Ironmon::GameplayCollection
    local = collection.current_context
    request = { "resource_cleanup" => value <= 0 }
    request["healing_requested"] = value - before if before && value > before && !local["healing_requested"] && local["healing_amount_kind"] != "full"
    request["damage_requested"] = before - value if before && value < before && !local["damage_requested"]
    result = nil
    collection.with_context(request) do
      result = super
      collection::Resources.observe { collection::Resources.hp_changed(self, before, @hp) }
    end
    return result
  end

  def status=(value)
    before = @status
    result = super
    resources = Ironmon::GameplayCollection::Resources
    resources.observe { resources.status_changed(self, before, @status) }
    return result
  end

  def heal_status
    before = @status
    result = super
    resources = Ironmon::GameplayCollection::Resources
    resources.observe { resources.status_changed(self, before, @status) }
    return result
  end

  def heal_HP
    before = @hp
    collection = Ironmon::GameplayCollection
    result = collection.with_context("healing_amount_kind" => "full", "healing_requested" => nil) { super }
    collection::Resources.observe do
      collection.with_context("healing_amount_kind" => "full", "healing_requested" => nil) { collection::Resources.hp_changed(self, before, @hp) }
    end
    return result
  end

  def item=(value)
    before = item_id
    result = super
    after = item_id
    resources = Ironmon::GameplayCollection::Resources
    resources.observe do
      if before != after && resources.owned?(self)
        Ironmon::GameplayCollection.emit("held_item.assignment_resolved", resources.item_common(after || before).merge("from_actor" => before ? resources.actor(self) : nil, "to_actor" => after ? resources.actor(self) : nil, "from_container_ref" => nil, "to_container_ref" => nil, "assignment_kind" => after ? "equip" : "unequip", "previous_target_item_id" => before, "effect_source" => resources.source, "outcome" => "success"))
      end
    end
    return result
  end
end
Pokemon.prepend(IronmonCollectionPokemonResources)

module IronmonCollectionPartyMoveResources
  def pp=(value)
    before = @pp
    collection = Ironmon::GameplayCollection
    result = nil
    requested = collection.current_context["pp_requested"] || (before && value > before ? value - before : nil)
    collection.with_context("pp_requested" => requested) do
      result = super
      collection::Resources.observe { collection::Resources.pp_changed(self, before, @pp) }
    end
    return result
  end

  def ppup=(value)
    before = @ppup
    max_before = total_pp
    pp_before = @pp
    result = super
    resources = Ironmon::GameplayCollection::Resources
    resources.observe do
      pokemon = $Trainer && $Trainer.party.find { |entry| entry.moves.include?(self) }
      if pokemon && before != @ppup
        Ironmon::GameplayCollection.emit("pp.capacity_change_resolved", { "recipient" => resources.actor(pokemon), "item_id" => resources.source["kind"] == "item" ? resources.source["canonical_id"] : { "coverage" => "unknown", "reason" => "non_item_capacity_write" }, "move_slot" => pokemon.moves.index(self), "move_id" => Ironmon::GameplayCollection.catalogue_ref("move", @id), "pp_ups_before" => before, "pp_ups_after" => @ppup, "max_pp_before" => max_before, "max_pp_after" => total_pp, "current_pp_before" => pp_before, "current_pp_after" => @pp, "outcome" => "success" })
      end
    end
    return result
  end
end
Pokemon::Move.prepend(IronmonCollectionPartyMoveResources)

module IronmonCollectionFieldItemHandlers
  def triggerUseFromBag(item)
    return Ironmon::GameplayCollection::Resources.item_handler(item, nil, "bag_field", "from_bag") { super }
  end

  def triggerUseOnPokemon(item, pokemon, scene)
    return Ironmon::GameplayCollection::Resources.item_handler(item, pokemon, "bag_field", "on_pokemon") { super }
  end

  def triggerUseInField(item)
    return Ironmon::GameplayCollection::Resources.item_handler(item, nil, "bag_field", "in_field") { super }
  end
end
ItemHandlers.singleton_class.prepend(IronmonCollectionFieldItemHandlers)

module IronmonCollectionGlobalResources
  private

  def pbUseItem(bag, item, *args)
    collection = Ironmon::GameplayCollection
    return super if !collection::Resources.active?
    machine = false
    collection::Resources.observe { machine = GameData::Item.get(item).is_machine? }
    return collection::Resources.bag_session(item, nil) do
      if machine
        collection::Resources.machine_use(item, nil, "bag_field") { super }
      else
        super
      end
    end
  end

  def pbUseItemOnPokemon(item, pokemon, *args)
    collection = Ironmon::GameplayCollection
    return super if !collection::Resources.active?
    machine = false
    collection::Resources.observe { machine = GameData::Item.get(item).is_machine? }
    return collection::Resources.bag_session(item, pokemon) do
      if machine
        collection::Resources.machine_use(item, pokemon, "bag_field") { super }
      else
        super
      end
    end
  end

  def pbItemRestoreHP(pokemon, amount)
    collection = Ironmon::GameplayCollection
    return super if !collection.enabled?
    current = collection.current_context
    kind = current["healing_amount_kind"] == "full" ? "full" : "limited"
    return collection.with_context("healing_requested" => amount, "healing_amount_kind" => kind) { super }
  end

  def pbRestorePP(pokemon, slot, amount)
    collection = Ironmon::GameplayCollection
    return super if !collection.enabled?
    return collection.with_context("pp_requested" => amount) { super }
  end

  def pbBattleRestorePP(pokemon, battler, slot, amount)
    collection = Ironmon::GameplayCollection
    return super if !collection.enabled?
    return collection.with_context("pp_requested" => amount) { super }
  end

  def pbBattleHPItem(pokemon, battler, amount, scene)
    collection = Ironmon::GameplayCollection
    return super if !collection.enabled?
    source_id = collection.current_context["source_id"]
    kind = collection::Resources::FULL_HEAL_ITEMS.any? { |item| item.to_s == source_id.to_s } ? "full" : "limited"
    return collection.with_context("healing_amount_kind" => kind, "healing_requested" => amount) { super }
  end

  def pbItemBall(item, quantity = 1, *args)
    return collection_pickup("visible_ground", item, quantity) { super }
  end

  def pbReceiveItem(item, quantity = 1, *args)
    return collection_pickup("script_gift", item, quantity) { super }
  end

  def collection_pickup(kind, item, quantity)
    collection = Ironmon::GameplayCollection
    return yield if !collection::Resources.active?
    previous = Thread.current[:ironmon_collection_pickup]
    state = { "received" => {} }
    Thread.current[:ironmon_collection_pickup] = state
    reference = nil
    hidden = nil
    begin
      reference = Ironmon.respond_to?(:current_area_event_reference) ? Ironmon.current_area_event_reference : nil
      hidden = !!(reference && Ironmon.tracker_area_hidden_items(reference[0]).any? { |entry| entry["event_id"].to_i == reference[1] })
    rescue StandardError
      # Optional authored classification cannot suppress the native acquisition.
      hidden = nil
    end
    origin = hidden == true ? "hidden" : (hidden.nil? ? "unknown" : (kind == "visible_ground" ? "ground" : "gift"))
    authored_reference = reference ? "#{reference[0]}:#{reference[1]}" : { "coverage" => "unknown", "reason" => "no_authored_event_reference" }
    return collection.with_group("pickup", "source_kind" => "script", "source_id" => kind, "inventory_reason" => kind, "acquisition_origin" => origin, "authored_source_ref" => authored_reference, "authored_source_tuple" => reference) do
      result = yield
      collection::Resources.observe do
        source_kind = hidden == true ? "hidden" : (hidden.nil? ? "other" : (kind == "visible_ground" ? "ordinary_visible" : "scripted_reward"))
        received = state["received"].empty? ? { GameData::Item.get(item).id => 0 } : state["received"]
        received.each do |actual_item, count|
          data = GameData::Item.get(actual_item)
          tags = collection::Resources.native_item_tags(actual_item)
          collection.emit("pickup.resolved", { "authored_source_ref" => authored_reference, "authored_source_tuple" => reference, "source_kind" => source_kind, "authored_item_id" => collection.catalogue_ref("item", item), "item_id" => collection.catalogue_ref("item", actual_item), "requested_quantity" => quantity, "received_quantity" => count, "classification_version" => "native-handler-semantics-v1", "item_tags" => tags, "outcome" => count > 0 ? "success" : "no_effect", "handler_returned_success" => result == true })
        end
        end
      result
    end
  ensure
    Thread.current[:ironmon_collection_pickup] = previous if state
  end
end
Object.prepend(IronmonCollectionGlobalResources)

module IronmonCollectionMoneyResources
  def heal_party(*args)
    collection = Ironmon::GameplayCollection
    return super if !collection::Resources.active?
    return collection.with_group("party_recovery", "source_kind" => "script", "source_id" => "heal_party", "healing_amount_kind" => "full") { super }
  end

  def money=(value)
    before = @money
    result = super
    Ironmon::GameplayCollection::Resources.observe do
      Ironmon::GameplayCollection::Resources.wallet_changed("money", before, @money, value, Settings::MAX_MONEY) if equal?($Trainer)
    end
    return result
  end
end
Player.prepend(IronmonCollectionMoneyResources)

module IronmonCollectionOtherWalletResources
end
{ :coins => :MAX_COINS, :battle_points => :MAX_BATTLE_POINTS, :soot => :MAX_SOOT }.each do |wallet, limit|
  IronmonCollectionOtherWalletResources.define_method("#{wallet}=") do |value|
    before = instance_variable_get("@#{wallet}")
    result = super(value)
    Ironmon::GameplayCollection::Resources.observe do
      after = instance_variable_get("@#{wallet}")
      Ironmon::GameplayCollection::Resources.wallet_changed(wallet.to_s, before, after, value, Settings.const_get(limit)) if equal?($Trainer)
    end
    result
  end
end
Player.prepend(IronmonCollectionOtherWalletResources)

module IronmonCollectionShopResources
  def pbBuyScreen(*args)
    return collection_shop("shop_purchase") { super }
  end

  def pbSellScreen(*args)
    return collection_shop("shop_sale") { super }
  end

  def collection_shop(kind)
    collection = Ironmon::GameplayCollection
    return yield if !collection.enabled?
    previous = Thread.current[:ironmon_collection_shop]
    state = { "shop_ref" => Ironmon.respond_to?(:current_area_event_reference) ? Ironmon.current_area_event_reference : nil, "items" => [], "prices" => {}, "kind" => kind }
    Thread.current[:ironmon_collection_shop] = state
    return collection.with_group(kind, "inventory_reason" => kind, "source_kind" => "shop", "source_id" => state["shop_ref"]) { yield }
  ensure
    collection::Resources.observe { collection::Resources.shop_flush(state) } if state
    Thread.current[:ironmon_collection_shop] = previous
  end
end
PokemonMartScreen.prepend(IronmonCollectionShopResources)

module IronmonCollectionShopChoiceResources
  def pbChooseBuyItem(*args)
    return collection_choose_shop_item { super }
  end

  def pbChooseSellItem(*args)
    return collection_choose_shop_item { super }
  end

  def collection_choose_shop_item
    state = Thread.current[:ironmon_collection_shop]
    Ironmon::GameplayCollection::Resources.observe { Ironmon::GameplayCollection::Resources.shop_flush(state) } if state
    result = yield
    state["selected_item"] = result ? GameData::Item.get(result).id : nil if state
    return result
  end
end
PokemonMart_Scene.prepend(IronmonCollectionShopChoiceResources)

module IronmonCollectionShopAdapterResources
  def getPrice(item, selling = false)
    result = super
    state = Thread.current[:ironmon_collection_shop]
    state["prices"][GameData::Item.get(item).id] = selling ? result / 2 : result if state
    return result
  end

  def addItem(item)
    result = super
    state = Thread.current[:ironmon_collection_shop]
    state["items"] << [GameData::Item.get(item).id, 1, state["kind"] == "shop_purchase" && state["money_set"] ? "bonus" : "paid"] if state && result
    return result
  end

  def removeItem(item)
    result = super
    state = Thread.current[:ironmon_collection_shop]
    state["items"] << [GameData::Item.get(item).id, -1, "paid"] if state && result
    return result
  end

  def setMoney(amount)
    state = Thread.current[:ironmon_collection_shop]
    Ironmon::GameplayCollection::Resources.observe { Ironmon::GameplayCollection::Resources.shop_flush(state) } if state && state["balance_delta"]
    before = getMoney
    result = super
    if state
      state["balance_delta"] = getMoney - before
      state["money_set"] = true
    end
    return result
  end
end
PokemonMartAdapter.prepend(IronmonCollectionShopAdapterResources)
