#===============================================================================
# Ironmon generated-evolution post-run metrics
#===============================================================================

module Ironmon
  EVOLUTION_METRICS_SCHEMA_VERSION = 1

  def self.reset_evolution_metrics
    return if !$PokemonGlobal
    $PokemonGlobal.ironmon_evolution_metrics = {
      "schema_version" => EVOLUTION_METRICS_SCHEMA_VERSION,
      "next_id" => 1,
      "events" => []
    }
  end

  def self.evolution_metrics
    return nil if !active? || !$PokemonGlobal
    metrics = $PokemonGlobal.ironmon_evolution_metrics
    if !metrics.is_a?(Hash) ||
       metrics["schema_version"] != EVOLUTION_METRICS_SCHEMA_VERSION
      reset_evolution_metrics
      metrics = $PokemonGlobal.ironmon_evolution_metrics
    end
    return metrics
  end

  def self.record_generated_evolution_offer(pokemon, pending, branch)
    metrics = evolution_metrics
    return if !metrics || !pokemon || !pending || !branch
    event_id = metrics["next_id"]
    metrics["next_id"] = event_id + 1
    pending[:metric_id] = event_id
    source = pokemon.species_data
    target = GameData::Species.get(branch[:target_id])
    metrics["events"] << {
      "event_id" => event_id,
      "pokemon_id" => pokemon.personalID.to_s,
      "source_kind" => pending[:source_kind].to_s,
      "source_species_id" => source.id.to_s,
      "source_species_name" => source.name,
      "target_species_id" => target.id.to_s,
      "target_species_name" => target.name,
      "level" => pokemon.level,
      "activation_context" => pending[:activation_context].to_s,
      "effective_method" => pending[:method] ? pending[:method].to_s : nil,
      "effective_parameter" => evolution_metric_parameter(pending[:parameter]),
      "component_side" => pending[:component_side] ?
        pending[:component_side].to_s : nil,
      "forced" => pending[:forced] == true,
      "fallback" => branch[:fallback] == true,
      "source_bst" => branch[:source_bst],
      "reference_bst" => branch[:reference_bst],
      "target_bst" => branch[:target_bst],
      "outcome" => "offered",
      "duplicate_target_species_id" => nil,
      "duplicate_target_species_name" => nil
    }
  rescue Exception => e
    echoln "Ironmon evolution offer metric failed safely: #{e.message}"
  end

  def self.record_generated_evolution_outcome(pending, outcome)
    event = evolution_metric_event(pending)
    return if !event
    event["outcome"] = outcome.to_s
  rescue Exception => e
    echoln "Ironmon evolution outcome metric failed safely: #{e.message}"
  end

  def self.record_generated_evolution_duplicate(pending, branch)
    event = evolution_metric_event(pending)
    return if !event || !branch
    target = GameData::Species.get(branch[:target_id])
    event["duplicate_target_species_id"] = target.id.to_s
    event["duplicate_target_species_name"] = target.name
  rescue Exception => e
    echoln "Ironmon evolution duplicate metric failed safely: #{e.message}"
  end

  def self.evolution_metrics_snapshot
    metrics = evolution_metrics
    return nil if !metrics || !evolution_randomization_active?
    return {
      "schema_version" => EVOLUTION_METRICS_SCHEMA_VERSION,
      "events" => metrics["events"].map { |event| event.dup }
    }
  rescue Exception => e
    echoln "Ironmon evolution metric finalization failed safely: #{e.message}"
    return nil
  end

  def self.evolution_metric_event(pending)
    metrics = evolution_metrics
    return nil if !metrics || !pending || !pending[:metric_id]
    return metrics["events"].find do |event|
      event["event_id"] == pending[:metric_id]
    end
  end

  def self.evolution_metric_parameter(parameter)
    return nil if parameter.nil?
    return parameter.id.to_s if parameter.respond_to?(:id)
    return parameter.map { |entry| evolution_metric_parameter(entry) }.join("|") if
      parameter.is_a?(Array)
    return parameter.to_s
  end
end

class PokemonEvolutionScene
  alias ironmon_evolution_metrics_original_pb_evolution pbEvolution

  def pbEvolution(cancancel = true, reversing = false)
    pending = Ironmon.pending_generated_evolution(@pokemon)
    result = ironmon_evolution_metrics_original_pb_evolution(
      cancancel, reversing
    )
    current = Ironmon.pending_generated_evolution(@pokemon)
    if pending && current && pending[:metric_id] == current[:metric_id]
      Ironmon.record_generated_evolution_outcome(pending, "cancelled")
      Ironmon.clear_pending_generated_evolution(@pokemon)
    end
    return result
  end
end
