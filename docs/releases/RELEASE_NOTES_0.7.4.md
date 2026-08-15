# Ironmon 0.7.4

Ironmon 0.7.4 replaces support-only Debug tracker builds with signed,
capability-based diagnostic access in the ordinary Release tracker.

## Diagnostic access

- Adds an always-available **Diagnostic Access** screen that accepts pasted
  tokens or bounded `.ironmon-access` files.
- Supports temporary and lifetime tokens. The tracker clearly shows Active,
  Expired, Invalid, and local Developer Override states.
- Shows the maintainer note, expiration, token and signing-key identifiers,
  direct grants, automatically included grants, and unknown future claims.
- Replacing a token requires confirmation. Removing, replacing, or reaching
  expiration updates the connected game immediately and clears protected
  information already displayed by the tracker.
- Stores one token for the current Windows user. The raw token is not shown
  again after activation and is never sent to the game.

## Fine-grained capabilities

- Separates current Player, current Enemies, and arbitrary active-run Pokemon
  availability. All Active Pokemon includes both current quick-access grants.
- Independently controls Pokemon Overview, Abilities, Base Stats, Move Access,
  exact Evolution Results, and Evolution Candidate lists.
- Independently controls wild encounters, trainer parties, uncollected ground
  items, fusion previews, fusion material pairs, run configuration, run seed,
  generator manifests, and evolution-generator details.
- Independently controls tracker protocol history, raw state, and persisted
  knowledge. Clipboard and exported reports contain only currently authorized
  groups and no token metadata.
- Uses game-resolved Player and Enemy targets for current-only tools, preventing
  a changed species identifier from becoming arbitrary active-run lookup.

## Maintainer tooling

- Adds a separate Windows Blazor Hybrid token generator with editable presets,
  individual capability selection, expiration controls, PKCS#8 P-256 private
  key import, ES256 signing, clipboard output, and `.ironmon-access` saving.
- The generator and private key are maintainer-only and are not shipped in the
  player archive. The tracker embeds only the matching public verification key.

## Using a token

1. Open **Diagnostic Access** in the tracker.
2. Paste the token or load the supplied `.ironmon-access` file.
3. Activate it and review every displayed grant and its expiration.
4. Use **Remove access** when support is finished. To replace access, submit a
   different valid token and confirm the replacement warning.

An expired token is marked Expired and grants nothing. It can be removed or
replaced without rebuilding or reinstalling the tracker.

## Compatibility

- A 0.7.4 tracker and game negotiate only capabilities supported by both.
- A 0.7.3 peer receives no signed named access. The existing dual-sided local
  developer override remains available for source development.
- Ordinary live tracking, completed-run lookup, deterministic generation, and
  gameplay behavior are unchanged when no valid token is active.
