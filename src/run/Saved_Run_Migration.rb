#===============================================================================
# Explicit saved-run migration for changed game-data catalogs
#===============================================================================

module Ironmon
  class SavedRunMigrationDeclined < StandardError; end

  SAVED_RUN_MIGRATION_SYSTEMS = [
    :custom_fusion_pool,
    :ability_randomization,
    :base_stat_randomization,
    :evolution_randomization,
    :item_randomization,
    :move_access_randomization
  ].freeze

  def self.with_saved_run_metadata(metadata)
    original_global = $PokemonGlobal
    $PokemonGlobal = metadata
    return yield
  ensure
    $PokemonGlobal = original_global
  end

  def self.saved_run_migration_issues(save_data)
    return [] if !save_data.is_a?(Hash)
    metadata = save_data[:global_metadata]
    return [] if !metadata || metadata.ironmon_mode != true
    return with_saved_run_metadata(metadata) do
      issues = []
      issues << :custom_fusion_pool if
        saved_custom_fusion_pool_incompatible?
      issues << :ability_randomization if
        saved_ability_randomization_incompatible?
      issues << :base_stat_randomization if
        saved_base_stat_randomization_incompatible?
      issues << :evolution_randomization if
        saved_evolution_randomization_incompatible?
      issues << :item_randomization if
        saved_item_randomization_incompatible?
      issues << :move_access_randomization if
        saved_move_access_randomization_incompatible?
      issues.freeze
    end
  end

  def self.saved_custom_fusion_pool_incompatible?
    fields = [
      :ironmon_custom_fusion_pool_version,
      :ironmon_custom_fusion_pool_size,
      :ironmon_custom_fusion_pool_fingerprint
    ]
    return false if generator_metadata_absent?(fields)
    info = custom_fusion_pool_info
    return !generator_metadata_matches?([
      ["schema", $PokemonGlobal.ironmon_custom_fusion_pool_version,
       info[:schema_version]],
      ["size", $PokemonGlobal.ironmon_custom_fusion_pool_size, info[:size]],
      ["catalog", $PokemonGlobal.ironmon_custom_fusion_pool_fingerprint,
       info[:fingerprint]]
    ])
  rescue Exception
    return true
  end

  def self.saved_ability_randomization_incompatible?
    fields = [
      :ironmon_ability_generator_version,
      :ironmon_ability_pool_fingerprint
    ]
    return false if generator_metadata_absent?(fields)
    return false if current_ability_randomization?
    return !legacy_fusion_fallback_ability_randomization?
  end

  def self.saved_base_stat_randomization_incompatible?
    fields = [
      :ironmon_base_stat_generator_version,
      :ironmon_base_stat_source_fingerprint
    ]
    return false if generator_metadata_absent?(fields)
    return !current_base_stat_randomization?
  end

  def self.saved_evolution_randomization_incompatible?
    return false if legacy_evolution_randomization?
    return false if current_evolution_randomization?
    return legacy_generated_evolution_rules_version.nil?
  end

  def self.saved_item_randomization_incompatible?
    return false if legacy_item_randomization?
    return false if generator_metadata_absent?([
      :ironmon_item_generator_version
    ])
    return !current_item_randomization?
  end

  def self.saved_move_access_randomization_incompatible?
    return false if generator_metadata_absent?(MOVE_ACCESS_METADATA_FIELDS)
    return !current_move_access_randomization?
  end

  def self.saved_run_migration_issue_label(issue)
    return _INTL("custom fusion pool") if issue == :custom_fusion_pool
    return _INTL("abilities") if issue == :ability_randomization
    return _INTL("base stats") if issue == :base_stat_randomization
    return _INTL("evolutions") if issue == :evolution_randomization
    return _INTL("items") if issue == :item_randomization
    return _INTL("move access") if issue == :move_access_randomization
    return issue.to_s
  end

  def self.confirm_saved_run_migration(save_data, issues)
    saved_version = save_data[:game_version].to_s
    saved_version = _INTL("an older version") if saved_version.empty?
    current_version = if defined?(Settings::GAME_VERSION_NUMBER)
                        Settings::GAME_VERSION_NUMBER.to_s
                      else
                        _INTL("the current version")
                      end
    labels = issues.map do |issue|
      saved_run_migration_issue_label(issue)
    end.join(", ")
    message = _INTL(
      "This Ironmon save uses game data from {1} that is incompatible with Infinite Fusion {2}.\n\nAffected systems: {3}\n\nMigrate this save for the current game version? Generated results in the affected systems may change. Choosing No returns to save selection without changing the save file.",
      saved_version, current_version, labels
    )
    return pbConfirmMessageSerious(message)
  end

  def self.begin_saved_run_migration(save_data)
    issues = saved_run_migration_issues(save_data)
    @approved_saved_run_migration = nil
    return true if issues.empty?
    if !confirm_saved_run_migration(save_data, issues)
      raise SavedRunMigrationDeclined
    end
    @approved_saved_run_migration = issues
    echoln _INTL(
      "Ironmon approved saved-run migration for: {1}.",
      issues.map { |issue| saved_run_migration_issue_label(issue) }.join(", ")
    )
    return true
  end

  def self.saved_run_migration_approved?(system)
    return false if !SAVED_RUN_MIGRATION_SYSTEMS.include?(system)
    return @approved_saved_run_migration &&
      @approved_saved_run_migration.include?(system)
  end

  def self.finish_saved_run_migration
    @approved_saved_run_migration = nil
  end
end

module Game
  class << self
    alias ironmon_saved_run_migration_original_load load

    def load(save_data)
      Ironmon.begin_saved_run_migration(save_data)
      return ironmon_saved_run_migration_original_load(save_data)
    ensure
      Ironmon.finish_saved_run_migration
    end
  end
end

class PokemonLoadScreen
  alias ironmon_saved_run_migration_original_start pbStartLoadScreen

  def pbStartLoadScreen
    loop do
      begin
        return ironmon_saved_run_migration_original_start
      rescue Ironmon::SavedRunMigrationDeclined
        @scene = PokemonLoad_Scene.new
      end
    end
  end
end
