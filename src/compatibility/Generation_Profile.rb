require "digest/sha2"

module Ironmon
  module GenerationProfile
    SCHEMA_VERSION = 1
    SHA256_PATTERN = /\A[0-9a-f]{64}\z/
    NAME_PATTERN = /\A[a-z][a-z0-9_]*\z/
    ALGORITHM_FAMILIES = [
      "hash_contract",
      "custom_fusion_eligibility",
      "species_mapping",
      "ability_assignment",
      "base_stats",
      "move_access",
      "item_slots",
      "normal_evolution",
      "fusion_evolution",
      "player_fusion_reversal",
      "gym_party_expansion",
      "caught_fusion_component",
      "progression_support_pokemon"
    ].sort.freeze
    CURRENT_ALGORITHM_VERSIONS = ALGORITHM_FAMILIES.each_with_object({}) do |name, result|
      result[name] = 1
    end.freeze

    def self.build(algorithms, components)
      manifest = {
        "schema_version" => SCHEMA_VERSION,
        "algorithms" => algorithms.map do |name, version|
          { "name" => name.to_s, "version" => version }
        end,
        "components" => components
      }
      return normalize(manifest)
    end

    def self.component(name, schema_version, path)
      bytes = File.binread(path)
      return {
        "name" => name.to_s,
        "schema_version" => schema_version,
        "sha256" => Digest::SHA256.hexdigest(bytes),
        "byte_length" => bytes.bytesize
      }
    end

    def self.fingerprint(manifest)
      return Digest::SHA256.hexdigest(canonical_json(normalize(manifest)))
    end

    def self.canonical_json(value)
      return generate_json(canonical_value(value))
    end

    def self.normalize(manifest)
      raise ArgumentError, "generation profile must be a hash" if !manifest.is_a?(Hash)
      schema_version = value_for(manifest, "schema_version")
      if schema_version != SCHEMA_VERSION
        raise ArgumentError, "unsupported generation profile schema #{schema_version.inspect}"
      end
      algorithms = normalize_algorithms(value_for(manifest, "algorithms"))
      components = normalize_components(value_for(manifest, "components"))
      return {
        "schema_version" => SCHEMA_VERSION,
        "algorithms" => algorithms,
        "components" => components
      }
    end

    def self.normalize_algorithms(algorithms)
      raise ArgumentError, "generation profile algorithms must be an array" if !algorithms.is_a?(Array)
      result = algorithms.map do |entry|
        raise ArgumentError, "generation algorithm must be a hash" if !entry.is_a?(Hash)
        name = value_for(entry, "name").to_s
        version = value_for(entry, "version")
        raise ArgumentError, "invalid generation algorithm name #{name.inspect}" if !NAME_PATTERN.match?(name)
        raise ArgumentError, "invalid generation algorithm version for #{name}" if !version.is_a?(Integer) || version <= 0
        { "name" => name, "version" => version }
      end.sort_by { |entry| entry["name"] }
      names = result.map { |entry| entry["name"] }
      raise ArgumentError, "generation algorithm names must be unique" if names.uniq.length != names.length
      if names != ALGORITHM_FAMILIES
        raise ArgumentError, "generation profile does not contain the required algorithm families"
      end
      return result
    end

    def self.normalize_components(components)
      raise ArgumentError, "generation profile components must be a non-empty array" if !components.is_a?(Array) || components.empty?
      result = components.map do |entry|
        raise ArgumentError, "generation component must be a hash" if !entry.is_a?(Hash)
        name = value_for(entry, "name").to_s
        schema_version = value_for(entry, "schema_version")
        sha256 = value_for(entry, "sha256").to_s
        byte_length = value_for(entry, "byte_length")
        raise ArgumentError, "invalid generation component name #{name.inspect}" if !NAME_PATTERN.match?(name)
        raise ArgumentError, "invalid component schema for #{name}" if !schema_version.is_a?(Integer) || schema_version <= 0
        raise ArgumentError, "invalid component digest for #{name}" if !SHA256_PATTERN.match?(sha256)
        raise ArgumentError, "invalid component byte length for #{name}" if !byte_length.is_a?(Integer) || byte_length <= 0
        {
          "name" => name,
          "schema_version" => schema_version,
          "sha256" => sha256,
          "byte_length" => byte_length
        }
      end.sort_by { |entry| entry["name"] }
      names = result.map { |entry| entry["name"] }
      raise ArgumentError, "generation component names must be unique" if names.uniq.length != names.length
      return result
    end

    def self.canonical_value(value)
      case value
      when Hash
        result = {}
        value.keys.sort_by(&:to_s).each do |original_key|
          key = original_key.to_s
          if result.key?(key)
            raise ArgumentError, "canonical JSON object keys must be unique after string conversion"
          end
          result[key] = canonical_value(value[original_key])
        end
        return result
      when Array
        return value.map { |entry| canonical_value(entry) }
      when Symbol
        return value.to_s
      when String, Integer, Float, TrueClass, FalseClass, NilClass
        return value
      end
      raise ArgumentError, "unsupported canonical JSON value #{value.class}"
    end

    def self.generate_json(value)
      case value
      when Hash
        entries = value.map do |key, child|
          "#{json_string(key)}:#{generate_json(child)}"
        end
        return "{#{entries.join(",")}}"
      when Array
        return "[#{value.map { |child| generate_json(child) }.join(",")}]"
      when String
        return json_string(value)
      when TrueClass, FalseClass
        return value.to_s
      when NilClass
        return "null"
      when Numeric
        raise ArgumentError, "non-finite numbers are not valid JSON" if value.respond_to?(:finite?) && !value.finite?
        return value.to_s
      end
      raise ArgumentError, "unsupported canonical JSON value #{value.class}"
    end

    def self.json_string(value)
      escaped = value.to_s.each_codepoint.map do |codepoint|
        case codepoint
        when 0x08 then "\\b"
        when 0x09 then "\\t"
        when 0x0A then "\\n"
        when 0x0C then "\\f"
        when 0x0D then "\\r"
        when 0x22 then '\\"'
        when 0x5C then "\\\\"
        else
          codepoint < 0x20 ? sprintf("\\u%04x", codepoint) :
            codepoint.chr(Encoding::UTF_8)
        end
      end.join
      return "\"#{escaped}\""
    end

    def self.value_for(hash, key)
      return hash[key] if hash.key?(key)
      symbol = key.to_sym
      return hash[symbol] if hash.key?(symbol)
      return nil
    end
  end
end
