module IronmonEncounterTriggerRuntimeTests
  def self.assert(condition, message)
    raise "Encounter trigger test failed: #{message}" if !condition
  end

  def self.run
    source_root = File.dirname($ironmon_source_root)
    load File.join(source_root, "tools/generation/Encounter-Triggers.rb")
    document = Ironmon.tracker_area_catalog_document
    Ironmon.tracker_validate_area_catalog(document)
    original_catalogs = Ironmon.instance_variable_get(:@tracker_area_encounter_catalogs)
    original_active_profile = Ironmon.instance_variable_get(:@active_generation_profile_id)
    original_documents = Ironmon.instance_variable_get(:@tracker_area_catalog_documents)
    begin
      Ironmon.instance_variable_set(:@tracker_area_encounter_catalogs, nil)
      ["classic", "remix"].each do |mode|
        recipe = { "data_mode" => mode }
        service = Ironmon::TrackerObtainabilityService.allocate
        service.instance_variable_set(:@recipe, recipe)
        service.define_singleton_method(:cooperative_checkpoint) {}
        service.send(:prepare_encounter_tables)
        tables = service.instance_variable_get(:@encounter_tables)
        assert(tables.none? { |table| [78, 79, 80, 102, 104].include?(table[1]) }, "#{mode} obtainability excludes phantom gym and interior sources")
        catalog = Ironmon.tracker_area_encounter_catalog(recipe)
        [78, 79, 80, 102, 104].each do |map_id|
          assert(catalog[map_id].empty?, "#{mode} map #{map_id} has no usable encounter rows")
        end
        assert(catalog[28].none? { |row| row["encounter_type"] == "Land" }, "Rusturf excludes grass encounters without grass")
        assert(catalog[28].any? { |row| row["encounter_type"] == "RockSmash" }, "Rusturf Rock Smash encounters are retained")
        assert(catalog[28].any? { |row| row["encounter_type"] == "Cave" }, "Rusturf cave encounters are retained")
        assert(catalog[35].all? { |row| row["encounter_type"] == "Cave" }, "Granite B2F retains Cave but removes unused Rock Smash")
        assert(!catalog[35].empty?, "Granite cave encounters do not need grass")
        assert(catalog[97].all? { |row| row["encounter_type"] == "Land1" }, "Illusion Grove needs its actual alternate grass")
        assert(!catalog[97].empty?, "Illusion Grove alternate grass is retained")
        assert(catalog[77].none? { |row| row["encounter_type"].start_with?("Land") }, "Route 111 South has no usable land tables")
        assert(catalog[77].any? { |row| row["encounter_type"] == "WaterRain" }, "Route 111 South keeps weather water encounters")
        assert(catalog[39].any? { |row| row["encounter_type"] == "OldRod" }, "Route 109 keeps fishing")
        assert(catalog[76].any? { |row| row["encounter_type"] == "TallGrass" }, "Route 118 keeps tall grass")
        [78, 79, 80, 102].each do |map_id|
          area = { "map_ids" => [map_id] }
          metadata = Ironmon.tracker_area_encounter_metadata(area, recipe)
          assert(Ironmon.tracker_area_encounter_total(metadata, recipe) == 0, "gym has neither slots nor derived fusions")
        end
        catalog.each_value do |rows|
          rows.each do |row|
            assert(row["context_slot"] == row["slot"] - 1, "filtering preserves generator slot identities")
            assert(row["entry_id"].end_with?(":#{row['encounter_type']}:#{row['slot']}"), "filtering preserves discovery keys")
          end
        end
      end

      legacy_id = "f" * 64
      documents = original_documents.dup
      documents[legacy_id] = document.reject { |key, _| key == "encounter_triggers" }
      Ironmon.instance_variable_set(:@tracker_area_catalog_documents, documents)
      Ironmon.instance_variable_set(:@active_generation_profile_id, legacy_id)
      assert(!Ironmon.tracker_area_encounter_table_available?(79, :Land), "old saved profiles receive the gym fix")
      assert(Ironmon.tracker_area_encounter_table_available?(76, :TallGrass), "old saved profiles retain real encounters")
      assert(!documents[legacy_id].key?("encounter_triggers"), "legacy catalog is not rewritten")

      map = RPG::Map.new(2, 2)
      tileset = RPG::Tileset.new
      tileset.terrain_tags = Table.new(386)
      tileset.passages = Table.new(386)
      tileset.priorities = Table.new(386)
      tileset.priorities[0] = 5
      tileset.terrain_tags[384] = GameData::TerrainTag.get(:Grass).id_number
      map.data[0, 0, 0] = 384
      assert(IronmonEncounterTriggers.for_map(map, tileset).include?("Land"), "real walkable grass permits land encounters")
      tileset.passages[384] = 15
      assert(!IronmonEncounterTriggers.for_map(map, tileset).include?("Land"), "impassable decorative grass is excluded")
      tileset.passages[384] = 0
      tileset.terrain_tags[384] = GameData::TerrainTag.get(:Grass_alt1).id_number
      methods = IronmonEncounterTriggers.for_map(map, tileset)
      assert(methods.include?("Land1") && !methods.include?("Land"), "alternate grass does not invent ordinary grass")

      puts "Encounter trigger runtime regressions passed"
    ensure
      Ironmon.instance_variable_set(:@tracker_area_encounter_catalogs, original_catalogs)
      Ironmon.instance_variable_set(:@active_generation_profile_id, original_active_profile)
      Ironmon.instance_variable_set(:@tracker_area_catalog_documents, original_documents)
    end
  end
end

IronmonEncounterTriggerRuntimeTests.run
