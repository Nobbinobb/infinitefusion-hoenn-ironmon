#===============================================================================
# Audited evolution source catalog and effective methods
#===============================================================================

module Ironmon
  class EvolutionRandomizationError < StandardError; end

  class EvolutionCatalog
    SCHEMA_VERSION = 1
    RULES_VERSION = 1
    FIRST_STAGE_FALLBACK_LEVEL = 25
    INTERMEDIATE_FALLBACK_LEVEL = 35
    PRESERVED_METHODS = [
      :Level, :LevelMale, :LevelFemale, :LevelDay, :LevelNight,
      :LevelMorning, :LevelAfternoon, :LevelEvening, :LevelNoWeather,
      :LevelSun, :LevelRain, :LevelSnow, :LevelSandstorm, :LevelCycling,
      :LevelSurfing, :LevelDiving, :LevelDarkness, :AttackGreater,
      :AtkDefEqual, :DefenseGreater, :Silcoon, :Cascoon, :Ninjask,
      :Shedinja, :Happiness, :HappinessMale, :HappinessFemale,
      :HappinessDay, :HappinessNight, :HappinessHoldItem, :MaxHappiness,
      :HoldItem, :HoldItemMale, :HoldItemFemale, :DayHoldItem,
      :NightHoldItem, :HoldItemHappiness, :Item, :ItemMale, :ItemFemale,
      :ItemDay, :ItemNight, :ItemHappiness
    ].freeze

    CONVERTED_METHODS = [
      :Location, :Region, :HasInParty, :LevelDarkInParty, :HasMove,
      :HasMoveType, :HappinessMove, :HappinessMoveType, :Beauty, :Trade,
      :TradeMale, :TradeFemale, :TradeDay, :TradeNight, :TradeItem,
      :TradeSpecies
    ].freeze

    PARAMETER_REGISTRIES = {
      :Item => GameData::Item,
      :Move => GameData::Move,
      :Species => GameData::Species,
      :Type => GameData::Type
    }.freeze

    EEVEE_STONE_TYPES = {
      :WATERSTONE => :WATER,
      :THUNDERSTONE => :ELECTRIC,
      :FIRESTONE => :FIRE,
      :SUNSTONE => :PSYCHIC,
      :MOONSTONE => :DARK,
      :LEAFSTONE => :GRASS,
      :ICESTONE => :ICE,
      :SHINYSTONE => :FAIRY
    }.freeze

    attr_reader :species_catalog
    attr_reader :source_catalog
    attr_reader :taxonomy_catalog
    attr_reader :branch_catalog
    attr_reader :method_catalog
    attr_reader :normal_target_catalog
    attr_reader :unused_registered_methods
    attr_reader :source_fingerprint
    attr_reader :taxonomy_fingerprint
    attr_reader :method_fingerprint
    attr_reader :normal_target_fingerprint

    def initialize
      @species_data_by_identity = {}
      @outgoing_entries = {}
      @incoming_identities = {}
      build_species_catalog
      build_source_entries
      build_taxonomy_catalog
      build_branch_catalog
      build_method_catalog
      build_normal_target_catalog
      build_fingerprints
      freeze_catalogs
    end

    def validate
      validate_counts
      validate_taxonomy
      validate_branches
      validate_targets
      return true
    end

    def summary
      effective_count = @branch_catalog.inject(0) do |sum, branch|
        sum + branch[:effective_methods].length
      end
      converted_count = @branch_catalog.inject(0) do |sum, branch|
        sum + branch[:effective_methods].count { |method| method[:converted] }
      end
      merged_count = @branch_catalog.inject(0) do |sum, branch|
        sum + branch[:effective_methods].count do |method|
          method[:original_methods].length > 1
        end
      end
      return {
        :species_count => @species_catalog.length,
        :source_count => @source_catalog.length,
        :method_entry_count => @source_catalog.inject(0) do |sum, source|
          sum + source[:entries].length
        end,
        :conceptual_branch_count => @branch_catalog.length,
        :effective_method_count => effective_count,
        :converted_effective_method_count => converted_count,
        :merged_effective_method_count => merged_count,
        :normal_target_count => @normal_target_catalog.length,
        :mechanical_form_count => @species_catalog.count do |species|
          species[:form] > 0
        end,
        :unused_registered_methods => @unused_registered_methods.dup,
        :source_fingerprint => @source_fingerprint,
        :taxonomy_fingerprint => @taxonomy_fingerprint,
        :method_fingerprint => @method_fingerprint,
        :normal_target_fingerprint => @normal_target_fingerprint
      }
    end

    def fingerprints_for_rules(rules_version)
      return fingerprints_for(rules_version, true)
    end

    def required_target_types(source, branch)
      identity = source.respond_to?(:id) ? source.id.to_s : source.to_s
      if identity == "EEVEE"
        method = branch[:effective_methods].find do |entry|
          entry[:method] == :Item && EEVEE_STONE_TYPES.key?(entry[:parameter])
        end
        return [EEVEE_STONE_TYPES[method[:parameter]]].freeze if method
      end
      species = @species_catalog.find { |entry| entry[:identity] == identity }
      if !species
        raise EvolutionRandomizationError,
              "#{identity} has no audited source types"
      end
      return species[:types]
    end

    private

    def build_species_catalog
      records = []
      GameData::Species.each do |species|
        next if !normal_species_record?(species)
        next if species.form.to_i > 0 && !mechanical_form?(species)
        identity = species_identity(species)
        if @species_data_by_identity.key?(identity)
          raise EvolutionRandomizationError,
                "duplicate audited evolution identity #{identity}"
        end
        @species_data_by_identity[identity] = species
        records << {
          :identity => identity,
          :id => species.id,
          :id_number => species.id_number,
          :base_species => species.species,
          :form => species.form.to_i,
          :bst => species_bst(species),
          :types => species.types.freeze
        }
      end
      @species_catalog = records.sort_by do |entry|
        [entry[:id_number], entry[:form], entry[:identity]]
      end
      if @species_catalog.empty?
        raise EvolutionRandomizationError,
              "normal evolution species catalog is empty"
      end
    end

    def build_source_entries
      @species_catalog.each do |species_entry|
        identity = species_entry[:identity]
        @outgoing_entries[identity] = []
        @incoming_identities[identity] = []
      end
      @species_catalog.each do |species_entry|
        identity = species_entry[:identity]
        species = @species_data_by_identity[identity]
        species.evolutions.each_with_index do |raw_entry, index|
          validate_raw_entry(identity, raw_entry, index)
          next if raw_entry[3]
          next if raw_entry[1] == :None
          destination = exact_audited_destination(identity, raw_entry[0])
          method = raw_entry[1]
          parameter = raw_entry[2]
          validate_used_method(identity, destination, method, parameter)
          if destination == identity
            raise EvolutionRandomizationError,
                  "#{identity} has a self evolution"
          end
          entry = {
            :destination => destination,
            :method => method,
            :parameter => copy_parameter(parameter),
            :source_index => index
          }
          @outgoing_entries[identity] << entry
          @incoming_identities[destination] << identity
        end
        @outgoing_entries[identity].sort_by! do |entry|
          [entry[:destination], entry[:method].to_s,
           canonical_value(entry[:parameter]), entry[:source_index]]
        end
      end
      @incoming_identities.each_value do |incoming|
        incoming.uniq!
        incoming.sort!
      end
      @source_catalog = @species_catalog.map do |species_entry|
        identity = species_entry[:identity]
        next if @outgoing_entries[identity].empty?
        {
          :identity => identity,
          :entries => @outgoing_entries[identity].map(&:dup)
        }
      end.compact
    end

    def build_taxonomy_catalog
      validate_native_graph_is_acyclic
      adjacency = {}
      @species_catalog.each do |species|
        adjacency[species[:identity]] = []
      end
      @outgoing_entries.each do |source, entries|
        entries.each do |entry|
          destination = entry[:destination]
          adjacency[source] << destination
          adjacency[destination] << source
        end
      end
      adjacency.each_value do |neighbors|
        neighbors.uniq!
        neighbors.sort!
      end
      family_by_identity = {}
      unvisited = adjacency.keys.sort
      until unvisited.empty?
        start = unvisited[0]
        family = []
        queue = [start]
        until queue.empty?
          identity = queue.shift
          next if family.include?(identity)
          family << identity
          adjacency[identity].each do |neighbor|
            queue << neighbor if !family.include?(neighbor)
          end
        end
        family.sort!
        family_id = family[0]
        family.each { |identity| family_by_identity[identity] = family_id }
        unvisited -= family
      end
      @taxonomy_catalog = @species_catalog.map do |species|
        identity = species[:identity]
        incoming = @incoming_identities[identity]
        outgoing = @outgoing_entries[identity].map do |entry|
          entry[:destination]
        end.uniq.sort
        {
          :identity => identity,
          :role => role_for(incoming, outgoing),
          :family => family_by_identity[identity],
          :incoming => incoming.dup,
          :outgoing => outgoing
        }
      end
      @taxonomy_by_identity = {}
      @taxonomy_catalog.each do |entry|
        @taxonomy_by_identity[entry[:identity]] = entry
      end
    end

    def build_branch_catalog
      branches = []
      @source_catalog.each do |source|
        source_identity = source[:identity]
        source_role = @taxonomy_by_identity[source_identity][:role]
        grouped = source[:entries].group_by { |entry| entry[:destination] }
        grouped.keys.sort.each do |destination|
          original_methods = grouped[destination].map do |entry|
            {
              :method => entry[:method],
              :parameter => copy_parameter(entry[:parameter]),
              :source_index => entry[:source_index]
            }
          end
          effective = merge_effective_methods(source_identity, destination,
                                               source_role, original_methods)
          branches << {
            :identity => "#{source_identity}>#{destination}",
            :source => source_identity,
            :original_destination => destination,
            :original_destination_role =>
              @taxonomy_by_identity[destination][:role],
            :original_methods => original_methods,
            :effective_methods => effective
          }
        end
      end
      @branch_catalog = branches.sort_by { |branch| branch[:identity] }
    end

    def build_method_catalog
      used_counts = Hash.new(0)
      @source_catalog.each do |source|
        source[:entries].each { |entry| used_counts[entry[:method]] += 1 }
      end
      registered = []
      GameData::Evolution.each { |method| registered << method.id }
      registered.sort_by!(&:to_s)
      @method_catalog = registered.map do |method|
        policy = if PRESERVED_METHODS.include?(method)
                   :preserve
                 elsif CONVERTED_METHODS.include?(method)
                   :convert
                 else
                   :unused
                 end
        {
          :method => method,
          :policy => policy,
          :used_entries => used_counts[method]
        }
      end
      @unused_registered_methods = registered.select do |method|
        used_counts[method] == 0
      end
    end

    def build_normal_target_catalog
      @normal_target_catalog = @species_catalog.select do |species|
        species[:form] == 0
      end.map do |species|
        taxonomy = @taxonomy_by_identity[species[:identity]]
        {
          :identity => species[:identity],
          :id => species[:id],
          :id_number => species[:id_number],
          :bst => species[:bst],
          :role => taxonomy[:role],
          :family => taxonomy[:family],
          :types => species[:types]
        }
      end
    end

    def build_fingerprints
      values = fingerprints_for(RULES_VERSION, true)
      @source_fingerprint = values[:source]
      @taxonomy_fingerprint = values[:taxonomy]
      @method_fingerprint = values[:method]
      @normal_target_fingerprint = values[:target]
    end

    def fingerprints_for(rules_version, include_types)
      source_values = [SCHEMA_VERSION, rules_version]
      @species_catalog.each do |species|
        entries = [
          species[:identity], species[:id_number], species[:base_species],
          species[:form], species[:bst]
        ]
        entries << species[:types] if include_types
        source_values.concat(entries)
      end
      @source_catalog.each do |source|
        source_values << source[:identity]
        source[:entries].each do |entry|
          source_values.concat([
            entry[:destination], entry[:method], entry[:parameter],
            entry[:source_index]
          ])
        end
      end
      taxonomy_values = [SCHEMA_VERSION, rules_version]
      @taxonomy_catalog.each do |entry|
        taxonomy_values.concat([
          entry[:identity], entry[:role], entry[:family], entry[:incoming],
          entry[:outgoing]
        ])
      end
      method_values = [
        SCHEMA_VERSION, rules_version, FIRST_STAGE_FALLBACK_LEVEL,
        INTERMEDIATE_FALLBACK_LEVEL, PRESERVED_METHODS.sort_by(&:to_s),
        CONVERTED_METHODS.sort_by(&:to_s)
      ]
      @method_catalog.each do |entry|
        method_values.concat([
          entry[:method], entry[:policy], entry[:used_entries]
        ])
      end
      @branch_catalog.each do |branch|
        method_values << branch[:identity]
        branch[:effective_methods].each do |method|
          method_values.concat([
            method[:method], method[:parameter], method[:converted],
            method[:original_methods]
          ])
        end
      end
      target_values = [SCHEMA_VERSION, rules_version]
      @normal_target_catalog.each do |target|
        entries = [
          target[:identity], target[:id_number], target[:bst], target[:role],
          target[:family]
        ]
        entries << target[:types] if include_types
        target_values.concat(entries)
      end
      return {
        :source => fingerprint(source_values),
        :taxonomy => fingerprint(taxonomy_values),
        :method => fingerprint(method_values),
        :target => fingerprint(target_values)
      }
    end

    def merge_effective_methods(source, destination, source_role, originals)
      merged = {}
      originals.each do |original|
        effective = effective_method(source_role, original[:method],
                                      original[:parameter])
        key = [effective[:method], canonical_value(effective[:parameter])]
        merged[key] ||= {
          :method => effective[:method],
          :parameter => copy_parameter(effective[:parameter]),
          :converted => effective[:converted],
          :original_methods => []
        }
        merged[key][:converted] ||= effective[:converted]
        merged[key][:original_methods] << {
          :method => original[:method],
          :parameter => copy_parameter(original[:parameter]),
          :source_index => original[:source_index]
        }
      end
      methods = merged.values
      methods.sort_by! do |method|
        [method[:method].to_s, canonical_value(method[:parameter])]
      end
      if methods.empty?
        raise EvolutionRandomizationError,
              "#{source}>#{destination} has no effective methods"
      end
      return methods
    end

    def effective_method(source_role, method, parameter)
      if PRESERVED_METHODS.include?(method)
        return {
          :method => method,
          :parameter => copy_parameter(parameter),
          :converted => false
        }
      end
      fallback = fallback_level(source_role)
      effective_method, effective_parameter = case method
                                              when :LevelDarkInParty
                                                [:Level, parameter]
                                              when :HappinessMove,
                                                   :HappinessMoveType
                                                [:Happiness, nil]
                                              when :TradeMale
                                                [:LevelMale, fallback]
                                              when :TradeFemale
                                                [:LevelFemale, fallback]
                                              when :TradeDay
                                                [:LevelDay, fallback]
                                              when :TradeNight
                                                [:LevelNight, fallback]
                                              when :TradeItem
                                                [:HoldItem, parameter]
                                              else
                                                [:Level, fallback]
                                              end
      return {
        :method => effective_method,
        :parameter => copy_parameter(effective_parameter),
        :converted => true
      }
    end

    def fallback_level(source_role)
      return FIRST_STAGE_FALLBACK_LEVEL if source_role == :first_stage
      return INTERMEDIATE_FALLBACK_LEVEL if source_role == :intermediate
      raise EvolutionRandomizationError,
            "terminal source role #{source_role} has an outgoing evolution"
    end

    def validate_raw_entry(source, entry, index)
      if !entry.is_a?(Array) || entry.length < 3
        raise EvolutionRandomizationError,
              "#{source} evolution entry #{index} is malformed"
      end
      return true
    end

    def exact_audited_destination(source, destination_id)
      destination = GameData::Species.try_get(destination_id)
      if !destination || destination.id != destination_id
        raise EvolutionRandomizationError,
              "#{source} has unknown evolution destination #{destination_id}"
      end
      identity = species_identity_for_record(destination)
      if !@species_data_by_identity.key?(identity)
        raise EvolutionRandomizationError,
              "#{source} evolves outside the audited normal catalog to #{destination_id}"
      end
      return identity
    end

    def validate_used_method(source, destination, method, parameter)
      method_data = GameData::Evolution.try_get(method)
      if !method_data || method_data.id != method
        raise EvolutionRandomizationError,
              "#{source}>#{destination} uses unknown method #{method} " +
              "with parameter #{parameter.inspect}"
      end
      if !PRESERVED_METHODS.include?(method) &&
         !CONVERTED_METHODS.include?(method)
        raise EvolutionRandomizationError,
              "#{source}>#{destination} uses unaudited method #{method} " +
              "with parameter #{parameter.inspect}"
      end
      validate_parameter(source, destination, method_data, parameter)
      return true
    end

    def validate_parameter(source, destination, method_data, parameter)
      expected = method_data.parameter
      valid = if expected.nil?
                parameter.nil?
              elsif expected == Integer
                parameter.is_a?(Integer)
              elsif PARAMETER_REGISTRIES.key?(expected)
                valid_registry_parameter?(expected, parameter)
              else
                false
              end
      return true if valid
      raise EvolutionRandomizationError,
            "#{source}>#{destination} method #{method_data.id} has invalid " +
            "parameter #{parameter.inspect}; expected #{expected.inspect}"
    end

    def valid_registry_parameter?(kind, parameter)
      return false if !parameter.is_a?(Symbol)
      data = PARAMETER_REGISTRIES[kind].try_get(parameter)
      return data && data.id == parameter
    end

    def validate_native_graph_is_acyclic
      state = {}
      @species_data_by_identity.keys.sort.each do |identity|
        validate_native_node_is_acyclic(identity, state, []) if !state[identity]
      end
    end

    def validate_native_node_is_acyclic(identity, state, path)
      if state[identity] == :visiting
        raise EvolutionRandomizationError,
              "native evolution cycle detected: #{(path + [identity]).join('>')}"
      end
      return if state[identity] == :visited
      state[identity] = :visiting
      @outgoing_entries[identity].each do |entry|
        validate_native_node_is_acyclic(entry[:destination], state,
                                        path + [identity])
      end
      state[identity] = :visited
    end

    def role_for(incoming, outgoing)
      return :standalone if incoming.empty? && outgoing.empty?
      return :first_stage if incoming.empty?
      return :final if outgoing.empty?
      return :intermediate
    end

    def validate_counts
      identities = @species_catalog.map { |entry| entry[:identity] }
      if identities.uniq.length != identities.length
        raise EvolutionRandomizationError,
              "evolution species identities are not unique"
      end
      if @normal_target_catalog.empty?
        raise EvolutionRandomizationError,
              "normal evolution target catalog is empty"
      end
      return true
    end

    def validate_taxonomy
      @taxonomy_catalog.each do |entry|
        expected = role_for(entry[:incoming], entry[:outgoing])
        if entry[:role] != expected
          raise EvolutionRandomizationError,
                "#{entry[:identity]} has inconsistent stage role"
        end
        if !@taxonomy_by_identity.key?(entry[:family])
          raise EvolutionRandomizationError,
                "#{entry[:identity]} has unknown family #{entry[:family]}"
        end
      end
      return true
    end

    def validate_branches
      branch_ids = {}
      @branch_catalog.each do |branch|
        if branch_ids[branch[:identity]]
          raise EvolutionRandomizationError,
                "duplicate conceptual branch #{branch[:identity]}"
        end
        branch_ids[branch[:identity]] = true
        if branch[:original_methods].empty? ||
           branch[:effective_methods].empty?
          raise EvolutionRandomizationError,
                "#{branch[:identity]} has no activation method"
        end
        effective_keys = branch[:effective_methods].map do |method|
          [method[:method], canonical_value(method[:parameter])]
        end
        if effective_keys.uniq.length != effective_keys.length
          raise EvolutionRandomizationError,
                "#{branch[:identity]} has duplicate effective triggers"
        end
      end
      return true
    end

    def validate_targets
      @normal_target_catalog.each do |target|
        if target[:types].empty?
          raise EvolutionRandomizationError,
                "#{target[:identity]} has no evolution target type"
        end
        if target[:role] == :first_stage &&
           @taxonomy_by_identity[target[:identity]][:incoming].length > 0
          raise EvolutionRandomizationError,
                "#{target[:identity]} has invalid first-stage taxonomy"
        end
        if target[:bst] <= 0
          raise EvolutionRandomizationError,
                "#{target[:identity]} has invalid BST #{target[:bst]}"
        end
      end
      return true
    end

    def normal_species_record?(species)
      return species.id_number > 0 && species.id_number <= NB_POKEMON
    end

    def species_identity(species)
      return species.id.to_s if species.form.to_i == 0
      return species.id.to_s if mechanical_form?(species)
      base = GameData::Species.try_get(species.species)
      return base.id.to_s if base
      return species.species.to_s
    end

    def species_identity_for_record(species)
      return species_identity(species)
    end

    def mechanical_form?(species)
      return false if species.form.to_i == 0
      base = GameData::Species.try_get(species.species)
      return true if !base || base.id == species.id
      return mechanical_signature(species) != mechanical_signature(base)
    end

    def mechanical_signature(species)
      stats = if species.respond_to?(:ironmon_unrandomized_base_stats)
                species.ironmon_unrandomized_base_stats
              else
                species.base_stats
              end
      abilities = if species.respond_to?(:ironmon_unrandomized_abilities)
                    species.ironmon_unrandomized_abilities
                  else
                    species.abilities
                  end
      hidden = if species.respond_to?(:ironmon_unrandomized_hidden_abilities)
                 species.ironmon_unrandomized_hidden_abilities
               else
                 species.hidden_abilities
               end
      moves = if species.respond_to?(:ironmon_unrandomized_moves)
                species.ironmon_unrandomized_moves
              else
                species.moves
              end
      return [
        species.type1, species.type2,
        stats.keys.sort_by(&:to_s).map do |stat|
          [stat, stats[stat]]
        end,
        abilities, hidden, moves, species.evolutions
      ]
    end

    def species_bst(species)
      stats = if species.respond_to?(:ironmon_unrandomized_base_stats)
                species.ironmon_unrandomized_base_stats
              else
                species.base_stats
              end
      total = stats.values.inject(0) do |sum, value|
        sum + value.to_i
      end
      if total <= 0
        raise EvolutionRandomizationError,
              "#{species.id} has invalid BST #{total}"
      end
      return total
    end

    def copy_parameter(parameter)
      return parameter.map { |value| copy_parameter(value) } if
        parameter.is_a?(Array)
      return parameter.dup if parameter.is_a?(String)
      return parameter
    end

    def canonical_value(value)
      return "nil" if value.nil?
      if value.is_a?(Array)
        return "[#{value.map { |entry| canonical_value(entry) }.join(',')}]"
      end
      if value.is_a?(Hash)
        entries = value.keys.sort_by { |key| canonical_value(key) }.map do |key|
          "#{canonical_value(key)}=#{canonical_value(value[key])}"
        end
        return "{#{entries.join(',')}}"
      end
      return "#{value.class}:#{value}"
    end

    def fingerprint(values)
      return Ironmon.fnv1a_64_fingerprint(values) do |entry|
        canonical_value(entry)
      end
    end

    def freeze_catalogs
      [@species_catalog, @source_catalog, @taxonomy_catalog, @branch_catalog,
       @method_catalog, @normal_target_catalog,
       @unused_registered_methods].each do |catalog|
        deep_freeze(catalog)
      end
    end

    def deep_freeze(value)
      if value.is_a?(Array)
        value.each { |entry| deep_freeze(entry) }
      elsif value.is_a?(Hash)
        value.each do |key, entry|
          deep_freeze(key)
          deep_freeze(entry)
        end
      end
      value.freeze
    end
  end

  def self.evolution_catalog
    @evolution_catalog ||= EvolutionCatalog.new
    return @evolution_catalog
  end

  def self.validate_evolution_catalogs
    return evolution_catalog.validate
  end

  def self.reset_evolution_catalog_cache
    @evolution_catalog = nil
  end
end
