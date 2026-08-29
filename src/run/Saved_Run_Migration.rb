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

  def self.saved_run_migration_issue_explanation(issue)
    if issue == :custom_fusion_pool
      return _INTL(
        "Custom fusion pool: the eligible custom-sprite catalog changed. " +
        "This usually happens after Infinite Fusion updates CUSTOM_SPRITES " +
        "or Sprite_Credits.csv."
      )
    end
    return _INTL(
      "{1}: the installed source data or generator rules changed.",
      saved_run_migration_issue_label(issue)
    )
  end

  def self.saved_run_migration_message(save_data, issues)
    saved_version = save_data[:game_version].to_s
    saved_version = _INTL("an older version") if saved_version.empty?
    current_version = if defined?(Settings::GAME_VERSION_NUMBER)
                        Settings::GAME_VERSION_NUMBER.to_s
                      else
                        _INTL("the current version")
                      end
    version_context = if saved_version == current_version
                        _INTL(
                          "The game version is still {1}; an underlying " +
                          "data catalog changed.", current_version
                        )
                      else
                        _INTL(
                          "The save was created with game version {1}; the " +
                          "installed version is {2}.", saved_version,
                          current_version
                        )
                      end
    explanations = issues.map do |issue|
      "- #{saved_run_migration_issue_explanation(issue)}"
    end.join("\n")
    return _INTL(
      "This Ironmon save no longer matches the installed generation data.\n\n{1}\n\nAffected systems:\n{2}\n\nMigrate the affected systems to the currently installed data? Generated results in those systems may change. Choosing No returns to save selection without changing the save file.",
      version_context, explanations
    )
  end

  def self.confirm_saved_run_migration(save_data, issues)
    return pbConfirmMessageSerious(
      saved_run_migration_message(save_data, issues)
    )
  end

  def self.begin_saved_run_migration(save_data)
    @approved_saved_run_migration = nil
    return true if checkpoint_reset_loading?
    issues = saved_run_migration_issues(save_data)
    return true if issues.empty?
    if !confirm_saved_run_migration(save_data, issues)
      raise SavedRunMigrationDeclined
    end
    @approved_saved_run_migration = issues
    if issues.include?(:custom_fusion_pool)
      reset_fusion_predecessor_index_cache
      fusion_predecessor_index
    end
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
