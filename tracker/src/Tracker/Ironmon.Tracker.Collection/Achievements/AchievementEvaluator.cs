using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Ironmon.Tracker.Collection.Achievements;

/// <summary>
/// Holds one directly accessible joint summary with its known typed values.
/// </summary>
internal sealed class AchievementEvidence
{
    /// <summary>
    /// Gets the known dimensions of this same joint observation.
    /// </summary>
    public Dictionary<string, JsonElement> Dimensions { get; } = [];

    /// <summary>
    /// Gets exact scalar measures, excluding unavailable measures.
    /// </summary>
    public Dictionary<string, BigInteger> Scalars { get; } = [];

    /// <summary>
    /// Gets exact known set members, including known empty sets.
    /// </summary>
    public Dictionary<string, List<JsonElement>> Sets { get; } = [];

    /// <summary>
    /// Gets exact known rational measures without floating-point conversion.
    /// </summary>
    public Dictionary<string, (BigInteger Numerator, BigInteger Denominator)> Ratios { get; } = [];
}

/// <summary>
/// Evaluates typed private predicates over generic retained summaries without scanning run archives.
/// </summary>
internal static class AchievementEvaluator
{
    private const string _sum = "sum";
    private const string _maximum = "maximum";
    private const string _cardinality = "cardinality";
    private const string _exists = "exists";
    private const string _minimumFraction = "minimum_fraction";
    private const string _atMost = "at_most";
    private const string _atLeast = "at_least";
    private const string _and = "and";
    private const string _or = "or";
    private const string _equal = "eq";
    private const string _notEqual = "ne";
    private const string _in = "in";
    private const string _contains = "contains";
    private const string _nonempty = "nonempty";
    private const string _setContains = "set_contains";
    private const string _equalField = "eq_field";
    private const string _greater = "gt";
    private const string _greaterEqual = "ge";
    private const string _less = "lt";
    private const string _lessEqual = "le";
    private const string _numerator = "numerator";
    private const string _denominator = "denominator";
    private const string _nativeIdentity = "id";

    /// <summary>
    /// Tests one active definition using exact threshold arithmetic and evidence from the same joint rows.
    /// </summary>
    /// <param name="rule">The already-decrypted active rule.</param>
    /// <param name="evidence">Rows in its registered generic summary group.</param>
    /// <returns>True only when known retained evidence proves the complete condition.</returns>
    public static bool Satisfied(AchievementRule rule, IReadOnlyList<AchievementEvidence> evidence)
    {
        if (rule.Comparison is not (_atLeast or _atMost))
            throw new InvalidDataException("An achievement comparison is unsupported.");

        if (rule.AllowedMembers.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Array))
            throw new InvalidDataException("An achievement member list is invalid.");

        AchievementEvidence[] rows = [.. evidence.Where(row => Matches(rule.Filter, row))];
        if (!Fraction(rule.Threshold, out BigInteger target, out BigInteger denominator))
            throw new InvalidDataException("An achievement threshold is invalid.");

        if (rule.Operation == _minimumFraction)
            return rows.Any(row => row.Ratios.TryGetValue(rule.Measure, out var value) && value.Numerator > 0 && value.Denominator > 0 && value.Numerator * denominator <= target * value.Denominator);

        BigInteger[] values = [.. rows.Where(row => row.Scalars.ContainsKey(rule.Measure)).Select(row => row.Scalars[rule.Measure])];
        BigInteger actual = rule.Operation switch
        {
            _exists => rows.Length > 0 ? BigInteger.One : BigInteger.Zero,
            _sum => values.Aggregate(BigInteger.Zero, (total, value) => total + value),
            _maximum => values.Length == 0 ? BigInteger.Zero : values.Max(),
            _cardinality => Cardinality(rule, rows),
            _ => throw new InvalidDataException("An achievement evidence operation is unsupported.")
        };

        return rule.Comparison == _atMost ? rows.Length > 0 && actual * denominator <= target : actual * denominator >= target;
    }

    /// <summary>
    /// Counts the union of admitted identities without allowing unrelated members to satisfy a collection.
    /// </summary>
    /// <param name="rule">The active collection rule with an optional member allowlist.</param>
    /// <param name="rows">The correlated summary rows admitted by the rule's predicate.</param>
    /// <returns>The exact distinct count after applying the optional allowlist.</returns>
    private static int Cardinality(AchievementRule rule, IReadOnlyList<AchievementEvidence> rows)
    {
        return rows.Where(row => row.Sets.ContainsKey(rule.Measure))
            .SelectMany(row => row.Sets[rule.Measure])
            .Where(member => rule.AllowedMembers.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null || rule.AllowedMembers.EnumerateArray().Any(expected => Equal(member, expected)))
            .Select(member => Convert.ToHexString(CollectionEncoding.Canonical(member)))
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    /// <summary>
    /// Evaluates an explicit conjunction, disjunction or known-value comparison.
    /// </summary>
    /// <param name="predicate">The private declarative predicate.</param>
    /// <param name="row">One correlated summary row.</param>
    /// <returns>True when the row proves the predicate; missing evidence never proves a comparison.</returns>
    internal static bool Matches(JsonElement predicate, AchievementEvidence row)
    {
        if (predicate.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;

        if (predicate.ValueKind != JsonValueKind.Array || predicate.GetArrayLength() == 0)
            throw new InvalidDataException("An achievement predicate is invalid.");

        string operation = predicate[0].GetString()!;
        JsonElement[] arguments = [.. predicate.EnumerateArray().Skip(1)];
        if (operation == _and)
            return arguments.All(argument => Matches(argument, row));

        if (operation == _or)
            return arguments.Any(argument => Matches(argument, row));

        string field = arguments[0].GetString()!;
        if (operation == _setContains)
            return row.Sets.TryGetValue(field, out List<JsonElement>? members) && members.Any(member => Equal(member, arguments[1]));

        if (!row.Dimensions.TryGetValue(field, out JsonElement actual))
            return false;

        if (operation == _nonempty)
            return actual.ValueKind == JsonValueKind.Array && actual.GetArrayLength() > 0;

        if (operation == _equalField)
            return row.Dimensions.TryGetValue(arguments[1].GetString()!, out JsonElement other) && Equal(actual, other);

        if (operation is _equal or _contains)
            return Equal(actual, arguments[1]);

        if (operation == _notEqual)
            return !Equal(actual, arguments[1]);

        if (operation == _in)
            return arguments.Skip(1).Any(expected => Equal(actual, expected));

        if (!Fraction(actual, out BigInteger numerator, out BigInteger denominator) || !Fraction(arguments[1], out BigInteger expectedNumerator, out BigInteger expectedDenominator))
            return false;

        int comparison = (numerator * expectedDenominator).CompareTo(expectedNumerator * denominator);
        return operation switch
        {
            _greater => comparison > 0,
            _greaterEqual => comparison >= 0,
            _less => comparison < 0,
            _lessEqual => comparison <= 0,
            _ => throw new InvalidDataException("An achievement predicate operation is unsupported.")
        };
    }

    /// <summary>
    /// Compares canonical identities, typed numbers and set membership without stringifying missing evidence.
    /// </summary>
    /// <param name="actual">A known scalar or set-valued observation.</param>
    /// <param name="expected">The required exact identity or value.</param>
    /// <returns>Whether a scalar matches or a set contains the required value.</returns>
    private static bool Equal(JsonElement actual, JsonElement expected)
    {
        if (actual.ValueKind == JsonValueKind.Object && expected.ValueKind == JsonValueKind.String && actual.TryGetProperty(_nativeIdentity, out JsonElement identity))
            return Equal(identity, expected);

        if (actual.ValueKind == JsonValueKind.Array && expected.ValueKind != JsonValueKind.Array)
            return actual.EnumerateArray().Any(member => Equal(member, expected));

        if (actual.ValueKind == JsonValueKind.String && expected.ValueKind == JsonValueKind.String)
            return string.Equals(actual.GetString(), expected.GetString(), StringComparison.Ordinal);

        if (actual.ValueKind is JsonValueKind.True or JsonValueKind.False && expected.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return actual.GetBoolean() == expected.GetBoolean();

        if (Fraction(actual, out BigInteger numerator, out BigInteger denominator) && Fraction(expected, out BigInteger otherNumerator, out BigInteger otherDenominator))
            return numerator * otherDenominator == otherNumerator * denominator;

        return CollectionEncoding.Canonical(actual).AsSpan().SequenceEqual(CollectionEncoding.Canonical(expected));
    }

    /// <summary>
    /// Reads a finite exact integer, decimal or retained rational without binary floating-point rounding.
    /// </summary>
    /// <param name="value">The known numeric JSON value.</param>
    /// <param name="numerator">The exact signed numerator.</param>
    /// <param name="denominator">The positive denominator.</param>
    /// <returns>True for supported finite numeric values.</returns>
    private static bool Fraction(JsonElement value, out BigInteger numerator, out BigInteger denominator)
    {
        numerator = BigInteger.Zero;
        denominator = BigInteger.One;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(_numerator, out JsonElement top) && value.TryGetProperty(_denominator, out JsonElement bottom))
            return BigInteger.TryParse(top.ToString(), CultureInfo.InvariantCulture, out numerator) && BigInteger.TryParse(bottom.ToString(), CultureInfo.InvariantCulture, out denominator) && denominator > 0;

        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        if (text is null || text.Length > 10000)
            return false;

        string[] parts = text.Split('.');
        if (parts.Length > 2 || !BigInteger.TryParse(string.Concat(parts), NumberStyles.Integer, CultureInfo.InvariantCulture, out numerator))
            return false;

        denominator = parts.Length == 2 ? BigInteger.Pow(10, parts[1].Length) : BigInteger.One;
        return true;
    }
}
