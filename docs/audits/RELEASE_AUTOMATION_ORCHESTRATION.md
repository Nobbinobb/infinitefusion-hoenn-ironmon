# Hosted release orchestration rehearsal — 2026-09-06

## Scope

The new orchestration passed in the private [disposable rehearsal repository](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906). The production automation branch is `feature/release-automation-test` in `Ironmon-release-automation`. It is ready for its adoption PR; production main is not merged by this rehearsal.

The initial rehearsal main commit `cb94b46d424912b7a2889571f70a04da909d062b` has exactly the same source tree (`0648736a7f9c1ac5d86d04a6afbc3d4fb3340df6`) as production proposal commit `1983280`. Its only release baseline is a verified copy of the previous v0.8.5 runtime-required package. Production main (`f68cc8ace8639b18365caeec1daae396b7e6496f`), the real v0.8.5 release, repository visibility, the installed game and the other local worktrees are outside the rehearsal's mutation scope.

After corrections, production code commit `f285b3ec49f4e79e5de5ba6d0dd7d31ae44bbfb3` and rehearsal PR #3 head `ac26e2e89a39ec8bd095db10daf09daea911c50f` match **724 workflow, tool, runtime, tracker and test files byte-for-byte as Git blobs**. Only the disposable mod/tracker versions and the confined resolver simulation differ; there are no extra execution files on the rehearsal PR branch. The simulation does not exist in production code.

## Repository configuration

| Production setting | Applied state |
| --- | --- |
| Existing `Main PR and checks` (`22278807`) | Active and unchanged: PRs, strict up-to-date Core tracker tests / Windows app tests, no force push or deletion |
| `Release automation required checks` (`22383153`) | Prepared **disabled**: PR validation / Release readiness, Actions integration 15368, strict, no bypass |
| `Immutable release tags` (`22383151`) | **Active**, `refs/tags/v*`, prohibit update/deletion, allow creation, no bypass |
| `release` environment | Created, main **branch** only, deployment policy `59246278`, no second reviewer gate |
| Default workflow permissions | Read-only, PR review approval disabled |
| Visibility | Private |

Equivalent branch/check/tag rules and the release environment are active in the disposable repository. No administrator bypass is used to merge rehearsal PRs. The new production checks remain disabled until the reviewed automation is on main; existing PRs continue under their existing checks.

The private-plan environment API rejected reviewer/wait-timer protection fields, even when empty. The supported main-only deployment policy was applied successfully without those unnecessary fields. The fork-contributor approval endpoint separately rejected configuration while private. On an authorized move to public, set its policy to `all_external_contributors` and verify it. Repository-wide mandatory Action SHA pinning can be enabled after the pinned workflows reach main; enabling it earlier would affect the old unpinned main workflow.

## Hosted cases

### Incomplete release is blocked

[Rehearsal PR #1](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/pull/1) initially increased only the tracker version. [Release candidate run 34029482405](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34029482405) failed metadata validation and **Release readiness**. A normal merge request for the exact head was rejected by GitHub's branch policy.

Correcting mod/tracker versions and adding the current release notes produced head `0248d4b41f701c2ba1819a04a1906554ca5cf365`. Both workflows started automatically on the update, and superseded [Tracker CI run 34029482354](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34029482354) was canceled by workflow concurrency.

### Tag immutability

GitHub allowed creation of `v0.0.0-guard-rehearsal` in the disposable repository, then rejected both a forced update and deletion with HTTP 422 repository-rule violations. Reading the ref back confirmed its original target remained unchanged. This tests the same tag-rule payload applied in production, without touching a real release tag.

### Current-source validation

[Tracker CI run 34029589265](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34029589265) passed generation-profile validation, the full bundled-runtime suite, 80 cosmetics assertions, 1,667 defense assertions, and the encounter/fusion probe. The probe reported 176,700 pool members and no mapping errors. All 328 tracker-core tests and 11 Windows app tests passed with no skips. The aggregate **PR validation** succeeded.

[Release candidate run 34029589274](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34029589274) passed the complete release builder, both package builds, current-note/archive verification and membership comparison. **Release readiness** succeeded. Downloaded packages independently passed all-entry readability, checksums, source-script parity, current release-note content, documentation/exclusion checks and shared Data parity. The comparison recorded **46 additions, zero removals** against the verified v0.8.5 pool (176,654 → 176,700).

The fingerprint was `59bdbe732e55c42e3ad88364a69b7e0beb474bf9e435d9c1e5307455df545b90`, game commit `aadf65fb6ba960a15fe1764bfcb3c31bca2a512e`, and pool SHA-256 `3fa6184041d8608861ad4629545a2b14a965dd98aa45aad0796db6d506c96840`.

### Unchanged inputs, approved source and deployment branch

[Watcher run 34030585099](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34030585099) completed against PR #1 after its checks passed, requesting zero reruns because inputs were unchanged. Both original validation runs remained at attempt 1.

GitHub accepted the ordinary merge of PR #1 with all four required checks green, producing `feb028a1d71c285a224b08d2ee5aa95002f9fd58`. No administrator bypass was used. [Publication run 34030627586](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34030627586) started automatically from that push, successfully reused the reviewed candidate and skipped the full rebuild. All seven downloaded assets from that publication build were independently confirmed byte-identical to the PR candidate.

[Unapproved-source run 34031155923](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34031155923) attempted a manual publication of PR #2's unmerged head. The plan job rejected it; input resolution, build and publication were skipped.

[Environment probe 34031162214](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34031162214) used a harmless, read-only test workflow on `codex/environment-guard-rehearsal`. GitHub rejected access to the `release` environment because the branch was not main. This test workflow exists only on the disposable probe branch.

### Faults found and corrected

- Distribution packaging selected hardcoded 0.8.5 release notes. It now selects notes from the project version, and archive verification compares their complete content with the corresponding source notes.
- The first hosted publication reused correct bytes but failed immediately after draft creation: GitHub's by-tag REST lookup returned 404 for the unpublished draft. No assets were uploaded. Publication now resolves the draft with `gh release view`, validates the numeric release ID/tag and reads by ID. Regression tests cover initial creation, partial-draft recovery, immutable retry, mismatched identities and remote tampering. [CLI release lookup](https://cli.github.com/manual/gh_release_view), [release API](https://docs.github.com/en/rest/releases/releases).
- PowerShell 7.5 JSON date deserialization combined with locale-dependent reparsing could interpret September 6 as June 9 and falsely reject a recent run as outside GitHub's rerun window. The scheduler now preserves typed dates and parses string dates invariantly; German-culture and string-date cases are covered.
- A missing input artifact can represent a pruned successful release candidate, not just an ordinary PR. The scheduler checks completed build jobs before skipping candidate refresh. The three-attempt input-resolution failure limit no longer suppresses successful runs whose artifacts were pruned. Offline regression cases cover both distinctions.

The corrected publication and scheduler suites passed locally, and the first corrections passed in [PR #2's metadata run](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34030901433).

One ordinary core-test job in [run 34030901421](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34030901421/attempts/1) failed after a three-second polling wait for a persisted completed-run recipe (327 passed, one failed). The unchanged failed-job rerun passed all 328 tests, as did the separate full release candidate. The failure is preserved in the evidence; it was not bypassed. A subsequent test-only fix waits on the archive's save notification with a ten-second bound, handles already-completed saves, and includes the archive error if it times out. The focused test and all 328 core tests passed locally, followed by the full hosted PR #3 validation. Production persistence behavior is unchanged.

### Draft recovery and immutable publication retry

PR #2 passed all four required checks and merged normally at `20f71817552c51e5235147a80e104d9bf0cec784`. Its corrected trusted publication tools then recovered the original empty v0.8.6 draft by rerunning only the failed publication job in [run 34030627586](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34030627586).

The private test release published all seven assets. Their sizes and SHA-256 digests independently matched the original PR #1 candidate; the release tag resolves to approved commit `feb028a1d71c285a224b08d2ee5aa95002f9fd58`. A further publication-job rerun succeeded while preserving all seven remote asset IDs, sizes and digests. This verifies draft recovery and immutable retry with GitHub's actual token, release API, tag rules and main-only environment.

### Automatic full rebuild after simulated input change

[PR #2](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/pull/2) uses a resolver fixture confined to the disposable repository. It appends a harmless Ruby comment to downloaded settings, hashes the actual changed bytes, and labels the simulation in the input manifest. Phase A is used in PR checks and the initial publication inputs; phase B is used immediately before publication and throughout automatic refresh attempt 1. The prior 0.8.6 draft recovery is explicitly exempt so its original unmodified inputs and reviewed bytes can be recovered. No real upstream repository or production resolver contains this fixture.

| Snapshot | Fingerprint |
| --- | --- |
| Simulated phase A | `a992df8617e595d5ffb7c021886246cdf5392faf0ac6d721287797ee19b061f6` |
| Simulated phase B | `4d6ceef26d4eabf3491acf3ec234142da2d3a95cc9a149afd4aced6b33c374ee` |

Only the settings bytes differ; the actual upstream game revision is unchanged. The successful PR candidate's source tree is `491085581f29e9b54e8c912d05102a5c65a40044`.

[Initial publication run 34031967642](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34031967642) reused candidate A, detected B at its final check, and withheld the 0.8.7 release. It dispatched [fresh build 34032087840](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34032087840) automatically; GitHub records `github-actions[bot]` as both actor and triggering actor. The fresh run resolved B, rejected reuse of A, regenerated all data/audits, passed the full gate and built both packages. All 328 core tests, 11 app tests, 80 cosmetics assertions, 1,667 defense assertions, runtime/profile checks and the encounter probe passed; mapping errors remained empty.

The final fingerprint matched B, and [private test v0.8.7](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/releases/tag/v0.8.7) published automatically from approved source `20f71817552c51e5235147a80e104d9bf0cec784`. Its seven remote asset sizes and hashes independently matched the downloaded rebuilt candidate, whose `run_id` identifies the automatic refresh. Both archives passed independent verification again. The pool remained at 176,700 with zero additions/removals against the now-published test v0.8.6, as expected for a harmless settings-comment simulation. The tag still targets the approved release commit even though a subsequent ordinary PR advanced main.

### Automatic ordinary PR recovery

[PR #3](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/pull/3) keeps the version unchanged and includes the final scheduler/test refinements. Its [release-readiness run 34032008451](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34032008451) passed metadata and automation contracts, skipped candidate/input jobs, and produced no release packages.

The initial [Tracker CI run 34032008453](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34032008453) was intentionally canceled after resolving inputs. Only its disposable `upstream-inputs` artifact (`9988917237`) was deleted. GitHub rejected a normal merge for the exact PR head while validation was canceled.

[Watcher run 34032175151](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34032175151) then succeeded and automatically reran Tracker CI on the same head. GitHub records attempt 2 triggered by `github-actions[bot]`; a replacement input artifact (`9988975641`) was uploaded. The ordinary release-candidate workflow was not needlessly rerun. Full recovered gameplay, generation, core and app checks passed, including the revised connection-test wait. All four required checks were green before PR #3 merged normally at `4b3d65ad6f6dc1560a3bdce08b3fea934ad6e81c`.

That ordinary merge triggered [planning run 34033139346](https://github.com/Nobbinobb/ironmon-release-rehearsal-20260906/actions/runs/34033139346), which succeeded and skipped inputs, build and publication because the version was unchanged. No release was created for the ordinary merge.

The disposable repository enforces repository-wide full-length Action SHA pinning during these publication/recovery runs. Its scheduler was briefly disabled between the unchanged-input test and the controlled recovery case to prevent a scheduled run from interfering with the simulation, then re-enabled for the real watcher dispatch.

## Measured execution cost

The first passing ordinary PR validation used 12 Windows minutes for generation/gameplay, 3 for core tests and 7 for app tests; its input and aggregate jobs used one Linux minute each. Ordinary metadata/readiness adds two Linux minutes. That totals **22 Windows + 4 Linux minutes**, or **$0.244** before included usage at $0.010/Windows minute and $0.006/Linux minute.

Release candidate generation added **20 Windows + 1 Linux minute**, making a complete release PR iteration **$0.450**. The first candidate reuse took two rounded Windows minutes. The unchanged-input watcher took one rounded Linux minute.

The automatic fresh release run used **24 Windows + 3 Linux minutes**, approximately **$0.258**. Across this entire disposable rehearsal, **20 workflow runs and 69 allocated jobs** used **169 Windows + 46 Linux minutes**, approximately **$1.966** before included-usage discounts. This includes failed/canceled attempts, draft recovery, immutable retry, the controlled full rebuild and ordinary PR recovery. Equivalent allowance consumption at the handoff's observed $0.006 accounting is about 327.7 minutes. Production adoption-PR validation and the earlier prototype experiment are separate.

These are per-job durations rounded upward to whole minutes, excluding queue time, using the posted runner rates; they are not invoice measurements. Initial failures, canceled iterations, probes and the controlled rebuild are additional rehearsal costs. [Runner pricing](https://docs.github.com/en/billing/reference/actions-runner-pricing). Monthly planning in [the adoption guide](../RELEASE_AUTOMATION.md) includes repeated PR iterations instead of assuming one run per PR.

## Evidence

After the completed rehearsal and explicit cleanup approval, all temporary Actions artifacts were removed after saving local copies, Actions were disabled, and the private rehearsal repository was archived. Its test releases and their assets remain available for review. Read-back confirmed zero Actions artifacts, `enabled: false` and `isArchived: true`.

Ignored raw evidence is under `docs/audits/generated/orchestration-rehearsal/`: repository/settings snapshots, exact ruleset payloads, rejected merge/tag/environment receipts, sanitized hosted logs, downloaded candidate/input artifacts, source-equivalence receipts, every run attempt's job data, cost receipts and the isolated rehearsal checkout. GitHub Actions artifacts are temporary; the independent local snapshots and private test release assets preserve the verified results. Historical local-only results are in [the freshness audit](RELEASE_AUTOMATION_FRESHNESS.md); they are not substitutes for hosted results.
