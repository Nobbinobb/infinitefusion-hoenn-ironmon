#===============================================================================
# Authorized Ironmon tracker debug inspection
#===============================================================================

module Ironmon
  class TrackerDebugError < StandardError
    attr_reader :code

    def initialize(code, message)
      @code = code
      super(message)
    end
  end

  def self.tracker_debug_inspect_pokemon(payload)
    tracker_validate_debug_context
    pokemon = tracker_debug_resolve_pokemon(payload || {})
    species = pokemon.species_data
    fusion = fusion_ability_species?(species)
    ability = pokemon.ability_id
    item = pokemon.item
    body = fusion ? tracker_debug_species(species.body_pokemon) : nil
    head = fusion ? tracker_debug_species(species.head_pokemon) : nil
    return {
      "pokemon_id" => pokemon.personalID.to_s,
      "nickname" => pokemon.name,
      "species_id" => tracker_species_id(pokemon),
      "species_name" => species.name,
      "sprite_path" => tracker_sprite_path(pokemon),
      "level" => pokemon.level,
      "gender" => tracker_debug_gender(pokemon),
      "held_item_id" => item ? item.id.to_s : nil,
      "held_item_name" => item ? item.name : nil,
      "fusion" => fusion,
      "form" => species.form,
      "form_name" => species.form_name,
      "body" => body,
      "head" => head,
      "active_ability_index" => pokemon.ability_index.to_i,
      "active_ability_slot" => tracker_debug_active_slot(pokemon),
      "active_ability_id" => inspector_ability_id(ability),
      "active_ability_name" => inspector_ability_name(ability),
      "generator" => tracker_debug_generator_snapshot,
      "ability_slots" => tracker_debug_ability_slots(pokemon)
    }
  end

  def self.tracker_debug_run_diagnostics
    tracker_validate_debug_context
    configuration_value = configuration
    fusion_info = custom_fusion_pool_info
    wild_mappings = $PokemonGlobal.ironmon_wild_species_map
    trainer_mappings = $PokemonGlobal.ironmon_trainer_species_map
    return {
      "game_version" => tracker_game_version,
      "ironmon_version" => VERSION,
      "protocol_version" => TRACKER_SCHEMA_VERSION,
      "run_id" => ensure_tracker_run_id,
      "battle_id" => tracker_battle_id,
      "run_seed" => $PokemonGlobal.ironmon_seed,
      "wild_policy" => configuration_value.wild_policy.to_s,
      "trainer_policy" => configuration_value.trainer_policy.to_s,
      "unfusion_setting" => configuration_value.unfusion_setting.to_s,
      "custom_fusion_pool_size" => fusion_info[:size],
      "custom_fusion_pool_fingerprint" => fusion_info[:fingerprint],
      "ability_generator_version" => AbilityGenerator::SCHEMA_VERSION,
      "ability_pool_size" => allowed_ability_pool.length,
      "ability_pool_fingerprint" => ability_pool_fingerprint,
      "wild_mapping_count" => wild_mappings.is_a?(Hash) ? wild_mappings.length : 0,
      "trainer_mapping_count" => trainer_mappings.is_a?(Hash) ? trainer_mappings.length : 0
    }
  end

  def self.tracker_validate_debug_context
    if $DEBUG != true
      raise TrackerDebugError.new(
        "debug_forbidden", "The game has not authorized tracker debug access."
      )
    end
    if !active? || !$PokemonGlobal
      raise TrackerDebugError.new(
        "ironmon_inactive", "Ironmon must be active for debug inspection."
      )
    end
    if !current_ability_randomization?
      raise TrackerDebugError.new(
        "generator_unavailable", ability_randomization_error_message
      )
    end
    return true
  end

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

  def self.tracker_debug_gender(pokemon)
    return "male" if pokemon.male?
    return "female" if pokemon.female?
    return "genderless"
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
      "ability_id" => inspector_ability_id(ability),
      "ability_name" => inspector_ability_name(ability),
      "original_ability_id" => original ? inspector_ability_id(original) : nil,
      "original_ability_name" => original ? inspector_ability_name(original) : nil,
      "eligibility" => tracker_debug_ability_eligibility(ability),
      "active" => active,
      "source" => source,
      "source_ability_id" => source_ability ? inspector_ability_id(source_ability) : nil,
      "source_ability_name" => source_ability ? inspector_ability_name(source_ability) : nil,
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
