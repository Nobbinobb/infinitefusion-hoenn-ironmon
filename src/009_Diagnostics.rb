#===============================================================================
# Milestone 1 diagnostic logging
#===============================================================================

module Ironmon
  DIAGNOSTIC_LOG_FILENAME = "Ironmon.log"

  def self.diagnostic_log_path
    return nil if !defined?(SaveData::SAVE_DIR) || !SaveData::SAVE_DIR
    return File.join(SaveData::SAVE_DIR, DIAGNOSTIC_LOG_FILENAME)
  end

  def self.run_diagnostic_line(context)
    configuration_value = configuration
    pool_size = if $PokemonGlobal &&
                   $PokemonGlobal.ironmon_custom_fusion_pool_size
                  $PokemonGlobal.ironmon_custom_fusion_pool_size
                else
                  custom_fusion_pool_info[:size]
                end
    pool_fingerprint = if $PokemonGlobal &&
                          $PokemonGlobal.ironmon_custom_fusion_pool_fingerprint
                         $PokemonGlobal.ironmon_custom_fusion_pool_fingerprint
                       else
                         custom_fusion_pool_info[:fingerprint]
                       end
    wild_count = if $PokemonGlobal &&
                    $PokemonGlobal.ironmon_wild_species_map.is_a?(Hash)
                   $PokemonGlobal.ironmon_wild_species_map.length
                 else
                   0
                 end
    trainer_count = if $PokemonGlobal &&
                       $PokemonGlobal.ironmon_trainer_species_map.is_a?(Hash)
                      $PokemonGlobal.ironmon_trainer_species_map.length
                    else
                      0
                    end
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : nil
    ability_version = if $PokemonGlobal
                        $PokemonGlobal.ironmon_ability_generator_version
                      else
                        nil
                      end
    ability_pool_size = if $PokemonGlobal
                          $PokemonGlobal.ironmon_ability_pool_size
                        else
                          nil
                        end
    ability_fingerprint = if $PokemonGlobal
                            $PokemonGlobal.ironmon_ability_pool_fingerprint
                          else
                            nil
                          end
    base_stat_version = if $PokemonGlobal
                          $PokemonGlobal.ironmon_base_stat_generator_version
                        else
                          nil
                        end
    base_stat_fingerprint = if $PokemonGlobal
                              $PokemonGlobal.ironmon_base_stat_source_fingerprint
                            else
                              nil
                            end
    evolution_version = $PokemonGlobal ?
      $PokemonGlobal.ironmon_evolution_generator_version : nil
    evolution_rules = $PokemonGlobal ?
      $PokemonGlobal.ironmon_evolution_rules_version : nil
    evolution_source_fingerprint = $PokemonGlobal ?
      $PokemonGlobal.ironmon_evolution_source_fingerprint : nil
    evolution_target_fingerprint = $PokemonGlobal ?
      $PokemonGlobal.ironmon_evolution_target_fingerprint : nil
    fusion_evolution_version = $PokemonGlobal ?
      $PokemonGlobal.ironmon_evolution_fusion_generator_version : nil
    fusion_evolution_rules = $PokemonGlobal ?
      $PokemonGlobal.ironmon_evolution_fusion_rules_version : nil
    fusion_evolution_pool_fingerprint = $PokemonGlobal ?
      $PokemonGlobal.ironmon_evolution_fusion_target_pool_fingerprint : nil
    evolution_event_count = if $PokemonGlobal &&
                               $PokemonGlobal.ironmon_evolution_metrics.is_a?(Hash)
                              events = $PokemonGlobal.ironmon_evolution_metrics["events"]
                              events.is_a?(Array) ? events.length : 0
                            else
                              0
                            end
    move_access_version = if $PokemonGlobal
                            $PokemonGlobal.ironmon_move_access_generator_version
                          else
                            nil
                          end
    move_pool_size = if $PokemonGlobal
                       $PokemonGlobal.ironmon_move_pool_size
                     else
                       nil
                     end
    move_pool_fingerprint = if $PokemonGlobal
                              $PokemonGlobal.ironmon_move_pool_fingerprint
                            else
                              nil
                            end
    move_source_fingerprint = if $PokemonGlobal
                                $PokemonGlobal.ironmon_move_source_fingerprint
                              else
                                nil
                              end
    egg_move_source_fingerprint = if $PokemonGlobal
                                    $PokemonGlobal.ironmon_egg_move_source_fingerprint
                                  else
                                    nil
                                  end
    tm_roster_size = $PokemonGlobal ?
      $PokemonGlobal.ironmon_tm_roster_size : nil
    tm_roster_fingerprint = $PokemonGlobal ?
      $PokemonGlobal.ironmon_tm_roster_fingerprint : nil
    tm_source_fingerprint = $PokemonGlobal ?
      $PokemonGlobal.ironmon_tm_source_fingerprint : nil
    tr_roster_size = $PokemonGlobal ?
      $PokemonGlobal.ironmon_tr_roster_size : nil
    tr_roster_fingerprint = $PokemonGlobal ?
      $PokemonGlobal.ironmon_tr_roster_fingerprint : nil
    tr_source_fingerprint = $PokemonGlobal ?
      $PokemonGlobal.ironmon_tr_source_fingerprint : nil
    tutor_catalog_size = $PokemonGlobal ?
      $PokemonGlobal.ironmon_tutor_catalog_size : nil
    tutor_catalog_fingerprint = $PokemonGlobal ?
      $PokemonGlobal.ironmon_tutor_catalog_fingerprint : nil
    tutor_source_fingerprint = $PokemonGlobal ?
      $PokemonGlobal.ironmon_tutor_source_fingerprint : nil
    fusion_tutor_regular_size = $PokemonGlobal ?
      $PokemonGlobal.ironmon_fusion_tutor_regular_catalog_size : nil
    fusion_tutor_legendary_size = $PokemonGlobal ?
      $PokemonGlobal.ironmon_fusion_tutor_legendary_catalog_size : nil
    fusion_tutor_catalog_fingerprint = $PokemonGlobal ?
      $PokemonGlobal.ironmon_fusion_tutor_catalog_fingerprint : nil
    fusion_tutor_source_fingerprint = $PokemonGlobal ?
      $PokemonGlobal.ironmon_fusion_tutor_source_fingerprint : nil
    return "[Ironmon #{VERSION}] context=#{context} seed=#{seed} " +
      "wild_policy=#{configuration_value.wild_policy} " +
      "trainer_policy=#{configuration_value.trainer_policy} " +
      "unfusion_setting=#{configuration_value.unfusion_setting} " +
      "custom_pool_size=#{pool_size} " +
      "custom_pool_fingerprint=#{pool_fingerprint} " +
      "ability_generator=#{ability_version} " +
      "ability_pool_size=#{ability_pool_size} " +
      "ability_pool_fingerprint=#{ability_fingerprint} " +
      "base_stat_generator=#{base_stat_version} " +
      "base_stat_source_fingerprint=#{base_stat_fingerprint} " +
      "evolution_generator=#{evolution_version} " +
      "evolution_rules=#{evolution_rules} " +
      "evolution_source_fingerprint=#{evolution_source_fingerprint} " +
      "evolution_target_fingerprint=#{evolution_target_fingerprint} " +
      "fusion_evolution_generator=#{fusion_evolution_version} " +
      "fusion_evolution_rules=#{fusion_evolution_rules} " +
      "fusion_evolution_pool_fingerprint=#{fusion_evolution_pool_fingerprint} " +
      "evolution_events=#{evolution_event_count} " +
      "move_access_generator=#{move_access_version} " +
      "move_pool_size=#{move_pool_size} " +
      "move_pool_fingerprint=#{move_pool_fingerprint} " +
      "move_source_fingerprint=#{move_source_fingerprint} " +
      "egg_move_source_fingerprint=#{egg_move_source_fingerprint} " +
      "tm_roster_size=#{tm_roster_size} " +
      "tm_roster_fingerprint=#{tm_roster_fingerprint} " +
      "tm_source_fingerprint=#{tm_source_fingerprint} " +
      "tr_roster_size=#{tr_roster_size} " +
      "tr_roster_fingerprint=#{tr_roster_fingerprint} " +
      "tr_source_fingerprint=#{tr_source_fingerprint} " +
      "tutor_catalog_size=#{tutor_catalog_size} " +
      "tutor_catalog_fingerprint=#{tutor_catalog_fingerprint} " +
      "tutor_source_fingerprint=#{tutor_source_fingerprint} " +
      "fusion_tutor_regular_size=#{fusion_tutor_regular_size} " +
      "fusion_tutor_legendary_size=#{fusion_tutor_legendary_size} " +
      "fusion_tutor_catalog_fingerprint=#{fusion_tutor_catalog_fingerprint} " +
      "fusion_tutor_source_fingerprint=#{fusion_tutor_source_fingerprint} " +
      "wild_mappings=#{wild_count} trainer_mappings=#{trainer_count}"
  end

  def self.log_run_diagnostics(context)
    return if !active?
    line = run_diagnostic_line(context)
    echoln line
    path = diagnostic_log_path
    return line if !path
    begin
      File.open(path, "ab") do |file|
        file.write("#{Time.now.strftime("%Y-%m-%d %H:%M:%S")} #{line}\n")
      end
    rescue Exception => e
      echoln "Ironmon diagnostic log could not be written: #{e.message}"
    end
    return line
  end
end

module Game
  class << self
    alias ironmon_diagnostics_original_load load
    def load(save_data)
      result = ironmon_diagnostics_original_load(save_data)
      return result if Ironmon.checkpoint_reset_loading?
      Ironmon.log_run_diagnostics(:load) if Ironmon.active?
      return result
    end
  end
end
