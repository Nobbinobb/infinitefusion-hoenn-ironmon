#===============================================================================
# Ironmon diagnostic Pokemon resolution and snapshots
#===============================================================================

module Ironmon
  def self.tracker_debug_resolve_pokemon(payload)
    target = payload["target"].to_s
    pokemon = if target == "player"
                @tracker_player_pokemon
              elsif target == "enemy"
                position = payload["enemy_position"]
                battler = @tracker_enemy_battlers[position.to_i] if
                  position && @tracker_enemy_battlers
                battler ? battler.pokemon : nil
              elsif target == "party"
                index = payload["party_index"]
                if index && $Trainer && index.to_i >= 0
                  $Trainer.party[index.to_i]
                end
              end
    if !pokemon
      raise TrackerDebugError.new(
        "pokemon_not_found", "The requested Pokemon is not available for inspection."
      )
    end
    return pokemon
  end

  def self.tracker_debug_species(species)
    return {
      "species_id" => "#{species.id}:#{species.form}",
      "species_name" => species.name
    }
  end

  def self.tracker_debug_generator_snapshot
    return {
      "run_seed" => $PokemonGlobal.ironmon_seed,
      "schema_version" => AbilityGenerator::SCHEMA_VERSION,
      "pool_rules_version" => AbilityGenerator::POOL_RULES_VERSION,
      "pool_size" => allowed_ability_pool.length,
      "pool_fingerprint" => ability_pool_fingerprint
    }
  end

  def self.tracker_debug_base_stat_generator_snapshot
    return {
      "enabled" => base_stat_randomization_active?,
      "run_seed" => $PokemonGlobal.ironmon_seed,
      "schema_version" => $PokemonGlobal.ironmon_base_stat_generator_version,
      "rules_version" => BaseStatGenerator::RULES_VERSION,
      "minimum_stat" => BaseStatGenerator::MINIMUM_STAT,
      "maximum_stat" => BaseStatGenerator::MAXIMUM_STAT,
      "source_fingerprint" => $PokemonGlobal.ironmon_base_stat_source_fingerprint
    }
  end

  def self.tracker_debug_active_slot(pokemon)
    index = pokemon.ability_index.to_i
    return "Hidden #{index - 2}" if index >= 2
    return "Normal #{index}"
  end

  def self.tracker_debug_ability_slots(pokemon)
    species = pokemon.species_data
    ability = pokemon.ability_id
    active_kind = pokemon.ability_index.to_i >= 2 ? :hidden : :normal
    active_index = pokemon.ability_index.to_i >= 2 ? pokemon.ability_index.to_i - 2 : pokemon.ability_index.to_i
    result = [tracker_debug_ability_slot(
      :current, active_kind, active_index, ability, nil, true
    )]
    if fusion_ability_species?(species)
      tracker_debug_append_fusion_slots(result, species)
      tracker_debug_append_species_slots(result, :body_generated, species.body_pokemon)
      tracker_debug_append_species_slots(result, :head_generated, species.head_pokemon)
    else
      tracker_debug_append_species_slots(result, :generated, species)
    end
    return result.compact
  end

  def self.tracker_debug_append_species_slots(result, group, species)
    normal = generated_normal_abilities(species)
    hidden = generated_hidden_abilities(species)
    original_normal = original_normal_abilities(species)
    original_hidden = original_hidden_abilities(species)
    tracker_debug_append_slot_kind(
      result, group, :normal, normal, original_normal
    )
    tracker_debug_append_slot_kind(
      result, group, :hidden, hidden, original_hidden
    )
  end

  def self.tracker_debug_append_slot_kind(result, group, kind, generated, original)
    generated.each_with_index do |ability, index|
      next if !ability
      result << tracker_debug_ability_slot(
        group, kind, index, ability, original[index], false
      )
    end
  end

  def self.tracker_debug_append_fusion_slots(result, species)
    normal = generated_normal_abilities(species)
    hidden = generated_hidden_abilities(species)
    original_normal = original_normal_abilities(species)
    original_hidden = original_hidden_abilities(species)
    [[:normal, normal, original_normal],
     [:hidden, hidden, original_hidden]].each do |kind, generated, original|
      generated.each_with_index do |ability, index|
        next if !ability
        source = tracker_debug_fusion_slot_source(species, kind, index)
        result << tracker_debug_ability_slot(
          :final_fusion, kind, index, ability, original[index], false,
          source[0], source[1]
        )
      end
    end
  end

  def self.tracker_debug_fusion_slot_source(species, kind, index)
    component = index.even? ? species.body_pokemon : species.head_pokemon
    component_name = index.even? ? "Body" : "Head"
    normal = generated_normal_abilities(component)
    hidden = generated_hidden_abilities(component)
    return ["#{component_name} normal 0", normal[0]] if kind == :normal
    return ["#{component_name} normal 1", normal[1]] if index < 2
    return ["#{component_name} hidden 0", hidden[0]]
  end

  def self.tracker_debug_ability_slot(group, kind, index, ability, original,
                                       active, source = nil,
                                       source_ability = nil)
    return nil if !ability
    return {
      "group" => group.to_s,
      "kind" => kind.to_s,
      "index" => index,
      "ability_id" => tracker_debug_ability_id(ability),
      "ability_name" => tracker_debug_ability_name(ability),
      "original_ability_id" => original ? tracker_debug_ability_id(original) : nil,
      "original_ability_name" => original ? tracker_debug_ability_name(original) : nil,
      "eligibility" => tracker_debug_ability_eligibility(ability),
      "active" => active,
      "source" => source,
      "source_ability_id" => source_ability ? tracker_debug_ability_id(source_ability) : nil,
      "source_ability_name" => source_ability ? tracker_debug_ability_name(source_ability) : nil,
      "restricted_source_replaced" => !!(source_ability && source_ability != ability)
    }
  end

  def self.tracker_debug_ability_eligibility(ability)
    return "none" if !ability
    if AbilityGenerator::EXACT_SPECIES_ABILITY_RULES.key?(ability)
      return "exact_species"
    end
    if AbilityGenerator::COMPONENT_ABILITY_RULES.key?(ability)
      return "component_compatible"
    end
    return "universal"
  end
end
