# encoding: UTF-8
#===============================================================================
# Automatic single-runner entry for limited-party trainer battles
#===============================================================================

module Ironmon
  BATTLE_PARTY_DIALOGUE = {
    74 => {
      36 => {
        "You came! Are you ready for our battle? Let's try with two Pokémon." =>
          "You came! Are you ready for our battle?"
      }.freeze
    }.freeze,
    78 => {
      10 => {
        "Pokémon League rules state that a challenger can only bring the same number of Pokémon as the Gym Leader into a battle." =>
          "Let's see how well you've trained your Pokémon.",
        "I have two Pokémon on my team, so that means you can also only bring two." =>
          "I look forward to a challenging battle.",
        "Would you kindly demonstrate how you battle, and with which Pokémon?" =>
          "Please demonstrate your skill in battle!"
      }.freeze
    }.freeze,
    79 => {
      10 => {
        "So you want to challenge me? Alright, pick three Pokémon then!" =>
          "So you want to challenge me? Alright, let's battle!"
      }.freeze
    }.freeze,
    102 => {
      31 => {
        "Now, I, Wattson, the Leader of Mauville Gym, shall electrify you! Choose 4 Pokémon!" =>
          "Now, I, Wattson, the Leader of Mauville Gym, shall electrify you!"
      }.freeze
    }.freeze
  }.freeze

  def self.battle_party_selection_index(ableproc = nil)
    party = usable_party
    return nil if party.length != 1
    runner = party[0]
    return nil if !runner.able?
    return nil if ableproc && !ableproc.call(runner)
    return $Trainer.party.index { |pokemon| pokemon.equal?(runner) }
  end

  def self.patch_battle_party_dialogue(map_id, map)
    return false if !active? || !map
    events = BATTLE_PARTY_DIALOGUE[map_id]
    return false if !events
    changed = false
    events.each do |event_id, messages|
      event = map.events[event_id]
      next if !event
      event.pages.each do |page|
        replacement = battle_party_dialogue_commands(page.list, messages)
        next if replacement == page.list
        page.list = replacement
        changed = true
      end
    end
    return changed
  end

  def self.battle_party_dialogue_commands(list, messages)
    replacement = []
    index = 0
    while index < list.length
      command = list[index]
      if command.code == 101
        last = index + 1
        last += 1 while last < list.length && list[last].code == 401
        text = list[index...last].map do |line|
          line.parameters[0].to_s.dup.force_encoding(Encoding::UTF_8).strip
        end.join(" ")
        message = messages[text]
        if message
          parameters = command.parameters.dup
          parameters[0] = message
          replacement << RPG::EventCommand.new(101, command.indent, parameters)
          index = last
          next
        end
      end
      replacement << command
      index += 1
    end
    return replacement
  end
end

module IronmonBattlePartySelectionHooks
  def hasValidTeam?(min = 1, max = 6, canCancel = false, acceptFainted = false)
    return super if !Ironmon.active?
    return Ironmon.battle_party_selection_index != nil
  end

  def choose(min = 1, max = 6, canCancel = false, acceptFainted = false,
             ableproc = nil, indexesVar = nil)
    return super if !Ironmon.active?
    # Multi-entry filtered selections are quest hand-ins, not battle teams.
    return super if min > 1 && ableproc
    PokemonSelection.restore if $PokemonGlobal.pokemonSelectionOriginalParty
    index = Ironmon.battle_party_selection_index(ableproc)
    if index.nil?
      pbBattleChallenge.pbCancel
      return false
    end
    selected = PokemonSelection.force([index])
    pbSet(indexesVar, [index]) if selected && indexesVar
    return selected
  end
end

PokemonSelection.singleton_class.prepend(IronmonBattlePartySelectionHooks)

Events.onMapCreate += proc do |_sender, event|
  Ironmon.patch_battle_party_dialogue(event[0], event[1])
end
