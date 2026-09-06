# Release automation freshness refinement — 2026-09-06

This is the historical local-validation stage. Subsequent authenticated hosted work and applied repository settings are recorded in [the orchestration rehearsal](RELEASE_AUTOMATION_ORCHESTRATION.md).

## Scope and result

Work stayed in `Ironmon-release-automation` on `feature/release-automation-test`. The handoff file was preserved. The active feature worktree, installed game, production main, stable release and repository visibility were not modified by this work. No new hosted run was triggered.

The stale sprite-metadata cause is reproduced and fixed. A clean checkout of the newest Hoenn release generated **171,396** eligible fusions with its bundled metadata. Installing the resolved online settings, custom/base sprite lists and credits in that same checkout generated **176,700** through the actual bundled runtime and existing eligibility code.

The local comparison baseline has SHA-256 `2f5b4e528393376f0e76a0f0e449756506cc4aa6fccae56153502002b6825df0`, matching the independently recorded v0.8.5 component in the rehearsal evidence. The new pool has **46 additions and zero removals** relative to that verified baseline. The other three generation-profile component hashes remain unchanged. No eligibility rule was weakened and no count was forced to increase.

## Resolved inputs

| Input | Revision or digest |
| --- | --- |
| Hoenn `releases` commit | `aadf65fb6ba960a15fe1764bfcb3c31bca2a512e` |
| `pif-downloadables` commit | `ad0ce657cd0d9ab403a4698859be3e7503375728` |
| Downloaded settings SHA-256 | `0b3d7f520e1568436facbd4adf50185f612933390c7dee0c182bf9f239aa7524` |
| Custom sprite list SHA-256 | `a8ed40d0ab71821541e89d6a5efc9d6cc2b913e948d62d7bfb7c0daa808737cc` |
| Base sprite list SHA-256 | `b69c06448e28d1795cd0d15a9db64023bd8fd970897b7bb3df381527f647e9cc` |
| Credits SHA-256 | `6b909f5904bc82e29490acd90549a94808b55980777c54cb44eedc82c0e8aaef` |
| Combined input fingerprint | `59bdbe732e55c42e3ad88364a69b7e0beb474bf9e435d9c1e5307455df545b90` |
| Refreshed fusion-pool SHA-256 | `3fa6184041d8608861ad4629545a2b14a965dd98aa45aad0796db6d506c96840` |
| Generated profile ID | `770b5571600cf0f750c32634ed236ebe19e39d1595c761c042de033cbe5ce4db` |

These are provenance for this validation snapshot, not dependency pins for future builds. Credits remain a separately content-hashed online input. Settings and both lists were fetched from the same immutable metadata revision. The resolver checks known destination paths and validates every file before replacing bundled metadata; generators create fresh runtime caches.

## Automation changes

- Ordinary PR iterations, including drafts, now run the full gameplay/runtime, generation-profile, defense and encounter checks as well as core and Windows tracker tests. `PR validation` fails if any prerequisite fails or is skipped.
- A version increase automatically creates a release candidate with both packages, sidecars, all generated audits, the complete fusion membership delta and source/input provenance. Version agreement and release-note headings are checked.
- A merged release reuses an exact matching verified candidate; changed inputs, changed trees or expired artifacts require a full build. Before publication, another input check automatically dispatches a fresh gate if necessary. The user explicitly selected automatic rebuild/retest/publication on upstream changes.
- Publication verifies candidate and remote asset hashes, uploads to a draft first, and leaves identical published assets untouched on retry. Refresh retries are bounded.
- A six-hour scheduler rechecks open same-repository PRs when upstream fingerprints change. The documented GitHub rerun-window and fork-approval limits remain visible.
- Required checks, environment/tag permissions, public-readiness review and iteration-aware costs are documented in [Release automation](../RELEASE_AUTOMATION.md).

## Validation and limits

The PowerShell contract suite passed **23 assertions**, covering JSON-stable fingerprints, content validation, all-or-nothing input validation, path rejection, candidate integrity and identity-aware membership comparison. The offline publication integration suite passed successful publication, immutable retry, upstream-change dispatch, retry exhaustion and rejection of remote tampering. All CI scripts parse successfully.

Workflow validation passed with the sole targeted lint exception for `concurrency.queue`, a property supported by current GitHub documentation but absent from the locally available March 2026 actionlint schema. Action commit pins were resolved from the official repositories in this session. The exception and source link are documented in the adoption guide.

Local runtime validation uses a separate public upstream clone now at the ignored `data/g` path. Two environment failures were diagnosed before the final gate: the sandbox blocked the NuGet vulnerability feed; then the original deep checkout path exceeded the Windows packaging tool's path limit. Network-enabled execution and a shorter isolated path resolved these without changing production code or test expectations.

The final complete `Build-TrackerRelease.ps1` gate exited **0**: generation-profile checks, the entire bundled runtime suite, **328 core tests**, **11 app tests**, **80 cosmetics assertions**, **1,667 defense assertions**, and the encounter/fusion probe all passed. The probe reported 176,700 pool members and no mapping errors. Both packages built successfully.

The new archive verifier read every file in both ZIPs, verified their SHA-256 sidecars, matched all **119 Ruby scripts** against source, checked required documentation and sensitive-file exclusions, and verified all **134 shared Data files** are identical across variants. The final live resolver run produced the same input fingerprint as the packaged snapshot.

| Local validation package | Bytes | SHA-256 |
| --- | ---: | --- |
| Self contained | 77,693,766 | `66fd4b3f3aaa6842c9c3ba6c507a0ab881fdb15462bbb268d17dd896eb7fb1ed` |
| Runtime required | 40,892,982 | `354231899778260a74c3cb64c92a37e5cef48d8fd8aca76e1a0462ba6a329802` |

The new production triggers, scheduler, candidate reuse and automatic rebuild chain still require a hosted adoption rehearsal. Offline API doubles verify control flow, not GitHub authorization, environment restrictions or check attachment. The existing rehearsal establishes the earlier hosted build/publication mechanism only. The CLI was unauthenticated, so current remote protection/billing settings were not claimed as verified.

Ignored evidence: `docs/audits/generated/freshness-inputs/upstream-inputs.json`, `freshness-before/`, `freshness-after/`, `freshness-stable-comparison.json`, and `freshness-full-gate*.log`. Test outputs and packages remain in `data/g/Ironmon/`; these are local validation artifacts using version 0.8.5, not replacement published releases.
