module Ironmon
  class GenerationProfileUnavailable < StandardError; end

  GENERATION_PROFILE_PATH = File.join(
    "Data", "Ironmon", "generation_profile.json"
  )
  GENERATION_PROFILE_COMPONENT_PATHS = {
    "area_catalog" => File.join("Data", "Ironmon", "area_catalog.dat"),
    "base_catalog" => File.join(
      "Data", "Ironmon", "generation_base_catalog.json"
    ),
    "custom_fusion_pool" => File.join(
      "Data", "Ironmon", "generation_custom_fusion_pool.bin"
    ),
    "custom_sprites" => File.join("Data", "Ironmon", "generation_custom_sprites.json"),
    "obtainability_sources" => File.join(
      "Data", "Ironmon", "obtainability_source_catalog.json"
    )
  }.freeze
  GENERATION_PROFILE_STORE_PATH = File.join(
    "Data", "Ironmon", "generation_profiles"
  )
  GENERATION_PROFILE_MANIFEST_FILENAME = "generation_profile.json"
  GENERATION_PROFILE_COMPONENT_FILENAMES = {
    "area_catalog" => "area_catalog.dat",
    "base_catalog" => "generation_base_catalog.json",
    "custom_fusion_pool" => "generation_custom_fusion_pool.bin",
    "custom_sprites" => "generation_custom_sprites.json",
    "obtainability_sources" => "obtainability_source_catalog.json"
  }.freeze

  def self.current_generation_profile
    return @current_generation_profile if @current_generation_profile
    @current_generation_profile = load_generation_profile(
      GENERATION_PROFILE_PATH,
      File.dirname(GENERATION_PROFILE_PATH),
      "installed"
    )
    return @current_generation_profile
  rescue GenerationProfileUnavailable
    raise
  rescue Exception => exception
    raise GenerationProfileUnavailable,
      "the installed generation profile is unavailable: #{exception.message}"
  end

  def self.generation_profile(profile_id)
    profile_id = validate_generation_profile_id(profile_id)
    current = current_generation_profile
    return current if current["profile_id"] == profile_id
    @generation_profiles ||= {}
    return @generation_profiles[profile_id] if @generation_profiles[profile_id]
    root = generation_profile_store_directory(profile_id)
    manifest_path = File.join(root, GENERATION_PROFILE_MANIFEST_FILENAME)
    profile = load_generation_profile(manifest_path, root, "archived")
    if profile["profile_id"] != profile_id
      raise GenerationProfileUnavailable,
        "the archived generation profile identity does not match its directory"
    end
    @generation_profiles[profile_id] = profile
    return profile
  rescue GenerationProfileUnavailable
    raise
  rescue Exception => exception
    raise GenerationProfileUnavailable,
      "generation profile #{profile_id} is unavailable: #{exception.message}"
  end

  def self.load_generation_profile(manifest_path, root, label)
    document = JSON.parse(File.binread(manifest_path))
    manifest = GenerationProfile.normalize(
      GenerationProfile.value_for(document, "manifest")
    )
    profile_id = GenerationProfile.value_for(document, "profile_id").to_s
    if !GenerationProfile::SHA256_PATTERN.match?(profile_id) ||
       GenerationProfile.fingerprint(manifest) != profile_id
      raise GenerationProfileUnavailable,
        "the #{label} generation profile identity is invalid"
    end
    validate_generation_profile_components(manifest, root, label)
    return {
      "profile_id" => profile_id,
      "manifest" => manifest,
      "root" => root
    }.freeze
  end

  def self.current_generation_profile_id
    return current_generation_profile["profile_id"]
  end

  def self.validate_generation_profile_components(manifest, root, label)
    components = manifest["components"]
    names = components.map { |component| component["name"] }
    expected_names = GENERATION_PROFILE_COMPONENT_PATHS.keys.sort
    eligibility = manifest["algorithms"].find do |entry|
      entry["name"] == "custom_fusion_eligibility"
    end
    if ![1, 2].include?(eligibility["version"])
      raise GenerationProfileUnavailable, "the custom sprite eligibility version is unsupported"
    end
    expected_names -= ["custom_sprites"] if eligibility["version"] < 2
    if names != expected_names
      raise GenerationProfileUnavailable,
        "the #{label} generation profile component set is incomplete"
    end
    components.each do |component|
      name = component["name"]
      path = File.join(root, GENERATION_PROFILE_COMPONENT_FILENAMES[name])
      bytes = File.binread(path)
      if bytes.bytesize != component["byte_length"] ||
         Digest::SHA256.hexdigest(bytes) != component["sha256"]
        raise GenerationProfileUnavailable,
          "the #{label} generation profile component #{name} is invalid"
      end
    end
    return true
  end

  def self.pinned_generation_profile_id
    attempt = current_run_attempt if respond_to?(:current_run_attempt)
    profile_id = if attempt
                   attempt["generation_profile_id"]
                 elsif $PokemonGlobal
                   $PokemonGlobal.ironmon_generation_profile_id
                 end
    profile_id = profile_id.to_s
    if !GenerationProfile::SHA256_PATTERN.match?(profile_id)
      raise GenerationProfileUnavailable,
        "this run does not identify an immutable generation profile"
    end
    return profile_id
  end

  def self.pin_current_generation_profile
    raise GenerationProfileUnavailable, "run metadata is unavailable" if
      !$PokemonGlobal
    profile_id = current_generation_profile_id
    persist_current_generation_profile
    $PokemonGlobal.ironmon_generation_profile_id = profile_id
    return profile_id
  end

  def self.saved_active_run_generation_profile_id(save_data)
    return nil if !save_data.is_a?(Hash)
    global = save_data[:global_metadata] || save_data["global_metadata"]
    return nil if !global || global.ironmon_mode != true
    ledger = global.ironmon_run_ledger
    return nil if !ledger.is_a?(Hash)
    attempt = ledger["current_attempt"] || ledger[:current_attempt]
    return nil if !attempt.is_a?(Hash)
    result = attempt["result"] || attempt[:result]
    return nil if result.to_s != "active"
    profile_id = attempt["generation_profile_id"] ||
                 attempt[:generation_profile_id]
    return validate_generation_profile_id(profile_id)
  end

  def self.run_generation_profile_id
    return @run_generation_profile_id
  end

  def self.activate_run_generation_profile(profile_id)
    profile_id = validate_generation_profile_id(profile_id)
    generation_profile(profile_id)
    return true if @run_generation_profile_id == profile_id
    if @active_generation_profile_id
      raise GenerationProfileUnavailable,
        "the run generation profile cannot change during an archived request"
    end
    deactivate_run_generation_profile
    @run_generation_profile_id = profile_id
    if profile_id != current_generation_profile_id
      @run_generation_profile_data_snapshot =
        activate_generation_profile_data(profile_id)
    end
    return true
  rescue Exception
    if @run_generation_profile_id == profile_id
      restore_generation_profile_data(@run_generation_profile_data_snapshot)
      @run_generation_profile_data_snapshot = nil
      @run_generation_profile_id = nil
    end
    raise
  end

  def self.deactivate_run_generation_profile
    if @active_generation_profile_id
      raise GenerationProfileUnavailable,
        "the run generation profile cannot be restored during an archived request"
    end
    restore_generation_profile_data(@run_generation_profile_data_snapshot)
    @run_generation_profile_data_snapshot = nil
    @run_generation_profile_id = nil
    return true
  end

  def self.prepare_generation_profile_load(save_data)
    deactivate_run_generation_profile
    return nil if checkpoint_reset_loading?
    profile_id = saved_active_run_generation_profile_id(save_data)
    activate_run_generation_profile(profile_id) if profile_id
    return profile_id
  end

  def self.finish_generation_profile_load(profile_id)
    return true if checkpoint_reset_loading?
    attempt = current_run_attempt if respond_to?(:current_run_attempt)
    active = attempt.is_a?(Hash) && attempt["result"].to_s == "active"
    if !active
      deactivate_run_generation_profile
      return true
    end
    pinned = pinned_generation_profile_id
    if profile_id != pinned || run_generation_profile_id != pinned
      raise GenerationProfileUnavailable,
        "the loaded run generation profile was not activated"
    end
    return true
  end

  def self.persist_current_generation_profile
    profile = current_generation_profile
    profile_id = profile["profile_id"]
    root = generation_profile_store_directory(profile_id)
    begin
      manifest_path = File.join(root, GENERATION_PROFILE_MANIFEST_FILENAME)
      stored = load_generation_profile(manifest_path, root, "archived")
      return stored if stored["profile_id"] == profile_id
    rescue GenerationProfileUnavailable
    end
    require "fileutils"
    FileUtils.mkdir_p(root)
    profile["manifest"]["components"].each do |component|
      name = component["name"]
      filename = GENERATION_PROFILE_COMPONENT_FILENAMES[name]
      source = GENERATION_PROFILE_COMPONENT_PATHS[name]
      generation_profile_atomic_write(File.join(root, filename), File.binread(source))
    end
    generation_profile_atomic_write(
      File.join(root, GENERATION_PROFILE_MANIFEST_FILENAME),
      File.binread(GENERATION_PROFILE_PATH)
    )
    @generation_profiles ||= {}
    @generation_profiles.delete(profile_id)
    return generation_profile(profile_id)
  end

  def self.generation_profile_component_path(name, profile_id = nil)
    filename = GENERATION_PROFILE_COMPONENT_FILENAMES[name.to_s]
    raise ArgumentError, "unknown generation profile component #{name}" if !filename
    profile_id ||= active_generation_profile_id
    profile = generation_profile(profile_id)
    return File.join(profile["root"], filename)
  end

  def self.active_generation_profile_id
    return @active_generation_profile_id || @run_generation_profile_id ||
      current_generation_profile_id
  end

  def self.generation_profile_algorithm_version(name, profile_id = nil)
    profile_id ||= active_generation_profile_id
    profile = generation_profile(profile_id)
    algorithm = profile["manifest"]["algorithms"].find do |entry|
      entry["name"] == name.to_s
    end
    if !algorithm
      raise GenerationProfileUnavailable,
        "generation profile #{profile_id} does not define algorithm #{name}"
    end
    return algorithm["version"]
  end

  def self.generation_profile_context?(profile_id = nil)
    selected = @active_generation_profile_id || @run_generation_profile_id
    return false if !selected
    return true if !profile_id
    return selected == profile_id.to_s
  end

  def self.with_generation_profile(profile_id)
    profile_id = validate_generation_profile_id(profile_id)
    generation_profile(profile_id)
    previous = @active_generation_profile_id
    if previous && previous != profile_id
      raise GenerationProfileUnavailable,
        "generation profile contexts cannot overlap"
    end
    return yield if previous == profile_id
    @active_generation_profile_id = profile_id
    baseline = @run_generation_profile_id || current_generation_profile_id
    data_snapshot = activate_generation_profile_data(profile_id) if
      profile_id != baseline
    return yield
  ensure
    restore_generation_profile_data(data_snapshot) if data_snapshot
    @active_generation_profile_id = previous
  end

  def self.generation_profile_store_directory(profile_id)
    profile_id = validate_generation_profile_id(profile_id)
    return File.join(GENERATION_PROFILE_STORE_PATH, profile_id)
  end

  def self.validate_generation_profile_id(profile_id)
    value = profile_id.to_s
    if !GenerationProfile::SHA256_PATTERN.match?(value)
      raise GenerationProfileUnavailable,
        "the generation profile identity is invalid"
    end
    return value
  end

  def self.generation_profile_atomic_write(path, bytes)
    temporary_path = "#{path}.tmp-#{Process.pid}-#{rand(1 << 30)}"
    File.binwrite(temporary_path, bytes)
    File.delete(path) if File.file?(path)
    File.rename(temporary_path, path)
  ensure
    File.delete(temporary_path) if temporary_path && File.file?(temporary_path)
  end

  def self.reset_current_generation_profile_cache
    ValidatedCustomSprites.reset
    @current_generation_profile = nil
    @generation_profiles = nil
    @tracker_area_catalog_documents = nil
    @tracker_obtainability_source_catalogs = nil
    @tracker_area_event_entry_indexes = nil
    @tracker_area_hidden_item_indexes = nil
    @profile_custom_fusion_pool_services = nil
    @generation_profile_base_catalogs = nil
    @generation_profile_game_data = nil
  end
end
