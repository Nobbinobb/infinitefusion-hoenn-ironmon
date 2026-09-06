# Hosted release rehearsal

2026-09-06 — **The complete hosted build, test, merge and publication flow passed.** The fake prerelease was downloaded and verified, then removed with its tag, temporary integration branch and Actions artifacts. Production `main`, stable release `v0.8.5`, and the active feature worktree were not changed by this experiment.

## What was tested

The separate `Ironmon-release-automation` worktree started from main at `f68cc8ace8639b18365caeec1daae396b7e6496f`. Its `feature/release-automation-test` branch was merged through [disposable PR #6](https://github.com/Nobbinobb/infinitefusion-hoenn-ironmon/pull/6) into `release-test/integration-20260906`, never into production main.

Each candidate and CI run downloads the **newest upstream Hoenn releases branch**. Upstream `infinitefusion/infinitefusion-hoenn-public` has no GitHub Release entries, and its main branch contains older game data. This experiment resolved releases to `aadf65fb6ba960a15fe1764bfcb3c31bca2a512e` (1.2.2 patch; internal game version 6.8.2). That recorded revision is provenance, not a fixed dependency pin for later runs.

The successful candidate records source commit `235e3d2d968eaf96ae9eafc46de402bcdb678c08`, source tree `be21244c1136b58dd7a0c63be1192bd73416bfb1`, upstream commit/version, archive sizes and SHA-256 checksums. The regular test merge produced `9b324daebc450420beda633b801010eb3151ae5a` with the identical source tree. Publication verified that relationship and uploaded the original candidate files; it did not rebuild or download different game inputs after merge.

- [Full release gate: 34010098470](https://github.com/Nobbinobb/infinitefusion-hoenn-ironmon/actions/runs/34010098470) — passed generation-profile checks, 80 cosmetics assertions, the complete bundled-game runtime suite, 328 core tests, 11 app tests, 1,667 defense assertions, the encounter/fusion probe, and both package builds.
- [Final current-source CI: 34010628095](https://github.com/Nobbinobb/infinitefusion-hoenn-ironmon/actions/runs/34010628095) — all three jobs passed.
- [Automatic post-merge publication: 34011066186](https://github.com/Nobbinobb/infinitefusion-hoenn-ironmon/actions/runs/34011066186) — passed and created the explicitly marked prerelease `release-test-34011066186`.

## Measured cost

These are measured job durations rounded separately to whole minutes, priced at current standard-runner overage rates. They are **not additional charges when covered by the account's allowance**.

| Work | Measured runner minutes | List-price equivalent |
| --- | ---: | ---: |
| Successful full candidate build | 20 Windows | $0.200 |
| Automatic publication after merge | 1 Linux | $0.006 |
| Build and publication combined | 20 Windows + 1 Linux | **$0.206** |
| Final separate PR CI | 14 Windows | $0.140 |
| Successful cycle including that CI | 34 Windows + 1 Linux | **$0.346** |
| Entire experiment, including failures and cancellations | 174 Windows + 1 Linux | **$1.746** |

The successful candidate job took 1,193 seconds. Two earlier successful CI runs used 12 and 14 rounded Windows minutes, so ordinary CI measured about $0.12–$0.14 at overage rates. Runner and network speed will vary; a future upstream update can also change generation time.

The authenticated GitHub account reports the Pro plan. Its final available billing snapshot showed this repository at 316 Windows minutes and 1 Linux minute for the billing period, including earlier work: $3.166 gross execution usage, fully discounted, **$0 net**. The snapshot also reported $0 net storage. Billing can lag, especially storage, so this is an observed snapshot rather than a final invoice.

GitHub Pro includes 3,000 monthly Actions minutes and 1 GB of shared artifact/Packages storage. Standard Windows execution is currently $0.010/minute and Linux $0.006/minute above allowances. [GitHub billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions), [runner pricing and rounding](https://docs.github.com/en/billing/reference/actions-runner-pricing).

## Storage and public repositories

The candidate Actions artifact was 118,473,221 bytes (about 113 MiB). CI catalogs were about 592 KiB. Both have explicit one-day retention, and all experiment artifacts were deleted after verification. The downloaded game stayed on disposable runner disk and was not uploaded or cached.

At $0.25 per GB-month above the shared storage allowance, keeping this candidate for a whole day would cost about **$0.00092**, or about $0.028 for a month. Deletion stops future accrual; it does not reverse previously accrued usage. [Storage billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions).

**Standard hosted-runner execution is free for public repositories**, including this workflow's game execution, generation, tests and builds. Larger runners have different rules. Release assets are separate from Actions artifacts: GitHub documents no total release-size or release-bandwidth limit, with each asset below 2 GiB. [Public-runner billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions), [release asset limits](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases).

## Findings and fixes

1. **Hosted graphics:** the bundled game needs software OpenGL on the standard Windows runner. Setup downloads a checksum-verified Mesa package into the disposable game directory and enables software rendering and null audio. Mesa is not included in the release ZIPs. A short startup check precedes toolchain installation.
2. **Fresh generation:** eager runtime catalog validation previously tried to load an installed profile before generation could create it. That initialization now has its own manifest entry. Generation skips only that entry; normal gameplay still enforces catalog validation. The startup test checks both clean initialization and rejection of missing runtime data.
3. **Stale CI catalogs:** CI now generates from its tested source and newest game checkout, then shares a same-run artifact with the two test jobs. It no longer extracts generated inputs from the previous Ironmon release. The expected game-version metadata comes from the downloaded game.
4. **Fixture metadata:** the seeded-world test substitutes a fixed fusion pool but included the installed profile ID in its golden snapshot. It now normalizes only that fixture metadata while separately checking the actual exported profile ID. Its original golden checksum remains unchanged.
5. **Runtime cost:** diagnostics located a timeout in full reverse-material indexing, repeatedly hashing an identical seed/input prefix. Reusing the prefix and continuing the same FNV-1a hash removes redundant work. Boundary-vector comparisons and the unchanged golden snapshot passed, as did the full runtime suite within its existing 300-second limit. No timeout was increased. Runtime validation now runs before tracker compilation to avoid those builds when it fails.

The fresh pool contained 171,396 eligible fusions, compared with 176,654 in the verified published v0.8.5 archive: 189 memberships added and 5,447 removed. The other three profile-component hashes matched. This proves the published catalogs and fresh upstream checkout are not interchangeable. It does not establish that the upstream checkout includes every later online sprite update.

## Artifact verification and cleanup

Both candidate ZIPs and both published ZIPs were independently downloaded and checked. Every entry was read; all 119 Ruby scripts matched the source after newline normalization; required documents were present; no forbidden maintainer/key files were found; and all 134 shared Data files matched between variants. ZIPs, sidecars and candidate provenance were byte-identical before and after publication.

| Variant | ZIP bytes | SHA-256 |
| --- | ---: | --- |
| Runtime required | 40,835,384 | `b5be6830aa6c48dd923282ef400c5469f5d35aa05fcb05d3f959874653f53a58` |
| Self contained | 77,636,168 | `9286c13fbbfcfbe62aaf483eaa3222853f35470d0d1f0a7102bd9e3ebbece9b0` |

Cleanup removed release ID 383461702, its test tag, the isolated integration branch, and 11 experiment artifacts totaling 121,505,875 bytes. Verification confirmed their absence. Stable release ID 382044189 (`v0.8.5`) retained the same asset IDs, sizes and digests, and production main remained at `f68cc8a`. Run history and local JSON/log receipts under `docs/audits/generated/release-rehearsal` remain as evidence. The source branch and this report remain available for review.

## Production adoption

This proves the mechanism, but the temporary branch triggers are not a production release policy. A production workflow can prepare version changes in a release PR, build its frozen candidate, then publish the original validated files after merge or an approved tag. Checksums can remain in candidate provenance and release sidecars, avoiding another source commit solely to record build output.

A manual workflow must exist on the default branch before GitHub accepts its manual trigger, which is why this isolated experiment used branch pushes. Adoption should retain required PR checks, define how expired candidates are rebuilt and reviewed, and decide whether a new upstream release after candidate freeze requires a fresh candidate. Each new candidate should resolve the newest upstream release; publication should preserve the already-tested candidate. [Workflow triggers](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow), [merged-PR events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows).
