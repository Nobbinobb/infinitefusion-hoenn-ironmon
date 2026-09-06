# Ironmon 0.8.6

Ironmon 0.8.6 brings a redesigned tracker, revised trainer randomization and
boss parties, visible custom sprite validation, and live gym badge progress.
Release packages now pass automated validation against current upstream game
and sprite metadata before publication.

## Tracker and starter selection

- Redesign the tracker with a consistent dark interface across battle cards,
  settings, lookups, archives, research views, dialogs, and diagnostic tools.
- Preserve hidden enemy information, detailed move and defense information,
  nicknames, fusion identity, and temporary battle-stat changes.
- Add live eight-badge progress to player and enemy cards, center sprites in
  their shared panel, and retain both opponent selectors in double battles.
- Keep lookup content visible during paging and improve search and fusion
  candidate caching. Normal Pokemon inspection no longer starts global fusion
  preparation.
- Add independent Show all and Single row evolution-graph controls, configured
  neighborhood scope, route modes, and stable BST-group selection.
- Separate starter selection from sprite previews and preserve hidden/revealed
  states. Enforce the BST ceiling in both the game and tracker, including random
  selection; automatic selection uses only the random pick or eligible favorite.

## Trainer randomization and boss parties

- Select fully evolved randomized trainer species at effective level 30 or
  above without forcing story Pokemon to evolve at level 30.
- Preserve Wally's and the Hoenn main rival's policy-selected story Pokemon and
  their persistent evolution paths.
- Give the Hoenn main rival a normal starter under Normal Only, a planned
  normal-to-custom-fusion progression under Mixed, and a custom fusion from the
  start under Custom Fusions Only.
- Add three Pokemon, up to a party of six, to Wally, Gym Leaders, the Elite Four,
  Champion Steven, and the Team Aqua and Team Magma bosses. The Hoenn main rival
  is excluded from this party expansion.
- Keep live battles and World Lookup reconstruction aligned with the rules
  pinned by each generation profile, including archived version-1 profiles.

## Fusion preparation and sprites

- Detect isolated reverse-pair targets before exhaustive repair in the game
  and tracker. Impossible pairings fail with a diagnostic instead of exhausting
  repair work; successful pairing rules are unchanged.
- Require validated visible permitted custom sprite variants for new generation
  packages. Exclude blank or unavailable variants and pin the accepted variants
  into the generation profile.
- Preserve existing eligibility-version-1 profiles. Existing saved runs and
  encounters are not migrated or rerolled, and the proposed reverse-pair fallback
  remains disabled.
- Validate the committed sprite catalogue offline during PR checks; refreshing
  the full catalogue is a separate maintainer operation.

## Diagnostic access

- Add development controls gated by explicit capabilities and preserve existing
  permission boundaries in the redesigned diagnostics interface.
- Redesign the maintainer Access Generator, keep its window stable when changing
  presets, and improve signing-key selection and clipboard handling.
- Require removal of an active access token before importing another token.

## Release validation

- Run gameplay and tracker checks on every PR iteration against freshly resolved
  upstream game and online sprite metadata, using catalogs generated from the
  source under test.
- Build both Windows packages, checksums, provenance, and audit evidence for a
  release PR. Reuse matching verified candidates after merge, or rebuild and
  fully retest if source or upstream inputs have changed.
- Verify all release attachments before publishing the draft. New published
  releases protect their tags and attachments against modification.

## Compatibility and installation

Ironmon 0.8.6 targets Pokemon Infinite Fusion 2 version 6.8.2. Install the game
scripts and tracker from the same 0.8.6 archive. The standard Windows x64 package
includes .NET; the smaller runtime-required package needs the Windows x64
.NET 10 Runtime. Both provide the same player features and license notices.

Back up saves before updating and retain existing generation-profile data.
Compatible saved runs continue using their original pinned data and algorithms;
a missing or damaged profile is reported rather than regenerated differently.
