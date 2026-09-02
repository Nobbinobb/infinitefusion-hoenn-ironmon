#===============================================================================
# Ironmon tracker deterministic post-run inspection
#===============================================================================

require "digest/sha2"

module Ironmon
  TRACKER_SEARCH_LIMIT = 50

  class TrackerLookupError < StandardError
    attr_reader :code

    def initialize(code, message)
      @code = code
      super(message)
    end
  end

  def self.complete_tracker_run(result)
    return complete_run(result)
  end

  def self.publish_run_completion
    recipe = tracker_completed_run_recipe
    if recipe
      run_ledger["last_completed_recipe"] = Marshal.load(Marshal.dump(recipe))
      tracker_connection.send_event("run_completed", {
        "recipe" => recipe,
        "request_archive_selection" => true
      })
    end
  rescue Exception => e
    echoln "Ironmon tracker could not complete the run recipe: #{e.message}"
  end

  def self.tracker_recoverable_completed_run_recipe
    attempt = current_run_attempt if respond_to?(:current_run_attempt)
    return nil if attempt && attempt["result"] == "active"
    recipe = tracker_completed_run_recipe
    return recipe if recipe
    stored = run_ledger["last_completed_recipe"]
    return stored if stored.is_a?(Hash)
    return nil
  end

  def self.tracker_completed_run_recipe(result_override = nil)
    return nil if !active? || !$PokemonGlobal
    result = result_override || $PokemonGlobal.ironmon_run_result
    return nil if !result || result.to_s.empty?
    reproduction = tracker_run_reproduction_recipe
    recipe = {
      "schema_version" => 1,
      "run_id" => ensure_tracker_run_id,
      "seed" => reproduction.delete("seed"),
      "result" => result.to_s
    }
    reproduction.each { |key, value| recipe[key] = value }
    recipe.merge!({
      "item_mappings" => tracker_item_mapping_recipe(
        $PokemonGlobal.randomItemsHash
      ),
      "tm_mappings" => tracker_item_mapping_recipe(
        $PokemonGlobal.randomTMsHash
      ),
      "statistics" => tracker_attempt_statistics(current_run_attempt)
    })
    return recipe
  end

  def self.tracker_run_reproduction_recipe
    return {
      "seed" => $PokemonGlobal.ironmon_seed || 0,
      "generation_profile_id" => pinned_generation_profile_id,
      "game_version" => tracker_game_version,
      "ironmon_version" => VERSION,
      "configuration" => configuration_snapshot,
      "data_mode" => tracker_data_mode,
      "species_generator" => tracker_species_generator_recipe,
      "ability_generator" => tracker_ability_generator_recipe,
      "base_stat_generator" => tracker_base_stat_generator_recipe,
      "evolution_generator" => tracker_evolution_generator_recipe,
      "move_access_generator" => tracker_move_access_generator_recipe,
      "player_fusion_generator" => tracker_player_fusion_generator_recipe,
      "item_generator" => item_generator_recipe
    }
  end

  def self.tracker_compatibility_manifest(recipe = nil)
    recipe ||= tracker_run_reproduction_recipe
    manifest = {
      "schema_version" => 1,
      "generation_profile_id" => recipe["generation_profile_id"],
      "game_version" => recipe["game_version"],
      "ironmon_version" => recipe["ironmon_version"],
      "data_mode" => recipe["data_mode"],
      "species_generator" => recipe["species_generator"],
      "ability_generator" => recipe["ability_generator"]
    }
    base_stats = recipe["base_stat_generator"]
    evolutions = recipe["evolution_generator"]
    moves = recipe["move_access_generator"]
    items = recipe["item_generator"]
    manifest["base_stat_generator"] = base_stats if base_stats
    manifest["evolution_generator"] = evolutions if evolutions
    manifest["move_access_generator"] = moves if moves
    manifest["player_fusion_generator"] = recipe["player_fusion_generator"]
    if items
      manifest["item_generator"] = {
        "version" => items["version"],
        "rules_version" => items["rules_version"],
        "ground_pool_size" => items["ground_pool_size"],
        "ground_total_weight" => items["ground_total_weight"],
        "ground_pool_fingerprint" => items["ground_pool_fingerprint"],
        "tm_pool_size" => items["tm_pool_size"],
        "tm_pool_fingerprint" => items["tm_pool_fingerprint"],
        "result_bans" => items["result_bans"],
        "result_ban_fingerprint" => items["result_ban_fingerprint"],
        "shop_policy_version" => items["shop_policy_version"]
      }
    end
    return manifest
  end

  def self.tracker_compatibility_fingerprint(recipe = nil)
    json = tracker_json_generate(tracker_compatibility_manifest(recipe))
    return Digest::SHA256.hexdigest(json)
  end

  def self.tracker_attempt_statistics(attempt)
    return nil if !attempt.is_a?(Hash)
    tick_active_run_duration if attempt["result"] == "active"
    statistics = attempt["statistics"]
    return nil if !statistics.is_a?(Hash)
    if !statistics["trainer_species_names"].is_a?(Hash)
      statistics = normalize_attempt_statistics(statistics)
      attempt["statistics"] = statistics
    end
    ledger = run_ledger
    return {
      "schema_version" => statistics["schema_version"],
      "attempt_number" => attempt["attempt_number"],
      "save_slot" => ($Trainer ? $Trainer.save_slot : nil),
      "seed" => attempt["seed"],
      "result" => attempt["result"],
      "active_seconds" => attempt["active_seconds"],
      "attempts_started" => ledger["attempts_started"],
      "attempts_lost" => ledger["attempts_lost"],
      "attempts_won" => ledger["attempts_won"],
      "attempts_abandoned" => ledger["attempts_abandoned"],
      "battles_completed" => statistics["battles_completed"],
      "highest_player_level" => statistics["highest_player_level"],
      "badges_earned" => statistics["badges_earned"],
      "total_item_healing" => statistics["total_item_healing"],
      "wasted_item_healing" => statistics["wasted_item_healing"],
      "items_used" => statistics["items_used"],
      "items_by_source" => statistics["items_by_source"],
      "trainer_species_counts" => statistics["trainer_species_counts"],
      "trainer_species_names" => statistics["trainer_species_names"],
      "trainer_species_distinct" => statistics["trainer_species_distinct"],
      "trainer_species_most_encountered" =>
        statistics["trainer_species_most_encountered"],
      "trainer_defeated_count" => statistics["trainer_defeated_count"],
      "trainer_defeated_bst_average" =>
        statistics["trainer_defeated_bst_average"],
      "trainer_defeated_bst_minimum" =>
        statistics["trainer_defeated_bst_minimum"],
      "trainer_defeated_bst_minimum_species" =>
        statistics["trainer_defeated_bst_minimum_species"],
      "trainer_defeated_bst_maximum" =>
        statistics["trainer_defeated_bst_maximum"],
      "trainer_defeated_bst_maximum_species" =>
        statistics["trainer_defeated_bst_maximum_species"]
    }
  end

  def self.tracker_species_generator_recipe
    return {
      "version" => $PokemonGlobal.ironmon_species_generator_version,
      "pool_fingerprint" => tracker_species_pool_fingerprint
    }
  end

  def self.tracker_ability_generator_recipe
    return {
      "version" => $PokemonGlobal.ironmon_ability_generator_version,
      "pool_size" => $PokemonGlobal.ironmon_ability_pool_size,
      "pool_fingerprint" => $PokemonGlobal.ironmon_ability_pool_fingerprint
    }
  end

  def self.tracker_base_stat_generator_recipe
    version = $PokemonGlobal.ironmon_base_stat_generator_version
    return nil if !version
    return {
      "version" => version,
      "source_fingerprint" => $PokemonGlobal.ironmon_base_stat_source_fingerprint
    }
  end

  def self.tracker_evolution_generator_recipe
    version = $PokemonGlobal.ironmon_evolution_generator_version
    return nil if !version
    return {
      "version" => version,
      "rules_version" => $PokemonGlobal.ironmon_evolution_rules_version,
      "source_fingerprint" => $PokemonGlobal.ironmon_evolution_source_fingerprint,
      "taxonomy_fingerprint" => $PokemonGlobal.ironmon_evolution_taxonomy_fingerprint,
      "method_fingerprint" => $PokemonGlobal.ironmon_evolution_method_fingerprint,
      "target_fingerprint" => $PokemonGlobal.ironmon_evolution_target_fingerprint,
      "base_stat_generator" => {
        "version" => $PokemonGlobal.ironmon_evolution_base_stat_generator_version,
        "source_fingerprint" => $PokemonGlobal.ironmon_evolution_base_stat_source_fingerprint
      },
      "fusion" => {
        "version" => $PokemonGlobal.ironmon_evolution_fusion_generator_version,
        "rules_version" => $PokemonGlobal.ironmon_evolution_fusion_rules_version,
        "target_pool" => {
          "version" => $PokemonGlobal.ironmon_evolution_fusion_target_pool_version,
          "size" => $PokemonGlobal.ironmon_evolution_fusion_target_pool_size,
          "fingerprint" => $PokemonGlobal.ironmon_evolution_fusion_target_pool_fingerprint
        }
      }
    }
  end

  def self.tracker_move_access_generator_recipe
    version = $PokemonGlobal.ironmon_move_access_generator_version
    return nil if !version
    return {
      "version" => version,
      "pool_fingerprint" => $PokemonGlobal.ironmon_move_pool_fingerprint,
      "contextual_restriction_fingerprint" => $PokemonGlobal.ironmon_move_contextual_restriction_fingerprint,
      "level_up_source_fingerprint" => $PokemonGlobal.ironmon_move_source_fingerprint,
      "egg_source_fingerprint" => $PokemonGlobal.ironmon_egg_move_source_fingerprint,
      "tm" => {
        "roster_fingerprint" => $PokemonGlobal.ironmon_tm_roster_fingerprint,
        "source_fingerprint" => $PokemonGlobal.ironmon_tm_source_fingerprint
      },
      "tr" => {
        "roster_fingerprint" => $PokemonGlobal.ironmon_tr_roster_fingerprint,
        "source_fingerprint" => $PokemonGlobal.ironmon_tr_source_fingerprint
      },
      "tutor" => {
        "catalog_fingerprint" => $PokemonGlobal.ironmon_tutor_catalog_fingerprint,
        "source_fingerprint" => $PokemonGlobal.ironmon_tutor_source_fingerprint
      },
      "fusion_tutor" => {
        "catalog_fingerprint" => $PokemonGlobal.ironmon_fusion_tutor_catalog_fingerprint,
        "source_fingerprint" => $PokemonGlobal.ironmon_fusion_tutor_source_fingerprint
      }
    }
  end

  def self.tracker_player_fusion_generator_recipe
    return {
      "version" => PlayerFusionMapper::SCHEMA_VERSION,
      "pool_size" => $PokemonGlobal.ironmon_custom_fusion_pool_size,
      "pool_fingerprint" => $PokemonGlobal.ironmon_custom_fusion_pool_fingerprint
    }
  end

  def self.tracker_item_mapping_recipe(mapping)
    return {} if !mapping.is_a?(Hash)
    result = {}
    mapping.each do |source, target|
      next if !source || !target
      result[source.to_s] = target.to_s
    end
    return result
  end
end
