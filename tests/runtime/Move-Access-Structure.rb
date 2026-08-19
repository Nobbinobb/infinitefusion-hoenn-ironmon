module IronmonMoveAccessStructureRuntimeTests
  OUTPUT_PATH = $ironmon_move_access_structure_test_output_path.to_s

  def self.assert(condition, message)
    raise "Move-access structure test failed: #{message}" if !condition
  end

  def self.assert_source(callable, expected_file, description)
    location = callable.source_location
    assert(location, "#{description} exposes its source location")
    assert(
      File.basename(location[0]) == expected_file,
      "#{description} is defined by #{expected_file}"
    )
  end

  def self.run
    assert_source(
      Ironmon::MoveAccessGenerator.instance_method(:moves_for),
      "003_Move_Access_Randomization.rb",
      "move-access generation"
    )
    assert_source(
      Ironmon.method(:move_access_source_fingerprint),
      "003_Move_Access_Catalogs.rb",
      "move-access source catalogs"
    )
    assert_source(
      Ironmon.method(:ensure_move_access_randomization),
      "003_Move_Access_Readiness.rb",
      "move-access readiness"
    )
    assert_source(
      Ironmon.method(:machine_channel_for_item),
      "003_Move_Access_Runtime_Integration.rb",
      "move-access runtime helpers"
    )
    assert_source(
      GameData::Species.instance_method(:moves),
      "003_Move_Access_Runtime_Integration.rb",
      "move-access engine integration"
    )
    File.binwrite(OUTPUT_PATH, "move-access structure tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonMoveAccessStructureRuntimeTests.run
