# Step 3.4 Part 7 validation

## Review boundary

This part completes Step 3.4 with local evolution diagnostics, encountered-run
metrics, cumulative regression checks, documentation, and the reproducible
0.6.0 release package. All Part 7 changes remain unstaged for review.

## Metrics and diagnostics

- [x] A new run resets evolution metrics independently from move-access
  metrics.
- [x] Every generated offer records source and target identity, level, context,
  effective trigger, component side, forced/fallback flags, and relevant BSTs.
- [x] Accepted evolutions become `completed`; declined offers become
  `cancelled`.
- [x] The Nincada duplicate path records its independently generated duplicate
  target when that path occurs.
- [x] Completed-run recipes contain a versioned evolution-metrics snapshot.
- [x] Ordinary live Pokemon snapshots still contain no candidate list, graph,
  selected target, or run-level evolution metrics.
- [x] `Ironmon.log` diagnostics now include normal and fusion evolution versions,
  rules, fingerprints, and the current event count.

## Tracker analysis

- [x] Completed-run detail shows totals for offers, completions, cancellations,
  fusion offers, and duplicate outcomes.
- [x] The event table displays source, target, context, trigger, BST transition,
  outcome, and duplicate target.
- [x] Unsupported evolution-metrics schemas fail completed-run archival instead
  of being interpreted with current rules.
- [x] Evolution metrics are rejected when a completed recipe lacks evolution
  generator metadata.

## Automated validation

- [x] The tracker application built with zero warnings and zero errors.
- [x] All 40 .NET tests passed after rebuilding the test assembly.
- [x] Protocol serialization and archive validation cover evolution metrics.
- [x] The existing candidate-list, graph, navigation, authorization, and
  information-boundary tests remained green.

## Embedded-runtime cumulative validation

The final validator ran after game-data initialization inside Pokemon Infinite
Fusion 2's bundled Ruby runtime.

- [x] The complete normal graph passed its invariant validator.
- [x] Reconstructing the saved seed and fingerprints reproduced graph
  fingerprint `ba37e422cd0cb807`.
- [x] A seed increment produced a different graph, covering F7 reseeding.
- [x] Exact rules-version 1 and 2 metadata was recognized for migration.
- [x] Mudkip exposed 18 valid candidate targets for the validation seed, while
  its story target belonged to its selected generated branches.
- [x] Fusion `B282H282` produced both Head and Body branches; its first lookup
  exposed 1,714 Head and 2,191 Body candidates and passed source validation.
- [x] A forced normal offer was accepted through the real Pokemon species
  setter, retained level 30, and changed its metric to `completed`.
- [x] A second offer was finalized as `cancelled`; the completed recipe and
  diagnostic line both contained the two events.
- [x] Cold normal generation completed in 1,250 ms, the first tested fusion
  lookup in 845 ms, and the full embedded suite in 2,391 ms.

## Release and cleanup

- [x] Canonical source was synchronized into the distribution and installed
  game before runtime testing.
- [x] The temporary embedded validator and its report were removed.
- [x] The game completed a clean hidden startup after validator removal.
- [x] The deterministic 0.6.0 archive contains 457 entries, including the
  self-contained tracker and all 33 Ruby scripts, with no PDB or generated XML
  files.
- [x] A second package build reproduced SHA-256
  `8506ebc50d522056e100fc78549fd06778377f53093437e98b26798f6609107d`.
- [x] Canonical, distribution, installed, and packaged Ruby copies match.
- [x] The packaged tracker and installed game passed a concurrent hidden
  startup smoke test; only the exact processes started by the test were stopped.
