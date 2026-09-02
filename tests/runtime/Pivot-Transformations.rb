module IronmonPivotTransformationRuntimeTests
  OUTPUT_PATH = $ironmon_pivot_transformation_test_output_path.to_s

  def self.assert(condition, message)
    raise "Pivot transformation runtime test failed: #{message}" if !condition
  end

  def self.with_reversed_species(species)
    singleton = class << Ironmon; self; end
    singleton.send(
      :alias_method, :pivot_hp_test_original_paired_species,
      :paired_custom_fusion_species
    )
    singleton.send(:define_method, :paired_custom_fusion_species) do |_source|
      species
    end
    return yield
  ensure
    if singleton &&
       singleton.method_defined?(:pivot_hp_test_original_paired_species)
      singleton.send(
        :alias_method, :paired_custom_fusion_species,
        :pivot_hp_test_original_paired_species
      )
      singleton.send(:remove_method, :pivot_hp_test_original_paired_species)
    end
  end

  def self.caught_fusion_candidate
    candidate = Pokemon.new(:BLISSEY, 50)
    candidate.define_singleton_method(:isFusion?) { true }
    Ironmon.mark_caught_fusion(candidate)
    return candidate
  end

  def self.test_reversal_preserves_remaining_hp_percentage
    candidate = caught_fusion_candidate
    candidate.hp = (candidate.totalhp * 0.37).round
    previous_hp = candidate.hp
    previous_total_hp = candidate.totalhp

    result = with_reversed_species(:DIGLETT) do
      Ironmon.build_reversed_caught_result(candidate)
    end
    expected_hp = (previous_hp.to_f * result.totalhp / previous_total_hp).round
    expected_hp = 1 if expected_hp < 1

    assert(result.totalhp < previous_total_hp,
           "the reversal fixture lowers maximum HP")
    assert(result.hp == expected_hp,
           "reversal preserves the caught Pokemon's remaining HP percentage")
  end

  def self.test_reversal_cannot_faint_a_living_catch
    candidate = caught_fusion_candidate
    candidate.hp = 1

    result = with_reversed_species(:DIGLETT) do
      Ironmon.build_reversed_caught_result(candidate)
    end

    assert(result.hp == 1,
           "a living one-HP catch remains at least one HP after reversal")
    assert(result.able?, "reversal cannot faint a living caught Pokemon")
  end

  def self.run
    test_reversal_preserves_remaining_hp_percentage
    test_reversal_cannot_faint_a_living_catch
    File.binwrite(OUTPUT_PATH, "pivot transformation runtime tests passed\n")
  rescue Exception => exception
    File.binwrite(
      OUTPUT_PATH,
      "#{exception.class}: #{exception.message}\n#{exception.backtrace.join("\n")}\n"
    )
    raise
  end
end

IronmonPivotTransformationRuntimeTests.run
