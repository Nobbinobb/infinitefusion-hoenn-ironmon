using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.Achievements;
using System.Numerics;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Verifies private rule admission and exact correlated achievement evidence.
/// </summary>
public sealed class AchievementTests
{
    private const string _sum = "sum";
    private const string _maximum = "maximum";
    private const string _cardinality = "cardinality";
    private const string _fraction = "minimum_fraction";
    private const string _atLeast = "at_least";
    private const string _count = "count";
    private const string _members = "members";
    private const string _ratio = "ratio";
    private const string _native = """["eq","item", "POTION"]""";
    private const string _potion = """{"namespace":"item_id","id":"POTION"}""";
    private const string _item = "item";
    private const string _critical = "critical";
    private const string _quadruple = "quadruple";
    private const string _joint = """["and",["eq","critical",true],["eq","quadruple",true]]""";
    private const string _knownFalse = """["eq","critical",false]""";
    private const string _null = "null";
    private const string _true = "true";
    private const string _false = "false";
    private const string _half = "0.5";
    private const string _first = "\"TACKLE\"";
    private const string _second = "\"GROWL\"";
    private const string _third = "\"THUNDERBOLT\"";
    private const string _placeholder = "Achievement ";
    private const string _typeMembers = """["NORMAL","FIRE","WATER","GRASS","QMARKS"]""";
    private const string _electricType = "ELECTRIC";
    private const string _ownerRole = "owner_role";
    private const string _playerRole = "\"player\"";

    /// <summary>
    /// Checks every shipped definition and gate while proving dormant nodes remain unopened at startup.
    /// </summary>
    [Fact]
    public void PackContainsEveryApprovedReachableProtectedDefinition()
    {
        AchievementPack pack = new();
        SealedAchievement[] nodes = [.. pack.Nodes];
        HashSet<Guid> all = [.. nodes.Select(node => node.Id)];
        Assert.Equal(513, all.Count);
        Assert.Equal(all.Count, GameplayCollectionStore.AchievementCount);
        Assert.All(nodes, node => Assert.False(pack.IsOpen(node.Id)));
        SealedAchievement dormant = nodes.First(node => node.Gates.Length > 0);
        Assert.Throws<InvalidOperationException>(() => pack.Open(dormant.Id, new HashSet<Guid>()));
        AchievementRule[] rules = [.. nodes.Select(node => pack.Open(node.Id, all))];
        Assert.Equal(Enumerable.Range(1, all.Count), rules.Select(rule => rule.Number).Order());
        Assert.All(rules, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Name));
            Assert.False(rule.Name.StartsWith(_placeholder, StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(rule.Condition));
            Assert.False(string.IsNullOrWhiteSpace(rule.Group));
            Assert.False(AchievementEvaluator.Satisfied(rule, []));
        });

        Assert.Equal(rules.Length, rules.Select(rule => rule.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Rejects nonstandard move types when counting the approved eighteen-type collection.
    /// </summary>
    [Fact]
    public void TypeCollectionExcludesUnrelatedNativeMembers()
    {
        AchievementPack pack = new();
        HashSet<Guid> all = [.. pack.Nodes.Select(node => node.Id)];
        AchievementRule rule = pack.Nodes.Select(node => pack.Open(node.Id, all)).Single(rule => rule.Number == 95);
        AchievementEvidence row = new();
        row.Dimensions[_ownerRole] = JsonSerializer.Deserialize<JsonElement>(_playerRole);
        row.Sets[_members] = [.. JsonSerializer.Deserialize<string[]>(_typeMembers)!.Select(type => JsonSerializer.SerializeToElement(new { id = type }))];
        Assert.False(AchievementEvaluator.Satisfied(rule, [row]));
        row.Sets[_members].Add(JsonSerializer.SerializeToElement(new { id = _electricType }));
        Assert.True(AchievementEvaluator.Satisfied(rule, [row]));
    }

    /// <summary>
    /// Admits the first Ball milestone independently of unrelated item-use unlocks.
    /// </summary>
    [Fact]
    public void FirstBallMilestoneDoesNotRequireItemConsumption()
    {
        AchievementPack pack = new();
        HashSet<Guid> all = [.. pack.Nodes.Select(node => node.Id)];
        SealedAchievement firstBall = pack.Nodes.Single(node => pack.Open(node.Id, all).Number == 489);
        Assert.Empty(firstBall.Gates);
        Assert.True(AchievementPack.Eligible(firstBall, new HashSet<Guid>()));
    }

    /// <summary>
    /// Checks AND between activation groups and OR inside an alternative group.
    /// </summary>
    [Fact]
    public void ActivationGroupsPreserveBothBooleanMeanings()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid(), third = Guid.NewGuid();
        SealedAchievement node = new(Guid.NewGuid(), [new(true, [first, second]), new(false, [third])], [], [], []);
        Assert.False(AchievementPack.Eligible(node, new HashSet<Guid> { first }));
        Assert.False(AchievementPack.Eligible(node, new HashSet<Guid> { third }));
        Assert.True(AchievementPack.Eligible(node, new HashSet<Guid> { second, third }));
    }

    /// <summary>
    /// Prevents different hits from combining into one critical quadruple-effective hit.
    /// </summary>
    [Fact]
    public void JointConditionsCannotCombineDifferentObservations()
    {
        AchievementEvidence first = new(), second = new();
        first.Dimensions[_critical] = JsonSerializer.Deserialize<JsonElement>(_true);
        first.Dimensions[_quadruple] = JsonSerializer.Deserialize<JsonElement>(_false);
        second.Dimensions[_critical] = JsonSerializer.Deserialize<JsonElement>(_false);
        second.Dimensions[_quadruple] = JsonSerializer.Deserialize<JsonElement>(_true);
        first.Scalars[_count] = second.Scalars[_count] = BigInteger.One;
        AchievementRule rule = Rule(_sum, _count, _joint);
        Assert.False(AchievementEvaluator.Satisfied(rule, [first, second]));
        second.Dimensions[_critical] = JsonSerializer.Deserialize<JsonElement>(_true);
        Assert.True(AchievementEvaluator.Satisfied(rule, [first, second]));
    }

    /// <summary>
    /// Keeps unavailable evidence different from false and resolves compact native identifiers.
    /// </summary>
    [Fact]
    public void MissingEvidenceNeverProvesFalseAndNativeIdentityMatches()
    {
        AchievementEvidence evidence = new();
        evidence.Scalars[_count] = BigInteger.One;
        Assert.False(AchievementEvaluator.Satisfied(Rule(_sum, _count, _knownFalse), [evidence]));
        evidence.Dimensions[_critical] = JsonSerializer.Deserialize<JsonElement>(_false);
        Assert.True(AchievementEvaluator.Satisfied(Rule(_sum, _count, _knownFalse), [evidence]));
        evidence.Dimensions[_item] = JsonSerializer.Deserialize<JsonElement>(_potion);
        Assert.True(AchievementEvaluator.Satisfied(Rule(_sum, _count, _native), [evidence]));
    }

    /// <summary>
    /// Counts union membership once and compares wide integers and HP ratios exactly.
    /// </summary>
    [Fact]
    public void SetUnionAndWideFractionsRemainExact()
    {
        AchievementEvidence first = new(), second = new();
        first.Sets[_members] = [JsonSerializer.Deserialize<JsonElement>(_first), JsonSerializer.Deserialize<JsonElement>(_second)];
        second.Sets[_members] = [JsonSerializer.Deserialize<JsonElement>(_second), JsonSerializer.Deserialize<JsonElement>(_third)];
        Assert.True(AchievementEvaluator.Satisfied(Rule(_cardinality, _members, _null, 3), [first, second]));
        Assert.False(AchievementEvaluator.Satisfied(Rule(_cardinality, _members, _null, 4), [first, second]));
        BigInteger large = BigInteger.One << 100;
        first.Ratios[_ratio] = (large, large * 2 - 1);
        AchievementRule half = Rule(_fraction, _ratio, _null) with { Threshold = JsonSerializer.Deserialize<JsonElement>(_half) };
        Assert.False(AchievementEvaluator.Satisfied(half, [first]));
        first.Ratios[_ratio] = (large, large * 2);
        Assert.True(AchievementEvaluator.Satisfied(half, [first]));
        first.Scalars[_count] = large;
        Assert.True(AchievementEvaluator.Satisfied(Rule(_maximum, _count, _null), [first]));
    }

    /// <summary>
    /// Creates one private test definition over general summary evidence.
    /// </summary>
    /// <param name="operation">The exact accumulation operation.</param>
    /// <param name="measure">The selected measure.</param>
    /// <param name="filter">A JSON correlated predicate.</param>
    /// <param name="threshold">The positive target count.</param>
    /// <returns>A minimal active rule with no presentation dependency.</returns>
    private static AchievementRule Rule(string operation, string measure, string filter, int threshold = 1)
        => new(1, string.Empty, string.Empty, string.Empty, operation, measure, JsonSerializer.Deserialize<JsonElement>(filter), JsonSerializer.SerializeToElement(threshold), _atLeast);
}
