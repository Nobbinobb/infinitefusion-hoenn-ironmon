require "zlib"

module Ironmon
  module ValidatedCustomSprites
    SOURCE_PATH = $ironmon_source_root ? File.expand_path(
      "../resources/sprites/validated_custom_sprites.json.gz", $ironmon_source_root
    ) : ""
    SCHEMA_VERSION = 1
    SPRITE_PATTERN = /\A([1-9][0-9]*)\.([1-9][0-9]*)([a-zA-Z]*)\z/

    def self.source
      return @source if @source
      document = Zlib::GzipReader.open(SOURCE_PATH) do |reader|
        Ironmon.generation_profile_stringify_keys(JSON.parse(reader.read))
      end
      payload = document["catalogue"]
      if payload["schema_version"] != SCHEMA_VERSION ||
         payload["tile_size"] != 96 || payload["columns"] != 20 ||
         Digest::SHA256.hexdigest(GenerationProfile.canonical_json(payload)) != document["catalogue_id"]
        raise CustomFusionPoolError, "the validated sprite catalogue is invalid"
      end
      sprites = payload["sprites"]
      if !sprites.is_a?(Array) || sprites.empty? || sprites != sprites.uniq.sort ||
         sprites.any? { |name| !SPRITE_PATTERN.match?(name) }
        raise CustomFusionPoolError, "the validated sprite list is invalid"
      end
      @source = document.freeze
      return @source
    rescue CustomFusionPoolError
      raise
    rescue Exception => exception
      raise CustomFusionPoolError,
        "the validated sprite catalogue is unavailable: #{exception.message}"
    end

    def self.visible_index
      @visible_index ||= source["catalogue"]["sprites"].each_with_object({}) do |name, index|
        index[name] = true
      end.freeze
      return @visible_index
    end

    def self.profile_variants
      profile_id = Ironmon.active_generation_profile_id
      return nil if Ironmon.generation_profile_algorithm_version("custom_fusion_eligibility", profile_id) < 2
      @profile_variants ||= {}
      return @profile_variants[profile_id] if @profile_variants[profile_id]
      path = Ironmon.generation_profile_component_path("custom_sprites", profile_id)
      document = Ironmon.generation_profile_stringify_keys(JSON.parse(File.binread(path)))
      if document["schema_version"] != SCHEMA_VERSION ||
         !GenerationProfile::SHA256_PATTERN.match?(document["catalogue_id"].to_s)
        raise GenerationProfileUnavailable, "the pinned sprite catalogue is invalid"
      end
      variants = document["variants"]
      if !variants.is_a?(Hash) || variants.empty?
        raise GenerationProfileUnavailable, "the pinned sprite variants are invalid"
      end
      variants.each do |identity, letters|
        if !/\AB[1-9][0-9]*H[1-9][0-9]*\z/.match?(identity) ||
           !letters.is_a?(Hash) || letters.empty? ||
           letters.any? do |letter, status|
             !/\A[a-zA-Z]*\z/.match?(letter) || !["main", "temp"].include?(status)
           end
          raise GenerationProfileUnavailable, "the pinned sprite variants are invalid"
        end
        letters.freeze
      end
      @profile_variants[profile_id] = variants.freeze
      return variants
    end

    def self.allowed_statuses(head_id, body_id)
      return nil if !Ironmon.generation_profile_context? && !Ironmon.active?
      variants = profile_variants
      return nil if !variants
      return variants.fetch("B#{body_id}H#{head_id}", {})
    end

    def self.allowed_letters(head_id, body_id)
      statuses = allowed_statuses(head_id, body_id)
      return statuses && statuses.keys.sort
    end

    def self.reset
      @source = nil
      @visible_index = nil
      @profile_variants = nil
    end
  end
end
