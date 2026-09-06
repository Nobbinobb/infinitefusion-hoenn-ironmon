# Validated custom sprites

Fusion eligibility uses a reviewed image catalogue in addition to the current
custom index and credits. A listed filename is not proof that the corresponding
96-by-96 tile contains visible pixels. Only indexed, non-Japeal `main` or `temp`
variants with visible pixels can admit a fusion into a new generation package.

## Normal development and PRs

`resources/sprites/validated_custom_sprites.json.gz` is the approved input. Its
internal SHA-256 identifies the canonical catalogue contents. Git pins the exact
compressed file to each source revision. PRs verify this small file and run
synthetic PNG tests. They do **not** download the sprite sheets.

The existing current-source generation jobs still fetch fresh upstream settings,
indexes and credits. Generation intersects those inputs with the validated
variants. New variants absent from the reviewed catalogue remain ineligible.
Removing a variant from the current index or disallowing it in the credits also
removes it from a newly generated package.

## Refreshing the catalogue

Run the separate **Validate custom sprite catalogue** workflow manually. It
resolves fresh metadata, checks every sheet containing a permitted variant and
publishes `validated-custom-sprites-<run ID>` as a workflow artifact. It makes no
commits, release publications, or changes to existing runs.

The first run downloads the required sheets. Subsequent runs keep only small
per-sheet validation records, SHA-256 hashes and HTTP validators in the Actions
cache. A `304` reuses the prior pixel inspection; changed sheets are downloaded
and decoded again. All tile positions are inspected so newly indexed variants
can be evaluated even when the sheet itself is unchanged. A cache eviction
requires a complete scan again. If the server provides no usable validators, a
sheet must be downloaded to establish its current contents.

Only an exact HTTP 404 becomes an unavailable sheet. A 403, exhausted retry,
invalid image, unexpected layout, or incomplete scan fails the workflow without
publishing a replacement catalogue. Known 404s are checked again in each explicit
validation run. Blank tiles and out-of-range tile positions are listed separately.

Download the resulting catalogue, inspect its `excluded` lists and the membership
diff, and replace `resources/sprites/validated_custom_sprites.json.gz` in a normal
reviewed change. Keep the workflow run and metadata artifact as provenance. Run:

```powershell
python -m pip install -r tools/sprites/requirements.txt
python tools/sprites/validate_catalog.py --check resources/sprites/validated_custom_sprites.json.gz
python -m unittest discover -s tools/sprites -p 'test_*.py'
```

Rebuild the generation package with the usual release-generation tooling. The
pool audit records the selected catalogue ID and rejected variant count; the
release membership audit retains every added and removed fusion. No full image
library is required by the package builder.

## Profiles, saved runs and rendering

New packages use custom-fusion eligibility version 2. The fifth profile component,
`generation_custom_sprites.json`, records the validated catalogue ID and the
permitted variants for every retained fusion. Its own SHA-256 is part of the
generation profile ID. Packaging and profile retention copy it alongside the
immutable pool, so later catalogue updates cannot alter an existing run.

The game and tracker select variants from that pinned component for those fusions.
Already stored sprite choices outside that component are corrected to a permitted
custom variant. This is a cosmetic choice, not a reroll of the Pokémon. Older
eligibility-version-1 profiles still load their original four components and keep
their previous pool and sprite-selection behavior. This change does not repair or
reroll a saved encounter from an older run.

Validation establishes what was present in the inspected source snapshot. It does
not install images on a player's computer or promise that mutable upstream sheets
can never change later. Local missing downloads are handled by sprite installation;
they never change the randomization pool. Keep sprite downloads or synchronization
available as before.

## Initial catalogue provenance

The first catalogue was produced on 2026-09-06 by a read-only scan of the installed
custom sheets after the reported full synchronization. It is explicitly a local
snapshot, not a claim of a successful fresh server download. The catalogue embeds
the hashes of the index, credits and every inspected sheet. Its identity is
`11770a11330f9e1b6722dd9fb990cce8ba43e59cd316343b1c71d00805877635`.

The scan covered 2,856 sheets containing permitted variants: 205,298 visible
variants, 3,154 transparent variants and nine permitted variants on confirmed
404 sheets. The custom tile `561.505` (Walnoone / `B505H561`) is in the transparent
list. These are variant counts; a fusion remains eligible when another permitted
variant is usable. The generated pool contains 174,168 fusions after the existing
even-size rule.

For an explicit local snapshot, the validator also accepts `--local-sheets` and
`--known-404`. Every required sheet must exist or have an exact confirmed 404 URL;
a merely absent local file aborts the scan. This mode reads the chosen directory
and does not change the game's synchronization metadata or sprite files.
