module Ironmon
  module SkyDrop
    def self.carried_targets(carrier)
      return [] if !carrier.battle
      return carrier.battle.battlers.filter_map do |target|
        next if !target || !target.effects || target.effects[PBEffects::SkyDrop] != carrier.index
        [target, target.pokemon]
      end
    end

    def self.restore_visible(battler)
      return if battler.fainted? || battler.semiInvulnerable? || battler.effects[PBEffects::SkyDrop] >= 0
      scene = battler.battle.scene
      return if !scene.respond_to?(:sprites) || !scene.sprites.is_a?(Hash)
      sprite = scene.sprites["pokemon_#{battler.index}"]
      sprite.visible = true if sprite && !sprite.disposed?
    end

    def self.release(carrier, targets, carrier_pokemon)
      targets.each do |target, pokemon|
        next if !target.pokemon.equal?(pokemon)
        target.effects[PBEffects::SkyDrop] = -1 if target.effects[PBEffects::SkyDrop] == carrier.index
        restore_visible(target)
      end
      restore_visible(carrier) if carrier.pokemon.equal?(carrier_pokemon)
    end

    module BattlerCleanup
      def pbUseMove(choice, special_usage = false)
        dropping = inTwoTurnAttack?("0CE")
        targets = Ironmon::SkyDrop.carried_targets(self) if dropping
        carrier_pokemon = pokemon
        completed = false
        result = super
        completed = true
        return result
      ensure
        interrupted = !completed && inTwoTurnAttack?("0CE")
        if dropping || interrupted
          targets ||= Ironmon::SkyDrop.carried_targets(self)
          if pokemon.equal?(carrier_pokemon)
            @effects[PBEffects::TwoTurnAttack] = nil if inTwoTurnAttack?("0CE")
            @currentMove = nil if @currentMove && GameData::Move.get(@currentMove).function_code == "0CE"
          end
          Ironmon::SkyDrop.release(self, targets, carrier_pokemon)
        end
      end

      def pbCancelMoves(full_cancel = false)
        targets = Ironmon::SkyDrop.carried_targets(self)
        carrier_pokemon = pokemon
        return super
      ensure
        Ironmon::SkyDrop.release(self, targets, carrier_pokemon) if targets && !targets.empty?
      end

      def pbInitEffects(baton_pass)
        targets = Ironmon::SkyDrop.carried_targets(self)
        carrier_pokemon = pokemon
        return super
      ensure
        Ironmon::SkyDrop.release(self, targets, carrier_pokemon) if targets && !targets.empty?
      end
    end
  end
end

PokeBattle_Battler.prepend(Ironmon::SkyDrop::BattlerCleanup)
