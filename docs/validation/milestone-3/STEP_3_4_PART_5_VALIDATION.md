# Step 3.4 Part 5 validation

## Review boundary

This part adds authorized generated-evolution candidate queries and presents
them as clickable tracker lists. Selected destination summaries are retained
separately for the evolution graph, whose presentation remains reserved for
Part 6.

## Authorized lookup protocol

- [x] Completed-run recipes persist every normal- and fusion-evolution schema,
  rules version, source fingerprint, base-stat dependency, and fusion target
  pool identity needed to reconstruct the run.
- [x] Current recipes are rejected with `incompatible_evolutions` when their
  evolution metadata no longer matches the installed game data.
- [x] Legacy recipes without evolution metadata remain valid and retain native
  evolution lookup.
- [x] Exact rules-version-1 and rules-version-2 active saves and completed-run
  recipes upgrade to rules version 3 while preserving the saved seed; altered
  legacy fingerprints remain incompatible.
- [x] Normal Pokemon expose one paged candidate query; complete fusions expose
  separate Head and Body candidate queries.
- [x] Candidate queries union every conceptual branch for the requested source
  and side, without identifying which destination was selected.
- [x] A non-empty preferred set contains every structurally valid candidate in
  the branch's 90%-115% reference-BST window across permitted stage buckets.
- [x] Closest stronger fallback candidates appear only when the preferred set
  would otherwise be empty.
- [x] Normal candidates share at least one type with the source Pokemon; fusion
  candidates share at least one resulting fusion type with the evolving Head
  or Body component. Native destinations do not influence this comparison.
- [x] Candidate rows contain only exact species identity, localized name,
  sprite path, and generated BST.
- [x] Separate selected-target summaries retain effective method labels for
  Part 6 but are not used by the Part 5 lists.
- [x] Candidate responses omit original destinations, original BSTs,
  assignment buckets, fallback decisions, selected-target identity, and other
  generator diagnostics.
- [x] Authorized Debug and completed-run lookup reconstruct identical targets
  through the same deterministic generators.
- [x] Reconstruction does not consume or mutate gameplay random state.
- [x] Ordinary live player snapshots expose none of the generated target
  collections.

## Tracker presentation and navigation

- [x] Normal lookup renders one `EVOLUTION TARGETS` candidate list.
- [x] Fusion lookup renders independent `HEAD EVOLUTIONS` and `BODY
  EVOLUTIONS` lists when those sides can evolve.
- [x] Each visible candidate row contains only the icon, Pokemon name, and
  generated BST.
- [x] Lists use 50-row server-side pages, show the filtered total, and provide
  an internal name filter.
- [x] Normal lists hide the filter below 50 total candidates. Fusion Head and
  Body lists always show it.
- [x] Fusion Head and Body candidates share one list area with a two-tab toggle;
  only the selected side is rendered.
- [x] The complete row is a button that navigates to the exact candidate
  through the lookup explorer's existing history path.
- [x] Existing Back and Forward controls therefore include target-list
  navigation.
- [x] The authorized Pokemon inspector includes an Evolutions subtab for its
  currently inspected player or enemy Pokemon; candidate clicks open the exact
  Pokemon in authorized lookup.
- [x] Unrestricted lookup always indexes normal Pokemon and custom fusions,
  independent of encounter policies. Fusion-material searches remain
  normal-only.
- [x] Fusion stat-dominance labels render as compact `H`/`B` badges before the
  smaller stat abbreviations rather than uneven secondary lines beneath them.
- [x] Current generated recipes suppress misleading native destination and
  predecessor lists; legacy recipes continue to show them.
- [x] The native MAUI tracker application builds with zero warnings and zero
  errors. No browser-hosted tracker surface was available for visual browser
  inspection; graph presentation was implemented in Part 6.

## Automated tracker validation

- [x] The controlled single-node .NET test run passed all 38 tests.
- [x] Protocol round-trip coverage verifies selected-target methods and paged
  candidate request/response data for completed and Debug channels.
- [x] Archive coverage verifies current metadata persistence, rejection of
  partial evolution metadata, and acceptance of legacy recipes.

## Embedded-runtime validation

The automated suite ran after normal game-data initialization inside Pokemon
Infinite Fusion 2's bundled Ruby runtime.

- [x] Bulbasaur returned one normal target matching the gameplay generator's
  exact identity, generated BST, and effective method labels.
- [x] Fusion `B382H133` returned eight Head targets and two Body targets,
  matching the gameplay generator with no duplicated exact target.
- [x] Ditto returned no generated targets.
- [x] Serialized target fields were limited to `species_id`, `species_name`,
  `sprite_path`, `base_stat_total`, and `effective_methods`.
- [x] Authorized Debug matched completed-run lookup.
- [x] Unauthorized Debug lookup was rejected with `debug_forbidden`.
- [x] Ordinary live lookup exposed no generated target fields.
- [x] A legacy recipe returned native evolution relations and no generated
  targets.
- [x] A changed fusion target-pool fingerprint was rejected as
  `incompatible_evolutions`.
- [x] Unrestricted search for `mudkip` included normal Mudkip even when fusion
  lookup was also available.
- [x] Normal Mudkip exposed 18 valid preferred candidates after the shared-type
  rule was applied.
- [x] Fusion `B282H282` exposed 4,036 Head candidates and 748 Body
  candidates, while its selected graph data still contained exactly one Head
  and one Body destination.
- [x] Every measured Mudkip candidate shared at least one type with Mudkip or
  the evolving Mudkip component, as appropriate.
- [x] Simulated rules-version-1 and rules-version-2 metadata were recognized for
  exact migration to version 3; altered legacy fingerprints remain rejected as
  `incompatible_evolutions`.
- [x] All eight Eevee stone branches required their intended elemental type:
  Water, Electric, Fire, Psychic, Dark, Grass, Ice, and Fairy. Normal was
  replaced rather than added.
- [x] Normal Eevee generated all eight targets and exposed 123 unique valid
  candidates across its conceptual branches.
- [x] Eevee/Eevee generated all 16 component-side branches, and every complete
  fusion target contained the type required by its activating stone branch.
- [x] Candidate pages returned at most 50 rows, name filtering retained exact
  matches, and serialized fields were limited to `species_id`, `species_name`,
  `sprite_path`, and `base_stat_total`.

## Packaging and cleanup

- [x] Canonical source was synchronized into the distribution and installed
  game through `tools/Build-Distribution.ps1` before embedded-runtime testing.
- [x] All canonical Ruby scripts match their distribution and installed copies
  byte-for-byte.
- [x] With the validator removed, the game loaded normally and remained running
  at the ten-second smoke-test checkpoint.
- [x] Only the exact hidden validation and smoke-test processes were stopped;
  no pre-existing Infinite Fusion process was present.
- [x] The temporary runtime validator and output report were removed.
- [x] Repository whitespace validation passed.

## Player review

Part 5 is ready for review. Part 6 evolution-graph presentation has not begun.
