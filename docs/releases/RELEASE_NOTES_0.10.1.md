# Ironmon 0.10.1

Ironmon 0.10.1 improves speed-up, loading and calculation responsiveness while
preserving existing runs and archives.

- **Smoother speed-up.** Gameplay pacing is separated from screen updates, with
  a presentation target of up to 120 updates per second. Music keeps its normal
  speed and pitch. Separate battle and overworld settings, Toggle and Hold modes
  remain available.
- **More predictable controls.** Held menu inputs repeat at their normal
  real-time cadence while walking stays accelerated. Rapid speed-stage changes
  preserve each selection and smooth the transition between speeds.
  Move-learning protection remains in place.
- **Clearer transitions.** Encounter effects, battle introductions and fades run
  at no more than 3× speed before returning to the selected gameplay speed.
- **Preparation before play.** Loading an Ironmon save finishes game preparation before
  returning control to the map. The game shows a simple waiting message, while
  the tracker temporarily fills its window with an animated preparation view
  and the current step. The normal tracker page returns without shifting.
- **Responsive calculations.** Automatic calculations use small background
  slices during play. Opening an archived run or requesting visible lookup data
  gives that calculation foreground priority and continues its existing work.
  Preparing uncached lookup data also causes fewer long pauses.
- **Adjusted fusion stats for new runs.** Seeded stat contributions now use
  85/15, 75/25 and two-thirds/one-third weights. The game and tracker use the same
  formula. Existing runs and archives keep their recorded formulas and stats;
  normal Pokémon stat generation is unchanged.
- **Sky Drop recovery.** Carried Pokémon are released correctly when damage is
  prevented or the move is interrupted, including the Wonder Guard case.
- **Volt Switch and U-turn visibility.** Both moves still deal damage when no
  eligible replacement exists, and the user remains visible afterward. Normal
  switching and fainting behavior is preserved.
- **More reliable Windows updates.** The updater handles temporary file locks
  more reliably when preparing and cleaning its download cache.
