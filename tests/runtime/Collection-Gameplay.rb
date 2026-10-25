module IronmonCollectionGameplayRuntimeTests
  module CaptureTimings
    def encode(value)
      return super if Thread.current != IronmonCollectionGameplayRuntimeTests.capture_thread
      started = clock
      gc_ms = GC.stat[:time] || 0
      result = super
      seconds = clock - started
      IronmonCollectionGameplayRuntimeTests.record_capture_timing("json", seconds)
      if seconds >= 0.005
        IronmonCollectionGameplayRuntimeTests.record_slow_capture({ "type" => value.is_a?(Hash) ? value["type"] : nil, "bytes" => result.bytesize, "elapsed_ms" => seconds * 1000, "gc_ms" => (GC.stat[:time] || 0) - gc_ms })
      end
      return result
    end
  end

  module AppendTimings
    def append(event)
      return super if Thread.current != IronmonCollectionGameplayRuntimeTests.capture_thread
      started = Ironmon::GameplayCollection.clock
      result = super
      IronmonCollectionGameplayRuntimeTests.record_capture_timing("append", Ironmon::GameplayCollection.clock - started)
      return result
    end
  end

  def self.capture_thread
    return @capture_thread
  end

  def self.record_capture_timing(kind, seconds)
    (@capture_timings[kind] ||= []) << seconds * 1000
  end

  def self.record_slow_capture(value)
    @slow_captures << value
  end

  def self.capture_timings
    return @capture_timings.transform_values do |values|
      sorted = values.sort
      { "count" => values.length, "total_ms" => values.sum, "maximum_ms" => sorted[-1], "p95_ms" => sorted[(sorted.length * 0.95).ceil - 1] }
    end
  end

  def self.assert(value, message)
    raise "Gameplay collection runtime: #{message}" if !value
  end

  def self.run
    collection = Ironmon::GameplayCollection
    @capture_thread = Thread.current
    @capture_timings = {}
    @slow_captures = []
    collection.singleton_class.prepend(CaptureTimings)
    collection::Journal.prepend(AppendTimings)
    workload_started = collection.clock
    original = [$Trainer, $PokemonGlobal, $PokemonSystem, $PokemonBag, $PokemonTemp, $game_switches, $game_variables, $game_temp, $game_map]
    original_profile = Ironmon.run_generation_profile_id
    original_suspended = collection.capture_suspended
    collection.capture_suspended = true
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonSystem = PokemonSystem.new
    $PokemonBag = PokemonBag.new
    $PokemonTemp = PokemonTemp.new
    $game_switches = Game_Switches.new
    $game_variables = Game_Variables.new
    $game_temp = Game_Temp.new
    $game_map = Game_Map.new
    trainer_type = nil
    GameData::TrainerType.each { |entry| trainer_type = entry.id; break }
    $Trainer = Player.new("Collection", trainer_type)
    pokemon = Pokemon.new(:BULBASAUR, 20, $Trainer)
    pokemon.moves = [Pokemon::Move.new(:TACKLE), Pokemon::Move.new(:GROWL)]
    $Trainer.party = [pokemon]
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_seed = 12345
    $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new
    $PokemonGlobal.ironmon_run_ledger = Ironmon.default_run_ledger
    $PokemonGlobal.ironmon_run_ledger["current_attempt"] = { "run_id" => "collection-native-runtime", "attempt_number" => 1, "seed" => 12345, "result" => "active" }
    pokemon.calc_stats
    collection.capture_suspended = false
    collection::World.new_attempt("runtime_validation")
    assert(!collection.last_error, collection.last_error)
    first_attempt = collection.attempt_id
    assert(/\A[0-9a-f]{32}\z/.match?(first_attempt), "wire attempt identity is UUID32")
    starter_handoff
    initial = collection.journal.sequence
    pokemon.hp = 1
    restored = pbItemRestoreHP(pokemon, 20)
    assert(restored == [20, pokemon.totalhp - 1].min, "native item healing result preserved")
    pokemon.moves[0].pp = 1
    pp_restored = pbRestorePP(pokemon, 0, 10)
    assert(pp_restored == 10, "native PP result preserved")
    pokemon.moves[0].ppup = 1
    pokemon.status = :PARALYSIS
    pokemon.status = :NONE
    $PokemonBag.pbStoreItem(:POTION, 3)
    assert(collection::Resources.native_item_tags(:POTION).include?("hp_restore"), "native Potion handler is classified as HP restoring")
    assert(!collection::Resources.native_item_tags(:FULLHEAL).include?("hp_restore") && collection::Resources.native_item_tags(:FULLHEAL).include?("status_restore"), "native Full Heal handler is status recovery without HP recovery")
    $PokemonBag.pbDeleteItem(:POTION, 1)
    $Trainer.money += 23
    identity_scene = IronmonCollectionCombatRuntimeTests::QuietScene.new
    $PokemonBag.pbStoreItem(:ORANBERRY, 1)
    assert(pbGiveItemToPokemon(:ORANBERRY, pokemon, identity_scene), "native equip succeeds with tracked units")
    equipped_unit = pokemon.instance_variable_get(:@ironmon_collection_held_unit)["resource_ref"]
    assert(pbTakeItemFromPokemon(pokemon, identity_scene), "native unequip succeeds with tracked units")
    assert(pbGiveItemToPokemon(:ORANBERRY, pokemon, identity_scene), "native re-equip succeeds with tracked units")
    assert(pokemon.instance_variable_get(:@ironmon_collection_held_unit)["resource_ref"] == equipped_unit, "native equip and unequip preserve the same source unit")
    resource_transactions(pokemon)
    achievement_learning_sources(pokemon)
    pokemon.changeHappiness("walking")
    pokemon.givePokerus(1)
    pbJustRaiseEffortValues(pokemon, :ATTACK, 1)
    $game_switches[3000] = true
    $game_switches[Ironmon::DEVON_SWITCH_PEEKO_RESCUED] = true
    $game_switches[Ironmon::DEVON_SWITCH_PEEKO_RESCUED] = true
    quests = %w[main_steven_letter main_devon_parts].map { |id| QUESTS[id].clone }
    quests.each { |quest| quest.completed = false }
    $Trainer.quests = quests
    quests.each { |quest| pbSetQuest(quest.id, true); pbSetQuest(quest.id, true) }
    collection.with_context("story_reconciliation" => true) { quests.each { |quest| pbSetQuest(quest.id, false); pbSetQuest(quest.id, true) } }
    prior_iv = pokemon.iv[:ATTACK]
    pokemon.iv[:ATTACK] = prior_iv == 31 ? 30 : prior_iv + 1
    clone = Marshal.load(Marshal.dump(pokemon))
    assert(clone.iv[:ATTACK] == pokemon.iv[:ATTACK], "observed training hashes preserve native save serialization")
    $Trainer.badges[0] = true
    $Trainer.badges[0] = false
    $Trainer.badges[0] = true
    assert(Marshal.load(Marshal.dump($Trainer)).badges[0], "observed badge commits preserve native save serialization")
    native_battle(pokemon)
    collection.with_context("validation_case" => "programmatic_learning") { pokemon.learn_move(:VINEWHIP) }
    collection::Ownership.poll("runtime_change")
    assert(!collection.last_error, collection.last_error)
    before_baseline = collection.journal.sequence
    collection::Ownership.baseline("reload_test")
    assert(collection.journal.sequence > before_baseline, "loaded configurations remain evidence marked baseline")
    collection::World.close_clock("runtime_test")
    collection::World.leave_loaded("runtime_test_complete")
    collection.flush
    assert(!collection.last_error, collection.last_error)
    assert(collection.journal.sequence > initial + 10, "actual game mutations emitted facts")
    metrics = collection.metrics
    native_actions_and_capture_ms = (collection.clock - workload_started) * 1000
    measured_capture_timings = capture_timings
    measured_capture_timings["slow_json"] = @slow_captures
    @capture_thread = nil
    batches = []
    incarnation = collection.uuid
    if $ironmon_collection_tracker_port
      batches.concat(drain_real_tracker(collection, $ironmon_collection_tracker_port))
    end
    loop do
      batch = collection.journal.next_batch
      break if !batch
      raise "Real tracker left unacknowledged data" if $ironmon_collection_tracker_port
      value = collection.decode(batch["json"])
      batches << value
      prefix = batch["last_sequence"]
      collection.journal.acknowledge({ "profile_id" => collection.journal.profile_id, "epoch_id" => collection.journal.epoch_id, "store_incarnation_id" => incarnation, "stream_id" => batch["stream_id"], "durable_prefix" => prefix })
      deadline = collection.clock + 5
      loop do
        pending = collection.journal.next_batch
        break if !pending || pending["stream_id"] != batch["stream_id"] || pending["last_sequence"] != prefix
        raise "Journal did not publish acknowledgement" if collection.clock >= deadline
        sleep(0.005)
      end
    end
    types = batches.flat_map { |batch| batch["events"].map { |event| event["type"] } }
    ["hp.healing_resolved", "pp.restoration_resolved", "pp.expenditure_resolved", "pp.capacity_change_resolved", "status.major_cure_resolved", "inventory.quantity_changed", "pokemon.owned_configuration_committed", "clock.interval_closed", "world.story_state_changed"].each do |type|
      assert(types.include?(type), "missing native #{type}")
    end
    facts = batches.flat_map { |batch| batch["events"] }.uniq { |event| event["sequence"] }
    File.binwrite(File.join($ironmon_collection_test_root, "batches.json"), collection.encode(batches))
    %w[player automatic favorite].each do |method|
      selected = facts.select { |event| event["context"]["validation_case"] == "starter_handoff_#{method}" }
      receipts = selected.select { |event| event["type"] == "pokemon.starter_selection_resolved" }
      assert(receipts.length == 1, "story return never counts as another starter selection")
      assert(receipts[0]["payload"]["selection_id"] == "native-selection-#{method}" && receipts[0]["payload"]["selected_choice_index"] == 1, "receipt uses original choice identity and slot")
      assert(receipts[0]["payload"]["selection_method"] == (method == "automatic" ? "automatic" : "player"), "receipt preserves actual selection method")
      acquisitions = selected.select { |event| event["type"] == "pokemon.acquisition_resolved" }
      assert(acquisitions.length == 1, "story return never counts as a new acquisition")
      configs = selected.select { |event| event["type"] == "pokemon.owned_configuration_committed" }
      assert(configs.count { |event| event["payload"]["capture_kind"] == "acquisition" } == 1, "base-stat contribution happens once")
      assert(configs.map { |event| event["payload"]["owned_entity_id"] }.uniq.length == 1, "native acquisition clones preserve one retained individual across the story return")
      acquired = configs.find { |event| event["payload"]["capture_kind"] == "acquisition" }
      choice = acquired["payload"]["acquisition"]["starter_selection"]
      assert(choice && choice["selection_id"] == receipts[0]["payload"]["selection_id"] && choice["selected_choice_index"] == 1, "actual choice metadata is retained in the stored Pokemon details")
      assert(configs.any? { |event| event["payload"]["change_reasons"].include?("starter_story_return") }, "story return configuration remains retained")
    end
    validate_resource_transactions(facts)
    validate_end_of_round_abilities(facts)
    field_uses = facts.select { |event| event["type"] == "item.use_resolved" && event["context"]["validation_case"] == "field_item_commit" }
    assert(field_uses.length == 2 && field_uses.all? { |event| event["payload"]["consumed_quantity"] == 1 && event["payload"]["resource_ref"] }, "separate field activations retain their actual consumed source units")
    assert(facts.any? { |event| event["type"] == "pokemon.training_attributes_changed" }, "native training mutations retained")
    owned = facts.select { |event| event["type"] == "pokemon.owned_configuration_committed" }
    assert(owned.all? { |event| event["payload"]["training_traits"]["iv"].values.all? { |value| value.is_a?(Integer) } }, "real six-stat IVs retained")
    assert(owned.any? { |event| event["payload"]["moves"].length == 2 }, "simultaneous moveset retained")
    protected_gate = facts.find { |event| event["type"] == "move.target_gate_resolved" && event["payload"]["gate_result"] == "protected" }
    assert(protected_gate && protected_gate["payload"]["gate_mechanic_context"]["mechanic_id"] == "Protect", "native winning protection branch retained")
    blocked = facts.find { |event| event["type"] == "move.attempted" && event["payload"]["attempt_outcome"] == "blocked" }
    assert(blocked && blocked["payload"]["block_cause_ref"]["mechanic_id"] == "Flinch", "native prevented action names the actual winning branch")
    accuracy = facts.find { |event| event["type"] == "move.accuracy_resolved" && event["payload"]["check_kind"] == "rolled" }
    assert(accuracy && accuracy["payload"]["effective_accuracy"].is_a?(Numeric), "actual rolled accuracy threshold retained")
    prevented = facts.find { |event| event["type"] == "status.major_prevention_resolved" }
    assert(prevented && prevented["payload"]["prevention_kind"] == "existing_status", "native status prevention branch retained")
    hazards = facts.select { |event| event["type"] == "hazard.resolved" }.map { |event| [event["payload"]["hazard_id"], event["payload"]["outcome"]] }
    [["StealthRock", "applied"], ["Spikes", "airborne"], ["ToxicSpikes", "absorbed"], ["StickyWeb", "applied"]].each do |expected|
      assert(hazards.include?(expected), "actual native hazard outcome retained: #{expected}")
    end
    round = facts.find { |event| event["type"] == "round.finished" }
    assert(round && round["payload"]["field_exposure_segments"].any? { |entry| entry["weather"] == "Sun" } && round["payload"]["field_exposure_segments"].any? { |entry| entry["weather"] == "Rain" }, "field exposure preserves changes within one round")
    faint = facts.find { |event| event["type"] == "battler.fainted" }
    assert(faint && faint["payload"]["terminal_hp_loss_ref"] && faint["payload"]["cause"]["source_id"] == "COUNTER", "faint retains actual terminal loss cause")
    learning = facts.find { |event| event["type"] == "pokemon.move_learning_resolved" && event["context"]["validation_case"] == "programmatic_learning" }
    assert(learning && !learning["payload"]["offered_to_player"] && learning["payload"]["outcome"] == "learned", "native silent learning has an actual learning outcome")
    battle_learnings = facts.select { |event| event["type"] == "pokemon.move_learning_resolved" && event["context"]["validation_case"] == "native_battle_learning" }
    assert(battle_learnings.map { |event| event["payload"]["outcome"] }.sort == %w[already_known learned], "native battle learning records the actual mutation once")
    assert(battle_learnings.all? { |event| event["payload"]["source_kind"] == "level_up" }, "native battle learning has an explicit level-up source")
    %w[tm tutor].each do |kind|
      event = facts.find { |entry| entry["type"] == "pokemon.move_learning_resolved" && entry["context"]["validation_case"] == "achievement_learning_#{kind}" }
      assert(event && event["payload"]["source_kind"] == kind && event["payload"]["outcome"] == "learned" && event["payload"]["learning_context"]["actual_new_or_replacement"], "native accepted #{kind} learning retains its exact source")
    end
    capture = facts.reverse.find { |event| event["type"] == "capture.resolved" }
    assert(capture && capture["payload"]["completed_throw_context"]["usable_balls_before"] == @expected_balls_before_reservation, "capture count includes the actual ball reserved from the Bag")
    money = facts.select { |event| event["type"] == "economy.balance_changed" }
    ["trainer_prize", "money_move_reward", "blackout_loss"].each do |reason|
      assert(money.any? { |event| event["payload"]["reason"] == reason && event["payload"]["actual_delta"] != 0 }, "actual native money flow classified separately: #{reason}")
    end
    badge_awards = facts.count { |event| event["type"] == "world.badge_state_changed" && event["payload"]["owned_after"] }
    assert(badge_awards == 2, "badge commits between world polls are retained without poll duplication")
    own_berry = facts.find { |event| event["type"] == "held_item.effect_resolved" && ["own_held", "end_of_round"].include?(event["payload"]["activation_kind"]) && event["payload"]["item_id"] == "ORANBERRY" && event["payload"]["consumed_quantity"] == 1 }
    assert(own_berry && own_berry["payload"]["effect_succeeded"], "held berry resolution includes its actual later consumption")
    flung_berry = facts.find { |event| event["type"] == "held_item.effect_resolved" && event["payload"]["activation_kind"] == "flung_item" && event["payload"]["effect_succeeded"] }
    assert(flung_berry && flung_berry["payload"].dig("holder", "owner_role") == "player" && flung_berry["payload"].dig("recipient", "owner_role") == "opponent", "Fling preserves original holder and actual consumer separately")
    %w[story:peeko_rescued quest:main_steven_letter quest:main_devon_parts].each do |identity|
      assert(facts.count { |event| event["type"] == "world.story_state_changed" && event["payload"]["story_fact_id"] == identity && event["payload"]["transition_kind"] == "completed" } == 1, "native story completion is counted once and never reconstructed by quest-log synchronization: #{identity}")
    end
    File.binwrite(File.join($ironmon_collection_test_root, "batches.json"), collection.encode(batches))
    File.binwrite(File.join($ironmon_collection_test_root, "gameplay.json"), collection.encode({ "status" => "passed", "events" => facts.length, "event_types" => types.uniq, "metrics" => metrics, "native_actions_and_capture_ms" => native_actions_and_capture_ms, "capture_timings" => measured_capture_timings, "socket_drain_ms" => @socket_drain_ms }))
    collection.journal.close
    collection.instance_variable_set(:@journal, nil)
  ensure
    @capture_thread = nil
    collection.capture_suspended = true if collection
    Ironmon.deactivate_run_generation_profile
    $Trainer, $PokemonGlobal, $PokemonSystem, $PokemonBag, $PokemonTemp, $game_switches, $game_variables, $game_temp, $game_map = original if original
    Ironmon.activate_run_generation_profile(original_profile) if original_profile
    collection.capture_suspended = original_suspended if collection
  end

  def self.resource_transactions(pokemon)
    collection = Ironmon::GameplayCollection
    resources = collection::Resources
    collection.with_context("validation_case" => "field_item_commit") do
      $PokemonBag.pbStoreItem(:POTION, 2)
      resources.bag_session(:POTION, pokemon) do
        2.times do
          pokemon.hp = 1
          assert(ItemHandlers.triggerUseOnPokemon(:POTION, pokemon, IronmonCollectionResourceRuntimeTests::QuietScene.new), "native Potion handler applies actual HP recovery")
          $PokemonBag.pbDeleteItem(:POTION, 1)
        end
      end
    end
    collection.with_context("validation_case" => "direct_recovery") do
      pokemon.hp = 1
      pokemon.heal_HP
      pokemon.status = :PARALYSIS
      pokemon.heal_status
      assert(pokemon.hp == pokemon.totalhp && pokemon.status == :NONE, "native direct recovery result preserved")
    end
    collection.with_context("validation_case" => "wallet_cap") do
      $Trainer.money = Settings::MAX_MONEY
      $Trainer.money += 47
      assert($Trainer.money == Settings::MAX_MONEY, "native wallet cap preserved")
    end
    previous = Thread.current[:ironmon_collection_shop]
    adapter = PokemonMartAdapter.new
    collection.with_context("validation_case" => "shop_rollback", "inventory_reason" => "shop_purchase") do
      state = { "shop_ref" => nil, "kind" => "shop_purchase", "selected_item" => :POKEBALL, "items" => [], "prices" => {} }
      Thread.current[:ironmon_collection_shop] = state
      assert(adapter.addItem(:POKEBALL), "native partial purchase adds item")
      assert(adapter.removeItem(:POKEBALL), "native purchase rollback removes item")
      resources.shop_flush(state)
    end
    collection.with_context("validation_case" => "shop_commit", "inventory_reason" => "shop_purchase") do
      state = { "shop_ref" => nil, "kind" => "shop_purchase", "selected_item" => :POKEBALL, "items" => [], "prices" => {} }
      Thread.current[:ironmon_collection_shop] = state
      price = adapter.getPrice(:POKEBALL)
      10.times { assert(adapter.addItem(:POKEBALL), "native purchase delivers paid item") }
      adapter.setMoney(adapter.getMoney - price * 10)
      assert(adapter.addItem(:PREMIERBALL), "native purchase delivers bonus item")
      resources.shop_flush(state)
    end
  ensure
    Thread.current[:ironmon_collection_shop] = previous
  end

  def self.validate_resource_transactions(facts)
    selected = ->(name) { facts.select { |event| event["context"]["validation_case"] == name } }
    rolled_back = selected.call("shop_rollback")
    assert(rolled_back.count { |event| event["type"] == "inventory.quantity_changed" } == 2, "rollback retains actual inventory changes")
    assert(rolled_back.none? { |event| event["type"] == "item.provenance_registered" || event["type"] == "shop.purchase_resolved" }, "failed transaction cannot award acquisition")
    committed = selected.call("shop_commit")
    purchase = committed.find { |event| event["type"] == "shop.purchase_resolved" }["payload"]
    assert(purchase["delivered_paid_quantity"] == 10 && purchase["bonus_items"][0]["quantity"] == 1, "paid and bonus quantities kept distinct")
    lots = committed.select { |event| event["type"] == "item.provenance_registered" }.map { |event| event["payload"]["quantity"] }.sort
    assert(lots == [1, 10], "only committed paid and bonus acquisitions counted")
    capped = selected.call("wallet_cap").find { |event| event["type"] == "economy.balance_changed" && event["payload"]["requested_delta"] == 47 }
    assert(capped && capped["payload"]["actual_delta"] == 0 && capped["payload"]["unapplied_amount"]["at_cap"] == 47, "fully capped wallet request remains measurable")
    recovery = selected.call("direct_recovery")
    assert(recovery.any? { |event| event["type"] == "hp.healing_resolved" && event["payload"]["healing_amount_kind"] == "full" }, "direct native full heal captured")
    assert(recovery.any? { |event| event["type"] == "status.major_cure_resolved" }, "direct native status cure captured")
  end

  def self.achievement_learning_sources(pokemon)
    collection = Ironmon::GameplayCollection
    original_moves = pokemon.moves.dup
    original_message = Object.instance_method(:pbMessage)
    Object.send(:define_method, :pbMessage) { |*_args, &_block| nil }
    machine = nil
    GameData::Item.each { |item| machine = item.id; break if item.is_TM? }
    assert(machine && GameData::Item.get(machine).is_TM?, "native machine item is available")
    collection.with_context("validation_case" => "achievement_learning_tm", "learning_source" => "machine", "source_kind" => "item", "source_id" => machine.to_s) { pbLearnMove(pokemon, :SPLASH, true, true) }
    pokemon.moves.replace(original_moves)
    collection.with_context("validation_case" => "achievement_learning_tutor", "learning_source" => "tutor") { pbLearnMove(pokemon, :SPLASH, true) }
  ensure
    pokemon.moves.replace(original_moves) if original_moves
    Object.send(:define_method, :pbMessage, original_message) if original_message
    Object.send(:private, :pbMessage)
  end

  def self.native_battle(pokemon)
    collection = Ironmon::GameplayCollection
    previous = $game_temp.in_battle
    $game_temp.in_battle = true
    opponent = Pokemon.new(:SNORLAX, 80, nil, false)
    opponent.moves.replace([Pokemon::Move.new(:SPLASH)])
    battle = PokeBattle_Battle.new(IronmonCollectionCombatRuntimeTests::QuietScene.new, [pokemon], [opponent], $Trainer, nil)
    battle.internalBattle = false
    battle.expGain = false
    random = Random.new(19_811)
    battle.define_singleton_method(:pbRandom) { |maximum| random.rand(maximum) }
    battle.positions[0], battle.positions[1] = PokeBattle_ActivePosition.new, PokeBattle_ActivePosition.new
    collection::Combat.admit(battle)
    player, enemy = PokeBattle_Battler.new(battle, 0), PokeBattle_Battler.new(battle, 1)
    battle.battlers[0], battle.battlers[1] = player, enemy
    player.pbInitialize(pokemon, 0)
    enemy.pbInitialize(opponent, 0)
    collection.with_context("validation_case" => "native_battle_learning") do
      battle.pbLearnMove(0, :RAZORLEAF)
      battle.pbLearnMove(0, :RAZORLEAF)
    end
    player.define_singleton_method(:checkChallengesAfterTurn) { nil }
    battle.successStates[0], battle.successStates[1] = PokeBattle_SuccessState.new, PokeBattle_SuccessState.new
    battle.choices[0] = [:UseMove, 0, player.moves[0], 1, 0]
    battle.choices[1] = [:UseMove, 0, enemy.moves[0], 0, 0]
    battle.pbCalculatePriority(true)
    player.ability = :OVERGROW
    enemy.ability = :THICKFAT
    invocation = collection::Combat.begin_move(player, battle.choices[0], false)
    collection::Combat.with_context({ "source_kind" => "move", "source_id" => "TACKLE", "source_actor" => collection::Combat.actor_ref(player) }, battle) do
      enemy.effects[PBEffects::Protect] = true
      assert(!player.pbSuccessCheckAgainstTarget(player.moves[0], player, enemy), "native Protect actually blocks the target")
      enemy.effects[PBEffects::Protect] = false
      player.effects[PBEffects::Flinch] = true
      assert(!player.pbTryUseMove(battle.choices[0], player.moves[0], false, false), "native flinch actually prevents action")
      player.effects[PBEffects::Flinch] = false
      invocation[:attempted] = false
      enemy.status = :BURN
      assert(!enemy.pbCanInflictStatus?(:POISON, player, false), "native existing status actually prevents replacement")
      enemy.pbCureStatus(false)
      player.pbSuccessCheckPerHit(player.moves[0], player, enemy, false)
    end
    collection::Combat.finish_move(player, invocation, true)
    before = enemy.hp
    player.pbUseMove(battle.choices[0])
    assert(enemy.hp < before, "real native move dealt damage through the durable collector")
    native_source_cases(battle, player, enemy)
    replacement = Pokemon.new(:PIDGEY, 10, nil, false)
    battle.pbParty(1) << replacement
    enemy.pbInitialize(replacement, 1)
    combat = collection::Combat
    combat.with_context("source_kind" => "move", "source_id" => "PERISHSONG", "source_actor" => combat.actor_ref(enemy)) { player.effects[PBEffects::PerishSong] = 1 }
    battle.decision = 1
    battle.define_singleton_method(:pbGainCosmeticsMoney) { 0 }
    battle.pbEndOfBattle
  ensure
    collection::Combat.release_battle(battle, !collection::Combat.state(battle)&.fetch(:ended, false)) if battle
    $game_temp.in_battle = previous
  end

  def self.starter_handoff
    collection = Ironmon::GameplayCollection
    original_party = $Trainer.party.dup
    original_map = $game_map.map_id
    original_starter = pbGet(VAR_HOENN_STARTER)
    original_selection = Ironmon.instance_variable_get(:@tracker_starter_selection)
    original_message = Object.instance_method(:pbMessage)
    Object.send(:define_method, :pbMessage) { |*_args| true }
    [:player, :automatic, :favorite].each do |method|
      collection.with_context("validation_case" => "starter_handoff_#{method}") do
        $Trainer.party.clear
        $game_map.instance_variable_set(:@map_id, 5)
        choices = [:BULBASAUR, :CHARMANDER, :SQUIRTLE].map { |species| Pokemon.new(species, 5, $Trainer) }
        selection_id = "native-selection-#{method}"
        Ironmon.instance_variable_set(:@tracker_starter_selection, {
          :selection_id => selection_id, :pokemon => choices, :auto_select => method != :player,
          :revealed => [true, true, true], :favorites => [false, false, false], :ceiling => nil,
          :sprites => [nil, nil, nil], :random_pick_index => 1
        })
        scene = StartersSelectionScene.allocate
        scene.instance_variable_set(:@starter_pokemon, choices)
        scene.instance_variable_set(:@index, 1)
        scene.instance_variable_set(:@ironmon_collection_favorite_choice, method == :favorite)
        loader = Object.new
        loader.define_singleton_method(:registerSpriteSubstitution) { |_sprite| nil }
        scene.instance_variable_set(:@spritesLoader, loader)
        scene.define_singleton_method(:disposeGraphics) { nil }
        chosen = scene.ironmon_tracker_finalize_starter
        Ironmon.instance_variable_set(:@tracker_starter_selection, nil)
        pbSet(VAR_HOENN_STARTER, chosen)
        assert(Ironmon.with_starter_acquisition { pbAddPokemonSilent(chosen) }, "native starter loan succeeds")
        # The story removes the loan before Birch restores the same saved individual.
        $Trainer.party.clear
        chosen = Marshal.load(Marshal.dump(chosen))
        pbSet(VAR_HOENN_STARTER, chosen)
        $game_map.instance_variable_set(:@map_id, 18)
        assert(pbAddPokemon(chosen, 5, true, true), "native lab return succeeds after save serialization")
        metadata = chosen.instance_variable_get(:@ironmon_collection_selected_starter)
        assert(metadata["selection_id"] == selection_id && metadata["choice_index"] == 1, "original choice context survives UI teardown")
        assert(metadata["method"] == (method == :automatic ? "automatic" : "player"), "actual selection origin retained")
      end
    end
  ensure
    Object.send(:define_method, :pbMessage, original_message) if original_message
    $Trainer.party = original_party if original_party
    $game_map.instance_variable_set(:@map_id, original_map)
    pbSet(VAR_HOENN_STARTER, original_starter)
    Ironmon.instance_variable_set(:@tracker_starter_selection, original_selection)
  end

  def self.native_source_cases(battle, player, enemy)
    collection = Ironmon::GameplayCollection
    combat = collection::Combat
    combat.start_round(battle)
    combat.with_context("phase" => "attack", "source_kind" => "move", "source_id" => "SUNNYDAY", "source_actor" => combat.actor_ref(player)) do
      battle.field.weather = :Sun
      battle.field.weather = :Rain
    end
    combat.finish_round(battle, "complete")
    native_end_of_round_abilities(battle, player, enemy)
    side = enemy.pbOwnSide
    side.effects[PBEffects::StealthRock] = true
    battle.pbOnActiveOne(enemy)
    side.effects[PBEffects::StealthRock] = false
    side.effects[PBEffects::Spikes] = 1
    enemy.ability = :LEVITATE
    battle.pbOnActiveOne(enemy)
    side.effects[PBEffects::Spikes] = 0
    enemy.ability = :THICKFAT
    side.effects[PBEffects::StickyWeb] = true
    battle.pbOnActiveOne(enemy)
    side.effects[PBEffects::StickyWeb] = false
    player.pbOwnSide.effects[PBEffects::ToxicSpikes] = 2
    player.pbChangeTypes(:POISON)
    battle.pbOnActiveOne(player)
    player.item = :ORANBERRY
    enemy.item = :PECHABERRY
    first = player.pokemon.instance_variable_get(:@ironmon_collection_held_unit)["resource_ref"]
    second = enemy.pokemon.instance_variable_get(:@ironmon_collection_held_unit)["resource_ref"]
    trick = PokeBattle_Move.from_pokemon_move(battle, Pokemon::Move.new(:TRICK))
    combat.with_context("phase" => "attack", "source_kind" => "move", "source_id" => "TRICK", "source_actor" => combat.actor_ref(player)) { trick.pbEffectAgainstTarget(player, enemy) }
    assert(player.pokemon.instance_variable_get(:@ironmon_collection_held_unit)["resource_ref"] == second && enemy.pokemon.instance_variable_get(:@ironmon_collection_held_unit)["resource_ref"] == first, "native Trick transfers exact source units without item-ID guesses")
    player.pbConsumeItem
    recycle = PokeBattle_Move.from_pokemon_move(battle, Pokemon::Move.new(:RECYCLE))
    combat.with_context("phase" => "attack", "source_kind" => "move", "source_id" => "RECYCLE", "source_actor" => combat.actor_ref(player)) { recycle.pbEffectGeneral(player) }
    recovered = player.pokemon.instance_variable_get(:@ironmon_collection_held_unit)
    assert(recovered && recovered["parent_resource_refs"] == [second], "native Recycle retains the actual consumed-unit parent")
    player.item = :ORANBERRY
    player.pbReduceHP(player.hp - 1)
    combat.with_context("phase" => "end_of_round", "source_kind" => "environment") { player.pbItemHPHealCheck }
    assert(player.item.nil? && player.hp > 1, "actual native berry heals and is consumed")
    player.item = :ORANBERRY
    fling = PokeBattle_Move.from_pokemon_move(battle, Pokemon::Move.new(:FLING))
    combat.with_context("phase" => "attack", "source_kind" => "move", "source_id" => "FLING", "source_actor" => combat.actor_ref(player)) do
      fling.pbEffectAgainstTarget(player, enemy)
      fling.pbEndOfMoveUsageEffect(player, [enemy], 1, [])
    end
    $PokemonBag.pbStoreItem(:POTION, 1)
    battle.pbRegisterItem(player.index, :POTION, 0)
    reserved = combat.state(battle)[:bag_reservations][player.index]["resource_ref"]
    battle.pbReturnUnusedItemToBag(:POTION, player.index)
    assert(collection::ItemIdentity.state["bag"]["POTION"].any? { |entry| entry["resource_ref"] == reserved }, "native rejected item returns the reserved source unit")
    $PokemonBag.pbStoreItem(:POKEBALL, 1)
    @expected_balls_before_reservation = combat.usable_balls
    battle.pbRegisterItem(player.index, :POKEBALL, enemy.index)
    $game_switches[SWITCH_CANNOT_CATCH_POKEMON] = true
    battle.pbUsePokeBallInBattle(:POKEBALL, enemy.index, player)
    $game_switches[SWITCH_CANNOT_CATCH_POKEMON] = false
    previous_opponents = battle.instance_variable_get(:@opponent)
    previous_money_gain = battle.instance_variable_get(:@moneyGain)
    previous_internal = battle.internalBattle
    $Trainer.money = 1000
    battle.instance_variable_set(:@opponent, [NPCTrainer.new("CollectionMoney", $Trainer.trainer_type)])
    battle.instance_variable_set(:@moneyGain, true)
    battle.internalBattle = true
    battle.field.effects[PBEffects::PayDay] = 7
    battle.pbGainMoney
    battle.pbLoseMoney
    battle.field.effects[PBEffects::PayDay] = 0
    battle.instance_variable_set(:@opponent, previous_opponents)
    battle.instance_variable_set(:@moneyGain, previous_money_gain)
    battle.internalBattle = previous_internal
    combat.with_context("phase" => "attack", "source_kind" => "move", "source_id" => "COUNTER", "source_actor" => combat.actor_ref(player), "loss_kind" => "move_hit") do
      enemy.pbReduceHP(enemy.hp)
      enemy.pbFaint(false)
    end
  end

  def self.native_end_of_round_abilities(battle, player, enemy)
    collection = Ironmon::GameplayCollection
    previous = [player.ability, enemy.ability, battle.field.weather, battle.field.weatherDuration]
    player.ability = :HARVEST
    enemy.ability = :PICKUP
    [player, enemy].each do |battler|
      battler.item = nil
      battler.setRecycleItem(nil)
      battler.hp = battler.totalhp
      battler.effects[PBEffects::PickupItem] = nil
      battler.effects[PBEffects::PickupUse] = 0
    end
    battle.field.weather = :Sun
    battle.field.weatherDuration = -1
    collection.with_context("validation_case" => "eor_ability_no_item") { battle.pbEndOfRoundPhase }
    assert(player.item.nil? && enemy.item.nil?, "native Harvest and Pickup safely leave empty hands without a recoverable item")
    player.setRecycleItem(:ORANBERRY)
    player.effects[PBEffects::PickupItem] = :SITRUSBERRY
    player.effects[PBEffects::PickupUse] = 1
    collection.with_context("validation_case" => "eor_ability_gain_item") { battle.pbEndOfRoundPhase }
    assert(player.item == :ORANBERRY && enemy.item == :SITRUSBERRY, "native end-of-round Harvest and Pickup still recover their actual items")
    assert(!collection.last_error, collection.last_error)
  ensure
    player.ability, enemy.ability, battle.field.weather, battle.field.weatherDuration = previous if previous
  end

  def self.validate_end_of_round_abilities(facts)
    %w[eor_ability_no_item eor_ability_gain_item].each do |name|
      events = facts.select { |event| event["context"]["validation_case"] == name }
      abilities = events.select { |event| event["type"] == "ability.effect_resolved" && event["payload"]["handler_family"] == "triggerEORGainItemAbility" }
      assert(abilities.map { |event| event["payload"]["ability_id"] }.sort == %w[HARVEST PICKUP], "both native item-gaining abilities are retained as ability activations")
      assert(abilities.all? { |event| event["payload"]["effect_succeeded"] == (name == "eor_ability_gain_item") }, "native item-gaining ability success follows actual item recovery")
      assert(events.none? { |event| event["type"] == "held_item.effect_resolved" && %w[HARVEST PICKUP].include?(event["payload"]["item_id"]) }, "an ability is never recorded as a held item")
    end
  end

  def self.drain_real_tracker(collection, port)
    connection = Ironmon::TrackerConnection.new
    observed = {}
    connection.define_singleton_method(:send_collection_json) do |event, raw|
      transfer = collection.instance_variable_get(:@transfer)
      observed[[transfer["stream_id"], transfer["last_sequence"]]] ||= transfer["json"] if transfer
      super(event, raw)
    end
    socket = TCPSocket.new("127.0.0.1", port)
    connection.instance_variable_set(:@state, :connected)
    connection.instance_variable_set(:@socket, socket)
    connection.instance_variable_set(:@last_heartbeat, Ironmon.tracker_uptime_seconds)
    connection.define_singleton_method(:handle_request) { |_message| nil }
    connection.send(:queue_message, connection.send(:game_handshake_message))
    deadline = collection.clock + 30.0
    started = collection.clock
    loop do
      connection.update
      break if collection.journal.metrics["pending_events"] == 0
      raise "Real collection socket did not acknowledge: #{collection.metrics}" if collection.clock >= deadline
      sleep(0.005)
    end
    @socket_drain_ms = (collection.clock - started) * 1000
    assert(connection.session_ready? && connection.collection_supported?, "actual tracker handshake negotiated collection")
    assert(!collection.instance_variable_get(:@transport_error), "actual tracker accepted native facts")
    File.binwrite(File.join($ironmon_collection_test_root, "transport.json"), collection.encode({ "status" => "passed", "pending_events" => collection.journal.metrics["pending_events"] }))
    return observed.values.map { |raw| collection.decode(raw) }
  ensure
    connection.send(:disconnect) if connection
  end
end
