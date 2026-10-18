module Ironmon
  module GameplayCollection
    module Combat
      EFFECT_METADATA_REVISION = "source-audit-2026-10-16-2"
      # Explicit mechanics, audited against PBEffects and their native writers.
      # Other slots remain readable generic field facts, never volatile episodes
      # or evidence that a move succeeded merely because bookkeeping changed.
      VOLATILE_MECHANICS = %w[
        AquaRing Attract BanefulBunker Bide BurnUp Charge Confusion Curse
        DefenseCurl DestinyBond Disable Electrify Embargo Encore Endure FlashFire
        Flinch FocusEnergy FocusPunch FollowMe Foresight GastroAcid Grudge
        HealBlock HelpingHand HyperBeam Imprison Ingrain KingsShield LaserFocus
        LeechSeed LockOn MagicCoat MagnetRise MeanLook Minimize MiracleEye
        MudSport Nightmare Outrage PerishSong Powder PowerTrick Protect Quash
        Rage RagePowder Rollout Roost ShellTrap SkyDrop SlowStart SmackDown
        Snatch SpikyShield Spotlight Stockpile Substitute Taunt Telekinesis
        ThroatChop Torment Transform Trapping Truant TwoTurnAttack Type3
        Uproar WaterSport WeightChange Yawn
      ].freeze
      FIELD_MECHANICS = {
        "position" => %w[FutureSightCounter HealingWish LunarDance Wish],
        "side" => %w[AuroraVeil CraftyShield LightScreen LuckyChant MatBlock Mist QuickGuard Rainbow Reflect Safeguard SeaOfFire Spikes StealthRock StickyWeb Swamp Tailwind ToxicSpikes WideGuard],
        "field" => %w[FairyLock Gravity HappyHour IonDeluge MagicRoom MudSportField PayDay TrickRoom WaterSportField WonderRoom]
      }.freeze
      EFFECT_DECLARATIONS = [
        ["battler", "AquaRing", 0, "boolean", "not_duration", false, nil],
        ["battler", "Attract", 1, "battler_index", "not_duration", false, nil],
        ["battler", "BanefulBunker", 2, "boolean", "not_duration", false, nil],
        ["battler", "BeakBlast", 3, "boolean", "not_duration", false, nil],
        ["battler", "Bide", 4, "remaining_action_counter", "actions", true, nil],
        ["battler", "BideDamage", 5, "hp_quantity", "not_duration", true, nil],
        ["battler", "BideTarget", 6, "battler_index", "not_duration", false, nil],
        ["battler", "BurnUp", 7, "boolean", "not_duration", false, nil],
        ["battler", "Charge", 8, "mechanic_state", "phase_ticks", false, nil],
        ["battler", "ChoiceBand", 9, "MoveId", "not_duration", false, nil],
        ["battler", "Confusion", 10, "remaining_action_counter", "actions", true, nil],
        ["battler", "Counter", 11, "hp_quantity", "not_duration", true, nil],
        ["battler", "CounterTarget", 12, "battler_index", "not_duration", false, nil],
        ["battler", "Curse", 13, "boolean", "not_duration", false, nil],
        ["battler", "Dancer", 14, "boolean", "not_duration", false, nil],
        ["battler", "DefenseCurl", 15, "boolean", "not_duration", false, nil],
        ["battler", "DestinyBond", 16, "boolean", "not_duration", false, nil],
        ["battler", "DestinyBondPrevious", 17, "boolean", "not_duration", false, nil],
        ["battler", "DestinyBondTarget", 18, "battler_index", "not_duration", false, nil],
        ["battler", "Disable", 19, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "DisableMove", 20, "MoveId", "not_duration", false, nil],
        ["battler", "Electrify", 21, "boolean", "not_duration", false, nil],
        ["battler", "Embargo", 22, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "Encore", 23, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "EncoreMove", 24, "MoveId", "not_duration", false, nil],
        ["battler", "Endure", 25, "boolean", "not_duration", false, nil],
        ["battler", "FirstPledge", 26, "MoveFunctionId", "not_duration", false, nil],
        ["battler", "FlashFire", 27, "boolean", "not_duration", false, nil],
        ["battler", "Flinch", 28, "boolean", "not_duration", false, nil],
        ["battler", "FocusEnergy", 29, "mechanic_count", "not_duration", true, nil],
        ["battler", "FocusPunch", 30, "boolean", "not_duration", false, nil],
        ["battler", "FollowMe", 31, "ordering_rank", "not_duration", true, nil],
        ["battler", "Foresight", 32, "boolean", "not_duration", false, nil],
        ["battler", "FuryCutter", 33, "mechanic_count", "not_duration", true, nil],
        ["battler", "GastroAcid", 34, "boolean", "not_duration", false, nil],
        ["battler", "GemConsumed", 35, "ItemId", "not_duration", false, nil],
        ["battler", "Grudge", 36, "boolean", "not_duration", false, nil],
        ["battler", "HealBlock", 37, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "HelpingHand", 38, "boolean", "not_duration", false, nil],
        ["battler", "HyperBeam", 39, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "Illusion", 40, "PokemonInstanceRef", "not_duration", false, nil],
        ["battler", "Imprison", 41, "boolean", "not_duration", false, nil],
        ["battler", "Ingrain", 42, "boolean", "not_duration", false, nil],
        ["battler", "Instruct", 43, "boolean", "not_duration", false, nil],
        ["battler", "Instructed", 44, "boolean", "not_duration", false, nil],
        ["battler", "KingsShield", 45, "boolean", "not_duration", false, nil],
        ["battler", "LaserFocus", 46, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "LeechSeed", 47, "battler_index", "not_duration", false, nil],
        ["battler", "LockOn", 48, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "LockOnPos", 49, "battler_index", "not_duration", false, nil],
        ["battler", "MagicBounce", 50, "boolean", "not_duration", false, nil],
        ["battler", "MagicCoat", 51, "boolean", "not_duration", false, nil],
        ["battler", "MagnetRise", 52, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "MeanLook", 53, "battler_index", "not_duration", false, nil],
        ["battler", "MeFirst", 54, "boolean", "not_duration", false, nil],
        ["battler", "Metronome", 55, "mechanic_count", "not_duration", true, nil],
        ["battler", "MicleBerry", 56, "boolean", "not_duration", false, nil],
        ["battler", "Minimize", 57, "boolean", "not_duration", false, nil],
        ["battler", "MiracleEye", 58, "boolean", "not_duration", false, nil],
        ["battler", "MirrorCoat", 59, "hp_quantity", "not_duration", true, nil],
        ["battler", "MirrorCoatTarget", 60, "battler_index", "not_duration", false, nil],
        ["battler", "MoveNext", 61, "boolean", "not_duration", false, nil],
        ["battler", "MudSport", 62, "boolean", "not_duration", false, nil],
        ["battler", "Nightmare", 63, "boolean", "not_duration", false, nil],
        ["battler", "Outrage", 64, "remaining_action_counter", "actions", true, nil],
        ["battler", "ParentalBond", 65, "hit_state", "hits", false, nil],
        ["battler", "PerishSong", 66, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "PerishSongUser", 67, "battler_index", "not_duration", false, nil],
        ["battler", "PickupItem", 68, "ItemId", "not_duration", false, nil],
        ["battler", "PickupUse", 69, "ordering_rank", "not_duration", true, nil],
        ["battler", "Pinch", 70, "boolean", "not_duration", false, nil],
        ["battler", "Powder", 71, "boolean", "not_duration", false, nil],
        ["battler", "PowerTrick", 72, "boolean", "not_duration", false, nil],
        ["battler", "Prankster", 73, "boolean", "not_duration", false, nil],
        ["battler", "PriorityAbility", 74, "boolean", "not_duration", false, nil],
        ["battler", "PriorityItem", 75, "boolean", "not_duration", false, nil],
        ["battler", "Protect", 76, "boolean", "not_duration", false, nil],
        ["battler", "ProtectRate", 77, "probability_denominator", "not_duration", true, nil],
        ["battler", "Pursuit", 78, "boolean", "not_duration", false, nil],
        ["battler", "Quash", 79, "ordering_rank", "not_duration", true, nil],
        ["battler", "Rage", 80, "boolean", "not_duration", false, nil],
        ["battler", "RagePowder", 81, "boolean", "not_duration", false, nil],
        ["battler", "Rollout", 82, "remaining_action_counter", "actions", true, nil],
        ["battler", "Roost", 83, "boolean", "not_duration", false, nil],
        ["battler", "ShellTrap", 84, "boolean", "not_duration", false, nil],
        ["battler", "SkyDrop", 85, "battler_index", "not_duration", false, nil],
        ["battler", "SlowStart", 86, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "SmackDown", 87, "boolean", "not_duration", false, nil],
        ["battler", "Snatch", 88, "ordering_rank", "not_duration", true, nil],
        ["battler", "SpikyShield", 89, "boolean", "not_duration", false, nil],
        ["battler", "Spotlight", 90, "ordering_rank", "not_duration", true, nil],
        ["battler", "Stockpile", 91, "mechanic_count", "not_duration", true, nil],
        ["battler", "StockpileDef", 92, "mechanic_count", "not_duration", true, nil],
        ["battler", "StockpileSpDef", 93, "mechanic_count", "not_duration", true, nil],
        ["battler", "Substitute", 94, "hp_quantity", "not_duration", true, nil],
        ["battler", "Taunt", 95, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "Telekinesis", 96, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "ThroatChop", 97, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "Torment", 98, "boolean", "not_duration", false, nil],
        ["battler", "Toxic", 99, "mechanic_count", "not_duration", true, nil],
        ["battler", "Transform", 100, "boolean", "not_duration", false, nil],
        ["battler", "TransformSpecies", 101, "SpeciesId", "not_duration", false, nil],
        ["battler", "Trapping", 102, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "TrappingMove", 103, "MoveId", "not_duration", false, nil],
        ["battler", "TrappingUser", 104, "battler_index", "not_duration", false, nil],
        ["battler", "Truant", 105, "boolean", "not_duration", false, nil],
        ["battler", "TwoTurnAttack", 106, "MoveId", "not_duration", false, nil],
        ["battler", "Type3", 107, "TypeId", "not_duration", false, nil],
        ["battler", "Unburden", 108, "boolean", "not_duration", false, nil],
        ["battler", "Uproar", 109, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "WaterSport", 110, "boolean", "not_duration", false, nil],
        ["battler", "WeightChange", 111, "weight_offset", "not_duration", true, nil],
        ["battler", "Yawn", 112, "remaining_phase_counter", "phase_ticks", true, nil],
        ["battler", "FusionSwap", 113, "reserved_unwritten", "unknown", false, nil],
        ["position", "FutureSightCounter", 0, "remaining_phase_counter", "phase_ticks", true, nil],
        ["position", "FutureSightMove", 1, "MoveId", "not_duration", false, "position.FutureSightCounter"],
        ["position", "FutureSightUserIndex", 2, "battler_index", "not_duration", false, "position.FutureSightCounter"],
        ["position", "FutureSightUserPartyIndex", 3, "party_index", "not_duration", false, "position.FutureSightCounter"],
        ["position", "HealingWish", 4, "boolean", "not_duration", false, nil],
        ["position", "LunarDance", 5, "boolean", "not_duration", false, nil],
        ["position", "Wish", 6, "remaining_phase_counter", "phase_ticks", true, nil],
        ["position", "WishAmount", 7, "hp_quantity", "not_duration", true, "position.Wish"],
        ["position", "WishMaker", 8, "party_index", "not_duration", false, "position.Wish"],
        ["side", "AuroraVeil", 0, "remaining_phase_counter", "phase_ticks", true, nil],
        ["side", "CraftyShield", 1, "boolean", "not_duration", false, nil],
        ["side", "EchoedVoiceCounter", 2, "mechanic_count", "not_duration", true, nil],
        ["side", "EchoedVoiceUsed", 3, "boolean", "not_duration", false, nil],
        ["side", "LastRoundFainted", 4, "turn_index", "not_duration", true, nil],
        ["side", "LightScreen", 5, "remaining_phase_counter", "phase_ticks", true, nil],
        ["side", "LuckyChant", 6, "remaining_phase_counter", "phase_ticks", true, nil],
        ["side", "MatBlock", 7, "boolean", "not_duration", false, nil],
        ["side", "Mist", 8, "remaining_phase_counter", "phase_ticks", true, nil],
        ["side", "QuickGuard", 9, "boolean", "not_duration", false, nil],
        ["side", "Rainbow", 10, "remaining_phase_counter", "phase_ticks", true, nil],
        ["side", "Reflect", 11, "remaining_phase_counter", "phase_ticks", true, nil],
        ["side", "Round", 12, "boolean", "not_duration", false, nil],
        ["side", "Safeguard", 13, "remaining_phase_counter", "phase_ticks", true, nil],
        ["side", "SeaOfFire", 14, "remaining_phase_counter", "phase_ticks", true, nil],
        ["side", "Spikes", 15, "hazard_layers", "not_duration", true, nil],
        ["side", "StealthRock", 16, "boolean", "not_duration", false, nil],
        ["side", "StickyWeb", 17, "boolean", "not_duration", false, nil],
        ["side", "Swamp", 18, "remaining_phase_counter", "phase_ticks", true, nil],
        ["side", "Tailwind", 19, "remaining_phase_counter", "phase_ticks", true, nil],
        ["side", "ToxicSpikes", 20, "hazard_layers", "not_duration", true, nil],
        ["side", "WideGuard", 21, "boolean", "not_duration", false, nil],
        ["field", "AmuletCoin", 0, "boolean", "not_duration", false, nil],
        ["field", "FairyLock", 1, "remaining_phase_counter", "phase_ticks", true, nil],
        ["field", "FusionBolt", 2, "boolean", "not_duration", false, nil],
        ["field", "FusionFlare", 3, "boolean", "not_duration", false, nil],
        ["field", "Gravity", 4, "remaining_phase_counter", "phase_ticks", true, nil],
        ["field", "HappyHour", 5, "boolean", "not_duration", false, nil],
        ["field", "IonDeluge", 6, "boolean", "not_duration", false, nil],
        ["field", "MagicRoom", 7, "remaining_phase_counter", "phase_ticks", true, nil],
        ["field", "MudSportField", 8, "remaining_phase_counter", "phase_ticks", true, nil],
        ["field", "PayDay", 9, "currency_pot", "not_duration", true, nil],
        ["field", "TrickRoom", 10, "remaining_phase_counter", "phase_ticks", true, nil],
        ["field", "WaterSportField", 11, "remaining_phase_counter", "phase_ticks", true, nil],
        ["field", "WonderRoom", 12, "remaining_phase_counter", "phase_ticks", true, nil],
      ].freeze
    end
  end
end

module Ironmon
  module GameplayCollection
    module Combat
      def self.effect_registry
        return @effect_registry if @effect_registry
        @effect_registry = {}
        EFFECT_DECLARATIONS.each do |scope, symbol, index, kind, duration, maxima, governing|
          next if !PBEffects.const_defined?(symbol, false)
          actual = PBEffects.const_get(symbol)
          @effect_registry[[scope, actual]] = {
            "registry_revision" => EFFECT_METADATA_REVISION, "scope" => scope,
            "symbol" => symbol, "native_index" => actual,
            "value_kind" => actual == index ? kind : "unsupported",
            "semantic_role" => actual != index ? "unsupported" : (scope == "battler" && VOLATILE_MECHANICS.include?(symbol) ? "volatile_mechanic" : ((FIELD_MECHANICS[scope] || []).include?(symbol) ? "field_mechanic" : "engine_state")),
            "duration_unit" => actual == index ? duration : "unknown",
            "maxima_eligible" => actual == index && maxima,
            "governing_effect_key" => governing
          }
        end
        return @effect_registry
      end

      def self.observe_array(array, battle, scope, subject)
        return if !array.is_a?(Array)
        array.instance_variable_set(:@ironmon_collection_battle, battle)
        array.instance_variable_set(:@ironmon_collection_scope, scope)
        array.instance_variable_set(:@ironmon_collection_subject, subject)
        install(array.singleton_class, IronmonCollectionEffectArrayHooks)
      end

      def self.observe_stages(stages, battler)
        return if !stages.is_a?(Hash)
        stages.instance_variable_set(:@ironmon_collection_battler, battler)
        install(stages.singleton_class, IronmonCollectionStageHashHooks)
      end

      def self.effect_subject(scope, subject)
        return actor_ref(subject) if scope == "battler"
        return { "side_id" => subject } if scope == "side"
        return { "position_id" => subject } if scope == "position"
        return { "field_id" => "battle" }
      end

      def self.effect_value(raw, metadata, battle)
        kind = metadata["value_kind"]
        if ["battler_index", "position_index", "party_index", "side_index"].include?(kind)
          actor = kind == "battler_index" && raw.is_a?(Integer) && raw >= 0 ? read(battle, :battlers, [])[raw] : nil
          return { "kind" => kind, "native_value" => raw, "actor" => actor_ref(actor), "coverage" => "known" }
        end
        if kind == "PokemonInstanceRef"
          return { "kind" => kind, "instance_id" => raw ? instance_id(raw) : nil, "coverage" => "engine_private" }
        end
        return { "kind" => kind, "value" => value(raw), "coverage" => kind == "unsupported" ? "unsupported" : "known" }
      end

      def self.effect_active?(raw, metadata)
        return raw == true if metadata["value_kind"] == "boolean"
        return raw.is_a?(Integer) && raw >= 0 if ["battler_index", "position_index", "party_index", "side_index"].include?(metadata["value_kind"])
        return raw > 0 if raw.is_a?(Numeric)
        return !raw.nil?
      end

      def self.effect_changed(battle, scope, subject, index, before, after)
        return if before == after || !active_battle?(battle)
        local = GameplayCollection.current_context
        return if local["combat_initialization"]
        metadata = effect_registry[[scope, index]] || {
          "registry_revision" => EFFECT_METADATA_REVISION, "scope" => scope, "symbol" => nil,
          "native_index" => index, "value_kind" => "unsupported", "duration_unit" => "unknown",
          "semantic_role" => "unsupported",
          "maxima_eligible" => false, "governing_effect_key" => nil
        }
        metadata = metadata.dup
        key = metadata["symbol"] || "native_#{index}"
        before_active = effect_active?(before, metadata)
        after_active = effect_active?(after, metadata)
        governing = metadata["governing_effect_key"]
        if governing
          governing_scope, governing_name = governing.split(".", 2)
          owner = scope == "position" ? read(battle, :positions, [])[subject] : (scope == "side" ? read(battle, :sides, [])[subject] : subject)
          if governing_scope == scope && PBEffects.const_defined?(governing_name, false)
            governing_value = read(owner, :effects, [])[PBEffects.const_get(governing_name)]
            before_active &&= governing_value.is_a?(Numeric) && governing_value > 0
            after_active &&= governing_value.is_a?(Numeric) && governing_value > 0
          end
        end
        operation = if !after_active && before_active
          local["phase"] == "end_of_round" ? "expired" : "cleared"
        elsif after_active && !before_active
          "applied"
        elsif before.is_a?(Numeric) && after.is_a?(Numeric)
          after > before ? "incremented" : "decremented"
        else
          "other"
        end
        metadata["active"] = after_active
        provenance_key = effect_provenance_key(scope, subject, index)
        if after_active && ["applied", "incremented"].include?(operation) && !local["combat_cleanup"]
          state(battle)[:effect_provenance][provenance_key] = cause
        elsif !after_active
          state(battle)[:retired_effect_provenance] ||= {}
          state(battle)[:retired_effect_provenance][provenance_key] = state(battle)[:effect_provenance][provenance_key] if state(battle)[:effect_provenance][provenance_key]
          state(battle)[:effect_provenance].delete(provenance_key)
        end
        emit("field.effect_changed", {
          "effect_key" => { "namespace" => scope, "effect_id" => key, "subject" => effect_subject(scope, subject) },
          "before" => effect_value(before, metadata, battle), "after" => effect_value(after, metadata, battle),
          "operation" => operation, "duration_unit" => metadata["duration_unit"],
          "cause" => cause, "effect_metadata" => metadata,
          "lifecycle" => local["combat_cleanup"] ? "cleanup" : "active"
        }, battle)
        return if scope != "battler"
        if key == "Substitute"
          previous = before.to_i
          following = after.to_i
          round_damage(battle, actor_ref(subject), [previous - following, 0].max, cause) if respond_to?(:round_damage)
          emit("substitute.changed", {
            "target" => actor_ref(subject),
            "operation" => following > previous ? "created" : (following == 0 ? (local["hit_ref"] ? "broken" : "cleared") : "damaged"),
            "substitute_hp_before" => previous, "substitute_hp_after" => following,
            "actual_substitute_hp_lost" => [previous - following, 0].max,
            "owner_cost_ref" => nil, "cause" => cause
          }, battle)
        end
        return if metadata["semantic_role"] != "volatile_mechanic"
        actor = actor_ref(subject)
        return if !actor
        episodes = state(battle)[:effect_episodes]
        episode_key = [actor["actor_id"], key]
        if after_active && !before_active
          episodes[episode_key] = { :id => GameplayCollection.next_id("effect_episode"), :cause => cause }
        end
        episode = episodes[episode_key]
        retained = state(battle)[:effect_provenance][provenance_key]
        retained["effect_episode_id"] = episode[:id] if retained && episode
        common = {
          "target" => actor, "effect_source" => cause, "effect_id" => key,
          "effect_episode_id" => episode && episode[:id],
          "value_before" => value(before), "value_after" => value(after)
        }
        if after_active && !before_active && !local["combat_cleanup"]
          native_kind = metadata["value_kind"]
          kind = if native_kind == "boolean"
            "boolean"
          elsif ["battler_index", "position_index", "party_index", "side_index", "PokemonInstanceRef"].include?(native_kind)
            "actor_reference"
          elsif native_kind == "MoveId"
            "move_reference"
          elsif after.is_a?(Numeric)
            "counter"
          else
            "other"
          end
          duration = { "actions" => "actions", "rounds" => "turns", "not_duration" => "none" }[metadata["duration_unit"]] || "unknown"
          emit("volatile.application_resolved", common.merge(
            "source_actor" => local["source_actor"], "value_kind" => kind,
            "duration_unit" => duration, "application_outcome" => "applied",
            "native_value_kind" => native_kind, "native_duration_unit" => metadata["duration_unit"]
          ), battle)
        elsif !after_active && before_active
          removal = local["combat_cleanup"] ? "switch_cleanup" : (operation == "expired" ? "natural_expiry" : "other")
          emit("volatile.removal_resolved", common.merge(
            "removal_kind" => removal, "preventer_id" => nil, "outcome" => "success"
          ), battle)
          episodes.delete(episode_key)
        elsif after_active && before_active
          emit("volatile.progressed", common.merge(
            "progress_kind" => operation == "decremented" ? "countdown" : "other", "consequence_event_id" => nil
          ), battle)
        end
        if key == "GastroAcid"
          emit("ability.suppression_changed", {
            "target" => actor, "effect_source" => cause, "ability_id" => native_id(read(subject, :ability_id)),
            "active_before" => read(subject, :hp, 0) > 0 && before != true, "active_after" => read(subject, :abilityActive?),
            "suppression_reason" => after == true ? "gastro_acid" : "mechanic_restore", "ignore_fainted_context" => false
          }, battle)
        end
      end

      def self.stage_changed(battler, stat, before, after)
        return if before == after || !active_battle?(battler.battle) || !before.is_a?(Numeric) || !after.is_a?(Numeric)
        local = GameplayCollection.current_context
        return if local["combat_initialization"]
        delta = after - before
        emit("stat_stage.changed", {
          "target" => actor_ref(battler), "stat_id" => native_id(stat),
          "before" => before, "after" => after, "delta" => delta,
          "change_kind" => local["combat_cleanup"] ? "reset" : (delta > 0 ? "raise" : "lower"),
          "cause" => cause
        }, battler.battle)
      end

      def self.identity_changed(battler, before, kind)
        return if !active_battle?(battler.battle)
        after = identity_snapshot(battler)
        return if before == after
        emit("battler.identity_changed", {
          "actor" => actor_ref(battler), "change_kind" => kind,
          "before" => before, "after" => after, "cause" => cause
        }, battler.battle)
      end
    end
  end
end

module IronmonCollectionEffectArrayHooks
  def []=(*args)
    battle = @ironmon_collection_battle
    combat = Ironmon::GameplayCollection::Combat
    return super if !combat.active_battle?(battle)
    if args.length == 2 && args[0].is_a?(Integer)
      index = args[0] < 0 ? length + args[0] : args[0]
      before = self[index]
      result = super
      combat.effect_changed(battle, @ironmon_collection_scope, @ironmon_collection_subject, index, before, self[index])
    else
      before = dup
      result = super
      [before.length, length].max.times do |index|
        combat.effect_changed(battle, @ironmon_collection_scope, @ironmon_collection_subject, index, before[index], self[index])
      end
    end
    return result
  end

  def replace(other)
    before = dup
    result = super
    combat = Ironmon::GameplayCollection::Combat
    if combat.active_battle?(@ironmon_collection_battle)
      [before.length, length].max.times do |index|
        combat.effect_changed(@ironmon_collection_battle, @ironmon_collection_scope, @ironmon_collection_subject, index, before[index], self[index])
      end
    end
    return result
  end

  def clear
    return replace([])
  end
end

module IronmonCollectionStageHashHooks
  def []=(stat, amount)
    before = self[stat]
    result = super
    Ironmon::GameplayCollection::Combat.stage_changed(@ironmon_collection_battler, stat, before, self[stat])
    return result
  end

  def replace(other)
    before = dup
    result = super
    (before.keys | keys).each do |stat|
      Ironmon::GameplayCollection::Combat.stage_changed(@ironmon_collection_battler, stat, before[stat], self[stat])
    end
    return result
  end
end

module IronmonCollectionBattlerEffectHooks
  def pbInitEffects(*args)
    combat = Ironmon::GameplayCollection::Combat
    result = combat.with_context({ "combat_cleanup" => true }, @battle) { super }
    combat.observe_array(@effects, @battle, "battler", self)
    combat.observe_stages(@stages, self)
    return result
  end

  def effects=(array)
    before = @effects ? @effects.dup : []
    result = super
    combat = Ironmon::GameplayCollection::Combat
    combat.observe_array(array, @battle, "battler", self)
    [before.length, array.length].max.times do |index|
      combat.effect_changed(@battle, "battler", self, index, before[index], array[index])
    end
    return result
  end

  def stages=(stages)
    before = @stages ? @stages.dup : {}
    result = super
    combat = Ironmon::GameplayCollection::Combat
    combat.observe_stages(stages, self)
    (before.keys | stages.keys).each { |stat| combat.stage_changed(self, stat, before[stat], stages[stat]) }
    return result
  end
end

[:pbRaiseStatStageBasic, :pbLowerStatStageBasic].each do |method_name|
  IronmonCollectionBattlerEffectHooks.define_method(method_name) do |stat, amount, *args|
    combat = Ironmon::GameplayCollection::Combat
    return super(stat, amount, *args) if !combat.active_battle?(@battle)
    depth = @ironmon_collection_stage_request_depth.to_i
    @ironmon_collection_stage_request_depth = depth + 1
    before = @stages[stat]
    begin
      result = super(stat, amount, *args)
      if depth == 0
        delta = @stages[stat] - before
        requested = method_name == :pbRaiseStatStageBasic ? amount : -amount
        combat.emit("stat_stage.attempt_resolved", {
          "actor" => Ironmon::GameplayCollection.current_context["source_actor"] || { "coverage" => "unavailable", "reason" => "actorless_native_effect" },
          "target" => combat.actor_ref(self), "stat_id" => combat.native_id(stat),
          "requested_delta" => requested,
          "outcome" => delta == 0 ? "at_cap" : (delta * requested < 0 ? "reversed" : "changed"),
          "cause" => combat.cause, "request_id" => Ironmon::GameplayCollection.next_id("stat_request"),
          "prevention" => nil
        }, @battle)
      end
      result
    ensure
      @ironmon_collection_stage_request_depth = depth
    end
  end
end

[:pbChangeTypes, :pbResetForm, :pbTransform, :pbChangeForm].each do |method_name|
  next if !PokeBattle_Battler.method_defined?(method_name)
  IronmonCollectionBattlerEffectHooks.define_method(method_name) do |*args, &block|
    combat = Ironmon::GameplayCollection::Combat
    before = combat.active_battle?(@battle) ? combat.identity_snapshot(self) : nil
    result = super(*args, &block)
    combat.identity_changed(self, before, method_name == :pbChangeTypes ? "type" : "other") if before
    result
  end
end

Ironmon::GameplayCollection::Combat.install(PokeBattle_Battler, IronmonCollectionBattlerEffectHooks)
