#===============================================================================
# Sprite loading and lookup performance safeguards
#===============================================================================

module Ironmon
  SPRITE_CREDIT_CACHE_SCHEMA_VERSION = 1
  TRACKER_SPRITE_PATH_CACHE_LIMIT = 2048
  TRACKER_TRANSPARENT_CACHE_INSPECTION_LIMIT = 512
  TRACKER_SPRITE_CACHE_FOLDER =
    "Graphics/CustomBattlers/local_sprites/IronmonTracker"

  def self.sprite_credit_catalog(work_checkpoint = nil)
    path = Settings::CREDITS_FILE_PATH
    signature = [
      SPRITE_CREDIT_CACHE_SCHEMA_VERSION,
      File.size(path),
      File.mtime(path).to_i
    ]
    return @sprite_credit_catalog if
      @sprite_credit_catalog && @sprite_credit_catalog_signature == signature
    catalog = Hash.new { |hash, key| hash[key] = {} }
    File.foreach(path).each_with_index do |line, index|
      work_checkpoint.call if work_checkpoint && index % 128 == 0
      row = line.strip.split(',')
      next if row.length < 3
      match = /\A(\d+(?:\.\d+)?)([a-zA-Z]*)\z/.match(row[0].to_s)
      next if !match
      catalog[match[1]][match[2]] = row[2]
    end
    catalog.each_value do |entries|
      entries.freeze
      work_checkpoint.call if work_checkpoint
    end
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

  def self.tracker_live_pif_sprite(pokemon, preferred_sprite = nil)
    return preferred_sprite if preferred_sprite
    pif_sprite = pokemon.pif_sprite
    if pif_sprite
      begin
        return pif_sprite if pif_sprite.species == pokemon.species_data.species
      rescue Exception
      end
    end
    pif_sprite = BattleSpriteLoader.new.get_pif_sprite_from_species(
      pokemon.species
    )
    pokemon.pif_sprite = pif_sprite if
      pif_sprite && pokemon.respond_to?(:pif_sprite=)
    return pif_sprite
  end

  def self.tracker_materialized_sprite_path(pif_sprite)
    type = pif_sprite.type.to_s.downcase
    body = pif_sprite.body_id || 0
    variant = pif_sprite.alt_letter.to_s
    variant = "main" if variant.empty?
    filename = [type, pif_sprite.head_id, body, variant].join("-") + ".png"
    return "#{TRACKER_SPRITE_CACHE_FOLDER}/#{filename}"
  end

  def self.ensure_tracker_sprite_cache_folder
    return if Dir.exist?(TRACKER_SPRITE_CACHE_FOLDER)
    parent = File.dirname(TRACKER_SPRITE_CACHE_FOLDER)
    Dir.mkdir(parent) if !Dir.exist?(parent)
    Dir.mkdir(TRACKER_SPRITE_CACHE_FOLDER)
  end

  def self.load_tracker_sprite_bitmap(loader, pif_sprite)
    extractor = loader.get_sprite_extractor_instance(pif_sprite.type)
    sprite = extractor.load_sprite(pif_sprite)
    return sprite if pif_sprite.type != :CUSTOM && pif_sprite.type != :BASE
    return sprite if tracker_sprite_bitmap_visible?(sprite)
    sprite.dispose if sprite && sprite.respond_to?(:dispose)
    if pif_sprite.type == :CUSTOM
      fallback = PIFSprite.new(
        :AUTOGEN, pif_sprite.head_id, pif_sprite.body_id, ""
      )
      return loader.get_sprite_extractor_instance(:AUTOGEN).load_sprite(
        fallback
      )
    end
    return nil if pif_sprite.alt_letter.to_s.empty?
    fallback = PIFSprite.new(:BASE, pif_sprite.head_id, nil, "")
    sprite = extractor.load_sprite(fallback)
    return sprite if tracker_sprite_bitmap_visible?(sprite)
    sprite.dispose if sprite && sprite.respond_to?(:dispose)
    return nil
  end

  def self.tracker_sprite_bitmap_visible?(sprite)
    return false if !sprite || !sprite.respond_to?(:bitmap)
    bitmap = sprite.bitmap
    return false if !bitmap || bitmap.disposed?
    step = [bitmap.width / 96, bitmap.height / 96, 1].max
    y = 0
    while y < bitmap.height
      x = 0
      while x < bitmap.width
        return true if bitmap.get_pixel(x, y).alpha > 0
        x += step
      end
      y += step
    end
    return false
  rescue Exception
    return false
  end

  def self.tracker_cached_sprite_usable?(path)
    return false if !File.file?(path)
    return true if File.size(path) > TRACKER_TRANSPARENT_CACHE_INSPECTION_LIMIT
    sprite = AnimatedBitmap.new(path)
    return tracker_sprite_bitmap_visible?(sprite)
  rescue Exception
    return false
  ensure
    sprite.dispose if sprite && sprite.respond_to?(:dispose)
  end

  def self.tracker_resolved_sprite_path(pif_sprite)
    return nil if !pif_sprite
    loader = BattleSpriteLoader.new
    local_path = loader.check_for_local_sprite(pif_sprite)
    resolved_local_path = pbResolveBitmap(local_path) if local_path
    if local_path && !resolved_local_path && pif_sprite.local_path
      pif_sprite.local_path = nil
      local_path = loader.check_for_local_sprite(pif_sprite)
      resolved_local_path = pbResolveBitmap(local_path) if local_path
    end
    return resolved_local_path.tr("\\", "/") if resolved_local_path
    cache_path = tracker_materialized_sprite_path(pif_sprite)
    return cache_path if tracker_cached_sprite_usable?(cache_path)
    File.delete(cache_path) if File.file?(cache_path)
    sprite = load_tracker_sprite_bitmap(loader, pif_sprite)
    return nil if !sprite
    ensure_tracker_sprite_cache_folder
    sprite.bitmap.save_to_png(cache_path)
    return cache_path
  rescue Exception => e
    echoln "Ironmon tracker sprite materialization failed: #{e.message}"
    return nil
  ensure
    sprite.dispose if sprite && !sprite.disposed?
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
      pif_sprite = tracker_live_pif_sprite(pokemon, preferred_sprite)
      key = tracker_sprite_cache_key(pif_sprite)
      cache = tracker_live_sprite_paths
      return cache[key] if key && cache.key?(key)
      normalized = tracker_resolved_sprite_path(pif_sprite)
      resolved_key = tracker_sprite_cache_key(pif_sprite)
      store_tracker_live_sprite_path(resolved_key, normalized) if
        resolved_key && normalized
      return normalized
    rescue Exception
      return ironmon_sprite_fix_original_tracker_sprite_path(
        pokemon, preferred_sprite
      )
    end
  end
end
