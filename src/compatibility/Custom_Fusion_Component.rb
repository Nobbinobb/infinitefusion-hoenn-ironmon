module Ironmon
  module CustomFusionComponent
    MAGIC = "IFCFPOOL".b.freeze
    SCHEMA_VERSION = 1
    HEADER_BYTES = 20

    def self.encode(normal_species_count, identities)
      validate_species_count(normal_species_count)
      raise ArgumentError, "custom fusion identities must be an array" if !identities.is_a?(Array)
      bit_count = normal_species_count * normal_species_count
      bitset = "\0" * ((bit_count + 7) / 8)
      seen = {}
      identities.each do |identity|
        body_id, head_id = material_ids(identity, normal_species_count)
        index = bit_index(body_id, head_id, normal_species_count)
        raise ArgumentError, "custom fusion identities must be unique" if seen[index]
        seen[index] = true
        byte_index = index >> 3
        bitset.setbyte(byte_index, bitset.getbyte(byte_index) | (1 << (index & 7)))
      end
      if seen.length.odd?
        raise ArgumentError, "custom fusion eligibility count must be even"
      end
      header = [
        MAGIC, SCHEMA_VERSION, normal_species_count,
        seen.length, bitset.bytesize
      ].pack("a8vvVV")
      return header + bitset
    end

    def self.decode(bytes)
      raise ArgumentError, "custom fusion component must be binary data" if !bytes.is_a?(String)
      raise ArgumentError, "custom fusion component header is truncated" if bytes.bytesize < HEADER_BYTES
      magic, schema_version, normal_species_count, eligible_count,
        bitset_bytes = bytes.unpack("a8vvVV")
      raise ArgumentError, "custom fusion component magic is invalid" if magic != MAGIC
      if schema_version != SCHEMA_VERSION
        raise ArgumentError, "unsupported custom fusion component schema #{schema_version}"
      end
      validate_species_count(normal_species_count)
      expected_bitset_bytes = (
        (normal_species_count * normal_species_count) + 7
      ) / 8
      if bitset_bytes != expected_bitset_bytes ||
         bytes.bytesize != HEADER_BYTES + bitset_bytes
        raise ArgumentError, "custom fusion component length is invalid"
      end
      if eligible_count.odd?
        raise ArgumentError, "custom fusion eligibility count must be even"
      end
      bitset = bytes.byteslice(HEADER_BYTES, bitset_bytes)
      used_bits = normal_species_count * normal_species_count
      remainder = used_bits & 7
      if remainder != 0 &&
         (bitset.getbyte(bitset.bytesize - 1) & (0xFF << remainder)) != 0
        raise ArgumentError, "custom fusion component padding bits must be zero"
      end
      actual_count = bitset.bytes.inject(0) do |count, byte|
        count + byte.to_s(2).count("1")
      end
      if actual_count != eligible_count
        raise ArgumentError, "custom fusion eligibility count does not match its bitset"
      end
      return {
        "schema_version" => schema_version,
        "normal_species_count" => normal_species_count,
        "eligible_count" => eligible_count,
        "bitset" => bitset
      }
    end

    def self.identities(bytes)
      component = decode(bytes)
      normal_species_count = component["normal_species_count"]
      bitset = component["bitset"]
      result = []
      (1..normal_species_count).each do |body_id|
        (1..normal_species_count).each do |head_id|
          index = bit_index(body_id, head_id, normal_species_count)
          byte = bitset.getbyte(index >> 3)
          next if (byte & (1 << (index & 7))) == 0
          result << "B#{body_id}H#{head_id}".to_sym
        end
      end
      return result
    end

    def self.include?(bytes, body_id, head_id)
      component = decode(bytes)
      normal_species_count = component["normal_species_count"]
      material_ids("B#{body_id}H#{head_id}", normal_species_count)
      index = bit_index(body_id, head_id, normal_species_count)
      byte = component["bitset"].getbyte(index >> 3)
      return (byte & (1 << (index & 7))) != 0
    end

    def self.bit_index(body_id, head_id, normal_species_count)
      return ((body_id - 1) * normal_species_count) + head_id - 1
    end

    def self.material_ids(identity, normal_species_count)
      match = /\AB(\d+)H(\d+)\z/.match(identity.to_s)
      raise ArgumentError, "invalid custom fusion identity #{identity.inspect}" if !match
      body_id = match[1].to_i
      head_id = match[2].to_i
      if body_id < 1 || body_id > normal_species_count ||
         head_id < 1 || head_id > normal_species_count
        raise ArgumentError, "custom fusion materials are outside the normal species catalog"
      end
      return [body_id, head_id]
    end

    def self.validate_species_count(normal_species_count)
      if !normal_species_count.is_a?(Integer) ||
         normal_species_count <= 0 || normal_species_count > 65_535
        raise ArgumentError, "normal species count is outside the component format"
      end
    end
  end
end
