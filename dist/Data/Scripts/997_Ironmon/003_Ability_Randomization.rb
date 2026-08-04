#===============================================================================
# Deterministic Pokemon ability randomization
#===============================================================================

module Ironmon
  class AbilityRandomizationError < StandardError; end

  class AbilityGenerator
    SCHEMA_VERSION = 2
    POOL_RULES_VERSION = 2
    FNV_OFFSET_BASIS = 14_695_981_039_346_656_037
    FNV_PRIME = 1_099_511_628_211
    FNV_MASK = 0xFFFFFFFFFFFFFFFF

    EXACT_SPECIES_ABILITY_RULES = {
      :RKSSYSTEM       => [:SILVALLY],
      :BATTLEBOND      => [:GRENINJA],
      :POWERCONSTRUCT  => [:ZYGARDE],
      :SCHOOLING       => [:WISHIWASHI],
      :ICEFACE         => [:EISCUE],
      :ZENMODE         => [:DARMANITAN],
      :FORECAST        => [:CASTFORM]
    }.freeze

    COMPONENT_ABILITY_RULES = {
      :DISGUISE     => [:MIMIKYU],
      :SHIELDSDOWN  => [:MINIOR, :MINIOR_M, :MINIOR_C]
    }.freeze

    CONTEXTUAL_ABILITIES = (
      EXACT_SPECIES_ABILITY_RULES.keys + COMPONENT_ABILITY_RULES.keys
    ).freeze

    attr_reader :pool
    attr_reader :pool_fingerprint

    def initialize(seed, pool, pool_fingerprint)
      @seed = seed.to_i
      @pool = pool
      @pool_fingerprint = pool_fingerprint
      @slot_cache = {}
      @fusion_slot_cache = {}
    end

    def slots_for(species_data)
      identity = Ironmon.ability_species_identity(species_data)
      cached = @slot_cache[identity]
      return cached if cached

      original_normal = Ironmon.original_normal_abilities(species_data)
      original_hidden = Ironmon.original_hidden_abilities(species_data)
      defined_slot_count = original_normal.compact.length +
        original_hidden.compact.length
      eligible_pool = Ironmon.eligible_ability_pool(species_data)
      if defined_slot_count > eligible_pool.length
        raise AbilityRandomizationError,
              "the allowed ability pool is too small for #{identity}"
      end

      used = {}
      normal = generate_slot_array(
        identity, :normal, original_normal, used, eligible_pool
      )
      hidden = generate_slot_array(
        identity, :hidden, original_hidden, used, eligible_pool
      )
      result = {
        :normal => normal.freeze,
        :hidden => hidden.freeze
      }.freeze
      @slot_cache[identity] = result
      return result
    end

    def fusion_fallback(identity, kind, index, used = {})
      return select_ability(identity, kind, index, used, @pool, "fusion")
    end

    def fusion_slots_for(species_data)
      identity = Ironmon.ability_species_identity(species_data)
      cached = @fusion_slot_cache[identity]
      return cached if cached

      body = component_slots_for(species_data.body_pokemon)
      head = component_slots_for(species_data.head_pokemon)
      body_secondary = body[:normal][1] || body[:normal][0]
      head_secondary = head[:normal][1] || head[:normal][0]
      normal = Ironmon.fusion_ability_slots(
        species_data, [body[:normal][0], head[:normal][0]], :normal
      ).freeze
      hidden = Ironmon.fusion_ability_slots(species_data, [
        body_secondary,
        head_secondary,
        body[:hidden][0] || body_secondary,
        head[:hidden][0] || head_secondary
      ], :hidden).freeze
      result = { :normal => normal, :hidden => hidden }.freeze
      @fusion_slot_cache[identity] = result
      return result
    end

    private

    def component_slots_for(species_data)
      return slots_for(species_data) if
        Ironmon.normal_ability_species?(species_data)
      return {
        :normal => Ironmon.original_normal_abilities(species_data),
        :hidden => Ironmon.original_hidden_abilities(species_data)
      }
    end

    def generate_slot_array(identity, kind, original_slots, used, pool)
      generated = Array.new(original_slots.length)
      original_slots.each_with_index do |original_ability, index|
        next if !original_ability
        generated[index] = select_ability(
          identity, kind, index, used, pool, "species"
        )
        used[generated[index]] = true
      end
      return generated
    end

    def select_ability(identity, kind, index, used, pool, namespace)
      start = deterministic_value(identity, kind, index, namespace, "start") %
        pool.length
      pool.length.times do |offset|
        candidate = pool[(start + offset) % pool.length]
        return candidate if !used[candidate]
      end
      raise AbilityRandomizationError,
            "no unique ability remains for #{identity} #{kind} slot #{index}"
    end

    def deterministic_value(*parts)
      value = FNV_OFFSET_BASIS
      input = [SCHEMA_VERSION, @seed, "ability", *parts].join("|")
      input.each_byte do |byte|
        value ^= byte
        value = (value * FNV_PRIME) & FNV_MASK
      end
      return value
    end
  end

  def self.allowed_ability_pool
    return @allowed_ability_pool if @allowed_ability_pool
    contextual = {}
    AbilityGenerator::CONTEXTUAL_ABILITIES.each do |ability|
      contextual[ability] = true
    end
    pool = []
    GameData::Ability.each do |ability|
      next if !ability || contextual[ability.id]
      next if ability.id_number < 0
      pool << ability
    end
    pool.sort_by! { |ability| [ability.id_number, ability.id.to_s] }
    pool.map! { |ability| ability.id }
    if pool.empty?
      raise AbilityRandomizationError, "the allowed ability pool is empty"
    end
    @allowed_ability_pool = pool.freeze
    @ability_pool_fingerprint = ability_pool_fingerprint_for(pool)
    return @allowed_ability_pool
  end

  def self.ability_pool_fingerprint
    allowed_ability_pool
    return @ability_pool_fingerprint
  end

  def self.ability_pool_fingerprint_for(pool)
    value = AbilityGenerator::FNV_OFFSET_BASIS
    entries = [AbilityGenerator::POOL_RULES_VERSION] + pool
    [AbilityGenerator::EXACT_SPECIES_ABILITY_RULES,
     AbilityGenerator::COMPONENT_ABILITY_RULES].each do |rules|
      rules.keys.sort_by { |ability| ability.to_s }.each do |ability|
        entries << ability
        rules[ability].each { |species| entries << species }
      end
    end
    entries.each do |entry|
      entry.to_s.each_byte do |byte|
        value ^= byte
        value = (value * AbilityGenerator::FNV_PRIME) &
          AbilityGenerator::FNV_MASK
      end
      value ^= 0
      value = (value * AbilityGenerator::FNV_PRIME) &
        AbilityGenerator::FNV_MASK
    end
    return sprintf("%016x", value)
  end

  def self.prepare_ability_randomization
    @ability_randomization_ready = false
    raise AbilityRandomizationError, "run metadata is unavailable" if
      !$PokemonGlobal
    pool = allowed_ability_pool
    $PokemonGlobal.ironmon_ability_generator_version =
      AbilityGenerator::SCHEMA_VERSION
    $PokemonGlobal.ironmon_ability_pool_size = pool.length
    $PokemonGlobal.ironmon_ability_pool_fingerprint =
      ability_pool_fingerprint
    reset_ability_generator_cache
    ability_generator
    @ability_randomization_ready = true
    @ability_randomization_error_message = nil
    return true
  rescue AbilityRandomizationError => e
    @ability_randomization_error_message = _INTL(
      "Ironmon could not prepare ability randomization: {1}", e.message
    )
    echoln @ability_randomization_error_message
    return false
  rescue Exception => e
    @ability_randomization_error_message = _INTL(
      "Ironmon could not prepare ability randomization because of an unexpected error: {1}",
      e.message
    )
    echoln @ability_randomization_error_message
    return false
  end

  def self.current_ability_randomization?
    return false if !$PokemonGlobal
    return false if $PokemonGlobal.ironmon_ability_generator_version !=
      AbilityGenerator::SCHEMA_VERSION
    return false if $PokemonGlobal.ironmon_ability_pool_size !=
      allowed_ability_pool.length
    return false if $PokemonGlobal.ironmon_ability_pool_fingerprint !=
      ability_pool_fingerprint
    return true
  rescue Exception
    return false
  end

  def self.ensure_ability_randomization
    @ability_randomization_ready = false
    return false if !$PokemonGlobal
    if !$PokemonGlobal.ironmon_ability_generator_version &&
       !$PokemonGlobal.ironmon_ability_pool_fingerprint
      generated = prepare_ability_randomization
      echoln "Ironmon migrated ability randomization metadata." if generated
      return generated
    end
    if !current_ability_randomization?
      raise AbilityRandomizationError,
            "the saved ability generator or allowed pool is incompatible"
    end
    reset_ability_generator_cache
    ability_generator
    @ability_randomization_ready = true
    return true
  end

  def self.ability_randomization_active?
    return false if !$PokemonGlobal
    return false if $PokemonGlobal.ironmon_mode != true
    return @ability_randomization_ready == true
  end

  def self.ability_generator
    seed = $PokemonGlobal ? $PokemonGlobal.ironmon_seed : 0
    if !@ability_generator || @ability_generator_seed != seed
      @ability_generator_seed = seed
      @ability_generator = AbilityGenerator.new(
        seed, allowed_ability_pool, ability_pool_fingerprint
      )
    end
    return @ability_generator
  end

  def self.reset_ability_generator_cache
    @ability_generator = nil
    @ability_generator_seed = nil
  end

  def self.suspend_ability_randomization
    @ability_randomization_ready = false
    reset_ability_generator_cache
  end

  def self.ability_randomization_error_message
    return @ability_randomization_error_message ||
      _INTL("Ironmon could not prepare ability randomization.")
  end

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
      body_secondary = body_normal[1] || body_normal[0]
      head_secondary = head_normal[1] || head_normal[0]
      return [
        body_secondary,
        head_secondary,
        body_hidden[0] || body_secondary,
        head_hidden[0] || head_secondary
      ]
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

  def self.normalize_ability_index(pokemon, requested_index = nil)
    return pokemon if !pokemon || !ability_randomization_active?
    requested_index = pokemon.ability_index if requested_index.nil?
    normal = generated_normal_abilities(pokemon.species_data)
    hidden = generated_hidden_abilities(pokemon.species_data)
    resolved_index = requested_index.to_i
    if resolved_index >= 2
      if !hidden[resolved_index - 2]
        resolved_index = pokemon.personalID.to_i & 1
      end
    end
    if resolved_index < 2 && !normal[resolved_index]
      resolved_index = 0
    end
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

class GameData::Species
  alias ironmon_unrandomized_abilities abilities
  alias ironmon_unrandomized_hidden_abilities hidden_abilities

  def abilities
    return ironmon_unrandomized_abilities if
      !Ironmon.ability_randomization_active?
    return Ironmon.ability_generator.fusion_slots_for(self)[:normal] if
      Ironmon.fusion_ability_species?(self)
    return ironmon_unrandomized_abilities if
      !Ironmon.normal_ability_species?(self)
    return Ironmon.ability_generator.slots_for(self)[:normal]
  end

  def hidden_abilities
    return ironmon_unrandomized_hidden_abilities if
      !Ironmon.ability_randomization_active?
    return Ironmon.ability_generator.fusion_slots_for(self)[:hidden] if
      Ironmon.fusion_ability_species?(self)
    return ironmon_unrandomized_hidden_abilities if
      !Ironmon.normal_ability_species?(self)
    return Ironmon.ability_generator.slots_for(self)[:hidden]
  end
end

class Pokemon
  alias ironmon_ability_original_ability_id ability_id
  def ability_id
    return ironmon_ability_original_ability_id if
      !Ironmon.ability_randomization_active?
    species_value = species_data
    slots = if Ironmon.fusion_ability_species?(species_value)
              Ironmon.ability_generator.fusion_slots_for(species_value)
            elsif Ironmon.normal_ability_species?(species_value)
              Ironmon.ability_generator.slots_for(species_value)
            else
              {
                :normal => species_value.ironmon_unrandomized_abilities,
                :hidden => species_value.ironmon_unrandomized_hidden_abilities
              }
            end
    index = ability_index
    selected = nil
    if index >= 2
      selected = slots[:hidden][index - 2]
      index = (@personalID & 1) if !selected
    end
    selected ||= slots[:normal][index] || slots[:normal][0]
    return selected
  end

  alias ironmon_ability_original_ability= ability=
  def ability=(value)
    if Ironmon.ability_randomization_active?
      @ability = nil
      return
    end
    self.ironmon_ability_original_ability = value
  end

  alias ironmon_ability_original_species= species=
  def species=(species_id)
    old_ability_index = ability_index
    self.ironmon_ability_original_species = species_id
    Ironmon.normalize_ability_index(self, old_ability_index)
  end


  alias ironmon_ability_original_type1 type1
  def type1
    if Ironmon.ability_randomization_active? && hasAbility?(:MULTITYPE) &&
       species_data.type1 == :NORMAL
      return getHeldPlateType()
    end
    return ironmon_ability_original_type1
  end

  alias ironmon_ability_original_type2 type2
  def type2
    if Ironmon.ability_randomization_active? && hasAbility?(:MULTITYPE) &&
       species_data.type2 == :NORMAL
      return getHeldPlateType()
    end
    return ironmon_ability_original_type2
  end

  alias ironmon_ability_original_checkHPRelatedFormChange checkHPRelatedFormChange
  def checkHPRelatedFormChange
    if Ironmon.ability_randomization_active? && hasAbility?(:SHIELDSDOWN)
      return if $game_temp.in_battle
      if isFusionOf(:MINIOR_M) && @hp <= (@totalhp / 2)
        changeFormSpecies(:MINIOR_M, :MINIOR_C)
      elsif isFusionOf(:MINIOR_C) && @hp > (@totalhp / 2)
        changeFormSpecies(:MINIOR_C, :MINIOR_M)
      end
      return
    end
    ironmon_ability_original_checkHPRelatedFormChange
  end
end

Events.onWildPokemonCreate += proc { |_sender, event|
  if Ironmon.ability_randomization_active? &&
     (player_on_hidden_ability_map || isAlwaysHiddenAbilityMap($game_map.map_id))
    Ironmon.assign_generated_hidden_ability(event[0])
  end
}

Events.onTrainerPartyLoad += proc { |_sender, event|
  trainer = event[0]
  if Ironmon.ability_randomization_active? && trainer && trainer.party
    trainer.party.each { |pokemon| Ironmon.normalize_ability_index(pokemon) }
  end
}

alias ironmon_ability_original_pb_hatch pbHatch
def pbHatch(pokemon)
  result = ironmon_ability_original_pb_hatch(pokemon)
  if Ironmon.ability_randomization_active? && player_on_hidden_ability_map
    Ironmon.assign_generated_hidden_ability(pokemon)
  else
    Ironmon.normalize_ability_index(pokemon)
  end
  return result
end

if defined?(PokemonDebugMenuCommands)
  PokemonDebugMenuCommands.register("ironmon_ability_inspector", {
    "parent"      => "main",
    "name"        => _INTL("Inspect Ironmon abilities"),
    "always_show" => true,
    "effect"      => proc { |pkmn, _pkmnid, _heldpoke, _settingUpBattle, screen|
      if !Ironmon.active?
        screen.pbDisplay(_INTL("Ironmon is not active."))
      elsif !Ironmon.current_ability_randomization?
        screen.pbDisplay(Ironmon.ability_randomization_error_message)
      else
        screen.pbDisplay(Ironmon.ability_inspection_text(pkmn))
      end
      next false
    }
  })
end

module Game
  class << self
    alias ironmon_ability_original_load load
    def load(save_data)
      Ironmon.suspend_ability_randomization
      result = ironmon_ability_original_load(save_data)
      Ironmon.ensure_ability_randomization if Ironmon.active?
      return result
    end
  end
end
