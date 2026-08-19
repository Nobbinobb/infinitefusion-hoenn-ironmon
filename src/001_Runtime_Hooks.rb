#===============================================================================
# Ironmon centralized game-runtime lifecycle hooks
#===============================================================================

module Ironmon
  @game_load_hooks = []
  @game_save_hooks = []
  @graphics_update_hooks = []

  def self.register_game_load_hook(name, before_load = nil, after_load = nil)
    raise "the game-load hook name is already registered: #{name}" if
      @game_load_hooks.any? { |hook| hook[:name] == name }
    @game_load_hooks << {
      :name => name,
      :before => before_load,
      :after => after_load
    }
  end

  def self.register_game_save_hook(name, before_save = nil, after_save = nil)
    raise "the game-save hook name is already registered: #{name}" if
      @game_save_hooks.any? { |hook| hook[:name] == name }
    @game_save_hooks << {
      :name => name,
      :before => before_save,
      :after => after_save
    }
  end

  def self.register_graphics_update_hook(name, callback)
    raise "the graphics-update hook name is already registered: #{name}" if
      @graphics_update_hooks.any? { |hook| hook[:name] == name }
    @graphics_update_hooks << { :name => name, :callback => callback }
  end

  def self.game_load_hook_names
    return @game_load_hooks.map { |hook| hook[:name] }
  end

  def self.game_save_hook_names
    return @game_save_hooks.map { |hook| hook[:name] }
  end

  def self.graphics_update_hook_names
    return @graphics_update_hooks.map { |hook| hook[:name] }
  end

  def self.run_game_load_hooks(save_data)
    hook_states = {}
    @game_load_hooks.reverse_each do |hook|
      hook_states[hook[:name]] = hook[:before].call(save_data) if hook[:before]
    end
    result = yield
    @game_load_hooks.each do |hook|
      hook[:after].call(save_data, result, hook_states[hook[:name]]) if
        hook[:after]
    end
    return result
  end

  def self.run_game_save_hooks(slot, auto, safe)
    hook_states = {}
    @game_save_hooks.reverse_each do |hook|
      hook_states[hook[:name]] = hook[:before].call(slot, auto, safe) if
        hook[:before]
    end
    result = yield
    @game_save_hooks.each do |hook|
      hook[:after].call(
        slot, auto, safe, result, hook_states[hook[:name]]
      ) if hook[:after]
    end
    return result
  end

  def self.run_graphics_update_hooks
    result = nil
    @graphics_update_hooks.each do |hook|
      result = hook[:callback].call
    end
    return result
  end
end

module Game
  class << self
    alias ironmon_runtime_hooks_original_load load
    def load(save_data)
      return Ironmon.run_game_load_hooks(save_data) do
        ironmon_runtime_hooks_original_load(save_data)
      end
    end

    alias ironmon_runtime_hooks_original_save save
    def save(slot = nil, auto = false, safe: false)
      return Ironmon.run_game_save_hooks(slot, auto, safe) do
        ironmon_runtime_hooks_original_save(slot, auto, safe: safe)
      end
    end
  end
end

module Graphics
  class << self
    alias ironmon_runtime_hooks_original_update update
    def update
      ironmon_runtime_hooks_original_update
      return Ironmon.run_graphics_update_hooks
    end
  end
end
