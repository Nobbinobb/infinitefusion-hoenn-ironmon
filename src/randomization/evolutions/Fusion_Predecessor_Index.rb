#===============================================================================
# Release-stable complete-fusion predecessor candidates
#===============================================================================

module Ironmon
  FUSION_PREDECESSOR_INDEX_PATH = File.join(
    "Data", "Ironmon", "fusion_predecessor_index.dat"
  )

  class FusionPredecessorIndexError < StandardError
  end

  class FusionPredecessorIndex
    SCHEMA_VERSION = 2

    def initialize(document, catalog, fusion_pool, fusion_pool_info)
      @document = document
      validate(catalog, fusion_pool, fusion_pool_info)
      @source_ids_by_signature = {}
    end

    def source_ids_for(bucket, types)
      result = []
      seen = {}
      types.each do |type|
        source_ids_for_signature(bucket, type).each do |source_id|
          next if seen[source_id]
          seen[source_id] = true
          result << source_id
        end
      end
      result.sort!
      return result.freeze
    end

    def membership_count
      return @document["membership_count"]
    end

    private

    def source_ids_for_signature(bucket, type)
      key = "#{bucket}|#{type}"
      cached = @source_ids_by_signature[key]
      return cached if cached
      packed = @document["signatures"][key]
      source_ids = packed ? packed.unpack("L<*") : []
      @source_ids_by_signature[key] = source_ids.freeze
      return @source_ids_by_signature[key]
    end

    def validate(catalog, fusion_pool, fusion_pool_info)
      if !@document.is_a?(Hash) ||
         @document["schema_version"] != SCHEMA_VERSION ||
         !@document["signatures"].is_a?(Hash)
        raise FusionPredecessorIndexError,
              "unsupported fusion predecessor index schema"
      end
      expected = {
        "normal_species_count" => NB_POKEMON,
        "source_fingerprint" => catalog.source_fingerprint,
        "taxonomy_fingerprint" => catalog.taxonomy_fingerprint,
        "method_fingerprint" => catalog.method_fingerprint,
        "fusion_pool_schema_version" => fusion_pool_info[:schema_version],
        "fusion_pool_size" => fusion_pool_info[:size],
        "fusion_pool_fingerprint" => fusion_pool_info[:fingerprint]
      }
      expected.each do |key, value|
        next if @document[key] == value
        raise FusionPredecessorIndexError,
              "fusion predecessor index #{key} does not match runtime data"
      end
      valid_source_ids = {}
      fusion_pool.each do |species_id|
        match = /\AB(\d+)H(\d+)\z/.match(species_id.to_s)
        if !match
          raise FusionPredecessorIndexError,
                "fusion predecessor index received an invalid fusion pool"
        end
        source_id = (match[1].to_i * NB_POKEMON) + match[2].to_i
        valid_source_ids[source_id] = true
      end
      membership_count = @document["signatures"].values.inject(0) do |sum, packed|
        if !packed.is_a?(String) || packed.bytesize % 4 != 0
          raise FusionPredecessorIndexError,
                "fusion predecessor index contains malformed source data"
        end
        previous_source_id = 0
        packed.unpack("L<*").each do |source_id|
          if source_id <= previous_source_id || !valid_source_ids[source_id]
            raise FusionPredecessorIndexError,
                  "fusion predecessor index contains a non-pool source"
          end
          previous_source_id = source_id
        end
        sum + (packed.bytesize / 4)
      end
      if membership_count != @document["membership_count"]
        raise FusionPredecessorIndexError,
              "fusion predecessor index membership count does not match"
      end
    end
  end

  def self.fusion_predecessor_index
    return @fusion_predecessor_index if @fusion_predecessor_index
    document = File.open(FUSION_PREDECESSOR_INDEX_PATH, "rb") do |file|
      Marshal.load(file)
    end
    @fusion_predecessor_index = FusionPredecessorIndex.new(
      document, evolution_catalog, custom_fusion_pool,
      custom_fusion_pool_info
    )
    return @fusion_predecessor_index
  rescue FusionPredecessorIndexError
    raise
  rescue Exception => exception
    raise FusionPredecessorIndexError,
          "The bundled fusion predecessor index is unavailable: #{exception.message}"
  end

  def self.reset_fusion_predecessor_index_cache
    @fusion_predecessor_index = nil
  end
end
