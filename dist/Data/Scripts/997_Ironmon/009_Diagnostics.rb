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
      Ironmon.log_run_diagnostics(:load) if Ironmon.active?
      return result
    end
  end
end
