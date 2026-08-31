module IronmonDefenseOverviewTests
  def self.recovery_details(snapshot)
    return snapshot["recovery"].flat_map { |row| row["healing_amounts"] }
  end

  def self.protection_labels(snapshot)
    return snapshot["protections"].map { |row| row["label"] }
  end

  def self.test_audited_coverage
    previous_player = Ironmon.instance_variable_get(:@tracker_player_battler)
    previous_known = Ironmon.instance_variable_get(:@tracker_enemy_abilities)
    pokemon = Pokemon.new(:BULBASAUR, 50, nil, false)
    opponent = Pokemon.new(:PIKACHU, 50, nil, false)
    battle = PokeBattle_Battle.new(nil, [pokemon], [opponent], nil, nil)
    player = PokeBattle_Battler.new(battle, 0)
    enemy = PokeBattle_Battler.new(battle, 1)
    battle.battlers[0], battle.battlers[1] = player, enemy
    player.pbInitialize(pokemon, 0)
    enemy.pbInitialize(opponent, 0)
    battle.positions[0] = PokeBattle_ActivePosition.new
    battle.positions[1] = PokeBattle_ActivePosition.new
    Ironmon.instance_variable_set(:@tracker_player_battler, player)
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, {})
    player.pbChangeTypes(:NORMAL)
    player.item = nil
    player.ability = :DRYSKIN
    dry = overview(player)
    assert(dry["recovery"].map { |row| row["label"] }.sort == ["Rain", "Water move"], "Dry Skin keeps both distinct healing events")
    assert(recovery_details(dry).sort == ["+1/4 HP", "+1/8 HP per turn"], "absorbed-hit healing is not per-turn healing")
    player.effects[PBEffects::HealBlock] = 2
    assert(overview(player)["recovery"].empty? && multiplier(overview(player), :WATER) == 0, "Heal Block removes absorbed healing, never immunity")
    player.ability = :REGENERATOR
    assert(recovery_details(overview(player)) == ["+1/3 HP"], "native Regenerator bypasses Heal Block")
    player.ability = :NATURALCURE
    assert(recovery_details(overview(player)) == ["Heals status conditions"], "switch-out status cure is independent of Heal Block")
    player.ability = :SHEDSKIN
    assert(recovery_details(overview(player)) == ["30% chance: Heals status conditions"], "Shed Skin uses the installed 30 percent chance")
    player.ability = :POISONHEAL
    assert(overview(player)["recovery"].empty? && protection_labels(overview(player)).include?("Poison damage"), "poison damage protection remains when its heal is blocked")
    assert(!protection_labels(overview(player)).include?("Poison"), "Poison Heal does not prevent poisoning")
    player.effects[PBEffects::HealBlock] = 0
    assert(recovery_details(overview(player)) == ["+1/8 HP per turn"], "Poison Heal advertises a future condition without requiring current poison")
    player.ability = :EARLYBIRD
    assert(recovery_details(overview(player)) == ["Sleep duration \u00d71/2"], "Early Bird describes duration, not an immediate cure: #{recovery_details(overview(player)).inspect}")
    player.ability = :HEALER
    assert(overview(player)["recovery"].empty?, "Healer never advertises self-healing")
    ally = PokeBattle_Battler.new(battle, 2)
    ally.pbInitialize(Pokemon.new(:CHANSEY, 50, nil, false), 1)
    battle.battlers[2] = ally
    ally.ability = :HEALER
    player.ability = :BLAZE
    assert(overview(player)["recovery"].empty?, "unknown ally ability cannot add recovery")
    Ironmon.instance_variable_set(:@tracker_player_battler, ally)
    assert(recovery_details(overview(player)) == ["30% chance: Heals status conditions"], "known ally Healer adds its correctly scoped recovery")
    Ironmon.instance_variable_set(:@tracker_player_battler, player)
    battle.battlers[2] = nil

    { SITRUSBERRY: "+1/4 HP (once)", ORANBERRY: "+10 HP (once)", BERRYJUICE: "+20 HP (once)",
      FIGYBERRY: "+1/8 HP (once)", WIKIBERRY: "+1/8 HP (once)", MAGOBERRY: "+1/8 HP (once)",
      AGUAVBERRY: "+1/8 HP (once)", IAPAPABERRY: "+1/8 HP (once)", ENIGMABERRY: "+1/4 HP (once)",
      SHELLBELL: "+1/8 damage dealt as HP", PERSIMBERRY: "Heals confusion (once)",
      LUMBERRY: "Heals status and confusion (once)", WHITEHERB: "Restores lowered stages (once)" }.each do |item, detail|
      player.item = item
      assert(recovery_details(overview(player)).include?(detail), "#{item} uses its audited quantity and recovery kind")
    end
    [:CHESTOBERRY, :PECHABERRY, :RAWSTBERRY, :CHERIBERRY, :ASPEARBERRY].each do |item|
      player.item = item
      assert(recovery_details(overview(player)) == ["Heals condition (once)"], "#{item} is a one-use cure, not permanent immunity")
    end
    player.item = :MENTALHERB
    assert(overview(player)["recovery"].length == 6, "Mental Herb describes all six cured effects")
    player.item = :SITRUSBERRY
    enemy.ability = :UNNERVE
    assert(!overview(player)["recovery"].empty?, "unrevealed Unnerve cannot change the player's recovery")
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, { 1 => { "id" => "UNNERVE" } })
    assert(overview(player)["recovery"].empty?, "known Unnerve prevents ordinary held berry activation")
    player.item = :BERRYJUICE
    assert(!overview(player)["recovery"].empty?, "Unnerve does not prevent Berry Juice")
    player.item = nil
    player.ability = :CHEEKPOUCH
    assert(recovery_details(overview(player)) == ["+1/3 HP"], "berry-consumption capability includes forced consumption")
    player.effects[PBEffects::GastroAcid] = true
    assert(overview(player)["recovery"].empty?, "suppressed passive recovery is absent")
    player.effects[PBEffects::GastroAcid] = false
    player.ability = :BLAZE
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, {})
    player.item = :SITRUSBERRY
    player.effects[PBEffects::Embargo] = 2
    assert(overview(player)["recovery"].empty?, "inactive recovery items are omitted")
    player.effects[PBEffects::Embargo] = 0
    player.item = nil

    pokemon.moves.replace([Pokemon::Move.new(:WISH), Pokemon::Move.new(:MILKDRINK)])
    assert(overview(player)["recovery"].empty?, "knowing Wish or Milk Drink is not a defensive healing state")
    wish = PokeBattle_Move.from_pokemon_move(battle, Pokemon::Move.new(:WISH))
    wish.pbEffectGeneral(player)
    pending = overview(player)
    assert(pending["recovery"].first["label"] == "Next turn end", "native Wish creates a visible delayed heal")
    assert(recovery_details(pending) == ["+1/2 original max HP (once)"], "Wish amount describes its stored basis without reading it")
    position = battle.positions[player.index]
    position.effects[PBEffects::WishAmount] = 99999
    assert(overview(player)["recovery"] == pending["recovery"], "stored HP is never exposed by the display")
    position.effects[PBEffects::Wish] = 1
    assert(overview(player)["recovery"].first["label"] == "Turn end", "Wish countdown follows the pending state's remaining end phases")
    position.effects[PBEffects::Wish] = 0
    assert(overview(player)["recovery"].empty?, "consumed Wish disappears even while the move is known")
    consumed = overview(player)
    [:HealingWish, :LunarDance].each do |effect|
      position.effects[PBEffects.const_get(effect)] = true
      player.effects[PBEffects::HealBlock] = 2
      assert(recovery_details(overview(player)) == ["Full HP (once)", "Heals status conditions (once)"], "#{effect} is a pending native switch-in recovery, without PP clutter")
      position.effects[PBEffects.const_get(effect)] = false
    end
    player.effects[PBEffects::HealBlock] = 0
    enemy.effects[PBEffects::LeechSeed] = 0
    linked = overview(player)["recovery"]
    assert(recovery_details(overview(player)) == ["+Drained HP per turn"], "recipient index zero receives active drain recovery")
    enemy.hp = 1
    enemy.instance_variable_set(:@status, :POISON)
    enemy.ability = :LIQUIDOOZE
    assert(overview(player)["recovery"] == linked, "concealed source HP, status and ability do not alter drain presentation")
    [:LIQUIDOOZE, :MAGICGUARD].each do |ability|
      Ironmon.instance_variable_set(:@tracker_enemy_abilities, { 1 => { "id" => ability.to_s } })
      assert(overview(player)["recovery"].empty?, "known #{ability} prevents positive drain recovery")
    end
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, {})
    player.item = :BIGROOT
    assert(recovery_details(overview(player)) == ["+Drained HP ×1.3 per turn"], "drain boost stays distinct from the unknown base quantity")
    enemy.effects[PBEffects::LeechSeed] = -1
    battle.field.terrain = :Grassy
    player.effects[PBEffects::AquaRing] = true
    player.effects[PBEffects::Ingrain] = true
    player.item = :LEFTOVERS
    stacking = overview(player)
    assert(recovery_details(stacking).count("+1/16 HP per turn") == 4, "four independent turn-end recovery contributions survive projection")
    player.item = :BIGROOT
    assert(recovery_details(overview(player)).count("+1/16 HP ×1.3 per turn") == 2, "Big Root boosts Aqua Ring and Ingrain, not terrain")
    player.effects[PBEffects::AquaRing] = false
    player.effects[PBEffects::Ingrain] = false
    player.pbChangeTypes(:FLYING)
    player.item = nil
    assert(overview(player)["recovery"].empty?, "airborne Pokémon do not receive terrain recovery")
    player.pbChangeTypes(:NORMAL)
    assert(profile(overview(player), :GROUND)["physical_min"] == 0.5 && profile(overview(player), :GROUND)["physical_max"] == 1, "Grassy Terrain's specific Ground moves contribute a conditional factor")
    assert(profile(overview(player), :GROUND)["special_min"] == 1, "specific physical reductions are not applied to special attacks")
    battle.field.terrain = :None

    current_catalog = Ironmon::DefenseCatalog.load
    legacy = Marshal.load(Marshal.dump(current_catalog))
    legacy[:abilities][:ICEBODY][:recovery] = ["Hail", [1, 16]]
    [:recovery_formats, :position_recovery, :linked_recovery].each { |key| legacy.delete(key) }
    begin
      Ironmon::DefenseCatalog.instance_variable_set(:@data, Ironmon::DefenseCatalog.decode(Ironmon.tracker_json_generate(legacy)))
      player.ability = :ICEBODY
      assert(recovery_details(overview(player)) == ["+1/16 HP per turn"], "old recovery tuples render without new format or state tables")
    ensure
      Ironmon::DefenseCatalog.instance_variable_set(:@data, current_catalog)
      player.ability = :BLAZE
    end

    test_audited_protections(battle, player, enemy)
    enemy.item = :SITRUSBERRY
    enemy.ability = :REGENERATOR
    enemy.battle.positions[enemy.index].effects[PBEffects::Wish] = 2
    enemy_pending = overview(enemy, true)
    assert(enemy_pending["recovery"].length == 1, "enemy shows public Wish but neither concealed item nor ability")
    enemy.battle.positions[enemy.index].effects[PBEffects::WishAmount] = 1
    enemy.hp = enemy.totalhp
    enemy.instance_variable_set(:@status, :NONE)
    assert(overview(enemy, true)["recovery"] == enemy_pending["recovery"], "enemy pending healing is invariant under concealed HP/status/amount")
    return { "example_pending" => pending, "example_consumed" => consumed,
      "example_stacking" => stacking, "example_enemy_pending" => enemy_pending }
  ensure
    Ironmon.instance_variable_set(:@tracker_player_battler, previous_player)
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, previous_known)
  end

  def self.test_audited_protections(battle, player, enemy)
    player.item = nil
    player.ability = :ROCKHEAD
    recoil = overview(player)["protections"].find { |row| row["label"] == "Recoil" }
    assert(recoil["moves"].include?(GameData::Move.get(:DOUBLEEDGE).name) && !recoil["moves"].include?(GameData::Move.get(:STRUGGLE).name), "recoil catalog respects the native shared path")
    player.ability = :LONGREACH
    assert(protection_labels(overview(player)).include?("Contact effects"), "Long Reach prevents contact consequences")
    player.ability = :BLAZE
    player.item = :PROTECTIVEPADS
    assert(protection_labels(overview(player)).include?("Contact effects"), "Protective Pads prevents contact consequences")
    player.item = nil
    player.ability = :STURDY
    player.hp = player.totalhp
    sturdy = overview(player)
    assert(protection_labels(sturdy).include?("1 HP survival (full HP)"), "Sturdy includes full-HP survival")
    ohko = sturdy["protections"].find { |row| row["label"] == "One-hit KOs" }
    assert(ohko["moves"].include?(GameData::Move.get(:FISSURE).name) && !ohko["moves"].include?(GameData::Move.get(:COUNTER).name), "OHKO list excludes ordinary fixed damage")
    player.hp -= 1
    assert(!protection_labels(overview(player)).include?("1 HP survival (full HP)"), "used-up full-HP protection is no longer active")
    player.ability = :BLAZE
    player.item = :FOCUSBAND
    assert(!protection_labels(overview(player)).any? { |label| label.include?("survival") }, "installed Focus Band does not cover the non-full-HP path")
    player.hp = player.totalhp
    assert(protection_labels(overview(player)).include?("1 HP survival (full HP, 10%)"), "Focus Band shows its actual probability and prerequisite")
    player.item = nil
    player.effects[PBEffects::Endure] = true
    assert(protection_labels(overview(player)).include?("1 HP survival"), "active Endure is a defensive state")
    player.effects[PBEffects::Endure] = false
    masked = PokeBattle_Battler.new(battle, 2)
    masked.pbInitialize(Pokemon.new(:MIMIKYU, 50, nil, false), 1)
    masked.ability = :DISGUISE
    masked.form = 0
    masked.effects[PBEffects::GastroAcid] = true
    assert(protection_labels(overview(masked)).include?("First-hit damage shield"), "intact Disguise follows the native suppression exception")
    masked.form = 1
    assert(!protection_labels(overview(masked)).include?("First-hit damage shield"), "broken Disguise is no longer advertised")
    player.effects[PBEffects::Substitute] = 10
    substitute = overview(player)["protections"].find { |row| row["label"] == "Blocked status moves" }
    assert(substitute["moves"].include?(GameData::Move.get(:TOXIC).name) && !substitute["moves"].include?(GameData::Move.get(:MILKDRINK).name), "substitute list includes blocked incoming status, not self healing")
    assert(!substitute["moves"].include?(GameData::Move.get(:ROAR).name), "substitute list excludes native bypasses")
    player.effects[PBEffects::Substitute] = 0
    player.effects[PBEffects::Protect] = true
    guarded = overview(player)["protections"].find { |row| row["label"] == "Guarded moves" }
    assert(guarded["moves"].include?(GameData::Move.get(:TACKLE).name) && !guarded["moves"].include?(GameData::Move.get(:FEINT).name), "guard list follows installed protectability")
    player.effects[PBEffects::Protect] = false
    player.effects[PBEffects::KingsShield] = true
    guarded = overview(player)["protections"].find { |row| row["label"] == "Guarded damaging moves" }
    assert(!guarded["moves"].include?(GameData::Move.get(:TOXIC).name), "King's Shield does not advertise status protection")
    player.effects[PBEffects::KingsShield] = false
    enemy.ability = :DAMP
    assert(!protection_labels(overview(player)).include?("Explosions"), "unrevealed global protection is not inferred")
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, { 1 => { "id" => "DAMP" } })
    assert(protection_labels(overview(player)).include?("Explosions"), "known Damp applies globally")
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, {})
    player.ability = :LEVITATE
    assert(["Spikes", "Toxic Spikes", "Sticky Web"].all? { |label| protection_labels(overview(player)).include?(label) }, "airborne hazards are explicit protections")
    player.ability = :BLAZE
    player.effects[PBEffects::TwoTurnAttack] = :DIG
    assert(["Hail", "Sandstorm"].all? { |label| protection_labels(overview(player)).include?(label) }, "Dig's active phase protects against weather chip")
    player.effects[PBEffects::TwoTurnAttack] = nil
    player.ability = :OVERCOAT
    assert(!protection_labels(overview(player)).include?("Powder"), "disabled modern powder protection is not imported")
    player.ability = :HEATPROOF
    assert(protection_labels(overview(player)).include?("Burn damage ×0.5"), "partial burn protection is not a burn immunity")
    player.ability = :SANDVEIL
    battle.field.weather = :Sandstorm
    assert(protection_labels(overview(player)).include?("Evasion ×1.25 (sandstorm)"), "conditional evasion is displayed while active")
    battle.field.weather = :None
    assert(!protection_labels(overview(player)).include?("Evasion ×1.25 (sandstorm)"), "inactive weather evasion disappears")
    player.ability = :BLAZE
  end
end
