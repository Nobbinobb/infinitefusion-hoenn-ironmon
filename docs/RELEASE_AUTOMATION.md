# Release automation

This is the production workflow proposal on `feature/release-automation-test`. It replaces the disposable rehearsal triggers. Local validation is recorded in `audits/RELEASE_AUTOMATION_FRESHNESS.md`; adoption still needs a hosted rehearsal and the repository settings below. No real release or visibility change is part of this implementation.

## Maintainer workflow

Develop through PRs to `main`. Every opening, update, reopening and ready-for-review event runs checks, including draft PRs. Superseded runs are canceled. There are no path filters that could omit a gameplay or tracker change.

For a release, prepare a PR that increases `Ironmon::VERSION` in `src/foundation/Core.rb`, `ApplicationDisplayVersion` and `Version` in the tracker app project, and its numeric `ApplicationVersion`. Add `docs/releases/RELEASE_NOTES_<version>.md` headed `# Ironmon <version>`. Branch names and labels do not control publication; the reviewed version increase does. An agent can prepare these source changes locally without generating or committing binary data.

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

## Repository settings for adoption

These are proposed settings, not settings applied by this branch. The current GitHub CLI session is unauthenticated; existing remote rules could not be rechecked during this refinement.

1. Keep PR-only changes to `main` and existing required `Core tracker tests` / `Windows app tests`. Add **PR validation** and **Release readiness** as required checks, selecting the GitHub Actions source after they first appear. Require the branch to be up to date before merging, so the tested merge tree includes current main. Do not enable merge queue without adding its `merge_group` triggers and validation policy.
2. Create the **release** environment with deployment restricted to **main**. For the requested automatic publication after PR acceptance, do not add a second environment reviewer gate. Keep write permissions scoped to publication (`contents: write`, `actions: write`) and upstream scheduling (`actions: write`). The default workflow token remains read-only; no personal access token or external signing secret is required.
3. Keep force-push/deletion protection for `main`. Protect release tags from modification/deletion while permitting the publication identity to create new version tags. Check that any ruleset bypass is scoped to this need and does not bypass PR tests.
4. Merge the automation through its own reviewed PR without increasing the application version. Scheduled/manual workflows become available after reaching the default branch. Run a disposable hosted adoption rehearsal before creating a real version-bump PR. This branch does not change repository visibility or enable automatic merge.

The workflow uses GitHub's documented `queue: max` for production release/publication concurrency, avoiding replacement of a waiting release. Up to 100 runs can wait; excess requests fail/cancel visibly. The locally available March 2026 actionlint does not yet recognize that property, so local lint ignores only that specific diagnostic and validates everything else. [Concurrency queues](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#concurrency).

## Public repository readiness

Keep fork PRs on `pull_request` with read-only tokens and no secrets; never run their code from a privileged `pull_request_target` job. Require approval for outside contributors' workflow runs. Retain protected-main review and required tests, especially for workflow and release-tool changes. Actions here are pinned to verified commit SHAs; review updates to these pins. [GitHub security guidance](https://docs.github.com/en/actions/reference/security/secure-use).

Before changing visibility, inspect the full Git history and existing releases for private material and redistribution scope, not only today's checkout. A prior repository audit recorded old release ZIPs in history; recheck them and their licenses before public exposure. Review generated audit visibility, security reporting/contact choices, Actions permissions, fork settings and branch/tag rules. The license covers original Ironmon work; upstream game downloads remain runner inputs and are not uploaded as game archives. Public visibility is a separate explicit decision. [Visibility changes](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility).

## Costs and iteration planning

As verified on 2026-09-06, standard Windows runners cost $0.010 per actual minute and standard Linux runners $0.006 above included usage. Standard hosted execution is free for public repositories. Larger runners and storage have separate billing. [Runner pricing](https://docs.github.com/en/billing/reference/actions-runner-pricing), [Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions).

The rehearsal measured 14 rounded Windows minutes for the older PR CI and 20 Windows minutes for a candidate. That PR CI omitted the newly added gameplay checks. Do not reuse its $0.14/run as a measured price for this implementation. Account allowance/storage figures in the handoff are a prior authenticated snapshot, not a fresh billing check.

For planning until the new workflow is measured, assume **20–24 Windows + 4 Linux minutes per ordinary PR iteration** ($0.224–$0.264). A release PR iteration additionally generates its full candidate: roughly another 20 Windows + 1 Linux ($0.206). These are deliberately conservative projections, not hosted measurements. Budget all initial runs, fixes, base updates, retries and upstream-triggered reruns. Draft updates now also count.

The following scenarios assume ten release PRs/month with **two candidate iterations each**, reuse at merge (about $0.028 each), a one-minute six-hour watcher ($0.72/month), and a full 3,000-minute allowance valued at the handoff's observed $18 Windows/Linux equivalent. Other account usage, storage, taxes, non-release main planning jobs and longer jobs are extra. Each fresh post-merge rebuild adds approximately $0.20; each extra development rerun adds the ordinary iteration cost.

| Development PRs | Iterations per PR | Estimated monthly execution value | Estimated execution overage |
| ---: | ---: | ---: | ---: |
| 10 | 3 | $16.32–$18.32 | $0–$0.32 |
| 20 | 4 | $27.52–$31.52 | $9.52–$13.52 |
| 30 | 5 | $43.20–$50.00 | $25.20–$32.00 |
| 40 | 5 | $54.40–$63.20 | $36.40–$45.20 |

The formula is `(development PRs × iterations + 20) × ordinary iteration cost + 20 × $0.206 + $1.00`. Convert value to allowance-equivalent minutes by dividing by $0.006, using the handoff's observed accounting rather than the obsolete 2× Windows multiplier. Remeasure rounded job times after adoption and compare with the authenticated billing dashboard.

The testing policy should remain intact. If public release is already intended and this iteration volume is realistic, moving public after the history/permissions review is a reasonable way to remove standard-runner execution cost. Staying private is also feasible if the measured overage is acceptable.

## Recovery

- Failed PR checks: fix the source; the next update regenerates and retests current inputs.
- Expired or changed candidate at merge: handled automatically by a full rebuild.
- Failed post-merge gate or temporary network error: rerun the failed workflow, or dispatch `Publish release` on main with the approved 40-character source commit and refresh attempt `0`.
- Upstream changes during the build: automatic full-gate retry, bounded to three refreshes; repeated changes leave a failure for inspection.
- Existing conflicting tag, draft or asset: investigate the discrepancy. Never delete or overwrite a real release to force automation through.
- A newer release is already published: publication rejects an older version instead of making it latest.

Offline automation verification: `tools/ci/Test-ReleaseAutomation.ps1` and `tools/ci/Test-ReleasePublication.ps1`. These use temporary fixtures and do not contact GitHub or mutate a repository. Local game validation must use an isolated game checkout: this worktree shares its parent directory with the user's installed game, so invoking the release builder directly here would install into that game.
