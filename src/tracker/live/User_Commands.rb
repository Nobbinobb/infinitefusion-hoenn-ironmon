#===============================================================================
# Ironmon tracker battle-item commands and shared payload utilities
#===============================================================================

module Ironmon
  TRACKER_FIXED_HEALING = {
    :POTION => 20,
    :BERRYJUICE => 20,
    :SWEETHEART => 20,
    :SUPERPOTION => 50,
    :FRESHWATER => 50,
    :ENERGYPOWDER => 50,
    :SODAPOP => 60,
    :LEMONADE => 80,
    :MOOMOOMILK => 100,
    :ORANBERRY => 10,
    :HYPERPOTION => 200,
    :ENERGYROOT => 200
  }

  def self.begin_tracker_battle_command(
    battle, battler_index, first_action, interrupt_result,
    interruptible = true
  )
    return if battle != @tracker_battle
    @tracker_battle_command = {
      "battler_index" => battler_index,
      "first_action" => first_action,
      "interrupt_result" => interrupt_result,
      "interrupt_depth" => interruptible ? 1 : 0
    }
  end

  def self.end_tracker_battle_command
    @tracker_battle_command = nil
  end

  def self.with_tracker_battle_item_interrupt
    return yield if !@tracker_battle_command
    @tracker_battle_command["interrupt_depth"] += 1
    return yield
  ensure
    if @tracker_battle_command
      @tracker_battle_command["interrupt_depth"] -= 1
    end
  end

  def self.request_tracker_battle_item(payload, battle_id)
    return tracker_battle_item_result(false, "No active battle is available.") if
      !@tracker_battle_id || battle_id.to_s != @tracker_battle_id.to_s
    return tracker_battle_item_result(false, "Choose an item while a battle action menu is open.") if
      !@tracker_battle_command ||
      @tracker_battle_command["interrupt_depth"] <= 0
    return tracker_battle_item_result(false, "Another tracker item is already selected.") if
      @tracker_pending_battle_item
    item_id = (payload || {})["item_id"].to_s
    item = GameData::Item.try_get(item_id.to_sym)
    return tracker_battle_item_result(false, "That item is not available.") if
      !item || item.battle_use <= 0 || !$PokemonBag || $PokemonBag.pbQuantity(item.id) <= 0
    move_index = (payload || {})["move_index"]
    if [2, 7].include?(item.battle_use)
      moves = @tracker_player_pokemon ? @tracker_player_pokemon.moves : []
      return tracker_battle_item_result(false, "Choose a move for that PP item.") if
        !move_index.is_a?(Integer) || move_index < 0 || move_index >= moves.length
    else
      move_index = -1
    end
    target_position = (payload || {})["target_position"]
    @tracker_pending_battle_item = {
      "item" => item.id,
      "move_index" => move_index,
      "target_position" => target_position
    }
    return tracker_battle_item_result(true, "#{item.name} was selected for this turn.")
  end

  def self.tracker_battle_item_result(accepted, message)
    return { "accepted" => accepted, "message" => message }
  end

  def self.tracker_battle_item_interrupt?
    return !!(
      @tracker_battle_command && @tracker_pending_battle_item &&
      @tracker_battle_command["interrupt_depth"] > 0
    )
  end

  def self.tracker_battle_item_menu_active?
    return !!@tracker_battle_command
  end

  def self.tracker_battle_item_pending?
    return !!@tracker_pending_battle_item
  end

  def self.tracker_battle_item_interrupt_result
    return nil if !tracker_battle_item_interrupt?
    return @tracker_battle_command["interrupt_result"]
  end

  def self.consume_tracker_battle_item
    item = @tracker_pending_battle_item
    @tracker_pending_battle_item = nil
    return item
  end

  def self.tracker_item_healing(item, maximum_hp)
    return maximum_hp if [:MAXPOTION, :FULLRESTORE].include?(item)
    return maximum_hp / 4 if item == :SITRUSBERRY
    return 20 if item == :RAGECANDYBAR && !Settings::RAGE_CANDY_BAR_CURES_STATUS_PROBLEMS
    return TRACKER_FIXED_HEALING[item] || 0
  end

  def self.tracker_sprite_path(pokemon, preferred_sprite = nil)
    pif_sprite = preferred_sprite || pokemon.pif_sprite
    if !pif_sprite
      loader = BattleSpriteLoader.new
      pif_sprite = loader.get_pif_sprite_from_species(pokemon.species)
    end
    path = BattleSpriteLoader.new.check_for_local_sprite(pif_sprite)
    return path ? path.tr("\\", "/") : nil
  rescue Exception
    return nil
  end

  def self.tracker_game_version
    return Settings::GAME_VERSION_NUMBER if defined?(Settings::GAME_VERSION_NUMBER)
    return "unknown"
  end

  def self.tracker_timestamp
    return Time.now.utc.strftime("%Y-%m-%dT%H:%M:%S.%LZ")
  end
end
