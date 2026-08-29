#===============================================================================
# Ironmon game-mode menu and preset activation
#===============================================================================

module Ironmon
  MODE_ENTRY_MAP_ID = 295
  MODE_ENTRY_EVENT_ID = 1
  MODE_ENTRY_EVENT_NAME = "Intro"
  MODE_ENTRY_SELECTION_SCRIPT = "select_game_mode"
  MODE_ENTRY_CONDITION_SCRIPT =
    "Ironmon.mode_available? || $game_switches[SWITCH_NEW_GAME_PLUS]"

  def self.patch_mode_entry_map(map_id, map)
    return false if map_id != MODE_ENTRY_MAP_ID || !map || !map.events
    event = map.events[MODE_ENTRY_EVENT_ID]
    return false if !event || event.name != MODE_ENTRY_EVENT_NAME ||
                    event.pages.empty?
    commands = event.pages[0].list
    selection_index = commands.index do |command|
      command.code == 355 &&
        command.parameters[0] == MODE_ENTRY_SELECTION_SCRIPT
    end
    return false if !selection_index || selection_index == 0
    condition = commands[selection_index - 1]
    return false if condition.code != 111 ||
                    condition.parameters != [0, SWITCH_NEW_GAME_PLUS, 0]
    condition.parameters = [12, MODE_ENTRY_CONDITION_SCRIPT]
    return true
  end
end

def select_game_mode
  if Ironmon.consume_mode_selection_skip
    return :IRONMON
  end

  game_mode = nil
  cmd_mode_classic = _INTL("Classic")
  cmd_mode_remix = _INTL("Remix Mode")
  cmd_mode_random = _INTL("Randomized Mode")
  cmd_mode_ironmon = _INTL("Ironmon")
  cmd_mode_legendary = _INTL("Legendary Mode")
  cmd_mode_expert = _INTL("Expert Mode")

  commands = [cmd_mode_classic]
  commands << cmd_mode_remix if Settings::KANTO
  commands << cmd_mode_random if Ironmon.randomized_mode_available?
  commands << cmd_mode_ironmon if Ironmon.mode_available?
  commands << cmd_mode_legendary if Settings::KANTO && $Trainer.new_game_plus_unlocked

  until game_mode
    chosen_index = pbMessage(_INTL("Which mode would you like to play?"), commands)
    case commands[chosen_index]
    when cmd_mode_classic
      choices = [_INTL("Back"), _INTL("Play Classic Mode")]
      text = _INTL("\\C[1]Classic\\C[0] is the default game mode. All player teams and encounters are based on the original games. Every Pokemon is still available.")
      game_mode = :CLASSIC if pbMessage(text, choices) == 1
    when cmd_mode_remix
      choices = [_INTL("Back"), _INTL("Play Remix Mode")]
      text = _INTL("\\C[1]Remix mode\\C[0] changes trainer teams and wild encounters to showcase more Pokemon from newer generations.")
      game_mode = :REMIX if pbMessage(text, choices) == 1
    when cmd_mode_random
      choices = [_INTL("Back"), _INTL("Play Randomized Mode")]
      text = _INTL("In \\C[1]Randomized mode\\C[0], trainers, wild encounters and items can be randomized with custom settings.")
      game_mode = :RANDOMIZED if pbMessage(text, choices) == 1
    when cmd_mode_ironmon
      choices = [_INTL("Back"), _INTL("Play Ironmon")]
      text = _INTL("\\C[1]Ironmon\\C[0] starts with Pokemon, trainers and items fully randomized. Press F7 to return to the starter selection with a fresh randomization.")
      if pbMessage(text, choices) == 1
        stored_configuration = if $PokemonGlobal
                                 $PokemonGlobal.ironmon_configuration
                               else
                                 nil
                               end
        selected_configuration = Ironmon::ConfigurationScreen.new(
          stored_configuration
        ).run
        if selected_configuration
          if !Ironmon.prepare_custom_fusion_pool
            pbMessage(Ironmon.custom_fusion_pool_error_message)
            next
          end
          Ironmon.configuration = selected_configuration
          game_mode = :IRONMON
        end
      end
    when cmd_mode_legendary
      choices = [_INTL("Back"), _INTL("Play Legendary Mode")]
      text = _INTL("In \\C[1]Legendary mode\\C[0], every trainer Pokemon is fused with a legendary Pokemon and you receive legendary eggs and a legendary starter.")
      game_mode = :LEGENDARY if pbMessage(text, choices) == 1
    when cmd_mode_expert
      choices = [_INTL("Back"), _INTL("Play Expert Mode")]
      text = _INTL("\\C[1]Expert mode\\C[0] changes trainer teams to make them much more challenging.")
      game_mode = :EXPERT if pbMessage(text, choices) == 1
    end
  end

  apply_game_mode(game_mode)
  return game_mode
end

alias ironmon_original_apply_game_mode apply_game_mode
def apply_game_mode(game_mode)
  if game_mode == :IRONMON
    if !Ironmon.apply_preset
      pbMessage(Ironmon.generation_error_message)
    end
    return
  end
  ironmon_original_apply_game_mode(game_mode)
end

Events.onMapCreate += proc do |_sender, event|
  Ironmon.patch_mode_entry_map(event[0], event[1])
end
