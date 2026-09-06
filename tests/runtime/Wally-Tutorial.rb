module IronmonWallyTutorialRuntimeTests
  OUTPUT_PATH = $ironmon_wally_tutorial_test_output_path.to_s

  def self.assert(condition, message)
    raise "Wally tutorial runtime test failed: #{message}" if !condition
  end

  def self.with_singleton_method_stub(owner, name, implementation)
    singleton = class << owner; self; end
    backup = :"wally_tutorial_test_original_#{name}"
    singleton.send(:alias_method, backup, name)
    singleton.send(:define_method, name, implementation)
    return yield
  ensure
    if singleton && singleton.method_defined?(backup)
      singleton.send(:alias_method, name, backup)
      singleton.send(:remove_method, backup)
    end
  end

  def self.with_trainer_policy(policy)
    configuration = Ironmon::Configuration.new(
      Ironmon::Configuration::POLICY_MIXED, policy
    )
    return with_singleton_method_stub(
      Ironmon, :configuration, proc { configuration }
    ) { yield }
  end

  def self.with_custom_fusion_pool_fixture
    original = Ironmon.instance_variable_get(:@custom_fusion_pool_service)
    pool = [:B1H2, :B2H1].freeze
    service = Object.new
    service.define_singleton_method(:pool) { pool }
    service.define_singleton_method(:include_identity?) do |identity|
      pool.include?(identity)
    end
    Ironmon.instance_variable_set(:@custom_fusion_pool_service, service)
    return yield
  ensure
    Ironmon.instance_variable_set(:@custom_fusion_pool_service, original)
  end

  def self.with_player_fusion_mapper_fixture
    results = {
      [25, 4] => :B1H2,
      [4, 25] => :B2H1
    }
    mapper = Object.new
    mapper.define_singleton_method(:species) do |body, head|
      body_id = GameData::Species.get(body).id_number
      head_id = GameData::Species.get(head).id_number
      results.fetch([body_id, head_id])
    end
    with_singleton_method_stub(
      Ironmon, :normal_species_pool, proc { [:PIKACHU, :CHARMANDER] }
    ) do
      return with_singleton_method_stub(
        Ironmon, :player_fusion_mapper, proc { mapper }
      ) { yield }
    end
  end

  def self.event_strings(map)
    return map.events.values.flat_map do |event|
      event.pages.flat_map do |page|
        page.list.flat_map do |command|
          command.parameters.select { |parameter| parameter.is_a?(String) }
        end
      end
    end
  end

  def self.test_policy_split
    with_trainer_policy(Ironmon::Configuration::POLICY_NORMAL_ONLY) do
      assert(
        !Ironmon.wally_tutorial_uses_fusion?,
        "Normal Only skips the tutorial fusion"
      )
    end
    [
      Ironmon::Configuration::POLICY_MIXED,
      Ironmon::Configuration::POLICY_CUSTOM_FUSIONS_ONLY
    ].each do |policy|
      with_trainer_policy(policy) do
        assert(
          Ironmon.wally_tutorial_uses_fusion?,
          "#{policy} retains the tutorial fusion"
        )
      end
    end
  end

  def self.test_materials_resolve_one_custom_fusion_plan
    with_trainer_policy(Ironmon::Configuration::POLICY_MIXED) do
      first = Ironmon.wally_tutorial_fusion_plan
      second = Ironmon.wally_tutorial_fusion_plan
      assert(first == second, "the tutorial fusion plan is deterministic")
      assert(
        Ironmon.custom_fusion_species?(first[:fusion]),
        "the resolved result belongs to the custom-sprite pool"
      )
      assert(
        Ironmon.player_fusion_species(first[:body], first[:head]) ==
          first[:fusion],
        "the selected Ironmon materials map to the planned fusion exactly"
      )
      standard = getFusedPokemonIdFromSymbols(first[:body], first[:head])
      assert(
        GameData::Species.get(standard).id != first[:fusion],
        "the tutorial does not fall back to Infinite Fusion's raw components"
      )
      assert(
        GameData::Species.get(first[:body]).id_number <= NB_POKEMON &&
          GameData::Species.get(first[:head]).id_number <= NB_POKEMON,
        "both event materials are normal component species"
      )
    end
  end

  def self.test_fusion_scene_uses_ironmon_materials_and_target_sprite
    with_trainer_policy(Ironmon::Configuration::POLICY_MIXED) do
      plan = Ironmon.wally_tutorial_fusion_plan
      body = Pokemon.new(plan[:body], 10)
      head = Pokemon.new(plan[:head], 5)
      loader_arguments = nil
      start_arguments = nil
      fusion_calls = 0
      end_calls = 0
      loader = Object.new
      loader.define_singleton_method(:obtain_fusion_pif_sprite) do |*arguments|
        loader_arguments = arguments
        :wally_target_sprite
      end
      scene = Object.new
      scene.define_singleton_method(:pbStartScreen) do |*arguments|
        start_arguments = arguments
        true
      end
      scene.define_singleton_method(:pbFusionScreen) do |*arguments|
        fusion_calls += 1
      end
      scene.define_singleton_method(:pbEndScreen) { end_calls += 1 }
      with_singleton_method_stub(
        BattleSpriteLoader, :new, proc { loader }
      ) do
        with_singleton_method_stub(
          PokemonFusionScene, :new, proc { scene }
        ) do
          Ironmon.show_wally_tutorial_fusion(body, head, plan[:fusion])
        end
      end
      fusion = GameData::Species.get(plan[:fusion])
      assert(
        start_arguments[0] == body && start_arguments[1] == head,
        "the scene displays the Ironmon material Pokemon"
      )
      assert(
        start_arguments[2] == fusion.id_number &&
          start_arguments[3] == :DNASPLICERS &&
          start_arguments[4] == :wally_target_sprite,
        "the scene receives the preselected Ironmon fusion result"
      )
      assert(
        loader_arguments == [
          GameData::Species.get(fusion.get_head_species_symbol).id_number,
          GameData::Species.get(fusion.get_body_species_symbol).id_number
        ],
        "the middle sprite is loaded for the planned custom fusion"
      )
      assert(
        fusion_calls == 1 && end_calls == 1,
        "the authored fusion animation completes"
      )
    end
  end

  def self.test_tutorial_result_is_persistent
    with_trainer_policy(Ironmon::Configuration::POLICY_NORMAL_ONLY) do
      pokemon = Ironmon.wally_tutorial_catch_pokemon
      assert(
        GameData::Species.get(pokemon.species).id_number <= NB_POKEMON,
        "Normal Only creates one normal catch"
      )
      assert(
        Ironmon.persistent_trainer_species?(pokemon),
        "the normal catch is marked as Wally's persistent species"
      )
      assert(
        Ironmon.trainer_maturity_exception?(pokemon),
        "Wally's persistent Pokemon keeps normal level-based evolution"
      )
      pokemon.species = :RAICHU
      assert(
        Ironmon.persistent_trainer_species?(pokemon),
        "the persistence marker survives a later evolution"
      )
    end

    with_trainer_policy(Ironmon::Configuration::POLICY_CUSTOM_FUSIONS_ONLY) do
      plan = Ironmon.wally_tutorial_fusion_plan
      body = Ironmon.wally_tutorial_catch_pokemon(plan)
      head = Ironmon.build_wally_story_pokemon(plan[:head], 5)
      result = Ironmon.build_wally_tutorial_fusion(body, head, plan)
      assert(body.species == plan[:body], "the running Pokemon is the body")
      assert(result.species == plan[:fusion], "the fusion keeps the plan result")
      assert(result.level == 7, "the fusion retains the authored average level")
      assert(
        Ironmon.persistent_trainer_species?(result),
        "the custom fusion is marked as Wally's persistent species"
      )
    end
  end

  def self.test_persistent_species_bypasses_battle_remapping
    marked = Ironmon.mark_persistent_trainer_species(
      Pokemon.new(:PIKACHU, 10)
    )
    mappings = []
    mapping = proc do |species, context, level = nil|
      mappings << [species, context, level]
      :RAICHU
    end
    with_trainer_policy(Ironmon::Configuration::POLICY_NORMAL_ONLY) do
      with_singleton_method_stub(Ironmon, :active?, proc { true }) do
        with_singleton_method_stub(
          Ironmon, :trainer_species_for, mapping
        ) do
          party = Ironmon.trainer_battle_party(
            [marked, :RATTATA], [:wally]
          )
          assert(
            party[0].species == :PIKACHU,
            "the persistent Wally Pokemon keeps its story species"
          )
          assert(
            party[1] == :RAICHU && mappings.length == 1,
            "ordinary dynamic trainer Pokemon still use battle remapping"
          )
          assert(
            party[0] != marked,
            "the battle still receives a clone of the persistent Pokemon"
          )
        end
      end
    end
  end

  def self.test_policy_specific_gym_dialogue
    with_trainer_policy(Ironmon::Configuration::POLICY_NORMAL_ONLY) do
      map = load_data("Data/Map061.rxdata")
      assert(
        Ironmon.patch_early_game_map(
          Ironmon::EARLY_GAME_PETALBURG_GYM_MAP_ID, map
        ),
        "Normal Only Petalburg Gym event is patched"
      )
      strings = event_strings(map)
      assert(
        strings.include?("Ironmon.run_wally_gym_sequence"),
        "the Gym delegates to the policy-aware sequence"
      )
      assert(
        strings.none? { |text| text.include?("wally_fuse_pokemon") } &&
          strings.none? { |text| text.include?("DNA Splicers") } &&
          strings.none? { |text| text.include?("you gave me") },
        "Normal Only removes the gift and fusion sequence"
      )
    end

    with_trainer_policy(Ironmon::Configuration::POLICY_MIXED) do
      map = load_data("Data/Map061.rxdata")
      assert(
        Ironmon.patch_early_game_map(
          Ironmon::EARLY_GAME_PETALBURG_GYM_MAP_ID, map
        ),
        "Mixed Petalburg Gym event is patched"
      )
      strings = event_strings(map)
      assert(
        strings.include?("wally_fuse_pokemon()") &&
          strings.any? { |text| text.include?("DNA Splicers") } &&
          strings.any? { |text| text.include?("Mr. Norman!") },
        "Mixed retains the material gift and authored fusion scene"
      )
    end
  end

  def self.run
    with_custom_fusion_pool_fixture do
      with_player_fusion_mapper_fixture do
        test_policy_split
        test_materials_resolve_one_custom_fusion_plan
        test_fusion_scene_uses_ironmon_materials_and_target_sprite
        test_tutorial_result_is_persistent
        test_persistent_species_bypasses_battle_remapping
        test_policy_specific_gym_dialogue
      end
    end
    File.binwrite(OUTPUT_PATH, "Wally tutorial runtime tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonWallyTutorialRuntimeTests.run
