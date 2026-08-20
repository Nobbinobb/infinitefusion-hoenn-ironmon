#===============================================================================
# Ironmon generated ability slot resolution and inspection
#===============================================================================

module Ironmon
  def self.ability_species_identity(species_data)
    return species_data.id.to_s
  end

  def self.ability_base_species(species_data)
    return species_data.species if species_data.respond_to?(:species)
    return species_data.id
  end

  def self.registered_ability?(ability)
    data = GameData::Ability.try_get(ability)
    return data && data.id == ability
  end

  def self.eligible_ability_pool(species_data)
    base_species = ability_base_species(species_data)
    @eligible_ability_pools ||= {}
    cached = @eligible_ability_pools[base_species]
    return cached if cached
    contextual = []
    [AbilityGenerator::EXACT_SPECIES_ABILITY_RULES,
     AbilityGenerator::COMPONENT_ABILITY_RULES].each do |rules|
      rules.each do |ability, species|
        next if !species.include?(base_species)
        next if !registered_ability?(ability)
        contextual << ability
      end
    end
    if contextual.empty?
      @eligible_ability_pools[base_species] = allowed_ability_pool
      return allowed_ability_pool
    end
    pool = allowed_ability_pool + contextual
    pool.sort_by! do |ability|
      data = GameData::Ability.get(ability)
      [data.id_number, data.id.to_s]
    end
    @eligible_ability_pools[base_species] = pool.freeze
    return @eligible_ability_pools[base_species]
  end

  def self.exact_species_ability?(ability)
    return AbilityGenerator::EXACT_SPECIES_ABILITY_RULES.key?(ability)
  end

  def self.fusion_ability_slots(species_data, slots, kind)
    used = {}
    resolved = Array.new(slots.length)
    identity = ability_species_identity(species_data)
    slots.each_with_index do |ability, index|
      next if !ability
      if exact_species_ability?(ability)
        ability = ability_generator.fusion_fallback(
          identity, kind, index, used
        )
      end
      resolved[index] = ability
      used[ability] = true
    end
    return resolved
  end

  def self.normal_ability_species?(species_data)
    return false if !species_data
    return species_data.id_number > 0 && species_data.id_number <= NB_POKEMON
  end

  def self.fusion_ability_species?(species_data)
    return false if !species_data
    return false if species_data.id_number <= NB_POKEMON
    return false if species_data.id_number >= Settings::ZAPMOLCUNO_NB
    return species_data.respond_to?(:body_pokemon) &&
      species_data.respond_to?(:head_pokemon)
  end

  def self.original_normal_abilities(species_data)
    if fusion_ability_species?(species_data)
      body = original_normal_abilities(species_data.body_pokemon)
      head = original_normal_abilities(species_data.head_pokemon)
      return [body[0], head[0]]
    end
    return species_data.ironmon_unrandomized_abilities.dup
  end

  def self.original_hidden_abilities(species_data)
    if fusion_ability_species?(species_data)
      body_normal = original_normal_abilities(species_data.body_pokemon)
      head_normal = original_normal_abilities(species_data.head_pokemon)
      body_hidden = original_hidden_abilities(species_data.body_pokemon)
      head_hidden = original_hidden_abilities(species_data.head_pokemon)
      slots = [
        body_normal[1],
        head_normal[1],
        body_hidden[0],
        head_hidden[0]
      ]
      return trim_trailing_nil_ability_slots(slots)
    end
    return species_data.ironmon_unrandomized_hidden_abilities.dup
  end

  def self.generated_normal_abilities(species_data)
    return original_normal_abilities(species_data) if
      !ability_randomization_active?
    return ability_generator.fusion_slots_for(species_data)[:normal] if
      fusion_ability_species?(species_data)
    return original_normal_abilities(species_data) if
      !normal_ability_species?(species_data)
    return ability_generator.slots_for(species_data)[:normal]
  end

  def self.generated_hidden_abilities(species_data)
    return original_hidden_abilities(species_data) if
      !ability_randomization_active?
    return ability_generator.fusion_slots_for(species_data)[:hidden] if
      fusion_ability_species?(species_data)
    return original_hidden_abilities(species_data) if
      !normal_ability_species?(species_data)
    return ability_generator.slots_for(species_data)[:hidden]
  end

  def self.trim_trailing_nil_ability_slots(slots)
    result = slots.dup
    result.pop while !result.empty? && !result[-1]
    return result
  end

  def self.resolved_ability_index(pokemon, requested_index, slots)
    resolved_index = requested_index.to_i
    normal = slots[:normal]
    hidden = slots[:hidden]
    if resolved_index >= 2 && !hidden[resolved_index - 2]
      if fusion_ability_species?(pokemon.species_data)
        case resolved_index
        when 2
          resolved_index = 0
        when 3
          resolved_index = 1
        when 4
          resolved_index = hidden[0] ? 2 : 0
        when 5
          resolved_index = hidden[1] ? 3 : 1
        else
          resolved_index = pokemon.personalID.to_i & 1
        end
      else
        resolved_index = pokemon.personalID.to_i & 1
      end
    end
    if resolved_index >= 2 && !hidden[resolved_index - 2]
      resolved_index = pokemon.personalID.to_i & 1
    end
    if resolved_index < 2 && !normal[resolved_index]
      resolved_index = 0
    end
    return resolved_index
  end

  def self.normalize_ability_index(pokemon, requested_index = nil)
    return pokemon if !pokemon || !ability_randomization_active?
    requested_index = pokemon.ability_index if requested_index.nil?
    slots = {
      :normal => generated_normal_abilities(pokemon.species_data),
      :hidden => generated_hidden_abilities(pokemon.species_data)
    }
    resolved_index = resolved_ability_index(pokemon, requested_index, slots)
    pokemon.ability_index = resolved_index
    pokemon.ability = nil
    return pokemon
  end

  def self.assign_generated_hidden_ability(pokemon)
    return pokemon if !pokemon || !ability_randomization_active?
    hidden = generated_hidden_abilities(pokemon.species_data)
    available = []
    hidden.each_with_index do |ability, index|
      available << index if ability
    end
    return pokemon if available.empty?
    selected = available[pokemon.personalID.to_i % available.length]
    pokemon.ability_index = selected + 2
    pokemon.ability = nil
    return pokemon
  end

  def self.ability_slot_lines(label, slots)
    lines = []
    slots.each_with_index do |ability, index|
      next if !ability
      data = GameData::Ability.get(ability)
      lines << "#{label} #{index}: #{data.name} (#{data.id})"
    end
    lines << "#{label}: none" if lines.empty?
    return lines
  end

  def self.ability_inspection_text(pokemon)
    species_data = pokemon.species_data
    lines = []
    lines << "Ironmon ability inspector"
    lines << "Species: #{species_data.name} (#{species_data.id})"
    lines << "Seed: #{$PokemonGlobal.ironmon_seed}"
    lines << "Generator: #{AbilityGenerator::SCHEMA_VERSION}"
    lines << "Pool: #{allowed_ability_pool.length} " +
      "(#{ability_pool_fingerprint})"
    lines << "Active slot: #{pokemon.ability_index} / #{pokemon.ability_id}"
    if fusion_ability_species?(species_data)
      lines << "Body: #{species_data.body_pokemon.name} " +
        "(#{species_data.body_pokemon.id})"
      lines << "Head: #{species_data.head_pokemon.name} " +
        "(#{species_data.head_pokemon.id})"
      lines.concat(ability_slot_lines(
        "Body generated normal",
        generated_normal_abilities(species_data.body_pokemon)
      ))
      lines.concat(ability_slot_lines(
        "Body generated hidden",
        generated_hidden_abilities(species_data.body_pokemon)
      ))
      lines.concat(ability_slot_lines(
        "Head generated normal",
        generated_normal_abilities(species_data.head_pokemon)
      ))
      lines.concat(ability_slot_lines(
        "Head generated hidden",
        generated_hidden_abilities(species_data.head_pokemon)
      ))
    end
    lines.concat(ability_slot_lines(
      "Original normal", original_normal_abilities(species_data)
    ))
    lines.concat(ability_slot_lines(
      "Original hidden", original_hidden_abilities(species_data)
    ))
    lines.concat(ability_slot_lines(
      "Generated normal", generated_normal_abilities(species_data)
    ))
    lines.concat(ability_slot_lines(
      "Generated hidden", generated_hidden_abilities(species_data)
    ))
    return lines.join("\n")
  end
end
