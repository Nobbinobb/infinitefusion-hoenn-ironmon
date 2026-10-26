module IronmonBattlePartySelectionTests
  class Challenge
    attr_reader :rules, :cancel_count
    attr_accessor :party

    def initialize
      @cancel_count = 0
    end

    def set(_name, _rounds, rules)
      @rules = rules
      @party = nil
    end

    def getParty
      return @party
    end

    def pbCancel
      @cancel_count += 1
      @party = @rules = nil
    end
  end

  class << self
    attr_accessor :active, :challenge, :ui_calls, :ui_party, :variables
  end

  def self.assert(condition, message)
    raise "Battle party selection failed: #{message}" if !condition
  end

  def self.pokemon
    previous = active
    self.active = false
    return Pokemon.new(:BULBASAUR, 10, nil, false, false)
  ensure
    self.active = previous
  end

  def self.test_battle_sizes
    runner = pokemon
    $Trainer.party = [runner]
    [1, 2, 3, 4, 6].each do |count|
      [false, true].each do |accept_fainted|
        assert(PokemonSelection.hasValidTeam?(count, count, false, accept_fainted),
          "one runner satisfies the requested battle size #{count}")
        assert(PokemonSelection.choose(count, count, false, accept_fainted),
          "battle size #{count} enters without a picker")
        assert($Trainer.party == [runner], "the current runner enters unchanged")
      end
    end
    assert(ui_calls.empty?, "battle selectors never open the party screen")
    assert(!$PokemonGlobal.pokemonSelectionOriginalParty,
      "a one-member party needs no restoration")
  end

  def self.test_party_restoration
    runner = pokemon
    egg = pokemon
    egg.steps_to_hatch = 1
    helper = pokemon
    helper.ironmon_party_exclusion = :utility_slave
    original = [egg, helper, runner]
    $Trainer.party = original
    accepted = proc { |entry| entry.equal?(runner) }
    assert(PokemonSelection.choose(1, 4, true, true, accepted, 5),
      "type-filtered battles enter the eligible runner")
    assert($Trainer.party == [runner] && variables[5] == [2],
      "eggs and excluded helpers are omitted, with the original index retained")
    assert($PokemonGlobal.pokemonSelectionOriginalParty.equal?(original),
      "the native original-party reference is retained")
    runner.hp = 3
    runner.status = :POISON
    runner.item = :POTION
    runner.moves << Pokemon::Move.new(:TACKLE)
    runner.moves[0].pp = 1
    assert(PokemonSelection.restore, "the native restoration path remains available")
    assert($Trainer.party.equal?(original) && $Trainer.party[2].equal?(runner),
      "restoration keeps the original party and runner objects")
    assert(runner.hp == 3 && runner.status == :POISON && runner.item_id == :POTION && runner.moves[0].pp == 1,
      "restoration retains battle damage, status, item and move changes")
    assert(!$PokemonGlobal.pokemonSelectionOriginalParty,
      "the saved-party reference is cleared")

    assert(PokemonSelection.force([2]), "a previous selection can be pending")
    assert(PokemonSelection.choose(1, 2), "a new automatic entry restores a pending party first")
    assert($PokemonGlobal.pokemonSelectionOriginalParty.equal?(original),
      "the full original party remains available after a repeated selection")
    PokemonSelection.restore
  end

  def self.test_rejected_parties
    runner = pokemon
    fainted = pokemon
    fainted.hp = 0
    egg = pokemon
    egg.steps_to_hatch = 1
    helper = pokemon
    helper.ironmon_party_exclusion = :utility_slave
    [[], [fainted], [egg, helper], [runner, pokemon]].each do |party|
      $Trainer.party = party
      assert(!PokemonSelection.hasValidTeam?(1, 4, false, true),
        "an invalid runner cannot pass the battle preflight")
      assert(!PokemonSelection.choose(1, 4, false, true),
        "an invalid party returns instead of entering a selection loop")
      assert($Trainer.party.equal?(party), "rejected entry leaves the party untouched")
    end
    $Trainer.party = [runner]
    reject = proc { |_entry| false }
    variables[5] = :untouched
    assert(!PokemonSelection.choose(1, 4, true, true, reject, 5),
      "an ineligible type-expert runner cannot start a battle")
    assert(variables[5] == :untouched && $Trainer.party == [runner],
      "rejection leaves selected indices and the runner untouched")
    assert(ui_calls.empty?, "rejected battle entry never opens a picker")
    assert(challenge.cancel_count > 0, "battle selection state is cleared")
  end

  def self.test_native_selection_scope
    runner = pokemon
    $Trainer.party = [runner]
    self.active = false
    assert(!PokemonSelection.hasValidTeam?(3, 3, true, true),
      "ordinary mode retains the original minimum party size")
    assert(PokemonSelection.hasValidTeam?(1, 4),
      "ordinary mode still uses native eligibility")
    filter = proc { |_entry| true }
    self.ui_party = [runner]
    assert(PokemonSelection.choose(1, 4, true, true, filter, 5),
      "ordinary mode still accepts a native party selection")
    assert(ui_calls.pop == [filter, 5, 1, 4],
      "ordinary mode forwards the native picker arguments and limits")
    self.ui_party = nil
    assert(!PokemonSelection.choose(1, 4, true), "ordinary cancellation is retained")
    ui_calls.clear

    self.active = true
    assert(!PokemonSelection.choose(3, 3, true, true, filter, 5),
      "multi-Pokemon quest hand-ins retain native cancellation")
    assert(ui_calls.pop == [filter, 5, 3, 3],
      "quest hand-ins keep the original required count and validation filter")
  end

  def self.test_dialogue
    self.active = true
    Ironmon::BATTLE_PARTY_DIALOGUE.each do |map_id, events|
      map = RPG::Map.new(1, 1)
      events.each do |event_id, messages|
        event = RPG::Event.new(0, 0)
        event.id = event_id
        list = []
        messages.each_key do |text|
          words = text.split(" ")
          middle = words.length / 2
          list << RPG::EventCommand.new(101, 2, [(words[0...middle].join(" ") + " ").b])
          list << RPG::EventCommand.new(401, 2, [words[middle..-1].join(" ")])
        end
        battle = RPG::EventCommand.new(111, 2, [12, "PokemonSelection.choose(1, 4, true, true)"])
        reward = RPG::EventCommand.new(355, 3, ["pbReceiveItem(:POTION)"])
        unknown = RPG::EventCommand.new(101, 2, ["An unrelated message."])
        list.concat([battle, reward, unknown, RPG::EventCommand.new(0, 0, [])])
        event.pages[0].list = list
        map.events[event_id] = event
        self.active = false
        assert(!Ironmon.patch_battle_party_dialogue(map_id, map),
          "ordinary mode keeps the original dialogue")
        assert(event.pages[0].list.equal?(list), "ordinary map commands stay untouched")
        self.active = true
        assert(Ironmon.patch_battle_party_dialogue(map_id, map),
          "Ironmon replaces selection dialogue on map #{map_id}")
        result = event.pages[0].list
        assert(result.select { |entry| entry.code == 101 }.map { |entry| entry.parameters[0] } == messages.values + [unknown.parameters[0]],
          "only the exact known message groups change")
        assert(result.include?(battle) && result.include?(reward) && result.include?(unknown),
          "battle conditions, rewards and unrelated dialogue retain their native objects")
        assert(result[0].indent == 2 && result.none? { |entry| entry.code == 401 },
          "message indentation is preserved and old continuation lines are removed")
        assert(!Ironmon.patch_battle_party_dialogue(map_id, map),
          "patching the same map again makes no further changes")
      end
    end
    assert(!Ironmon.patch_battle_party_dialogue(1, RPG::Map.new(1, 1)),
      "unrelated maps are unchanged")
  end

  def self.run
    original_trainer = $Trainer
    original_global = $PokemonGlobal
    singleton = Ironmon.singleton_class
    singleton.send(:alias_method, :battle_party_test_original_active, :active?)
    singleton.send(:define_method, :active?) { IronmonBattlePartySelectionTests.active }
    backups = []
    stubs = {
      :pbBattleChallenge => proc { IronmonBattlePartySelectionTests.challenge },
      :pbSet => proc { |index, value| IronmonBattlePartySelectionTests.variables[index] = value },
      :pbEntryScreen => proc do |filter = nil, indexes = nil|
        tests = IronmonBattlePartySelectionTests
        rules = tests.challenge.rules.ruleset
        tests.ui_calls << [filter, indexes, rules.minLength, rules.maxLength]
        tests.challenge.party = tests.ui_party
      end
    }
    stubs.each do |name, implementation|
      backup = :"battle_party_test_original_#{name}"
      Object.send(:alias_method, backup, name)
      Object.send(:define_method, name, implementation)
      Object.send(:private, name)
      backups << [name, backup]
    end
    $Trainer = Struct.new(:party).new([])
    $PokemonGlobal = PokemonGlobalMetadata.new
    self.challenge = Challenge.new
    self.ui_calls = []
    self.variables = {}
    self.active = true
    test_battle_sizes
    test_party_restoration
    test_rejected_parties
    test_native_selection_scope
    test_dialogue
  ensure
    $Trainer = original_trainer
    $PokemonGlobal = original_global
    backups.to_a.reverse_each do |name, backup|
      Object.send(:alias_method, name, backup)
      Object.send(:remove_method, backup)
      Object.send(:private, name)
    end
    if singleton && singleton.method_defined?(:battle_party_test_original_active)
      singleton.send(:alias_method, :active?, :battle_party_test_original_active)
      singleton.send(:remove_method, :battle_party_test_original_active)
    end
  end
end

IronmonBattlePartySelectionTests.run
