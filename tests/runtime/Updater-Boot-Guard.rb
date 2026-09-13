require "digest/sha2"

module IronmonBootGuardRuntimeTests
  def self.assert(value, message)
    raise "Updater guard: #{message}" unless value
  end

  def self.mkdir(path)
    return if File.directory?(path)
    mkdir(File.dirname(path))
    Dir.mkdir(path)
  end

  def self.run(root)
    mkdir(File.join(root, "Data/Ironmon"))
    mkdir(File.join(root, "Data/Scripts"))
    source = "runtime fixture\n"
    code = File.join(root, "Data/Scripts/game.rb")
    File.binwrite(code, source)
    fingerprint = { "Length" => source.bytesize, "Sha256" => Digest::SHA256.hexdigest(source).upcase }
    inventory = { "Commit" => "1" * 40, "Files" => [{ "Path" => "Data/Scripts/game.rb", "Canonical" => fingerprint, "WindowsText" => nil }] }
    metadata = File.join(root, IronmonBootGuard::INVENTORY)
    File.write(metadata, HTTPLite::JSON.stringify(inventory))
    errors = []
    trace = TracePoint.new(:raise) { |event| errors << event.raised_exception.message }
    compatible = trace.enable { IronmonBootGuard.check(root) }
    assert(compatible, "complete ZIP accepts exact code: #{errors.inspect}")
    mkdir(File.join(root, ".git/refs/heads"))
    File.write(File.join(root, ".git/HEAD"), "ref: refs/heads/releases\n")
    reference = File.join(root, ".git/refs/heads/releases")
    File.write(reference, "1" * 40)
    assert(IronmonBootGuard.check(root), "ordinary Git accepts matching HEAD")
    File.write(reference, "2" * 40)
    assert(!IronmonBootGuard.check(root), "external launcher advance blocks Ironmon")
    File.write(reference, "1" * 40)
    File.delete(reference)
    File.write(File.join(root, ".git/packed-refs"), "#{'1' * 40} refs/heads/releases\n")
    assert(IronmonBootGuard.check(root), "packed Git references remain supported")
    File.binwrite(code, "changed code")
    assert(!IronmonBootGuard.check(root), "mixed program files block Ironmon")
    File.binwrite(code, source)
    mkdir(File.join(root, ".ironmon-update"))
    marker = File.join(root, IronmonBootGuard::ACTIVE)
    File.write(marker, "pending")
    assert(!IronmonBootGuard.check(root), "interrupted updates block Ironmon")
    Dir.chdir(root) { load_scripts_from_folder("Data/Scripts/997_Ironmon") }
    assert($ironmon_loader_calls.empty?, "the upstream loader never enters incompatible Ironmon hooks")
    Dir.chdir(root) { load_scripts_from_folder("Data/Scripts/003_Game processing") }
    assert($ironmon_loader_calls == ["Data/Scripts/003_Game processing"], "ordinary game folders retain upstream loading")
    File.delete(marker)
    Dir.chdir(root) { load_scripts_from_folder("Data/Scripts/997_Ironmon") }
    assert($ironmon_loader_calls.last == "Data/Scripts/997_Ironmon", "compatible Ironmon delegates to the upstream loader")
    File.delete(metadata)
    assert(!IronmonBootGuard.check(root), "missing compatibility data blocks Ironmon")

    IronmonBootGuard.block
    global = PokemonGlobalMetadata.allocate
    global.instance_variable_set(:@ironmon_mode, true)
    global.instance_variable_set(:@ironmon_configuration, Ironmon::Configuration.new)
    global.instance_variable_set(:@ironmon_pivot_state, Ironmon::PivotState.new)
    save = File.join(root, "guard-save.rxdata")
    original = Marshal.dump({ global_metadata: global })
    File.binwrite(save, original)
    begin
      SaveData.read_from_file(save)
      raise "An incompatible save reached conversion"
    rescue IronmonBootGuard::IncompatibleSave
    end
    assert(File.binread(save) == original, "blocked save remains byte-identical")
    assert(!File.exist?(save + ".bak"), "blocked save creates no backup or migration")
    begin
      Game.load({ global_metadata: global })
      raise "An incompatible save reached gameplay"
    rescue IronmonBootGuard::IncompatibleSave
    end
    ordinary = Marshal.dump({ global_metadata: PokemonGlobalMetadata.allocate })
    File.binwrite(save, ordinary)
    assert(SaveData.get_data_from_file(save).is_a?(Hash), "ordinary save reads remain available")
    assert(!IronmonBootGuard.ironmon_save?({ global_metadata: PokemonGlobalMetadata.allocate }), "ordinary play is not classified as Ironmon")
    assert(!Ironmon.respond_to?(:active?), "no incompatible gameplay hooks were loaded")
  end
end

IronmonBootGuardRuntimeTests.run($ironmon_guard_fixture)
