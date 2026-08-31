module IronmonDefenseOverviewTests
  def self.assert(value, message)
    raise "Defense overview: #{message}" if !value
    @assertions = (@assertions || 0) + 1
  end

  def self.multiplier(snapshot, type)
    return snapshot["type_matchups"].find { |row| row["type"] == type.to_s }["multiplier"]
  end

  def self.rule(snapshot, id)
    return snapshot.values.select { |value| value.is_a?(Array) }.flatten.find { |row| row["id"] == id.to_s }
  end

  def self.overview(battler, enemy = false, known = nil)
    snapshot = Ironmon::DefenseOverview.new(battler.pokemon, battler, enemy, known && { "id" => known.to_s }).snapshot
    @visible_behavior ||= []
    profiles = snapshot["type_matchups"].map do |row|
      row.reject { |key, _| ["adjustments", "exceptions"].include?(key) }.transform_values do |value|
        value.is_a?(Float) ? value.round(9) : value
      end
    end
    @visible_behavior << [profiles, snapshot["protections"], snapshot["recovery"]]
    return snapshot
  end

  def self.profile(snapshot, type)
    return snapshot["type_matchups"].find { |row| row["type"] == type.to_s }
  end

  def self.test_combined_damage(battle, player, enemy)
    player.item = nil
    player.pbChangeTypes(:GRASS)
    player.ability = :THICKFAT
    thick_fat = overview(player)
    fire = profile(thick_fat, :FIRE)
    assert(fire["base_multiplier"] == 2 && fire["physical_min"] == 1 && fire["special_max"] == 1, "Thick Fat cancels Fire weakness in combined display")
    assert(profile(thick_fat, :ICE)["physical_min"] == 1, "Thick Fat also cancels Ice weakness")
    assert(profile(thick_fat, :WATER)["physical_min"] == 0.5, "unrelated resistance unchanged")
    assert(thick_fat["ability_description"] == GameData::Ability.get(:THICKFAT).description, "supporting metadata uses the game catalog description")
    assert(thick_fat["protections"].any? { |effect| effect["label"] == "Leech Seed" }, "protection labels describe only the defense")
    player.effects[PBEffects::GastroAcid] = true
    assert(profile(overview(player), :FIRE)["physical_min"] == 2, "suppressed Thick Fat no longer reduces damage")
    player.effects[PBEffects::GastroAcid] = false
    player.item = :OCCABERRY
    assert(profile(overview(player), :FIRE)["physical_min"] == 0.5, "berry combines with Thick Fat using the original chart trigger")
    player.item = nil
    player.ability = :FILTER
    assert(profile(overview(player), :FIRE)["physical_min"] == 1.5, "Filter combines with super-effective matchup")
    assert(profile(overview(player), :NORMAL)["physical_min"] == 1, "Filter does not reduce neutral damage")
    player.ability = :DRYSKIN
    assert(profile(overview(player), :FIRE)["special_max"] == 2.5, "Dry Skin amplifies Fire weakness")
    assert(profile(overview(player), :WATER)["physical_max"] == 0, "absorbed attacks remain immune")
    player.ability = :FLUFFY
    fire = profile(overview(player), :FIRE)
    normal = profile(overview(player), :NORMAL)
    assert(fire["physical_min"] == 2 && fire["physical_max"] == 4, "Fluffy combines Fire and contact factors without assuming contact")
    assert(normal["physical_min"] == 0.5 && normal["physical_max"] == 1, "Fluffy contact reduction applies beyond Fire")
    player.ability = :THICKFAT
    player.pbOwnSide.effects[PBEffects::Reflect] = 4
    fire = profile(overview(player), :FIRE)
    assert(fire["physical_max"] == 0.5 && fire["special_max"] == 1, "Reflect combines only with physical damage")
    player.pbOwnSide.effects[PBEffects::AuroraVeil] = 4
    fire = profile(overview(player), :FIRE)
    assert(fire["physical_max"] == 0.5 && fire["special_max"] == 0.5, "Aurora Veil does not double-count Reflect")
    player.pbOwnSide.effects[PBEffects::Reflect] = 0
    player.pbOwnSide.effects[PBEffects::AuroraVeil] = 0
    battle.field.weather = :Sun
    assert(profile(overview(player), :FIRE)["physical_max"] == 1.5, "weather stacks with Thick Fat and typing")
    battle.field.weather = :None
    unknown_hp = profile(overview(enemy, true, :MULTISCALE), :NORMAL)
    assert(unknown_hp["physical_min"] == 0.5 && unknown_hp["physical_max"] == 1, "concealed full-HP condition produces a range")
    enemy.hp = enemy.totalhp
    assert(profile(overview(enemy, true, :MULTISCALE), :NORMAL) == unknown_hp, "changing concealed HP does not narrow the range")
    player.ability = :DARKAURA
    enemy.ability = :DARKAURA
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, { 1 => { "id" => "DARKAURA" } })
    assert(profile(overview(player), :DARK)["physical_max"] == 4.0 / 3, "duplicate global auras do not stack")
    enemy.ability = :AURABREAK
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, { 1 => { "id" => "AURABREAK" } })
    assert(profile(overview(player), :DARK)["physical_max"] == 2.0 / 3, "known Aura Break reverses the global aura factor")
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, {})
    assert(profile(overview(player), :DARK)["physical_max"] == 4.0 / 3, "hidden Aura Break does not leak through damage factors")
    player.ability = :THICKFAT
    return thick_fat
  end

  def self.test_compact_effects(player, enemy)
    player.item = nil
    player.pbChangeTypes(:GRASS)
    player.effects[PBEffects::Type3] = :ICE
    player.ability = :ICEBODY
    ice_body = overview(player)
    labels = ice_body["protections"].map { |effect| effect["label"] }
    assert(labels.count("Hail") == 1, "overlapping weather protections collapse to one label")
    assert(labels.include?("Leech Seed") && labels.include?("Powder"), "compact protections omit immunity suffixes")
    assert(ice_body["recovery"].map { |effect| [effect["label"], effect["healing_amounts"]] } == [["Hail", ["+1/16 HP per turn"]]], "recovery separates its trigger from the healing amount")
    powder = ice_body["protections"].find { |effect| effect["label"] == "Powder" }
    assert(powder["moves"] == Ironmon::DefenseOverview.flagged_moves("l"), "powder expansion retains the exact installed move catalog")
    (ice_body["protections"] + ice_body["recovery"]).each do |effect|
      expected_keys = ice_body["recovery"].include?(effect) ? ["active", "healing_amounts", "label", "moves"] : ["active", "label", "moves"]
      assert(effect.keys.sort == expected_keys, "compact effects carry no source or explanation")
    end
    player.effects[PBEffects::GastroAcid] = true
    suppressed = overview(player)
    assert(suppressed["recovery"].empty?, "suppressed ability does not advertise recovery")
    assert(suppressed["protections"].any? { |effect| effect["label"] == "Hail" }, "typing keeps hail protection when ability is suppressed")
    player.effects[PBEffects::GastroAcid] = false
    player.effects[PBEffects::HealBlock] = 2
    assert(overview(player)["recovery"].empty?, "known Heal Block hides recovery")
    player.effects[PBEffects::HealBlock] = 0
    player.effects[PBEffects::Type3] = nil
    player.ability = :BLAZE
    player.item = :SAFETYGOGGLES
    labels = overview(player)["protections"].map { |effect| effect["label"] }
    assert(labels.include?("Hail") && labels.include?("Sandstorm"), "item weather defenses appear without item names")
    player.effects[PBEffects::Embargo] = 2
    assert(!overview(player)["protections"].any? { |effect| effect["label"] == "Hail" }, "inactive items cannot add compact protection")
    player.effects[PBEffects::Embargo] = 0
    player.item = :BLACKSLUDGE
    assert(overview(player)["recovery"].empty?, "damaging Black Sludge is not presented as healing")
    player.pbChangeTypes(:POISON)
    assert(overview(player)["recovery"].length == 1, "Poison holder has compact Black Sludge healing")
    player.item = nil
    enemy.ability = :ICEBODY
    enemy.item = :LEFTOVERS
    hidden = overview(enemy, true)
    assert(hidden["recovery"].empty? && !hidden["protections"].any? { |effect| effect["label"] == "Hail" }, "unrevealed ability and held item never add compact effects")
    assert(overview(enemy, true, :ICEBODY)["recovery"].length == 1, "revealed ability adds only its recovery, never the hidden item")
    return ice_body
  end

  def self.test_hydration(player, enemy)
    player.ability = :HYDRATION
    player.item = nil
    player.effects[PBEffects::GastroAcid] = false
    expected = [{ "label" => "Rain", "active" => true, "moves" => [], "healing_amounts" => ["Heals status conditions"] }]
    [:None, :Rain, :HeavyRain].each do |weather|
      player.battle.field.weather = weather
      assert(overview(player)["recovery"] == expected, "Hydration advertises the rain trigger without requiring current rain")
    end
    player.effects[PBEffects::HealBlock] = 2
    hydration = overview(player)
    assert(hydration["recovery"] == expected, "Heal Block does not prevent Hydration's status cure")
    player.item = :LEFTOVERS
    assert(overview(player)["recovery"] == expected, "Heal Block hides HP recovery while retaining status recovery")
    player.effects[PBEffects::GastroAcid] = true
    assert(overview(player)["recovery"].empty?, "suppressed Hydration supplies no recovery")
    player.effects[PBEffects::GastroAcid] = false
    player.effects[PBEffects::HealBlock] = 0
    player.item = nil
    player.battle.field.weather = :None
    enemy.ability = :HYDRATION
    assert(overview(enemy, true)["recovery"].empty?, "unrevealed Hydration is not exposed")
    enemy.instance_variable_set(:@status, :BURN)
    known = overview(enemy, true, :HYDRATION)["recovery"]
    enemy.instance_variable_set(:@status, :NONE)
    assert(known == expected && overview(enemy, true, :HYDRATION)["recovery"] == known, "known Hydration does not consult concealed enemy status")
    return hydration
  end

  def self.run
    catalog = Ironmon::DefenseCatalog.load
    literal = Ironmon::DefenseCatalog.decode('{"schema_version":1,"literal":"#{1+1}"}')
    assert(literal[:literal] == '#{1+1}', "audit data is parsed as JSON and never interpreted as Ruby")
    stale = JSON.parse(File.binread(Ironmon::DefenseCatalog::PATH))
    stale["source_catalog_hashes"] = { "moves.dat" => "previous-game-version" }
    stale["audit_revision"] = "previous-release"
    decoded = Ironmon::DefenseCatalog.decode(JSON.generate(stale))
    assert(decoded[:abilities] == catalog[:abilities], "older provenance does not invalidate audited ability data")
    assert(decoded[:source_catalog_hashes][:"moves.dat"] == "previous-game-version", "catalog provenance is retained without checking the installed game")
    assert(decoded.frozen? && decoded[:abilities].values.all?(&:frozen?), "loaded audit definitions cannot be mutated during presentation")
    $game_switches = Game_Switches.new
    $game_variables = Game_Variables.new
    $game_temp = Game_Temp.new
    $game_temp.in_battle = true
    $PokemonSystem = PokemonSystem.new
    $PokemonGlobal = PokemonGlobalMetadata.new
    $PokemonTemp = PokemonTemp.new
    pokemon = Pokemon.new(:CHARIZARD, 50, nil, false)
    opponent = Pokemon.new(:PIKACHU, 50, nil, false)
    pokemon.ability = :BLAZE
    opponent.ability = :STATIC
    battle = PokeBattle_Battle.new(nil, [pokemon], [opponent], nil, nil)
    player = PokeBattle_Battler.new(battle, 0)
    enemy = PokeBattle_Battler.new(battle, 1)
    battle.battlers[0] = player
    battle.battlers[1] = enemy
    player.pbInitialize(pokemon, 0)
    enemy.pbInitialize(opponent, 0)
    Ironmon.instance_variable_set(:@tracker_player_battler, player)
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, {})

    original = Marshal.dump([player.effects, player.stages, pokemon])
    chart = overview(player)
    assert(multiplier(chart, :ROCK) == 4, "dual-type weakness")
    assert(multiplier(chart, :GROUND) == 0, "Flying immunity")
    assert(original == Marshal.dump([player.effects, player.stages, pokemon]), "presentation does not mutate Pokemon or effects")
    player.item = :RINGTARGET
    assert(multiplier(overview(player), :GROUND) == 2, "Ring Target removes Flying chart immunity")
    player.ability = :LEVITATE
    assert(multiplier(overview(player), :GROUND) == 0, "Ring Target does not remove Levitate")
    player.item = :IRONBALL
    assert(multiplier(overview(player), :GROUND) == 1, "Iron Ball makes whole Ground matchup neutral")
    player.item = nil
    battle.field.effects[PBEffects::Gravity] = 3
    assert(multiplier(overview(player), :GROUND) == 2, "ordinary grounding preserves Fire weakness")
    battle.field.effects[PBEffects::Gravity] = 0
    player.ability = :BLAZE
    battle.field.weather = :StrongWinds
    assert(multiplier(overview(player), :ROCK) == 2, "Strong Winds changes Flying contribution only")
    battle.field.weather = :None
    player.effects[PBEffects::Type3] = :BUG
    assert(multiplier(overview(player), :ROCK) == 8, "three types preserve eight-times weakness")
    player.effects[PBEffects::Type3] = nil
    player.effects[PBEffects::Type3] = :GRASS
    assert(multiplier(overview(player), :GRASS) == 0.125, "three types preserve one-eighth resistance")
    player.effects[PBEffects::Type3] = nil
    player.pbChangeTypes(:NORMAL)
    player.ability = :WONDERGUARD
    assert(multiplier(overview(player), :NORMAL) == 0, "Wonder Guard blocks neutral damaging types")
    assert(multiplier(overview(player), :FIGHTING) == 2, "Wonder Guard retains weaknesses")
    player.effects[PBEffects::GastroAcid] = true
    assert(multiplier(overview(player), :NORMAL) == 1, "suppression restores neutral matchup")
    assert(rule(overview(player), :ability_WONDERGUARD)["active"] == false, "suppressed effect labeled inactive")
    player.effects[PBEffects::GastroAcid] = false
    player.ability = :SOUNDPROOF
    soundproof = overview(player)
    sound_moves = rule(soundproof, :ability_SOUNDPROOF)["moves"]
    expected = []
    GameData::Move.each { |move| expected << move.name if move.flags.include?("k") }
    assert(sound_moves == expected.sort, "Soundproof list matches installed flags")
    assert(sound_moves.include?(GameData::Move.get(:HYPERVOICE).name), "Soundproof contains Hyper Voice")
    player.ability = :BULLETPROOF
    assert(rule(overview(player), :ability_BULLETPROOF)["moves"].include?(GameData::Move.get(:SHADOWBALL).name), "Bulletproof contains Shadow Ball")

    player.ability = :MULTISCALE
    player.hp = player.totalhp
    assert(rule(overview(player), :ability_MULTISCALE)["active"], "full-HP protection available")
    player.hp -= 1
    assert(rule(overview(player), :ability_MULTISCALE)["active"] == false, "HP change updates protection")
    player.pbChangeTypes(:ELECTRIC)
    assert(rule(overview(player), :type_paralysis), "Electric status protection")
    player.ability = :LEVITATE
    battle.field.terrain = :Misty
    assert(rule(overview(player), :misty_status)["active"] == false, "airborne target does not receive terrain status protection")
    player.effects[PBEffects::SmackDown] = true
    assert(rule(overview(player), :misty_status)["active"], "grounding enables terrain protection")
    player.effects[PBEffects::SmackDown] = false
    battle.field.terrain = :None
    player.pbOwnSide.effects[PBEffects::Reflect] = 4
    player.pbOwnSide.effects[PBEffects::AuroraVeil] = 3
    assert(rule(overview(player), :Reflect)["active"] == false, "screens do not stack with Aurora Veil")
    player.pbOwnSide.effects[PBEffects::Reflect] = 0
    player.pbOwnSide.effects[PBEffects::AuroraVeil] = 0

    # Vary concealed state while public identity, types and effects stay fixed.
    hidden_before = overview(enemy, true)
    [:LEVITATE, :WONDERGUARD, :CLOUDNINE, :MULTISCALE, :MARVELSCALE].each do |ability|
      enemy.ability = ability
      enemy.item = :AIRBALLOON
      enemy.hp = 1
      enemy.instance_variable_set(:@status, :POISON)
      assert(overview(enemy, true) == hidden_before, "unrevealed #{ability}, item, HP and status do not leak")
    end
    known = overview(enemy, true, :LEVITATE)
    assert(multiplier(known, :GROUND) == 0, "individually revealed ability applies")
    assert(known["ability_name"] == GameData::Ability.get(:LEVITATE).name, "known ability is named")
    assert(rule(overview(enemy, true, :MULTISCALE), :ability_MULTISCALE)["active"].nil?, "hidden full HP remains conditional")
    assert(rule(overview(enemy, true, :MARVELSCALE), :ability_MARVELSCALE)["active"].nil?, "hidden status remains conditional")
    assert(rule(overview(enemy, true), :item_AIRBALLOON).nil?, "hidden item not named")
    battle.field.weather = :Sun
    baseline = overview(player)
    enemy.ability = :CLOUDNINE
    assert(overview(player) == baseline, "hidden global weather suppression cannot leak through player view")
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, { 1 => { "id" => "CLOUDNINE" } })
    assert(rule(overview(player), :sun_damage).nil?, "revealed weather suppression applies")
    Ironmon.instance_variable_set(:@tracker_enemy_abilities, {})
    battle.field.weather = :None

    # Every installed ability and item must be safe to present, including species restrictions.
    ability_count = 0
    GameData::Ability.each do |ability|
      player.ability = ability.id
      snapshot = overview(player)
      assert(snapshot["type_matchups"].length == 18, "ability #{ability.id} can be presented")
      ability_count += 1
    end
    player.ability = :BLAZE
    player.item = nil
    native_move = PokeBattle_Move.from_pokemon_move(battle, Pokemon::Move.new(:TACKLE))
    GameData::Type.each do |defense_type|
      player.pbChangeTypes(defense_type.id)
      snapshot = overview(player)
      Ironmon::DefenseCatalog.load[:attack_types].map(&:to_sym).each do |attack_type|
        expected = native_move.pbCalcTypeMod(attack_type, enemy, player).to_f / Effectiveness::NORMAL_EFFECTIVE
        assert(multiplier(snapshot, attack_type) == expected, "native chart #{attack_type} vs #{defense_type.id}, including composite types")
      end
    end
    player.pbChangeTypes(:ELECTRIC)
    item_count = 0
    GameData::Item.each do |item|
      player.item = item.id
      assert(overview(player)["type_matchups"].length == 18, "item #{item.id} can be presented")
      item_count += 1
    end
    assert(Ironmon.tracker_defense_snapshot(pokemon, player), "live snapshot bridge succeeds")
    pokemon.ability = :SOUNDPROOF
    offline = Ironmon::DefenseOverview.new(pokemon).snapshot
    assert(!offline["in_battle"], "outside-battle snapshot has no battle conditions")
    assert(rule(offline, :ability_SOUNDPROOF), "outside-battle ability rules available")
    thick_fat = test_combined_damage(battle, player, enemy)
    ice_body = test_compact_effects(player, enemy)
    hydration = test_hydration(player, enemy)
    coverage_examples = test_audited_coverage
    report = { "passed" => true, "assertions" => @assertions, "abilities" => ability_count,
               "items" => item_count, "sound_moves" => sound_moves.length,
               "example_player" => soundproof, "example_thick_fat" => thick_fat, "example_ice_body" => ice_body, "example_hydration" => hydration,
               "example_enemy" => overview(enemy, true, :MULTISCALE) }
    report.merge!(coverage_examples)
    File.binwrite($ironmon_defense_test_output_path, Ironmon.tracker_json_generate(report))
    File.binwrite($ironmon_defense_test_output_path + ".behavior.json", Ironmon.tracker_json_generate(@visible_behavior))
  end
end
IronmonDefenseOverviewTests.run
