module IronmonScriptLoader
  IGNORED_ENTRIES = [".", "..", ".git", ".idea", ".gitignore"].freeze

  def self.load_directory(path, excluded_root_patterns = [], root = true)
    entries = Dir.entries(path) - IGNORED_ENTRIES
    files, folders = entries.partition do |entry|
      !File.directory?(File.join(path, entry))
    end
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
end
