module IronmonGenerationProfileExporter
  OUTPUT_PATH = $ironmon_generation_profile_output_path.to_s
  DESCRIPTOR_PATHS = $ironmon_generation_profile_descriptor_paths
  REQUIRED_COMPONENTS = [
    "area_catalog",
    "base_catalog",
    "custom_fusion_pool",
    "obtainability_sources"
  ].freeze

  def self.run
    if !DESCRIPTOR_PATHS.is_a?(Array) || DESCRIPTOR_PATHS.empty?
      raise "generation profile component descriptors are unavailable"
    end
    components = DESCRIPTOR_PATHS.map do |path|
      JSON.parse(File.binread(path.to_s))
    end
    manifest = Ironmon::GenerationProfile.build(
      Ironmon::GenerationProfile::CURRENT_ALGORITHM_VERSIONS,
      components
    )
    component_names = manifest["components"].map do |component|
      component["name"]
    end
    if component_names != REQUIRED_COMPONENTS
      raise "generation profile does not contain the required source components"
    end
    document = {
      "profile_id" => Ironmon::GenerationProfile.fingerprint(manifest),
      "manifest" => manifest
    }
    temporary_path = "#{OUTPUT_PATH}.tmp"
    File.binwrite(
      temporary_path,
      Ironmon::GenerationProfile.canonical_json(document) + "\n"
    )
    File.delete(OUTPUT_PATH) if File.file?(OUTPUT_PATH)
    File.rename(temporary_path, OUTPUT_PATH)
  end
end

IronmonGenerationProfileExporter.run
