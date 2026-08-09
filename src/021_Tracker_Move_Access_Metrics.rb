#===============================================================================
# Ironmon move-access post-run metrics
#===============================================================================

module Ironmon
  MOVE_ACCESS_METRICS_SCHEMA_VERSION = 1
  MOVE_ACCESS_METRIC_CHANNELS = [
    ["learnset", "learnset"],
    ["egg", "egg_moves"],
    ["machine", "machine_moves"],
    ["tutor", "tutor_moves"]
  ].freeze

  def self.reset_move_access_metrics
    return if !$PokemonGlobal
    $PokemonGlobal.ironmon_move_access_metrics = {
      "schema_version" => MOVE_ACCESS_METRICS_SCHEMA_VERSION,
      "encounters" => {},
      "machine_acquisitions" => [],
      "tutor_visits" => [],
      "move_acquisitions" => [],
      "move_uses" => {}
    }
  end

  def self.move_access_metrics
    return nil if !active? || !$PokemonGlobal
    metrics = $PokemonGlobal.ironmon_move_access_metrics
    if !metrics.is_a?(Hash) ||
       metrics["schema_version"] != MOVE_ACCESS_METRICS_SCHEMA_VERSION
      reset_move_access_metrics
      metrics = $PokemonGlobal.ironmon_move_access_metrics
    end
    return metrics
  end

  def self.record_move_access_encounter(pokemon, level, side)
    metrics = move_access_metrics
    return if !metrics || !pokemon || !move_access_randomization_active?
    species = pokemon.species_data
    key = [species.id.to_s, pokemon.form.to_i, level.to_i, side].join("|")
    record = metrics["encounters"][key]
    if record
      record["encounter_count"] += 1
      return
    end
    metrics["encounters"][key] = {
      "species_id" => species.id.to_s,
      "form" => pokemon.form.to_i,
      "species_name" => species.name,
      "level" => level.to_i,
      "side" => side,
      "encounter_count" => 1
    }
  rescue Exception => e
    echoln "Ironmon move-access encounter metric failed safely: #{e.message}"
  end

  def self.record_move_access_use(pokemon, move_id, side, copied)
    metrics = move_access_metrics
    move = GameData::Move.try_get(move_id)
    return if !metrics || !pokemon || !move
    species = pokemon.species_data
    key = [side, species.id, move.id, copied ? "copy" : "direct"].join("|")
    record = metrics["move_uses"][key]
    if record
      record["count"] += 1
    else
      metrics["move_uses"][key] = {
        "side" => side,
        "species_id" => species.id.to_s,
        "species_name" => species.name,
        "move_id" => move.id.to_s,
        "move_name" => move.name,
        "copied" => copied,
        "count" => 1
      }
    end
    record_move_access_acquisition(pokemon, move.id, "copy_effect") if copied
  rescue Exception => e
    echoln "Ironmon move-access use metric failed safely: #{e.message}"
  end

  def self.with_move_acquisition_source(source)
    previous = @move_acquisition_source
    @move_acquisition_source = source
    return yield
  ensure
    @move_acquisition_source = previous
  end

  def self.current_move_acquisition_source
    return @move_acquisition_source
  end

  def self.record_move_access_acquisition(pokemon, move_id, source = nil)
    metrics = move_access_metrics
    move = GameData::Move.try_get(move_id)
    return if !metrics || !pokemon || !move
    source ||= current_move_acquisition_source || "script"
    metrics["move_acquisitions"] << {
      "pokemon_id" => pokemon.personalID.to_s,
      "species_id" => pokemon.species_data.id.to_s,
      "species_name" => pokemon.species_data.name,
      "level" => pokemon.level,
      "move_id" => move.id.to_s,
      "move_name" => move.name,
      "source" => source
    }
  end

  def self.machine_inventory_snapshot
    return nil if !active? || !move_access_randomization_active? ||
      !$PokemonBag
    items = machine_item_roster(:tm) + machine_item_roster(:tr)
    return items.each_with_object({}) do |item, result|
      result[item.id] = $PokemonBag.pbQuantity(item.id)
    end
  end

  def self.record_new_machine_items(before, source)
    return if !before || !$Trainer || !$Trainer.party
    after = machine_inventory_snapshot
    return if !after
    after.each do |item_id, quantity|
      gained = quantity - before.fetch(item_id, 0)
      next if gained <= 0
      item = GameData::Item.get(item_id)
      compatible = $Trainer.party.select do |pokemon|
        next false if pokemon.egg?
        channel = machine_channel_for_item(item)
        with_machine_compatibility(channel) do
          pokemon.compatible_with_move?(item.move)
        end
      end
      move_access_metrics["machine_acquisitions"] << {
        "item_id" => item.id.to_s,
        "item_name" => item.name,
        "move_id" => item.move.to_s,
        "move_name" => GameData::Move.get(item.move).name,
        "quantity" => gained,
        "source" => source,
        "party_count" => $Trainer.party.length,
        "compatible_party" => compatible.map do |pokemon|
          {
            "pokemon_id" => pokemon.personalID.to_s,
            "species_name" => pokemon.species_data.name
          }
        end
      }
    end
  end

  def self.record_tutor_visit(move, source, taught_pokemon = nil,
                              tutor_id = nil, tutor_name = nil)
    metrics = move_access_metrics
    move_data = GameData::Move.try_get(move)
    return if !metrics || !move_data || !$Trainer || !$Trainer.party
    compatible = $Trainer.party.select do |pokemon|
      next false if pokemon.egg?
      case source
      when "ordinary_tutor"
        with_machine_compatibility(:tutor) do
          pokemon.compatible_with_move?(move_data.id)
        end
      when "egg"
        generated_egg_moves_for(pokemon.species_data).include?(move_data.id)
      else
        pokemon.equal?(taught_pokemon)
      end
    end
    metrics["tutor_visits"] << {
      "source" => source,
      "tutor_id" => tutor_id,
      "tutor_name" => tutor_name,
      "move_id" => move_data.id.to_s,
      "move_name" => move_data.name,
      "compatible_party" => compatible.map do |pokemon|
        {
          "pokemon_id" => pokemon.personalID.to_s,
          "species_name" => pokemon.species_data.name
        }
      end,
      "taught_pokemon_id" => taught_pokemon ? taught_pokemon.personalID.to_s : nil,
      "taught_species_name" => taught_pokemon ? taught_pokemon.species_data.name : nil
    }
  end

  def self.move_access_metrics_snapshot
    metrics = move_access_metrics
    return nil if !metrics || !move_access_randomization_active?
    recipe = tracker_debug_active_recipe
    encounters = metrics["encounters"].values.map do |record|
      move_access_encounter_snapshot(record, recipe)
    end.compact
    return {
      "schema_version" => MOVE_ACCESS_METRICS_SCHEMA_VERSION,
      "encounters" => encounters,
      "machine_acquisitions" => metrics["machine_acquisitions"],
      "tutor_visits" => metrics["tutor_visits"],
      "move_acquisitions" => metrics["move_acquisitions"],
      "move_uses" => metrics["move_uses"].values
    }
  rescue Exception => e
    echoln "Ironmon move-access metric finalization failed safely: #{e.message}"
    return nil
  end

  def self.move_access_encounter_snapshot(record, recipe)
    species = GameData::Species.try_get(record["species_id"])
    return nil if !species
    pokemon = Pokemon.new(species.id, [record["level"], 1].max)
    access = tracker_lookup_move_access(species, recipe, pokemon)
    channels = MOVE_ACCESS_METRIC_CHANNELS.map do |channel, key|
      move_access_channel_metric(species, channel, access[key], recipe)
    end
    all_ids = channels.flat_map { |channel| channel["move_ids"] }
    frequencies = all_ids.each_with_object(Hash.new(0)) do |move_id, counts|
      counts[move_id] += 1
    end
    learnset = access["learnset"]
    damaging = learnset.select { |entry| entry["power"].to_i > 0 }
    eligible = learnset.select do |entry|
      entry["learned_level"].to_i <= record["level"]
    end
    initial = eligible.last(Pokemon::MAX_MOVES)
    level_one = learnset.select { |entry| entry["learned_level"].to_i <= 1 }
    starting_level_one = level_one.last(Pokemon::MAX_MOVES)
    return record.merge(
      "is_fusion" => fusion_move_access_species?(species),
      "channels" => channels.map do |channel|
        channel.reject { |key, _value| key == "move_ids" }
      end,
      "unique_move_count" => frequencies.length,
      "cross_channel_overlap_count" => frequencies.count do |_move, count|
        count > 1
      end,
      "earliest_damaging_move_level" => damaging.empty? ? nil :
        damaging.map { |entry| entry["learned_level"].to_i }.min,
      "level_one_move_count" => level_one.length,
      "level_one_damaging_move_count" => starting_level_one.count do |entry|
        entry["power"].to_i > 0
      end,
      "level_one_guarantee_satisfied" => level_one.length >= 4 &&
        starting_level_one.any? { |entry| entry["power"].to_i > 0 },
      "initial_moves" => initial.map do |entry|
        {
          "move_id" => entry["id"],
          "move_name" => entry["name"],
          "category" => entry["category"],
          "power" => entry["power"]
        }
      end,
      "ordinary_tutor_abstract_count" =>
        access["ordinary_tutor_abstract_count"],
      "ordinary_tutor_supported_count" =>
        access["ordinary_tutor_supported_count"]
    )
  end

  def self.move_access_channel_metric(species, channel, entries, recipe)
    ids = entries.map { |entry| entry["id"] }.uniq
    metric = {
      "channel" => channel,
      "generated_entry_count" => entries.length,
      "unique_move_count" => ids.length,
      "move_ids" => ids,
      "component_entry_count" => nil,
      "duplicate_removals" => nil,
      "growth_over_larger_component" => nil
    }
    return metric if !fusion_move_access_species?(species)
    generator = tracker_move_access_generator(recipe)
    component = tracker_move_access_component_moves(species, generator, true)
    body, head = move_access_component_channel_moves(component, channel)
    component_count = body.length + head.length
    fused_count = if channel == "tutor"
                    generated_ordinary_tutor_moves_for(species, generator).length
                  else
                    ids.length
                  end
    metric["component_entry_count"] = component_count
    metric["duplicate_removals"] = component_count - fused_count
    metric["growth_over_larger_component"] = fused_count -
      [body.length, head.length].max
    return metric
  end

  def self.move_access_component_channel_moves(component, channel)
    case channel
    when "learnset"
      return [component[:level_body] || [], component[:level_head] || []]
    when "egg"
      return [component[:egg_body] || [], component[:egg_head] || []]
    when "machine"
      body = (component[:tm_body] || []) + (component[:tr_body] || [])
      head = (component[:tm_head] || []) + (component[:tr_head] || [])
      return [body.uniq, head.uniq]
    when "tutor"
      return [component[:tutor_body] || [], component[:tutor_head] || []]
    end
    return [[], []]
  end
end

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
