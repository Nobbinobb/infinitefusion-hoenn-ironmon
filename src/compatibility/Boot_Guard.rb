# Loaded from Data/Scripts before the upstream folder loader enters 997_Ironmon.
# This file must remain independent of every Ironmon gameplay hook.
module IronmonBootGuard
  INVENTORY = "Data/Ironmon/game-compatibility.json".freeze
  ACTIVE = ".ironmon-update/active.json".freeze
  MESSAGE = "Ironmon cannot start with this game installation. Finish or recover the update in the tracker, or install a matching Ironmon release. Your save has not been changed.".freeze

  class IncompatibleSave < StandardError; end

  def self.plain_path(root, relative)
    parts = relative.split("/")
    raise "Invalid compatibility path" if parts.empty? || parts.any? { |part| part.empty? || part == "." || part == ".." || part.include?("\\") || part.include?(":") }
    path = root
    parts.each do |part|
      path = File.join(path, part)
      raise "Linked compatibility path" if File.symlink?(path)
    end
    path
  end

  def self.head(root)
    metadata = plain_path(root, ".git")
    return nil unless File.exist?(metadata)
    raise "Unsupported Git layout" unless File.directory?(metadata)
    value = File.read(plain_path(root, ".git/HEAD"), 1024).strip
    return value if value.match?(/\A[0-9a-f]{40}\z/)
    raise "Invalid Git HEAD" unless value.start_with?("ref: refs/heads/")
    reference = value.delete_prefix("ref: ")
    loose = plain_path(root, ".git/#{reference}")
    return File.read(loose, 1024).strip if File.file?(loose)
    packed = plain_path(root, ".git/packed-refs")
    raise "Missing Git reference" unless File.file?(packed) && File.size(packed) <= 8 * 1024 * 1024
    File.foreach(packed) do |line|
      commit, name = line.strip.split(" ", 2)
      return commit if name == reference
    end
    raise "Missing Git reference"
  end

  def self.check(root)
    require "digest/sha2"
    return false if File.exist?(plain_path(root, ACTIVE))
    path = plain_path(root, INVENTORY)
    return false unless File.file?(path) && File.size(path) <= 16 * 1024 * 1024
    inventory = stringify(HTTPLite::JSON.parse(File.binread(path).force_encoding(Encoding::UTF_8)))
    commit = inventory.fetch("Commit")
    return false unless commit.match?(/\A[0-9a-f]{40}\z/)
    installed = head(root)
    return false if installed && installed != commit
    files = inventory.fetch("Files")
    return false unless files.is_a?(Array) && !files.empty?
    files.each do |entry|
      file = plain_path(root, entry.fetch("Path"))
      return false unless File.file?(file)
      fingerprint = { "Length" => File.size(file), "Sha256" => digest_file(file) }
      return false unless fingerprint == entry["Canonical"] || fingerprint == entry["WindowsText"]
    end
    !File.exist?(plain_path(root, ACTIVE)) && head(root) == installed
  rescue StandardError, LoadError
    false
  end

  def self.digest_file(path)
    digest = Digest::SHA256.new
    File.open(path, "rb") do |file|
      while (chunk = file.read(1024 * 1024))
        digest.update(chunk)
      end
    end
    digest.hexdigest.upcase
  end

  def self.stringify(value)
    return value.each_with_object({}) { |(key, item), result| result[key.to_s] = stringify(item) } if value.is_a?(Hash)
    return value.map { |item| stringify(item) } if value.is_a?(Array)
    value
  end

  def self.ironmon_save?(data)
    values = data.is_a?(Hash) ? [data[:global_metadata]] : Array(data)
    values.any? { |value| value && value.instance_variable_get(:@ironmon_mode) }
  end

  # Intercept raw reads before the upstream reader can convert and resave them.
  module SaveReads
    def get_data_from_file(path)
      data = super
      raise IncompatibleSave, MESSAGE if IronmonBootGuard.ironmon_save?(data)
      data
    end
  end

  module GameLoads
    def load(data)
      raise IncompatibleSave, MESSAGE if IronmonBootGuard.ironmon_save?(data)
      super
    end
  end

  def self.block
    # Only inert Marshal identities are needed to recognize existing saves.
    Object.const_set(:Ironmon, Module.new) unless defined?(Ironmon)
    [:Configuration, :PivotState].each do |name|
      Ironmon.const_set(name, Class.new) unless Ironmon.const_defined?(name, false)
    end
    Ironmon.const_set(:AreaEncounterEntry, Class.new(Array)) unless Ironmon.const_defined?(:AreaEncounterEntry, false)
    Ironmon.const_set(:Cosmetics, Module.new) unless Ironmon.const_defined?(:Cosmetics, false)
    Ironmon::Cosmetics.const_set(:Profile, Class.new) unless Ironmon::Cosmetics.const_defined?(:Profile, false)
    SaveData.singleton_class.prepend(SaveReads) unless SaveData.singleton_class.ancestors.include?(SaveReads)
    Game.singleton_class.prepend(GameLoads) unless Game.singleton_class.ancestors.include?(GameLoads)
  end

  def self.allow_folder?(path)
    return true unless File.basename(path).casecmp?("997_Ironmon")
    return true if check(Dir.pwd)
    block
    false
  end
end

if Object.private_method_defined?(:load_scripts_from_folder) || Object.method_defined?(:load_scripts_from_folder)
  alias ironmon_original_load_scripts_from_folder load_scripts_from_folder
  def load_scripts_from_folder(path)
    return unless IronmonBootGuard.allow_folder?(path)
    ironmon_original_load_scripts_from_folder(path)
  end
end
