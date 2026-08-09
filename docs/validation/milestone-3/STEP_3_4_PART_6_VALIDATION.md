# Step 3.4 Part 6 validation

## Review boundary

This part adds the authorized one-step generated evolution graph. It does not
begin Part 7 packaging or release validation.

## Graph protocol

- [x] Normal lookup exposes immediate generated predecessors in addition to the
  selected outgoing targets retained by Part 5.
- [x] Each predecessor contains exact species identity, localized name, sprite
  path, generated BST, and every effective method for that conceptual edge.
- [x] Incoming and outgoing views of the same normal edge report identical
  effective methods.
- [x] Fusion lookup exposes no predecessor collection and retains only its
  immediate selected Head and Body targets.
- [x] Authorized Debug and completed-run lookup use the same deterministic graph
  reconstruction helpers.
- [x] Ordinary live snapshots remain unchanged and expose no generated graph.

## Tracker presentation

- [x] The selected Pokemon appears once as the highlighted central node.
- [x] Normal predecessor nodes appear above the selected node and outgoing nodes
  appear below it.
- [x] Fusion graphs omit predecessors and place their outgoing targets below the
  selected fusion.
- [x] Every node shows icon, name, and generated BST and uses the lookup
  explorer's existing clickable Back/Forward navigation path.
- [x] Every edge lists all effective method labels.
- [x] Fusion edges include a visible Head or Body badge.
- [x] Multiple branches remain horizontally scrollable instead of compressing
  node labels or overflowing the lookup card.
- [x] A Pokemon with only incoming normal edges still displays its graph even
  though it has no candidate list of its own.

## Automated validation

- [x] The tracker application built with zero warnings and zero errors.
- [x] All 38 .NET tests passed after rebuilding the test assembly.
- [x] Protocol round-trip coverage includes a generated predecessor and its
  effective methods.
- [x] No browser-hosted tracker surface was available for automated visual
  inspection of the native MAUI view; final appearance remains part of this
  review gate.

## Embedded-runtime validation

The validator ran after normal game-data initialization inside Pokemon Infinite
Fusion 2's bundled Ruby runtime.

- [x] Mudkip had one outgoing conceptual edge to Seadra for the validation
  seed.
- [x] Seadra's immediate incoming graph contained Mudkip and seven other sources.
- [x] Mudkip's outgoing and Seadra's incoming representation both reported
  `Level 16` for the same edge.
- [x] The predecessor snapshot exposed only species ID, name, sprite, generated
  BST, and effective methods.
- [x] Fusion `B282H282` exposed exactly one Head and one Body graph edge, every
  edge had an effective method, and no fusion predecessor was reported.

## Packaging and cleanup

- [x] Canonical source was synchronized into the distribution and installed
  game before embedded-runtime testing.
- [x] The temporary runtime validator and report were removed.
- [x] The game completed a clean hidden startup after validator removal.
- [x] All canonical Ruby scripts match their distribution and installed copies
  byte-for-byte.
- [x] Existing staged review state was left untouched; Part 6 corrections remain
  unstaged for review.
