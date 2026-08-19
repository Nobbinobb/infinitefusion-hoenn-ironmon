#===============================================================================
# Ironmon move-access engine and event integration
#===============================================================================

module Ironmon
  def self.machine_channel_for_item(item)
    item_data = GameData::Item.try_get(item)
    return nil if !item_data
    return :tr if item_data.is_TR?
    return :tm if item_data.is_TM?
    return nil
  end

  def self.machine_channel_for_move(move)
    in_tm = raw_machine_move_lookup(:tm)[move]
    in_tr = raw_machine_move_lookup(:tr)[move]
    return :machine if in_tm && in_tr
    return :tr if in_tr
    return :tm if in_tm
    return nil
  end

  def self.with_machine_compatibility(channel)
    previous = @machine_compatibility_channel
    @machine_compatibility_channel = channel
    return yield
  ensure
    @machine_compatibility_channel = previous
  end

  def self.machine_compatibility_channel
    return @machine_compatibility_channel
  end

  def self.generated_machine_compatible?(pokemon, move, channel = nil)
    species_data = pokemon.species_data
    channel ||= machine_compatibility_channel
    if channel == :machine
      return true if generated_machine_moves_for(species_data, :tm).include?(move)
      return generated_machine_moves_for(species_data, :tr).include?(move)
    end
    return false if channel != :tm && channel != :tr
    return generated_machine_moves_for(species_data, channel).include?(move)
  end

  def self.generated_ordinary_tutor_compatible?(pokemon, move)
    offerings = move_access_generator.ordinary_tutor_offerings.values
    return false if !offerings.include?(move)
    compatible = generated_ordinary_tutor_moves_for(pokemon.species_data)
    return compatible.include?(move)
  end

  def self.ordinary_tutor_slot_lookup
    return @ordinary_tutor_slot_lookup if @ordinary_tutor_slot_lookup
    lookup = {}
    ORDINARY_TUTOR_SLOTS.each do |slot|
      lookup[[slot[:map_id], slot[:event_id]]] = slot
    end
    @ordinary_tutor_slot_lookup = lookup.freeze
    return @ordinary_tutor_slot_lookup
  end

  def self.current_ordinary_tutor_slot
    interpreter = pbMapInterpreter
    return nil if !interpreter
    map_id = interpreter.instance_variable_get(:@map_id)
    event_id = interpreter.instance_variable_get(:@event_id)
    return ordinary_tutor_slot_lookup[[map_id, event_id]]
  end

  def self.ordinary_tutor_offering(slot, generator = nil)
    return nil if !slot
    generator ||= move_access_generator
    return generator.ordinary_tutor_offering_for(slot[:id])
  end

  def self.patch_ordinary_tutor_event(event, slot, generator = nil)
    generator ||= move_access_generator
    page = event.pages[slot[:page_index]]
    return false if !page
    backups = event.instance_variable_get(:@ironmon_tutor_page_backups)
    if !backups
      backups = {}
      event.instance_variable_set(:@ironmon_tutor_page_backups, backups)
    end
    backup = backups[slot[:page_index]]
    if !backup
      backup = page.list.map do |command|
        Marshal.dump(command.parameters)
      end
      backups[slot[:page_index]] = backup
    else
      page.list.each_with_index do |command, index|
        command.parameters.replace(Marshal.load(backup[index]))
      end
    end
    offering = ordinary_tutor_offering(slot, generator)
    original_token = ":#{slot[:original_move]}"
    offering_token = ":#{offering}"
    original_name = GameData::Move.get(slot[:original_move]).real_name
    offering_name = GameData::Move.get(offering).real_name
    page.list.each do |command|
      command.parameters.each do |parameter|
        next if !parameter.is_a?(String)
        if command.code == 355 || command.code == 655 || command.code == 111
          parameter.gsub!(/#{Regexp.escape(original_token)}\b/, offering_token)
        elsif command.code == 101 || command.code == 401
          parameter.gsub!(/#{Regexp.escape(original_name)}/i, offering_name)
        end
      end
    end
    return true
  end

  def self.patch_ordinary_tutor_map(map_id, map, generator = nil)
    return false if !move_access_randomization_active?
    patched = false
    ORDINARY_TUTOR_SLOTS.each do |slot|
      next if slot[:map_id] != map_id
      event = map.events[slot[:event_id]]
      next if !event
      patched = true if patch_ordinary_tutor_event(event, slot, generator)
    end
    return patched
  end

  def self.patch_loaded_ordinary_tutor_events
    return false if !$game_map || !$game_map.events
    patched = false
    $game_map.events.each_value do |game_event|
      slot = ordinary_tutor_slot_lookup[[$game_map.map_id, game_event.id]]
      next if !slot
      event = game_event.instance_variable_get(:@event)
      next if !event
      if patch_ordinary_tutor_event(event, slot)
        game_event.refresh
        patched = true
      end
    end
    return patched
  end
end

class GameData::Species
  alias ironmon_unrandomized_moves moves
  alias ironmon_unrandomized_egg_moves egg_moves
  alias ironmon_unrandomized_tutor_moves tutor_moves

  def moves
    return ironmon_unrandomized_moves if
      !Ironmon.move_access_randomization_active?
    return Ironmon.generated_level_up_moves_for(self)
  end

  def egg_moves
    return ironmon_unrandomized_egg_moves if
      !Ironmon.move_access_randomization_active?
    return Ironmon.generated_egg_moves_for(self)
  end
end

class Pokemon
  alias ironmon_move_access_original_compatible_with_move? compatible_with_move?

  def compatible_with_move?(move_id)
    move_data = GameData::Move.try_get(move_id)
    channel = Ironmon.machine_compatibility_channel
    if Ironmon.move_access_randomization_active? && move_data && channel
      if channel == :tutor
        return Ironmon.generated_ordinary_tutor_compatible?(
          self, move_data.id
        )
      end
      return Ironmon.generated_machine_compatible?(self, move_data.id, channel)
    end
    return ironmon_move_access_original_compatible_with_move?(move_id)
  end
end

alias ironmon_machine_original_pb_move_tutor_choose pbMoveTutorChoose
def pbMoveTutorChoose(move, movelist = nil, bymachine = false,
                      oneusemachine = false, selectedPokemonVariable = nil)
  if Ironmon.move_access_randomization_active? && bymachine
    channel = Ironmon.machine_compatibility_channel
    channel ||= Ironmon.machine_channel_for_move(GameData::Move.get(move).id)
    return Ironmon.with_machine_compatibility(channel) do
      ironmon_machine_original_pb_move_tutor_choose(
        move, movelist, bymachine, oneusemachine, selectedPokemonVariable
      )
    end
  end
  if Ironmon.move_access_randomization_active? && !bymachine
    slot = Ironmon.current_ordinary_tutor_slot
    if slot
      offering = Ironmon.ordinary_tutor_offering(slot)
      return Ironmon.with_machine_compatibility(:tutor) do
        ironmon_machine_original_pb_move_tutor_choose(
          offering, movelist, bymachine, oneusemachine,
          selectedPokemonVariable
        )
      end
    end
  end
  return ironmon_machine_original_pb_move_tutor_choose(
    move, movelist, bymachine, oneusemachine, selectedPokemonVariable
  )
end

alias ironmon_tutor_original_pb_move_tutor_battle pbMoveTutorBattle
def pbMoveTutorBattle(trainerID, trainerName, moves, scaleLevel = true,
                      event_id = nil, map_id = nil)
  if Ironmon.move_access_randomization_active?
    slot = Ironmon.current_ordinary_tutor_slot
    if slot
      offering = Ironmon.ordinary_tutor_offering(slot)
      if moves.is_a?(Array)
        moves = moves.map do |move|
          move == slot[:original_move] ? offering : move
        end
      elsif moves == slot[:original_move]
        moves = offering
      end
    end
  end
  return ironmon_tutor_original_pb_move_tutor_battle(
    trainerID, trainerName, moves, scaleLevel, event_id, map_id
  )
end

class FusionTutorService
  alias ironmon_move_access_original_get_compatible_moves getCompatibleMoves

  def getCompatibleMoves(includeLegendaries = false)
    if !Ironmon.move_access_randomization_active?
      return ironmon_move_access_original_get_compatible_moves(
        includeLegendaries
      )
    end
    channel = Ironmon.specialized_tutor_channel(includeLegendaries)
    if @show_full_list
      return Ironmon.generated_specialized_tutor_catalog(channel)
    end
    return Ironmon.generated_specialized_tutor_moves_for(@pokemon, channel)
  end
end

alias ironmon_move_access_original_rare_tutor_example showRandomRareMoveConditionExample
def showRandomRareMoveConditionExample(legendary = false)
  if Ironmon.move_access_randomization_active?
    category = legendary ? _INTL("legendary") : _INTL("regular")
    pbMessage(_INTL(
      "Each fusion keeps its original number of compatible {1} moves, but the moves themselves are randomized for this run.",
      category
    ))
    return
  end
  ironmon_move_access_original_rare_tutor_example(legendary)
end

alias ironmon_machine_original_pb_use_item pbUseItem
def pbUseItem(bag, item, bagscene = nil)
  channel = Ironmon.machine_channel_for_item(item)
  if Ironmon.move_access_randomization_active? && channel
    return Ironmon.with_machine_compatibility(channel) do
      ironmon_machine_original_pb_use_item(bag, item, bagscene)
    end
  end
  return ironmon_machine_original_pb_use_item(bag, item, bagscene)
end

alias ironmon_machine_original_pb_use_item_on_pokemon pbUseItemOnPokemon
def pbUseItemOnPokemon(item, pokemon, scene)
  channel = Ironmon.machine_channel_for_item(item)
  if Ironmon.move_access_randomization_active? && channel
    return Ironmon.with_machine_compatibility(channel) do
      ironmon_machine_original_pb_use_item_on_pokemon(item, pokemon, scene)
    end
  end
  return ironmon_machine_original_pb_use_item_on_pokemon(item, pokemon, scene)
end

class PokemonPartyScreen
  def pbUseItem(bag, pokemon)
    ret = nil
    pbFadeOutIn do
      scene = PokemonBag_Scene.new
      screen = PokemonBagScreen.new(scene, bag)
      ret = screen.pbChooseItemScreen(Proc.new do |item|
        item_data = GameData::Item.get(item)
        next false if !pbCanUseOnPokemon?(item_data)
        if item_data.is_machine?
          move = item_data.move
          compatible = Ironmon.with_machine_compatibility(
            Ironmon.machine_channel_for_item(item_data)
          ) { pokemon.compatible_with_move?(move) }
          next false if pokemon.hasMove?(move) || !compatible
        end
        next true
      end)
      yield if block_given?
    end
    return ret
  end
end

alias ironmon_machine_original_daycare_generate_egg pbDayCareGenerateEgg
def pbDayCareGenerateEgg
  if Ironmon.move_access_randomization_active?
    return Ironmon.with_machine_compatibility(:machine) do
      ironmon_machine_original_daycare_generate_egg
    end
  end
  return ironmon_machine_original_daycare_generate_egg
end

Events.onMapCreate += proc do |_sender, event|
  map_id = event[0]
  map = event[1]
  Ironmon.patch_ordinary_tutor_map(map_id, map) if
    Ironmon.move_access_randomization_active?
end

Events.onWildPokemonCreate += proc do |_sender, event|
  pokemon = event[0]
  if Ironmon.move_access_randomization_active? && pokemon &&
     !pokemon.shadowPokemon?
    pokemon.reset_moves
  end
end

Events.onTrainerPartyLoad += proc do |_sender, event|
  trainer = event[0]
  if Ironmon.move_access_randomization_active? && trainer && trainer.party
    trainer.party.each do |pokemon|
      pokemon.reset_moves if !pokemon.shadowPokemon?
    end
  end
end

Ironmon.register_game_load_hook(
  :move_access_randomization,
  proc { |_save_data| Ironmon.suspend_move_access_randomization },
  proc do |_save_data, _result|
    next if Ironmon.checkpoint_reset_loading?
    Ironmon.ensure_move_access_randomization if Ironmon.active?
  end
)
