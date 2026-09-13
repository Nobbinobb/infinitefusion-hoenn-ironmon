# Updater release production

The existing release workflow builds the installer, both tracker packages and
updater metadata together. Ordinary IDE builds do not create release manifests,
inventories or signing output. `Build-TrackerRelease.ps1 -GenerateOnly` still
produces catalog and game-baseline inputs without producing releases.

## Build and publication boundary

The read-only candidate job uses the source tree and upstream game commit already
selected by the release workflow. It restores published history, tests the updater
and native Setup, builds both tracker flavors and self-contained Windows tools,
and freezes the candidate. Publication exposes exactly these seven files:

| Asset | Purpose |
| --- | --- |
| `Ironmon-v<V>-win-x64.zip` | Self-contained tracker, scripts and data |
| `Ironmon-v<V>-win-x64-runtime-required.zip` | Runtime-required tracker, scripts and data |
| `Ironmon-Setup-v<V>-win-x64.exe` | Disposable standalone installer |
| `update-manifest.json` | Versions, compatibility, roles, sizes and SHA-256 hashes |
| `update-manifest.sig.json` | Detached signature over the exact manifest bytes |
| `update-data.json` | Current and historical inventories plus release notes |
| `SHA256SUMS.txt` | One checksum list for the six files above |

GitHub adds its two automatic source archives. Players choose the installer or one
of the two Ironmon ZIPs; the updater reads the JSON files automatically. Historical
inventory growth never adds more public download entries.

Installer and tracker release discovery use anonymous HTTPS requests. Their
configured release repository and assets must be publicly accessible. A private
rehearsal repository can verify authenticated CI publication, but cannot establish
that a downloaded installer works for players: GitHub returns 404 to anonymous
clients even when the signed assets exist. Do not embed GitHub credentials in
Setup or change repository visibility as a workaround without release-owner
authorization. Rehearse the anonymous download path separately before claiming
end-to-end installer acceptance.

Setup uses fixed, non-scrolling pages with a minimum window size of 680 × 600
Windows layout units. Changed files are reviewed one at a time; moving between
files retains each explicit replacement approval. Long paths and diagnostic
details have tooltips, while status and actions stay visible in the footer.

Native Setup, its session, the independent updater and shared engine messages
use `tracker/src/Ironmon.Updater.Core/Resources/Localization/UpdaterResources.resx`
through the strongly typed `UpdaterText` accessors. Resource lookup follows the
current UI culture, with the same English fallback as the tracker; formatted
values use the current formatting culture. Add translations as culture-specific
resource files with the same keys. The tracker update panel continues to use its
existing `TrackerResources.resx` entries. Protocol identities, paths and signing
data are not localized.

Setup validates the selected folder before presenting Options. New Ironmon
installations offer **Runtime included** (the default self-contained package) or
**Runtime required**. Existing installations display and retain their detected
package; signed review independently rechecks that choice. The run-completion
confirmation appears during review only when the release changes the game or its
signed compatibility policy requires a finished run. Ordinary new installations
and compatible updates do not display that confirmation.

The Actions candidate artifact retains `candidate.json`, `release-evidence.zip`,
loose inventories, public trust and per-file verification evidence for review.
These build files and their individual checksum sidecars are not release assets.
The public `SHA256SUMS.txt` uses stable LF line endings and lists every published
payload and metadata document exactly once, excluding itself.

Release manifest schema 2 binds one metadata container by length and SHA-256, then
independently binds every document inside it. The container preserves original
inventory bytes and is downloaded and parsed once for a preparation. Older schema
1 releases remain readable for authenticated history and recovery.

Both tracker packages contain `Ironmon Tracker/Updater/Ironmon.Updater.exe`,
public trust and notices. `Ironmon Tracker/update-package.json` inventories the
payload before archiving; the external signed inventory also hashes this inner
document. Historical generation profiles retain their immutable ownership policy.
Setup and the helper embed notices from their exact resolved runtime packs.

Helper version `<H>` uses the engine major/minor plus the engine patch and signed
release sequence. Every release gets an immutable helper path even when only
embedded game data or trust changes. Setup and tracker binary versions are checked
against `<V>` before packaging. The signed helper identity must match both player
packages. Preparation extracts that executable from the selected package's cached
ZIP, without downloading a separate helper archive or the other tracker flavor.

The protected `release` publication job rechecks source, tree, candidate hashes
and current upstream inputs. An upstream change dispatches a new read-only build;
stale manifests are not signed. An intervening stable Ironmon release likewise
invalidates candidate reuse and triggers a rebuild with current adoption history
and release sequence. The trusted metadata tool is built from `main`
in a separate step before the signing secret is exposed. It validates the same
wire contract as installed clients, then signs the frozen raw manifest bytes,
adding `update-manifest.sig.json` and the combined `SHA256SUMS.txt`.

Uploads stay in a draft until every remote artifact's length and digest match.
Retries restore an existing detached signature and preserve its exact bytes.
Published assets are never overwritten. Stable discovery cannot see a partially
uploaded draft.

## One-time production trust provisioning

`resources/updater/trusted-keys.json` is reviewed source: a map from lowercase key
identifiers to base64 DER SubjectPublicKeyInfo public keys. The algorithm is ECDSA
P-256 with SHA-256 and 64-byte IEEE P1363 signatures. Keep this identity separate
from tracker access-code and other signing keys.

The production signing identity is `ironmon-updater-2026`. Its public key is
checked in, and the matching private key and identifier are configured in the
protected `release` environment. That environment permits only `main`. No
fixture private key is checked in or used as production trust. Developer builds
can run with empty trust, but release candidate generation rejects it.

When provisioning a new signing identity, the owner must:

1. Create and securely retain a dedicated P-256 signing key outside the repository.
2. Add only its public SubjectPublicKeyInfo bytes and identifier to the reviewed
   trust document before candidate executables are built.
3. Configure the existing protected `release` environment's secret
   `IRONMON_UPDATER_SIGNING_KEY` with base64 DER PKCS8 private bytes, and its
   `IRONMON_UPDATER_SIGNING_KEY_ID` variable with the matching identifier.

Publication reads private bytes only from that step's environment, checks their
public identity against reviewed trust, and clears the byte buffer after signing.
It does not write a key file or print key material. Candidate and test jobs receive
no signing secret. Never put private material in arguments, source, release notes,
generated inventories or artifacts.

Clients embed public trust; downloaded files and runtime environment variables
cannot replace it. For rotation, publish overlapping public trust while the old
key is still accepted. Retain public keys needed to verify historical releases.
The initial protocol supports at most four keys.

## History and selected game version

The candidate job automatically restores the previous stable release. Modern
history restores the manifest, signature and shared metadata file. It is
authenticated by the detached signature and referenced hashes; exact inventory
bytes are copied into the next candidate. Previous current ownership
becomes an explicit legacy reference. Player installations never become a source
of trusted ownership.

For the first updater-enabled release, the job downloads the last pre-updater
candidate and both packages, checking independent GitHub asset digests. It reads
that candidate's game commit, fetches the official objects, and runs the existing
inventory generator against that historical tree. The producer checks package
hashes and game identity before authenticating the resulting legacy inventories.
Missing historical evidence fails the build rather than dropping adoption support.

The preferred and supported game commit remains the workflow-selected newest
revision, matching normal release generation. There is no hardcoded 6.8.2 target.
Historical commits identify older installations; they do not make arbitrary game
versions compatible. The producer enforces clients' aggregate metadata budget and
protocol history limits, blocking releases that installed clients cannot consume.

## Local verification

`tools/Test-Updater.ps1 -Offline` runs updater and native Setup tests after the
pinned test runtime has been provisioned. `ReleaseBundleTests` creates synthetic
`99.0.1`, `99.0.2` and `99.0.3` bundles in disposable output, signs with in-memory
fixture keys, and installs/updates both flavors through the shared transaction
engine. Inconsistent, incomplete and corrupt packages fail before signing.
Production version fields and real release archives remain unchanged.

`tools/ci/Test-ReleaseAutomation.ps1` and `Test-ReleasePublication.ps1` test
orchestration offline. Their fake GitHub and signing boundaries cannot publish;
cryptography is tested by the real C# producer tests. Fresh Windows, real-game
acceptance and protected-folder elevation remain separate acceptance gates.
See [Updater acceptance and recovery](UPDATER_ACCEPTANCE.md) for repeatable checks
and the remaining native rehearsal matrix. Automated administrator-boundary tests
are separate from actual Windows permission-dialog acceptance.

## Installation preparation and progress

Fresh game installations fetch a shallow checkout of the signed game revision.
Preparation deepens history only when needed to prove the previous-to-target and
target-to-upstream ancestry relationships; an unrelated history still fails.
Ordinary branch, remote, index, object integrity and signed file checks remain
required. Existing full checkouts retain their history, and shallow checkouts
remain usable by the launcher's normal fetch/pull flow.

The disposable game archive is stored without recompression. Its verified files
move into the combined staging directory instead of passing through another full
copy. The tracker package downloads alongside game preparation. Game progress
remains visible until that work finishes; if the package is still downloading,
its measured byte progress takes over.

For an empty installation on the same volume, the transaction takes ownership of
the combined extraction folder. Signed root distribution documents are verified
during extraction, then excluded from this disposable folder using the same
ownership policy as ordinary updates; existing installation documents are never
removed by this cleanup. It flushes and verifies the managed files without
copying them again. Other destinations use verified durable copies. Preparation,
backup copies and snapshot hashing use at most four concurrent file operations.
All workers finish or cancel before preparation can return.

Fresh installation promotes complete top-level directories and root files under
one durable intent covering every planned file operation. The installation root,
its identity and its recovery directory stay in place. The journal format and
cursor meaning are unchanged: ordinary file-by-file recovery can reconcile both
promoted files and files whose promotion never happened. Unexpected content still
blocks automatic cleanup and remains available for recovery. Rollback prioritizes
recoverability and can take longer than forward folder promotion.

Existing installations retain individual file intents and process checks. Already
flushed replacement files move from transaction storage into place; recovery
backups remain available. Forward application does not rewrite an identical
journal generation after each file mutation. Final verification and the committed
record remain mandatory. Temporary extraction is disposable; owned payloads,
backups and recovery records are durable before installation starts.

Installer and updater operations carry numeric stage measurements through a scoped
observer, including optional sprite work. Administrator peers opt into bounded
intermediate progress frames; the terminal reply still controls completion and
safe cancellation. Progress contains no paths or raw remote output and grants no
installation authority.

## Protected installations

The same self-contained recovery executable also hosts the administrator worker.
Setup and tracker preparation probe the destination only after the user chooses
an operation. Writable folders use the existing in-process engine. Protected
folders stage a helper from signed release content, hold its executable and
directory ancestry against replacement, and request Windows `runas` permission.
Runtime hook variables are removed for the launch and native bundle extraction
uses the protected updater cache. The normal UI process is not elevated.

One local pipe capability binds the worker to one reviewed installation. Initial
input includes signed release evidence, exact conflict fingerprints and the
original filesystem identity; the worker reauthenticates them after UAC. Later
messages cannot change the root, transaction or release. Windows-reported peer
PIDs bind initialization to the original UI and transfer to the authenticated
retained helper. A capability is never persisted in a journal. Network pipe
clients are denied; credential-based UAC can use a different administrator SID.

Administrator downloads and Git preparation use protected storage under
`%ProgramFiles%/Ironmon Updater`. Durable evidence, payloads and backups remain
under the game's `.ironmon-update`, with administrator/system write access and
ordinary-user read access. No-follow file handles reject redirects and hard links;
directory identity locks prevent replacing reviewed ancestors. The worker invokes
the same signed transaction engine, including fresh idle checks and rollback.

Cancellation is acknowledged after the engine reaches a safe state. An abandoned
untouched preparation is discarded; interrupted application retains recovery
records. The normal independent helper owns progress and tracker relaunch. Its
administrator worker never launches the tracker or creates a user's shortcut.
Completed sessions are closed; an abandoned session also has a fifteen-minute
idle deadline.

Recovery authenticates a retained helper from the signed tracker inventory and
uses the protected shared MinGit cache. Intact recovery records and caches avoid
a network dependency for recovery. Missing/corrupt dependencies still require
repair. Optional sprite downloads use the same worker after commit, or a new
installed-release session restricted to sprite work. That session verifies the
installed receipt and signed payload and cannot apply or recover program files.
Setup can therefore be discarded while protected sprite synchronization remains
available in the tracker.
