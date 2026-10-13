using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ironmon.Tracker.Connection.Presentation;

/// <summary>
/// Resolves explicitly deferred stateless move displays from an exact shared catalogue match.
/// </summary>
/// <remarks>
/// Existing game presentations take precedence. Only guarded catalogue rules are reconstructed;
/// move types and game-dependent damage calculations remain authoritative game facts.
/// </remarks>
public static class TrackerSimpleMovePowerPresentation
{
    private const string _resourceName = "Ironmon.Tracker.Connection.Resources.move_power_presentation.json";
    private const string _versionProperty = "version";
    private const string _movesProperty = "moves";
    private const string _idProperty = "id";
    private const string _modeProperty = "mode";
    private const string _fixedValueProperty = "fixed_value";
    private const string _conditionMode = "condition";
    private const string _fixedDamageMode = "fixed_damage";
    private const string _fixedMultiHitMode = "fixed_multi_hit";
    private const string _hiddenDamageMode = "hidden_fixed_damage";
    private const string _unknownDamageMode = "unknown_damage";
    private const string _oneHitKoMode = "one_hit_ko";
    private const string _presentMode = "present";
    private const string _unknownPower = "???";
    private const string _oneHitKoPower = "KO";
    private const int _catalogueVersion = 1;
    private static readonly Catalogue _catalogue = LoadCatalogue();
    private static readonly MovePowerPresentationSnapshot _unknown = new() { Display = _unknownPower };

    /// <summary>
    /// Gets the lowercase SHA-256 fingerprint of the embedded catalogue used for feature negotiation.
    /// </summary>
    /// <remarks>
    /// An empty fingerprint disables negotiation when the resource cannot be loaded or validated.
    /// </remarks>
    public static string CatalogueFingerprint => _catalogue.Fingerprint;

    /// <summary>
    /// Determines whether the peer fingerprint exactly matches a successfully loaded local catalogue.
    /// </summary>
    /// <param name="fingerprint">The peer's optional catalogue identity.</param>
    /// <returns><see langword="true"/> when both fingerprints are nonempty and equal using ordinal comparison; otherwise, <see langword="false"/>.</returns>
    public static bool SupportsCatalogue(string? fingerprint)
        => _catalogue.Fingerprint.Length > 0 && string.Equals(fingerprint, _catalogue.Fingerprint, StringComparison.Ordinal);

    /// <summary>
    /// Preserves an explicit presentation or resolves a marked stateless display without inferring hidden game state.
    /// </summary>
    /// <remarks>
    /// Missing or mismatched catalogue identities and unsupported deferred move identifiers produce an unknown
    /// display. Accepted hidden-damage and unknown-damage rules intentionally produce the same unknown display.
    /// </remarks>
    /// <param name="moveId">The unchanged canonical move identifier.</param>
    /// <param name="basePower">The transmitted base power.</param>
    /// <param name="nativePresentation">The existing explicit presentation, which takes precedence over every derived value.</param>
    /// <param name="deferred">Whether the game explicitly deferred this display.</param>
    /// <param name="catalogueFingerprint">The connected game's negotiated catalogue identity.</param>
    /// <returns>The existing presentation, a derived or unknown deferred presentation, or <see langword="null"/> when neither a presentation nor a deferral is present.</returns>
    public static MovePowerPresentationSnapshot? Resolve(string moveId, int basePower, MovePowerPresentationSnapshot? nativePresentation, bool deferred, string? catalogueFingerprint)
    {
        if (nativePresentation is not null || !deferred)
            return nativePresentation;

        if (!SupportsCatalogue(catalogueFingerprint) || !_catalogue.Rules.TryGetValue(moveId, out Rule? rule))
            return _unknown;

        return rule.Mode switch
        {
            _conditionMode => new() { Display = basePower.ToString(CultureInfo.InvariantCulture) },
            _fixedDamageMode => new() { Display = rule.FixedValue.ToString(CultureInfo.InvariantCulture), Indicator = MovePowerIndicator.FixedDamage },
            _fixedMultiHitMode => new() { Display = ((long)basePower * 2).ToString(CultureInfo.InvariantCulture) },
            _oneHitKoMode => new() { Display = _oneHitKoPower },
            _presentMode => CreatePresent(),
            _ => _unknown
        };
    }

    /// <summary>
    /// Completes marked current, stored, and level-up player move presentations in place before publication or persistence.
    /// </summary>
    /// <param name="player">The inbound player snapshot, or <see langword="null"/> when no player is present.</param>
    /// <param name="catalogueFingerprint">The connected game's catalogue identity.</param>
    public static void CompletePlayer(PlayerPokemonSnapshot? player, string? catalogueFingerprint)
    {
        if (player is null)
            return;

        for (int index = 0; index < player.Moves.Count; index++)
            CompleteMove(player.Moves[index], catalogueFingerprint);

        for (int index = 0; index < player.StoredMoves.Count; index++)
            CompleteMove(player.StoredMoves[index], catalogueFingerprint);

        for (int index = 0; index < player.LevelUpMoves.Count; index++)
            CompleteObservation(player.LevelUpMoves[index], catalogueFingerprint);
    }

    /// <summary>
    /// Completes one marked player move in place while retaining any existing explicit presentation.
    /// </summary>
    /// <param name="move">The inbound player move.</param>
    /// <param name="catalogueFingerprint">The connected game's catalogue identity.</param>
    private static void CompleteMove(PlayerMoveSnapshot move, string? catalogueFingerprint)
    {
        if (move.PowerPresentationDeferred)
            move.CompletePowerPresentation(Resolve(move.Id, move.Power, move.PowerPresentation, true, catalogueFingerprint)!);
    }

    /// <summary>
    /// Completes one marked observed-move presentation in place before it can enter persistent knowledge.
    /// </summary>
    /// <param name="move">The inbound move observation, or <see langword="null"/> when none is present.</param>
    /// <param name="catalogueFingerprint">The connected game's catalogue identity.</param>
    public static void CompleteObservation(ObservedMoveSnapshot? move, string? catalogueFingerprint)
    {
        if (move?.PowerPresentationDeferred == true)
            move.CompletePowerPresentation(Resolve(move.Id, move.Power, move.PowerPresentation, true, catalogueFingerprint)!);
    }

    /// <summary>
    /// Reproduces Present's catalogue-independent power and healing distribution without consulting battle state.
    /// </summary>
    /// <returns>An unknown power display with the three power outcomes and one healing outcome used by the game.</returns>
    private static MovePowerPresentationSnapshot CreatePresent()
    {
        return new()
        {
            Display = _unknownPower,
            DetailsKind = MovePowerDetailsKind.Outcomes,
            Outcomes =
            [
                new() { Power = 40, ChancePercent = 40 },
                new() { Power = 80, ChancePercent = 30 },
                new() { Power = 120, ChancePercent = 10 },
                new() { Kind = MovePowerOutcomeKind.Healing, HealingPercent = 25, ChancePercent = 20 }
            ]
        };
    }

    /// <summary>
    /// Loads the guarded catalogue and disables negotiation when resource loading or validation fails.
    /// </summary>
    /// <returns>The exact resource fingerprint and guarded rules, or an empty fingerprint and rule set on a handled failure.</returns>
    private static Catalogue LoadCatalogue()
    {
        try
        {
            return ReadCatalogue();
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException)
        {
            return new(string.Empty, []);
        }
    }

    /// <summary>
    /// Reads the versioned embedded catalogue and excludes modes or attributes outside the stateless rule contract.
    /// </summary>
    /// <returns>The exact source fingerprint and accepted rules, or an empty catalogue when the resource is absent or its version is unsupported.</returns>
    private static Catalogue ReadCatalogue()
    {
        using Stream? resource = typeof(TrackerSimpleMovePowerPresentation).Assembly.GetManifestResourceStream(_resourceName);
        if (resource is null)
            return new(string.Empty, []);

        using MemoryStream bytes = new();
        resource.CopyTo(bytes);
        byte[] content = bytes.ToArray();
        using JsonDocument document = JsonDocument.Parse(content);
        if (document.RootElement.GetProperty(_versionProperty).GetInt32() != _catalogueVersion)
            return new(string.Empty, []);

        Dictionary<string, Rule> rules = new(StringComparer.Ordinal);
        foreach (JsonElement definition in document.RootElement.GetProperty(_movesProperty).EnumerateArray())
        {
            string? mode = definition.GetProperty(_modeProperty).GetString();
            if (!IsPureMode(mode) || definition.EnumerateObject().Any(property => property.Name is not (_idProperty or _modeProperty or _fixedValueProperty)))
                continue;

            string? id = definition.GetProperty(_idProperty).GetString();
            if (string.IsNullOrEmpty(id))
                continue;

            int fixedValue = 0;
            if (mode == _fixedDamageMode && (!definition.TryGetProperty(_fixedValueProperty, out JsonElement value) || !value.TryGetInt32(out fixedValue)))
                continue;

            rules.Add(id, new(mode!, fixedValue));
        }

        return new(Convert.ToHexStringLower(SHA256.HashData(content)), rules);
    }

    /// <summary>
    /// Identifies the seven supported stateless presentation modes.
    /// </summary>
    /// <param name="mode">The catalogue mode.</param>
    /// <returns><see langword="true"/> for one of the seven supported modes; otherwise, <see langword="false"/>.</returns>
    private static bool IsPureMode(string? mode)
        => mode is _conditionMode or _fixedDamageMode or _fixedMultiHitMode or _hiddenDamageMode or _unknownDamageMode or _oneHitKoMode or _presentMode;

    /// <summary>
    /// Holds one guarded presentation rule.
    /// </summary>
    /// <remarks>
    /// Initializes a guarded rule from the canonical catalogue without retaining native-dependent attributes.
    /// </remarks>
    /// <param name="mode">The stateless presentation mode.</param>
    /// <param name="fixedValue">The fixed HP damage for a fixed-damage rule, or zero for other modes.</param>
    private sealed class Rule(string mode, int fixedValue)
    {
        /// <summary>
        /// Gets the stateless presentation mode.
        /// </summary>
        public string Mode { get; } = mode;

        /// <summary>
        /// Gets the fixed HP damage for a fixed-damage rule, or zero for other modes.
        /// </summary>
        public int FixedValue { get; } = fixedValue;
    }

    /// <summary>
    /// Holds a resource identity and its pure presentation rules.
    /// </summary>
    /// <remarks>
    /// Initializes the catalogue from the guarded embedded resource, or from empty values when negotiation is disabled.
    /// </remarks>
    /// <param name="fingerprint">The exact lowercase source SHA-256 fingerprint, or an empty value when unavailable.</param>
    /// <param name="rules">The accepted stateless rules.</param>
    private sealed class Catalogue(string fingerprint, Dictionary<string, Rule> rules)
    {
        /// <summary>
        /// Gets the exact resource fingerprint, or an empty value when negotiation is disabled.
        /// </summary>
        public string Fingerprint { get; } = fingerprint;

        /// <summary>
        /// Gets the guarded stateless rules.
        /// </summary>
        public Dictionary<string, Rule> Rules { get; } = rules;
    }
}
