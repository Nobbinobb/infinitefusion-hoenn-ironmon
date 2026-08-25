module IronmonPlayerFusionWorkerCatalogExporter
  OUTPUT_PATH = $ironmon_player_fusion_worker_catalog_output_path.to_s
  VERIFICATION_SEED = 1_187_411_801
  EVOLUTION_VERIFICATION_SEEDS = [
    VERIFICATION_SEED, 1_792_136_788, 1_689, 2_895
  ].freeze
  EVOLUTION_VERIFICATION_SOURCE_COUNT = 96
  ROLE_CODES = {
    :standalone => 0, :first_stage => 1, :intermediate => 2, :final => 3
  }.freeze
  ITEM_EVOLUTION_METHODS = [
    :HappinessHoldItem, :HoldItem, :HoldItemMale, :HoldItemFemale,
    :DayHoldItem, :NightHoldItem, :HoldItemHappiness,
    :Item, :ItemMale, :ItemFemale, :ItemDay, :ItemNight, :ItemHappiness
  ].freeze

  def self.type_codes
    types = []
    GameData::Species.each do |species|
      next if species.id_number <= 0 ||
        species.id_number >= Settings::ZAPMOLCUNO_NB
      types << species.type1 if species.type1
      types << species.type2 if species.type2
    end
    result = {}
    types.compact.uniq.map(&:to_s).sort.each_with_index do |type, index|
      result[type.to_sym] = index
    end
    return result
  end

  def self.type_mask(species, codes)
    return [species.type1, species.type2].compact.uniq.inject(0) do |mask, type|
      mask | (1 << codes.fetch(type))
    end
  end

  def self.normal_species(codes)
    catalog = Ironmon.evolution_catalog
    taxonomy = {}
    catalog.taxonomy_catalog.each { |entry| taxonomy[entry[:identity]] = entry }
    families = catalog.taxonomy_catalog.map { |entry| entry[:family] }.uniq.sort
    family_codes = {}
    families.each_with_index { |family, index| family_codes[family] = index }
    (1..NB_POKEMON).map do |species_id|
      species = GameData::Species.get(species_id)
      stats = Ironmon.original_base_stats_for(species)
      entry = taxonomy.fetch(species.id.to_s)
      [
        species_id, species.id.to_s,
        *Ironmon::BaseStatGenerator::STAT_ORDER.map { |stat| stats[stat].to_i },
        type_mask(species, codes), codes.fetch(species.type1) + 1,
        species.type2 ? codes.fetch(species.type2) + 1 : 0,
        ROLE_CODES.fetch(entry[:role]), family_codes.fetch(entry[:family])
      ]
    end
  end

  def self.evolution_branches(codes)
    catalog = Ironmon.evolution_catalog
    catalog.branch_catalog.map do |branch|
      source = GameData::Species.get(branch[:source])
      destination = GameData::Species.get(branch[:original_destination])
      required_types = catalog.required_target_types(source, branch)
      required_mask = required_types.inject(0) do |mask, type|
        mask | (1 << codes.fetch(type))
      end
      item_options = branch[:effective_methods].map do |method|
        evolution_method_item(method)
      end.uniq
      [
        source.id_number, branch[:identity], destination.id_number,
        ROLE_CODES.fetch(branch[:original_destination_role]), required_mask,
        item_options
      ]
    end
  end

  def self.evolution_method_item(method)
    return "POKEBALL" if method[:method] == :Shedinja
    return "" if !ITEM_EVOLUTION_METHODS.include?(method[:method])
    item = GameData::Item.try_get(method[:parameter])
    return item ? item.id.to_s : ""
  end

  def self.fusion_pool(codes)
    Ironmon.custom_fusion_pool.map do |identity|
      match = /\AB(\d+)H(\d+)\z/.match(identity.to_s)
      raise "invalid custom fusion identity #{identity.inspect}" if !match
      body_id = match[1].to_i
      head_id = match[2].to_i
      species = GameData::Species.get(identity)
      [
        (body_id << Ironmon::PlayerFusionMapper::MATERIAL_ID_BITS) | head_id,
        type_mask(species, codes)
      ]
    end
  end

  def self.verification_pairs
    ids = [1, 4, 7, 25, 94, 150, 251, 384, 493, NB_POKEMON]
    result = []
    ids.each_with_index do |first, first_index|
      ids[first_index..-1].each { |second| result << [first, second] }
    end
    (1..NB_POKEMON).each do |first|
      second = ((first * 257) + 113) % NB_POKEMON + 1
      result << [first, second].sort
    end
    return result.uniq
  end

  def self.verification
    mapper = Ironmon::PlayerFusionMapper.new(
      VERIFICATION_SEED, Ironmon.custom_fusion_pool, {}, {},
      Ironmon::BaseStatGenerator.new(
        VERIFICATION_SEED, Ironmon.base_stat_source_fingerprint
      )
    )
    return verification_pairs.map do |first, second|
      results = mapper.species_pair(first, second).map do |identity|
        GameData::Species.get(identity).id_number
      end
      [first, second, results[0], results[1]]
    end
  end

  def self.evolution_verification_sources
    pool = Ironmon.custom_fusion_pool
    stride = [pool.length / EVOLUTION_VERIFICATION_SOURCE_COUNT, 1].max
    selected = []
    index = 0
    while index < pool.length && selected.length < EVOLUTION_VERIFICATION_SOURCE_COUNT
      selected << pool[index]
      index += stride
    end
    selected.concat([:B506H10, :B285H506, :B133H15, :B14H289])
    return selected.map(&:to_sym).uniq.select do |identity|
      Ironmon.custom_fusion_species?(identity)
    end
  end

  def self.evolution_verification
    catalog = Ironmon.evolution_catalog
    pool = Ironmon.custom_fusion_pool
    pool_info = Ironmon.custom_fusion_pool_info
    result = []
    EVOLUTION_VERIFICATION_SEEDS.each do |seed|
      generator = Ironmon::FusionEvolutionGenerator.new(
        seed, catalog, pool, pool_info,
        Ironmon::BaseStatGenerator.new(
          seed, Ironmon.base_stat_source_fingerprint
        )
      )
      evolution_verification_sources.each do |identity|
        species = GameData::Species.get(identity)
        match = /\AB(\d+)H(\d+)\z/.match(identity.to_s)
        packed = (match[1].to_i << 10) | match[2].to_i
        branches = generator.branches_for(species).map do |branch|
          [
            branch[:component_side] == :body ? 0 : 1,
            branch[:component_branch_identity],
            GameData::Species.get(branch[:target_id]).id_number
          ]
        end
        result << [seed, packed, branches]
      end
    end
    return result
  end

  def self.run
    codes = type_codes
    info = Ironmon.custom_fusion_pool_info
    document = {
      "schema_version" => 3,
      "normal_species_count" => NB_POKEMON,
      "base_stat_source_fingerprint" =>
        Ironmon.base_stat_source_fingerprint,
      "player_fusion_generator_version" =>
        Ironmon::PlayerFusionMapper::SCHEMA_VERSION,
      "custom_fusion_pool_version" => info[:schema_version],
      "custom_fusion_pool_size" => info[:size],
      "custom_fusion_pool_fingerprint" => info[:fingerprint],
      "excluded_sprite_authors" =>
        Ironmon::CustomFusionPool::AUTOGENERATED_SPRITE_AUTHORS,
      "rejected_autogenerated_credit_count" =>
        info[:rejected_autogenerated_credits],
      "type_count" => codes.length,
      "type_names" => codes.sort_by { |_type, code| code }.map do |type, _code|
        type.to_s
      end,
      "normal_species" => normal_species(codes),
      "evolution_generator_version" =>
        Ironmon::FusionEvolutionGenerator::SCHEMA_VERSION,
      "evolution_rules_version" =>
        Ironmon::FusionEvolutionGenerator::RULES_VERSION,
      "evolution_source_fingerprint" =>
        Ironmon.evolution_catalog.source_fingerprint,
      "evolution_taxonomy_fingerprint" =>
        Ironmon.evolution_catalog.taxonomy_fingerprint,
      "evolution_method_fingerprint" =>
        Ironmon.evolution_catalog.method_fingerprint,
      "evolution_branches" => evolution_branches(codes),
      "custom_fusion_pool" => fusion_pool(codes),
      "verification_seed" => VERIFICATION_SEED,
      "verification_mappings" => verification,
      "evolution_verification_mappings" => evolution_verification
    }
    temporary_path = "#{OUTPUT_PATH}.tmp"
    File.binwrite(temporary_path, JSON.generate(document) + "\n")
    File.delete(OUTPUT_PATH) if File.file?(OUTPUT_PATH)
    File.rename(temporary_path, OUTPUT_PATH)
  end
end

IronmonPlayerFusionWorkerCatalogExporter.run
