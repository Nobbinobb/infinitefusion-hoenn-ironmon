module IronmonScriptLoader
  IGNORED_ENTRIES = [
    ".", "..", ".DS_Store", ".git", ".idea", ".gitignore"
  ].freeze

  def self.configure_game_id
    return if defined?(Settings) && Settings.const_defined?(:GAME_ID, false)
    title_line = File.open("Game.ini", "rb") do |file|
      file.each_line.find { |line| line.start_with?("Title=") }
    end
    title = title_line && title_line.split("=", 2)[1].to_s.strip.downcase
    game_id = case title
              when "infinitefusion-hoenn" then :IF_HOENN
              when "infinitefusion" then :IF_KANTO
              else
                raise "unsupported Infinite Fusion game title: #{title.inspect}"
              end
    Object.const_set(:Settings, Module.new) unless defined?(Settings)
    Settings.const_set(:GAME_ID, game_id)
  end

  def self.load_directory(path, excluded_root_patterns = [], root = true)
    entries = Dir.entries(path) - IGNORED_ENTRIES
    files, folders = entries.partition do |entry|
      !File.directory?(File.join(path, entry))
    end
    files.select! { |file_name| File.extname(file_name).downcase == ".rb" }
    files.sort.each do |file_name|
      path_name = File.join(path, file_name)
      code = File.open(path_name, "rb") { |file| file.read }
      eval(code, TOPLEVEL_BINDING, file_name)
    end
    folders.sort.each do |folder_name|
      next if root && excluded_root_patterns.any? do |pattern|
        folder_name.match?(pattern)
      end
      load_directory(File.join(path, folder_name), [], false)
    end
  end

  def self.load_manifest(source_root, manifest_path, initialize_catalogs = true)
    root = File.expand_path(source_root)
    root_prefix = root.end_with?(File::SEPARATOR) ? root :
      root + File::SEPARATOR
    document = JSON.parse(File.open(manifest_path, "rb") { |file| file.read })
    raise "the Ironmon Ruby source manifest is empty" if document.empty?
    document.each do |entry|
      relative_source = entry["source"] || entry[:source]
      output_name = entry["output"] || entry[:output]
      if !relative_source || !output_name
        raise "the Ironmon Ruby source manifest entry is incomplete"
      end
      source_path = File.expand_path(relative_source, root)
      if !source_path.start_with?(root_prefix) ||
         File.extname(source_path) != ".rb"
        raise "invalid Ironmon Ruby source path in the manifest"
      end
      next if !initialize_catalogs &&
        relative_source == "tracker/area_lookup/Initialize.rb"
      code = File.open(source_path, "rb") { |file| file.read }
      eval(code, TOPLEVEL_BINDING, output_name)
    end
  end
end

IronmonScriptLoader.configure_game_id
