using System.Text.Json;

namespace Ironmon.Tracker.Connection.CompletedRuns;

/// <summary>
/// Validates persisted completed-run recipe contracts and nested generator manifests.
/// </summary>
internal static class CompletedRunRecipeValidator
{
    /// <summary>
    /// Determines whether a JSON document has the required nested recipe contract.
    /// </summary>
    /// <param name="root">The parsed recipe root.</param>
    /// <returns><see langword="true"/> when required nested manifests are present as objects.</returns>
    internal static bool HasRequiredShape(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object
            && HasObjectProperty(root, "configuration")
            && HasObjectProperty(root, "species_generator")
            && HasObjectProperty(root, "ability_generator")
            && HasObjectProperty(root, "player_fusion_generator");
    }

    /// <summary>
    /// Validates the required identity and generator metadata of one recipe.
    /// </summary>
    /// <param name="recipe">The recipe to validate.</param>
    internal static void Validate(CompletedRunRecipePayload recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentOutOfRangeException.ThrowIfNotEqual(recipe.SchemaVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.Result);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.GameVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.IronmonVersion);
        ArgumentNullException.ThrowIfNull(recipe.Configuration);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.Configuration.SchemaVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.Configuration.WildPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.Configuration.TrainerPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.Configuration.UnfusionSetting);

        ArgumentNullException.ThrowIfNull(recipe.SpeciesGenerator);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.SpeciesGenerator.Version, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.SpeciesGenerator.PoolFingerprint);

        ArgumentNullException.ThrowIfNull(recipe.AbilityGenerator);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.AbilityGenerator.Version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.AbilityGenerator.PoolSize, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.AbilityGenerator.PoolFingerprint);

        if (recipe.BaseStatGenerator is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(recipe.BaseStatGenerator.Version, 1);
            ArgumentException.ThrowIfNullOrWhiteSpace(recipe.BaseStatGenerator.SourceFingerprint);
        }

        if (recipe.EvolutionGenerator is not null)
            ValidateEvolutionGenerator(recipe.EvolutionGenerator);

        if (recipe.MoveAccessGenerator is not null)
            ValidateMoveAccessGenerator(recipe.MoveAccessGenerator);

        ArgumentNullException.ThrowIfNull(recipe.PlayerFusionGenerator);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.PlayerFusionGenerator.Version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(recipe.PlayerFusionGenerator.PoolSize, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.PlayerFusionGenerator.PoolFingerprint);

        if (recipe.ItemGenerator is not null)
            ValidateItemGenerator(recipe.ItemGenerator);

        if (recipe.Statistics is not null)
            ValidateStatistics(recipe.Statistics);
    }

    /// <summary>
    /// Determines whether a JSON object contains one named object property.
    /// </summary>
    /// <param name="root">The containing JSON object.</param>
    /// <param name="propertyName">The serialized property name.</param>
    /// <returns><see langword="true"/> when the property exists and contains an object.</returns>
    private static bool HasObjectProperty(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Object;

    /// <summary>
    /// Validates deterministic item-slot generator metadata.
    /// </summary>
    /// <param name="generator">The item generator metadata to validate.</param>
    private static void ValidateItemGenerator(ItemGeneratorRecipePayload generator)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.RulesVersion, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.GroundPoolSize, 1);
        if (generator.RulesVersion >= 3)
            ArgumentOutOfRangeException.ThrowIfLessThan(generator.GroundTotalWeight, 1);

        ArgumentException.ThrowIfNullOrWhiteSpace(generator.GroundPoolFingerprint);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.TmPoolSize, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.TmPoolFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.ResultBanFingerprint);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.ShopPolicyVersion, 1);
        if (generator.ResultBans.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Item result bans must have identifiers.", nameof(generator));

        if (generator.ResultBans.Distinct(StringComparer.Ordinal).Count() != generator.ResultBans.Count)
            throw new ArgumentException("Item result bans must be unique.", nameof(generator));
    }

    /// <summary>
    /// Validates one authoritative run-statistics payload.
    /// </summary>
    /// <param name="statistics">The statistics to validate.</param>
    private static void ValidateStatistics(RunStatisticsPayload statistics)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(statistics.SchemaVersion, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(statistics.AttemptNumber, 1);
        if (statistics.SaveSlot is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(statistics.SaveSlot);

        ArgumentException.ThrowIfNullOrWhiteSpace(statistics.Result);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.ActiveSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.AttemptsStarted);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.AttemptsLost);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.AttemptsWon);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.AttemptsAbandoned);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.BattlesCompleted);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.HighestPlayerLevel);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.BadgesEarned);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.TotalItemHealing);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.WastedItemHealing);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.ItemsUsed);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.TrainerSpeciesDistinct);
        ArgumentOutOfRangeException.ThrowIfNegative(statistics.TrainerDefeatedCount);
        if (statistics.TrainerDefeatedBstAverage is double average)
            ArgumentOutOfRangeException.ThrowIfNegative(average);

        if (statistics.TrainerDefeatedBstMinimum is int minimum)
            ArgumentOutOfRangeException.ThrowIfNegative(minimum);

        if (statistics.TrainerDefeatedBstMaximum is int maximum)
            ArgumentOutOfRangeException.ThrowIfNegative(maximum);

        foreach (IReadOnlyDictionary<string, int> counts in statistics.ItemsBySource.Values)
        {
            foreach (int count in counts.Values)
                ArgumentOutOfRangeException.ThrowIfNegative(count);
        }

        foreach (int count in statistics.TrainerSpeciesCounts.Values)
            ArgumentOutOfRangeException.ThrowIfNegative(count);

        foreach (KeyValuePair<string, string> species in statistics.TrainerSpeciesNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(species.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(species.Value);
        }
    }

    /// <summary>
    /// Validates one complete evolution-generator manifest.
    /// </summary>
    /// <param name="generator">The manifest to validate.</param>
    private static void ValidateEvolutionGenerator(EvolutionGeneratorRecipePayload generator)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.RulesVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.SourceFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.TaxonomyFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.MethodFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.TargetFingerprint);
        ArgumentNullException.ThrowIfNull(generator.BaseStatGenerator);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.BaseStatGenerator.Version, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.BaseStatGenerator.SourceFingerprint);
        ArgumentNullException.ThrowIfNull(generator.Fusion);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Fusion.Version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Fusion.RulesVersion, 1);
        ArgumentNullException.ThrowIfNull(generator.Fusion.TargetPool);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Fusion.TargetPool.Version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Fusion.TargetPool.Size, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.Fusion.TargetPool.Fingerprint);
    }

    /// <summary>
    /// Validates one complete move-access-generator manifest.
    /// </summary>
    /// <param name="generator">The manifest to validate.</param>
    private static void ValidateMoveAccessGenerator(MoveAccessGeneratorRecipePayload generator)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(generator.Version, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.PoolFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.ContextualRestrictionFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.LevelUpSourceFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator.EggSourceFingerprint);
        ValidateMachineSource(generator.Tm);
        ValidateMachineSource(generator.Tr);
        ValidateTutorSource(generator.Tutor);
        ValidateTutorSource(generator.FusionTutor);
    }

    /// <summary>
    /// Validates one machine source manifest.
    /// </summary>
    /// <param name="source">The manifest to validate.</param>
    private static void ValidateMachineSource(MachineSourceRecipePayload source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.RosterFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceFingerprint);
    }

    /// <summary>
    /// Validates one tutor source manifest.
    /// </summary>
    /// <param name="source">The manifest to validate.</param>
    private static void ValidateTutorSource(TutorSourceRecipePayload source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.CatalogFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceFingerprint);
    }
}
