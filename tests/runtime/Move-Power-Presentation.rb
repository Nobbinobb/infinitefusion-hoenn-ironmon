module IronmonMovePowerPresentationRuntimeTests
  OUTPUT_PATH = $ironmon_move_power_presentation_test_output_path.to_s

  class CounterSide
    attr_reader :effects

    def initialize
      @effects = Hash.new(0)
    end
  end

  class CounterUser
    attr_reader :effects

    def initialize
      @effects = Hash.new(0)
      @side = CounterSide.new
    end

    def pbOwnSide
      return @side
    end
  end

  class FixedFormPokemon
    def species; return :GRENINJA; end
    def form; return 2; end
  end

  class FixedTypePokemon
    def types; return [:NORMAL, :FLYING]; end
  end

  class ActiveTypeBattler
    attr_reader :include_extra_type

    def initialize(types)
      @types = types
      @include_extra_type = false
    end

    def pbTypes(include_extra_type = false)
      @include_extra_type = include_extra_type
      return @types
    end
  end

  class TransformScene
    def pbDisplayMessage(*_args); end
    def pbRefreshOne(*_args); end
  end

  def self.assert(condition, message)
    raise "Move-power presentation runtime test failed: #{message}" if
      !condition
  end

  def self.test_catalog
    ids = Ironmon.tracker_move_power_catalog_ids
    assert(ids.length == 101, "the maintained rules produce 101 move entries")
    assert(ids.first == "RETURN", "the presentation catalog begins with Return")
    assert(ids.last == "TWINEEDLE", "the presentation catalog ends with Twineedle")
    assert(
      Ironmon.tracker_move_power_catalog["FUSIONBOLT"]["prior_field_effect"] ==
        "FusionFlare",
      "generated metadata preserves the Fusion Bolt prior-move condition"
    )
  end

  def self.test_active_battle_types
    pokemon = FixedTypePokemon.new
    assert(
      Ironmon.tracker_active_types(pokemon) == ["NORMAL", "FLYING"],
      "a Pokemon outside battle uses its species types"
    )

    battler = ActiveTypeBattler.new([:FIRE])
    assert(
      Ironmon.tracker_active_types(pokemon, battler) == ["FIRE"],
      "a Color Changed battler uses its current battle type"
    )
    assert(
      battler.include_extra_type,
      "active type lookup includes the engine's temporary third type"
    )
  end

  def self.test_offline_calculation
    pokemon = Pokemon.new(:PIKACHU, 50, nil, false)
    pokemon.happiness = 255
    presentation = Ironmon.tracker_move_power_presentation(
      Pokemon::Move.new(:RETURN), pokemon
    )
    assert(
      presentation["display"] == "102",
      "Return uses the native happiness calculation outside battle"
    )
    fixed_hits = Ironmon.tracker_move_power_presentation(
      Pokemon::Move.new(:DOUBLEKICK), pokemon
    )
    assert(
      fixed_hits["display"] == "60" &&
        fixed_hits["indicator"] == "none",
      "fixed two-hit moves show cumulative power without the variable-hit icon"
    )
    variable_hits = Ironmon.tracker_move_power_presentation(
      Pokemon::Move.new(:PINMISSILE), pokemon
    )
    assert(
      variable_hits["display"] == "25" &&
        variable_hits["indicator"] == "multi_hit",
      "random hit-count moves retain the variable-hit icon"
    )
    fixed_form_hits = Ironmon.tracker_water_shuriken_power_presentation(
      Pokemon::Move.new(:WATERSHURIKEN),
      FixedFormPokemon.new,
      Ironmon.tracker_move_power_catalog["WATERSHURIKEN"],
      nil,
      false
    )
    assert(
      fixed_form_hits["display"] == "60" &&
        fixed_form_hits["indicator"] == "none",
      "fixed-form Water Shuriken shows its combined total without the icon"
    )
    conditional_power = Ironmon.tracker_move_power_presentation(
      Pokemon::Move.new(:REVENGE), pokemon
    )
    assert(
      conditional_power["indicator"] == "none",
      "conditional moves no longer show a power-column icon"
    )
    trump_card = Pokemon::Move.new(:TRUMPCARD)
    trump_card.pp = 4
    trump_card_power = Ironmon.tracker_move_power_presentation(
      trump_card, pokemon
    )
    assert(
      trump_card_power["display"] == "50",
      "Trump Card uses the PP remaining after its prospective use"
    )
    hidden_enemy_power = Ironmon.tracker_move_power_presentation(
      Pokemon::Move.new(:RETURN), pokemon, nil, true
    )
    assert(
      hidden_enemy_power["display"] == "???",
      "enemy-sensitive calculations do not expose hidden state"
    )
    hidden_power = Ironmon.tracker_move_power_presentation(
      Pokemon::Move.new(:HIDDENPOWER), pokemon
    )
    expected_hidden_power_type = pbHiddenPower(
      pokemon, pokemon.hiddenPowerType
    )[0]
    expected_hidden_power_type = :NEUTRAL if
      Settings::TRIPLE_TYPES.include?(expected_hidden_power_type)
    assert(
      hidden_power["type"] == expected_hidden_power_type.to_s,
      "Hidden Power resolves its current type outside battle"
    )
    pokemon.hiddenPowerType = :ICEFIREELECTRIC
    neutral_hidden_power = Ironmon.tracker_move_power_presentation(
      Pokemon::Move.new(:HIDDENPOWER), pokemon
    )
    assert(
      neutral_hidden_power["type"] == "NEUTRAL",
      "composite Hidden Power types use the game's single Neutral label"
    )
    pokemon.hiddenPowerType = nil
    counterfeit = nil
    GameData::Move.each do |move_data|
      counterfeit = move_data if move_data.name == "Counterfeit"
    end
    assert(counterfeit, "the installed move catalog contains Counterfeit")
    assert(
      counterfeit.type == :QMARKS,
      "Counterfeit retains the QMARKS pseudo-type " +
        "(id=#{counterfeit.id.inspect}, type=#{counterfeit.type.inspect})"
    )
    hidden_speed_power = Ironmon.tracker_move_power_presentation(
      Pokemon::Move.new(:GYROBALL), pokemon
    )
    assert(
      hidden_speed_power["display"] == "???",
      "speed-ratio moves follow the audit's hidden-enemy-speed decision"
    )
    hidden_hp_power = Ironmon.tracker_move_power_presentation(
      Pokemon::Move.new(:SUPERFANG), pokemon
    )
    assert(
      hidden_hp_power["display"] == "???" &&
        hidden_hp_power["indicator"] == "none",
      "unknown direct-HP damage has no additional row marker"
    )
    knockout_power = Ironmon.tracker_move_power_presentation(
      Pokemon::Move.new(:FISSURE), pokemon
    )
    assert(
      knockout_power["display"] == "KO" &&
        knockout_power["indicator"] == "none",
      "one-hit knockout moves use the plain KO display"
    )
  end

  def self.test_prospective_counters
    user = CounterUser.new
    fury_cutter = Pokemon::Move.new(:FURYCUTTER)
    fury_definition = Ironmon.tracker_move_power_catalog["FURYCUTTER"]
    first_power = Ironmon.tracker_prospective_counter_power(
      fury_cutter, fury_definition, user, true
    )
    user.effects[PBEffects::FuryCutter] = 1
    second_power = Ironmon.tracker_prospective_counter_power(
      fury_cutter, fury_definition, user, true
    )
    assert(
      first_power == 40 && second_power == 80,
      "Fury Cutter displays the power of the next consecutive use"
    )

    echoed_voice = Pokemon::Move.new(:ECHOEDVOICE)
    echoed_definition = Ironmon.tracker_move_power_catalog["ECHOEDVOICE"]
    user.pbOwnSide.effects[PBEffects::EchoedVoiceCounter] = 1
    next_round_power = Ironmon.tracker_prospective_counter_power(
      echoed_voice, echoed_definition, user, true
    )
    user.pbOwnSide.effects[PBEffects::EchoedVoiceUsed] = true
    same_round_power = Ironmon.tracker_prospective_counter_power(
      echoed_voice, echoed_definition, user, true
    )
    assert(
      next_round_power == 80 && same_round_power == 40,
      "Echoed Voice accounts for whether its side already used it this round " +
        "(next=#{next_round_power}, same=#{same_round_power})"
    )
  end

  def self.test_random_details
    pokemon = Pokemon.new(:PIKACHU, 50, nil, false)
    present = Ironmon.tracker_move_power_presentation(
      Pokemon::Move.new(:PRESENT), pokemon
    )
    chances = present["outcomes"].map { |outcome| outcome["chance_percent"] }
    assert(
      present["display"] == "???" && chances == [40, 30, 10, 20],
      "Present exposes its audited outcome grid without consuming RNG"
    )
  end

  def self.test_imposter_snapshot
    original_global = $PokemonGlobal
    original_bag = $PokemonBag
    original_game_temp = $game_temp
    tracked = [:@tracker_battle, :@tracker_battle_id, :@tracker_player_battler,
               :@tracker_player_pokemon, :@tracker_enemy_battlers]
    previous = {}
    tracked.each { |name| previous[name] = Ironmon.instance_variable_get(name) }
    begin
      $PokemonGlobal = PokemonGlobalMetadata.new
      $PokemonGlobal.ironmon_mode = true
      $PokemonBag = nil
      $game_temp = Game_Temp.new
      $game_temp.in_battle = true
      pokemon = Pokemon.new(:DITTO, 42, nil, false)
      pokemon.ability = :IMPOSTER
      pokemon.item = :POTION
      pokemon.moves.replace([
        Pokemon::Move.new(:TACKLE), Pokemon::Move.new(:GROWL)
      ])
      target = Pokemon.new(:PIKACHU, 50, nil, false)
      target.ability = :STATIC
      target.item = :ORANBERRY
      target.moves.replace([
        Pokemon::Move.new(:THUNDERBOLT), Pokemon::Move.new(:QUICKATTACK),
        Pokemon::Move.new(:ELECTROBALL), Pokemon::Move.new(:THUNDERWAVE)
      ])
      battle = PokeBattle_Battle.new(
        TransformScene.new, [pokemon], [target], nil, nil
      )
      player = PokeBattle_Battler.new(battle, 0)
      enemy = PokeBattle_Battler.new(battle, 1)
      battle.battlers[0], battle.battlers[1] = player, enemy
      battle.positions[0] = PokeBattle_ActivePosition.new
      battle.positions[1] = PokeBattle_ActivePosition.new
      player.pbInitialize(pokemon, 0)
      enemy.pbInitialize(target, 0)
      Ironmon.instance_variable_set(:@tracker_battle, battle)
      Ironmon.instance_variable_set(:@tracker_battle_id, "imposter-test")
      Ironmon.instance_variable_set(:@tracker_player_battler, player)
      Ironmon.instance_variable_set(:@tracker_player_pokemon, pokemon)
      Ironmon.instance_variable_set(:@tracker_enemy_battlers, { 1 => enemy })
      original_hp = pokemon.hp
      original_total_hp = pokemon.totalhp
      original_level = pokemon.level
      player.pbTransform(enemy)
      snapshot = Ironmon.tracker_player_snapshot
      assert(snapshot["transformed"], "the live snapshot identifies Transform")
      assert(
        snapshot["original_species_id"] == Ironmon.tracker_species_id(pokemon) &&
          snapshot["stored_ability_details"]["id"] == "IMPOSTER" &&
          snapshot["copied_ability_details"]["id"] == "STATIC",
        "persistent learnset and ability ownership remain tied to the original Pokemon"
      )
      assert(
        snapshot["species_id"] == Ironmon.tracker_species_id(target) &&
          snapshot["species_name"] == target.species_data.name &&
          snapshot["sprite_path"] == Ironmon.tracker_sprite_path(target),
        "the transformed card uses the copied visible species and sprite"
      )
      assert(
        snapshot["ability_details"]["id"] == "STATIC" &&
          snapshot["ability"] == GameData::Ability.get(:STATIC).name,
        "the transformed card uses the copied ability"
      )
      assert(
        [snapshot["attack"], snapshot["defense"],
         snapshot["special_attack"], snapshot["special_defense"],
         snapshot["speed"]] ==
          [enemy.attack, enemy.defense, enemy.spatk, enemy.spdef, enemy.speed],
        "the transformed card uses the copied non-HP battle stats"
      )
      assert(
        snapshot["level"] == original_level &&
          snapshot["current_hp"] == original_hp &&
          snapshot["maximum_hp"] == original_total_hp &&
          snapshot["held_item"] == GameData::Item.get(:POTION).name,
        "Transform retains the player's level, HP, and held item"
      )
      assert(
        snapshot["moves"].map { |move| move["id"] } ==
          target.moves.map { |move| move.id.to_s } &&
          snapshot["moves"].all? { |move| move["current_pp"] == 5 && move["total_pp"] == 5 },
        "the current move list uses the four copied 5-PP battle moves"
      )
      assert(
        snapshot["stored_moves"].map { |move| move["id"] } ==
          pokemon.moves.map { |move| move.id.to_s },
        "PP-item targets retain the original party move slots"
      )
      assert(
        snapshot["nature_adjustments"].values.uniq == ["neutral"],
        "copied stats do not retain misleading original-nature highlights"
      )
    ensure
      $PokemonGlobal = original_global
      $PokemonBag = original_bag
      $game_temp = original_game_temp
      previous.each { |name, value| Ironmon.instance_variable_set(name, value) }
    end
  end

  def self.run
    test_catalog
    test_active_battle_types
    test_offline_calculation
    test_prospective_counters
    test_random_details
    test_imposter_snapshot
    File.binwrite(OUTPUT_PATH, "move-power presentation runtime tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonMovePowerPresentationRuntimeTests.run
