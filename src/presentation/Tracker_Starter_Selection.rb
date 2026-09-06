#===============================================================================
# Ironmon tracker starter selection
#===============================================================================

module Ironmon
  TRACKER_STARTER_RANDOM_PICK_NAMESPACE = "starter_random_pick"
  TRACKER_STARTER_AUTOSELECT_DELAY_SECONDS = 2.0
  TRACKER_STARTER_AUTOSELECT_REVEAL_SECONDS = 0.75

  def self.begin_tracker_starter_selection(pokemon)
    return if !active? || !pokemon || pokemon.empty?
    random_pick = tracker_starter_random_pick(pokemon.length)
    auto_select = tracker_auto_select_starter?
    ceiling = tracker_maximum_starter_base_stat_total
    if ceiling
      eligible = pokemon.each_index.select do |index|
        total = pokemon[index].baseStats.values.inject(0) do |sum, value|
          sum + value
        end
        total <= ceiling
      end
      random_pick = if eligible.empty?
                      nil
                    else
                      eligible[tracker_starter_random_pick(eligible.length)]
                    end
    end
    @tracker_starter_selection_sequence = (@tracker_starter_selection_sequence || 0) + 1
    @tracker_starter_selection = {
      :selection_id => "#{ensure_tracker_run_id}:#{@tracker_starter_selection_sequence}",
      :auto_select => auto_select,
      :ceiling => ceiling,
      :favorites => pokemon.map { |candidate| tracker_favorite_pokemon?(candidate) },
      :accepting => false,
      :pokemon => pokemon,
      :sprites => if auto_select
                    tracker_select_starter_sprites(pokemon)
                  else
                    Array.new(pokemon.length)
                  end,
      :revealed => Array.new(pokemon.length, false),
      :random_pick_index => random_pick
    }
    if auto_select
      @tracker_starter_selection[:revealed] = Array.new(pokemon.length, true)
    end
    tracker_connection.send_event(
      "starter_selection_changed", tracker_starter_selection_snapshot
    )
  rescue Exception => e
    @tracker_starter_selection = nil
    echoln "Ironmon tracker starter selection failed safely: #{e.message}"
  end

  def self.reveal_tracker_starter(index, sprite = nil)
    selection = @tracker_starter_selection
    return if !selection || index.nil? || index < 0
    return if index >= selection[:pokemon].length
    selection[:sprites][index] ||= sprite if sprite
    return if selection[:revealed][index]
    selection[:revealed][index] = true
    tracker_connection.send_event(
      "starter_selection_changed", tracker_starter_selection_snapshot
    )
  rescue Exception => e
    echoln "Ironmon tracker starter reveal failed safely: #{e.message}"
  end

  def self.tracker_select_starter_sprites(pokemon)
    loader = BattleSpriteLoader.new
    return pokemon.map do |candidate|
      begin
        loader.get_pif_sprite_from_species(candidate.species)
      rescue Exception => e
        echoln "Ironmon tracker starter sprite selection failed safely: #{e.message}"
        nil
      end
    end
  end

  def self.tracker_starter_sprite(index)
    selection = @tracker_starter_selection
    return nil if !selection || index.nil? || index < 0
    return selection[:sprites][index]
  end

  def self.with_tracker_starter_sprite(index)
    sprite = tracker_starter_sprite(index)
    return yield if !sprite || !$PokemonSystem
    substitutions = $PokemonSystem.alt_sprite_substitutions
    return yield if !substitutions
    substitution_id = get_sprite_substitution_id_from_dex_number(sprite.species)
    previous_present = substitutions.key?(substitution_id)
    previous = substitutions[substitution_id]
    substitutions[substitution_id] = sprite
    return yield
  ensure
    if substitutions && substitution_id
      if previous_present
        substitutions[substitution_id] = previous
      else
        substitutions.delete(substitution_id)
      end
    end
  end

  def self.end_tracker_starter_selection
    return if !@tracker_starter_selection
    @tracker_starter_selection = nil
    tracker_connection.send_event(
      "starter_selection_changed", { "active" => false, "choices" => [] }
    )
  rescue Exception => e
    echoln "Ironmon tracker starter completion failed safely: #{e.message}"
  end

  def self.tracker_starter_selection_snapshot
    selection = @tracker_starter_selection
    return nil if !selection
    choices = selection[:pokemon].each_with_index.map do |pokemon, index|
      revealed = selection[:revealed][index]
      choice = { "index" => index, "revealed" => revealed }
      if revealed
        choice["species_id"] = tracker_species_id(pokemon)
        choice["species_name"] = pokemon.species_data.name
        choice["sprite_path"] = tracker_sprite_path(
          pokemon, selection[:sprites][index]
        )
        choice["base_stat_total"] = pokemon.baseStats.values.inject(0) do |sum, value|
          sum + value
        end
        ceiling = selection[:ceiling]
        choice["bst_eligible"] = !ceiling ||
          choice["base_stat_total"] <= ceiling
        choice["favorite"] = selection[:favorites][index]
        choice["can_select"] = tracker_starter_selectable?(index)
      end
      choice
    end
    return {
      "active" => true,
      "selection_id" => selection[:selection_id],
      "auto_select" => selection[:auto_select],
      "random_pick_index" => selection[:random_pick_index],
      "maximum_base_stat_total" => selection[:ceiling],
      "choices" => choices
    }
  end

  def self.tracker_starter_random_pick(choice_count)
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : nil
    source = "#{seed}|#{TRACKER_STARTER_RANDOM_PICK_NAMESPACE}"
    hash = 2_166_136_261
    source.each_byte do |byte|
      hash ^= byte
      hash = (hash * 16_777_619) & 0xffffffff
    end
    return hash % choice_count
  end

  def self.tracker_starter_random_pick_index
    selection = @tracker_starter_selection
    return selection ? selection[:random_pick_index] : nil
  end

  def self.tracker_starter_eligible?(index)
    selection = @tracker_starter_selection
    return false if !selection || !index.is_a?(Integer) || index < 0 ||
      index >= selection[:pokemon].length
    ceiling = selection[:ceiling]
    total = selection[:pokemon][index].baseStats.values.inject(0) do |sum, value|
      sum + value
    end
    return !ceiling || total <= ceiling
  end

  def self.tracker_starter_allowed?(index)
    selection = @tracker_starter_selection
    return false if !tracker_starter_eligible?(index) ||
      !selection[:revealed][index]
    return !selection[:auto_select] ||
      index == selection[:random_pick_index] || selection[:favorites][index]
  end

  def self.tracker_starter_selectable?(index)
    selection = @tracker_starter_selection
    return tracker_starter_allowed?(index) && selection[:accepting] &&
      !selection.key?(:pending_index)
  end

  def self.tracker_starter_favorite?(index)
    return @tracker_starter_selection[:favorites][index]
  end

  def self.tracker_starter_selection_auto_select?
    return @tracker_starter_selection && @tracker_starter_selection[:auto_select]
  end

  def self.set_tracker_starter_accepting(value)
    return if !@tracker_starter_selection
    @tracker_starter_selection[:accepting] = value
    tracker_connection.send_event("starter_selection_changed", tracker_starter_selection_snapshot)
  end

  def self.request_tracker_starter(payload, run_id)
    selection = @tracker_starter_selection
    index = payload.is_a?(Hash) ? payload["index"] : nil
    accepted = selection && run_id.to_s == ensure_tracker_run_id.to_s &&
      payload.is_a?(Hash) && payload["selection_id"] == selection[:selection_id] &&
      tracker_starter_selectable?(index)
    if accepted
      selection[:pending_index] = index
      set_tracker_starter_accepting(false)
    end
    return { "accepted" => !!accepted }
  end

  def self.take_tracker_starter_choice
    selection = @tracker_starter_selection
    return nil if !selection || !selection.key?(:pending_index)
    return selection.delete(:pending_index)
  end

  def self.tracker_auto_select_starter?
    return tracker_connection.auto_select_starter?
  end

  def self.tracker_maximum_starter_base_stat_total
    return tracker_connection.maximum_starter_base_stat_total
  end

  def self.tracker_favorite_pokemon?(pokemon)
    favorites = tracker_connection.favorite_species_ids.map do |identifier|
      identifier.to_s.split(":", 2)[0]
    end
    return false if favorites.empty? || !pokemon
    species = pokemon.species_data
    identifiers = [species.id.to_s.upcase]
    if species.respond_to?(:get_body_species_symbol)
      identifiers << species.get_body_species_symbol.to_s.upcase
      identifiers << species.get_head_species_symbol.to_s.upcase
    elsif species.respond_to?(:body_pokemon) && species.body_pokemon
      identifiers << species.body_pokemon.id.to_s.upcase
      identifiers << species.head_pokemon.id.to_s.upcase
    end
    return identifiers.any? { |identifier| favorites.include?(identifier) }
  rescue Exception
    return false
  end
end

class StartersSelectionScene
  alias ironmon_tracker_original_initialize initialize
  def initialize(starters = [])
    ironmon_tracker_original_initialize(starters)
    if self.class == StartersSelectionScene && Ironmon.starter_acquisition?
      Ironmon.begin_tracker_starter_selection(@starter_pokemon)
    end
  end

  alias ironmon_tracker_original_start_scene startScene
  def startScene
    return ironmon_tracker_original_start_scene if
      self.class != StartersSelectionScene || !Ironmon.starter_acquisition?
    begin
      return ironmon_tracker_auto_select_starter if
        Ironmon.tracker_starter_selection_auto_select?
      return ironmon_tracker_manual_select_starter
    ensure
      Ironmon.end_tracker_starter_selection
    end
  end

  def ironmon_tracker_manual_select_starter
    initializeGraphics
    @index = nil
    Ironmon.set_tracker_starter_accepting(true)
    loop do
      requested = Ironmon.take_tracker_starter_choice
      if !requested.nil?
        @index = requested
        updateStarterSelectionGraphics
        return ironmon_tracker_finalize_starter
      end
      previous_index = @index
      if @index
        @index = (@index + 1) % @starter_pokemon.length if Input.trigger?(Input::RIGHT)
        @index = (@index - 1) % @starter_pokemon.length if Input.trigger?(Input::LEFT)
        updateStarterSelectionGraphics if previous_index != @index
        previous_index = @index
        if Input.trigger?(Input::BACK)
          pbPlayCancelSE
          disposeGraphics
          return nil
        end
        if Input.trigger?(Input::USE)
          if !Ironmon.tracker_starter_selectable?(@index)
            Ironmon.set_tracker_starter_accepting(false)
            pbMessage(_INTL("This starter exceeds the maximum BST."))
            Ironmon.set_tracker_starter_accepting(true)
          else
            Ironmon.set_tracker_starter_accepting(false)
            confirmed = pbConfirmMessage(_INTL("Do you choose this Pokémon?"))
            return ironmon_tracker_finalize_starter if confirmed &&
              Ironmon.tracker_starter_eligible?(@index)
            Ironmon.set_tracker_starter_accepting(true)
          end
        end
      else
        @index = 0 if Input.trigger?(Input::LEFT)
        @index = 1 if Input.trigger?(Input::DOWN)
        @index = 2 if Input.trigger?(Input::RIGHT)
      end
      if @index && (previous_index != @index || Input.trigger?(Input::UP) || Input.trigger?(Input::DOWN))
        updateStarterSelectionGraphics
      end
      Input.update
      Graphics.update
    end
  end

  def ironmon_tracker_auto_select_starter
    initializeGraphics
    eligible_indices = @starter_pokemon.each_index.select do |index|
      Ironmon.tracker_starter_eligible?(index)
    end
    if eligible_indices.empty?
      ironmon_tracker_dispose_unopened_graphics
      raise Ironmon::StarterBstCeilingExceeded,
            "No generated starter satisfies the maximum BST."
    end
    deadline = Ironmon.tracker_uptime_seconds +
      Ironmon::TRACKER_STARTER_AUTOSELECT_DELAY_SECONDS
    while Ironmon.tracker_uptime_seconds < deadline
      Input.update
      Graphics.update
    end
    @index = Ironmon.tracker_starter_random_pick_index || eligible_indices[
      Ironmon.tracker_starter_random_pick(eligible_indices.length)
    ]
    updateOpenPokeballPosition
    updateStarterSelectionGraphics
    favorite_indices = eligible_indices.select do |index|
      Ironmon.tracker_starter_favorite?(index)
    end
    if !favorite_indices.empty? && !favorite_indices.include?(@index)
      return ironmon_tracker_choose_random_or_favorite(
        [@index] + favorite_indices
      )
    end
    reveal_deadline = Ironmon.tracker_uptime_seconds +
      Ironmon::TRACKER_STARTER_AUTOSELECT_REVEAL_SECONDS
    while Ironmon.tracker_uptime_seconds < reveal_deadline
      Input.update
      Graphics.update
    end
    return ironmon_tracker_finalize_starter
  end

  def ironmon_tracker_dispose_unopened_graphics
    [@pokeball_closed_left, @pokeball_closed_middle,
     @pokeball_closed_right, @background, @foreground].each do |sprite|
      sprite.dispose if sprite
    end
  end

  def ironmon_tracker_choose_random_or_favorite(allowed_indices)
    allowed_indices = allowed_indices.uniq
    random_pick = Ironmon.tracker_starter_random_pick_index
    commands = allowed_indices.map do |index|
      pokemon_name = @starter_pokemon[index].species_data.name
      label = index == random_pick ? "Random Pick" : "Favorite"
      _INTL("{1} ({2})", pokemon_name, label)
    end
    message_window = pbCreateMessageWindow
    message_window.text = _INTL("Favorite Clause: choose from the available starters.")
    command_window = Window_CommandPokemonEx.new(commands)
    command_window.z = 99999
    command_window.resizeToFit(commands)
    pbPositionNearMsgWindow(command_window, message_window, :right)
    Ironmon.set_tracker_starter_accepting(true)
    begin
      loop do
        Graphics.update
        Input.update
        command_window.update
        message_window.update
        requested = Ironmon.take_tracker_starter_choice
        if !requested.nil?
          @index = requested
          break
        end
        if Input.trigger?(Input::USE)
          candidate = allowed_indices[command_window.index]
          next if !Ironmon.tracker_starter_selectable?(candidate)
          @index = candidate
          Ironmon.set_tracker_starter_accepting(false)
          break
        end
      end
    ensure
      command_window.dispose
      pbDisposeMessageWindow(message_window)
    end
    updateOpenPokeballPosition
    updateStarterSelectionGraphics
    reveal_deadline = Ironmon.tracker_uptime_seconds +
      Ironmon::TRACKER_STARTER_AUTOSELECT_REVEAL_SECONDS
    while Ironmon.tracker_uptime_seconds < reveal_deadline
      Input.update
      Graphics.update
    end
    return ironmon_tracker_finalize_starter
  end

  def ironmon_tracker_finalize_starter
    raise "The selected starter is not allowed." if
      !Ironmon.tracker_starter_allowed?(@index)
    Ironmon.set_tracker_starter_accepting(false)
    chosen_pokemon = @starter_pokemon[@index]
    @spritesLoader.registerSpriteSubstitution(@pif_sprite)
    disposeGraphics
    pbSet(VAR_HOENN_CHOSEN_STARTER_INDEX, @index)
    chosen_pokemon.pif_sprite = @pif_sprite
    return chosen_pokemon
  end

  alias ironmon_tracker_original_update_starter_selection_graphics updateStarterSelectionGraphics
  def updateStarterSelectionGraphics
    result = Ironmon.with_tracker_starter_sprite(@index) do
      ironmon_tracker_original_update_starter_selection_graphics
    end
    if self.class == StartersSelectionScene && Ironmon.starter_acquisition?
      Ironmon.reveal_tracker_starter(@index, @pif_sprite)
    end
    return result
  end
end
