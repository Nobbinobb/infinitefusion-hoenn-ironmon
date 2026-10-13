#===============================================================================
# Resumable, read-only area fusion mapping work
#===============================================================================

module Ironmon
  TRACKER_AREA_NATIVE_PAGE_LIMIT = 8
  TRACKER_AREA_NATIVE_PAGE_LIFETIME = 30.0

  class TrackerAreaFusionWork
    WORK_SECONDS = 0.004

    def initialize(recipe)
      @recipe = recipe
      @results = {}
      @queued = {}
      @pending = []
      @mapper = nil
      @fiber = nil
      @error = nil
    end

    def generation_profile_id
      return @recipe["generation_profile_id"].to_s
    end

    def results_for(pairs)
      raise_failure if @error
      pairs.each do |pair|
        next if @results.key?(pair) || @queued.key?(pair)
        @queued[pair] = true
        @pending << pair
      end
      advance
      raise_failure if @error
      return nil if pairs.any? { |pair| !@results.key?(pair) }
      return pairs.map { |pair| @results[pair] }
    end

    def advance
      return if @error || @pending.empty? && !@fiber
      @fiber ||= Fiber.new do
        @mapper ||= build_mapper
        while !@pending.empty?
          pair = @pending.shift
          number = @mapper.species_number(pair[0], pair[1])
          Fiber.yield
          @results[pair] = format_result(number)
          @queued.delete(pair)
          Fiber.yield
        end
      end
      deadline = Ironmon.tracker_uptime_seconds + WORK_SECONDS
      @fiber.resume while @fiber.alive? &&
        Ironmon.tracker_uptime_seconds < deadline
      @fiber = nil if !@fiber.alive?
    rescue StandardError => error
      @error = error
      @fiber = nil
      @pending.clear
      @queued.clear
      echoln "Ironmon area fusion preparation failed: #{error.message}"
    end

    def native_results_for(pairs, assignments)
      if !assignments.is_a?(Array) || assignments.length > 50
        raise TrackerLookupError.new(
          "invalid_area_fusion_result", "The tracker fusion page is malformed."
        )
      end
      numbers = {}
      assignments.each do |entry|
        if !entry.is_a?(Hash)
          raise TrackerLookupError.new(
            "invalid_area_fusion_result", "The tracker fusion result is malformed."
          )
        end
        pair = [entry["body_id"], entry["head_id"]]
        number = entry["species_number"]
        if pair.any? { |id| !id.is_a?(Integer) || id <= 0 || id > NB_POKEMON } ||
           !number.is_a?(Integer) ||
           !Ironmon.custom_fusion_pool_service.include_number?(number) ||
           (numbers.key?(pair) && numbers[pair] != number)
          raise TrackerLookupError.new(
            "invalid_area_fusion_result", "The tracker fusion result is invalid."
          )
        end
        next if !pairs.include?(pair)
        numbers[pair] = number
      end
      return nil if pairs.any? { |pair| !@results.key?(pair) && !numbers.key?(pair) }
      pairs.each do |pair|
        @results[pair] ||= format_result(numbers[pair])
        @queued.delete(pair)
      end
      @pending.reject! { |pair| @results.key?(pair) }
      if @pending.empty?
        @fiber = nil
        @mapper = nil
        @queued.clear
      end
      return pairs.map { |pair| @results[pair] }
    end

    def format_result(number)
      body = (number - 1) / NB_POKEMON
      head = number - body * NB_POKEMON
      return {
        "species_id" => "B#{body}H#{head}:0",
        "species_name" => Ironmon.tracker_search_fusion_name(body, head),
        "sprite_path" => Ironmon.tracker_lookup_fusion_sprite_path(body, head)
      }
    end

    def build_mapper
      generator = Ironmon.base_stat_generator_for(
        @recipe["seed"], @recipe["base_stat_source_fingerprint"]
      )
      return PlayerFusionMapper.new(
        @recipe["seed"], Ironmon.custom_fusion_pool_numbers, {}, {},
        generator, @recipe["player_fusion_generator_version"],
        proc { Fiber.yield }
      )
    end

    def raise_failure
      raise TrackerLookupError.new(
        "fusion_lookup_failed",
        "Unable to prepare encounter fusions: #{@error.message}"
      )
    end
  end

  def self.tracker_lookup_fusion_sprite_path(body, head)
    identity = "B#{body}H#{head}".to_sym
    return tracker_sprite_paths[identity] if
      tracker_sprite_paths.key?(identity)
    sprite = $PokemonSystem.alt_sprite_substitutions[[head, body]] if
      $PokemonGlobal && $PokemonSystem
    sprite ||= BattleSpriteLoader.new.select_new_pif_fusion_sprite(head, body)
    tracker_sprite_paths[identity] = tracker_resolved_sprite_path(sprite)
    return tracker_sprite_paths[identity]
  rescue StandardError
    tracker_sprite_paths[identity] = nil
    return nil
  end

  def self.tracker_area_fusion_work(recipe)
    @tracker_area_fusion_work ||= {}
    key = [
      recipe["run_id"], recipe["seed"], recipe["active_run"],
      recipe["generation_profile_id"],
      recipe["player_fusion_generator_version"],
      recipe["base_stat_source_fingerprint"],
      tracker_loaded_recipe?(recipe) ? pivot_state.object_id : nil
    ]
    return @tracker_area_fusion_work[key] if
      @tracker_area_fusion_work.key?(key)
    work = TrackerAreaFusionWork.new(recipe)
    tracker_store_bounded(@tracker_area_fusion_work, key, work, 2)
    return work
  end

  def self.tracker_area_native_page_descriptor(result, payload, recipe,
                                               archived, full_details,
                                               descriptors, discoveries)
    catalog = tracker_pokemon_name_catalog(fusion_only: true)
    return if payload["fusion_name_context_token"] != catalog["context_token"]
    rows = descriptors.map do |descriptor|
      first = descriptor["first"]["metadata"]
      second = descriptor["second"]["metadata"]
      metadata = tracker_area_encounter_fusion_metadata(descriptor, discoveries)
      revealed = tracker_area_encounter_fusion_revealed?(
        descriptor, discoveries, full_details
      )
      row = {
        "map_id" => first["map_id"], "origin" => descriptor["origin"],
        "environment" => metadata["environment"],
        "first_version" => first["version"],
        "first_encounter_type" => first["encounter_type"],
        "first_slot" => first["slot"],
        "second_version" => second["version"],
        "second_encounter_type" => second["encounter_type"],
        "second_slot" => second["slot"],
        "minimum_level" => metadata["minimum_level"],
        "maximum_level" => metadata["maximum_level"],
        "fusion_chance_percent" => metadata["fusion_chance_percent"],
        "encountered" => metadata["encountered"],
        "details_revealed" => revealed
      }
      if revealed
        pair = tracker_area_encounter_fusion_materials([descriptor])[0]
        row["materials"] = { "body_id" => pair[0], "head_id" => pair[1] }
      end
      row
    end
    @tracker_area_native_page_sequence = (@tracker_area_native_page_sequence || 0) + 1
    token = "#{Process.pid}:area:#{@tracker_area_native_page_sequence}"
    pairs = result["required_fusion_materials"].map do |entry|
      [entry["body_id"], entry["head_id"]]
    end
    @tracker_area_native_page_tokens ||= {}
    tracker_store_bounded(@tracker_area_native_page_tokens, token, {
      :recipe => Marshal.load(Marshal.dump(recipe)),
      :session => @game_session_id && @game_session_id.dup,
      :system => $PokemonSystem,
      :pivot_state => tracker_loaded_recipe?(recipe) ? pivot_state : nil,
      :level_scaling => [LEVEL_MULTIPLIER, MAX_SCALED_LEVEL],
      :archived => archived, :full_details => full_details,
      :request => [payload["area_id"].to_s.dup, payload["category"].to_s.dup,
                   payload["encounter_environment"].to_s.dup,
                   payload["offset"].to_i, payload["limit"].to_i,
                   discoveries.keys.map(&:dup).sort],
      :area_name => result["name"].dup, :pairs => pairs,
      :name_context => catalog["context_token"],
      :work => tracker_area_fusion_work(recipe),
      :work_cache => @tracker_area_fusion_work,
      :expires_at => tracker_uptime_seconds + TRACKER_AREA_NATIVE_PAGE_LIFETIME
    }, TRACKER_AREA_NATIVE_PAGE_LIMIT)
    result["fusion_page_token"] = token
    result["fusion_name_context_token"] = catalog["context_token"]
    result["fusion_descriptors"] = rows
  end

  def self.tracker_area_native_page_presentation(payload, recipe, archived, debug)
    token = payload["fusion_page_token"]
    page = @tracker_area_native_page_tokens && @tracker_area_native_page_tokens[token]
    keys = payload["discovery_keys"].is_a?(Array) ? payload["discovery_keys"] : []
    requested = [payload["area_id"].to_s, payload["category"].to_s,
                 payload["encounter_environment"].to_s,
                 payload["offset"].to_i, payload["limit"].to_i,
                 keys.map(&:to_s).uniq.sort]
    full_details = debug || archived
    loaded_pivot = tracker_loaded_recipe?(recipe) ? pivot_state : nil
    if !token.is_a?(String) || !page || page[:recipe] != recipe ||
       page[:session] != @game_session_id ||
       !page[:system].equal?($PokemonSystem) || !page[:pivot_state].equal?(loaded_pivot) ||
       page[:level_scaling] != [LEVEL_MULTIPLIER, MAX_SCALED_LEVEL] ||
       page[:archived] != archived || page[:full_details] != full_details ||
       page[:request] != requested || !page[:work_cache].equal?(@tracker_area_fusion_work) ||
       tracker_uptime_seconds > page[:expires_at]
      raise TrackerLookupError.new(
        "area_page_changed", "The encounter page changed. Request its current descriptors."
      )
    end
    catalog = tracker_pokemon_name_catalog(fusion_only: true)
    if catalog["context_token"] != page[:name_context]
      raise TrackerLookupError.new(
        "area_page_changed", "The encounter name context changed. Request its current descriptors."
      )
    end
    assignments = payload["fusion_results"]
    if !assignments.is_a?(Array) || assignments.length > 50 || assignments.any? do |entry|
      !entry.is_a?(Hash) || !page[:pairs].include?([entry["body_id"], entry["head_id"]])
    end
      raise TrackerLookupError.new(
        "invalid_area_fusion_result", "The tracker presentation contains an unauthorized assignment."
      )
    end
    results = page[:work].native_results_for(page[:pairs], assignments)
    if !results
      raise TrackerLookupError.new(
        "invalid_area_fusion_result", "The tracker presentation assignments are incomplete."
      )
    end
    presentations = page[:pairs].each_with_index.map do |pair, index|
      identity = /\AB(\d+)H(\d+):0\z/.match(results[index]["species_id"])
      {
        "body_id" => pair[0], "head_id" => pair[1],
        "species_number" => identity[1].to_i * NB_POKEMON + identity[2].to_i,
        "sprite_path" => results[index]["sprite_path"]
      }
    end
    @tracker_area_native_page_tokens.delete(token)
    return {
      "area_id" => page[:request][0], "name" => page[:area_name],
      "category" => page[:request][1], "pending" => false,
      "overworld_encounters" => recipe["overworld_encounters"],
      "fusion_page_token" => token,
      "fusion_name_context_token" => page[:name_context],
      "fusion_presentations" => presentations
    }
  end

  def self.update_tracker_area_fusions
    return if !tracker_obtainability_background_safe?
    if @tracker_area_fusion_work
      @tracker_area_fusion_work.each_value do |work|
        with_generation_profile(work.generation_profile_id) { work.advance }
      end
    end
    if @tracker_wild_occurrence_work
      @tracker_wild_occurrence_work.each_value do |work|
        with_generation_profile(work.generation_profile_id) { work.advance }
      end
    end
  end
end
