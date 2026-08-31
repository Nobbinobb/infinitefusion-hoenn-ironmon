module IronmonWildEncounterFusionRuntimeTests
  OUTPUT_PATH = $ironmon_wild_encounter_fusion_test_output_path.to_s

  def self.assert(condition, message)
    raise "Wild encounter fusion runtime test failed: #{message}" if !condition
  end

  def self.with_singleton_method_stub(owner, name, implementation)
    singleton = class << owner; self; end
    backup = "ironmon_wild_fusion_test_original_#{name}".to_sym
    singleton.send(:alias_method, backup, name)
    singleton.send(:define_method, name, implementation)
    return yield
  ensure
    if singleton && singleton.method_defined?(backup)
      singleton.send(:alias_method, name, backup)
      singleton.send(:remove_method, backup)
    end
  end

  def self.encounter(species, map_id, version, encounter_type, slot)
    result = [species, 12]
    Ironmon.attach_wild_source(
      result,
      "encounter:#{map_id}:#{version}:#{encounter_type}:#{slot}"
    )
    return result
  end

  def self.test_configured_rates
    assert(
      Ironmon::WILD_FUSION_STANDARD_SAME_CHANCE == 10,
      "standard same-environment encounters use a 10 percent roll"
    )
    assert(
      Ironmon::WILD_FUSION_STANDARD_CROSS_CHANCE == 5,
      "standard cross-environment encounters use a 5 percent roll"
    )
    assert(
      Ironmon::WILD_FUSION_OVERWORLD_CHANCE == 36,
      "overworld pairs use the actual 36 percent roll"
    )
  end

  def self.test_source_classification_and_keys
    grass = encounter(:PIKACHU, 17, 0, :LandMorning, 2)
    grass_partner = encounter(:CHARMANDER, 17, 0, :LandMorning, 5)
    water = encounter(:SQUIRTLE, 17, 0, :WaterRain, 1)
    assert(
      Ironmon.encounter_environment(:LandRain) == :grass &&
        Ironmon.encounter_environment(:CaveNight) == :cave &&
        Ironmon.encounter_environment(:WaterMorning) == :water &&
        Ironmon.encounter_environment(:GoodRod) == :fishing,
      "encounter types map to stable broad environments"
    )
    assert(
      Ironmon.wild_fusion_discovery_key(
        grass, grass_partner, :standard_same
      ) == "encounter_fusion:17:standard_same:0:LandMorning:2:0:LandMorning:5",
      "same-environment source slots produce a stable discovery key"
    )
    assert(
      Ironmon.wild_fusion_discovery_key(
        grass, water, :overworld_cross
      ) == "encounter_fusion:17:overworld_cross:0:LandMorning:2:0:WaterRain:1",
      "cross-environment source slots preserve both tables"
    )
  end

  def self.test_standard_fusion_uses_player_mapping_and_queues_discovery
    first = encounter(:PIKACHU, 17, 0, :Land, 1)
    second = encounter(:CHARMANDER, 17, 0, :Land, 2)
    calls = []
    Ironmon.begin_area_encounter_sequence
    with_singleton_method_stub(
      Ironmon,
      :player_wild_fusion_species,
      proc { |body, head| calls << [body, head]; :BULBASAUR }
    ) do
      result = Ironmon.fuse_wild_results(
        first, second, :standard_same
      )
      assert(result[0] == :BULBASAUR, "the mapped player fusion is returned")
    end
    pending = Ironmon.instance_variable_get(:@pending_area_encounter_fusions)
    assert(
      calls == [[:PIKACHU, :CHARMANDER]],
      "standard encounters retain first-as-body orientation"
    )
    assert(
      pending == [
        "encounter_fusion:17:standard_same:0:Land:1:0:Land:2"
      ],
      "the exact derived pair is queued for battle-time disclosure"
    )
  ensure
    Ironmon.clear_area_encounter_sequence
  end

  def self.test_overworld_fusion_retains_existing_orientation
    first = Pokemon.new(:PIKACHU, 10)
    second = Pokemon.new(:CHARMANDER, 14)
    first_source = encounter(:PIKACHU, 17, 0, :Land, 1)
    second_source = encounter(:CHARMANDER, 17, 0, :Water, 2)
    first.instance_variable_set(
      :@ironmon_wild_source, Ironmon.wild_source_for(first_source)
    )
    second.instance_variable_set(
      :@ironmon_wild_source, Ironmon.wild_source_for(second_source)
    )
    calls = []
    recorded = []
    with_singleton_method_stub(
      Ironmon,
      :player_wild_fusion_species,
      proc { |body, head| calls << [body, head]; :BULBASAUR }
    ) do
      with_singleton_method_stub(
        Ironmon,
        :record_encountered_area_slots,
        proc { |keys| recorded.concat(keys) }
      ) do
        result = Ironmon.build_overworld_wild_fusion(
          first, second, :overworld_cross
        )
        assert(result.level == 12, "overworld fusion levels are averaged")
        assert(
          result.instance_variable_get(:@ironmon_wild_policy_mapped) == true,
          "the derived Pokemon cannot be remapped as a scripted encounter"
        )
      end
    end
    assert(
      calls == [[:CHARMANDER, :PIKACHU]],
      "overworld fusions retain the base game's second-as-body orientation"
    )
    assert(
      recorded == [
        "encounter_fusion:17:overworld_cross:0:Land:1:0:Water:2"
      ],
      "an overworld fusion discloses its exact two source slots"
    )
  end

  def self.lookup_source(species, encounter_type, slot, version = 0)
    return {
      "metadata" => {
        "entry_id" => "encounter:17:#{version}:#{encounter_type}:#{slot}",
        "map_id" => 17, "version" => version,
        "encounter_type" => encounter_type.to_s, "slot" => slot,
        "minimum_level" => 4, "maximum_level" => 6
      },
      "species" => species
    }
  end

  def self.test_lookup_lists_same_and_cross_table_possibilities
    sources = [
      lookup_source(:PIKACHU, :LandMorning, 1),
      lookup_source(:CHARMANDER, :LandMorning, 2),
      lookup_source(:SQUIRTLE, :LandDay, 1),
      lookup_source(:BULBASAUR, :Water, 1)
    ]
    same_count = Ironmon.tracker_area_encounter_fusion_count(
      sources, false, "grass"
    )
    cross_count = Ironmon.tracker_area_encounter_fusion_count(sources, true)
    assert(
      same_count == 4,
      "two normal slots in one table expose both standard and overworld orientations"
    )
    assert(
      cross_count == 20,
      "different time and terrain tables expose every ordered cross-table possibility"
    )
    [false, true].each do |overworld|
      [false, true].each do |cross|
        count = Ironmon.tracker_area_encounter_fusion_count(
          sources, cross, nil, overworld
        )
        active_descriptors = Ironmon.tracker_area_encounter_fusion_descriptors(
          sources, cross, 0, 50, nil, overworld
        )
        origin = "#{overworld ? 'overworld' : 'standard'}_#{cross ? 'cross' : 'same'}"
        chance = overworld ? 36 : (cross ? 5 : 10)
        assert(count == active_descriptors.length, "mode-specific paging totals agree")
        assert(count == (cross ? 10 : 2), "only the active mechanic is listed")
        assert(active_descriptors.all? { |entry| entry["origin"] == origin },
          "lookup excludes the inactive encounter mode")
        assert(Ironmon.tracker_area_encounter_fusion_chance(origin) == chance,
          "lookup shows the active mechanic's exact chance")
      end
    end
    descriptors = Ironmon.tracker_area_encounter_fusion_descriptors(
      sources, true, 0, cross_count
    )
    assert(
      descriptors.any? do |descriptor|
        descriptor["first"]["metadata"]["encounter_type"] == "LandMorning" &&
          descriptor["second"]["metadata"]["encounter_type"] == "LandDay"
      end,
      "morning and day are cross-table possibilities even within grass"
    )
    version_sources = [
      lookup_source(:PIKACHU, :LandMorning, 1, 0),
      lookup_source(:CHARMANDER, :LandMorning, 1, 1)
    ]
    version_descriptors = Ironmon.tracker_area_encounter_fusion_descriptors(
      version_sources, true, 0, 10
    )
    assert(
      version_descriptors.all? do |descriptor|
        descriptor["origin"] == "overworld_cross"
      end,
      "different encounter versions can meet only through retained overworld spawns"
    )
    assert(Ironmon.tracker_area_encounter_fusion_count(
      version_sources, true, nil, false
    ) == 0, "disabled overworld mode cannot combine encounter versions")
  end

  def self.test_overview_derived_locations_match_area_entries
    sources = [
      lookup_source(:PIKACHU, :LandMorning, 1),
      lookup_source(:CHARMANDER, :LandMorning, 2),
      lookup_source(:CHARMANDER, :LandDay, 1),
      lookup_source(:CHARMANDER, :Water, 1),
      lookup_source(:CHARMANDER, :LandMorning, 1, 1),
      lookup_source(:CHARMANDER, :OldRod, 1)
    ]
    foreign = lookup_source(:CHARMANDER, :Water, 1)
    foreign["metadata"]["map_id"] = 18
    sources << foreign
    target = GameData::Species.get(:B1H2)
    [false, true, nil].each do |overworld|
      recipe = { "overworld_encounters" => overworld }
      occurrences = []
      Ironmon.tracker_append_derived_wild_occurrences(
        occurrences, target, recipe, sources, [[25, 4], [25, 25]], nil
      )
      expected = [false, true].flat_map do |cross|
        Ironmon.tracker_area_encounter_fusion_descriptors(sources, cross, 0, 100, nil, overworld)
      end.select do |descriptor|
        Ironmon.tracker_area_encounter_fusion_materials([descriptor]) == [[25, 4]]
      end
      expected_ids = expected.map do |descriptor|
        Ironmon.tracker_area_encounter_fusion_metadata(descriptor, {})["entry_id"]
      end
      assert(!expected_ids.empty?, "the test includes eligible derived fusions")
      assert(occurrences.map { |entry| entry["entry_id"] }.sort == expected_ids.sort,
        "overview and area lookup share exact pair eligibility, direction, mode, and table identities")
      assert(occurrences.all? { |entry| entry["map_id"] == 17 }, "different maps cannot combine")
      assert(occurrences.none? { |entry| entry["chance_percent"] }, "fusion rolls are not presented as exact encounter probabilities")
      occurrences.each do |entry|
        assert(entry["fusion_chance_percent"] == Ironmon.tracker_area_encounter_fusion_chance(entry["origin"]),
          "overview uses the area's actual fusion roll")
      end
    end
    bits = Array.new((NB_POKEMON * NB_POKEMON + 7) / 8, 0)
    [[25, 4], [NB_POKEMON, 1], [1, NB_POKEMON]].each do |body, head|
      position = (body - 1) * NB_POKEMON + head - 1
      bits[position / 8] |= 1 << (position % 8)
    end
    decoded = Ironmon.tracker_decode_occurrence_materials([bits.pack("C*")].pack("m0"))
    assert(decoded.sort == [[25, 4], [NB_POKEMON, 1], [1, NB_POKEMON]].sort,
      "packed native membership retains ordered materials and boundary positions")
    assert(Ironmon.tracker_decode_occurrence_materials(nil).nil?, "missing membership preserves cooperative fallback")
    rejected = false
    begin
      Ironmon.tracker_decode_occurrence_materials("!")
    rescue Ironmon::TrackerLookupError
      rejected = true
    end
    assert(rejected, "malformed material membership is rejected")
    active = { "run_id" => "same", "active_run" => true, "overworld_encounters" => false }
    key = Ironmon.tracker_occurrence_cache_key(active, target, :wild)
    assert(key != Ironmon.tracker_occurrence_cache_key(active.merge("overworld_encounters" => true), target, :wild),
      "overworld option changes cannot reuse standard locations")
    assert(key != Ironmon.tracker_occurrence_cache_key(active.merge("active_run" => false), target, :wild),
      "archive and diagnostic locations cannot collide")
  end

  def self.test_concealed_fusion_rows_and_disclosure
    sources = [
      lookup_source(:PIKACHU, :LandMorning, 1),
      lookup_source(:CHARMANDER, :LandMorning, 2),
      lookup_source(:SQUIRTLE, :Water, 1)
    ]
    [false, true].each do |overworld|
      [false, true].each do |cross|
        descriptors = Ironmon.tracker_area_encounter_fusion_descriptors(
          sources, cross, 0, 50, nil, overworld
        )
        ids = descriptors.map { |entry| Ironmon.tracker_area_encounter_fusion_metadata(entry, {})["entry_id"] }
        first = descriptors.first["first"]["metadata"]["entry_id"]
        second = descriptors.first["second"]["metadata"]["entry_id"]
        with_singleton_method_stub(Ironmon, :tracker_area_fusion_work, proc { |_recipe| raise "concealed rows must not map fusion results" }) do
          [{}, { first => true }].each do |discoveries|
            rows = Ironmon.tracker_area_encounter_fusion_entries(descriptors, {}, discoveries, [], false)
            assert(rows.map { |row| row["entry_id"] } == ids, "concealed rows preserve all possibilities and ordering")
            assert(rows.all? { |row| row["details_revealed"] == false }, "one discovered source cannot reveal a fusion")
            assert(rows.none? { |row| ["species_id", "species_name", "sprite_path"].any? { |key| row.key?(key) } }, "concealed payloads omit every identity field")
          end
        end
        requested_pairs = []
        work = Object.new
        work.define_singleton_method(:native_results_for) do |pairs, _native|
          requested_pairs.replace(pairs)
          pairs.map { |pair| { "species_id" => "B#{pair[0]}H#{pair[1]}:0", "species_name" => "Visible fusion" } }
        end
        with_singleton_method_stub(Ironmon, :tracker_area_fusion_work, proc { |_recipe| work }) do
          discoveries = { first => true, second => true }
          rows = Ironmon.tracker_area_encounter_fusion_entries(descriptors, {}, discoveries, [], false)
          visible = rows.select { |row| row["details_revealed"] }
          assert(rows.map { |row| row["entry_id"] } == ids, "discoveries cannot change paging positions")
          assert(visible.length == 2 && requested_pairs.length == 2, "only the two known orientations are reconstructed")
          assert(rows.reject { |row| row["details_revealed"] }.none? { |row| row["species_id"] }, "mixed pages retain concealed identities")
          rows = Ironmon.tracker_area_encounter_fusion_entries(descriptors, {}, { ids.first => true }, [], false)
          assert(rows.count { |row| row["details_revealed"] } == 1, "an exact encountered combination reveals only itself")
          rows = Ironmon.tracker_area_encounter_fusion_entries(descriptors, {}, {}, [], true)
          assert(rows.all? { |row| row["details_revealed"] }, "archive and diagnostics retain every revealed identity")
        end
      end
    end
  end

  def self.test_location_failure_and_live_target_binding
    with_singleton_method_stub(Ironmon, :tracker_lookup_wild_occurrences,
      proc { |*_args| raise "location test failure" }) do
      work = Ironmon::TrackerWildOccurrenceWork.new(nil, {}, [])
      2.times do
        rejected = false
        begin
          work.results
        rescue Ironmon::TrackerLookupError => error
          rejected = error.message.include?("location test failure")
        end
        assert(rejected, "failed location work is terminal, never a pending or successful empty page")
      end
    end
    with_singleton_method_stub(Ironmon, :tracker_validate_debug_context, proc { |_scopes| nil }) do
      with_singleton_method_stub(Ironmon, :tracker_debug_resolve_pokemon, proc { |_payload| Pokemon.new(:PIKACHU, 5) }) do
        secured = Ironmon.tracker_debug_secure_species_payload({
          "species_id" => "CHARMANDER:0", "target" => "player",
          "fusion_material_membership" => "supplied"
        })
        assert(secured["species_id"] == "PIKACHU:0", "live species overrides submitted identity")
        assert(secured["fusion_membership_species_id"] == "CHARMANDER:0", "native membership remains bound to the original target")
        rejected = false
        begin
          Ironmon.tracker_occurrence_search_for_recipe(secured, {}, :wild)
        rescue Ironmon::TrackerLookupError => error
          rejected = error.message.include?("represented Pokemon")
        end
        assert(rejected, "stale live-target material membership is rejected before lookup or cache access")
      end
    end
  end

  def self.test_native_page_validation
    pool = Object.new
    pool.define_singleton_method(:include_number?) { |number| number == NB_POKEMON + 2 }
    with_singleton_method_stub(Ironmon, :custom_fusion_pool_service, proc { pool }) do
      work = Ironmon::TrackerAreaFusionWork.new({})
      work.define_singleton_method(:format_result) { |number| { "species_number" => number } }
      assert(work.native_results_for([[25, 4]], []).nil?, "missing native assignments request only their page")
      unrelated = { "body_id" => 1, "head_id" => 2, "species_number" => NB_POKEMON + 2 }
      assert(work.native_results_for([[25, 4]], [unrelated]).nil?, "unrequested pairs cannot satisfy or populate a page")
      assert(work.instance_variable_get(:@results).empty?, "unrequested pairs are not cached")
      supplied = { "body_id" => 25, "head_id" => 4, "species_number" => NB_POKEMON + 2 }
      result = work.native_results_for([[25, 4], [25, 4]], [supplied])
      assert(result.length == 2, "repeated source slots reuse their ordered material mapping")
      assert(work.instance_variable_get(:@mapper).nil?, "native pages never initialize a Ruby mapper")
      work.instance_variable_set(:@queued, { [1, 2] => true })
      work.instance_variable_set(:@fiber, Fiber.new { Fiber.yield })
      work.native_results_for([[25, 4]], [])
      assert(work.instance_variable_get(:@queued).empty?, "native handoff does not strand an abandoned fallback pair")
      rejected = false
      begin
        work.native_results_for([[25, 4]], [supplied.merge("species_number" => 25)])
      rescue Ironmon::TrackerLookupError => error
        rejected = error.message.include?("invalid")
      end
      assert(rejected, "invalid native results fail instead of polling forever")
    end
  end

  def self.run
    test_configured_rates
    test_source_classification_and_keys
    test_standard_fusion_uses_player_mapping_and_queues_discovery
    test_overworld_fusion_retains_existing_orientation
    test_lookup_lists_same_and_cross_table_possibilities
    test_concealed_fusion_rows_and_disclosure
    test_overview_derived_locations_match_area_entries
    test_location_failure_and_live_target_binding
    test_native_page_validation
    File.binwrite(OUTPUT_PATH, "wild encounter fusion runtime tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonWildEncounterFusionRuntimeTests.run
