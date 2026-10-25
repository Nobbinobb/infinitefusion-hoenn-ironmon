#===============================================================================
# Stable spread description and one current stat during fusion move selection
#===============================================================================

module Ironmon
  FUSION_SPREAD_CUTOFFS = [20, 35, 50, 65].freeze
  FUSION_SPREAD_LABELS = [
    _INTL("Even"), _INTL("Slightly uneven"), _INTL("Uneven"),
    _INTL("Highly uneven"), _INTL("Extremely uneven")
  ].freeze
  FUSION_STAT_METHODS = {
    :HP => :totalhp, :ATTACK => :attack, :DEFENSE => :defense,
    :SPECIAL_ATTACK => :spatk, :SPECIAL_DEFENSE => :spdef, :SPEED => :speed
  }.freeze

  def self.fusion_spread_label(stats)
    values = BaseStatGenerator::STAT_ORDER.map { |stat| stats.fetch(stat) }
    total = values.inject(0, :+)
    raise ArgumentError, "fusion stats must have a positive total" if total <= 0
    variance = values.length * values.inject(0) { |sum, value| sum + value * value } - total * total
    band = FUSION_SPREAD_CUTOFFS.count do |cutoff|
      10_000 * variance >= cutoff * cutoff * total * total
    end
    return FUSION_SPREAD_LABELS[band]
  end

  def self.fusion_stat_clue(pokemon)
    stats = pokemon.species_data.base_stats
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    hash = fnv1a_64_joined([
      1, seed, "analysis_fusion_spread_reveal", pokemon.species_data.id_number
    ])
    stat = BaseStatGenerator::STAT_ORDER[fusion_choice_hash(hash) % 6]
    return {
      :label => fusion_spread_label(stats), :stat => stat,
      :name => GameData::Stat.get(stat).name,
      :value => pokemon.public_send(FUSION_STAT_METHODS.fetch(stat))
    }.freeze
  end
end

class FusionMovesOptionsScene
  alias ironmon_clue_original_draw_pokemon_info draw_pokemon_info

  def draw_pokemon_info
    return ironmon_clue_original_draw_pokemon_info if
      !Ironmon.active? || !@fused_pokemon.isFusion?
    clue = Ironmon.fusion_stat_clue(@fused_pokemon)
    @sprites["bg_stats"].visible = true
    @sprites["bg_moves"].visible = false
    @sprites["title"].text = ""
    bitmap = @sprites["overlay"].bitmap
    bitmap.clear
    color = Color.new(248, 248, 248)
    shadow = Color.new(104, 104, 104)
    positions = []
    labels = [_INTL("HP"), _INTL("Attack"), _INTL("Defense"),
      _INTL("Sp. Attack"), _INTL("Sp. Defense"), _INTL("Speed")]
    Ironmon::BaseStatGenerator::STAT_ORDER.each_with_index do |stat, index|
      y = 78 + index * 32
      positions << [labels[index], 20, y, 0, color, shadow]
      value = stat == clue[:stat] ? clue[:value].to_s : "?"
      positions << [value, 158, y, 0, color, shadow]
    end
    pbDrawTextPositions(bitmap, positions)
  end

  alias ironmon_clue_original_update_description updateDescription

  def updateDescription(index)
    ironmon_clue_original_update_description(index)
    return if !Ironmon.active? || !@fused_pokemon.isFusion? || (index || 0) != 0
    col = @sprites["option"] ? (@sprites["option"][0] || 0) : 0
    species = col == 0 ? @head_species : @body_species
    @sprites["textbox"].text = _INTL("Spread: {1}\nSelect all moves from {2}",
      Ironmon.fusion_stat_clue(@fused_pokemon)[:label], GameData::Species.get(species).name)
  end
end
