# Tracker localization

The tracker uses standard .NET resource files for native MAUI text and Blazor
component text. English (`en-US`) is the neutral language and fallback.

The executable assembly is named `Ironmon Tracker`, while the code and
resource namespace is `Ironmon.Tracker.App`. `LocalizationAssemblyInfo.cs`
declares that resource root explicitly so .NET does not derive the wrong root
from the executable name.

The application uses the operating system's current UI culture. .NET selects
the closest matching translated resource automatically and falls back to the
neutral English resource when no translation exists. Release builds explicitly
filter satellite assemblies to the supported cultures.

## Add a language

1. Copy
   `src/Ironmon.Tracker.App/Resources/Localization/TrackerResources.resx`.
2. Rename the copy with a standard culture suffix, for example
   `TrackerResources.de-DE.resx` or `TrackerResources.fr.resx`.
3. Keep every `<data name="...">` identifier unchanged and translate only its
   `<value>`.
4. Add the culture to the semicolon-separated `SatelliteResourceLanguages`
   property in `src/Ironmon.Tracker.App/Ironmon.Tracker.App.csproj`. The release
   publisher reads this same property and removes every unsupported culture
   directory, including framework resources.
5. Build the tracker. .NET creates the matching satellite resource assembly
   and loads it when the operating-system UI culture matches.

Use a region-specific culture such as `pt-BR` when wording differs by region.
Use a language-only culture such as `de` when one translation should cover all
regions. Resource fallback follows the normal specific-to-general chain, such
as `de-DE` to `de` to neutral English.

## Add user-visible text

Add a multipart resource key to `TrackerResources.resx` using
`Area.Component.Meaning`, then retrieve it with the injected `Text` localizer
in Razor components or with `IStringLocalizer<TrackerResources>` in native
code. Examples include `Player.Card.WaitingForPlayer`,
`Lookup.Search.NoMatchingPokemon`, and `Debug.Protocol.ExportFailed`.

The component segment describes translation context rather than the current UI
control type. Use `Lookup.Search.Submit`, not `Lookup.Search.Button`, so a UI
refactor does not require renaming the resource. Keep context-specific entries
separate even when their English values are identical; another language may
need different grammar. Format values belong in the resource string as
numbered placeholders so translations can change word order.

Protocol identifiers, game-provided names and descriptions, CSS identifiers,
persistence keys, and diagnostic payload fields are data rather than tracker
interface copy and must not be translated.

Diagnostic capability IDs such as `pokemon.current_player` and
`world.wild_encounters` are stable protocol data and must remain unchanged.
Their user-facing names, descriptions, lifecycle states, validation messages,
and activation controls use `Access.*` tracker resources. The separate
maintainer generator uses its own
`tracker/tools/Ironmon.Tracker.AccessGenerator.App/Resources/Localization/GeneratorResources.resx`
resource set; tracker and generator keys are not interchangeable. Preset names
are localized templates only and do not change the independently selected
capability claims.

The static Blazor host page retains only the product title and its emergency
interface-error recovery copy. That fallback is shown when the component
runtime cannot render and therefore cannot access application resources. The
normal startup placeholder contains no language-specific text.
