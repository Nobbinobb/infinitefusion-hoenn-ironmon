#===============================================================================
# Development-only Ironmon data inspector
#===============================================================================
module Ironmon
  def self.inspector_ability_name(ability)
    return _INTL("None") if !ability
    data = GameData::Ability.try_get(ability)
    return ability.to_s if !data
    return data.name
  end

  def self.inspector_ability_id(ability)
    return "-" if !ability
    data = GameData::Ability.try_get(ability)
    return ability.to_s if !data
    return data.id.to_s
  end

  def self.inspector_eligibility(ability)
    return _INTL("None") if !ability
    if AbilityGenerator::EXACT_SPECIES_ABILITY_RULES.key?(ability)
      return _INTL("Exact species")
    end
    if AbilityGenerator::COMPONENT_ABILITY_RULES.key?(ability)
      return _INTL("Component-compatible")
    end
    return _INTL("Universal")
  end
end

class IronmonInspector_Scene
  PAGE_NAMES = [_INTL("OVERVIEW"), _INTL("ABILITIES")].freeze
  VISIBLE_ROWS = 6
  ROW_HEIGHT = 32
  ROW_TOP = 98
  PAGE_HEADER_TOP = 62
  PAGE_HEADER_HEIGHT = 34
  FOOTER_TOP = 294

  def pbUpdate
    pbUpdateSpriteHash(@sprites)
  end

  def pbStartScene(pokemon)
    @pokemon = pokemon
    @species_data = pokemon.species_data
    @page = 0
    @selected_row = 0
    @scroll_offset = 0
    @viewport = Viewport.new(0, 0, Graphics.width, Graphics.height)
    @viewport.z = 99999
    @sprites = {}
    @sprites["background"] = BitmapSprite.new(
      Graphics.width, Graphics.height, @viewport
    )
    draw_background(@sprites["background"].bitmap)
    @sprites["pokemon"] = PokemonSprite.new(@viewport)
    @sprites["pokemon"].setOffset(PictureOrigin::Center)
    @sprites["pokemon"].x = 108
    @sprites["pokemon"].y = 218
    @sprites["pokemon"].setPokemonBitmap(@pokemon)
    if @pokemon.egg?
      @sprites["pokemon"].zoom_x = Settings::EGGSPRITE_SCALE
      @sprites["pokemon"].zoom_y = Settings::EGGSPRITE_SCALE
    else
      @sprites["pokemon"].zoom_x = Settings::FRONTSPRITE_SCALE
      @sprites["pokemon"].zoom_y = Settings::FRONTSPRITE_SCALE
    end
    @sprites["itemicon"] = ItemIconSprite.new(
      18, 316, @pokemon.item_id, @viewport
    )
    @sprites["itemicon"].blankzero = true
    @sprites["overlay"] = BitmapSprite.new(
      Graphics.width, Graphics.height, @viewport
    )
    pbSetSystemFont(@sprites["overlay"].bitmap)
    @sprites["messagebox"] = Window_AdvancedTextPokemon.new("")
    @sprites["messagebox"].viewport = @viewport
    @sprites["messagebox"].visible = false
    @sprites["messagebox"].letterbyletter = true
    pbBottomLeftLines(@sprites["messagebox"], 3)
    reset_selection
    draw_page
    pbFadeInAndShow(@sprites) { pbUpdate }
  end

  def pbEndScene
    pbFadeOutAndHide(@sprites) { pbUpdate }
    pbDisposeSpriteHash(@sprites)
    @viewport.dispose
  end

  def pbScene
    @pokemon.play_cry
    loop do
      Graphics.update
      Input.update
      pbUpdate
      if Input.trigger?(Input::BACK)
        pbPlayCloseMenuSE
        break
      elsif Input.trigger?(Input::ACTION)
        pbSEStop
        @pokemon.play_cry
      elsif Input.trigger?(Input::LEFT)
        change_page(-1)
      elsif Input.trigger?(Input::RIGHT)
        change_page(1)
      elsif Input.trigger?(Input::UP)
        move_selection(-1)
      elsif Input.trigger?(Input::DOWN)
        move_selection(1)
      elsif Input.trigger?(Input::USE)
        show_selected_detail
      end
    end
  end

  private

  def fusion?
    return Ironmon.fusion_ability_species?(@species_data)
  end

  def rows
    @rows ||= {}
    @rows[@page] ||= (@page == 0) ? overview_rows : ability_rows
    return @rows[@page]
  end

  def row(label, value, detail = nil)
    return {
      :label => label.to_s,
      :value => value.to_s,
      :detail => detail,
      :selectable => true
    }
  end

  def section(label)
    return {
      :label => label.to_s,
      :value => "",
      :detail => nil,
      :selectable => false
    }
  end

  def overview_rows
    result = []
    result << section(_INTL("POKEMON"))
    result << row(_INTL("Kind"), fusion? ? _INTL("Fusion") : _INTL("Normal"))
    result << row(
      _INTL("Species"), @species_data.name,
      _INTL("Species ID: {1}", @species_data.id)
    )
    result << row(_INTL("ID"), @species_data.id)
    form_name = @species_data.form_name
    form_value = @species_data.form.to_s
    form_value += " - #{form_name}" if form_name && !form_name.empty?
    result << row(_INTL("Form"), form_value)
    if fusion?
      body = @species_data.body_pokemon
      head = @species_data.head_pokemon
      result << section(_INTL("DISPLAYED COMPONENTS"))
      result << row(
        _INTL("Body"), body.name, _INTL("Species ID: {1}", body.id)
      )
      result << row(
        _INTL("Head"), head.name, _INTL("Species ID: {1}", head.id)
      )
    end
    result << section(_INTL("ACTIVE ABILITY"))
    result << row(
      _INTL("Slot"), active_slot_label,
      _INTL("Pokemon ability index: {1}", @pokemon.ability_index)
    )
    result << row(
      _INTL("Ability"), Ironmon.inspector_ability_name(@pokemon.ability_id),
      _INTL("Internal ID: {1}", Ironmon.inspector_ability_id(@pokemon.ability_id))
    )
    result << section(_INTL("GENERATOR"))
    result << row(_INTL("Run seed"), $PokemonGlobal.ironmon_seed)
    result << row(
      _INTL("Schema"), Ironmon::AbilityGenerator::SCHEMA_VERSION
    )
    result << row(
      _INTL("Pool rules"), Ironmon::AbilityGenerator::POOL_RULES_VERSION
    )
    result << row(_INTL("Pool size"), Ironmon.allowed_ability_pool.length)
    result << row(
      _INTL("Fingerprint"), Ironmon.ability_pool_fingerprint,
      _INTL("Ability pool fingerprint: {1}", Ironmon.ability_pool_fingerprint)
    )
    return result
  end

  def ability_rows
    result = []
    result << section(_INTL("CURRENT"))
    result << row(
      active_slot_label,
      Ironmon.inspector_ability_name(@pokemon.ability_id),
      ability_detail(@pokemon.ability_id, nil, true)
    )
    if fusion?
      append_fusion_slots(result)
      append_component_slots(
        result, _INTL("BODY GENERATED"), @species_data.body_pokemon
      )
      append_component_slots(
        result, _INTL("HEAD GENERATED"), @species_data.head_pokemon
      )
    else
      result << section(_INTL("GENERATED SLOTS"))
      append_species_slots(result, @species_data)
    end
    return result
  end

  def append_species_slots(result, species_data)
    generated_normal = Ironmon.generated_normal_abilities(species_data)
    generated_hidden = Ironmon.generated_hidden_abilities(species_data)
    original_normal = Ironmon.original_normal_abilities(species_data)
    original_hidden = Ironmon.original_hidden_abilities(species_data)
    append_slot_kind(
      result, _INTL("Normal"), generated_normal, original_normal
    )
    append_slot_kind(
      result, _INTL("Hidden"), generated_hidden, original_hidden
    )
  end

  def append_component_slots(result, heading, species_data)
    result << section(heading)
    append_species_slots(result, species_data)
  end

  def append_slot_kind(result, label, generated, original)
    generated.each_with_index do |ability, index|
      next if !ability
      original_ability = original[index]
      detail = ability_detail(ability, original_ability, false)
      result << row(
        _INTL("{1} {2}", label, index),
        Ironmon.inspector_ability_name(ability), detail
      )
    end
  end

  def append_fusion_slots(result)
    result << section(_INTL("FINAL FUSION SLOTS"))
    generated_normal = Ironmon.generated_normal_abilities(@species_data)
    generated_hidden = Ironmon.generated_hidden_abilities(@species_data)
    original_normal = Ironmon.original_normal_abilities(@species_data)
    original_hidden = Ironmon.original_hidden_abilities(@species_data)
    append_fusion_slot_kind(
      result, :normal, _INTL("Normal"), generated_normal, original_normal
    )
    append_fusion_slot_kind(
      result, :hidden, _INTL("Hidden"), generated_hidden, original_hidden
    )
  end

  def append_fusion_slot_kind(result, kind, label, generated, original)
    generated.each_with_index do |ability, index|
      next if !ability
      source = fusion_slot_source(kind, index)
      source_text = _INTL("Source: {1}", source[0]) + "\n" +
        _INTL("From: {1}", Ironmon.inspector_ability_name(source[1]))
      replacement = ""
      if source[1] && source[1] != ability
        replacement = _INTL("\nRestricted source replaced for this fusion.")
      end
      detail = ability_detail(ability, original[index], false)
      detail = "#{source_text}\n#{detail}#{replacement}"
      result << row(
        _INTL("{1} {2}", label, index),
        Ironmon.inspector_ability_name(ability), detail
      )
    end
  end

  def fusion_slot_source(kind, index)
    body = @species_data.body_pokemon
    head = @species_data.head_pokemon
    component = (index % 2 == 0) ? body : head
    component_name = (index % 2 == 0) ? _INTL("Body") : _INTL("Head")
    normal = Ironmon.generated_normal_abilities(component)
    hidden = Ironmon.generated_hidden_abilities(component)
    if kind == :normal
      return [
        _INTL("{1} normal 0", component_name), normal[0]
      ]
    end
    if index < 2
      return [
        _INTL("{1} normal 1", component_name), normal[1]
      ]
    end
    return [_INTL("{1} hidden 0", component_name), hidden[0]]
  end

  def ability_detail(ability, original_ability = nil, active = false)
    lines = []
    if active
      lines << _INTL("Active ability slot.")
    end
    lines << _INTL(
      "Ability: {1}", Ironmon.inspector_ability_name(ability)
    )
    lines << _INTL("ID: {1}", Ironmon.inspector_ability_id(ability))
    if original_ability
      lines << _INTL(
        "Original: {1} ({2})",
        Ironmon.inspector_ability_name(original_ability),
        Ironmon.inspector_ability_id(original_ability)
      )
    end
    lines << _INTL(
      "Eligibility: {1}", Ironmon.inspector_eligibility(ability)
    )
    return lines.join("\n")
  end

  def active_slot_label
    index = @pokemon.ability_index.to_i
    return _INTL("Hidden {1}", index - 2) if index >= 2
    return _INTL("Normal {1}", index)
  end

  def change_page(direction)
    @page = (@page + direction) % PAGE_NAMES.length
    reset_selection
    pbSEPlay("GUI summary change page")
    draw_page
  end

  def reset_selection
    @scroll_offset = 0
    @selected_row = first_selectable_row
  end

  def first_selectable_row
    rows.each_with_index do |entry, index|
      return index if entry[:selectable]
    end
    return 0
  end

  def move_selection(direction)
    candidate = @selected_row + direction
    while candidate >= 0 && candidate < rows.length
      if rows[candidate][:selectable]
        @selected_row = candidate
        ensure_selected_visible
        pbPlayCursorSE
        draw_page
        return
      end
      candidate += direction
    end
  end

  def ensure_selected_visible
    if @selected_row < @scroll_offset
      @scroll_offset = @selected_row
    elsif @selected_row >= @scroll_offset + VISIBLE_ROWS
      @scroll_offset = @selected_row - VISIBLE_ROWS + 1
    end
  end

  def show_selected_detail
    selected = rows[@selected_row]
    return if !selected || !selected[:selectable] || !selected[:detail]
    pbDisplay(selected[:detail])
  end

  def pbDisplay(text)
    @sprites["messagebox"].text = text
    @sprites["messagebox"].visible = true
    pbPlayDecisionSE
    loop do
      Graphics.update
      Input.update
      pbUpdate
      if @sprites["messagebox"].busy?
        if Input.trigger?(Input::USE)
          pbPlayDecisionSE if @sprites["messagebox"].pausing?
          @sprites["messagebox"].resume
        end
      elsif Input.trigger?(Input::USE) || Input.trigger?(Input::BACK)
        break
      end
    end
    @sprites["messagebox"].visible = false
  end

  def draw_page
    overlay = @sprites["overlay"].bitmap
    overlay.clear
    draw_fixed_pokemon(overlay)
    draw_rows(overlay)
    draw_scrollbar(overlay)
  end

  def draw_background(bitmap)
    bitmap.clear
    bitmap.fill_rect(0, 0, Graphics.width, Graphics.height, Color.new(216, 224, 224))
    bitmap.fill_rect(0, 0, Graphics.width, 52, Color.new(88, 152, 232))
    bitmap.fill_rect(0, 48, Graphics.width, 4, Color.new(64, 80, 88))

    bitmap.fill_rect(6, 56, 204, 324, Color.new(64, 80, 88))
    bitmap.fill_rect(12, 62, 192, 58, Color.new(144, 160, 176))
    bitmap.fill_rect(12, 122, 192, 174, Color.new(232, 240, 240))
    bitmap.fill_rect(12, 298, 192, 76, Color.new(144, 160, 176))

    bitmap.fill_rect(216, 56, 292, 324, Color.new(64, 80, 88))
    bitmap.fill_rect(222, PAGE_HEADER_TOP, 280, PAGE_HEADER_HEIGHT, Color.new(112, 160, 224))
    bitmap.fill_rect(222, ROW_TOP, 280, VISIBLE_ROWS * ROW_HEIGHT, Color.new(232, 240, 240))
    bitmap.fill_rect(222, FOOTER_TOP, 280, 80, Color.new(208, 216, 224))
    bitmap.fill_rect(222, FOOTER_TOP, 280, 26, Color.new(144, 160, 176))
  end

  def draw_fixed_pokemon(overlay)
    light_base = Color.new(248, 248, 248)
    light_shadow = Color.new(104, 104, 104)
    dark_base = Color.new(64, 64, 64)
    dark_shadow = Color.new(176, 176, 176)
    header_y = centered_text_y(overlay, 0, 48)
    name_y = centered_text_y(overlay, 62, 29)
    level_y = centered_text_y(overlay, 91, 29)
    item_label_y = centered_text_y(overlay, 298, 32)
    item_value_y = centered_text_y(overlay, 334, 34)
    text = [
      [_INTL("IRONMON INSPECTOR"), 18, header_y, 0, light_base, light_shadow],
      [shorten(overlay, @pokemon.name, 148), 20, name_y, 0, light_base, light_shadow],
      [_INTL("Lv. {1}", @pokemon.level), 20, level_y, 0, dark_base, dark_shadow],
      [_INTL("ITEM"), 72, item_label_y, 0, light_base, light_shadow]
    ]
    if @pokemon.hasItem?
      text << [shorten(overlay, @pokemon.item.name, 126), 72, item_value_y, 0, dark_base, dark_shadow]
    else
      text << [
        _INTL("None"), 72, item_value_y, 0,
        Color.new(192, 200, 208), Color.new(208, 216, 224)
      ]
    end
    if @pokemon.male?
      text << ["M", 194, name_y, 1, Color.new(24, 112, 216), Color.new(136, 168, 208)]
    elsif @pokemon.female?
      text << ["F", 194, name_y, 1, Color.new(248, 56, 32), Color.new(224, 152, 144)]
    end
    pbDrawTextPositions(overlay, text)
  end

  def draw_rows(overlay)
    light_base = Color.new(248, 248, 248)
    light_shadow = Color.new(104, 104, 104)
    dark_base = Color.new(64, 64, 64)
    dark_shadow = Color.new(176, 176, 176)
    page_header_y = centered_text_y(
      overlay, PAGE_HEADER_TOP, PAGE_HEADER_HEIGHT
    )
    header = [
      [PAGE_NAMES[@page], 230, page_header_y, 0, light_base, light_shadow],
      [_INTL("{1} / {2}", @page + 1, PAGE_NAMES.length), 492, page_header_y, 1, light_base, light_shadow]
    ]
    pbDrawTextPositions(overlay, header)
    visible = rows[@scroll_offset, VISIBLE_ROWS] || []
    visible.each_with_index do |entry, visible_index|
      absolute_index = @scroll_offset + visible_index
      row_top = ROW_TOP + (visible_index * ROW_HEIGHT)
      # The game's pixel font reports a box that excludes the lowest part of
      # descenders such as g, p and y. Raise list text slightly so those pixels
      # stay inside the row and its selection background.
      text_y = centered_text_y(overlay, row_top, ROW_HEIGHT) - 4
      if !entry[:selectable]
        overlay.fill_rect(
          224, row_top + 1, 276, ROW_HEIGHT - 2, Color.new(136, 152, 168)
        )
        pbDrawTextPositions(overlay, [[
          shorten(overlay, entry[:label], 250), 230, text_y, 0,
          light_base, light_shadow
        ]])
        next
      end
      if absolute_index == @selected_row
        overlay.fill_rect(
          224, row_top + 1, 276, ROW_HEIGHT - 2, Color.new(88, 152, 232)
        )
      end
      pbDrawTextPositions(overlay, [
        [shorten(overlay, entry[:label], 104), 230, text_y, 0, dark_base, dark_shadow],
        [shorten(overlay, entry[:value], 150), 490, text_y, 1, dark_base, dark_shadow]
      ])
    end
    draw_footer(overlay, dark_base, dark_shadow, light_base, light_shadow)
  end

  def draw_footer(overlay, dark_base, dark_shadow, light_base, light_shadow)
    label_y = centered_text_y(overlay, FOOTER_TOP, 26)
    pbDrawTextPositions(overlay, [[
      _INTL("DETAILS"), 230, label_y, 0, light_base, light_shadow
    ]])
    selected = rows[@selected_row]
    detail = selected && selected[:detail]
    detail = _INTL(
      "L/R: Page; Up/Down: Scroll\nConfirm: More; Back: Close"
    ) if !detail
    lines = detail.to_s.split("\n")
    lines = [""] if lines.empty?
    lines = lines[0, 2] if lines.length > 2
    lines.each_with_index do |line, index|
      line_top = 320 + (index * 26)
      line_y = centered_text_y(overlay, line_top, 26)
      pbDrawTextPositions(overlay, [[
        shorten(overlay, line, 262), 230, line_y, 0,
        dark_base, dark_shadow
      ]])
    end
  end

  def centered_text_y(bitmap, top, height)
    text_height = bitmap.text_size("Ag").height
    return top + ((height - text_height) / 2) - 6
  end

  def shorten(bitmap, value, maximum_width)
    text = value.to_s
    return text if bitmap.text_size(text).width <= maximum_width
    suffix = "..."
    while text.length > 0 &&
          bitmap.text_size(text + suffix).width > maximum_width
      text = text[0, text.length - 1]
    end
    return text + suffix
  end

  def draw_scrollbar(overlay)
    return if rows.length <= VISIBLE_ROWS
    track_y = ROW_TOP + 3
    track_height = VISIBLE_ROWS * ROW_HEIGHT - 4
    overlay.fill_rect(
      496, track_y, 3, track_height, Color.new(176, 184, 192)
    )
    thumb_height = [
      (track_height * VISIBLE_ROWS / rows.length), 18
    ].max
    maximum_offset = rows.length - VISIBLE_ROWS
    thumb_y = track_y
    if maximum_offset > 0
      thumb_y += (@scroll_offset * (track_height - thumb_height) /
        maximum_offset)
    end
    overlay.fill_rect(
      495, thumb_y, 5, thumb_height, Color.new(72, 120, 184)
    )
  end
end

class IronmonInspectorScreen
  def initialize(scene)
    @scene = scene
  end

  def pbStartScreen(pokemon)
    @scene.pbStartScene(pokemon)
    @scene.pbScene
    @scene.pbEndScene
  end
end

if defined?(PokemonDebugMenuCommands)
  PokemonDebugMenuCommands.register("ironmon_ability_inspector", {
    "parent"      => "main",
    "name"        => _INTL("Inspect Ironmon data"),
    "always_show" => true,
    "effect"      => proc { |pkmn, _pkmnid, _heldpoke, _settingUpBattle, screen|
      if !Ironmon.active?
        screen.pbDisplay(_INTL("Ironmon is not active."))
      elsif !Ironmon.current_ability_randomization?
        screen.pbDisplay(Ironmon.ability_randomization_error_message)
      else
        scene = IronmonInspector_Scene.new
        inspector = IronmonInspectorScreen.new(scene)
        inspector.pbStartScreen(pkmn)
      end
      next false
    }
  })
end
