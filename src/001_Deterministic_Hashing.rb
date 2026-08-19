#===============================================================================
# Ironmon deterministic 64-bit hashing primitives
#===============================================================================

module Ironmon
  FNV1A_64_OFFSET_BASIS = 14_695_981_039_346_656_037
  FNV1A_64_PRIME = 1_099_511_628_211
  FNV1A_64_MASK = 0xFFFFFFFFFFFFFFFF

  def self.fnv1a_64(input, initial_value = FNV1A_64_OFFSET_BASIS)
    value = initial_value
    input.each_byte do |byte|
      value ^= byte
      value = (value * FNV1A_64_PRIME) & FNV1A_64_MASK
    end
    return value
  end

  def self.fnv1a_64_joined(parts, separator = "|")
    return fnv1a_64(parts.join(separator))
  end

  def self.fnv1a_64_entries(entries, initial_value = FNV1A_64_OFFSET_BASIS)
    value = initial_value
    entries.each do |entry|
      text = block_given? ? yield(entry) : entry.to_s
      value = fnv1a_64(text, value)
      value = (value * FNV1A_64_PRIME) & FNV1A_64_MASK
    end
    return value
  end

  def self.fnv1a_64_fingerprint(entries, &canonicalizer)
    value = fnv1a_64_entries(
      entries, FNV1A_64_OFFSET_BASIS, &canonicalizer
    )
    return sprintf("%016x", value)
  end
end
