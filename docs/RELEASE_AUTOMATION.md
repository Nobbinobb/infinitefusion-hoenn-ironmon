# Release automation

This workflow is active on `main`. [Local validation](audits/RELEASE_AUTOMATION_FRESHNESS.md) and the completed [hosted orchestration rehearsal](audits/RELEASE_AUTOMATION_ORCHESTRATION.md) record the implementation evidence. The private rehearsal verified required checks, candidate reuse, draft recovery, immutable publication, automatic full rebuilding after a simulated input change, and ordinary PR recovery. The repository became public on 2026-09-06 after an exposure audit and settings verification.

## Maintainer workflow

Develop through PRs to `main`. Every opening, update, reopening and ready-for-review event runs checks, including draft PRs. Superseded runs are canceled. There are no path filters that could omit a gameplay or tracker change.

For a release, prepare a PR that increases `Ironmon::VERSION` in `src/foundation/Core.rb`, `ApplicationDisplayVersion` and `Version` in the tracker app project, and its numeric `ApplicationVersion`. Add `docs/releases/RELEASE_NOTES_<version>.md` headed `# Ironmon <version>`, and update example package names in `packaging/README.md` and `docs/guides/INSTALLATION.md`. The distribution builder automatically selects the notes matching the project version, and archive validation checks their content. Branch names and labels do not control publication; the reviewed version increase does. An agent can prepare these source changes locally without generating or committing binary data.

The release PR automatically produces both Windows player packages, checksums, `candidate.json`, and `release-evidence.zip`. Review its notes, full test results and fusion-pool membership comparison before merging. Release notes and versions are validated together. Ordinary PRs still receive the `Release readiness` check, which validates metadata without building packages.

Merging a version increase starts publication. If the reviewed candidate's source tree and upstream fingerprint match the approved merge and current inputs, its bytes are reused. Otherwise, the approved source is regenerated, fully retested and packaged. This includes expired candidates, changed game/sprite inputs, and merge methods that produce a different tree. A failed build publishes nothing.

Immediately before publication, the publisher resolves upstream inputs again. A change dispatches a new full gate automatically, up to three refresh attempts. The publisher never builds with its write token. This implements the user's chosen policy: approved source is automatically rebuilt against newer upstream inputs, with publication only after the checks pass. A continuing upstream update or unavailable server eventually fails visibly instead of publishing stale data. There is necessarily a short interval between this final network check and publication; independent upstream servers cannot participate in an atomic GitHub release transaction.

Uploads first go to a draft release. All seven remote asset sizes and SHA-256 digests are verified before it becomes public. Rerunning publication accepts identical existing assets and never overwrites a published artifact or tag. A partial draft can be resumed if its assets match. A failed or conflicting draft remains inspectable.

## Validation and fresh inputs

`Tracker CI` downloads the newest commit on the upstream Hoenn `releases` branch, using an immutable commit only for that run. It obtains the current settings, custom sprite list and base sprite list from one resolved `pif-downloadables` commit, plus current online credits. Destination paths, content formats, file lengths and SHA-256 hashes are checked before installation. Refresh failure never falls back to bundled lists. Each generator starts a new game process and sprite cache.

All generated catalogs come from the tested source and this input snapshot. Previous release data is used only for the membership audit, never as a generation cache. The full runtime and generation-profile suites, defense tests and encounter/fusion probe run on ordinary PRs, followed by serialized core tracker tests and Windows app tests. Release candidates run the full existing `Build-TrackerRelease.ps1` gate and both package builds.

The `Check upstream updates` workflow resolves inputs every six hours and reruns current open same-repository PR checks when the fingerprint changes or their snapshot expires. It does not build on unchanged inputs. The scheduler reads downloaded manifests as data and never executes PR source with write permissions. Scheduling is best effort; GitHub can delay scheduled jobs. New PR iterations and every publication independently resolve current inputs.

GitHub only permits rerunning workflows for 30 days after the original run. If a dormant PR exceeds that window and needs refreshed validation, the watcher fails with the affected PR identified; a branch update starts a new run. Fork PRs retain GitHub's contributor approval flow and are not automatically rerun by the privileged scheduler. Maintainers can approve/rerun them, and merged release source always receives the fresh production gate. [Rerun limits](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs), [fork approval](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/approve-runs-from-forks).

Input snapshots and release candidates are retained for seven days; tracker-only catalogs for one day; failure diagnostics for three days. A rerun replaces same-run artifacts so scheduled refresh does not fail on an existing artifact name. Published evidence contains the game revision, metadata URLs/hashes, generation profile, audits, test reports, and complete added/removed fusion identities. Upstream may legitimately remove entries: reductions are exposed for review, not silently discarded or forced to be monotonic.

## Active repository settings

The authenticated administrator applied and read back these settings on 2026-09-06. The existing active **Main PR and checks** ruleset (`22278807`) is unchanged: PR-only changes, strict up-to-date checks, required **Core tracker tests** / **Windows app tests**, and no force pushes or deletion. No bypass was added.

1. **Release automation required checks** (`22383153`) is **active**. It requires **PR validation** and **Release readiness**, both bound to GitHub Actions integration `15368`, with strict up-to-date enforcement and no bypass. Together with the tracker checks, all four checks are required before merge. Do not enable merge queue without adding its `merge_group` triggers and validation policy.
2. The **release** environment exists with a custom deployment policy permitting only the **main branch** (not a similarly named tag). It has no second reviewer gate. Write permissions remain scoped to publication (`contents: write`, `actions: write`) and upstream scheduling (`actions: write`). The default workflow token is read-only and cannot approve PR reviews; no personal access token or external signing secret is required.
3. **Immutable release tags** (`22383151`) is **active** for `refs/tags/v*`: updates and deletions are prohibited, creation is allowed, and there are no bypass actors. The equivalent rehearsal rule was verified by creating a disposable tag and receiving GitHub rule violations for both update and deletion attempts.
4. Adoption is complete. Scheduled/manual workflows are available on the default branch. Automatic PR merging remains disabled. Release PRs publish automatically only after acceptance. The separate additional-reviewer rule remains disabled for the solo-maintainer workflow.

The release environment permits only the main branch, has no second reviewer gate, and disallows administrator bypass. Repository-wide full-length Action SHA pinning is enabled. Native release immutability is also enabled: attach every file to a draft before publishing. The private rehearsal verified successful draft publication and rejection of a later attachment rename.

The workflow uses GitHub's documented `queue: max` for production release/publication concurrency, avoiding replacement of a waiting release. Up to 100 runs can wait; excess requests fail/cancel visibly. The locally available March 2026 actionlint does not yet recognize that property, so local lint ignores only that specific diagnostic and validates everything else. [Concurrency queues](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#concurrency).

## Public repository protections

Keep fork PRs on `pull_request` with read-only tokens and no secrets; never run their code from a privileged `pull_request_target` job. Require approval for outside contributors' workflow runs. Retain protected-main review and required tests, especially for workflow and release-tool changes. Actions here are pinned to verified commit SHAs; review updates to these pins. [GitHub security guidance](https://docs.github.com/en/actions/reference/security/secure-use).

The Actions fork approval policy is **all external contributors** (`all_external_contributors`), verified after the public transition. Secret scanning, push protection, Dependabot alerts and security-fix PRs, and private vulnerability reporting are enabled. These settings preserve the existing testing policy.

The public-transition audit inspected Git history, historic ZIPs, release downloads, and Actions records; automated scans found no exposed secrets in that material. Historic ZIPs remain in source history. Obsolete 0.8.4 binary downloads without the bundled notices present in 0.8.5 were withdrawn, and temporary Actions artifacts were cleared after local backups. The 0.8.4 source tag and current 0.8.5 downloads were preserved. The license covers original Ironmon work; upstream game downloads remain runner inputs and are not uploaded as game archives. [Visibility changes](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility).

## Costs and iteration planning

As verified on 2026-09-06, standard Windows runners cost $0.010 per actual minute and standard Linux runners $0.006 above included usage. Standard hosted execution is free for public repositories. Larger runners and storage have separate billing. [Runner pricing](https://docs.github.com/en/billing/reference/actions-runner-pricing), [Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions).

The new hosted rehearsal measured **22 rounded Windows minutes** for PR validation (12 generation/gameplay, 3 core tests, 7 Windows app tests), plus two Linux minutes. Including the ordinary release-metadata/readiness jobs adds two Linux minutes: **$0.244 per ordinary PR iteration**. The full release candidate used another 20 Windows minutes and one input-resolution Linux minute, making a release PR iteration **$0.450**. These are rounded job-duration measurements from the first passing runs, not an invoice or a guarantee of future duration. Account allowance/storage figures in the handoff are a prior authenticated snapshot, not a fresh billing check.

Budget all initial runs, fixes, base updates, retries and upstream-triggered reruns. Draft updates also count. The earlier planning range of **20–24 Windows + 4 Linux minutes per ordinary PR iteration** ($0.224–$0.264) contains the new measurement; retain headroom for slower runners and changing test coverage.

The following scenarios use the measured ordinary iteration cost and assume ten release PRs/month with **two candidate iterations each**, reuse at merge (two Windows + three Linux minutes, about $0.038 each), a one-minute six-hour watcher ($0.72 per 30 days), one Linux planning minute for every ordinary PR merge, and a full 3,000-minute allowance valued at the handoff's observed $18 Windows/Linux equivalent. Other account usage, storage, taxes and longer jobs are extra. The controlled fresh post-merge rebuild used 24 Windows + three Linux minutes ($0.258); each extra development rerun adds the ordinary iteration cost.

| Development PRs | Iterations per PR | Estimated monthly execution value | Estimated execution overage |
| ---: | ---: | ---: | ---: |
| 10 | 3 | $17.48 | $0 |
| 20 | 4 | $29.74 | $11.74 |
| 30 | 5 | $46.88 | $28.88 |
| 40 | 5 | $59.14 | $41.14 |

The formula is `(development PRs × iterations + 20) × $0.244 + 20 × $0.206 + $1.10 + development PRs × $0.006`. Convert value to allowance-equivalent minutes by dividing by $0.006, using the handoff's observed accounting rather than the obsolete 2× Windows multiplier. The estimates intentionally include repeated development and release PR iterations. Remeasure rounded job times after adoption and compare with the authenticated billing dashboard.

The testing policy should remain intact. If public release is already intended and this iteration volume is realistic, moving public after the history/permissions review is a reasonable way to remove standard-runner execution cost. Staying private is also feasible if the measured overage is acceptable.

## Recovery

- Failed PR checks: fix the source; the next update regenerates and retests current inputs.
- Expired or changed candidate at merge: handled automatically by a full rebuild.
- Failed post-merge gate or temporary network error: rerun the failed workflow, or dispatch `Publish release` on main with the approved 40-character source commit and refresh attempt `0`.
- Upstream changes during the build: automatic full-gate retry, bounded to three refreshes; repeated changes leave a failure for inspection.
- Existing conflicting tag, draft or asset: investigate the discrepancy. Never delete or overwrite a real release to force automation through.
- A newer release is already published: publication rejects an older version instead of making it latest.

Offline automation verification: `tools/ci/Test-ReleaseAutomation.ps1`, `tools/ci/Test-ReleasePublication.ps1`, and `tools/ci/Test-UpstreamWatch.ps1`. These use temporary fixtures and do not contact GitHub or mutate a repository. They cover input/candidate integrity, draft recovery, immutable publication, bounded refresh dispatch, and scheduler decisions including moved heads, forks, expired artifacts, date cultures and GitHub's rerun window. Local game validation must use an isolated game checkout: this worktree shares its parent directory with the user's installed game, so invoking the release builder directly here would install into that game.
