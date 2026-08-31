module Ironmon
  # Release-generated audit data is informational. Its game version is
  # provenance, never an availability gate after a base-game update.
  class DefenseCatalog
    PATH = File.join("Data", "Ironmon", "defense_presentation.json").freeze
    SCHEMA_VERSION = 1

    def self.load
      return @data ||= decode(File.binread(PATH))
    end

    def self.decode(json)
      document = symbolize(HTTPLite::JSON.parse(json.dup.force_encoding(Encoding::UTF_8)))
      raise "Unsupported defense catalog schema" if document[:schema_version] != SCHEMA_VERSION
      return freeze_tree(document)
    end

    def self.symbolize(value)
      return value.to_i if value.is_a?(Float) && value.finite? && value == value.to_i
      return value.map { |entry| symbolize(entry) } if value.is_a?(Array)
      return value unless value.is_a?(Hash)
      return value.each_with_object({}) { |(key, entry), result| result[key.to_sym] = symbolize(entry) }
    end

    def self.freeze_tree(value)
      value.each_value { |entry| freeze_tree(entry) } if value.is_a?(Hash)
      value.each { |entry| freeze_tree(entry) } if value.is_a?(Array)
      return value.freeze
    end
  end
end
