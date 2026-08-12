#===============================================================================
# Ironmon tracker starter selection
#===============================================================================

module Ironmon
  TRACKER_STARTER_RANDOM_PICK_NAMESPACE = "starter_random_pick"

  def self.begin_tracker_starter_selection(pokemon)
    return if !active? || !pokemon || pokemon.empty?
    random_pick = tracker_starter_random_pick(pokemon.length)
    @tracker_starter_selection = {
      :pokemon => pokemon,
      :sprites => Array.new(pokemon.length),
      :revealed => Array.new(pokemon.length, false),
      :random_pick_index => random_pick
    }
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
      end
      choice
    end
    return {
      "active" => true,
      "random_pick_index" => selection[:random_pick_index],
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
      return ironmon_tracker_original_start_scene
    ensure
      Ironmon.end_tracker_starter_selection
    end
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
