# Test updates through the tracker

Open **Settings > Check for updates** and enable **Include test releases**.
The option is off by default. Enabling it checks published stable releases and
prereleases, selecting the highest numeric version. Drafts remain ineligible.
Test targets are labeled **Test release** in the invitation and update review.

The preference is saved under the user's existing
`%LOCALAPPDATA%/Ironmon/Updater/discovery/` directory, outside the installation.
It survives tracker replacement, including the 0.8.16-to-0.10.0 upgrade. Disabling
it refreshes stable discovery and clears the selected target and review consent.
It does not downgrade an installed test version. Each channel retains its own
authenticated discovery cache, conditional request headers, and rollback watermark.

Both channels use the same repository, signing keys, signed compatibility checks,
download verification, file review, helper handoff, and update transaction. The
setting changes release eligibility, not installation authority. Updates still
require a newer display version. Same-version replacement test builds use Setup
from the current test release.

## 0.8.16 bridge and 0.10.0 release order

The maintenance branch `release/0.8.16` starts at stable tag `v0.8.15`
(`63fb3954ca722e97f150ef08a54c3238e8b4d8a1`). It retains that release's gameplay
and tracker baseline and adds the test-update opt-in, release version, and signed
build-sequence allocation. The forward port belongs on current main and retains
version 0.10.0. Do not replace main with the historical maintenance tree.

1. Publish the signed 0.8.16 bridge as a separate mutable prerelease from
   `release/0.8.16`, without marking it latest. The one-time branch workflow
   accepts only its exact dispatched head and version. Existing main automation
   and existing releases remain unchanged. Regression suites and runtime
   acceptance are skipped by explicit request; package structure, ownership,
   signatures and checksums remain mandatory.
2. Keep native release immutability disabled for this temporary publication and
   exclude only `refs/tags/v0.8.16` from the tag deletion/update ruleset. Restore
   the temporary release-environment branch permission after the build finishes.
3. Reserve the issued signed build/helper sequence even after deleting this
   release. Prepare an update target with a higher sequence before testing the
   tracker upgrade. Sequences through 14 have already been issued; the bridge
   reserves sequence 15 or higher. This workflow never modifies 0.10.0, and a
   target must have a strictly higher signed sequence than the installed bridge.
   The 0.10 release line includes the same opt-in and starts allocation at 16,
   retaining the bridge reservation even after its release is deleted. Other
   published and maintainer-visible draft prereleases also advance the counter,
   regardless of their display version. Publication rechecks this before signing.
4. In the VM, run this release's Setup, expand **Advanced: Ironmon version**, and
   explicitly select **0.8.16 · Test release**. Setup otherwise defaults to the
   latest stable release. Complete Setup so the installed signed authority is
   retained for the subsequent upgrade. Create representative saves, settings,
   and history, then take a checkpoint.
5. Once a suitable target exists, enable **Include test releases** and perform
   the normal tracker update. Record the target manifest hash, signed sequence,
   package flavor, shutdown/handoff, restart, preservation, and retained opt-in.
6. Restore the checkpoint between runtime/package and protected-folder cases.
7. Delete only the temporary bridge release/tag and maintenance branch after
   testing is complete. Future stable releases need not use it as stable history;
   the installed bridge's signed receipt supplies its current update authority.
   Keep that receipt and the updater cache intact until the upgrade completes.

Automated signed-fixture and renderer tests verify the software boundaries. They
do not establish real VM, UAC, native window, or published-payload acceptance.
