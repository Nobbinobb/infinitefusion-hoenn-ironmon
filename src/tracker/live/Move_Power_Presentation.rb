#===============================================================================
# Ironmon tracker move-power presentation
#===============================================================================

module Ironmon
  MOVE_POWER_PRESENTATION_CATALOG_PATH = File.join(
    "Data", "Ironmon", "move_power_presentation.json"
  )
  MOVE_POWER_UNKNOWN = "???"
  MOVE_POWER_ONE_HIT_KO = "KO"

  class TrackerMovePokemonAdapter
    attr_reader :pokemon
    attr_reader :effects
    attr_reader :stages

    def initialize(pokemon)
      @pokemon = pokemon
      @effects = Hash.new(0)
      @stages = Hash.new(0)
    end

    def happiness; return @pokemon.happiness; end
    def hp; return @pokemon.hp; end
    def totalhp; return @pokemon.totalhp; end
    def level; return @pokemon.level; end
    def status; return @pokemon.status; end
    def iv; return @pokemon.iv; end
    def pbSpeed; return @pokemon.speed; end
    def pbWeight; return @pokemon.weight; end
    def item_id; return @pokemon.item; end
    def item; return GameData::Item.try_get(@pokemon.item); end
    def itemActive?; return !item.nil?; end
    def poisoned?; return [:POISON, :POISONED].include?(status); end
    def burned?; return [:BURN, :BURNED].include?(status); end
    def paralyzed?; return [:PARALYSIS, :PARALYZED].include?(status); end
    def hasActiveAbility?(ability, _ignore_fainted = false)
      return @pokemon.ability_id == ability
    end
  end

  def self.tracker_move_power_catalog
    return @tracker_move_power_catalog if @tracker_move_power_catalog
    document = File.open(MOVE_POWER_PRESENTATION_CATALOG_PATH, "rb") do |file|
      tracker_move_power_stringify_keys(JSON.parse(file.read))
    end
    raise "unsupported move-power presentation catalog" if
      document["version"].to_i != 1
    @tracker_move_power_catalog = {}
    document["moves"].each do |move|
      @tracker_move_power_catalog[move["id"]] = move.freeze
    end
    return @tracker_move_power_catalog.freeze
  end

  def self.tracker_move_power_stringify_keys(value)
    return value.map { |entry| tracker_move_power_stringify_keys(entry) } if
      value.is_a?(Array)
    if value.is_a?(Hash)
      result = {}
      value.each do |key, entry|
        result[key.to_s] = tracker_move_power_stringify_keys(entry)
      end
      return result
    end
    return value
  end

  def self.tracker_move_power_catalog_ids
    return tracker_move_power_catalog.keys
  end

  def self.tracker_move_power_presentation(move, pokemon, battler = nil,
                                           enemy = false)
    definition = tracker_move_power_catalog[move.id.to_s]
    return nil if !definition
    native_move = tracker_native_move_for_presentation(move, battler)
    user = battler || TrackerMovePokemonAdapter.new(pokemon)
    target = tracker_move_power_target(battler)
    sensitive = enemy && definition["enemy_sensitive"]
    type = tracker_move_power_type(
      move, definition, native_move, user, battler, sensitive
    )
    presentation = tracker_move_power_display(
      move, pokemon, definition, native_move, user, target, battler, enemy,
      sensitive
    )
    presentation["type"] = type.to_s if type
    return presentation
  rescue Exception => e
    echoln "Ironmon move-power presentation failed safely for #{move.id}: #{e.message}"
    return {
      "display" => MOVE_POWER_UNKNOWN,
      "indicator" => "none"
    }
  end

  def self.tracker_native_move_for_presentation(move, battler)
    existing = battler.moves.find { |candidate| candidate.id == move.id } if
      battler && battler.respond_to?(:moves)
    return existing.dup if existing
    battle = battler ? @tracker_battle : nil
    return PokeBattle_Move.from_pokemon_move(battle, move).dup
  end

  def self.tracker_move_power_target(battler)
    return nil if !battler
    return @tracker_player_battler if battler.index.odd?
    return nil if !@tracker_enemy_battlers
    return @tracker_enemy_battlers.keys.sort.map do |position|
      @tracker_enemy_battlers[position]
    end.find { |candidate| candidate && !candidate.fainted? }
  end

  def self.tracker_move_power_type(move, definition, native_move, user,
                                   battler, sensitive)
    return move.type if !definition["dynamic_type"] || sensitive
    return move.type if !battler && !definition["offline_native"]
    type = native_move.pbBaseType(user)
    return :NEUTRAL if Settings::TRIPLE_TYPES.include?(type)
    return type
  rescue Exception
    return move.type
  end

  def self.tracker_move_power_display(move, pokemon, definition, native_move,
                                      user, target, battler, enemy, sensitive)
    mode = definition["mode"]
    return tracker_power_presentation(MOVE_POWER_UNKNOWN) if sensitive
    case mode
    when "condition"
      return tracker_power_presentation(move.base_damage)
    when "fixed_damage"
      return tracker_power_presentation(definition["fixed_value"], "fixed_damage")
    when "level_damage"
      return tracker_power_presentation(pokemon.level, "fixed_damage")
    when "hidden_fixed_damage"
      return tracker_power_presentation(MOVE_POWER_UNKNOWN)
    when "unknown_damage"
      return tracker_power_presentation(MOVE_POWER_UNKNOWN)
    when "one_hit_ko"
      return tracker_power_presentation(MOVE_POWER_ONE_HIT_KO)
    when "present"
      return tracker_present_power_presentation
    when "magnitude"
      return tracker_magnitude_power_presentation(native_move, user, target, battler)
    when "psywave"
      return tracker_psywave_power_presentation(pokemon.level)
    when "triple_kick"
      return tracker_triple_kick_power_presentation(move, pokemon, battler, enemy)
    when "water_shuriken"
      return tracker_water_shuriken_power_presentation(
        move, pokemon, definition, battler, enemy
      )
    when "beat_up"
      return tracker_power_presentation(
        5 + pokemon.baseStats[:ATTACK].to_i / 10
      )
    when "double_iron_bash"
      power = tracker_calculated_move_power(
        move, definition, native_move, user, target, battler
      )
      return tracker_power_presentation(power.to_i * 2)
    when "current_hp_damage"
      return tracker_power_presentation(pokemon.hp)
    when "random_multi_hit"
      return tracker_random_multi_hit_power_presentation(
        move, pokemon, battler, enemy
      )
    when "fixed_multi_hit"
      return tracker_power_presentation(move.base_damage.to_i * 2)
    end
    power = tracker_calculated_move_power(
      move, definition, native_move, user, target, battler
    )
    return tracker_power_presentation(power)
  end

  def self.tracker_calculated_move_power(move, definition, native_move, user,
                                         target, battler)
    return move.base_damage if !battler && !definition["offline_native"]
    return move.base_damage if battler && !target
    counter_power = tracker_prospective_counter_power(
      move, definition, user, battler
    )
    return counter_power if counter_power
    tracker_prepare_native_move_power(move, definition, native_move, battler)
    base_damage = native_move.pbBaseDamage(move.base_damage, user, target)
    multiplier = 1.0
    if target && target.effects[PBEffects::Minimize] &&
       native_move.tramplesMinimize?(2)
      multiplier *= 2
    end
    multiplier = native_move.pbBaseDamageMultiplier(multiplier, user, target)
    multiplier = native_move.pbModifyDamage(multiplier, user, target)
    return (base_damage * multiplier).round
  rescue Exception
    return move.base_damage
  end

  def self.tracker_prospective_counter_power(move, definition, user, battler)
    effect_name = definition["prospective_counter_effect"]
    return nil if !effect_name || !battler
    container = if definition["prospective_counter_scope"] == "side"
                  user.pbOwnSide.effects
                else
                  user.effects
                end
    counter = container[PBEffects.const_get(effect_name)].to_i
    used_effect = definition["prospective_counter_used_effect"]
    counter += 1 if !used_effect ||
      container[PBEffects.const_get(used_effect)] != true
    counter = 1 if counter < 1
    maximum = definition["prospective_counter_maximum"]
    counter = [counter, maximum.to_i].min if maximum
    power = case definition["prospective_counter_growth"]
            when "doubling"
              move.base_damage.to_i * (2 ** (counter - 1))
            else
              move.base_damage.to_i * counter
            end
    maximum_power = definition["prospective_counter_maximum_power"]
    power = [power, maximum_power.to_i].min if maximum_power
    return power
  end

  def self.tracker_prepare_native_move_power(move, definition, native_move,
                                             battler)
    if definition["decrement_pp_before_power"]
      native_move.pp = [move.pp.to_i - 1, 0].max
    end
    effect_name = definition["prior_field_effect"]
    return if !effect_name || !battler || !@tracker_battle
    effect = PBEffects.const_get(effect_name)
    native_move.instance_variable_set(
      :@doublePower, @tracker_battle.field.effects[effect]
    )
  end

  def self.tracker_power_presentation(display, indicator = "none",
                                      details_kind = "none", outcomes = [])
    return {
      "display" => display.to_s,
      "indicator" => indicator,
      "details_kind" => details_kind,
      "outcomes" => outcomes
    }
  end

  def self.tracker_present_power_presentation
    outcomes = [
      tracker_power_outcome(40, 40),
      tracker_power_outcome(80, 30),
      tracker_power_outcome(120, 10),
      {
        "kind" => "healing",
        "healing_percent" => 25,
        "chance_percent" => 20
      }
    ]
    return tracker_power_presentation(
      MOVE_POWER_UNKNOWN, "none", "outcomes", outcomes
    )
  end

  def self.tracker_magnitude_power_presentation(native_move, user, target,
                                                battler)
    multiplier = 1.0
    multiplier = native_move.pbModifyDamage(multiplier, user, target) if
      battler && target
    powers = [10, 30, 50, 70, 90, 110, 150]
    chances = [5, 10, 20, 30, 20, 10, 5]
    outcomes = powers.each_with_index.map do |power, index|
      tracker_power_outcome((power * multiplier).round, chances[index])
    end
    return tracker_power_presentation(
      MOVE_POWER_UNKNOWN, "none", "outcomes", outcomes
    )
  end

  def self.tracker_psywave_power_presentation(level)
    outcomes = [{
      "kind" => "range",
      "minimum" => (level / 2.0).floor,
      "maximum" => (3 * level / 2.0).floor
    }]
    return tracker_power_presentation(
      MOVE_POWER_UNKNOWN, "none", "range", outcomes
    )
  end

  def self.tracker_triple_kick_power_presentation(move, pokemon, battler, enemy)
    skill_link = tracker_move_skill_link?(pokemon, battler, enemy)
    display = skill_link ? move.base_damage.to_i * 6 : move.base_damage
    outcomes = [1, 3, 6].map do |multiplier|
      { "kind" => "power", "power" => move.base_damage.to_i * multiplier }
    end
    presentation = tracker_power_presentation(
      display, skill_link ? "none" : "multi_hit", "triple_kick", outcomes
    )
    presentation["accuracy_checked_per_hit"] = !skill_link
    return presentation
  end

  def self.tracker_water_shuriken_power_presentation(move, pokemon, definition,
                                                     battler, enemy)
    fixed_form = pokemon.species == definition["fixed_form_species"].to_sym &&
      pokemon.form == definition["fixed_form"]
    power = fixed_form ? definition["fixed_form_power"] : move.base_damage.to_i
    if fixed_form
      total_power = power * definition["fixed_form_hits"]
      outcomes = [tracker_hit_outcome(
        definition["fixed_form_hits"], total_power, 100
      )]
      return tracker_power_presentation(
        total_power, "none", "outcomes", outcomes
      )
    end
    return tracker_random_multi_hit_power_presentation(
      move, pokemon, battler, enemy
    )
  end

  def self.tracker_random_multi_hit_power_presentation(move, pokemon, battler,
                                                       enemy)
    skill_link = tracker_move_skill_link?(pokemon, battler, enemy)
    display = skill_link ? move.base_damage.to_i * 5 : move.base_damage
    hits = skill_link ? [5] : [2, 3, 4, 5]
    chances = skill_link ? [100] : [100.0 / 3, 100.0 / 3, 100.0 / 6, 100.0 / 6]
    outcomes = hits.each_with_index.map do |count, index|
      tracker_hit_outcome(count, move.base_damage.to_i * count, chances[index])
    end
    return tracker_power_presentation(
      display, skill_link ? "none" : "multi_hit", "outcomes", outcomes
    )
  end

  def self.tracker_move_skill_link?(pokemon, battler, enemy)
    if enemy
      known = @tracker_enemy_abilities && battler ?
        @tracker_enemy_abilities[battler.index] : nil
      return known && known["id"] == "SKILLLINK"
    end
    return battler.hasActiveAbility?(:SKILLLINK) if battler
    return pokemon.ability_id == :SKILLLINK
  end

  def self.tracker_power_outcome(power, chance)
    return {
      "kind" => "power",
      "power" => power,
      "chance_percent" => chance
    }
  end

  def self.tracker_hit_outcome(hits, power, chance)
    return {
      "kind" => "hits",
      "hits" => hits,
      "power" => power,
      "chance_percent" => chance.round(1)
    }
  end
end
