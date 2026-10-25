module IronmonTrackerJsonRuntimeTests
  def self.run
    text = "Pok\u00e9mon Breeder \u2640 \u{1F600}"
    expected = '"' + text + '"'
    raise "UTF-8 tracker text changed" if Ironmon.tracker_json_string(text) != expected
    binary = text.dup.force_encoding(Encoding::ASCII_8BIT)
    raise "Binary UTF-8 tracker text was double encoded" if Ironmon.tracker_json_string(binary) != expected
    raise "Tracker text input was modified" if binary.encoding != Encoding::ASCII_8BIT
    raise "Tracker JSON quote escaping changed" if Ironmon.tracker_json_string('"').bytes != [34, 92, 34, 34]
    raise "Tracker JSON newline escaping changed" if Ironmon.tracker_json_string("\n").bytes != [34, 92, 110, 34]
    connection = Ironmon::TrackerConnection.new
    decoded = connection.send(:normalize_json, HTTPLite::JSON.parse('{"hat":null,"dye":137,"name":"null : test","values":[true,false,1.5]}'))
    raise "Null accessory was not decoded" if !decoded.has_key?("hat") || !decoded["hat"].nil?
    raise "Integral wardrobe dye was not normalized" if !decoded["dye"].is_a?(Integer) || decoded["dye"] != 137
    raise "JSON strings or primitive values changed" if decoded["name"] != "null : test" || decoded["values"] != [true, false, 1.5]
    puts "Tracker JSON text regressions passed"
  end
end

IronmonTrackerJsonRuntimeTests.run
