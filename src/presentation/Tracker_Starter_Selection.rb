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
    if tracker_auto_select_starter?
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
    end
    @tracker_starter_selection = {
      :pokemon => pokemon,
      :sprites => Array.new(pokemon.length),
      :revealed => Array.new(pokemon.length, false),
      :random_pick_index => random_pick
    }
    if tracker_auto_select_starter?
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
    selection[:sprites][index] = sprite if sprite
    return if selection[:revealed][index]
    selection[:revealed][index] = true
    tracker_connection.send_event(
      "starter_selection_changed", tracker_starter_selection_snapshot
    )
  rescue Exception => e
    echoln "Ironmon tracker starter reveal failed safely: #{e.message}"
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
        ceiling = tracker_maximum_starter_base_stat_total
        choice["bst_eligible"] = !ceiling ||
          choice["base_stat_total"] <= ceiling
        choice["favorite"] = tracker_favorite_pokemon?(pokemon)
      end
      choice
    end
    return {
      "active" => true,
      "random_pick_index" => selection[:random_pick_index],
      "maximum_base_stat_total" => tracker_maximum_starter_base_stat_total,
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
        Ironmon.tracker_auto_select_starter?
      return ironmon_tracker_original_start_scene
    ensure
      Ironmon.end_tracker_starter_selection
    end
  end

  def ironmon_tracker_auto_select_starter
    initializeGraphics
    ceiling = Ironmon.tracker_maximum_starter_base_stat_total
    eligible_indices = @starter_pokemon.each_index.select do |index|
      total = @starter_pokemon[index].baseStats.values.inject(0) do |sum, value|
        sum + value
      end
      !ceiling || total <= ceiling
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
      Ironmon.tracker_favorite_pokemon?(@starter_pokemon[index])
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
    position = pbMessage(
      _INTL("Favorite Clause: choose from the available starters."),
      commands, 0
    )
    @index = allowed_indices[position]
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
    chosen_pokemon = @starter_pokemon[@index]
    @spritesLoader.registerSpriteSubstitution(@pif_sprite)
    disposeGraphics
    pbSet(VAR_HOENN_CHOSEN_STARTER_INDEX, @index)
    chosen_pokemon.pif_sprite = @pif_sprite
    return chosen_pokemon
  end

  alias ironmon_tracker_original_update_starter_selection_graphics updateStarterSelectionGraphics
  def updateStarterSelectionGraphics
    result = ironmon_tracker_original_update_starter_selection_graphics
    if self.class == StartersSelectionScene && Ironmon.starter_acquisition?
      Ironmon.reveal_tracker_starter(@index, @pif_sprite)
    end
    return result
  end
end
