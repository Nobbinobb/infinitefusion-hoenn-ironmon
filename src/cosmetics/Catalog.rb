#===============================================================================
# Optional, installation-derived cosmetic catalog and stable point prices
#===============================================================================
require "digest/sha2"

module Ironmon
  module Cosmetics
    SCHEMA_VERSION = 1
    RECOVERABLE_ERRORS = [StandardError]
    RECOVERABLE_ERRORS << MKXPError if defined?(MKXPError)
    RECOVERABLE_ERRORS.freeze
    PRICE_BANDS = [[500, 20], [1500, 40], [4000, 80], [10000, 150],
                   [25000, 250], [50000, 400]].freeze
    CATEGORY_FILES = { "clothes" => "clothes_data.json",
                       "hat" => "hats_data.json",
                       "hair" => "hairstyles_data.json" }.freeze
    CLOTHES_ACTIONS = ["walk", "run", "bike", "surf", "dive", "fish", "trainer"].freeze
    APPEARANCE_FIELDS = ["clothes", "hair", "hat", "hat2", "skin_tone",
                         "clothes_color", "hair_color", "hat_color",
                         "hat2_color", "bike_color"].freeze
    DYE_FIELDS = ["clothes_color", "hair_color", "hat_color", "hat2_color", "bike_color"].freeze

    def self.default_outfit_keys
      return [GENDER_MALE, GENDER_FEMALE].map { |gender| "clothes:#{getDefaultClothes(gender)}" }.uniq
    end

    def self.encode(value)
      return "{" + value.map { |key, child| "#{encode(key.to_s)}:#{encode(child)}" }.join(",") + "}" if value.is_a?(Hash)
      return "[" + value.map { |child| encode(child) }.join(",") + "]" if value.is_a?(Array)
      return HTTPLite::JSON.stringify(value) if value.is_a?(String)
      return value.to_s if value.is_a?(Numeric) || value == true || value == false
      return "null" if value.nil?
      raise "Unsupported cosmetic data #{value.class}"
    end

    def self.decode(text)
      return normalize_numbers(HTTPLite::JSON.parse(text))
    rescue *RECOVERABLE_ERRORS => error
      raise "Invalid cosmetic JSON: #{error.message}"
    end

    def self.normalize_numbers(value)
      return value.to_i if value.is_a?(Float) && value.finite? && value == value.to_i
      return value.map { |child| normalize_numbers(child) } if value.is_a?(Array)
      return value.transform_values { |child| normalize_numbers(child) } if value.is_a?(Hash)
      return value
    end

    def self.points_for(category, price)
      number = Float(price) rescue 0
      return category == "hair" ? 20 : 80 if !number.finite? || number <= 0
      band = PRICE_BANDS.find { |maximum, _points| number <= maximum }
      return band ? band[1] : 600
    end

    class Catalog
      attr_reader :entries, :warnings, :source_hashes

      def initialize(root = ".")
        @root = root
        @entries = {}
        @warnings = []
        @source_hashes = {}
        CATEGORY_FILES.each { |category, filename| read_category(category, filename) }
      end

      def available(category)
        return @entries.values.select { |entry| entry["category"] == category && entry["available"] }
                       .sort_by { |entry| [entry["name"].downcase, entry["id"]] }
      end

      def find(category, id)
        return @entries["#{category}:#{id}"]
      end

      def valid?(category, id)
        entry = find(category, id)
        return entry && entry["available"]
      end

      def read_category(category, filename)
        path = File.join(@root, "Data", "outfits", filename)
        content = File.binread(path)
        @source_hashes[filename] = Digest::SHA256.hexdigest(content)
        rows = Cosmetics.decode(content.force_encoding(Encoding::UTF_8))
        raise "expected an array" if !rows.is_a?(Array)
        seen = {}
        invalid_rows = []
        rows.each_with_index do |row, index|
          id = row.is_a?(Hash) ? row["id"] : nil
          if !id.is_a?(String) || !id.match?(/\A[A-Za-z0-9_-]+\z/)
            invalid_rows << index + 1
            next
          end
          if seen[id]
            @entries["#{category}:#{id}"]["available"] = false
            @entries["#{category}:#{id}"]["issues"] << "duplicate ID"
            @warnings << "#{category}:#{id}: duplicate ID"
            next
          end
          seen[id] = true
          entry = build_entry(category, row)
          @entries[entry["key"]] = entry
        end
        if !invalid_rows.empty?
          @warnings << "#{category}: #{invalid_rows.length} rows have missing or unsafe IDs (rows #{invalid_rows.first}-#{invalid_rows.last})"
        end
        folder = File.join(@root, "Graphics", "Characters", "player", category)
        if File.directory?(folder)
          Dir.children(folder).sort.each do |id|
            next if !File.directory?(File.join(folder, id)) || seen[id]
            @warnings << "#{category}:#{id}: asset folder without catalog metadata"
          end
        end
      rescue StandardError => error
        @warnings << "#{filename}: #{error.message}"
      end

      def build_entry(category, row)
        id = row["id"]
        folder = File.join(@root, "Graphics", "Characters", "player", category, id)
        issues = []
        variants = []
        files = []
        if category == "hair"
          if File.directory?(folder)
            pattern = /\Ahair_(\d+)_#{Regexp.escape(id)}\.png\z/
            Dir.children(folder).sort.each do |name|
              match = pattern.match(name)
              next if !match
              version = match[1].to_i
              trainer_name = "hair_trainer_#{version}_#{id}.png"
              if File.file?(File.join(folder, trainer_name))
                variants << version
                files.concat([name, trainer_name])
              else
                issues << "missing #{trainer_name}"
              end
            end
          end
          issues << "no complete hair variants" if variants.empty?
        else
          files = if category == "clothes"
                    CLOTHES_ACTIONS.map { |action| "clothes_#{action}_#{id}.png" }
                  else
                    ["hat_#{id}.png", "hat_trainer_#{id}.png"]
                  end
          files.each { |name| issues << "missing #{name}" if !File.file?(File.join(folder, name)) }
        end
        price = Float(row["price"]) rescue 0
        fallback = !price.finite? || price <= 0
        return {
          "key" => "#{category}:#{id}", "category" => category, "id" => id,
          "name" => row["name"].to_s.empty? ? id : row["name"].to_s,
          "description" => row["description"].to_s,
          "base_price" => fallback ? nil : price.to_i,
          "points" => Cosmetics.points_for(category, row["price"]),
          "fallback_price" => fallback,
          "acquisition_hoenn" => row["obtainmethodhoenn"].to_s,
          "acquisition_kanto" => row["obtainmethodkanto"].to_s,
          "tags" => row["tags"].to_s, "author" => row["author"].to_s,
          "variants" => variants.uniq.sort,
          "available" => category == "hair" ? !variants.empty? : issues.empty?,
          "issues" => issues, "assets" => files
        }
      end
    end
  end
end
