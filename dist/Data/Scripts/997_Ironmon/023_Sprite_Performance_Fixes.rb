#===============================================================================
# Sprite loading and lookup performance safeguards
#===============================================================================

module Ironmon
  SPRITE_CREDIT_CACHE_SCHEMA_VERSION = 1
  TRACKER_SPRITE_PATH_CACHE_LIMIT = 2048

  def self.sprite_credit_catalog
    path = Settings::CREDITS_FILE_PATH
    signature = [
      SPRITE_CREDIT_CACHE_SCHEMA_VERSION,
      File.size(path),
      File.mtime(path).to_i
    ]
    return @sprite_credit_catalog if
      @sprite_credit_catalog && @sprite_credit_catalog_signature == signature
    catalog = Hash.new { |hash, key| hash[key] = {} }
    File.foreach(path) do |line|
      row = line.strip.split(',')
      next if row.length < 3
      match = /\A(\d+(?:\.\d+)?)([a-zA-Z]*)\z/.match(row[0].to_s)
      next if !match
      catalog[match[1]][match[2]] = row[2]
    end
    catalog.each_value(&:freeze)
    catalog.default_proc = nil
    @sprite_credit_catalog = catalog.freeze
    @sprite_credit_catalog_signature = signature.freeze
    return @sprite_credit_catalog
  end

  def self.reset_sprite_credit_catalog
    @sprite_credit_catalog = nil
    @sprite_credit_catalog_signature = nil
  end

  def self.tracker_live_sprite_paths
    @tracker_live_sprite_paths ||= {}
  end

  def self.tracker_sprite_cache_key(pif_sprite)
    return nil if !pif_sprite
    return [
      pif_sprite.type,
      pif_sprite.head_id,
      pif_sprite.body_id,
      pif_sprite.alt_letter,
      pif_sprite.local_path
    ]
  end

  def self.store_tracker_live_sprite_path(key, path)
    cache = tracker_live_sprite_paths
    cache.delete(key)
    cache.delete(cache.keys.first) if cache.length >=
      TRACKER_SPRITE_PATH_CACHE_LIMIT
    cache[key] = path
    return path
  end

  def self.reset_tracker_live_sprite_path_cache
    @tracker_live_sprite_paths = nil
  end
end

class BattleSpriteLoader
  alias ironmon_sprite_fix_original_select_new_pif_fusion_sprite select_new_pif_fusion_sprite
  def select_new_pif_fusion_sprite(head_id, body_id)
    pif_sprite = ironmon_sprite_fix_original_select_new_pif_fusion_sprite(
      head_id, body_id
    )
    if pif_sprite && !pif_sprite.local_path
      local_path = check_for_local_sprite(pif_sprite)
      pif_sprite.local_path = local_path if local_path
    end
    return pif_sprite
  end

  alias ironmon_sprite_fix_original_select_new_pif_base_sprite select_new_pif_base_sprite
  def select_new_pif_base_sprite(dex_number)
    pif_sprite = ironmon_sprite_fix_original_select_new_pif_base_sprite(
      dex_number
    )
    if pif_sprite && !pif_sprite.local_path
      local_path = check_for_local_sprite(pif_sprite)
      pif_sprite.local_path = local_path if local_path
    end
    return pif_sprite
  end

  alias ironmon_sprite_fix_original_load_pif_sprite_directly load_pif_sprite_directly
  def load_pif_sprite_directly(pif_sprite)
    if pif_sprite && !pif_sprite.local_path
      local_path = check_for_local_sprite(pif_sprite)
      pif_sprite.local_path = local_path if local_path
    end
    return ironmon_sprite_fix_original_load_pif_sprite_directly(pif_sprite)
  end
end

class CustomSpriteExtracter
  alias ironmon_sprite_fix_original_should_update_spritesheet? should_update_spritesheet?
  def should_update_spritesheet?(pif_sprite)
    return false if pbResolveBitmap(getSpritesheetPath(pif_sprite))
    return ironmon_sprite_fix_original_should_update_spritesheet?(pif_sprite)
  end
end

class BaseSpriteExtracter
  alias ironmon_sprite_fix_original_should_update_spritesheet? should_update_spritesheet?
  def should_update_spritesheet?(pif_sprite)
    return false if pbResolveBitmap(getSpritesheetPath(pif_sprite))
    return ironmon_sprite_fix_original_should_update_spritesheet?(pif_sprite)
  end
end

alias ironmon_sprite_fix_original_map_alt_sprite_letters_for_pokemon map_alt_sprite_letters_for_pokemon
def map_alt_sprite_letters_for_pokemon(sprite_name)
  return Ironmon.sprite_credit_catalog.fetch(sprite_name.to_s, {}).dup
rescue Exception => e
  echoln "Ironmon sprite credit cache failed: #{e.message}"
  return ironmon_sprite_fix_original_map_alt_sprite_letters_for_pokemon(
    sprite_name
  )
end

module Ironmon
  class << self
    alias ironmon_sprite_fix_original_tracker_sprite_path tracker_sprite_path
    def tracker_sprite_path(pokemon, preferred_sprite = nil)
      pif_sprite = preferred_sprite || pokemon.pif_sprite
      if !pif_sprite
        loader = BattleSpriteLoader.new
        pif_sprite = loader.get_pif_sprite_from_species(pokemon.species)
      end
      key = tracker_sprite_cache_key(pif_sprite)
      cache = tracker_live_sprite_paths
      return cache[key] if key && cache.key?(key)
      path = BattleSpriteLoader.new.check_for_local_sprite(pif_sprite)
      normalized = path ? path.tr("\\", "/") : nil
      store_tracker_live_sprite_path(key, normalized) if key && normalized
      return normalized
    rescue Exception
      return ironmon_sprite_fix_original_tracker_sprite_path(
        pokemon, preferred_sprite
      )
    end
  end
end
