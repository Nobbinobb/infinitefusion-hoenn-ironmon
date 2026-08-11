# Step 3.3 Part 5 Validation: Specialized Fusion Move Tutor

## Review boundary

Part 5 implements the specialized Fusion Move Tutor's regular and legendary
catalogs and its exact-fusion compatibility. Tracker/debug presentation remains
Part 6.

Validation used Pokemon Infinite Fusion 2's bundled executable and embedded
Ruby runtime after synchronizing the canonical `src` scripts into the local
installation.

## Source audit

The native Fusion Tutor exposes two separate progression-controlled catalogs:

- 32 regular source slots;
- 25 legendary source slots.

The original species, type, and native move conditions are evaluated only to
determine how many entries an exact displayed fusion originally qualified for
in each catalog. They do not determine which generated moves it receives.

## Embedded-runtime results

- [x] Generator schema 6 initializes successfully from a cold state.
- [x] Regular and legendary catalog sizes remain 32 and 25.
- [x] Every slot receives a deterministic move from the mechanically permitted
  global pool.
- [x] Generated moves are unique within each catalog.
- [x] Regular and legendary catalogs remain separate; the native progression
  Boolean continues to select which one is shown.
- [x] The tutor's full-list screen returns the generated catalog.
- [x] Its availability check and teaching screen return the same generated
  per-fusion list.
- [x] Normal Pokemon receive no specialized Fusion Tutor access.
- [x] Identical copies of the same displayed fusion receive identical access.
- [x] Reversed fusions use their distinct displayed identity and generated
  compatibility.
- [x] A different seed changes both catalog offerings and per-fusion access.

The validation matrix covered 196 ordered fusions assembled from 14 components:

- 161 had at least one regular source qualification;
- 164 had at least one legendary source qualification;
- 9 had no access to either catalog;
- the largest observed preserved regular capacity was 7;
- the largest observed preserved legendary capacity was 6.

For every matrix entry, generated list length exactly matched the native source
count, contained no duplicates, and was a subset of the corresponding generated
catalog.

## Save compatibility

- [x] Schema-5 review metadata migrates to schema 6 when all accepted source
  fingerprints still match.
- [x] Migration records regular and legendary catalog sizes and their catalog
  and source fingerprints.
- [x] Migration preserves level-up, Egg, TM, TR, ordinary tutor compatibility,
  and ordinary tutor offerings.
- [x] Changed specialized tutor metadata is rejected rather than silently
  altering an existing run.

Recorded fingerprints:

- Specialized catalog: `00009adf987bc14f`
- Specialized compatibility source: `d952f7ec6f427a18`

## Performance

- [x] Cold move-access preparation measured approximately 75-86 ms.
- [x] Repeated preparation measured approximately 0.02 ms.
- [x] First generated regular-catalog resolution measured under 1 ms.
- [x] Evaluating a previously unseen fusion's native source qualification
  measured approximately 43-50 ms in the validation runs.
- [x] Cached source qualification measured approximately 0.07 ms.
- [x] Generated per-fusion resolution after source caching measured under 1 ms;
  cached generated resolution remained below approximately 0.13 ms.

The one-time source-condition cost occurs only when this specialized tutor or an
authorized inspection path first requests that displayed fusion. It is not part
of starter generation, encounters, sprite selection, or ordinary move access.

## Player validation disposition

- [x] Part 5 is ready for code review and embedded-runtime acceptance.
- [x] The inaccessible manual teaching queue is retired; specialized tutor
  capacity and compatibility were validated through the generator boundary.
- [x] The completed Tutor tab exposes the full generated compatibility list
  through the same deterministic recipe.

Tracker/debug move-access tabs remain outside this Part 5 boundary.
