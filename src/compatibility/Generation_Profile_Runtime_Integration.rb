Ironmon.register_game_load_hook(
  :generation_profile,
  proc do |save_data|
    Ironmon.prepare_generation_profile_load(save_data)
  end,
  proc do |_save_data, _result, profile_id|
    Ironmon.finish_generation_profile_load(profile_id)
    if profile_id && Ironmon.active? && !Ironmon.checkpoint_reset_loading?
      Ironmon::ValidatedCustomSprites.profile_variants
    end
  end
)
