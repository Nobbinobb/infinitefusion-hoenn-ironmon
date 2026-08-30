#===============================================================================
# Ironmon gameplay hooks for authoritative area progress
#===============================================================================

module Ironmon
  class AreaEncounterEntry < Array
    attr_reader :area_entry_id

    def initialize(values, area_entry_id)
      super(values)
      @area_entry_id = area_entry_id
    end

    def [](index, *arguments)
      Ironmon.note_area_encounter_entry(@area_entry_id) if index == 1
      return super(index, *arguments)
    end
  end

  def self.current_area_event_reference(explicit_event_id = nil,
                                        explicit_map_id = nil)
    event_id = explicit_event_id.to_i
    map_id = explicit_map_id.to_i
    if event_id < 1
      interpreter = pbMapInterpreter
      event = interpreter ? interpreter.get_character(0) : nil
      event_id = event.id.to_i if event
      if event && event.respond_to?(:map) && event.map
        map_id = event.map.map_id.to_i
      end
    end
    map_id = $game_map.map_id.to_i if map_id < 1 && $game_map
    return nil if map_id < 1 || event_id < 1
    return [map_id, event_id]
  end

  def self.current_area_event_entry_id(category, explicit_event_id = nil,
                                       explicit_map_id = nil)
    reference = current_area_event_reference(
      explicit_event_id, explicit_map_id
    )
    return nil if !reference
    return tracker_area_event_entry_id(
      category, reference[0], reference[1]
    )
  end

  def self.begin_area_trainer_event(entry_id)
    @pending_area_trainer_entries ||= []
    @current_area_trainer_entry = entry_id
  end

  def self.queue_area_trainer_entries(entry_ids)
    @pending_area_trainer_entries ||= []
    @pending_area_trainer_entries.concat(entry_ids.compact)
    @pending_area_trainer_entries.uniq!
  end

  def self.finish_area_trainer_event(won, waiting)
    entry_id = @current_area_trainer_entry
    @current_area_trainer_entry = nil
    if won
      entries = (@pending_area_trainer_entries || []) + [entry_id]
      publish_area_discoveries("trainer", entries)
      @pending_area_trainer_entries = []
    elsif waiting && entry_id
      @pending_area_trainer_entries ||= []
      @pending_area_trainer_entries << entry_id
      @pending_area_trainer_entries.uniq!
    else
      @pending_area_trainer_entries = []
    end
  end

  def self.cancel_area_trainer_event
    @current_area_trainer_entry = nil
    @pending_area_trainer_entries = []
  end

  def self.begin_area_item_event(entry_id)
    @current_area_item_entry = entry_id
    @current_area_item_recorded = false
  end

  def self.record_current_area_item(item)
    return false if !@current_area_item_entry
    recorded = record_collected_area_item(@current_area_item_entry)
    @current_area_item_recorded = true if recorded
    return recorded
  end

  def self.finish_area_item_event(result, authored_item)
    if result && @current_area_item_entry && !@current_area_item_recorded
      replacement = hm_replacement_item(authored_item) if
        respond_to?(:hm_replacement_item)
      record_current_area_item(replacement || authored_item)
    end
    @current_area_item_entry = nil
    @current_area_item_recorded = false
    return result
  end

  def self.cancel_area_item_event
    @current_area_item_entry = nil
    @current_area_item_recorded = false
  end

  def self.tag_area_encounter_tables(encounters, map_id)
    mode = encounters.getEncounterMode()
    version = $PokemonGlobal ? $PokemonGlobal.encounter_version : 0
    data = mode.get(map_id, version)
    data = GameData::Encounter.get(map_id, version) if !data
    return false if !data
    tables = encounters.instance_variable_get(:@encounter_tables)
    return false if !tables.is_a?(Hash)
    tables.each do |encounter_type, entries|
      entries.each_with_index do |entry, slot|
        entry_id = "encounter:#{data.map}:#{data.version}:#{encounter_type}:#{slot + 1}"
        entries[slot] = AreaEncounterEntry.new(entry, entry_id)
      end
    end
    return true
  end

  def self.begin_area_encounter_sequence
    @pending_area_encounter_entries = []
    @area_encounter_sequence_active = true
  end

  def self.area_encounter_sequence_active?
    return @area_encounter_sequence_active == true
  end

  def self.begin_area_encounter_selection
    @area_encounter_selection_active = area_encounter_sequence_active?
    @selected_area_encounter_entry = nil
  end

  def self.note_area_encounter_entry(entry_id)
    return if !@area_encounter_selection_active
    @selected_area_encounter_entry = entry_id
  end

  def self.finish_area_encounter_selection(result)
    if result && @selected_area_encounter_entry
      @pending_area_encounter_entries ||= []
      @pending_area_encounter_entries << @selected_area_encounter_entry
      @pending_area_encounter_entries.uniq!
    end
    @area_encounter_selection_active = false
    @selected_area_encounter_entry = nil
    return result
  end

  def self.commit_area_encounter_sequence
    entries = @pending_area_encounter_entries || []
    record_encountered_area_slots(entries)
    clear_area_encounter_sequence
    return !entries.empty?
  end

  def self.clear_area_encounter_sequence
    @pending_area_encounter_entries = []
    @area_encounter_sequence_active = false
    @area_encounter_selection_active = false
    @selected_area_encounter_entry = nil
  end
end
