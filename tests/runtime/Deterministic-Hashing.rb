module IronmonDeterministicHashingRuntimeTests
  OUTPUT_PATH = $ironmon_deterministic_hashing_test_output_path.to_s

  def self.assert(condition, message)
    raise "Deterministic-hashing test failed: #{message}" if !condition
  end

  def self.hex(value)
    return sprintf("%016x", value)
  end

  def self.run
    assert(
      hex(Ironmon.fnv1a_64("")) == "cbf29ce484222325",
      "the empty-input FNV-1a vector is stable"
    )
    assert(
      hex(Ironmon.fnv1a_64("hello")) == "a430d84680aabd0b",
      "the standard hello FNV-1a vector is stable"
    )
    assert(
      hex(Ironmon.fnv1a_64_joined([3, 42, "ability", "PIKACHU", 1])) ==
        "3044b7c30eb4296e",
      "pipe-joined deterministic inputs retain their byte encoding"
    )
    entries = ["a", "bc", 17]
    assert(
      hex(Ironmon.fnv1a_64_entries(entries)) == "4af868bb58c5f6c7",
      "null-delimited fingerprint entries retain their byte encoding"
    )
    assert(
      Ironmon.fnv1a_64_fingerprint(entries) == "4af868bb58c5f6c7",
      "fingerprints remain fixed-width lowercase hexadecimal"
    )
    prefix = Ironmon.fnv1a_64("prefix")
    assert(
      hex(Ironmon.fnv1a_64("tail", prefix)) == "9a3cc50d24767577",
      "incremental hashing preserves the supplied state"
    )
    assert(
      Ironmon.species_pool_fingerprint(entries) == "4af868bb58c5f6c7" &&
        Ironmon.item_fingerprint(entries) == "4af868bb58c5f6c7" &&
        Ironmon.move_access_fingerprint(entries) == "4af868bb58c5f6c7",
      "generator fingerprint helpers share the common entry primitive"
    )
    File.binwrite(OUTPUT_PATH, "deterministic-hashing tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonDeterministicHashingRuntimeTests.run
