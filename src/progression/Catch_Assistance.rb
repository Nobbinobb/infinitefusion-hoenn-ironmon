#===============================================================================
# Ironmon wild-capture assistance
#===============================================================================

module Ironmon
  CATCH_ASSISTANCE_BONUS = 81.0
  CATCH_ASSISTANCE_EXPONENT = 3
  CATCH_ASSISTANCE_DAMAGE_MULTIPLIER = 2.0
  CAPTURE_RATE_MAXIMUM = 255.0
  CAPTURE_SHAKE_MAXIMUM = 65_536.0
  ULTRA_BEAST_SPECIES = [
    :NIHILEGO, :BUZZWOLE, :PHEROMOSA, :XURKITREE, :CELESTEELA,
    :KARTANA, :GUZZLORD, :POIPOLE, :NAGANADEL, :STAKATAKA,
    :BLACEPHALON
  ].freeze

  def self.catch_assistance_battle?(battle)
    return false if !active? || !battle || !battle.respond_to?(:wildBattle?)
    return false if defined?(PokeBattle_SafariZone) &&
                    battle.is_a?(PokeBattle_SafariZone)
    return battle.wildBattle?
  end

  def self.assisted_catch_rate(catch_rate, maximum_hp, current_hp)
    rate = [[catch_rate.to_f, 0.0].max, CAPTURE_RATE_MAXIMUM].min
    return rate if maximum_hp.to_i <= 0
    hp_fraction = current_hp.to_f / maximum_hp.to_f
    missing_hp_fraction = [[1.0 - hp_fraction, 0.0].max, 1.0].min
    damage_factor = [
      missing_hp_fraction * CATCH_ASSISTANCE_DAMAGE_MULTIPLIER, 1.0
    ].min
    remaining_rate_fraction = 1.0 - (rate / CAPTURE_RATE_MAXIMUM)
    bonus = CATCH_ASSISTANCE_BONUS *
      (remaining_rate_fraction ** CATCH_ASSISTANCE_EXPONENT) * damage_factor
    return [rate + bonus, CAPTURE_RATE_MAXIMUM].min
  end

  def self.poke_ball_catch_chance_percent(pokemon, battler,
                                          caught_off_guard = false)
    return nil if !pokemon || !battler
    catch_rate = assisted_catch_rate(
      pokemon.species_data.catch_rate, battler.totalhp, battler.hp
    )
    catch_rate /= 10.0 if ULTRA_BEAST_SPECIES.include?(pokemon.species)
    if caught_off_guard
      catch_rate = [catch_rate * 1.5, CAPTURE_RATE_MAXIMUM].min
    end
    return capture_success_chance_percent(
      catch_rate, battler.totalhp, battler.hp, battler.status
    )
  end

  def self.capture_success_chance_percent(catch_rate, maximum_hp, current_hp,
                                          status)
    return 0.0 if maximum_hp.to_i <= 0
    health_value = ((3 * maximum_hp - 2 * current_hp) * catch_rate.to_f) /
      (3 * maximum_hp)
    if status == :SLEEP || status == :FROZEN
      health_value *= 2.5
    elsif status != :NONE
      health_value *= 1.5
    end
    health_value = health_value.floor
    health_value = 1 if health_value < 1
    return 100.0 if health_value >= CAPTURE_RATE_MAXIMUM
    shake_threshold = (
      CAPTURE_SHAKE_MAXIMUM /
      ((CAPTURE_RATE_MAXIMUM / health_value) ** 0.1875)
    ).floor
    chance = (shake_threshold / CAPTURE_SHAKE_MAXIMUM) ** 4
    return (chance * 100.0).round(1)
  end
end

module PokeBattle_BattleCommon
  alias ironmon_original_pb_capture_calc pbCaptureCalc
  def pbCaptureCalc(pokemon, battler, catch_rate, ball)
    if !catch_rate && Ironmon.catch_assistance_battle?(self)
      catch_rate = Ironmon.assisted_catch_rate(
        pokemon.species_data.catch_rate, battler.totalhp, battler.hp
      )
    end
    return ironmon_original_pb_capture_calc(
      pokemon, battler, catch_rate, ball
    )
  end
end
