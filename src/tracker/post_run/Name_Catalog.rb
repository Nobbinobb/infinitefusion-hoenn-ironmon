#===============================================================================
# Compact game-owned name inputs for tracker search and area presentation
#===============================================================================
module Ironmon
  TRACKER_NAME_CATALOG_SCHEMA = 1
  TRACKER_NAME_CATALOG_PROFILE_LIMIT = 4

  def self.tracker_pokemon_name_catalog(normal_only: false, fusion_only: false)
    prefixes, suffixes = tracker_search_fusion_name_parts
    names = fusion_only ? nil : normal_species_pool.map do |id|
      [id.to_s, tracker_search_species_name(id)]
    end
    pool_fingerprint = !fusion_only && !normal_only ?
      custom_fusion_pool_info[:fingerprint] : nil
    context = [active_generation_profile_id,
               $PokemonSystem && $PokemonSystem.language, names,
               prefixes, suffixes, pool_fingerprint]
    @tracker_name_catalogs ||= {}
    profile_id = active_generation_profile_id
    bucket = @tracker_name_catalogs[profile_id]
    if !bucket
      @tracker_name_catalogs.delete(@tracker_name_catalogs.keys.first) if
        @tracker_name_catalogs.length >= TRACKER_NAME_CATALOG_PROFILE_LIMIT
      bucket = {}
      @tracker_name_catalogs[profile_id] = bucket
    end
    key = [normal_only, fusion_only]
    cached = bucket[key]
    if cached && cached[:context] == context
      return cached[:payload] if fusion_only
      fusion_cached = bucket[[false, true]]
      return cached[:payload] if fusion_cached &&
        cached[:payload]["fusion_name_context_token"] == fusion_cached[:payload]["context_token"]
    end
    @tracker_name_catalog_sequence = (@tracker_name_catalog_sequence || 0) + 1
    token = "#{game_session_id}:#{TRACKER_NAME_CATALOG_SCHEMA}:#{@tracker_name_catalog_sequence}"
    prefixes = Array.new(NB_POKEMON + 1) { |index| prefixes[index] && prefixes[index].dup }
    suffixes = Array.new(NB_POKEMON + 1) { |index| suffixes[index] && suffixes[index].dup }
    result = {
      "schema_version" => TRACKER_NAME_CATALOG_SCHEMA,
      "generation_profile_id" => active_generation_profile_id,
      "context_token" => token,
      "normal_species_count" => NB_POKEMON,
      "prefixes" => prefixes,
      "suffixes" => suffixes,
      "capitalized_suffixes" => suffixes.map { |suffix| suffix && suffix.capitalize }
    }
    if !fusion_only
      result["fusion_name_context_token"] =
        tracker_pokemon_name_catalog(fusion_only: true)["context_token"]
      result["lowercase_prefixes"] = prefixes.map { |entry| entry && entry.downcase }
      result["lowercase_trimmed_prefixes"] = prefixes.map { |entry| entry && entry[0..-2].downcase }
      result["lowercase_suffixes"] = suffixes.map { |entry| entry && entry.downcase }
      result["lowercase_capitalized_suffixes"] = suffixes.map { |entry| entry && entry.capitalize.downcase }
      available_names = names.select { |_id, name| name }
      result["normalized_normal_names"] = available_names.map { |_id, name| name.downcase }
      result["normalized_normal_search_texts"] = available_names.map { |id, name| "#{name} #{id}".downcase }
      result["normal_species"] = names.filter_map do |id, name|
        next if !name
        { "species_id" => "#{id}:0", "species_name" => name.dup, "fusion" => false }
      end
    end
    if !fusion_only && !normal_only
      result["packed_custom_fusion_pool"] =
        [fusion_predecessor_index.packed_pool_membership].pack("m0")
    end
    bucket[key] = { :context => tracker_copy_live_summary(context), :payload => result }
    return result
  end

  def self.tracker_pokemon_search_assignment(payload, query, normal_only)
    assignment = payload["name_search_assignment"]
    return nil if !assignment.is_a?(Hash)
    catalog = tracker_pokemon_name_catalog(normal_only: normal_only)
    if assignment["context_token"] != catalog["context_token"]
      raise TrackerLookupError.new("name_catalog_changed", "The Pokemon name catalog changed. Search again.")
    end
    ids = assignment["species_ids"]
    total = assignment["total"]
    limit = payload["limit"].to_i
    limit = 20 if limit == 0
    if !ids.is_a?(Array) || ids.length > limit || !total.is_a?(Integer) ||
       total < ids.length || total < 0 || ids.uniq.length != ids.length
      raise TrackerLookupError.new("invalid_page", "The tracker search page is malformed.")
    end
    normals = normal_species_pool
    matches = ids.map do |id|
      if !id.is_a?(String) || !id.end_with?(":0")
        raise TrackerLookupError.new("pokemon_not_found", "The selected Pokemon is not available in this run.")
      end
      identity = id[0..-3]
      fusion = /\AB(\d+)H(\d+)\z/.match(identity)
      if fusion
        body = fusion[1].to_i
        head = fusion[2].to_i
        if normal_only || body < 1 || body > NB_POKEMON || head < 1 ||
           head > NB_POKEMON || identity != "B#{body}H#{head}" ||
           !custom_fusion_pool_service.include_number?(body * NB_POKEMON + head)
          raise TrackerLookupError.new("pokemon_not_found", "The selected Pokemon is not available in this run.")
        end
      elsif !normals.include?(identity.to_sym)
        raise TrackerLookupError.new("pokemon_not_found", "The selected Pokemon is not available in this run.")
      end
      name = tracker_search_species_name(identity.to_sym)
      searchable = fusion ? name.downcase : "#{name} #{identity}".downcase
      identity_query = /\A(?:b\d*(?:h\d*)?|\d+h\d*|h\d+|\d+)\z/.match?(query)
      if !searchable.include?(query) && !(fusion && identity_query && identity.downcase.include?(query))
        raise TrackerLookupError.new("invalid_query", "The selected Pokemon does not match the search.")
      end
      { "species_id" => id, "species_name" => name, "fusion" => !!fusion }
    end
    return { "matches" => matches, "total" => total }
  end
end
