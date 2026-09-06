# Save H fusion preparation failure and fallback proposal

## Confirmed failure

Save H, attempt 52, seed `1271448503`, contains a generated custom fusion
`B470H315` (`271035`) at 824 BST. Its closest partner that shares neither
component is 97 BST away. The run's ordinary reverse-pair limit is 92 BST.
Consequently, this target cannot participate in any complete pairing under
the existing rules. Searching more repair chains or retrying the same seed
cannot solve it.

The original Ruby encounter probe did not finish within 90 seconds. The
tracker worker also spent minutes on this seed. The recorded protocol then
showed a `debug_inspect_pokemon` overview request for the normal player
Charmeleon timing out, followed by unanswered requests. The overview eagerly
obtained the global fusion mapper before checking whether the viewed Pokemon
was a fusion, forcing pending preparation onto the game thread.

## Implemented containment

- Ruby and C# detect a target with no legal disjoint partner before attempting
  exhaustive repair chains. The error reports the target BST, allowed gap,
  and nearest disjoint gap. Existing successful pairings retain their rules
  and traversal order.
- Existing mapper failure caches and tracker terminal-error handling retain
  the failure instead of repeatedly recalculating it.
- A normal Pokemon overview no longer requests the global fusion mapper.
  Unauthorized reverse-fusion information also does not request that mapper.
- The shared identity panel fits and centers visible sprite pixels inside
  its existing 72-pixel box. Fixed image enlargement is removed; the sprite
  preview dialog remains available.

These changes do not make an impossible pairing valid. Until a versioned
fallback is implemented, this seed reports a calculation error.

## Proposed fallback: minimum necessary strength relaxation

This is a design proposal. It is not enabled in gameplay.

1. Try the existing generator rules first. Runs that already produce a valid
   pairing keep their existing results.
2. If an isolated target proves the normal BST limit impossible, determine
   the nearest disjoint-partner gap for each isolated target. Use the largest
   of those gaps as the fallback limit for that run's reverse pairing.
3. Re-run deterministic pairing with that limit. Preserve the custom-sprite
   pool, component-disjointness requirement, type preference, and stable
   seed-based ordering. Apply this only to global reverse-pair construction;
   retain the normal material-pair BST roll and its existing nearest-pair
   selection behavior.
4. Validate complete coverage, uniqueness, reciprocal partners, and the
   effective strength bound before exposing any result. If no complete
   pairing is found, stop with a clear error; do not relax component rules,
   keep increasing the limit indefinitely, or publish partial mappings.
5. Cache successful work and deterministic failures. Keep game work
   cooperative and tracker work cancellable. Report the fallback reason and
   effective bound in diagnostics.

For this seed, an isolated C# experiment with a 97-BST limit produced all
88,327 reverse pairs. No pair shared a component, and exactly 17 pairs
exceeded the original 92-BST limit. The maximum gap was 97 BST. This proves
the proposed limit is sufficient for this seed as well as necessary for its
isolated target. It does not prove that every other impossible pool can be
repaired by this fallback.

## Compatibility requirements before implementation

- Introduce a separately versioned generator policy and implement identical
  Ruby/C# behavior and reference fixtures. Retain reconstruction of the old
  version for existing archives and pinned runs.
- Persist the selected player-fusion policy per run. The current active
  recipe derives its version from `PlayerFusionMapper::SCHEMA_VERSION`;
  simply incrementing that constant would silently reinterpret old runs.
- Include the policy in archived recipes, generation compatibility checks,
  worker negotiation, and cache keys. An older tracker must reject an
  unsupported policy instead of computing different results.
- Migrate an existing active run only through an explicit repair operation
  with a backup and checks that previously committed/discovered fusion
  mappings remain valid. Never rewrite those mappings or a pinned recipe
  silently. Save H has not been migrated by this investigation.
- Test old-version reference parity, the failing seed in both runtimes,
  repeated requests and cancellation, active-run repair eligibility, and
  archive round trips before enabling the new policy.

## Local verification

The unchanged save H checkpoint was loaded through a read-only runtime probe;
its file hash and gameplay fusion mappings remained unchanged. Charmeleon's
overview returned while leaving the scheduled pairing fiber untouched. A
second request to the failed mapper reused the same error immediately.

An isolated two-target fixture checks rejection on the first ordering attempt,
before entering exhaustive strength repairs, and verifies that a repeated
request does not restart preparation. The full save-H seed remains a separate
integration check for the diagnostic error and cached failure. Its elapsed
time is reported in the runtime test log and includes generating target stats,
ordering the installed pool, and any valid repairs before the isolated target
is reached. A fixed 15-second assertion therefore measured runner speed as
well as failure detection and failed on hosted CI. The runtime harness still
enforces its overall timeout.

The rendered production player card used the captured Charmeleon snapshot.
Its visible sprite bounds were fully inside the sprite button and centered
within 0.01 pixels in both directions. Clicking the sprite opened its dialog.
