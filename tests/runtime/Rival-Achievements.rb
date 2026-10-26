source_root = ENV.fetch("IRONMON_SMOKE_SOURCE_ROOT").tr("\\", "/")
output_root = ENV.fetch("IRONMON_SMOKE_OUTPUT_ROOT").tr("\\", "/")
$ironmon_rival_achievement_output = output_root
begin
  module System
    def self.data_directory
      return File.join($ironmon_rival_achievement_output, "userdata")
    end
  end
  Dir.mkdir(System.data_directory) if !Dir.exist?(System.data_directory)
  load File.join(source_root, "tools/generation/Script-Loader.rb")
  IronmonScriptLoader.load_directory("Data/Scripts", [/\A(?:997|998|999)/])
  GameData.load_all
  IronmonScriptLoader.load_manifest(File.join(source_root, "src"), File.join(source_root, "src/load_order.json"), false)
  catalog = Marshal.load(File.binread(File.join(source_root, "data/area_catalog.dat")))
  entries = catalog["areas"].flat_map { |area| area["trainers"] }.select { |entry| ["rival", "wally"].include?(entry["battle_kind"]) }
  raise "The opening rival is missing" if !entries.any? { |entry| entry["entry_id"] == "trainer:18:RIVAL1:Rival" }
  raise "Wally's encounters are missing" if !entries.any? { |entry| entry["battle_kind"] == "wally" }
  collection = Ironmon::GameplayCollection
  collection.define_singleton_method(:enabled?) { true }
  Ironmon.define_singleton_method(:current_run_attempt) { { "result" => "active" } }
  counter = 0
  collection.define_singleton_method(:next_id) { |kind| counter += 1; "rival-achievement:#{kind}:#{counter}" }
  captured = []
  collection.define_singleton_method(:emit) do |type, payload = {}, context = {}|
    captured << { "type" => type, "payload" => Ironmon::GameplayCollection::SourceContext.enrich(type, payload), "context" => current_context.merge(context) }
    captured.length
  end
  Object.send(:define_method, :pbMapInterpreter) { nil }
  cases = entries.map do |entry|
    expected_class = entry["battle_kind"] == "rival" ? "RIVAL1" : "RIVAL2"
    raise "A story encounter changed native trainer class: #{entry.inspect}" if entry["trainer_type"] != expected_class
    Ironmon.instance_variable_set(:@current_area_trainer_entry, entry["entry_id"])
    Ironmon.instance_variable_set(:@pending_area_trainer_entries, [])
    Ironmon.define_singleton_method(:current_area_event_reference) { |*_args| [entry["map_id"], entry["event_id"]] }
    $game_map = Struct.new(:map_id).new(entry["map_id"])
    captured.clear
    battle = PokeBattle_Battle.allocate
    battle.instance_variable_set(:@field, PokeBattle_ActiveField.new)
    battle.instance_variable_set(:@sides, [PokeBattle_ActiveSide.new, PokeBattle_ActiveSide.new])
    battle.instance_variable_set(:@positions, [])
    battle.instance_variable_set(:@battlers, [])
    battle.instance_variable_set(:@sideSizes, [1, 1])
    battle.instance_variable_set(:@player, [])
    battle.instance_variable_set(:@opponent, [NPCTrainer.new("A different display name", expected_class.to_sym)])
    battle.instance_variable_set(:@internalBattle, true)
    battle.instance_variable_set(:@debug, false)
    battle.instance_variable_set(:@decision, 1)
    battle.define_singleton_method(:trainerBattle?) { true }
    battle.define_singleton_method(:wildBattle?) { false }
    collection::Combat.admit(battle)
    collection::Combat.finish_battle(battle, [], [], 1)
    collection::Combat.release_battle(battle, false)
    started = captured.find { |event| event["type"] == "battle.started" }
    ended = captured.find { |event| event["type"] == "battle.ended" }
    raise "The native class was not captured" if started.dig("payload", "encounter", "trainer_class_ids") != [expected_class]
    raise "The stable encounter was not captured" if started.dig("payload", "encounter", "authored_entry_ids") != [entry["entry_id"]]
    raise "The native victory was not captured" if !ended || ended.dig("payload", "outcome") != "win"
    { "entry_id" => entry["entry_id"], "map_id" => entry["map_id"], "event_id" => entry["event_id"], "kind" => entry["battle_kind"], "trainer_class" => expected_class, "started" => started["payload"], "ended" => ended["payload"] }
  end
  File.binwrite(File.join(output_root, "rival-achievements.json"), JSON.generate({ "status" => "passed", "ruby_version" => RUBY_VERSION, "cases" => cases }))
  exit!(0)
rescue Exception => error
  File.binwrite(File.join(output_root, "rival-achievements.json"), JSON.generate({ "status" => "failed", "error" => "#{error.class}: #{error.message}", "backtrace" => error.backtrace }))
  exit!(1)
end
