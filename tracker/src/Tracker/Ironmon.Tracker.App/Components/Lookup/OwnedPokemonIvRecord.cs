using System.Globalization;
using System.Text.Json;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents one actual owned individual's latest recorded configuration for the selected species.
/// </summary>
/// <remarks>
/// Initializes the presentation from retained ownership evidence rather than species generation.
/// </remarks>
/// <param name="Nickname">The recorded nickname, empty when unknown.</param>
/// <param name="Level">The last recorded level for this species, or null when unknown.</param>
/// <param name="Runner">Whether the last matching configuration represented the runner.</param>
/// <param name="Ivs">The raw IV vector, or null when unavailable.</param>
/// <param name="EffectiveIvs">The effective IV vector, or null when unavailable.</param>
public sealed record OwnedPokemonIvRecord(string Nickname, int? Level, bool Runner, IndividualValuesSnapshot? Ivs, IndividualValuesSnapshot? EffectiveIvs)
{
    private const string _species = "species_identity";
    private const string _canonical = "canonical_species_id";
    private const string _form = "form_id";
    private const string _display = "display_metadata";
    private const string _nickname = "nickname";
    private const string _level = "level";
    private const string _role = "ownership_role";
    private const string _runner = "runner";
    private const string _training = "training_traits";
    private const string _rawIv = "iv";
    private const string _effectiveIv = "effective_iv";
    private const string _hp = "hp";
    private const string _attack = "attack";
    private const string _defense = "defense";
    private const string _specialAttack = "special_attack";
    private const string _specialDefense = "special_defense";
    private const string _speed = "speed";
    private const char _formSeparator = ':';

    /// <summary>
    /// Reads one exact owned payload only when its species and form match the lookup.
    /// </summary>
    /// <param name="payload">The retained owned configuration, never an encounter observation.</param>
    /// <param name="speciesId">The selected symbolic species and optional form.</param>
    /// <param name="numericSpeciesId">The equivalent legacy numeric species, or null when unknown.</param>
    /// <returns>The matching individual, or null for a different species or form.</returns>
    public static OwnedPokemonIvRecord? Read(JsonElement payload, string speciesId, int? numericSpeciesId)
    {
        string[] identity = speciesId.Split(_formSeparator, 2);
        JsonElement species = Property(payload, _species);
        string canonical = Property(species, _canonical).ToString();
        bool speciesMatches = string.Equals(canonical, identity[0], StringComparison.OrdinalIgnoreCase)
            || numericSpeciesId.HasValue && canonical == numericSpeciesId.Value.ToString(CultureInfo.InvariantCulture);
        int form = identity.Length > 1 && int.TryParse(identity[1], CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
        if (!speciesMatches || (Integer(species, _form) ?? 0) != form)
            return null;

        JsonElement training = Property(payload, _training);
        return new(Property(Property(payload, _display), _nickname).ToString(), Integer(payload, _level), Property(payload, _role).ToString() == _runner, ReadIvs(Property(training, _rawIv)), ReadIvs(Property(training, _effectiveIv)));
    }

    /// <summary>
    /// Reads an optional property without fabricating a default stat value.
    /// </summary>
    /// <param name="value">The optional object.</param>
    /// <param name="name">The exact native property name.</param>
    /// <returns>The value, or an undefined element when absent.</returns>
    private static JsonElement Property(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement result) ? result : default;

    /// <summary>
    /// Reads an optional exact integer.
    /// </summary>
    /// <param name="value">The optional object.</param>
    /// <param name="name">The exact native property name.</param>
    /// <returns>The integer, or null when missing or invalid.</returns>
    private static int? Integer(JsonElement value, string name)
    {
        JsonElement property = Property(value, name);
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int result) ? result : null;
    }

    /// <summary>
    /// Reads a partial IV vector while preserving unknown entries and legitimate zeroes.
    /// </summary>
    /// <param name="value">The native vector or unavailable coverage marker.</param>
    /// <returns>The raw vector, or null when no vector was recorded.</returns>
    private static IndividualValuesSnapshot? ReadIvs(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Object
            ? new() { Hp = Integer(value, _hp), Attack = Integer(value, _attack), Defense = Integer(value, _defense), SpecialAttack = Integer(value, _specialAttack), SpecialDefense = Integer(value, _specialDefense), Speed = Integer(value, _speed) }
            : null;
    }
}
