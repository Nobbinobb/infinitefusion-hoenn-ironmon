# encoding: UTF-8
#===============================================================================
# Ironmon's once-per-attempt Pokemon Center welcome gift
#===============================================================================

class PokemonGlobalMetadata
  attr_accessor :ironmon_pokecenter_birthday_run_id
end

module Ironmon
  POKECENTER_BIRTHDAY_GIFT_VERSION = 1
  POKECENTER_BIRTHDAY_GIFT_NAMESPACE = "pokecenter_birthday_gift"
  POKECENTER_BIRTHDAY_GIFT_SCRIPT = "Ironmon.receive_pokecenter_birthday_gift(@event_id)"
  POKECENTER_BIRTHDAY_NURSE_EVENT_ID = 7
  POKECENTER_BIRTHDAY_EXTRA_ITEMS = [
    :FIGYBERRY, :WIKIBERRY, :MAGOBERRY, :AGUAVBERRY, :IAPAPABERRY,
    :ENIGMABERRY, :REVIVE, :MAXREVIVE, :REVIVALHERB, :SACREDASH,
    :BANANA, :POISONMUSHROOM, :COFFEE, :ROCKETMEAL, :FANCYMEAL
  ].freeze
  POKECENTER_BIRTHDAY_REUSABLE_ITEMS = [
    :BLUEFLUTE, :REDFLUTE, :YELLOWFLUTE
  ].freeze

  def self.pokecenter_birthday_gift_pool
    candidates = ItemSlotGenerator::HP_RECOVERY_ITEMS +
      ItemSlotGenerator::STATUS_RECOVERY_ITEMS +
      ItemSlotGenerator::PP_RECOVERY_ITEMS + POKECENTER_BIRTHDAY_EXTRA_ITEMS
    return candidates.filter_map do |id|
      item = GameData::Item.try_get(id)
      item.id if item && !item.is_key_item? &&
        !POKECENTER_BIRTHDAY_REUSABLE_ITEMS.include?(item.id)
    end.uniq.sort_by { |id| id.to_s }
  end

  def self.pokecenter_birthday_gift_item(seed)
    pool = pokecenter_birthday_gift_pool
    raise ItemRandomizationError, "the Pokemon Center gift pool is empty" if
      pool.empty?
    value = fnv1a_64_joined([
      POKECENTER_BIRTHDAY_GIFT_VERSION, seed.to_i,
      POKECENTER_BIRTHDAY_GIFT_NAMESPACE
    ])
    return pool[value % pool.length]
  end

  def self.pokecenter_birthday_nurse_message(event_id, message, choices = nil)
    pbCallBub(2, event_id, true)
    return pbMessage(message, choices, 2)
  end

  def self.receive_pokecenter_birthday_gift(event_id = POKECENTER_BIRTHDAY_NURSE_EVENT_ID)
    return false if !active?
    attempt = current_run_attempt
    return false if !attempt || attempt["result"] != "active"
    run_id = attempt["run_id"].to_s
    return false if run_id.empty? ||
      $PokemonGlobal.ironmon_pokecenter_birthday_run_id == run_id
    item = pokecenter_birthday_gift_item(attempt["seed"])
    answer = pokecenter_birthday_nurse_message(
      event_id, _INTL("Oh... the system is showing a birthday notification. Is today your birthday?"),
      [_INTL("Yes"), _INTL("No")]
    )
    responses = if answer == 0
                  [
                    _INTL("Wonderful! Happy birthday, \\PN!"),
                    _INTL("It looks like the system has a little present waiting for you. You registered just in time!")
                  ]
                else
                  [
                    _INTL("No? But it's right here in the system... I've only just entered your name!"),
                    _INTL("Well, it's already approved your present. I'm certainly not going to argue with it!")
                  ]
                end
    responses.each { |message| pokecenter_birthday_nurse_message(event_id, message) }
    received = false
    if $PokemonBag.pbCanStore?(item, 1)
      pokecenter_birthday_nurse_message(
        event_id, _INTL("Here you are. I hope it comes in handy on your journey!")
      )
      received = pbReceiveItem(item, 1, "", nil, false)
    end
    if !received
      pokecenter_birthday_nurse_message(
        event_id, _INTL("Oh, your Bag is full! Don't worry, I'll keep your present here. Come back when you have some room.")
      )
      return false
    end
    $PokemonGlobal.ironmon_pokecenter_birthday_run_id = run_id
    return true
  end

  def self.pokecenter_birthday_event_list(map_id, event_id, list)
    return list if !active? || !list
    if map_id == 26 && event_id == 5
      condition_index = list.index do |command|
        command.code == 111 &&
          command.parameters == [12, "isPlayerBirthDay?"]
      end
      return list if !condition_index
      replacement = list.dup
      condition = list[condition_index]
      replacement[condition_index] = RPG::EventCommand.new(
        111, condition.indent, [12, "false"]
      )
      return replacement
    end
    return list if ![25, 27].include?(map_id) || event_id != POKECENTER_BIRTHDAY_NURSE_EVENT_ID
    return list if list.any? do |command|
      command.code == 355 &&
        command.parameters == [POKECENTER_BIRTHDAY_GIFT_SCRIPT]
    end
    if map_id == 25
      replacement = pokecenter_registration_with_gift(list)
      return replacement if !replacement.equal?(list)
    else
      gift_index = list.index do |command|
        command.code == 355 &&
          command.parameters == ["obtainBirthdayHat(:pokecenter)"]
      end
      replacement = gift_index ? list[(gift_index + 1)..-1] : list
    end
    return replacement.flat_map do |command|
      if command.code == 314
        [command, pokecenter_birthday_gift_command(command.indent)]
      else
        [command]
      end
    end
  end

  def self.pokecenter_registration_with_gift(list)
    picker_index = list.index do |command|
      command.code == 355 && command.parameters == ["set_player_birthday"]
    end
    return list if !picker_index
    condition = list[picker_index + 1]
    prompt = list[picker_index - 1]
    return list if !condition || condition.code != 111 ||
      condition.parameters != [12, "isPlayerBirthDate?"] ||
      !prompt || prompt.code != 101
    end_index = ((picker_index + 2)...list.length).find do |index|
      list[index].code == 412 && list[index].indent == condition.indent
    end
    return list if !end_index
    start_index = picker_index - 1
    bubble = list[start_index - 1]
    start_index -= 1 if bubble && bubble.code == 355 &&
      bubble.parameters[0].to_s.strip == "pbCallBub(2,@event_id,true)"
    gift = pokecenter_birthday_gift_command(prompt.indent)
    registration = [
      RPG::EventCommand.new(355, prompt.indent, ["pbCallBub(2,@event_id,true)"]),
      RPG::EventCommand.new(101, prompt.indent, ["There we are, all registered!"]),
      gift
    ]
    replacement = list[0...start_index] + registration + list[(end_index + 1)..-1]
    return pokecenter_registration_dialogue(replacement)
  end

  def self.pokecenter_registration_dialogue(list)
    messages = {
      "If you bring us your injured Pokémon, we'll restore" =>
        "We restore tired Pokémon to full health, free of charge. Let me get you registered first.",
      "I need to register you in the system quickly. It'll just" => nil,
      "So your name is \\C[1]\\PN\\C[0]? Perfect." =>
        "Your name is \\PN? Perfect. This will only take a moment.",
      "Okay, we're all set! Now that this is taken care of," =>
        "Now, let's take care of your Pokémon. We'll have them feeling better in no time.",
      "Thank you for waiting." =>
        "Thank you for waiting. Your Pokémon are back to full health!",
      "We've restored your Pokémon to full health." => nil,
      "We hope to see you again!" => "Take care, \\PN. We hope to see you again!"
    }
    replacement = []
    replacing_message = false
    list.each do |command|
      next if replacing_message && command.code == 401
      replacing_message = false
      key = command.code == 101 ? command.parameters[0].to_s.strip : nil
      if key && messages.key?(key)
        replacing_message = true
        message = messages[key]
        if message
          replacement << RPG::EventCommand.new(101, command.indent, [message])
        else
          bubble = replacement[-1]
          if bubble && bubble.code == 355 && bubble.indent == command.indent &&
             bubble.parameters[0].to_s.strip == "pbCallBub(2,@event_id,true)"
            replacement.pop
          end
        end
      else
        replacement << command
      end
    end
    return replacement
  end

  def self.pokecenter_birthday_gift_command(indent)
    return RPG::EventCommand.new(355, indent, [POKECENTER_BIRTHDAY_GIFT_SCRIPT])
  end
end

class Interpreter
  # The later native dialogue hook aliases setup; prepend would make that alias
  # point back into this wrapper and recurse through super.
  alias ironmon_birthday_original_setup setup

  def setup(list, event_id, map_id = nil)
    resolved_map_id = map_id || ($game_map ? $game_map.map_id : nil)
    list = Ironmon.pokecenter_birthday_event_list(
      resolved_map_id, event_id, list
    )
    return ironmon_birthday_original_setup(list, event_id, map_id)
  end
end

alias ironmon_birthday_original_enter_common_building enter_common_building
def enter_common_building(building_id, city)
  building_id = :POKEMON_CENTER if Ironmon.active? &&
    building_id == :POKEMON_CENTER_BIRTHDAY
  return ironmon_birthday_original_enter_common_building(building_id, city)
end
