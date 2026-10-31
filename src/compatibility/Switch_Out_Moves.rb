module Ironmon
  module SwitchOutMoves
    module MoveAnimation
      def pbShowAnimation(id, user, targets, hitNum = 0, showAnimation = true)
        return super if !showAnimation || @battle.pbCanChooseNonActive?(user.index)
        scene = @battle.scene
        return super if !scene.respond_to?(:sprites) || !scene.sprites.is_a?(Hash)
        sprite = scene.sprites["pokemon_#{user.index}"]
        return super if !sprite || sprite.disposed?
        pokemon = user.pokemon
        state = pbSaveSpriteStateAndBitmap(sprite)
        return super
      ensure
        # The animation can end with an invisible departure cel even though no
        # replacement can enter. Restore before damage and its effects run.
        if state && user.pokemon.equal?(pokemon) && !user.fainted? &&
           !user.semiInvulnerable? && user.effects[PBEffects::SkyDrop] < 0 &&
           scene.sprites["pokemon_#{user.index}"].equal?(sprite) && !sprite.disposed?
          bitmap = state[SpriteMetafile::BITMAP]
          pbRestoreSpriteStateAndBitmap(sprite, state) if !bitmap || !bitmap.disposed?
        end
      end
    end
  end
end

PokeBattle_Move_0EE.prepend(Ironmon::SwitchOutMoves::MoveAnimation)
