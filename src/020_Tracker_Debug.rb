#===============================================================================
# Authorized Ironmon tracker debug inspection
#===============================================================================

module Ironmon
  def self.tracker_debug_ability_name(ability)
    return _INTL("None") if !ability
    data = GameData::Ability.try_get(ability)
    return ability.to_s if !data
    return data.name
  end

  def self.tracker_debug_ability_id(ability)
    return "-" if !ability
    data = GameData::Ability.try_get(ability)
    return ability.to_s if !data
    return data.id.to_s
  end

  class TrackerDebugError < StandardError
    attr_reader :code

    def initialize(code, message)
      @code = code
      super(message)
    end
  end

  def self.tracker_debug_inspect_pokemon(payload)
    payload ||= {}
    section = payload["section"].to_s
    section = "overview" if section.empty?
    valid_sections = ["overview", "abilities", "stats", "moves", "evolutions"]
    if !valid_sections.include?(section)
      raise TrackerDebugError.new("invalid_section", "The requested Pokemon inspector section is invalid.")
    end
    availability = tracker_debug_availability_capability(payload["target"])
    tracker_validate_debug_context([availability])
    tracker_validate_any_debug_context(
      tracker_information_diagnostic_capabilities(section)
    )
    pokemon = tracker_debug_resolve_pokemon(payload)
    species = pokemon.species_data
    fusion = fusion_ability_species?(species)
    ability = pokemon.ability_id
    item = pokemon.item
    body = fusion ? tracker_debug_species(species.body_pokemon) : nil
    head = fusion ? tracker_debug_species(species.head_pokemon) : nil
    result = {
      "section" => section,
      "identity" => {
        "pokemon_id" => pokemon.personalID.to_s,
        "nickname" => pokemon.name,
        "species_id" => tracker_species_id(pokemon),
        "species_name" => species.name,
        "sprite_path" => tracker_sprite_path(pokemon),
        "level" => pokemon.level,
        "gender" => tracker_gender(pokemon),
        "held_item_id" => item ? item.id.to_s : nil,
        "held_item_name" => item ? item.name : nil,
        "fusion" => fusion,
        "form" => species.form,
        "form_name" => species.form_name,
        "body" => body,
        "head" => head,
        "active_ability_index" => pokemon.ability_index.to_i,
        "active_ability_slot" => tracker_debug_active_slot(pokemon),
        "active_ability_id" => tracker_debug_ability_id(ability),
        "active_ability_name" => tracker_debug_ability_name(ability)
      },
      "lookup" => tracker_pokemon_lookup_for_recipe(
        {
          "species_id" => tracker_species_id(pokemon),
          "section" => section
        },
        tracker_debug_active_recipe,
        tracker_debug_lookup_visibility
      )
    }

    case section
    when "abilities"
      result["abilities"] = {
        "generator" => tracker_debug_generator_snapshot,
        "slots" => tracker_debug_ability_slots(pokemon)
      }
    when "stats"
      original_stats = original_base_stats_for_pokemon(pokemon)
      generated_stats = if base_stat_randomization_active?
                          generated_base_stats_for_pokemon(pokemon)
                        else
                          original_stats
                        end
      result["stats"] = {
        "original" => tracker_base_stat_snapshot(original_stats),
        "original_total" => tracker_base_stat_total(original_stats),
        "generated" => tracker_base_stat_snapshot(generated_stats),
        "generated_total" => tracker_base_stat_total(generated_stats),
        "generator" => tracker_debug_base_stat_generator_snapshot
      }
    end
    return result
  end

  def self.tracker_debug_run_diagnostics
    capabilities = [
      "run.configuration", "run.seed", "run.generator_manifests",
      "evolution.generator_details"
    ]
    tracker_validate_any_debug_context(capabilities)
    wild_mappings = $PokemonGlobal.ironmon_wild_species_map
    trainer_mappings = $PokemonGlobal.ironmon_trainer_species_map
    result = {
      "runtime" => {
        "game_version" => tracker_game_version,
        "ironmon_version" => VERSION,
        "protocol_version" => TRACKER_SCHEMA_VERSION,
        "run_id" => ensure_tracker_run_id,
        "battle_id" => tracker_battle_id
      }
    }
    if tracker_connection.diagnostic_capability?("run.seed")
      result["runtime"]["run_seed"] = $PokemonGlobal.ironmon_seed
    end
    if tracker_connection.diagnostic_capability?("run.configuration")
      result["configuration"] = configuration_snapshot
    end
    if tracker_connection.diagnostic_capability?("run.generator_manifests")
      result["species_generator"] = tracker_species_generator_recipe
      result["ability_generator"] = tracker_ability_generator_recipe
      result["base_stat_generator"] = tracker_base_stat_generator_recipe
      result["move_access_generator"] = tracker_move_access_generator_recipe
      result["item_generator"] = item_generator_recipe
      result["player_fusion_generator"] = tracker_player_fusion_generator_recipe
      result["mappings"] = {
        "wild" => wild_mappings.is_a?(Hash) ? wild_mappings.length : 0,
        "trainer" => trainer_mappings.is_a?(Hash) ? trainer_mappings.length : 0
      }
    end
    if tracker_connection.diagnostic_capability?("evolution.generator_details")
      result["evolution_generator"] = tracker_evolution_generator_recipe
    end
    return result
  end
end
