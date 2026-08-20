#===============================================================================
# Ironmon base-stat engine and load integration
#===============================================================================

class GameData::Species
  alias ironmon_unrandomized_base_stats base_stats

  def base_stats
    return ironmon_unrandomized_base_stats if
      !Ironmon.base_stat_randomization_active?
    return Ironmon.generated_base_stats_for(self)
  end
end

class Pokemon
  alias ironmon_base_stat_original_baseStats baseStats

  def baseStats
    return ironmon_base_stat_original_baseStats if
      !Ironmon.base_stat_randomization_active?
    return Ironmon.generated_base_stats_for_pokemon(self)
  end
end

Ironmon.register_game_load_hook(
  :base_stat_randomization,
  proc { |_save_data| Ironmon.suspend_base_stat_randomization },
  proc do |_save_data, _result|
    next if Ironmon.checkpoint_reset_loading?
    Ironmon.ensure_base_stat_randomization if Ironmon.active?
  end
)
