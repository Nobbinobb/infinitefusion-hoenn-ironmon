module IronmonTrainerRematchRuntimeTests
  OUTPUT_PATH = $ironmon_trainer_rematch_test_output_path.to_s

  def self.assert(condition, message)
    raise "Trainer rematch runtime test failed: #{message}" if !condition
  end

  def self.with_ironmon_active(value)
    singleton = class << Ironmon; self; end
    singleton.send(
      :alias_method, :trainer_rematch_original_active,
      :active?
    )
    singleton.send(:define_method, :active?) { value }
    return yield
  ensure
    if singleton && singleton.method_defined?(:trainer_rematch_original_active)
      singleton.send(:alias_method, :active?, :trainer_rematch_original_active)
      singleton.send(:remove_method, :trainer_rematch_original_active)
    end
  end

  def self.test_scope
    with_ironmon_active(true) do
      assert(
        Ironmon.rematch_battles_blocked?,
        "rematch battles are blocked during an active challenge"
      )
    end
    with_ironmon_active(false) do
      assert(
        !Ironmon.rematch_battles_blocked?,
        "rematch battles retain their normal behavior outside Ironmon"
      )
    end
  end

  def self.test_battle_action_is_stopped
    messages = []
    singleton = class << Ironmon; self; end
    singleton.send(:define_method, :pbMessage) do |message|
      messages << message
    end
    begin
      result = with_ironmon_active(true) do
        catch(Ironmon::REMATCH_BLOCK_TAG) do
          doPostBattleAction(:BATTLE, Object.new)
          :battle_started
        end
      end
    ensure
      singleton.send(:remove_method, :pbMessage)
    end
    assert(result == true, "the rematch action exits before battle setup")
    assert(
      messages == [Ironmon::REMATCH_BLOCKED_MESSAGE],
      "the trainer explains why the rematch is deferred"
    )
  end

  def self.run
    test_scope
    test_battle_action_is_stopped
    File.binwrite(OUTPUT_PATH, "trainer rematch runtime tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonTrainerRematchRuntimeTests.run
