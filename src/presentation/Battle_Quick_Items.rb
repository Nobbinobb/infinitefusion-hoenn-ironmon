#===============================================================================
# Ironmon in-battle item quick access
#===============================================================================

class PokemonGlobalMetadata
  attr_accessor :ironmon_battle_quick_items
end

module Ironmon
  QUICK_ITEM_CATEGORIES = [
    "balls", "healing", "status", "pp_restore", "combat_stat", "other"
  ].freeze

  def self.battle_quick_item_preferences
    stored = $PokemonGlobal.ironmon_battle_quick_items
    if !stored.is_a?(Hash) || !stored["items"].is_a?(Hash)
      stored = { "category" => "balls", "items" => {} }
      $PokemonGlobal.ironmon_battle_quick_items = stored
    end
    return stored
  end

  def self.battle_quick_item_category(item)
    return "balls" if item.is_poke_ball?
    return tracker_battle_item_category(item, 100)
  end
end

class IronmonBattleQuickItems
  def initialize(preferences = Ironmon.battle_quick_item_preferences)
    @preferences = preferences
    @groups = {}
    @signature = nil
    refresh
  end

  def refresh
    entries = []
    if $PokemonBag
      $PokemonBag.pockets.each do |pocket|
        next if !pocket
        pocket.each { |id, quantity| entries.push([id, quantity]) if quantity.to_i > 0 }
      end
    end
    return if entries == @signature
    @signature = entries
    groups = {}
    entries.each do |id, _quantity|
      item = GameData::Item.try_get(id)
      next if !item || item.battle_use <= 0
      category = Ironmon.battle_quick_item_category(item)
      (groups[category] ||= []).push(item.id)
    end
    groups.each do |category, items|
      items.uniq!
      items.sort_by! { |id| [GameData::Item.get(id).name, id.to_s] }
      selected = @preferences["items"][category]
      next if items.any? { |id| id.to_s == selected }
      old_items = @groups[category] || []
      old_index = old_items.index { |id| id.to_s == selected } || 0
      @preferences["items"][category] = items[[old_index, items.length - 1].min].to_s
    end
    @preferences["items"].delete_if { |category, _id| !groups.key?(category) }
    @groups = groups
    @preferences["category"] = categories.first if !@groups.key?(category)
  end

  def categories
    return Ironmon::QUICK_ITEM_CATEGORIES.select { |category| @groups.key?(category) }
  end

  def category
    return @preferences["category"]
  end

  def item
    selected = @preferences["items"][category]
    return selected && GameData::Item.try_get(selected.to_sym)
  end

  def quantity
    selected = item
    return selected && $PokemonBag ? $PokemonBag.pbQuantity(selected.id) : 0
  end

  def move_item(direction)
    items = @groups[category] || []
    return false if items.length < 2
    index = items.index(item.id) || 0
    @preferences["items"][category] = items[(index + direction) % items.length].to_s
    return true
  end

  def move_category(direction)
    available = categories
    return false if available.length < 2
    index = available.index(category) || 0
    @preferences["category"] = available[(index + direction) % available.length]
    return true
  end
end

class IronmonBattleQuickItemBox < BitmapSprite
  WIDTH = 42
  HEIGHT = 46
  ICON_SIZE = 28

  def initialize(viewport)
    super(WIDTH, HEIGHT, viewport)
    @last_quantity = nil
  end

  def refresh(quantity)
    return if quantity == @last_quantity
    @last_quantity = quantity
    bitmap.clear
    return if quantity <= 0
    border = Color.new(174, 174, 174)
    background = Color.new(248, 248, 248)
    bitmap.fill_rect(3, 0, WIDTH - 6, HEIGHT, border)
    bitmap.fill_rect(1, 2, WIDTH - 2, HEIGHT - 4, border)
    bitmap.fill_rect(0, 4, WIDTH, HEIGHT - 8, border)
    bitmap.fill_rect(3, 2, WIDTH - 6, HEIGHT - 4, background)
    bitmap.fill_rect(2, 4, WIDTH - 4, HEIGHT - 8, background)
    (4...(HEIGHT - 4)).each do |row|
      shade = 244 - 49 * (row - 4) / (HEIGHT - 9)
      bitmap.fill_rect(4, row, WIDTH - 8, 1, Color.new(shade, shade, shade))
    end
  end
end

class IronmonBattleQuickItemCount < BitmapSprite
  def initialize(viewport)
    super(IronmonBattleQuickItemBox::WIDTH, IronmonBattleQuickItemBox::HEIGHT, viewport)
    @last_quantity = nil
  end

  def refresh(quantity)
    return if quantity == @last_quantity
    @last_quantity = quantity
    bitmap.clear
    return if quantity <= 0
    bitmap.font.name = MessageConfig.pbTryFonts("Arial")
    bitmap.font.bold = true
    bitmap.font.shadow = false if bitmap.font.respond_to?(:shadow)
    bitmap.font.size = 14
    text = quantity.to_s
    x = 1
    y = bitmap.height - 18
    width = bitmap.width - 6
    bitmap.font.color = Color.new(248, 248, 248)
    [-1, 0, 1].each do |dx|
      [-1, 0, 1].each do |dy|
        next if dx == 0 && dy == 0
        bitmap.draw_text(x + dx, y + dy, width, 16, text, 2)
      end
    end
    bitmap.font.color = PokeBattle_SceneConstants::MESSAGE_BASE_COLOR
    bitmap.draw_text(x, y, width, 16, text, 2)
  end
end

module IronmonBattleQuickItemSceneHooks
  def pbCommandMenu(idxBattler, firstAction)
    @ironmon_quick_item_battler = idxBattler
    return super
  ensure
    @ironmon_quick_item_battler = nil
    ironmon_hide_quick_items
  end

  def pbFightMenu(idxBattler, megaEvoPossible = false, &block)
    @ironmon_quick_item_battler = idxBattler
    return super
  ensure
    @ironmon_quick_item_battler = nil
    ironmon_hide_quick_items
  end

  def ironmon_quick_items_enabled?
    return false if @ironmon_quick_item_battler.nil? || !Ironmon.active?
    return false if !@battle.internalBattle ||
      !@battle.pbOwnedByPlayer?(@ironmon_quick_item_battler)
    battler = @battle.battlers[@ironmon_quick_item_battler]
    return false if !battler || battler.fainted? || battler.effects[PBEffects::SkyDrop] >= 0
    return @sprites["commandWindow"].visible || @sprites["fightWindow"].visible
  end

  def ironmon_hide_quick_items
    return if !@sprites
    ["ironmonQuickItemBox", "ironmonQuickItemIcon", "ironmonQuickItemCount"].each do |key|
      @sprites[key].visible = false if @sprites[key]
    end
  end

  def ironmon_refresh_quick_items
    @ironmon_quick_items ||= IronmonBattleQuickItems.new
    @ironmon_quick_items.refresh
    icon = @sprites["ironmonQuickItemIcon"] ||=
      ItemIconSprite.new(0, 0, nil, @viewport)
    box = @sprites["ironmonQuickItemBox"] ||=
      IronmonBattleQuickItemBox.new(@viewport)
    count = @sprites["ironmonQuickItemCount"] ||=
      IronmonBattleQuickItemCount.new(@viewport)
    selected = @ironmon_quick_items.item
    icon.visible = !selected.nil?
    icon.item = selected && selected.id
    bottom = @sprites["cmdBar_bg"].y
    pokemon = @sprites["pokemon_#{@ironmon_quick_item_battler}"]
    box.visible = icon.visible
    box.x = 0
    box.y = bottom - box.bitmap.height
    box.z = pokemon ? pokemon.z - 3 : 47
    box.refresh(@ironmon_quick_items.quantity)
    icon.x = box.x + box.bitmap.width / 2
    icon.y = box.y + box.bitmap.height / 2
    icon.zoom_x = IronmonBattleQuickItemBox::ICON_SIZE.to_f / ItemIconSprite::ANIM_ICON_SIZE
    icon.zoom_y = icon.zoom_x
    icon.z = box.z + 1
    count.visible = icon.visible
    count.x = box.x
    count.y = box.y
    count.z = icon.z + 1
    count.refresh(@ironmon_quick_items.quantity)
    icon.update
  end

  def pbInputUpdate
    super
    if !ironmon_quick_items_enabled?
      ironmon_hide_quick_items
      return
    end
    ironmon_refresh_quick_items
    changed = false
    if Ironmon.tracker_control_action_triggered?("quick_item_left", true)
      changed = @ironmon_quick_items.move_item(-1)
    elsif Ironmon.tracker_control_action_triggered?("quick_item_right", true)
      changed = @ironmon_quick_items.move_item(1)
    elsif Ironmon.tracker_control_action_triggered?("quick_item_up", true)
      changed = @ironmon_quick_items.move_category(-1)
    elsif Ironmon.tracker_control_action_triggered?("quick_item_down", true)
      changed = @ironmon_quick_items.move_category(1)
    elsif Ironmon.tracker_control_action_triggered?("quick_item_use")
      ironmon_use_quick_item
    end
    pbPlayCursorSE if changed
    ironmon_refresh_quick_items if changed
  end

  def ironmon_use_quick_item
    item = @ironmon_quick_items.item
    return if !item || Ironmon.tracker_battle_item_pending?
    payload = ironmon_quick_item_payload(item)
    return if !payload
    result = Ironmon.request_tracker_battle_item(payload, Ironmon.tracker_battle_id)
    return if !result["accepted"]
    pbPlayDecisionSE
    throw :ironmon_tracker_battle_item, Ironmon.tracker_battle_item_interrupt_result
  end

  def ironmon_quick_item_payload(item)
    index = @ironmon_quick_item_battler
    payload = { "item_id" => item.id.to_s }
    return payload if ![2, 7, 4, 9].include?(item.battle_use)
    window = @sprites["fightWindow"].visible ? PokeBattle_Scene::FIGHT_BOX : PokeBattle_Scene::COMMAND_BOX
    begin
      @ironmon_quick_item_battler = nil
      ironmon_hide_quick_items
      return Ironmon.without_tracker_battle_item_command do
        if [2, 7].include?(item.battle_use)
          moves = @battle.battlers[index].pokemon.moves
          choices = moves.map { |move| _INTL("{1} (PP {2}/{3})", move.name, move.pp, move.total_pp) }
          choices.push(_INTL("Cancel"))
          selected = pbShowCommands(_INTL("Restore which move?"), choices, moves.length)
          next nil if selected < 0 || selected >= moves.length
          payload["move_index"] = selected
        elsif @battle.pbOpposingBattlerCount(index) > 1
          target = pbChooseTarget(index, GameData::Target.get(:Foe))
          next nil if target < 0
          payload["target_position"] = target
        end
        next payload
      end
    ensure
      @ironmon_quick_item_battler = index
      pbShowWindow(window)
      pbSelectBattler(index)
      Input.update
    end
  end
end

PokeBattle_Scene.prepend(IronmonBattleQuickItemSceneHooks)
