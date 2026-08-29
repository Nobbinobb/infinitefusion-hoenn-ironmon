module IronmonRunTransitionRuntimeTests
  OUTPUT_PATH = $ironmon_run_transition_test_output_path.to_s

  def self.assert(condition, message)
    raise "Run-transition runtime test failed: #{message}" if !condition
  end

  def self.ledger_fixture
    ledger = Ironmon.default_run_ledger
    ledger["next_attempt_number"] = 5
    ledger["attempts_started"] = 4
    ledger["attempts_lost"] = 1
    ledger["attempts_won"] = 1
    ledger["attempts_abandoned"] = 1
    ledger["current_attempt"] = {
      "attempt_number" => 4,
      "run_id" => "run-four",
      "seed" => 44,
      "result" => "active",
      "active_seconds" => 12.5,
      "statistics" => nil
    }
    return ledger
  end

  def self.test_ledger_completion_staging
    ledger = ledger_fixture
    recipe = {
      "run_id" => "run-four",
      "result" => "abandoned",
      "statistics" => {
        "result" => "active",
        "active_seconds" => 0.0,
        "attempts_lost" => 0,
        "attempts_won" => 0,
        "attempts_abandoned" => 0
      }
    }
    staged = Ironmon.stage_run_ledger_completion(
      ledger, :abandoned, recipe
    )
    assert(
      ledger["current_attempt"]["result"] == "active",
      "completion staging does not mutate the live ledger"
    )
    assert(
      staged["current_attempt"]["result"] == "abandoned" &&
        staged["attempts_abandoned"] == 2,
      "completion staging archives and counts the attempt once"
    )
    statistics = staged["last_completed_recipe"]["statistics"]
    assert(
      statistics["result"] == "abandoned" &&
        statistics["active_seconds"] == 12.5 &&
        statistics["attempts_abandoned"] == 2,
      "the staged recipe reflects the committed ledger"
    )
  end

  def self.test_active_attempt_does_not_recover_completed_recipe
    with_active_run_runtime do
      ledger = ledger_fixture
      completed_recipe = { "run_id" => "run-three", "result" => "lost" }
      ledger["last_completed_recipe"] = completed_recipe
      $PokemonGlobal.ironmon_run_ledger = ledger
      assert(
        Ironmon.tracker_recoverable_completed_run_recipe.nil?,
        "an active attempt does not masquerade as the prior completed run"
      )
      ledger["current_attempt"] = nil
      assert(
        Ironmon.tracker_recoverable_completed_run_recipe == completed_recipe,
        "a completed recipe remains recoverable when no attempt is active"
      )
    end
  end

  def self.test_active_run_preparation_boundary
    with_active_run_runtime do
      $PokemonGlobal.ironmon_run_ledger = ledger_fixture
      $PokemonGlobal.ironmon_tracker_active_run_preparation_run_id = nil
      assert(
        !Ironmon.tracker_active_run_preparation_ready?,
        "an unset run marker cannot inherit stale story progression"
      )
      assert(
        Ironmon.tracker_active_fusion_assignment_recipe.nil?,
        "fusion assignment preparation is absent before the rival victory"
      )
      $PokemonGlobal.ironmon_tracker_active_run_preparation_run_id = "run-four"
      assert(
        Ironmon.tracker_active_run_preparation_ready?,
        "background preparation becomes ready after the first rival victory"
      )
      $PokemonGlobal.ironmon_tracker_active_run_preparation_run_id = "old-run"
      assert(
        !Ironmon.tracker_active_run_preparation_ready?,
        "a new attempt cannot inherit another run's preparation marker"
      )
      $PokemonGlobal.ironmon_tracker_active_run_preparation_run_id = "run-four"
      $PokemonGlobal.ironmon_run_ledger = Ironmon.default_run_ledger
      Ironmon.begin_run_attempt(123)
      assert(
        !Ironmon.tracker_active_run_preparation_ready?,
        "beginning an attempt clears preparation inherited from the prior run"
      )
    end
  end

  def self.with_active_run_runtime
    original_global = $PokemonGlobal
    original_switches = $game_switches
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonGlobal.ironmon_mode = true
    $PokemonGlobal.ironmon_configuration = Ironmon::Configuration.new
    $game_switches = []
    return yield
  ensure
    $PokemonGlobal = original_global
    $game_switches = original_switches
  end

  def self.with_reset_stubs(generation_result, save_result)
    ironmon_singleton = class << Ironmon; self; end
    game_singleton = class << Game; self; end
    methods = [
      :apply_preset, :generation_error_message,
      :restore_live_save_snapshot, :tracker_connection, :start_tracker_run
    ]
    methods.each do |method|
      ironmon_singleton.send(
        :alias_method, "run_transition_original_#{method}", method
      )
    end
    game_singleton.send(
      :alias_method, :run_transition_original_save, :save
    )
    original_trainer = $Trainer
    original_transaction = Ironmon.instance_variable_get(:@reset_transaction)
    original_in_progress = Ironmon.instance_variable_get(:@reset_in_progress)
    original_save_slot = Ironmon.instance_variable_get(:@reset_save_slot)
    original_notice = Ironmon.instance_variable_get(:@reset_notice)
    events = []
    arguments = []
    trainer = Object.new
    class << trainer
      attr_accessor :save_slot
    end
    trainer.save_slot = "File A"
    $Trainer = trainer
    connection = Object.new
    connection.define_singleton_method(:send_event) do |*event_arguments|
      events << [:publish, event_arguments]
    end
    ironmon_singleton.send(:define_method, :apply_preset) do |*method_arguments|
      events << :generate
      arguments << method_arguments
      generation_result
    end
    ironmon_singleton.send(:define_method, :generation_error_message) do
      "injected generation failure"
    end
    ironmon_singleton.send(:define_method, :restore_live_save_snapshot) do |data|
      events << [:restore, data]
      nil
    end
    ironmon_singleton.send(:define_method, :tracker_connection) { connection }
    ironmon_singleton.send(:define_method, :start_tracker_run) do
      events << :start_tracker
    end
    game_singleton.send(:define_method, :save) do |*method_arguments|
      events << [:save, method_arguments]
      save_result
    end
    result = yield(events, arguments)
    return result
  ensure
    methods.each do |method|
      original = "run_transition_original_#{method}"
      ironmon_singleton.send(:alias_method, method, original)
      ironmon_singleton.send(:remove_method, original)
    end
    game_singleton.send(
      :alias_method, :save, :run_transition_original_save
    )
    game_singleton.send(:remove_method, :run_transition_original_save)
    $Trainer = original_trainer
    Ironmon.instance_variable_set(:@reset_transaction, original_transaction)
    Ironmon.instance_variable_set(:@reset_in_progress, original_in_progress)
    Ironmon.instance_variable_set(:@reset_save_slot, original_save_slot)
    Ironmon.instance_variable_set(:@reset_notice, original_notice)
  end

  def self.reset_transaction(automatic = false, save_slot = "File B")
    return {
      "rollback_data" => { :marker => :live_attempt },
      "completed_recipe" => { "run_id" => "old-run" },
      "source_run_id" => "old-run",
      "source_sequence" => 9,
      "save_slot" => save_slot,
      "automatic" => automatic,
      "seed_to_avoid" => nil
    }
  end

  def self.prepare_reset(transaction)
    Ironmon.instance_variable_set(:@reset_transaction, transaction)
    Ironmon.instance_variable_set(:@reset_in_progress, true)
    Ironmon.instance_variable_set(:@reset_save_slot, transaction["save_slot"])
    Ironmon.instance_variable_set(:@reset_notice, nil)
  end

  def self.test_generation_failure_rollback
    with_reset_stubs(false, true) do |events, arguments|
      transaction = reset_transaction
      prepare_reset(transaction)
      result = Ironmon.finish_pending_reset
      assert(!result, "generation failure rejects the reset")
      assert(
        arguments == [[:f7_reset, nil, false]],
        "reset generation defers tracker startup until commit"
      )
      assert(
        events == [:generate, [:restore, transaction["rollback_data"]]],
        "generation failure restores the live attempt without publishing"
      )
      notice = Ironmon.instance_variable_get(:@reset_notice)
      assert(
        notice[:type] == :reset_failed &&
          notice[:message] == "injected generation failure",
        "generation failure retains its actionable error"
      )
    end
  end

  def self.test_save_failure_rollback
    with_reset_stubs(true, false) do |events, _arguments|
      transaction = reset_transaction
      prepare_reset(transaction)
      result = Ironmon.finish_pending_reset
      assert(!result, "save failure rejects the reset")
      assert(
        events == [
          :generate, [:save, ["File B"]],
          [:restore, transaction["rollback_data"]]
        ],
        "save failure restores the live attempt without publishing"
      )
    end
  end

  def self.test_successful_reset_commit_order
    with_reset_stubs(true, true) do |events, _arguments|
      prepare_reset(reset_transaction)
      result = Ironmon.finish_pending_reset
      assert(result, "successful reset commits")
      assert(
        events == [
          :generate, [:save, ["File B"]],
          [:publish, [
            "run_completed", {
              "recipe" => { "run_id" => "old-run" },
              "request_archive_selection" => true
            }, "old-run", 10
          ]],
          :start_tracker
        ],
        "completion publication and tracker startup follow the successful save"
      )
      assert(
        Ironmon.instance_variable_get(:@reset_notice) == :success,
        "manual reset reports success after commit"
      )
      assert(
        !Ironmon.instance_variable_get(:@reset_in_progress) &&
          !Ironmon.instance_variable_get(:@reset_transaction),
        "committed reset clears its transaction state"
      )
    end
  end

  def self.test_automatic_reset_does_not_republish_loss
    with_reset_stubs(true, true) do |events, _arguments|
      transaction = reset_transaction(true, nil)
      transaction["completed_recipe"] = nil
      prepare_reset(transaction)
      result = Ironmon.finish_pending_reset
      assert(result, "automatic reset commits")
      assert(
        events == [:generate, :start_tracker],
        "automatic reset does not republish its already completed loss"
      )
      assert(
        Ironmon.instance_variable_get(:@reset_notice) == :automatic_success,
        "automatic reset remains silent after success"
      )
    end
  end

  def self.test_live_snapshot_restore_uses_normal_load
    save_data_singleton = class << SaveData; self; end
    game_singleton = class << Game; self; end
    save_data_singleton.send(
      :alias_method, :run_transition_original_mark_values_as_unloaded,
      :mark_values_as_unloaded
    )
    game_singleton.send(
      :alias_method, :run_transition_original_load, :load
    )
    calls = []
    save_data_singleton.send(:define_method, :mark_values_as_unloaded) do
      calls << :unload
    end
    game_singleton.send(:define_method, :load) do |save_data|
      calls << [:load, save_data, Ironmon.checkpoint_reset_loading?]
      true
    end
    snapshot = { :marker => :live_attempt }
    result = Ironmon.restore_live_save_snapshot(snapshot)
    assert(!result, "live snapshot restoration succeeds without an error")
    assert(
      calls == [:unload, [:load, snapshot, false]],
      "rollback uses the normal load hooks so generators are restored"
    )
  ensure
    save_data_singleton.send(
      :alias_method, :mark_values_as_unloaded,
      :run_transition_original_mark_values_as_unloaded
    )
    save_data_singleton.send(
      :remove_method, :run_transition_original_mark_values_as_unloaded
    )
    game_singleton.send(
      :alias_method, :load, :run_transition_original_load
    )
    game_singleton.send(:remove_method, :run_transition_original_load)
  end

  def self.test_checkpoint_load_failure_is_caught
    ironmon_singleton = class << Ironmon; self; end
    save_data_singleton = class << SaveData; self; end
    ironmon_singleton.send(
      :alias_method, :run_transition_original_checkpoint_load,
      :with_checkpoint_reset_load
    )
    ironmon_singleton.send(
      :alias_method, :run_transition_original_fail_pending_reset,
      :fail_pending_reset
    )
    save_data_singleton.send(
      :alias_method, :run_transition_original_scene_unload,
      :mark_values_as_unloaded
    )
    failure = nil
    ironmon_singleton.send(:define_method, :with_checkpoint_reset_load) do
      raise "injected checkpoint load failure"
    end
    ironmon_singleton.send(:define_method, :fail_pending_reset) do |error|
      failure = error.message
      false
    end
    save_data_singleton.send(:define_method, :mark_values_as_unloaded) { nil }
    IronmonCheckpointLoadScene.new({}, {}, {}).main
    assert(
      failure == "injected checkpoint load failure",
      "checkpoint load failures enter the transaction rollback path"
    )
  ensure
    ironmon_singleton.send(
      :alias_method, :with_checkpoint_reset_load,
      :run_transition_original_checkpoint_load
    )
    ironmon_singleton.send(
      :remove_method, :run_transition_original_checkpoint_load
    )
    ironmon_singleton.send(
      :alias_method, :fail_pending_reset,
      :run_transition_original_fail_pending_reset
    )
    ironmon_singleton.send(
      :remove_method, :run_transition_original_fail_pending_reset
    )
    save_data_singleton.send(
      :alias_method, :mark_values_as_unloaded,
      :run_transition_original_scene_unload
    )
    save_data_singleton.send(
      :remove_method, :run_transition_original_scene_unload
    )
  end

  def self.test_checkpoint_load_skips_saved_run_migration
    ironmon_singleton = class << Ironmon; self; end
    ironmon_singleton.send(
      :alias_method, :run_transition_original_migration_issues,
      :saved_run_migration_issues
    )
    ironmon_singleton.send(
      :alias_method, :run_transition_original_migration_confirmation,
      :confirm_saved_run_migration
    )
    calls = []
    ironmon_singleton.send(:define_method, :saved_run_migration_issues) do |_data|
      calls << :issues
      [:ability_randomization]
    end
    ironmon_singleton.send(:define_method, :confirm_saved_run_migration) do |*|
      calls << :confirmation
      true
    end
    result = Ironmon.with_checkpoint_reset_load do
      Ironmon.begin_saved_run_migration({ :checkpoint => true })
    end
    assert(result, "internal checkpoint loading permits the baseline save")
    assert(
      calls.empty?,
      "internal checkpoint loading does not evaluate or display migration"
    )
  ensure
    ironmon_singleton.send(
      :alias_method, :saved_run_migration_issues,
      :run_transition_original_migration_issues
    )
    ironmon_singleton.send(
      :remove_method, :run_transition_original_migration_issues
    )
    ironmon_singleton.send(
      :alias_method, :confirm_saved_run_migration,
      :run_transition_original_migration_confirmation
    )
    ironmon_singleton.send(
      :remove_method, :run_transition_original_migration_confirmation
    )
  end

  def self.run
    test_ledger_completion_staging
    test_active_attempt_does_not_recover_completed_recipe
    test_active_run_preparation_boundary
    test_generation_failure_rollback
    test_save_failure_rollback
    test_successful_reset_commit_order
    test_automatic_reset_does_not_republish_loss
    test_live_snapshot_restore_uses_normal_load
    test_checkpoint_load_failure_is_caught
    test_checkpoint_load_skips_saved_run_migration
    File.binwrite(OUTPUT_PATH, "run-transition runtime tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonRunTransitionRuntimeTests.run
