#===============================================================================
# Resumable, read-only area fusion mapping work
#===============================================================================

module Ironmon
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
        proc { Fiber.yield }, true
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

  def self.update_tracker_area_fusions
    return if !tracker_obtainability_background_safe?
    @tracker_area_fusion_work.each_value(&:advance) if @tracker_area_fusion_work
    @tracker_wild_occurrence_work.each_value(&:advance) if @tracker_wild_occurrence_work
  end
end
