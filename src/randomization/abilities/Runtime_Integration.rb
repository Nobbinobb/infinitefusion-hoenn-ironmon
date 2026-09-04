#===============================================================================
# Ironmon generated ability engine and event integration
#===============================================================================

class GameData::Species
  alias ironmon_unrandomized_abilities abilities
  alias ironmon_unrandomized_hidden_abilities hidden_abilities

  def abilities
    return ironmon_unrandomized_abilities if
      !Ironmon.ability_randomization_active?
    return Ironmon.ability_generator.fusion_slots_for(self)[:normal] if
      Ironmon.fusion_ability_species?(self)
    return ironmon_unrandomized_abilities if
      !Ironmon.normal_ability_species?(self)
    return Ironmon.ability_generator.slots_for(self)[:normal]
  end

  def hidden_abilities
    return ironmon_unrandomized_hidden_abilities if
      !Ironmon.ability_randomization_active?
    return Ironmon.ability_generator.fusion_slots_for(self)[:hidden] if
      Ironmon.fusion_ability_species?(self)
    return ironmon_unrandomized_hidden_abilities if
      !Ironmon.normal_ability_species?(self)
    return Ironmon.ability_generator.slots_for(self)[:hidden]
  end
end

class Pokemon
  alias ironmon_ability_original_ability_id ability_id
  def ability_id
    if instance_variable_get(:@ironmon_development_ability_override)
      development_ability = instance_variable_get(
        :@ironmon_development_ability
      )
      return development_ability if development_ability &&
        GameData::Ability.exists?(development_ability)
    end
    return ironmon_ability_original_ability_id if
      !Ironmon.ability_randomization_active?
    species_value = species_data
    slots = if Ironmon.fusion_ability_species?(species_value)
              Ironmon.ability_generator.fusion_slots_for(species_value)
            elsif Ironmon.normal_ability_species?(species_value)
              Ironmon.ability_generator.slots_for(species_value)
            else
              {
                :normal => species_value.ironmon_unrandomized_abilities,
                :hidden => species_value.ironmon_unrandomized_hidden_abilities
              }
    end
    index = Ironmon.resolved_ability_index(self, ability_index, slots)
    @ability_index = index if @ability_index != index
    selected = nil
    if index >= 2
      selected = slots[:hidden][index - 2]
    end
    selected ||= slots[:normal][index] || slots[:normal][0]
    return selected
  end

  alias ironmon_ability_original_ability= ability=
  def ability=(value)
    if Ironmon.ability_randomization_active?
      @ability = nil
      return
    end
    self.ironmon_ability_original_ability = value
  end

  alias ironmon_ability_original_species= species=
  def species=(species_id)
    old_ability_index = ability_index
    self.ironmon_ability_original_species = species_id
    Ironmon.normalize_ability_index(self, old_ability_index)
  end


  alias ironmon_ability_original_type1 type1
  def type1
    if Ironmon.ability_randomization_active? && hasAbility?(:MULTITYPE) &&
       species_data.type1 == :NORMAL
      return getHeldPlateType()
    end
    return ironmon_ability_original_type1
  end

  alias ironmon_ability_original_type2 type2
  def type2
    if Ironmon.ability_randomization_active? && hasAbility?(:MULTITYPE) &&
       species_data.type2 == :NORMAL
      return getHeldPlateType()
    end
    return ironmon_ability_original_type2
  end

  alias ironmon_ability_original_checkHPRelatedFormChange checkHPRelatedFormChange
  def checkHPRelatedFormChange
    if Ironmon.ability_randomization_active? && hasAbility?(:SHIELDSDOWN)
      return if $game_temp.in_battle
      if isFusionOf(:MINIOR_M) && @hp <= (@totalhp / 2)
        changeFormSpecies(:MINIOR_M, :MINIOR_C)
      elsif isFusionOf(:MINIOR_C) && @hp > (@totalhp / 2)
        changeFormSpecies(:MINIOR_C, :MINIOR_M)
      end
      return
    end
    ironmon_ability_original_checkHPRelatedFormChange
  end
end

Events.onWildPokemonCreate += proc { |_sender, event|
  if Ironmon.ability_randomization_active? &&
     (player_on_hidden_ability_map || isAlwaysHiddenAbilityMap($game_map.map_id))
    Ironmon.assign_generated_hidden_ability(event[0])
  end
}

Events.onTrainerPartyLoad += proc { |_sender, event|
  trainer = event[0]
  if Ironmon.ability_randomization_active? && trainer && trainer.party
    trainer.party.each { |pokemon| Ironmon.normalize_ability_index(pokemon) }
  end
}

alias ironmon_ability_original_pb_hatch pbHatch
def pbHatch(pokemon)
  result = ironmon_ability_original_pb_hatch(pokemon)
  if Ironmon.ability_randomization_active? && player_on_hidden_ability_map
    Ironmon.assign_generated_hidden_ability(pokemon)
  else
    Ironmon.normalize_ability_index(pokemon)
  end
  return result
end

Ironmon.register_game_load_hook(
  :ability_randomization,
  proc { |_save_data| Ironmon.suspend_ability_randomization },
  proc do |_save_data, _result|
    next if Ironmon.checkpoint_reset_loading?
    Ironmon.ensure_ability_randomization if Ironmon.active?
  end
)
