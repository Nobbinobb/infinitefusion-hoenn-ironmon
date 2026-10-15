module Encryption
  IRONMON_XOR_WORD_BYTES = 8
  IRONMON_XOR_CHUNK_BYTES = 32_768
  IRONMON_XOR_WORD_FORMAT = "Q<*".freeze

  class << self
    alias ironmon_original_xor xor

    def xor(data)
      key = ENCRYPTION_KEY
      if !data.instance_of?(String) || data.bytesize < IRONMON_XOR_WORD_BYTES ||
         !key.instance_of?(Array) || key.empty? || key.length % IRONMON_XOR_WORD_BYTES != 0 ||
         !key.all? { |byte| byte.is_a?(Integer) && byte.between?(0, 255) }
        return ironmon_original_xor(data)
      end
      words = key.pack("C*").unpack(IRONMON_XOR_WORD_FORMAT)
      result = "".b
      offset = 0
      complete = data.bytesize - data.bytesize % IRONMON_XOR_WORD_BYTES
      while offset < complete
        length = [IRONMON_XOR_CHUNK_BYTES, complete - offset].min
        values = data.byteslice(offset, length).unpack(IRONMON_XOR_WORD_FORMAT)
        first = offset / IRONMON_XOR_WORD_BYTES
        values.each_index do |index|
          values[index] ^= words[(first + index) % words.length]
        end
        result << values.pack(IRONMON_XOR_WORD_FORMAT)
        offset += length
      end
      while offset < data.bytesize
        result << (data.getbyte(offset) ^ key[offset % key.length])
        offset += 1
      end
      return result
    end
  end
end
