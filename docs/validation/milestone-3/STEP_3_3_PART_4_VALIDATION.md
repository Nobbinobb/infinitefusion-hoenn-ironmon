# Step 3.3 Part 4 Validation: Ordinary Tutors

## Review boundary

Part 4 implements ordinary world-tutor offerings and compatibility. It does not
implement the specialized Fusion Move Tutor or the tracker/debug presentation;
those remain separate review parts.

Validation used Pokemon Infinite Fusion 2's bundled game executable and
embedded Ruby runtime after synchronizing the canonical `src` scripts into the
local installation.

## Audited tutor catalog

The supported game data contains four ordinary events that both start a tutor
battle and later call the teaching interface:

| Location | Source move | Seed-12345 offering |
| --- | --- | --- |
| Slateport City | Swagger | Metronome |
| Route 104 (North) | Echoed Voice | Baby-Doll Eyes |
| Mauville City | Rollout | Camouflage |
| Mauville City Interiors | Skill Swap | Freeze Shock |

Other calls to the tutor-battle helper award an HM/tool, heal the party, or
advance another event without opening the teaching interface. They are not
ordinary tutor slots.

## Embedded-runtime results

- [x] Generator schema 5 initializes successfully from a cold state.
- [x] All four audited tutor slots receive deterministic, unique offerings from
  the mechanically permitted global pool.
- [x] Repeated resolution preserves the same offerings; a different seed
  changes them.
- [x] All four map events replace their source move in setup and battle logic,
  expose the generated name, and can be restored and repatched safely.
- [x] Compatibility is checked in a dedicated tutor context. Native level-up,
  Egg, TM, or other tutor data cannot make an unsupported offering teachable.
- [x] Abstract tutor compatibility excludes TM, TR, and former-HM machine
  entries and preserves each species' distinct native non-machine tutor count.
- [x] Fusion abstract compatibility is the deduplicated body/head union.
- [x] Supported ordinary tutor moves equal the exact intersection between a
  species' abstract compatibility and the four generated offerings.

Measured source totals for the supported data set:

- 570 species or mechanical forms have non-empty ordinary-tutor capacity.
- 7,792 abstract compatibility entries are preserved in total.
- The largest individual capacity is 77.
- For validation seed 12,345, 47 species have at least one supported offering,
  with 48 supported species/move intersections in total.

## Save compatibility

- [x] Schema-4 review metadata migrates to schema 5 when all accepted source
  fingerprints still match.
- [x] Migration records the ordinary tutor catalog and source fingerprints.
- [x] Migration preserves previously accepted level-up, Egg, TM, and TR results.
- [x] A changed tutor catalog or source fingerprint is rejected rather than
  silently changing an existing run.

Recorded fingerprints:

- Tutor catalog: `81afeb8726c9a259`
- Tutor compatibility source: `b053fc007ffaf484`

## Performance

- [x] Cold move-access preparation measured approximately 89 ms.
- [x] Repeated preparation measured approximately 0.008 ms.
- [x] First single-species tutor resolution measured approximately 0.11 ms.
- [x] Cached single-species resolution measured approximately 0.001 ms.
- [x] First four-slot offering resolution measured approximately 0.04 ms and
  cached resolution approximately 0.001 ms.

## Player validation disposition

- [x] Part 4 is ready for code review and embedded-runtime acceptance.
- [x] The completed tracker exposes the generated supported tutor list; the
  inaccessible manual-teaching check is retired.
- [x] The cumulative Milestone 3 pass validated save/reload and F7
  reconstruction, and tracker lookup uses the same generator recipe.

The specialized Fusion Move Tutor remains outside this Part 4 boundary.
