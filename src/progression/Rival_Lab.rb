module Ironmon
  RIVAL_LAB_EXIT = [8, 16].freeze
  RIVAL_LAB_STEPS = {
    2 => [0, 1, PBMoveRoute::TurnDown, PBMoveRoute::Down],
    4 => [-1, 0, PBMoveRoute::TurnLeft, PBMoveRoute::Left],
    6 => [1, 0, PBMoveRoute::TurnRight, PBMoveRoute::Right],
    8 => [0, -1, PBMoveRoute::TurnUp, PBMoveRoute::Up]
  }.freeze

  def self.rival_lab_path(rival, target_x, target_y)
    start = [rival.x, rival.y]
    target = [target_x, target_y]
    queue = [start]
    parents = { start => nil }
    cursor = 0
    while cursor < queue.length && !parents.key?(target)
      x, y = queue[cursor]
      cursor += 1
      RIVAL_LAB_STEPS.each do |direction, step|
        position = [x + step[0], y + step[1]]
        next if parents.key?(position) || !rival.passable?(x, y, direction)
        parents[position] = [[x, y], direction]
        queue << position
      end
    end
    raise "No clear rival route to #{target.inspect}" if !parents.key?(target)
    path = []
    position = target
    while parents[position]
      position, direction = parents[position]
      path.unshift(direction)
    end
    return path
  end

  def self.walk_rival_lab_to(rival, x, y, speed = 4)
    return if !rival
    rival.direction_fix = false
    rival.through = false
    commands = [PBMoveRoute::DirectionFixOff, PBMoveRoute::ThroughOff,
      PBMoveRoute::ChangeSpeed, speed]
    previous = nil
    rival_lab_path(rival, x, y).each do |direction|
      step = RIVAL_LAB_STEPS.fetch(direction)
      if direction != previous
        commands.concat([step[2], PBMoveRoute::Wait, 2])
        previous = direction
      end
      commands << step[3]
    end
    pbMoveRoute(rival, commands)
    wait_for_early_game_movement(rival)
    raise "The rival did not reach the lab waypoint" if rival.x != x || rival.y != y
  end

  def self.resolve_rival_lab_choice(rival)
    return if !rival_lab_choice_enabled? || $PokemonGlobal.ironmon_rival_lab_decision
    decision = pending_rival_lab_decision(false)
    action = decision[:action]
    pbCallBub(2, EARLY_GAME_LAB_RIVAL_EVENT_ID)
    if action == :keep
      pbMessage(_INTL("We lost this time, but I'm sticking with my partner. We'll get stronger together!"))
    else
      message = case action
                when :swap then _INTL("Professor, could I try the other Pokémon instead?")
                when :fuse then _INTL("Professor, I'd like to fuse my partner with the other Pokémon!")
                when :unfuse then _INTL("Professor, could you help me unfuse my partner?")
                end
      pbMessage(message)
      birch = approach_birch_with_rival(rival)
      pbCallBub(2, EARLY_GAME_LAB_PROFESSOR_EVENT_ID)
      response = case action
                 when :swap then _INTL("Of course. Take good care of this one!")
                 when :fuse then _INTL("Let's see what these two can do together!")
                 when :unfuse then _INTL("All right. We'll separate them carefully.")
                 end
      pbMessage(response)
      pbWait(4)
      if action == :fuse
        finish_rival_lab_fusion(decision)
        trainer = $PokemonGlobal.battledTrainers[BATTLED_TRAINER_RIVAL_KEY]
        starter = rival_story_starter(trainer)
        material = Pokemon.new(decision[:remaining_species], starter.level, trainer.trainerName)
        show_rival_lab_fusion(starter, material, decision[:species])
      elsif action == :unfuse
        pbSEPlay("separate")
        pbWait(12)
      else
        pbSEPlay("itemlevel")
        pbWait(8)
      end
      pbTurnTowardEvent(birch, $game_player)
    end
    apply_rival_lab_decision(decision)
    if action != :keep
      rival.turn_toward_player if rival
      pbCallBub(2, EARLY_GAME_LAB_RIVAL_EVENT_ID)
      pbMessage(_INTL("All right, {1}! Let's get stronger for our next battle!", GameData::Species.get(decision[:species]).name))
    end
  end

  def self.finish_rival_lab_fusion(decision)
    if player_fusion_pairing_ready?
      number = player_fusion_mapper.species_number(decision[:initial_species], decision[:remaining_species])
      decision[:species] = fusion_species_identity(number)
      return
    end

    request = request_tracker_rival_fusion(decision)
    deadline = tracker_uptime_seconds + 3.0
    until request.key?(:species)
      raise PlayerFusionMappingError, request[:error] if request[:error]
      if !tracker_connection.rival_fusion_worker_supported? || tracker_uptime_seconds >= deadline
        raise PlayerFusionMappingError, "The tracker did not return the rival's fusion. Restart the matching tracker and game."
      end
      Graphics.update
      Input.update
      tracker_connection.update
    end
    decision[:species] = request[:species]
  ensure
    @tracker_rival_fusion_request = nil
  end

  def self.prepare_tracker_rival_fusion
    return false if !tracker_connection.rival_fusion_worker_supported?
    recipe = tracker_active_fusion_assignment_recipe(true)
    return false if !recipe
    tracker_connection.send_event("rival_fusion_prepare", {
      "session_id" => game_session_id, "recipe" => recipe
    })
    return true
  rescue StandardError => error
    echoln "Ironmon rival fusion preparation deferred: #{error.message}"
    return false
  end

  def self.prepare_rival_lab_resources
    return if !rival_lab_choice_enabled? || $PokemonGlobal.ironmon_rival_lab_decision
    ValidatedCustomSprites.profile_variants
    sprite_credit_catalog
  end

  def self.request_tracker_rival_fusion(decision)
    if !tracker_connection.rival_fusion_worker_supported?
      raise PlayerFusionMappingError, "The rival's fusion needs the matching updated tracker. Restart the tracker and game."
    end
    recipe = tracker_active_fusion_assignment_recipe(true)
    raise PlayerFusionMappingError, "The rival's fusion recipe is unavailable" if !recipe
    @tracker_rival_fusion_sequence = (@tracker_rival_fusion_sequence || 0) + 1
    payload = {
      "session_id" => game_session_id,
      "request_id" => "#{ensure_tracker_run_id}:rival:#{@tracker_rival_fusion_sequence}",
      "body_id" => GameData::Species.get(decision[:initial_species]).id_number,
      "head_id" => GameData::Species.get(decision[:remaining_species]).id_number,
      "recipe" => recipe
    }
    @tracker_rival_fusion_request = {
      :payload => payload, :run_id => ensure_tracker_run_id
    }
    tracker_connection.send_event("rival_fusion_requested", payload)
    return @tracker_rival_fusion_request
  end

  def self.receive_tracker_rival_fusion(message)
    request = @tracker_rival_fusion_request
    return if !request || message["run_id"] != request[:run_id] ||
      request[:run_id] != ensure_tracker_run_id
    payload = message["payload"]
    return if !payload.is_a?(Hash)
    expected = request[:payload]
    return if ["session_id", "request_id", "body_id", "head_id"].any? do |key|
      payload[key] != expected[key]
    end
    if payload["error"]
      request[:error] = payload["error"].to_s
      return
    end
    number = payload["species_number"]
    if !number.is_a?(Integer) || !custom_fusion_pool_service.include_number?(number)
      request[:error] = "The tracker returned an invalid rival fusion"
      return
    end
    request[:species] = fusion_species_identity(number)
  end

  def self.show_rival_lab_fusion(body_pokemon, head_pokemon, species)
    identity = fusion_species_identity(species)
    match = /\AB(\d+)H(\d+)\z/.match(identity.to_s)
    raise PlayerFusionMappingError, "The rival's animation needs a custom fusion" if !match
    body, head = match[1].to_i, match[2].to_i
    sprite = BattleSpriteLoader.new.obtain_fusion_pif_sprite(head, body)
    scene = RivalLabFusionScene.new
    started = scene.pbStartScreen(body_pokemon.clone, head_pokemon.clone,
      body * NB_POKEMON + head, :DNASPLICERS, sprite)
    scene.play_rival_animation if started
  ensure
    scene.pbEndScreen if scene && started
  end

  def self.position_rival_for_lab_battle(rival)
    return if !rival
    positions = [[8, 8], [$game_player.x - 1, $game_player.y],
      [$game_player.x + 1, $game_player.y], [$game_player.x, $game_player.y + 1]]
    position = positions.find do |x, y|
      next false if x == $game_player.x && y == $game_player.y
      begin
        rival_lab_path(rival, x, y)
        true
      rescue RuntimeError
        false
      end
    end
    raise "No clear rival battle position in the lab" if !position
    walk_rival_lab_to(rival, *position)
    rival.turn_toward_player
    pbTurnTowardEvent($game_player, rival)
  end

  def self.approach_birch_with_rival(rival)
    birch = $game_map.events[EARLY_GAME_LAB_PROFESSOR_EVENT_ID]
    raise "Professor Birch is missing from the lab" if !birch
    positions = [[birch.x - 1, birch.y], [birch.x + 1, birch.y],
      [birch.x, birch.y + 1], [birch.x, birch.y - 1]]
    position = positions.find do |x, y|
      next false if x == $game_player.x && y == $game_player.y
      begin
        rival_lab_path(rival, x, y)
        true
      rescue RuntimeError
        false
      end
    end
    raise "No clear rival route to Professor Birch" if !position
    walk_rival_lab_to(rival, *position)
    birch.direction_fix = false
    pbTurnTowardEvent(rival, birch)
    pbTurnTowardEvent(birch, rival)
    return birch
  end

  def self.leave_rival_lab(rival)
    return if !rival
    walk_rival_lab_to(rival, *RIVAL_LAB_EXIT, 5)
    rival.turn_down
    rival.opacity = 0
  end
end

Ironmon.register_game_load_hook(:rival_lab_resources, nil, proc do |_save_data, _result|
  Ironmon.prepare_rival_lab_resources if Ironmon.active?
end)

class Ironmon::RivalLabFusionScene < PokemonFusionScene
  def generateSplicerMetaFile(nb_seconds, x_pos, y_pos)
    splicer = SpriteMetafile.new
    splicer.opacity = 255
    splicer.x, splicer.y, splicer.z = x_pos, y_pos, 0
    duration = (Graphics.frame_rate * nb_seconds).ceil
    direction = 1
    duration.times do |frame|
      if frame % 5 == 0
        splicer.y += direction
        direction = -1 if splicer.y == 160
        direction = 1 if splicer.y == 140
      end
      splicer.opacity = 0 if frame >= duration * 0.75
      splicer.update
    end
    @metafile4 = splicer
  end

  # This NPC scene plays the three Pokemon tracks; it has no confetti players.
  def generateConfettiMetaFile(_start_frame_offset, _nb_particles = 30)
    @confetti_metafiles = []
  end

  def play_rival_animation
    players = [
      SpriteMetafilePlayer.new(@metafile1, @sprites["rsprite1"]),
      SpriteMetafilePlayer.new(@metafile2, @sprites["rsprite2"]),
      SpriteMetafilePlayer.new(@metafile3, @sprites["rsprite3"]),
      SpriteMetafilePlayer.new(@metafile4, @sprites["dnasplicer"])
    ]
    previous_bgm = $game_system.playing_bgm
    pbBGMPlay("fusion_short")
    players.each(&:play)
    loop do
      players.each(&:update)
      Graphics.update
      Input.update
      break if players.first(3).none?(&:playing?)
    end
    pbPlayCry(@newspecies)
    pbWait(30)
  ensure
    pbBGMStop
    pbBGMPlay(previous_bgm) if previous_bgm
  end
end
