# Standalone bundled-native compatibility and allocation benchmark. It never loads
# a save, starts a connection, or initializes the gameplay collection producer.
module IronmonTrackerJsonPerformance
  def self.assert(condition, message)
    raise "Tracker JSON performance validation failed: #{message}" if !condition
  end

  LEGACY_JSON_SOURCE = <<~'RUBY'
  def self.legacy_string(value)
    text = value.to_s.dup
    text.force_encoding(Encoding::UTF_8) if text.encoding == Encoding::ASCII_8BIT
    escaped = text.each_codepoint.map do |codepoint|
      case codepoint
      when 0x08 then "\\b"
      when 0x09 then "\\t"
      when 0x0A then "\\n"
      when 0x0C then "\\f"
      when 0x0D then "\\r"
      when 0x22 then '\\"'
      when 0x5C then "\\\\"
      else
        codepoint < 0x20 ? sprintf("\\u%04x", codepoint) : codepoint.chr(Encoding::UTF_8)
      end
    end.join
    return "\"#{escaped}\""
  end

  def self.legacy_generate(value)
    case value
    when Hash
      return "{#{value.map { |key, child| "#{legacy_string(key)}:#{legacy_generate(child)}" }.join(",")}}"
    when Array
      return "[#{value.map { |child| legacy_generate(child) }.join(",")}]"
    when String, Symbol
      return legacy_string(value)
    when TrueClass, FalseClass, Numeric
      return value.to_s
    when NilClass
      return "null"
    end
    raise "Unsupported tracker JSON value #{value.class}."
  end
  RUBY

  def self.configure_legacy(codec)
    source_encoding = codec.tracker_json_generate(nil).encoding
    module_eval(LEGACY_JSON_SOURCE.dup.force_encoding(source_encoding), __FILE__, 1)
  end

  def self.outcome(encoder, value)
    result = encoder.call(value)
    return [result.b, result.encoding.name]
  rescue Exception => error
    return [error.class.name, error.message]
  end

  def self.compatibility(codec = Ironmon)
    configure_legacy(codec)
    values = ["", "plain ASCII", "\"quoted\" \\ slash /", (0..31).to_a.pack("U*"),
      "Pokémon Flabébé ニドラン ♀ ♂ 🦊", [0x7f, 0x80, 0x85, 0x7ff, 0x800, 0xd7ff, 0xe000, 0xffff, 0x10000, 0x10ffff].pack("U*"),
      "\u2028\u2029", "frozen Pokémon".freeze, :SYMBOL, "Pokémon".b, "\xff".b,
      "\xc0\xaf".b, "\xed\xa0\x80".b, "\xf4\x90\x80\x80".b, "\xe2\x82".b,
      "\xff".b.force_encoding(Encoding::US_ASCII)]
    [Encoding::ISO_8859_1, Encoding::Windows_1252, Encoding::UTF_16LE, Encoding::UTF_16BE,
      Encoding::UTF_32LE, Encoding::UTF_32BE, Encoding::Shift_JIS].each do |encoding|
      values << "ASCII \t \" \\".encode(encoding)
    end
    values << "é".encode(Encoding::ISO_8859_1)
    values << "あ".encode(Encoding::Shift_JIS)
    values << "あ 🦊".encode(Encoding::UTF_16LE)
    values << "あ 🦊".encode(Encoding::UTF_32BE)
    [Encoding::UTF_16LE, Encoding::UTF_32BE, Encoding::Shift_JIS].each do |encoding|
      values << "\x82".b.force_encoding(encoding)
    end
    legacy = method(:legacy_string)
    optimized = codec.method(:tracker_json_string)
    values.each_with_index do |value, index|
      original = value.is_a?(String) ? [value.b, value.encoding.name] : nil
      expected = outcome(legacy, value)
      actual = outcome(optimized, value)
      assert(expected == actual, "matching bytes/encoding or exception for case #{index}: expected #{expected.inspect}, actual #{actual.inspect}")
      assert([value.b, value.encoding.name] == original, "input remains unchanged for case #{index}") if original
    end
    packet = { :same => 1, "same" => 2, "nested" => [true, false, nil, 1.5, :VALUE, "Pokémon\n"] }
    assert(outcome(method(:legacy_generate), packet) == outcome(codec.method(:tracker_json_generate), packet), "duplicate string/symbol keys and nested value semantics remain unchanged")
    ascii_packet = { "plain" => [nil, true, :SYMBOL, "ASCII\n"] }
    assert(outcome(method(:legacy_generate), ascii_packet) == outcome(codec.method(:tracker_json_generate), ascii_packet), "ASCII-only packets retain their source-literal encoding")
    assert(codec.tracker_json_string((0..31).to_a.pack("U*")).include?("\\u0000"), "all JSON control characters remain escaped")
    return values.length
  end

  def self.source_modes(source)
    connection = File.binread(File.join(source, "src/tracker/live/Connection.rb"))
    first = connection.index("  def self.tracker_json_generate(value)")
    last = connection.index("\n  class TrackerConnection", first)
    assert(first && last, "the pure connection encoder definitions are present")
    constant_first = connection.index("  TRACKER_JSON_STRING_ESCAPES")
    constant_last = connection.index("\n  TRACKER_DIAGNOSTIC_CAPABILITIES", constant_first)
    assert(constant_first && constant_last, "the pure connection encoder constants are present")
    definitions = connection[constant_first...constant_last] + "\n" + connection[first...last]
    return [Encoding::UTF_8, Encoding::ASCII_8BIT].map do |encoding|
      codec = Module.new
      codec.module_eval(definitions.dup.force_encoding(encoding), "tracker_json_#{encoding.name}", 1)
      checked = compatibility(codec)
      { "source_encoding" => encoding.name, "compatibility_cases" => checked }
    end
  end

  def self.measure(encoder, value, repetitions)
    encoder.call(value)
    GC.start
    allocations = GC.stat(:total_allocated_objects)
    collections = GC.count
    started = Process.clock_gettime(Process::CLOCK_MONOTONIC)
    repetitions.times { encoder.call(value) }
    seconds = Process.clock_gettime(Process::CLOCK_MONOTONIC) - started
    return { "milliseconds_per_packet" => seconds * 1000 / repetitions,
      "allocations_per_packet" => (GC.stat(:total_allocated_objects) - allocations).to_f / repetitions,
      "gc_cycles" => GC.count - collections }
  end

  def self.run
    source = ENV.fetch("IRONMON_SMOKE_SOURCE_ROOT")
    output = ENV.fetch("IRONMON_SMOKE_OUTPUT_ROOT")
    load File.join(source, "src/tracker/live/Connection.rb") if !defined?(Ironmon) || !Ironmon.respond_to?(:tracker_json_string)
    checked = compatibility
    modes = source_modes(source)
    configure_legacy(Ironmon)
    load File.join(source, "tests/runtime/Tracker-JSON.rb")
    nodes = Array.new(400) do |index|
      { "species_id" => "SPECIES_#{index}", "name" => "Pokémon / ニドラン ♀ #{index}",
        "description" => ("A sample move or evolution description with ordinary spaces and Unicode é. " * 6),
        "types" => ["NORMAL", "FLYING"], "moves" => ["MOVE_A", "MOVE_B", "MOVE_C", "MOVE_D"],
        "available" => true, "count" => index, "metadata" => { "escaped" => "line\nquote\"slash\\", "empty" => nil } }
    end
    packet = { "schema_version" => 1, "type" => "response", "request_id" => "benchmark",
      "payload" => { "complete" => true, "nodes" => nodes } }
    old = legacy_generate(packet)
    assert(old == Ironmon.tracker_json_generate(packet), "representative large packet is byte-identical")
    legacy = measure(method(:legacy_generate), packet, 5)
    optimized = measure(Ironmon.method(:tracker_json_generate), packet, 5)
    assert(optimized["allocations_per_packet"] < legacy["allocations_per_packet"] * 0.5, "bulk escaping removes per-codepoint allocations")
    report = { "status" => "passed", "ruby_version" => RUBY_VERSION, "ruby_platform" => RUBY_PLATFORM,
      "compatibility_cases" => checked, "packet_bytes" => old.bytesize, "legacy" => legacy, "optimized" => optimized,
      "source_modes" => modes,
      "time_speedup" => legacy["milliseconds_per_packet"] / optimized["milliseconds_per_packet"],
      "allocation_reduction" => 1 - optimized["allocations_per_packet"] / legacy["allocations_per_packet"] }
    File.binwrite(File.join(output, "tracker-json-performance.json"), Ironmon.tracker_json_generate(report))
    puts Ironmon.tracker_json_generate(report)
  end
end

if defined?($ironmon_speed_up_acceptance_output)
  IronmonTrackerJsonPerformance.run
else
  $stdout.sync = true
  $stderr.sync = true
  begin
    IronmonTrackerJsonPerformance.run
    exit!(0)
  rescue Exception => error
    output = ENV.fetch("IRONMON_SMOKE_OUTPUT_ROOT")
    File.binwrite(File.join(output, "tracker-json-performance-failure.txt"), "#{error.class}: #{error.message}\n#{error.backtrace.join("\n")}")
    warn "#{error.class}: #{error.message}"
    exit!(1)
  end
end
