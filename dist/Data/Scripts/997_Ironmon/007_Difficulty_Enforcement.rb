#===============================================================================
# Ironmon difficulty enforcement and level scaling
#===============================================================================

module Ironmon
  LEVEL_MULTIPLIER = 1.6
  MAX_SCALED_LEVEL = 100

  LOCKED_GAMEPLAY_OPTIONS = [
    _INTL("Difficulty"),
    _INTL("Battle type")
  ].freeze
  LOCKED_CHALLENGE_OPTIONS = [
    _INTL("Level caps"),
    _INTL("No reviving"),
    _INTL("No heals (overworld)")
  ].freeze
  DIFFICULTY_COMMON_EVENT_NAMES = [
    "game difficulty selection",
    "game difficulty selection_hoenn"
  ].freeze

  def self.scaled_level(original_level)
    scaled = (original_level.to_i * LEVEL_MULTIPLIER).ceil
    return [[scaled, 1].max, MAX_SCALED_LEVEL].min
  end

  def self.scale_battle_pokemon(pokemon)
    return pokemon if !active? || !pokemon
    return pokemon if pokemon.instance_variable_get(:@ironmon_level_scaled)
    pokemon.level = scaled_level(pokemon.level)
    pokemon.calc_stats
    pokemon.instance_variable_set(:@ironmon_level_scaled, true)
    return pokemon
  end

  def self.scale_trainer_party(trainer)
    return trainer if !active? || !trainer || !trainer.party
    trainer.party.each { |pokemon| scale_battle_pokemon(pokemon) }
    return trainer
  end

  def self.enforce_difficulty_settings
    return false if !active?
    if $game_switches
      $game_switches[SWITCH_GAME_DIFFICULTY_EASY] = false
      $game_switches[SWITCH_GAME_DIFFICULTY_HARD] = true
    end
    if $Trainer
      $Trainer.selected_difficulty = 2
      $Trainer.lowest_difficulty = 2
    end
    if $PokemonSystem
      $PokemonSystem.battle_type = 0
      $PokemonSystem.level_caps = 0
      $PokemonSystem.no_reviving = true
      $PokemonSystem.no_healing_items_ow = true
    end
    if $game_variables
      $game_variables[VAR_DEFAULT_BATTLE_TYPE] = [1, 1]
    end
    return true
  end

  def self.remove_locked_options(options, locked_names)
    return options if !active?
    return options.reject do |option|
      option.respond_to?(:name) && locked_names.include?(option.name)
    end
  end

  def self.difficulty_selection_common_event?(event_id)
    return false if !$data_common_events
    event = $data_common_events[event_id]
    return false if !event
    return DIFFICULTY_COMMON_EVENT_NAMES.include?(event.name)
  end
end

# Keep direct assignments from menus or event scripts from changing the locked
# challenge settings while an Ironmon save is active.
class PokemonSystem
  def battle_type=(value)
    @battle_type = Ironmon.active? ? 0 : value
  end

  def level_caps=(value)
    @level_caps = Ironmon.active? ? 0 : value
  end

  def no_reviving=(value)
    @no_reviving = Ironmon.active? ? true : value
  end

  def no_healing_items_ow=(value)
    @no_healing_items_ow = Ironmon.active? ? true : value
  end

end

alias ironmon_original_set_difficulty setDifficulty
def setDifficulty(index)
  return ironmon_original_set_difficulty(2) if Ironmon.active?
  return ironmon_original_set_difficulty(index)
end

class Interpreter
  alias ironmon_original_command_117 command_117
  def command_117
    if Ironmon.active? &&
       Ironmon.difficulty_selection_common_event?(@parameters[0])
      Ironmon.enforce_difficulty_settings
      return true
    end
    return ironmon_original_command_117
  end
end

class GameplayOptionsScene
  alias ironmon_original_pb_get_gameplay_options pbGetOptions
  def pbGetOptions(inloadscreen = false)
    options = ironmon_original_pb_get_gameplay_options(inloadscreen)
    return Ironmon.remove_locked_options(
      options, Ironmon::LOCKED_GAMEPLAY_OPTIONS
    )
  end
end

class ChallengeOptionsScene
  alias ironmon_original_pb_get_challenge_options pbGetOptions
  def pbGetOptions(inloadscreen = false)
    options = ironmon_original_pb_get_challenge_options(inloadscreen)
    return Ironmon.remove_locked_options(
      options, Ironmon::LOCKED_CHALLENGE_OPTIONS
    )
  end
end

module GameData
  class Trainer
    alias ironmon_difficulty_original_to_trainer to_trainer
    def to_trainer
      return ironmon_difficulty_original_to_trainer if !Ironmon.active?

      # The base method applies Hard Mode's 1.2 multiplier while constructing
      # ordinary parties. Suppress only that calculation, then restore Hard
      # Mode before the trainer can enter battle so its AI remains enabled.
      begin
        $game_switches[SWITCH_GAME_DIFFICULTY_HARD] = false
        trainer = ironmon_difficulty_original_to_trainer
      ensure
        $game_switches[SWITCH_GAME_DIFFICULTY_EASY] = false
        $game_switches[SWITCH_GAME_DIFFICULTY_HARD] = true
      end
      return Ironmon.scale_trainer_party(trainer)
    end
  end
end

Events.onWildPokemonCreate += proc do |_sender, event_args|
  Ironmon.enforce_difficulty_settings if Ironmon.active?
  Ironmon.scale_battle_pokemon(event_args[0])
end

Events.onTrainerPartyLoad += proc do |_sender, event_args|
  Ironmon.enforce_difficulty_settings if Ironmon.active?
  Ironmon.scale_trainer_party(event_args[0])
end

Events.onStartBattle += proc do |_sender, _event_args|
  Ironmon.enforce_difficulty_settings if Ironmon.active?
end

module Game
  class << self
    alias ironmon_difficulty_original_load load
    def load(save_data)
      result = ironmon_difficulty_original_load(save_data)
      return result if Ironmon.checkpoint_reset_loading?
      Ironmon.enforce_difficulty_settings if Ironmon.active?
      return result
    end
  end
end
