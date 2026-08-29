#===============================================================================
# Ironmon 0.7.3 early-run gameplay adjustments
#===============================================================================

module Ironmon
  OLDALE_ROUTE_102_REWARD_MAP_ID = 8
  OLDALE_ROUTE_102_REWARD_EVENT_ID = 17
  STARTER_ENCOUNTER_MAP_ID = 5
  STARTER_ENCOUNTER_EVENT_ID = 2
  STARTER_BST_MINIMUM = 30
  STARTER_BST_MAXIMUM = 1_530

  class StarterBstCeilingExceeded < StandardError; end

  def self.heal_pokemon_fully(pokemon)
    return false if !pokemon
    pokemon.heal
    return true
  end

  def self.first_rival_battle_active?
    return false if !active? || !$PokemonGlobal
    trainers = $PokemonGlobal.battledTrainers
    return !trainers || !trainers.has_key?(BATTLED_TRAINER_RIVAL_KEY)
  end

  def self.heal_after_first_rival_victory(won, first_battle)
    return false if !won || !first_battle || !$PokemonGlobal
    return false if $PokemonGlobal.ironmon_first_rival_heal_applied
    pokemon = @tracker_player_pokemon
    pokemon ||= $Trainer ? $Trainer.first_pokemon : nil
    return false if !heal_pokemon_fully(pokemon)
    $PokemonGlobal.ironmon_first_rival_heal_applied = true
    return true
  end

  def self.remove_default_pc_potion
    return false if !active? || !$PokemonGlobal
    storage = $PokemonGlobal.pcItemStorage
    return true if !storage
    return true if storage.pbQuantity(:POTION) <= 0
    return storage.pbDeleteItem(:POTION, 1)
  end

  def self.grant_starter_encounter_item
    return false if !$PokemonGlobal
    return false if $PokemonGlobal.ironmon_starter_item_reward_applied
    received = if item_randomization_active?
                 slot = ItemSlotGenerator::SPECIAL_GROUND_SLOTS[
                   :starter_rescue_reward
                 ]
                 reward = resolve_ground_reward(slot[1], slot[0])
                 pbReceiveItem(reward, 1, "", nil, false)
               else
                 pbReceiveItem(:POTION, 1)
               end
    return false if !received
    $PokemonGlobal.ironmon_starter_item_reward_applied = true
    return true
  end

  def self.route_102_reward_event?
    return false if !active?
    return current_area_event_reference == [
      OLDALE_ROUTE_102_REWARD_MAP_ID, OLDALE_ROUTE_102_REWARD_EVENT_ID
    ]
  end

  def self.starter_encounter_event?
    return false if !active?
    return current_area_event_reference == [
      STARTER_ENCOUNTER_MAP_ID, STARTER_ENCOUNTER_EVENT_ID
    ]
  end

  def self.valid_starter_bst_ceiling(value)
    return nil if !value.is_a?(Integer)
    return nil if value < STARTER_BST_MINIMUM ||
                  value > STARTER_BST_MAXIMUM
    return value
  end

  def self.queue_starter_bst_reset
    @starter_bst_reset_queued = true
  end

  def self.finish_queued_starter_bst_reset
    return false if !@starter_bst_reset_queued
    @starter_bst_reset_queued = false
    return start_checkpoint_reset(true)
  end
end

alias ironmon_073_original_hoenn_select_starter hoennSelectStarter
def hoennSelectStarter
  starter = ironmon_073_original_hoenn_select_starter
  Ironmon.arm_starter_rescue_battle if Ironmon.active? && starter
  return starter
rescue Ironmon::StarterBstCeilingExceeded
  Ironmon.queue_starter_bst_reset
  return nil
end

alias ironmon_073_original_hoenn_select_custom_starter hoennSelectCustomStarter
def hoennSelectCustomStarter
  starter = ironmon_073_original_hoenn_select_custom_starter
  Ironmon.arm_starter_rescue_battle if Ironmon.active? && starter
  return starter
end

alias ironmon_073_original_pb_wild_battle pbWildBattle
def pbWildBattle(*args)
  starter_encounter = Ironmon.starter_rescue_battle_pending? ||
    Ironmon.starter_encounter_event?
  won = ironmon_073_original_pb_wild_battle(*args)
  Ironmon.finish_starter_rescue_battle if starter_encounter
  reset_started = Ironmon.reset_after_early_game_loss(
    won, starter_encounter
  )
  Ironmon.queue_early_game_conditional_event_abort if
    !won && starter_encounter
  if won && starter_encounter
    Ironmon.heal_pokemon_fully(pbGet(VAR_HOENN_STARTER))
    Ironmon.grant_starter_encounter_item
  end
  return false if reset_started
  return won
end

module Ironmon
  class << self
    alias ironmon_073_original_apply_preset apply_preset
    def apply_preset(context = :new_run, seed_override = nil, start_tracker = true, expected_compatibility_fingerprint = nil)
      result = ironmon_073_original_apply_preset(context, seed_override, start_tracker, expected_compatibility_fingerprint)
      remove_default_pc_potion if result
      return result
    end
  end
end

class PCItemStorage
  alias ironmon_073_original_initialize initialize
  def initialize
    ironmon_073_original_initialize
    pbDeleteItem(:POTION, 1) if Ironmon.active? && pbQuantity(:POTION) > 0
  end
end

alias ironmon_073_original_hoenn_rival_battle hoennRivalBattle
def hoennRivalBattle(loseDialog = "...", canLose = false, items = [])
  first_battle = Ironmon.first_rival_battle_active?
  won = ironmon_073_original_hoenn_rival_battle(
    loseDialog, canLose, items
  )
  reset_started = Ironmon.reset_after_early_game_loss(won, first_battle)
  Ironmon.abort_current_early_game_event if !won && first_battle
  Ironmon.heal_after_first_rival_victory(won, first_battle)
  return false if reset_started
  return won
end

alias ironmon_073_original_pb_receive_item pbReceiveItem
def pbReceiveItem(item, quantity = 1, item_name = "", music = nil,
                  canRandom = true)
  if item == :DNASPLICERS && Ironmon.route_102_reward_event?
    item = :POKEBALL
    quantity = 1
    item_name = ""
  end
  return ironmon_073_original_pb_receive_item(
    item, quantity, item_name, music, canRandom
  )
end

class Interpreter
  alias ironmon_073_original_command_111 command_111
  def command_111
    result = ironmon_073_original_command_111
    return result if
      !Ironmon.consume_queued_early_game_conditional_event_abort
    Ironmon.abort_current_early_game_event
    return false
  end

  alias ironmon_073_original_command_101 command_101
  def command_101
    return ironmon_073_original_command_101 if
      !Ironmon.route_102_reward_event?
    changed = []
    replacements = {
      "It turns out that they sell \\C[3]DNA Splicers\\C[0] at " =>
        "I picked up a \\C[3]Poké Ball\\C[0] for you while I was here!",
      "any Pokémart now! I got you a pair too!" => "",
      "They're one-time use, so you should make sure to " =>
        "It should help you catch a new teammate on Route 102!",
      "stock up and get some more!" => ""
    }
    index = @index
    loop do
      command = @list[index]
      break if !command || ![101, 401].include?(command.code)
      text = command.parameters[0]
      if replacements.key?(text)
        changed << [command, text]
        command.parameters[0] = replacements[text]
      end
      index = pbNextIndex(index)
    end
    return ironmon_073_original_command_101
  ensure
    changed.each { |entry| entry[0].parameters[0] = entry[1] } if changed
  end
end

Ironmon.register_graphics_update_hook(
  :starter_bst_reset,
  proc { Ironmon.finish_queued_starter_bst_reset }
)
