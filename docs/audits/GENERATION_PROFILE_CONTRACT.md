# Generation profile contract

## Purpose

An Ironmon run must keep the deterministic world it started with. Updating the
game, Ironmon, generated audits, or the available custom sprites must not cause
an active or archived run to be interpreted through current installation data.

The generation profile is the immutable identity for that world. It separates
three concerns that were previously mixed together:

- algorithm-family versions identify deterministic behavior;
- data-component descriptors identify exact source snapshots;
- cache and persistence schemas identify storage formats only.

Release versions are labels and are not part of profile identity.

## Version 1 manifest

The manifest contains `schema_version`, `algorithms`, and `components`.
Algorithm and component arrays are sorted by their stable lowercase names
before hashing. Object keys are sorted ordinally at every depth. The profile ID
is the lowercase SHA-256 digest of the resulting compact UTF-8 JSON.

Every manifest must contain exactly these algorithm families:

- `hash_contract`
- `custom_fusion_eligibility`
- `species_mapping`
- `ability_assignment`
- `base_stats`
- `move_access`
- `item_slots`
- `normal_evolution`
- `fusion_evolution`
- `player_fusion_reversal`
- `gym_party_expansion`
- `caught_fusion_component`
- `progression_support_pokemon`

Each component descriptor contains its name, independent schema version, exact
byte length, and lowercase SHA-256 digest. The digest provides identity and
integrity; it does not replace Ironmon's deterministic generation hash.

## Source component boundary

`generation_base_catalog.json` is schema 1 of the immutable base catalog. It
contains normal species, types, abilities, moves, items, evolution methods,
trainer parties, and encounters from the authoritative bundled game runtime.
It explicitly excludes the custom-fusion pool.

The current profile contains four source components:

- `base_catalog` contains immutable normal game data;
- `custom_fusion_pool` contains the independently changing sprite eligibility
  set;
- `area_catalog` preserves authored area, trainer, and item lookup sources;
- `obtainability_sources` preserves authored gifts, static encounters, and
  resource sources needed by archived obtainability inspection.

Adding sprites therefore changes only `custom_fusion_pool` and creates a new
profile for future runs without changing any profile already pinned by an
active or archived run.

## Packed custom-fusion component

Custom-fusion component schema 1 stores a 576 by 576 row-major eligibility
bitset. Body is the outer axis and head is the inner axis, matching the current
numeric pool order. Bits within each byte are least-significant-bit first.

| Offset | Size | Value |
| ---: | ---: | --- |
| 0 | 8 | ASCII `IFCFPOOL` |
| 8 | 2 | little-endian schema version |
| 10 | 2 | little-endian normal-species count |
| 12 | 4 | little-endian eligible-fusion count |
| 16 | 4 | little-endian bitset byte length |
| 20 | remaining | eligibility bitset |

Readers reject unknown schemas, invalid dimensions, odd eligibility counts,
trailing or missing bytes, and count/bitset disagreements. The current pool is
encoded and decoded before export, and the decoded order must exactly match the
runtime pool.

## Compatibility behavior

Consumers must reject incomplete, malformed, or unavailable profiles. They
must never substitute current installation data for a missing historical
component. Source components are archival inputs; performance indexes derived
from them are rebuildable caches and do not define compatibility.

The build copies the current manifest and all four source components into the
runtime package and into the installation's content-addressed profile store.
Starting a new attempt validates and retains that package before pinning its
profile ID into both the attempt ledger and save-owned metadata. Updating the
current package never deletes stored historical packages. Normalizing or
loading an older attempt without an ID does not backfill one from the current
installation, and saved generator metadata is never rewritten to current
catalogs.

Loading an active attempt activates its pinned package before the save finishes
loading and keeps that package as the gameplay runtime context. Generator
readiness, deferred mappings, evolutions, encounters, and live tracker work
therefore all resolve through the run's original data. Completing the attempt
does not rewrite that identity. F7 or automatic reset returns to the installed
current package before generation and the replacement attempt pins that current
profile as a new, independent run.

Completed-run and seeded-run reproduction recipes carry the pinned profile ID,
and it contributes to their compatibility fingerprint. The tracker persists
that identity with the completed recipe and rejects missing or malformed IDs.
For an archived request, the game validates the matching stored package,
activates its base game data, custom-fusion pool, area catalog, and
obtainability catalog for the complete request, and restores the live data
afterward. An archive request may temporarily select another profile while an
active run is loaded; finishing the request restores the active run's package.
Nested temporary requests cannot cross profile identities. A missing or damaged
package returns `generation_profile_unavailable`; current catalogs or a later
custom-sprite pool are never substituted silently.

All retained deterministic algorithm families begin at version 1. Development
versions that predate this reset are intentionally unsupported because no
public release depended on them. Future behavior changes increment only their
affected algorithm family and keep that older implementation available for
profiles that reference it. Performance indexes remain rebuildable derivatives
and may change without changing a pinned profile.

The release type-coverage dataset is generated only after the current profile
and its components have been finalized and installed. Extraction runs inside
that exact profile context, so mutable installation sprite data cannot replace
the pinned `custom_fusion_pool` component. Tracker tests decode the component
and require its eligibility algorithm version, dimensions, population,
fingerprint, byte length, and SHA-256 descriptor to match the coverage dataset
and profile manifest before packaging.
