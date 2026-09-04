module Ironmon
  PROFILE_GAME_DATA_CLASSES = {
    "types" => GameData::Type,
    "abilities" => GameData::Ability,
    "moves" => GameData::Move,
    "items" => GameData::Item,
    "species" => GameData::Species,
    "trainer_types" => GameData::TrainerType
  }.freeze
  PROFILE_TRAINER_CLASSES = {
    "classic" => GameData::Trainer,
    "remix" => GameData::TrainerModern,
    "expert" => GameData::TrainerExpert
  }.freeze
  PROFILE_ENCOUNTER_CLASSES = {
    "classic" => GameData::Encounter,
    "remix" => GameData::EncounterModern,
    "randomized" => GameData::EncounterRandom
  }.freeze
  PROFILE_SOURCE_CACHE_VARIABLES = [
    :@normal_species_pool,
    :@fully_evolved_normal_species_pool,
    :@fully_evolved_normal_species_index,
    :@fully_evolved_custom_fusion_pool,
    :@fully_evolved_custom_fusion_index,
    :@allowed_ability_pool, :@ability_pool_fingerprint,
    :@base_stat_source_fingerprint,
    :@validated_base_stat_source_fingerprint,
    :@evolution_catalog,
    :@allowed_level_up_move_pool, :@damaging_level_up_move_pool,
    :@eligible_damaging_level_up_move_pools,
    :@eligible_level_up_move_pools, :@eligible_machine_move_pools,
    :@level_up_move_pool_fingerprint,
    :@level_up_move_contextual_fingerprint,
    :@move_access_source_entries, :@move_access_source_fingerprint,
    :@egg_move_access_source_entries,
    :@egg_move_access_source_fingerprint,
    :@machine_item_rosters, :@machine_move_pools,
    :@machine_roster_fingerprints,
    :@machine_move_access_source_entries,
    :@machine_move_access_source_fingerprints,
    :@all_machine_move_lookup, :@raw_machine_move_lookups,
    :@raw_machine_move_rosters,
    :@ordinary_tutor_source_entries,
    :@ordinary_tutor_source_fingerprint,
    :@ordinary_tutor_catalog_fingerprint,
    :@original_specialized_tutor_catalogs,
    :@original_specialized_tutor_move_cache,
    :@specialized_tutor_catalog_fingerprint,
    :@specialized_tutor_source_fingerprint,
    :@item_ground_pools, :@item_ground_weights, :@item_tm_pools
  ].freeze

  module ProfileGameDataDisplay
    def name
      return @real_name
    end

    def name_plural
      return @real_name_plural || @real_name
    end

    def description
      return @real_description
    end

    def form_name
      return @real_form_name
    end

    def category
      return @category if instance_variable_defined?(:@category)
      return @real_category
    end

    def pokedex_entry
      return @real_pokedex_entry
    end
  end

  def self.generation_profile_base_catalog(profile_id)
    @generation_profile_base_catalogs ||= {}
    return @generation_profile_base_catalogs[profile_id] if
      @generation_profile_base_catalogs[profile_id]
    path = generation_profile_component_path("base_catalog", profile_id)
    document = generation_profile_stringify_keys(JSON.parse(
      generation_profile_runtime_json(File.binread(path))
    ))
    if !document.is_a?(Hash) || document["schema_version"] != 1 ||
       document["normal_species_count"] != NB_POKEMON
      raise GenerationProfileUnavailable,
        "the generation profile base catalog is incompatible with this runtime"
    end
    PROFILE_GAME_DATA_CLASSES.keys.each do |name|
      if !document[name].is_a?(Array)
        raise GenerationProfileUnavailable,
          "the generation profile base catalog is missing #{name}"
      end
    end
    if !document["trainers"].is_a?(Hash) ||
       !document["encounters"].is_a?(Hash)
      raise GenerationProfileUnavailable,
        "the generation profile world catalogs are missing"
    end
    @generation_profile_base_catalogs[profile_id] = document
    return document
  rescue GenerationProfileUnavailable
    raise
  rescue Exception => exception
    raise GenerationProfileUnavailable,
      "the generation profile base catalog is invalid: #{exception.message}"
  end

  def self.generation_profile_runtime_json(bytes)
    result = +""
    quoted = false
    escaped = false
    index = 0
    while index < bytes.length
      character = bytes[index]
      if quoted
        result << character
        if escaped
          escaped = false
        elsif character == "\\"
          escaped = true
        elsif character == '"'
          quoted = false
        end
        index += 1
        next
      end
      if character == '"'
        quoted = true
        result << character
        index += 1
      elsif bytes[index, 4] == "null"
        result << "nil"
        index += 4
      else
        result << character
        index += 1
      end
    end
    return result
  end

  def self.generation_profile_stringify_keys(value)
    if value.is_a?(Hash)
      result = {}
      value.each do |key, entry|
        result[key.to_s] = generation_profile_stringify_keys(entry)
      end
      return result
    end
    return value.map { |entry| generation_profile_stringify_keys(entry) } if
      value.is_a?(Array)
    return value
  end

  def self.generation_profile_game_data(profile_id)
    @generation_profile_game_data ||= {}
    return @generation_profile_game_data[profile_id] if
      @generation_profile_game_data[profile_id]
    document = generation_profile_base_catalog(profile_id)
    result = {}
    PROFILE_GAME_DATA_CLASSES.each do |name, data_class|
      records = document[name]
      result[data_class] = generation_profile_records(
        data_class, name, records, document
      )
    end
    PROFILE_TRAINER_CLASSES.each do |name, data_class|
      result[data_class] = generation_profile_trainer_records(
        data_class, document["trainers"][name] || []
      )
    end
    PROFILE_ENCOUNTER_CLASSES.each do |name, data_class|
      result[data_class] = generation_profile_encounter_records(
        data_class, document["encounters"][name] || []
      )
    end
    @generation_profile_game_data[profile_id] = result
    return result
  end

  def self.generation_profile_fusion_name_parts(profile_id)
    records = generation_profile_base_catalog(profile_id)["fusion_name_parts"]
    return nil if !records.is_a?(Array) || records.empty?
    prefixes = []
    suffixes = []
    records.each do |record|
      id_number = record["id_number"].to_i
      next if id_number < 1 || id_number > NB_POKEMON
      prefixes[id_number] = record["prefix"]
      suffixes[id_number] = record["suffix"]
    end
    return [prefixes.freeze, suffixes.freeze].freeze
  end

  def self.generation_profile_records(data_class, name, records, document)
    result = {}
    numeric_ids = data_class.method(:each).owner == GameData::ClassMethods
    evolution_parameters = generation_profile_evolution_parameter_types(document)
    records.each do |record|
      attributes = case name
                   when "types"
                     generation_profile_type_attributes(record)
                   when "abilities"
                     generation_profile_ability_attributes(record)
                   when "moves"
                     generation_profile_move_attributes(record)
                   when "items"
                     generation_profile_item_attributes(record)
                   when "species"
                     generation_profile_species_attributes(
                       record, evolution_parameters
                     )
                   when "trainer_types"
                     generation_profile_trainer_type_attributes(record)
                   end
      entry = data_class.new(attributes)
      entry.extend(ProfileGameDataDisplay)
      result[entry.id] = entry
      if numeric_ids && entry.respond_to?(:id_number)
        result[entry.id_number] = entry
      end
    end
    return result.freeze
  end

  def self.generation_profile_type_attributes(record)
    return {
      :id => record["id"].to_sym,
      :id_number => record["id_number"],
      :name => record["name"],
      :special_type => record["special"] == true,
      :pseudo_type => record["pseudo"] == true,
      :weaknesses => generation_profile_symbols(record["weaknesses"]),
      :resistances => generation_profile_symbols(record["resistances"]),
      :immunities => generation_profile_symbols(record["immunities"])
    }
  end

  def self.generation_profile_ability_attributes(record)
    return {
      :id => record["id"].to_sym,
      :id_number => record["id_number"],
      :name => record["name"],
      :description => record["description"]
    }
  end

  def self.generation_profile_move_attributes(record)
    return {
      :id => record["id"].to_sym,
      :id_number => record["id_number"],
      :name => record["name"],
      :function_code => record["function_code"],
      :base_damage => record["base_damage"],
      :type => generation_profile_symbol(record["type"]),
      :category => record["category"],
      :accuracy => record["accuracy"],
      :total_pp => record["total_pp"],
      :effect_chance => record["effect_chance"],
      :target => generation_profile_symbol(record["target"]),
      :priority => record["priority"],
      :flags => record["flags"] || [],
      :description => record["description"]
    }
  end

  def self.generation_profile_item_attributes(record)
    return {
      :id => record["id"].to_sym,
      :id_number => record["id_number"],
      :name => record["name"],
      :name_plural => record["plural_name"],
      :pocket => record["pocket"],
      :price => record["price"],
      :description => record["description"],
      :field_use => record["field_use"],
      :battle_use => record["battle_use"],
      :type => record["type"],
      :move => generation_profile_symbol(record["move"])
    }
  end

  def self.generation_profile_species_attributes(record, parameter_types)
    wild_items = record["wild_items"] || []
    return {
      :id => record["id"].to_sym,
      :id_number => record["id_number"],
      :species => generation_profile_symbol(record["species"]),
      :form => record["form"],
      :name => record["name"],
      :form_name => record["form_name"],
      :category => record["category"],
      :pokedex_entry => record["pokedex_entry"],
      :pokedex_form => record["pokedex_form"],
      :type1 => generation_profile_symbol(record["type1"]),
      :type2 => generation_profile_symbol(record["type2"]),
      :base_stats => generation_profile_symbol_hash(record["base_stats"]),
      :evs => generation_profile_symbol_hash(record["evs"]),
      :base_exp => record["base_exp"],
      :growth_rate => generation_profile_symbol(record["growth_rate"]),
      :gender_ratio => generation_profile_symbol(record["gender_ratio"]),
      :catch_rate => record["catch_rate"],
      :happiness => record["happiness"],
      :moves => (record["moves"] || []).map do |level, move|
        [level, generation_profile_symbol(move)]
      end,
      :tutor_moves => generation_profile_symbols(record["tutor_moves"]),
      :egg_moves => generation_profile_symbols(record["egg_moves"]),
      :abilities => generation_profile_symbols(record["abilities"]),
      :hidden_abilities => generation_profile_symbols(record["hidden_abilities"]),
      :wild_item_common => generation_profile_symbol(wild_items[0]),
      :wild_item_uncommon => generation_profile_symbol(wild_items[1]),
      :wild_item_rare => generation_profile_symbol(wild_items[2]),
      :egg_groups => generation_profile_symbols(record["egg_groups"]),
      :hatch_steps => record["hatch_steps"],
      :incense => generation_profile_symbol(record["incense"]),
      :evolutions => generation_profile_evolutions(
        record["evolutions"], parameter_types
      ),
      :height => record["height"],
      :weight => record["weight"],
      :color => generation_profile_symbol(record["color"]),
      :shape => generation_profile_symbol(record["shape"]),
      :habitat => generation_profile_symbol(record["habitat"]),
      :generation => record["generation"],
      :mega_stone => generation_profile_symbol(record["mega_stone"]),
      :mega_move => generation_profile_symbol(record["mega_move"]),
      :unmega_form => record["unmega_form"],
      :mega_message => record["mega_message"]
    }
  end

  def self.generation_profile_evolution_parameter_types(document)
    result = {}
    (document["evolution_methods"] || []).each do |entry|
      result[entry["id"].to_s] = entry["parameter_type"].to_s
    end
    return result
  end

  def self.generation_profile_evolutions(values, parameter_types)
    return (values || []).map do |target, method, parameter, prevolution|
      parameter_type = parameter_types[method.to_s]
      parameter = generation_profile_symbol(parameter) if
        ["Item", "Move", "Species", "Type"].include?(parameter_type)
      [
        generation_profile_symbol(target),
        generation_profile_symbol(method),
        parameter,
        prevolution == true
      ]
    end
  end

  def self.generation_profile_trainer_type_attributes(record)
    return {
      :id => record["id"].to_sym,
      :id_number => record["id_number"],
      :name => record["name"],
      :base_money => record["base_money"],
      :battle_BGM => record["battle_bgm"],
      :victory_ME => record["victory_me"],
      :intro_ME => record["intro_me"],
      :gender => record["gender"],
      :skill_level => record["skill_level"],
      :skill_code => record["skill_code"]
    }
  end

  def self.generation_profile_trainer_records(data_class, records)
    result = {}
    records.each do |record|
      identifier = record["id"] || []
      trainer_type = generation_profile_symbol(record["trainer_type"])
      name = record["name"].to_s
      version = record["version"].to_i
      attributes = {
        :id => [trainer_type, identifier[1] || name, version],
        :id_number => record["id_number"],
        :trainer_type => trainer_type,
        :name => name,
        :version => version,
        :items => generation_profile_symbols(record["items"]),
        :pokemon => (record["pokemon"] || []).map do |pokemon|
          generation_profile_trainer_pokemon(pokemon)
        end
      }
      entry = data_class.new(attributes)
      entry.extend(ProfileGameDataDisplay)
      result[entry.id] = entry
    end
    return result.freeze
  end

  def self.generation_profile_trainer_pokemon(record)
    result = {}
    record.each { |key, value| result[key.to_sym] = value }
    [:species, :ability, :item, :nature, :poke_ball].each do |key|
      result[key] = generation_profile_symbol(result[key]) if result.key?(key)
    end
    [:moves, :moves_hard, :moves_easy].each do |key|
      result[key] = generation_profile_symbols(result[key]) if result.key?(key)
    end
    result[:iv] = generation_profile_symbol_hash(result[:iv]) if result[:iv]
    result[:ev] = generation_profile_symbol_hash(result[:ev]) if result[:ev]
    return result
  end

  def self.generation_profile_encounter_records(data_class, records)
    result = {}
    records.each do |record|
      types = {}
      (record["types"] || {}).each do |type, slots|
        types[type.to_sym] = slots.map do |slot|
          copy = slot.dup
          copy[1] = generation_profile_symbol(copy[1])
          copy
        end
      end
      attributes = {
        :id => record["id"].to_sym,
        :map => record["map"],
        :version => record["version"],
        :step_chances => generation_profile_symbol_hash(
          record["step_chances"]
        ),
        :types => types
      }
      entry = data_class.new(attributes)
      result[entry.id] = entry
    end
    return result.freeze
  end

  def self.generation_profile_symbol(value)
    return nil if value.nil?
    return value if value.is_a?(Symbol)
    return value.to_sym
  end

  def self.generation_profile_symbols(values)
    return (values || []).map { |value| generation_profile_symbol(value) }
  end

  def self.generation_profile_symbol_hash(values)
    result = {}
    (values || {}).each do |key, value|
      result[generation_profile_symbol(key)] = value
    end
    return result
  end

  def self.activate_generation_profile_data(profile_id)
    data = generation_profile_game_data(profile_id)
    snapshot = { :game_data => {}, :source_caches => {} }
    data.each do |data_class, entries|
      current = data_class::DATA
      snapshot[:game_data][data_class] = current.dup
      current.clear
      current.merge!(entries)
    end
    PROFILE_SOURCE_CACHE_VARIABLES.each do |variable|
      snapshot[:source_caches][variable] = instance_variable_get(variable)
      instance_variable_set(variable, nil)
    end
    return snapshot
  rescue Exception
    restore_generation_profile_data(snapshot) if snapshot
    raise
  end

  def self.restore_generation_profile_data(snapshot)
    return if !snapshot
    snapshot[:game_data].each do |data_class, entries|
      current = data_class::DATA
      current.clear
      current.merge!(entries)
    end
    snapshot[:source_caches].each do |variable, value|
      instance_variable_set(variable, value)
    end
  end
end
