#===============================================================================
# Ironmon move-access metric engine integration
#===============================================================================

alias ironmon_metrics_original_pb_item_ball pbItemBall
def pbItemBall(item, quantity = 1, item_name = "", canRandom = true)
  before = Ironmon.machine_inventory_snapshot
  result = ironmon_metrics_original_pb_item_ball(
    item, quantity, item_name, canRandom
  )
  Ironmon.record_new_machine_items(before, "found") if result
  return result
end

alias ironmon_metrics_original_pb_receive_item pbReceiveItem
def pbReceiveItem(item, quantity = 1, item_name = "", music = nil,
                  canRandom = true)
  before = Ironmon.machine_inventory_snapshot
  result = ironmon_metrics_original_pb_receive_item(
    item, quantity, item_name, music, canRandom
  )
  Ironmon.record_new_machine_items(before, "script") if result
  return result
end

alias ironmon_metrics_original_pb_learn_move pbLearnMove
def pbLearnMove(pkmn, move, ignoreifknown = false, bymachine = false,
                fast = false, &block)
  source = Ironmon.current_move_acquisition_source
  if !source && bymachine
    channel = Ironmon.machine_compatibility_channel
    source = channel == :tutor ? "tutor" : "tm"
  end
  result = Ironmon.with_move_acquisition_source(source || "script") do
    ironmon_metrics_original_pb_learn_move(
      pkmn, move, ignoreifknown, bymachine, fast, &block
    )
  end
  Ironmon.record_move_access_acquisition(pkmn, move, source) if result
  return result
end

alias ironmon_metrics_original_pb_move_tutor_choose pbMoveTutorChoose
def pbMoveTutorChoose(move, movelist = nil, bymachine = false,
                      oneusemachine = false, selectedPokemonVariable = nil)
  move_id = GameData::Move.get(move).id
  slot = Ironmon.current_ordinary_tutor_slot if !bymachine
  offering = slot ? Ironmon.ordinary_tutor_offering(slot) : move_id
  before = $Trainer.party.each_with_object({}) do |pokemon, result|
    result[pokemon.personalID] = pokemon.moves.map { |known| known.id }
  end
  acquisition_source = bymachine ? "tm" : "tutor"
  result = Ironmon.with_move_acquisition_source(acquisition_source) do
    ironmon_metrics_original_pb_move_tutor_choose(
      move, movelist, bymachine, oneusemachine, selectedPokemonVariable
    )
  end
  taught = result ? $Trainer.party.find do |pokemon|
    !before.fetch(pokemon.personalID, []).include?(offering) &&
      pokemon.hasMove?(offering)
  end : nil
  Ironmon.record_tutor_visit(
    offering, "ordinary_tutor", taught, slot ? slot[:id] : nil,
    slot ? slot[:location] : nil
  ) if !bymachine
  return result
end

alias ironmon_metrics_original_pb_relearn_egg_move_screen pbRelearnEggMoveScreen
def pbRelearnEggMoveScreen(pkmn)
  before = pkmn.moves.map { |move| move.id }
  result = Ironmon.with_move_acquisition_source("egg") do
    ironmon_metrics_original_pb_relearn_egg_move_screen(pkmn)
  end
  learned = pkmn.moves.map { |move| move.id }.find do |move|
    !before.include?(move)
  end
  Ironmon.record_tutor_visit(learned, "egg", pkmn, "egg_move_tutor",
                             "Egg Move Tutor") if learned
  return result
end

alias ironmon_metrics_original_pb_special_tutor pbSpecialTutor
def pbSpecialTutor(pokemon, legendaries = false)
  before = pokemon.moves.map { |move| move.id }
  source = legendaries ? "fusion_tutor_legendary" : "fusion_tutor_regular"
  result = Ironmon.with_move_acquisition_source("tutor") do
    ironmon_metrics_original_pb_special_tutor(pokemon, legendaries)
  end
  learned = pokemon.moves.map { |move| move.id }.find do |move|
    !before.include?(move)
  end
  Ironmon.record_tutor_visit(
    learned, source, pokemon, source, "Fusion Move Tutor"
  ) if learned
  return result
end

module IronmonMoveAccessBattleLearningMetrics
  def pbLearnMove(idxParty, newMove)
    pokemon = pbParty(0)[idxParty]
    before = pokemon ? pokemon.moves.map { |move| move.id } : []
    result = super
    if pokemon && !before.include?(newMove) && pokemon.hasMove?(newMove)
      Ironmon.record_move_access_acquisition(pokemon, newMove, "level_up")
    end
    return result
  end
end

PokeBattle_Battle.prepend(IronmonMoveAccessBattleLearningMetrics)

class Pokemon
  alias ironmon_metrics_original_learn_move learn_move

  def learn_move(move_id)
    already_known = hasMove?(move_id)
    result = ironmon_metrics_original_learn_move(move_id)
    if !already_known && hasMove?(move_id) &&
       !Ironmon.current_move_acquisition_source && $Trainer &&
       $Trainer.party.include?(self)
      Ironmon.record_move_access_acquisition(self, move_id, "script")
    end
    return result
  end
end
