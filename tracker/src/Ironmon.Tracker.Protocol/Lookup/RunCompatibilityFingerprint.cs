using System.Security.Cryptography;
using System.Text.Json;

namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Creates the canonical compatibility fingerprint shared by completed-run recipes and seeded-run tokens.
/// </summary>
public static class RunCompatibilityFingerprint
{
    /// <summary>
    /// Creates a SHA-256 fingerprint over every installation-owned input that can affect seeded generation.
    /// </summary>
    /// <param name="recipe">The reproduction recipe whose compatibility metadata will be fingerprinted.</param>
    /// <returns>The lowercase hexadecimal SHA-256 fingerprint.</returns>
    /// <exception cref="ArgumentNullException">Thrown when recipe is null.</exception>
    public static string Create(RunReproductionRecipePayload recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        var manifest = new
        {
            SchemaVersion = 1,
            recipe.GameVersion,
            recipe.IronmonVersion,
            recipe.DataMode,
            SpeciesGenerator = new
            {
                recipe.SpeciesGenerator.Version,
                recipe.SpeciesGenerator.PoolFingerprint
            },
            AbilityGenerator = new
            {
                recipe.AbilityGenerator.Version,
                recipe.AbilityGenerator.PoolSize,
                recipe.AbilityGenerator.PoolFingerprint
            },
            BaseStatGenerator = recipe.BaseStatGenerator is null ? null : new
            {
                recipe.BaseStatGenerator.Version,
                recipe.BaseStatGenerator.SourceFingerprint
            },
            EvolutionGenerator = recipe.EvolutionGenerator is null ? null : CreateEvolutionManifest(recipe.EvolutionGenerator),
            MoveAccessGenerator = recipe.MoveAccessGenerator is null ? null : CreateMoveAccessManifest(recipe.MoveAccessGenerator),
            PlayerFusionGenerator = new
            {
                recipe.PlayerFusionGenerator.Version,
                recipe.PlayerFusionGenerator.PoolSize,
                recipe.PlayerFusionGenerator.PoolFingerprint
            },
            ItemGenerator = recipe.ItemGenerator is null ? null : new
            {
                recipe.ItemGenerator.Version,
                recipe.ItemGenerator.RulesVersion,
                recipe.ItemGenerator.GroundPoolSize,
                recipe.ItemGenerator.GroundTotalWeight,
                recipe.ItemGenerator.GroundPoolFingerprint,
                recipe.ItemGenerator.TmPoolSize,
                recipe.ItemGenerator.TmPoolFingerprint,
                recipe.ItemGenerator.ResultBans,
                recipe.ItemGenerator.ResultBanFingerprint,
                recipe.ItemGenerator.ShopPolicyVersion
            }
        };

        byte[] canonicalJson = JsonSerializer.SerializeToUtf8Bytes(manifest, TrackerJson.Options);
        return Convert.ToHexString(SHA256.HashData(canonicalJson)).ToLowerInvariant();
    }

    /// <summary>
    /// Creates the canonical evolution-generator portion of a compatibility manifest.
    /// </summary>
    /// <param name="evolution">The evolution-generator metadata.</param>
    /// <returns>The serialization model for evolution compatibility.</returns>
    private static object CreateEvolutionManifest(EvolutionGeneratorRecipePayload evolution)
    {
        return new
        {
            evolution.Version,
            evolution.RulesVersion,
            evolution.SourceFingerprint,
            evolution.TaxonomyFingerprint,
            evolution.MethodFingerprint,
            evolution.TargetFingerprint,
            BaseStatGenerator = new
            {
                evolution.BaseStatGenerator.Version,
                evolution.BaseStatGenerator.SourceFingerprint
            },
            Fusion = new
            {
                evolution.Fusion.Version,
                evolution.Fusion.RulesVersion,
                TargetPool = new
                {
                    evolution.Fusion.TargetPool.Version,
                    evolution.Fusion.TargetPool.Size,
                    evolution.Fusion.TargetPool.Fingerprint
                }
            }
        };
    }

    /// <summary>
    /// Creates the canonical move-access-generator portion of a compatibility manifest.
    /// </summary>
    /// <param name="moves">The move-access-generator metadata.</param>
    /// <returns>The serialization model for move-access compatibility.</returns>
    private static object CreateMoveAccessManifest(MoveAccessGeneratorRecipePayload moves)
    {
        return new
        {
            moves.Version,
            moves.PoolFingerprint,
            moves.ContextualRestrictionFingerprint,
            moves.LevelUpSourceFingerprint,
            moves.EggSourceFingerprint,
            Tm = new { moves.Tm.RosterFingerprint, moves.Tm.SourceFingerprint },
            Tr = new { moves.Tr.RosterFingerprint, moves.Tr.SourceFingerprint },
            Tutor = new { moves.Tutor.CatalogFingerprint, moves.Tutor.SourceFingerprint },
            FusionTutor = new { moves.FusionTutor.CatalogFingerprint, moves.FusionTutor.SourceFingerprint }
        };
    }
}
