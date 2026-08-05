# Checkpoint and reset lifecycle

Ironmon captures a hidden save immediately before the starter selection so F7
can return to that point and generate a fresh run.

## Storage model

A new game does not have an Infinite Fusion save slot until its first manual
save. Its checkpoint is therefore first written as:

`IronmonCheckpoint_Unsaved.rxdata`

Only one unsaved checkpoint is kept. Starting another new Ironmon game replaces
it. After the player first saves, the checkpoint moves to the selected slot,
for example:

`IronmonCheckpoint_File_H.rxdata`

Each manual save slot keeps at most one checkpoint. Saving normally updates the
run without creating another checkpoint. Saving the run to another slot copies
the checkpoint so both save slots can still be reset independently.

## Compatibility

Versions through 0.3.3 used random filenames such as
`IronmonCheckpoint_123456.rxdata`. These files remain readable. After a
successful manual save, the active legacy checkpoint is migrated to the
slot-based filename and removed.

Infinite Fusion's files under `backups/File H/File H_<timestamp>.rxdata` are
ordinary rolling save backups and are unrelated to Ironmon checkpoints.
