module IronmonGameplayPerformanceTests
  def self.assert(condition, message)
    raise "Gameplay hook validation failed: #{message}" if !condition
  end

  def self.test_callbacks
    calls = []
    result = Object.new
    Ironmon.instance_variable_set(:@graphics_update_hooks, [
      { :name => :first, :callback => proc { calls << :first; :earlier_result } },
      { :name => :second, :callback => proc { calls << :second; result } }
    ])
    assert(Ironmon.run_graphics_update_hooks.equal?(result),
      "graphics updates return the final callback's original result")
    assert(calls == [:first, :second], "each callback runs once in registration order")
    Ironmon.run_graphics_update_hooks
    assert(calls == [:first, :second, :first, :second],
      "the next gameplay update invokes the registered callbacks again")
    Ironmon.instance_variable_set(:@graphics_update_hooks, [])
    assert(Ironmon.run_graphics_update_hooks.nil?, "an empty hook list returns nil")
  end

  def self.test_exception
    failure = RuntimeError.new("expected hook interruption")
    later_called = false
    Ironmon.instance_variable_set(:@graphics_update_hooks, [
      { :name => :interrupted, :callback => proc { raise failure } },
      { :name => :later, :callback => proc { later_called = true } }
    ])
    begin
      Ironmon.run_graphics_update_hooks
      raise "The hook interruption was swallowed"
    rescue RuntimeError => error
      assert(error.equal?(failure), "a callback's original exception propagates unchanged")
    end
    assert(!later_called, "a failed callback does not execute later hooks")
  end

  def self.test_startup_gate
    called = false
    Ironmon.instance_variable_set(:@graphics_update_hooks, [
      { :name => :gameplay, :callback => proc { called = true; :resumed } }
    ])
    Ironmon::StartupPreparation.instance_variable_set(:@active, true)
    assert(Ironmon.run_graphics_update_hooks.nil? && !called,
      "startup preparation suppresses gameplay hooks")
    Ironmon::StartupPreparation.instance_variable_set(:@active, false)
    assert(Ironmon.run_graphics_update_hooks == :resumed && called,
      "gameplay hooks resume after preparation completes")
  end

  def self.run
    hooks = Ironmon.instance_variable_get(:@graphics_update_hooks)
    preparing = Ironmon::StartupPreparation.instance_variable_get(:@active)
    Ironmon::StartupPreparation.instance_variable_set(:@active, false)
    test_callbacks
    test_exception
    test_startup_gate
    puts "Gameplay hooks: callback order, return values, exceptions and startup gating passed"
  ensure
    Ironmon.instance_variable_set(:@graphics_update_hooks, hooks)
    Ironmon::StartupPreparation.instance_variable_set(:@active, preparing)
  end
end

IronmonGameplayPerformanceTests.run
