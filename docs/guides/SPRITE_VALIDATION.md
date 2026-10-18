# Validated custom sprites

New generation packages admit a fusion only when the current custom index,
credits and reviewed image catalogue agree on a usable variant. The variant must
be indexed, credited as non-Japeal `main` or `temp`, and contain visible pixels
in its 96 × 96 tile. A filename alone is insufficient.

## Approved input

[`resources/sprites/validated_custom_sprites.json.gz`](../../resources/sprites/validated_custom_sprites.json.gz)
is the reviewed catalogue. Its `catalogue_id` identifies the canonical contents;
Git pins the exact file. Normal PR checks use this file and small synthetic PNG
fixtures rather than downloading the image library. Current-source generation
also intersects it with fresh upstream metadata: unreviewed new variants remain
ineligible, and removed or newly disallowed variants leave newly generated pools.

## Refresh and review

1. Run **Validate custom sprite catalogue** in GitHub Actions
   ([workflow](../../.github/workflows/sprite-validation.yml)). It resolves fresh
   metadata, scans permitted variants and publishes
   `validated-custom-sprites-<run ID>` without committing or releasing anything.
2. Download the resulting catalogue and retain the workflow URL and
   `upstream-inputs` artifact as provenance. Inspect its `excluded` lists and
   compare visible variants with the approved catalogue.
3. Replace the approved file in a reviewed change, then run these commands from
   the repository root:

   ```powershell
   python -m pip install -r tools/sprites/requirements.txt
   python tools/sprites/validate_catalog.py --check resources/sprites/validated_custom_sprites.json.gz
   python -m unittest discover -s tools/sprites -p 'test_*.py'
   ```

4. Regenerate the generation package using the
   [development workflow](DEVELOPMENT.md#tracker-builds). Review the resulting
   pool membership changes before release. The pool audit records the catalogue
   identity and rejected variant count; the builder does not need sprite images.

The Actions cache holds per-sheet pixel results, hashes and HTTP validators.
A `304` reuses the previous inspection; changed sheets are decoded again. Every
tile is inspected, so a newly indexed variant can use cached pixel evidence.
Cache eviction requires a full scan. Without usable server validators, the sheet
is downloaded again.

Only HTTP `404` counts as an unavailable sheet, and each explicit scan retries
known 404s. Other HTTP failures, exhausted retries, invalid images, unexpected
layouts or incomplete scans abort publication. Transparent and out-of-range tiles
are reported separately. Output and cache are replaced only after a complete scan.

## Existing runs and image downloads

Eligibility-version-2 profiles pin `generation_custom_sprites.json`, including
the catalogue identity and permitted variants. Its hash contributes to the
profile identity. Existing runs retain their pinned profile when the approved
catalogue changes; eligibility-version-1 profiles retain their original behavior.
The game and tracker use the pinned variants for rendering. Correcting a stored
sprite choice changes its appearance, not the Pokémon's randomized identity.

Validation records a source snapshot. It neither installs player images nor
makes upstream sheets immutable. Missing local images do not change the pool;
players can use **Settings > Custom sprite library** as described in
[installation](INSTALLATION.md#playing-and-sprite-downloads).

## Explicit local snapshots

The [validator](../../tools/sprites/validate_catalog.py) also accepts
`--local-sheets` and `--known-404`, alongside the required `--index`, `--credits`,
`--cache` and `--output` arguments. Every required sheet must exist, unless its
exact URL is listed in the JSON array passed to `--known-404`. A missing local
file alone is not evidence of an upstream 404 and aborts the scan. This mode does
not modify the game's sprite files or synchronization metadata. Label its
provenance as a local snapshot rather than a fresh server scan.
