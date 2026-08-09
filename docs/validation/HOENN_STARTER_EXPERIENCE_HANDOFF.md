# Hoenn starter experience handoff validation

## Root cause

The Route 101 starter event saves the object returned by `hoennSelectStarter`
in `VAR_HOENN_STARTER`, starts the rescue battle, and then removes party slot 0.
Professor Birch's lab later gives back the object from that variable.

Ironmon's starter acquisition deliberately clones the selected candidate when
committing the automatic `take` pivot. The battle therefore updates the party
clone, while the event variable still points to the pre-battle candidate.

## Fix

After an Ironmon wild battle, the game now checks whether
`VAR_HOENN_STARTER` contains the marked starter. If the current party contains
the Pokemon with the same personal ID, the variable is refreshed to that
post-battle object. This occurs before Route 101 deletes its temporary party
entry, so the lab handoff retains battle-earned experience and all other
mutations.

## Validation

- [x] Inspected the compiled Route 101 and Professor Birch's Lab event command
  sequences in the bundled game runtime.
- [x] Reproduced the distinct stored-candidate and party-clone references.
- [x] Assigned additional experience to the party clone and confirmed the
  synchronization replaced the stored reference and preserved the exact EXP.
- [x] Rebuilt the distribution and synchronized canonical scripts into the
  local game installation.
- [x] The focused bundled-runtime test passed with the expected EXP value.
- [x] The clean game startup smoke test remained healthy for ten seconds; only
  the exact process started by the test was stopped.
