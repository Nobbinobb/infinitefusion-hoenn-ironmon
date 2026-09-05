module IronmonFavoriteSearchTests
  class Connection < Ironmon::TrackerConnection
    attr_reader :response

    def queue_message(message)
      @response = message
    end
  end

  def self.assert(condition, message)
    @assertions += 1
    raise "Favorite search test failed: #{message}" if !condition
  end

  def self.search(connection, query, offset = 0, run_id = nil)
    connection.handle_request({
      "request_id" => "favorite-search-test",
      "command" => "favorite_pokemon_search",
      "run_id" => run_id,
      "payload" => {
        "query" => query, "offset" => offset, "limit" => 10,
        "normal_only" => false
      }
    })
    response = connection.response
    assert(response["success"], "#{query.inspect}: #{response["error"].inspect}")
    matches = response["payload"]["matches"]
    assert(matches.length <= 10, "suggestions respect the requested limit")
    assert(matches.all? { |match| match["fusion"] == false }, "only normal species are returned")
    assert(matches.none? { |match| match.key?("obtainability_status") }, "favorites omit run-specific obtainability")
    return response["payload"]
  end

  def self.run
    @assertions = 0
    connection = Connection.new
    partial = search(connection, "Lat")
    assert(partial["matches"].any? { |match| match["species_id"] == "LATIOS:0" }, "partial names find Latios without a loaded save")
    exact = search(connection, "Latios", 0, "favorite-test-run")
    assert(exact["matches"].any? { |match| match["species_id"] == "LATIOS:0" }, "search also accepts a run identifier")
    broad = search(connection, "a")
    assert(broad["matches"].length == 10 && broad["total"] > 10, "broad searches have a bounded first page")
    next_page = search(connection, "a", 10)
    first_ids = broad["matches"].map { |match| match["species_id"] }
    assert(next_page["matches"].none? { |match| first_ids.include?(match["species_id"]) }, "pagination advances past the first page")
    empty = search(connection, "no-such-pokemon-favorite-test")
    assert(empty["matches"].empty? && empty["total"] == 0, "unmatched names return an empty successful result")
    report = { "passed" => true, "assertions" => @assertions, "partial_matches" => partial["matches"] }
    File.binwrite($ironmon_favorite_search_output_path, Ironmon.tracker_json_generate(report))
  end
end

IronmonFavoriteSearchTests.run
