#===============================================================================
# Cosmetic-only game integration, safe restoration, and progression awards
#===============================================================================
class PokemonGlobalMetadata
  attr_accessor :ironmon_cosmetic_attempt_id
  attr_accessor :ironmon_cosmetic_starting_badges
  attr_accessor :ironmon_wardrobe_confirmed
end

module Ironmon
  module Cosmetics
    class << self
      attr_accessor :profile_directory, :battle_source
    end

    def self.profile
      directory = @profile_directory || File.join(SaveData::SAVE_DIR, "IronmonCosmetics")
      return Profile.new(directory)
    end

    def self.catalog(refresh = false)
      @catalog = nil if refresh
      return @catalog ||= Catalog.new
    end

    def self.safely
      return yield
    rescue *RECOVERABLE_ERRORS => error
      if @last_error != error.message
        echoln "Ironmon optional wardrobe: #{error.message}"
        @last_error = error.message
      end
      return nil
    end

    def self.appearance_key(slot = nil)
      slot ||= Ironmon.instance_variable_get(:@reset_save_slot) if Ironmon.checkpoint_reset_loading?
      slot ||= $Trainer.save_slot if $Trainer && $Trainer.respond_to?(:save_slot)
      return "slot:#{slot}" if slot && !slot.to_s.empty?
      return "unsaved:#{$PokemonGlobal ? $PokemonGlobal.ironmon_checkpoint_id : 0}"
    end

    def self.capture
      appearance = {}
      APPEARANCE_FIELDS.each do |field|
        appearance[field] = $Trainer.public_send(field)
      end
      DYE_FIELDS.each { |field| appearance[field] = appearance[field].to_i % 360 }
      appearance["skin_tone"] = [[appearance["skin_tone"].to_i, 1].max, 6].min
      ["hat", "hat2"].each { |field| appearance[field] = nil if appearance[field].to_s.empty? }
      return appearance
    end

    def self.apply(appearance)
      return false if !appearance.is_a?(Hash) || !$Trainer
      available = catalog
      APPEARANCE_FIELDS.each do |field|
        next if !appearance.has_key?(field)
        value = appearance[field]
        next if field == "clothes" && !available.valid?("clothes", value)
        if field == "hair"
          version, style = hair_parts(value)
          entry = available.find("hair", style)
          next if !entry || !entry["variants"].include?(version)
        end
        value = nil if ["hat", "hat2"].include?(field) && !available.valid?("hat", value)
        value = value.to_i % 360 if DYE_FIELDS.include?(field)
        next if field == "skin_tone" && !(1..6).include?(value.to_i)
        # Base-game setters refresh sprites immediately, including during a
        # checkpoint load before the new scene exists. Apply this whitelist as
        # one cosmetic update and refresh only after all layers are consistent.
        $Trainer.instance_variable_set("@#{field}", value)
      end
      clothes_dyes = $Trainer.instance_variable_get(:@dyed_clothes) || {}
      clothes_dyes[$Trainer.clothes] = $Trainer.clothes_color
      $Trainer.instance_variable_set(:@dyed_clothes, clothes_dyes)
      hat_dyes = $Trainer.instance_variable_get(:@dyed_hats) || {}
      hat_dyes[$Trainer.hat] = $Trainer.hat_color if $Trainer.hat
      hat_dyes[$Trainer.hat2] = $Trainer.hat2_color if $Trainer.hat2
      $Trainer.instance_variable_set(:@dyed_hats, hat_dyes)
      refresh_sprite
      return true
    end

    def self.refresh_sprite
      return if !$scene || !$scene.respond_to?(:spritesetGlobal)
      sprites = $scene.spritesetGlobal
      return if !sprites || !sprites.respond_to?(:playersprite) || !sprites.playersprite
      sprites.playersprite.refreshOutfit
    end

    def self.restore_appearance
      return if !Ironmon.active? || !$Trainer
      safely do
        state = profile.read
        appearance = state["appearances"][appearance_key]
        apply(appearance) if appearance
      end
    end

    def self.copy_slot_appearance(previous_key, saved_slot)
      return if !previous_key || !saved_slot
      destination = appearance_key(saved_slot)
      return if destination == previous_key
      safely do
        profile.transaction do |state|
          appearance = state["appearances"][previous_key]
          next false if !appearance
          state["appearances"][destination] = appearance.dup
          true
        end
      end
    end

    def self.badge_ids
      return [] if !$Trainer || !$Trainer.respond_to?(:badges)
      return $Trainer.badges.each_index.select { |index| $Trainer.badges[index] }
    end

    def self.begin_attempt
      return if !$PokemonGlobal
      # This local identity must distinguish repeated imports of the same seed,
      # without consuming the game's gameplay RNG or changing seed fingerprints.
      $PokemonGlobal.ironmon_cosmetic_attempt_id = Random.new.bytes(16).unpack("H*")[0]
      $PokemonGlobal.ironmon_cosmetic_starting_badges = badge_ids
      @milestone_signature = nil
    end

    def self.attempt_id
      return nil if !$PokemonGlobal || !Ironmon.current_run_attempt
      if !$PokemonGlobal.ironmon_cosmetic_attempt_id
        attempt = Ironmon.current_run_attempt
        identity = [appearance_key, $PokemonGlobal.ironmon_checkpoint_id,
                    attempt["attempt_number"], attempt["seed"]]
        $PokemonGlobal.ironmon_cosmetic_attempt_id = "legacy-" + Digest::SHA256.hexdigest(encode(identity))
        starting_count = (attempt["statistics"] || {})["starting_badges"].to_i
        $PokemonGlobal.ironmon_cosmetic_starting_badges = badge_ids.first(starting_count)
      end
      return $PokemonGlobal.ironmon_cosmetic_attempt_id
    end

    def self.award_trainer(source, badges_before, decision, internal)
      return false if !Ironmon.active? || decision != 1 || !internal || !source
      attempt = Ironmon.current_run_attempt
      return false if !attempt || attempt["result"] != "active"
      return safely do
        points = 10 + 5 * [[badges_before, 0].max, 8].min
        profile.award(attempt_id, "trainer:#{Digest::SHA256.hexdigest(encode(source))}", points)
      end
    end

    def self.queue_notice(points)
      @pending_notice_points = @pending_notice_points.to_i + points
    end

    def self.show_notice
      return false if @pending_notice_points.to_i <= 0 || !$game_temp || $game_temp.message_window_showing
      return false if pbMapInterpreterRunning? || !$game_player || $game_player.moving?
      points = @pending_notice_points
      @pending_notice_points = 0
      pbMessage(_INTL("You earned {1} wardrobe points!", points))
      return true
    end

    def self.check_milestones
      return if !Ironmon.active? || !Ironmon.current_run_attempt
      return if @retry_after && Time.now.to_f < @retry_after
      identity = attempt_id
      badges = badge_ids
      result = Ironmon.current_run_attempt["result"]
      signature = [identity, badges, result]
      return if signature == @milestone_signature
      completed = safely do
        starting = $PokemonGlobal.ironmon_cosmetic_starting_badges || []
        (badges - starting).each { |badge| queue_notice(100) if profile.award(identity, "badge:#{badge}", 100) }
        queue_notice(500) if result == "won" && profile.award(identity, "hall_of_fame", 500)
        true
      end
      @milestone_signature = signature if completed
      @retry_after = completed ? nil : Time.now.to_f + 5
    end

    def self.open_wardrobe
      return false if !$Trainer || !Ironmon.active?
      return false if Ironmon.failed_run_locked?
      begin
        return WardrobeScreen.new(catalog(true), profile, appearance_key).run
      rescue *RECOVERABLE_ERRORS => error
        echoln "Ironmon wardrobe unavailable: #{error.message}"
        pbMessage(_INTL("The optional wardrobe is unavailable. Your points and unlocks have not been reset. {1}", error.message))
        return nil
      end
    end

    def self.intro_wardrobe
      result = open_wardrobe
      if result || result.nil?
        $PokemonGlobal.ironmon_wardrobe_confirmed = true
        return true
      end
      return false
    end

    def self.patch_bedroom(map_id, map)
      return false if map_id != 13 || !map || !map.events
      patched = false
      map.events.each_value do |event|
        event.pages.each do |page|
          commands = page.list
          grant = commands.find { |command| command.code == 355 && command.parameters[0] == "obtainHat(getDefaultHat)" }
          condition = commands.find do |command|
            command.code == 111 && command.parameters[0] == 12 &&
              ["isWearingHat(HAT_BRENDAN)", "isWearingHat(HAT_MAY)"].include?(command.parameters[1])
          end
          next if !grant || !condition
          original = condition.parameters[1]
          grant.parameters[0] = "Ironmon.active? ? Ironmon::Cosmetics.intro_wardrobe : obtainHat(getDefaultHat)"
          condition.parameters[1] = "Ironmon.active? ? $PokemonGlobal.ironmon_wardrobe_confirmed == true : #{original}"
          commands.each do |command|
            next if command.code != 117 || command.parameters != [80]
            command.code = 355
            command.parameters = ["pbCommonEvent(80) unless Ironmon.active?"]
          end
          patched = true
        end
      end
      return patched
    end
  end

  module CosmeticLifecycleHooks
    def begin_run_attempt(seed)
      result = super
      Cosmetics.begin_attempt if result
      return result
    end

    def complete_run(result)
      completed = super
      Cosmetics.check_milestones if completed
      return completed
    end
  end
  singleton_class.prepend(CosmeticLifecycleHooks)
end

module IronmonCosmeticBattleHooks
  def pbStartBattle(*arguments)
    source = Ironmon::Cosmetics.battle_source
    badges = Ironmon::Cosmetics.badge_ids.length
    result = super
    Ironmon::Cosmetics.award_trainer(source, badges, result, @internalBattle) if trainerBattle?
    return result
  end
end
PokeBattle_Battle.prepend(IronmonCosmeticBattleHooks)

alias ironmon_cosmetic_original_trainer_battle_core pbTrainerBattleCore
def pbTrainerBattleCore(*arguments)
  previous = Ironmon::Cosmetics.battle_source
  if Ironmon.active?
    identities = arguments.map do |argument|
      argument.is_a?(Array) ? argument.first(3).map(&:to_s) : [argument.trainer_type.to_s, argument.name.to_s]
    end
    reference = Ironmon.current_area_event_reference
    Ironmon::Cosmetics.battle_source = [reference, identities]
  end
  return ironmon_cosmetic_original_trainer_battle_core(*arguments)
ensure
  Ironmon::Cosmetics.battle_source = previous
end

if Object.private_method_defined?(:changeOutfit) || Object.method_defined?(:changeOutfit)
  alias ironmon_cosmetic_original_change_outfit changeOutfit
  def changeOutfit
    return Ironmon::Cosmetics.open_wardrobe if Ironmon.active?
    return ironmon_cosmetic_original_change_outfit
  end
end

if Object.private_method_defined?(:nurseOutfitHeal) || Object.method_defined?(:nurseOutfitHeal)
  alias ironmon_cosmetic_original_nurse_heal nurseOutfitHeal
  def nurseOutfitHeal
    return if Ironmon.active?
    return ironmon_cosmetic_original_nurse_heal
  end
end

if Object.private_method_defined?(:pickUpTypeItemSetBonus) || Object.method_defined?(:pickUpTypeItemSetBonus)
  alias ironmon_cosmetic_original_type_bonus pickUpTypeItemSetBonus
  def pickUpTypeItemSetBonus
    return if Ironmon.active?
    return ironmon_cosmetic_original_type_bonus
  end
end

["isWearingTeamRocketOutfit", "isWearingTeamAquaOutfit", "isWearingTeamMagmaOutfit"].each do |name|
  next if !Object.private_method_defined?(name) && !Object.method_defined?(name)
  original = Object.instance_method(name)
  Object.send(:define_method, name) do
    Ironmon.active? ? false : original.bind(self).call
  end
  Object.send(:private, name)
end

if Object.private_method_defined?(:isWearingClothes) || Object.method_defined?(:isWearingClothes)
  alias ironmon_cosmetic_original_wearing_clothes isWearingClothes
  def isWearingClothes(id)
    return false if Ironmon.active? && defined?(CLOTHES_BREEDER) && id == CLOTHES_BREEDER
    return ironmon_cosmetic_original_wearing_clothes(id)
  end
end

if Object.private_method_defined?(:isWearingHat) || Object.method_defined?(:isWearingHat)
  alias ironmon_cosmetic_original_wearing_hat isWearingHat
  def isWearingHat(id)
    if Ironmon.active?
      return false if defined?(HAT_ZOROARK) && id == HAT_ZOROARK
      return false if defined?(HAT_TRUMPET) && id == HAT_TRUMPET
    end
    return ironmon_cosmetic_original_wearing_hat(id)
  end
end

Ironmon.register_game_load_hook(:cosmetics, nil, proc do |_data, _result|
  Ironmon::Cosmetics.restore_appearance
  Ironmon::Cosmetics.instance_variable_set(:@milestone_signature, nil)
end)
Ironmon.register_game_save_hook(:cosmetics,
  proc { |_slot, _auto, _safe| Ironmon.active? ? Ironmon::Cosmetics.appearance_key : nil },
  proc do |slot, auto, _safe, result, previous_key|
    if result && !auto && Ironmon.active?
      Ironmon::Cosmetics.copy_slot_appearance(previous_key, $Trainer.save_slot || slot)
    end
  end)
Events.onMapCreate += proc { |_sender, event| Ironmon::Cosmetics.patch_bedroom(event[0], event[1]) }
