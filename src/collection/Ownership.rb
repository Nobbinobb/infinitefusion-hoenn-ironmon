module Ironmon
  module GameplayCollection
    def self.owned_actor_ref(pokemon)
      return nil if !pokemon
      identifier = pokemon_instance_id(pokemon)
      return {
        "pokemon_instance_id" => identifier, "party_instance_id" => identifier,
        "owned_entity_id" => identifier, "owner_role" => "player", "side" => "player",
        "usable_runner" => Ironmon.respond_to?(:usable_party) && Ironmon.usable_party[0].equal?(pokemon)
      }
    end

    module Ownership
      STAT_KEYS = {
        :HP => "hp", :ATTACK => "attack", :DEFENSE => "defense",
        :SPECIAL_ATTACK => "special_attack", :SPECIAL_DEFENSE => "special_defense", :SPEED => "speed"
      }
      COMPUTED_READERS = {
        "maximum_hp" => :totalhp, "attack" => :attack, "defense" => :defense,
        "special_attack" => :spatk, "special_defense" => :spdef, "speed" => :speed
      }

      def self.active?
        return World.collecting? && $Trainer
      end

      def self.observe
        return if !active?
        return yield
      rescue StandardError => error
        GameplayCollection.fail_collection(error)
        return nil
      end

      def self.owned?(pokemon)
        return $Trainer && $Trainer.party.any? { |entry| entry.equal?(pokemon) }
      end

      def self.usable?(pokemon)
        return Ironmon.respond_to?(:usable_party) && Ironmon.usable_party[0].equal?(pokemon)
      end

      def self.vector(values)
        return nil if !values.is_a?(Hash)
        result = {}
        STAT_KEYS.each { |key, name| result[name] = values[key] }
        return result
      end

      def self.species_ref(pokemon)
        return nil if !pokemon
        return {
          "canonical_id" => Combat.native_id(pokemon.species),
          "canonical_species_id" => Combat.native_id(pokemon.species),
          "species_id" => Combat.native_id(pokemon.species), "form_id" => Combat.read(pokemon, :form),
          "fusion_body_id" => Combat.read(pokemon, :isFusion?, false) ? Combat.native_id(Combat.read(pokemon, :get_body_species)) : nil,
          "fusion_head_id" => Combat.read(pokemon, :isFusion?, false) ? Combat.native_id(Combat.read(pokemon, :get_head_species)) : nil,
          "size_category" => nil, "gender" => Combat.read(pokemon, :gender)
        }
      end

      def self.moves(pokemon)
        return pokemon.moves.each_with_index.map do |move, slot|
          {
            "slot" => slot, "move_id" => GameplayCollection.catalogue_ref("move", move.id),
            "max_pp" => Combat.read(move, :total_pp), "pp_up_count" => Combat.read(move, :ppup),
            "learning_source" => nil, "learning_level" => nil
          }
        end
      end

      def self.shiny_traits(pokemon)
        return {
          "shiny" => Combat.read(pokemon, :shiny?), "body_shiny" => Combat.read(pokemon, :body_shiny),
          "head_shiny" => Combat.read(pokemon, :head_shiny), "natural_shiny" => Combat.read(pokemon, :natural_shiny),
          "radar_shiny" => Combat.read(pokemon, :radar_shiny), "debug_shiny" => Combat.read(pokemon, :debug_shiny)
        }
      end

      def self.role(pokemon)
        return "egg" if Combat.read(pokemon, :egg?, false)
        return "runner" if usable?(pokemon)
        return "utility" if Ironmon.respond_to?(:party_excluded_pokemon?) && Ironmon.party_excluded_pokemon?(pokemon)
        return "other_retained_team"
      end

      def self.snapshot(pokemon, capture_kind = "configuration_change", reasons = ["other"], acquisition = nil)
        identifier = GameplayCollection.pokemon_instance_id(pokemon)
        generated = vector(Combat.read(pokemon, :baseStats, {}))
        reference = if Ironmon.respond_to?(:original_base_stats_for_pokemon)
          vector(Ironmon.original_base_stats_for_pokemon(pokemon))
        elsif Ironmon.respond_to?(:original_base_stats_for)
          vector(Ironmon.original_base_stats_for(pokemon.species_data))
        elsif pokemon.species_data.respond_to?(:ironmon_unrandomized_base_stats)
          vector(pokemon.species_data.ironmon_unrandomized_base_stats)
        end
        computed = {}
        COMPUTED_READERS.each { |name, reader| computed[name] = Combat.read(pokemon, reader) }
        current_pp = {}
        pokemon.moves.each_with_index { |move, slot| current_pp[slot.to_s] = Combat.read(move, :pp) }
        trait_values = Combat.read(pokemon, :iv)
        known_fields = ["species_identity", "level", "experience", "generated_base_stats", "computed_stats", "moves", "held_item"]
        known_fields << "training_traits.iv" if trait_values.is_a?(Hash)
        configuration = {
          "owned_entity_id" => identifier, "pokemon_ref" => identifier,
          "configuration_revision" => (@revisions || {}).fetch(identifier, 0) + 1,
          "capture_kind" => capture_kind, "change_reasons" => reasons, "ownership_role" => role(pokemon),
          "team_commit_context" => {
            "outer_transaction_id" => GameplayCollection.current_context["ownership_transaction_id"],
            "retained_after_commit" => owned?(pokemon), "final_team_role" => role(pokemon), "previous_team_entity_id" => nil
          },
          "favorite_set_revision" => @favorite_revision,
          "species_identity" => species_ref(pokemon),
          "display_metadata" => { "nickname" => Combat.read(pokemon, :name), "sprite_variant_ref" => nil, "sprite_catalogue_version" => nil },
          "acquisition" => acquisition || {
            "acquisition_id" => nil, "source_kind" => capture_kind == "first_observed_baseline" ? "baseline" : "unknown",
            "map_id" => Combat.read(pokemon, :obtain_map), "obtained_level" => Combat.read(pokemon, :obtain_level),
            "ball_id" => GameplayCollection.catalogue_ref("item", Combat.read(pokemon, :poke_ball)),
            "acquisition_coverage" => capture_kind == "first_observed_baseline" ? "baseline_only" : "partial"
          },
          "lineage" => pokemon.instance_variable_get(:@ironmon_collection_lineage) || { "parent_entity_ids" => [], "continuation_of" => nil, "transformation_kind" => nil, "lineage_transaction_id" => nil },
          "level" => Combat.read(pokemon, :level), "experience" => Combat.read(pokemon, :exp),
          "generated_base_stats" => generated, "reference_base_stats" => reference,
          "computed_stats" => computed,
          "training_traits" => {
            "iv" => vector(trait_values), "effective_iv" => vector(Combat.read(pokemon, :calcIV)),
            "iv_evidence" => trait_values.is_a?(Hash) ? "direct_game_values" : "unknown",
            "ev" => vector(Combat.read(pokemon, :ev)), "hyper_training" => vector(Combat.read(pokemon, :ivMaxed)),
            "nature_id" => Combat.native_id(Combat.read(pokemon, :nature)), "effective_nature_id" => Combat.native_id(Combat.read(pokemon, :nature_for_stats)),
            "friendship" => Combat.read(pokemon, :happiness), "pokerus" => Combat.read(pokemon, :pokerus)
          },
          "persistent_ability" => {
            "ability_id" => GameplayCollection.catalogue_ref("ability", Combat.read(pokemon, :ability_id)),
            "slot_id" => Combat.read(pokemon, :ability_index)
          },
          "owned_types" => Combat.value(Combat.read(pokemon, :types, [])), "moves" => moves(pokemon),
          "held_item" => { "item_id" => GameplayCollection.catalogue_ref("item", Combat.read(pokemon, :item_id)), "origin_ref" => nil, "origin_known" => false },
          "shiny_traits" => shiny_traits(pokemon),
          "checkpoint_resources" => { "current_hp" => Combat.read(pokemon, :hp), "major_status" => Combat.native_id(Combat.read(pokemon, :status)), "current_pp_by_slot" => current_pp },
          "progress_context" => {
            "badge_count" => Combat.read($Trainer, :badge_count, 0), "area_id" => World.area_id,
            "attempt_order" => Ironmon.current_attempt_number, "active_play_seconds" => World.instance_variable_get(:@attempt_seconds)
          },
          "data_versions" => {
            "game_data_version" => defined?(Settings::GAME_VERSION) ? Settings::GAME_VERSION.to_s : "runtime-current",
            "generation_profile" => Ironmon.current_run_attempt ? Ironmon.current_run_attempt["generation_profile_id"] : nil,
            "generator_version" => nil, "source_fingerprint" => nil, "stat_rules_version" => "native-generated-stat-1"
          },
          "coverage" => {
            "tracking_epoch_id" => GameplayCollection.journal.epoch_id, "first_observed" => !(@records || {}).key?(identifier),
            "known_fields" => known_fields, "history_complete_since_acquisition" => capture_kind != "first_observed_baseline"
          },
          "retirement" => { "retired" => false, "reason" => nil, "terminal_attempt_outcome" => nil }
        }
        return configuration
      end

      def self.signature(record)
        current = record.reject { |key, _value| ["configuration_revision", "capture_kind", "change_reasons", "team_commit_context", "acquisition", "coverage", "checkpoint_resources", "progress_context", "experience"].include?(key) }
        return Digest::SHA256.hexdigest(GameplayCollection.encode(current))
      end

      def self.commit(pokemon, kind = "configuration_change", reasons = ["other"], acquisition = nil, force = false)
        return if !owned?(pokemon)
        @records ||= {}
        @revisions ||= {}
        record = snapshot(pokemon, kind, reasons, acquisition)
        identifier = record["owned_entity_id"]
        previous = @records[identifier]
        digest = signature(record)
        return if !force && previous && previous[:signature] == digest
        record["display_metadata"]["sprite_path"] = archived_sprite_path(pokemon)
        record["history_context"] = { "branch_id" => GameplayCollection.branch_id, "saved_parent" => @history_parent, "ancestry_known" => @history_ancestry_known != false }
        record["configuration_revision"] = @revisions.fetch(identifier, 0) + 1
        GameplayCollection.emit("pokemon.owned_configuration_committed", record)
        @revisions[identifier] = record["configuration_revision"]
        @records[identifier] = { :signature => digest, :record => record }
        return record
      end

      def self.archived_sprite_path(pokemon)
        sprite = Combat.read(pokemon, :pif_sprite)
        return nil if !sprite
        local = BattleSpriteLoader.new.check_for_local_sprite(sprite)
        return local.tr("\\", "/") if local && File.file?(local)
        return Ironmon.tracker_materialized_sprite_path(sprite) if
          Ironmon.respond_to?(:tracker_materialized_sprite_path)
        return nil
      rescue StandardError
        return nil
      end

      def self.baseline(reason = "load")
        observe do
          @records = {}
          @revisions = {}
          @runner = nil
          update_favorites
          $Trainer.party.each { |pokemon| commit(pokemon, "first_observed_baseline", [reason], nil, true) }
          runner = Ironmon.respond_to?(:usable_party) ? Ironmon.usable_party[0] : nil
          @runner = runner ? { "id" => GameplayCollection.pokemon_instance_id(runner), "species" => species_ref(runner) } : nil
        end
      end

      def self.start_history_branch(new_attempt = false)
        checkpoint = $PokemonGlobal && $PokemonGlobal.instance_variable_get(:@ironmon_collection_history_checkpoint)
        @history_parent = if checkpoint.is_a?(Hash) && checkpoint["epoch_id"] == GameplayCollection.journal.epoch_id && checkpoint["attempt_id"] == GameplayCollection.attempt_id
                            checkpoint.dup
                          end
        runner = Ironmon.respond_to?(:usable_party) ? Ironmon.usable_party[0] : nil
        @history_ancestry_known = new_attempt || !@history_parent.nil? || runner.nil?
      end

      def self.prepare_history_save
        return nil if !GameplayCollection.enabled? || !$PokemonGlobal
        poll("save", false, true)
        previous = $PokemonGlobal.instance_variable_get(:@ironmon_collection_history_checkpoint)
        checkpoint = {
          "epoch_id" => GameplayCollection.journal.epoch_id, "attempt_id" => GameplayCollection.attempt_id,
          "branch_id" => GameplayCollection.branch_id, "stream_id" => GameplayCollection.journal.stream_id,
          "sequence" => GameplayCollection.journal.sequence.to_s
        }
        $PokemonGlobal.instance_variable_set(:@ironmon_collection_history_checkpoint, checkpoint)
        return previous
      end

      def self.poll(reason = "other", terminal = false, force = false)
        return if @transaction_depth.to_i > 0
        observe do
          update_favorites
          @records ||= {}
          present = {}
          $Trainer.party.each do |pokemon|
            identifier = GameplayCollection.pokemon_instance_id(pokemon)
            present[identifier] = true
            commit(pokemon, terminal ? "terminal" : "configuration_change", [reason], nil, terminal || force)
          end
          @records.each do |identifier, previous|
            next if present[identifier] || previous[:retired]
            retirement = previous[:record].merge(
              "configuration_revision" => @revisions.fetch(identifier, 0) + 1, "capture_kind" => "runner_exit",
              "change_reasons" => ["retirement"], "retirement" => { "retired" => true, "reason" => reason, "terminal_attempt_outcome" => nil }
            )
            GameplayCollection.emit("pokemon.owned_configuration_committed", retirement)
            @revisions[identifier] = retirement["configuration_revision"]
            previous[:retired] = true
          end
          runner_changed(reason)
        end
      end

      def self.runner_changed(reason, cause_ref = nil)
        pokemon = Ironmon.respond_to?(:usable_party) ? Ironmon.usable_party[0] : nil
        next_runner = pokemon ? { "id" => GameplayCollection.pokemon_instance_id(pokemon), "species" => species_ref(pokemon) } : nil
        return if @runner == next_runner
        GameplayCollection.emit("pokemon.runner_changed", {
          "runner_before_ref" => @runner && @runner["id"], "runner_after_ref" => next_runner && next_runner["id"],
          "species_before_ref" => @runner && @runner["species"], "species_after_ref" => next_runner && next_runner["species"],
          "cause_kind" => reason, "cause_ref" => cause_ref, "favorite_set_revision" => @favorite_revision,
          "favorite_component_ids" => @favorites
        })
        @runner = next_runner
      end

      def self.update_favorites
        connection = Ironmon.tracker_connection
        native_favorites = connection.respond_to?(:favorite_species_ids) ? connection.favorite_species_ids.map(&:to_s).sort : []
        favorites = native_favorites.map { |value| value.split(":", 2)[0] }.uniq.sort
        return if @favorites == favorites
        @favorites = favorites
        @favorite_revision = GameplayCollection.next_id("favorite_set")
        GameplayCollection.emit("pokemon.favorite_selection_changed", {
          "favorite_set_revision" => @favorite_revision, "operation" => "replace", "normal_species_ids" => favorites,
          "matching_policy_revision" => "normal-body-head-1", "persisted" => false,
          "game_sync_revision" => nil, "effective" => true, "native_favorite_species_refs" => native_favorites
        })
      end

      def self.disclosed(pokemon, kind, reference, knowledge = ["species", "shiny"])
        observe do
          GameplayCollection.emit("pokemon.observation_disclosed", {
            "pokemon_ref" => GameplayCollection.pokemon_instance_id(pokemon), "species_ref" => species_ref(pokemon),
            "observation_kind" => kind, "observation_ref" => reference,
            "shiny_display" => shiny_traits(pokemon), "knowledge_mask" => knowledge,
            "map_context" => World.world_context, "favorite_set_revision" => @favorite_revision
          })
        end
      end

      def self.with_transaction
        @transaction_depth = @transaction_depth.to_i + 1
        return yield
      ensure
        @transaction_depth -= 1
      end

      def self.boundary
        return [GameplayCollection.attempt_id, GameplayCollection.branch_id]
      end

      def self.same_boundary?(value)
        return boundary == value
      end

      def self.with_owned_mutation(pokemon, reason)
        return yield if !active? || !owned?(pokemon)
        depth = pokemon.instance_variable_get(:@ironmon_collection_mutation_depth).to_i
        before = nil
        observe { before = scalar_state(pokemon) } if depth == 0
        pokemon.instance_variable_set(:@ironmon_collection_mutation_depth, depth + 1)
        completed = false
        begin
          result = yield
          completed = true
          return result
        ensure
          pokemon.instance_variable_set(:@ironmon_collection_mutation_depth, depth)
          changed(pokemon, before, reason) if completed && before
        end
      end

      def self.assign_retained_identity(pending, result_pokemon, action, before_runner)
        candidate = pending[:candidate]
        transformed = ["fuse", "reverse_and_take", "swap_and_reverse", "unfuse_and_take", "swap_and_unfuse"].include?(action.to_s)
        return if !transformed || result_pokemon.equal?(candidate) || result_pokemon.equal?(before_runner)
        # Native Pokemon#clone copies custom ivars. A new transformed individual
        # must not inherit its parent's entity ID and create self-lineage edges.
        result_pokemon.instance_variable_set(:@ironmon_collection_epoch_id, nil)
        result_pokemon.instance_variable_set(:@ironmon_collection_instance_id, nil)
        GameplayCollection.pokemon_instance_id(result_pokemon)
      end

      def self.changed(pokemon, before, reason)
        return if !active? || !owned?(pokemon) || @transaction_depth.to_i > 0
        observe do
          after = scalar_state(pokemon)
          identifier = GameplayCollection.pokemon_instance_id(pokemon)
          local = GameplayCollection.current_context
          if before["species"] != after["species"] || before["nickname"] != after["nickname"] || before["owner"] != after["owner"] || before["shiny"] != after["shiny"]
            GameplayCollection.emit("pokemon.identity_changed", {
              "pokemon_ref" => identifier, "change_kind" => reason,
              "identity_before_ref" => Digest::SHA256.hexdigest(GameplayCollection.encode(before["species"])),
              "identity_after_ref" => Digest::SHA256.hexdigest(GameplayCollection.encode(after["species"])),
              "identity_before" => { "species" => before["species"], "nickname" => before["nickname"], "owner" => before["owner"], "shiny" => before["shiny"] },
              "identity_after" => { "species" => after["species"], "nickname" => after["nickname"], "owner" => after["owner"], "shiny" => after["shiny"] },
              "owner_relation_before" => "player_owned", "owner_relation_after" => "player_owned",
              "cause_kind" => reason, "cause_ref" => local["effect_group_id"]
            })
          end
          if before["experience"] != after["experience"]
            GameplayCollection.emit("pokemon.experience_changed", {
              "pokemon_ref" => identifier, "exp_before" => before["experience"], "exp_after" => after["experience"],
              "actual_delta" => after["experience"] - before["experience"], "nominal_delta" => nil,
              "source_kind" => local["source_kind"] || "unknown", "growth_rate_before" => before["growth_rate"],
              "growth_rate_after" => after["growth_rate"], "source_outcome_ref" => local["battle_instance_id"], "usable_runner" => usable?(pokemon)
            })
          end
          if before["level"] != after["level"]
            GameplayCollection.emit("pokemon.level_changed", {
              "pokemon_ref" => identifier, "level_before" => before["level"], "level_after" => after["level"],
              "levels_gained" => [after["level"] - before["level"], 0].max, "cause_kind" => reason,
              "cause_ref" => local["effect_group_id"], "usable_runner" => usable?(pokemon)
            })
          end
          if before["form"] != after["form"]
            GameplayCollection.emit("pokemon.form_changed", {
              "pokemon_ref" => identifier, "species_id" => Combat.native_id(pokemon.species),
              "form_before" => before["form"], "form_after" => after["form"], "persistence" => "persistent",
              "cause_kind" => reason, "cause_ref" => local["effect_group_id"], "knowledge_scope" => "player_owned"
            })
          end
          if before["ability"] != after["ability"] || before["ability_slot"] != after["ability_slot"]
            GameplayCollection.emit("pokemon.ability_changed", {
              "pokemon_ref" => identifier, "ability_before" => before["ability"], "ability_after" => after["ability"],
              "ability_slot_before" => before["ability_slot"], "ability_slot_after" => after["ability_slot"],
              "cause_kind" => reason, "cause_ref" => local["effect_group_id"]
            })
          end
          if before["moves"] != after["moves"]
            GameplayCollection.emit("pokemon.moveset_changed", {
              "pokemon_ref" => identifier, "moves_before" => before["moves"], "moves_after" => after["moves"],
              "change_kind" => reason, "learning_ref" => local["learning_id"], "cause_ref" => local["effect_group_id"]
            })
          end
          if before["stats"] != after["stats"] || before["base_stats"] != after["base_stats"]
            GameplayCollection.emit("pokemon.stats_changed", {
              "pokemon_ref" => identifier, "species_ref" => species_ref(pokemon),
              "base_stats_before" => before["base_stats"], "base_stats_after" => after["base_stats"],
              "computed_stats_before" => before["stats"], "computed_stats_after" => after["stats"],
              "cause_kind" => reason, "cause_ref" => local["effect_group_id"]
            })
          end
          ["iv", "ev", "hyper_training", "nature", "friendship", "pokerus"].each do |attribute|
            next if before[attribute] == after[attribute]
            GameplayCollection.emit("pokemon.training_attributes_changed", {
              "pokemon_ref" => identifier, "attribute_family" => attribute,
              "before" => before[attribute], "after" => after[attribute], "effective_delta" => nil,
              "source_kind" => local["source_kind"] || "unknown", "cause_ref" => local["effect_group_id"]
            })
          end
          commit(pokemon, "configuration_change", [reason])
        end
      end

      def self.scalar_state(pokemon)
        stats = {}
        COMPUTED_READERS.each { |name, reader| stats[name] = Combat.read(pokemon, reader) }
        return {
          "experience" => Combat.read(pokemon, :exp), "level" => Combat.read(pokemon, :level),
          "growth_rate" => Combat.native_id(Combat.read(pokemon.species_data, :growth_rate)),
          "nickname" => Combat.read(pokemon, :name), "owner" => Combat.native_id(Combat.read(Combat.read(pokemon, :owner), :id)),
          "shiny" => shiny_traits(pokemon),
          "form" => Combat.read(pokemon, :form), "species" => species_ref(pokemon),
          "ability" => Combat.native_id(Combat.read(pokemon, :ability_id)), "ability_slot" => Combat.read(pokemon, :ability_index),
          "moves" => moves(pokemon), "stats" => stats, "base_stats" => vector(Combat.read(pokemon, :baseStats)),
          "iv" => vector(Combat.read(pokemon, :iv)), "ev" => vector(Combat.read(pokemon, :ev)),
          "hyper_training" => vector(Combat.read(pokemon, :ivMaxed)), "nature" => Combat.native_id(Combat.read(pokemon, :nature)),
          "friendship" => Combat.read(pokemon, :happiness), "pokerus" => Combat.read(pokemon, :pokerus)
        }
      end

      def self.acquisition(pending, result_pokemon, action, before_runner)
        observe do
          candidate = pending[:candidate]
          source = pending[:source].to_s
          prior = candidate.instance_variable_get(:@ironmon_collection_starter_acquisition)
          map = World.world_context["map_id"]
          boundary = [GameplayCollection.journal.epoch_id, GameplayCollection.attempt_id]
          if source == "starter" && prior && prior["boundary"] == boundary &&
             prior["map_id"] == 5 && map == 18 &&
             GameplayCollection.pokemon_instance_id(candidate) == GameplayCollection.pokemon_instance_id(result_pokemon) &&
             pbGet(VAR_HOENN_STARTER).equal?(candidate)
            commit(result_pokemon, "configuration_change", ["starter_story_return"], nil, true)
            runner_changed("starter_story_return", GameplayCollection.current_context["ownership_transaction_id"])
            return
          end
          identifier = candidate.instance_variable_get(:@ironmon_collection_capture_acquisition) || GameplayCollection.next_id("acquisition")
          transaction = GameplayCollection.current_context["ownership_transaction_id"]
          GameplayCollection.emit("pokemon.acquisition_resolved", {
            "acquisition_id" => identifier, "source_kind" => source, "source_outcome_ref" => candidate.instance_variable_get(:@ironmon_collection_capture_outcome),
            "candidate_ref" => GameplayCollection.pokemon_instance_id(candidate), "candidate_species_ref" => species_ref(candidate),
            "resolution" => "retained", "exclusion_reason" => nil, "pivot_transaction_ref" => transaction,
            "retained_result_ref" => GameplayCollection.pokemon_instance_id(result_pokemon), "retained_species_ref" => species_ref(result_pokemon)
          })
          transformation = ["fuse", "reverse_and_take", "swap_and_reverse", "unfuse_and_take", "swap_and_unfuse"].include?(action.to_s)
          transformation_id = transformation ? GameplayCollection.next_id("transformation") : nil
          if transformation
            inputs = [before_runner, candidate].compact.uniq
            lineage = {
              "parent_entity_ids" => inputs.map { |pokemon| GameplayCollection.pokemon_instance_id(pokemon) },
              "continuation_of" => before_runner ? GameplayCollection.pokemon_instance_id(before_runner) : nil,
              "transformation_kind" => action.to_s, "lineage_transaction_id" => transaction
            }
            result_pokemon.instance_variable_set(:@ironmon_collection_lineage, lineage)
            GameplayCollection.emit("pokemon.transformation_resolved", {
              "transformation_id" => transformation_id, "kind" => action.to_s, "outcome" => "success",
              "input_pokemon_refs" => lineage["parent_entity_ids"], "input_species_refs" => inputs.map { |pokemon| species_ref(pokemon) },
              "output_pokemon_refs" => [GameplayCollection.pokemon_instance_id(result_pokemon)], "output_species_refs" => [species_ref(result_pokemon)],
              "origin_policy" => { "team_committed_only" => true }, "cause_ref" => transaction
            })
          end
          GameplayCollection.emit("pokemon.pivot_resolved", {
            "pivot_transaction_id" => transaction, "action" => action.to_s, "outcome" => "success",
            "previous_runner_ref" => before_runner ? GameplayCollection.pokemon_instance_id(before_runner) : nil,
            "candidate_ref" => GameplayCollection.pokemon_instance_id(candidate), "result_runner_ref" => GameplayCollection.pokemon_instance_id(result_pokemon),
            "result_species_ref" => species_ref(result_pokemon), "transformation_ref" => transformation_id,
            "returned_items_transaction_ref" => nil, "raw_action" => action.to_s
          }) if before_runner && source != "starter"
          acquisition = {
            "acquisition_id" => identifier, "source_kind" => source,
            "map_id" => World.world_context["map_id"], "obtained_level" => Combat.read(result_pokemon, :obtain_level),
            "ball_id" => GameplayCollection.catalogue_ref("item", Combat.read(result_pokemon, :poke_ball)), "acquisition_coverage" => "known"
          }
          selection = candidate.instance_variable_get(:@ironmon_collection_selected_starter)
          if source == "starter" && selection && selection["boundary"] == boundary
            acquisition["starter_selection"] = {
              "selection_id" => selection["selection_id"], "selected_choice_index" => selection["choice_index"],
              "selection_method" => selection["method"]
            }
          end
          commit(result_pokemon, "acquisition", [source, action.to_s], acquisition, true)
          if source == "starter"
            marker = {
              "boundary" => boundary, "map_id" => map, "acquisition_id" => identifier
            }
            candidate.instance_variable_set(:@ironmon_collection_starter_acquisition, marker)
            result_pokemon.instance_variable_set(:@ironmon_collection_starter_acquisition, marker)
          end
          runner_changed("acquisition", transaction)
          selected_starter(candidate, result_pokemon) if source == "starter"
        end
      end

      def self.track_move_learning(pokemon, move, source_kind)
        return yield if !active? || !owned?(pokemon)
        before = pokemon.moves.map(&:id)
        boundary = self.boundary
        learning = nil
        observe { learning = GameplayCollection.next_id("learning") }
        return yield if !learning
        result = GameplayCollection.with_context({ "learning_id" => learning, "learning_source" => source_kind }) do
          with_transaction { yield }
        end
        return result if !same_boundary?(boundary) || !owned?(pokemon)
        observe do
          after = pokemon.moves.map(&:id)
          move_id = GameplayCollection::Combat.native_id(move)
          slot = after.index { |identifier| identifier.to_s == move_id.to_s }
          local = GameplayCollection.current_context
          accepted = !slot.nil? && before != after
          GameplayCollection.emit("pokemon.move_learning_resolved", {
            "learning_id" => learning, "pokemon_ref" => GameplayCollection.pokemon_instance_id(pokemon), "move_id" => move_id,
            "source_kind" => source_kind,
            "outcome" => accepted ? "learned" : (slot ? "already_known" : "declined"),
            "forgotten_move_id" => (before - after)[0]&.to_s, "slot_index" => slot,
            "cause_ref" => local["effect_group_id"], "offered_to_player" => true, "usable_runner" => usable?(pokemon),
            "learning_context" => {
              "parent_transaction_ref" => local["effect_group_id"], "offered_level" => pokemon.level,
              "source_item_id" => local["source_kind"] == "item" ? local["source_id"] : nil,
              "authored_tutor_ref" => nil, "accepted" => accepted,
              "new_to_current_moveset" => !before.any? { |identifier| identifier.to_s == move_id.to_s }
            }
          })
          GameplayCollection.emit("pokemon.moveset_changed", {
            "pokemon_ref" => GameplayCollection.pokemon_instance_id(pokemon), "moves_before" => before.map(&:to_s),
            "moves_after" => moves(pokemon), "change_kind" => "learn", "learning_ref" => learning, "cause_ref" => local["effect_group_id"]
          }) if accepted
          commit(pokemon, "configuration_change", ["learn"]) if accepted
        end
        return result
      end

      def self.selected_starter(candidate, result_pokemon)
        selection = candidate.instance_variable_get(:@ironmon_collection_selected_starter)
        selection = nil if selection && selection["boundary"] != [GameplayCollection.journal.epoch_id, GameplayCollection.attempt_id]
        species = species_ref(result_pokemon)
        favorites = @favorites || []
        mask = 0
        mask |= 1 if !species["fusion_head_id"] && !species["fusion_body_id"] && favorites.include?(species["canonical_species_id"])
        mask |= 2 if species["fusion_head_id"] && favorites.include?(species["fusion_head_id"])
        mask |= 4 if species["fusion_body_id"] && favorites.include?(species["fusion_body_id"])
        GameplayCollection.emit("pokemon.starter_selection_resolved", {
          "selection_id" => selection && selection["selection_id"],
          "outcome" => "selected", "selected_choice_index" => selection && selection["choice_index"],
          "pokemon_ref" => GameplayCollection.pokemon_instance_id(result_pokemon), "species_ref" => species_ref(result_pokemon),
          "selection_method" => selection && selection["method"],
          "retained_usable_starter" => usable?(result_pokemon), "favorite_match_role_mask" => mask,
          "eligibility_policy_ref" => "current-starter-policy", "favorite_set_revision" => @favorite_revision
        })
      end

      def self.starter_offer(selection, index)
        return if !selection || !selection[:revealed][index]
        observe do
          pokemon = selection[:pokemon][index]
          GameplayCollection.emit("pokemon.starter_offer_disclosed", {
            "selection_id" => selection[:selection_id], "choice_index" => index,
            "pokemon_ref" => GameplayCollection.pokemon_instance_id(pokemon), "species_ref" => species_ref(pokemon),
            "disclosed_fields" => { "species" => true, "bst" => true },
            "eligibility" => { "favorite" => selection[:favorites][index], "ceiling" => selection[:ceiling] },
            "reveal_kind" => selection[:auto_select] ? "automatic" : "player"
          })
          disclosed(pokemon, "starter_offer", selection[:selection_id], ["species"])
        end
      end
    end
  end
end

module IronmonCollectionOwnedMutationHooks
end

module IronmonCollectionStarterChoiceHooks
  def ironmon_tracker_choose_random_or_favorite(*args)
    @ironmon_collection_favorite_choice = true
    super
  ensure
    @ironmon_collection_favorite_choice = false
  end

  def ironmon_tracker_finalize_starter
    chosen = super
    ownership = Ironmon::GameplayCollection::Ownership
    ownership.observe do
      selection = Ironmon.instance_variable_get(:@tracker_starter_selection)
      if selection && chosen
        Ironmon::GameplayCollection.pokemon_instance_id(chosen)
        index = selection[:pokemon].index { |pokemon| pokemon.equal?(chosen) }
        chosen.instance_variable_set(:@ironmon_collection_selected_starter, {
          "boundary" => [Ironmon::GameplayCollection.journal.epoch_id, Ironmon::GameplayCollection.attempt_id],
          "selection_id" => selection[:selection_id], "choice_index" => index,
          "method" => selection[:auto_select] && !@ironmon_collection_favorite_choice ? "automatic" : "player"
        })
      end
    end
    return chosen
  end
end

{
  :exp= => "experience", :level= => "level", :species= => "species", :form= => "form",
  :ability= => "ability", :ability_index= => "ability", :moves= => "moves", :learn_move => "learn",
  :forget_move => "forget", :forget_move_at_index => "forget", :calc_stats => "stats",
  :nature= => "training", :nature_for_stats= => "training", :iv= => "training", :ev= => "training",
  :ivMaxed= => "training", :happiness= => "training", :pokerus= => "training", :name= => "nickname",
  :changeHappiness => "friendship", :givePokerus => "pokerus", :resetPokerusTime => "pokerus", :lowerPokerusCount => "pokerus",
  :owner= => "owner", :gender= => "gender", :shiny= => "shiny", :body_shiny= => "shiny", :head_shiny= => "shiny"
}.each do |method_name, reason|
  next if !Pokemon.method_defined?(method_name)
  IronmonCollectionOwnedMutationHooks.define_method(method_name) do |*args, &block|
    ownership = Ironmon::GameplayCollection::Ownership
    return super(*args, &block) if !ownership.active? || !ownership.owned?(self)
    depth = @ironmon_collection_mutation_depth.to_i
    before = nil
    ownership.observe { before = ownership.scalar_state(self) } if depth == 0
    @ironmon_collection_mutation_depth = depth + 1
    completed = false
    begin
      result = super(*args, &block)
      completed = true
      result
    ensure
      @ironmon_collection_mutation_depth = depth
      ownership.changed(self, before, reason) if completed && before
    end
  end
end

module IronmonCollectionOwnedAcquisitionHooks
  def commit_pending_result(acquisition_id, result_pokemon, action = nil)
    ownership = Ironmon::GameplayCollection::Ownership
    return super if !ownership.active?
    identifier = nil
    ownership.observe { identifier = Ironmon::GameplayCollection.current_context["ownership_transaction_id"] || Ironmon::GameplayCollection.next_id("ownership_transaction") }
    return super if !identifier
    pending = pivot_state.pending_pivot.dup
    previous = usable_party[0]
    boundary = ownership.boundary
    return Ironmon::GameplayCollection.with_context({ "ownership_transaction_id" => identifier }) do
      ownership.with_transaction do
        result = super
        if result && ownership.same_boundary?(boundary)
          ownership.observe { ownership.assign_retained_identity(pending, result_pokemon, action, previous) }
          ownership.acquisition(pending, result_pokemon, action, previous)
        end
        result
      end
    end
  end

  def begin_tracker_starter_selection(pokemon)
    result = super
    selection = @tracker_starter_selection
    pokemon.each_index { |index| Ironmon::GameplayCollection::Ownership.starter_offer(selection, index) } if selection
    return result
  end

  def reveal_tracker_starter(index, *args)
    previous = @tracker_starter_selection && @tracker_starter_selection[:revealed][index]
    result = super
    Ironmon::GameplayCollection::Ownership.starter_offer(@tracker_starter_selection, index) if !previous
    return result
  end

  def record_excluded_acquisition(pokemon, reason, source = nil)
    result = super
    Ironmon::GameplayCollection::Ownership.observe do
      Ironmon::GameplayCollection.emit("pokemon.acquisition_resolved", {
        "acquisition_id" => Ironmon::GameplayCollection.next_id("acquisition"), "source_kind" => (source || current_acquisition_source).to_s,
        "source_outcome_ref" => nil, "candidate_ref" => Ironmon::GameplayCollection.pokemon_instance_id(pokemon),
        "candidate_species_ref" => Ironmon::GameplayCollection::Ownership.species_ref(pokemon), "resolution" => "excluded",
        "exclusion_reason" => reason.to_s, "pivot_transaction_ref" => nil, "retained_result_ref" => nil, "retained_species_ref" => nil
      })
    end
    return result
  end
end

module IronmonCollectionExperienceHooks
  def pbLearnMove(index, move)
    pokemon = pbParty(0)[index]
    return Ironmon::GameplayCollection::Ownership.track_move_learning(pokemon, move, "level_up") { super }
  end

  def pbGainExp(*args)
    return Ironmon::GameplayCollection::Combat.with_context({ "source_kind" => trainerBattle? ? "trainer_battle" : "wild_battle" }, self) { super }
  end

  def pbGainEVsOne(index, defeated)
    pokemon = pbParty(0)[index]
    return Ironmon::GameplayCollection::Ownership.with_owned_mutation(pokemon, "battle_effort_gain") { super }
  end
end

module IronmonCollectionEvolutionHooks
  def pbEvolution(*args)
    ownership = Ironmon::GameplayCollection::Ownership
    return super if !ownership.active? || !ownership.owned?(@pokemon)
    identifier = nil
    before = nil
    ownership.observe do
      before = ownership.species_ref(@pokemon)
      identifier = Ironmon::GameplayCollection.next_id("evolution")
    end
    return super if !identifier
    was_usable = ownership.usable?(@pokemon)
    boundary = ownership.boundary
    result = ownership.with_transaction { super }
    return result if !ownership.same_boundary?(boundary) || !ownership.owned?(@pokemon)
    ownership.observe do
      after = ownership.species_ref(@pokemon)
      Ironmon::GameplayCollection.emit("pokemon.evolution_resolved", {
        "evolution_id" => identifier, "pokemon_ref" => Ironmon::GameplayCollection.pokemon_instance_id(@pokemon),
        "species_before_ref" => before, "species_after_ref" => after, "outcome" => before == after ? "cancelled" : "success",
        "trigger_kind" => "native_evolution", "effective_method_id" => nil, "cause_ref" => nil, "duplicate_acquisition_ref" => nil,
        "usable_runner_context" => { "was_usable_runner" => was_usable, "retained_after_outer_commit" => ownership.owned?(@pokemon), "final_owned_entity_ref" => Ironmon::GameplayCollection.pokemon_instance_id(@pokemon) }
      })
      ownership.commit(@pokemon, "configuration_change", ["evolution"], nil, true) if before != after
    end
    return result
  end
end

Pokemon.prepend(IronmonCollectionOwnedMutationHooks)
StartersSelectionScene.prepend(IronmonCollectionStarterChoiceHooks)
Ironmon.singleton_class.prepend(IronmonCollectionOwnedAcquisitionHooks)
PokeBattle_Battle.prepend(IronmonCollectionExperienceHooks)
PokemonEvolutionScene.prepend(IronmonCollectionEvolutionHooks)

alias ironmon_collection_original_learn_move pbLearnMove
def pbLearnMove(pokemon, move, *args, &block)
  collection = Ironmon::GameplayCollection
  local = collection.current_context
  source = local["learning_source"] || (args[1] ? "machine" : "unknown")
  return collection::Ownership.track_move_learning(pokemon, move, source) do
    ironmon_collection_original_learn_move(pokemon, move, *args, &block)
  end
end

alias ironmon_collection_original_move_tutor_choose pbMoveTutorChoose
def pbMoveTutorChoose(move, movelist = nil, bymachine = false, *args, &block)
  return Ironmon::GameplayCollection.with_context({ "learning_source" => bymachine ? "machine" : "tutor" }) do
    ironmon_collection_original_move_tutor_choose(move, movelist, bymachine, *args, &block)
  end
end

[:pbJustRaiseEffortValues, :pbRaiseEffortValues, :pbRaiseHappinessAndLowerEV].each do |method_name|
  original_name = "ironmon_collection_original_#{method_name}"
  Object.send(:alias_method, original_name, method_name)
  Object.send(:define_method, method_name) do |pokemon, *args, &block|
    Ironmon::GameplayCollection::Ownership.with_owned_mutation(pokemon, "effort_item") do
      send(original_name, pokemon, *args, &block)
    end
  end
  Object.send(:private, method_name)
end

alias ironmon_collection_original_change_level pbChangeLevel
def pbChangeLevel(pokemon, new_level, scene)
  return Ironmon::GameplayCollection.with_context({ "learning_source" => "level_up" }) do
    ironmon_collection_original_change_level(pokemon, new_level, scene)
  end
end
