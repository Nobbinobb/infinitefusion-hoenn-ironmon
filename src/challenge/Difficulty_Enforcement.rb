#===============================================================================
# Ironmon difficulty enforcement and level scaling
#===============================================================================

module Ironmon
  LEVEL_MULTIPLIER = 1.5
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
    scaled = original_level.to_i * LEVEL_MULTIPLIER
    rounded = scaled.floor
    rounded += 1 if scaled != rounded && rounded.odd?
    return [[rounded, 1].max, MAX_SCALED_LEVEL].min
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

  def self.optional_trainer_reference(script)
    text = script.to_s
    scaled_match = /scaledLevelBattle\(\s*:([A-Za-z0-9_]+)\s*,\s*(["'])(.*?)\2/
      .match(text)
    if scaled_match
      return [scaled_match[1].to_sym, scaled_match[3], 0, :player_highest]
    end
    tutor_match = /pbMoveTutorBattle\(\s*:([A-Za-z0-9_]+)\s*,\s*(["'])(.*?)\2\s*,\s*(?:\[[^\]]*\]|pbGet\([^)]*\)|:[A-Za-z0-9_]+)\s*(?:,\s*(true|false))?/
      .match(text)
    if tutor_match
      scaling = tutor_match[4].to_s != "false"
      return [
        tutor_match[1].to_sym, tutor_match[3], 0,
        scaling ? :player_highest : nil
      ]
    end
    match = /pbTrainerBattle\(\s*:([A-Za-z0-9_]+)\s*,\s*(["'])(.*?)\2([^)]*)\)/
      .match(text)
    return nil if !match
    trailing_arguments = match[4].to_s.sub(/\A\s*,/, "").split(/\s*,\s*/)
    party_id = trailing_arguments[2].to_s.match?(/\A\d+\z/) ?
      trailing_arguments[2].to_i : 0
    return [match[1].to_sym, match[3], party_id]
  end

  def self.optional_trainer_minimum_level(reference)
    return nil if !reference
    if reference[3] == :player_highest
      return nil if !$Trainer
      level = $Trainer.highest_level_pokemon_in_party.to_i
      return level > 0 ? scaled_level(level) : nil
    end
    trainer_data_mode = getTrainersDataMode
    trainer = trainer_data_mode.try_get(
      reference[0], reference[1], reference[2]
    )
    if !trainer && trainer_data_mode != GameData::Trainer
      trainer = GameData::Trainer.try_get(
        reference[0], reference[1], reference[2]
      )
    end
    return nil if !trainer || trainer.pokemon.empty?
    levels = trainer.pokemon.map { |pokemon| pokemon[:level] }.compact
    return nil if levels.empty?
    return levels.map { |level| scaled_level(level) }.min
  rescue Exception => error
    echoln "Ironmon optional trainer warning failed: #{error.message}"
    return nil
  end

  def self.optional_trainer_choice_labels(list, choice_index)
    command = list[choice_index]
    return nil if !command
    return command.parameters[0] if !active? || command.code != 102
    choices = command.parameters[0]
    return choices if !choices.is_a?(Array)
    yes_index = choices.index do |choice|
      choice.to_s.strip.casecmp("Yes") == 0
    end
    return choices if !yes_index
    branch_index = ((choice_index + 1)...list.length).find do |index|
      candidate = list[index]
      candidate.code == 402 && candidate.indent == command.indent &&
        candidate.parameters[0] == yes_index
    end
    return choices if !branch_index
    branch_end = ((branch_index + 1)...list.length).find do |index|
      candidate = list[index]
      [402, 403, 404].include?(candidate.code) &&
        candidate.indent == command.indent
    end || list.length
    script = list[(branch_index + 1)...branch_end].flat_map do |candidate|
      candidate.parameters.select { |parameter| parameter.is_a?(String) }
    end.join(" ")
    minimum_level = optional_trainer_minimum_level(
      optional_trainer_reference(script)
    )
    return choices if !minimum_level
    decorated = choices.dup
    decorated[yes_index] = _INTL(
      "{1} (lowest Lv. {2})", choices[yes_index], minimum_level
    )
    return decorated
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
  alias ironmon_original_command_101 command_101
  def command_101
    choice_index = nil
    scan_index = @index
    loop do
      next_index = pbNextIndex(scan_index)
      candidate = @list[next_index]
      break if !candidate
      if candidate.code == 401
        scan_index = next_index
        next
      end
      choice_index = next_index if candidate.code == 102
      break
    end
    return ironmon_original_command_101 if !choice_index
    choice_command = @list[choice_index]
    choices = choice_command.parameters[0]
    decorated = Ironmon.optional_trainer_choice_labels(@list, choice_index)
    return ironmon_original_command_101 if decorated.equal?(choices)
    choice_command.parameters[0] = decorated
    return ironmon_original_command_101
  ensure
    choice_command.parameters[0] = choices if choice_command && choices
  end

  alias ironmon_original_command_102 command_102
  def command_102
    choices = @list[@index].parameters[0]
    decorated = Ironmon.optional_trainer_choice_labels(@list, @index)
    return ironmon_original_command_102 if decorated.equal?(choices)
    @list[@index].parameters[0] = decorated
    return ironmon_original_command_102
  ensure
    @list[@index].parameters[0] = choices if choices
  end

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

Ironmon.register_game_load_hook(
  :difficulty_enforcement, nil,
  proc do |_save_data, _result|
    next if Ironmon.checkpoint_reset_loading?
    Ironmon.enforce_difficulty_settings if Ironmon.active?
  end
)
