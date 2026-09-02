#===============================================================================
# Ironmon generator metadata primitives
#===============================================================================

module Ironmon
  def self.generator_metadata_mismatch(checks)
    mismatch = checks.find do |_label, actual, expected|
      actual != expected
    end
    return mismatch ? mismatch[0] : nil
  end

  def self.generator_metadata_matches?(checks)
    return generator_metadata_mismatch(checks).nil?
  end

  def self.record_generator_metadata(values)
    raise "run metadata is unavailable" if !$PokemonGlobal
    values.each do |field_name, value|
      $PokemonGlobal.public_send("#{field_name}=", value)
    end
    return true
  end
end
