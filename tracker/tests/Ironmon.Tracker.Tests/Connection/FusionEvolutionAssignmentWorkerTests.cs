using Ironmon.Tracker.Connection.Obtainability;
using System.Buffers.Binary;

namespace Ironmon.Tracker.Tests.Connection;

/// <summary>
/// Verifies exact cross-runtime complete-fusion evolution assignment parity.
/// </summary>
public sealed class FusionEvolutionAssignmentWorkerTests
{
    private const int MaterialIdBits = 10;

    /// <summary>
    /// Confirms every generated Ruby reference assignment is reproduced exactly.
    /// </summary>
    [Fact]
    public void GeneratedAssignmentsMatchRubyReferences()
    {
        PlayerFusionMappingWorkerCatalog catalog = PlayerFusionMappingWorkerCatalog.Load();
        FusionEvolutionAssignmentWorker worker = new(catalog);
        foreach (IGrouping<long, PlayerFusionEvolutionReferenceMapping> seedGroup in catalog.EvolutionVerificationMappings.GroupBy(mapping => mapping.Seed))
        {
            Dictionary<int, FusionEvolutionSourceAssignment> actual = worker
                .GenerateAll(seedGroup.Key, [.. seedGroup.Select(mapping => mapping.PackedSourceComponents)])
                .ToDictionary(assignment => assignment.PackedSourceComponents);
            foreach (PlayerFusionEvolutionReferenceMapping expected in seedGroup)
            {
                FusionEvolutionSourceAssignment assignment = actual[expected.PackedSourceComponents];
                Assert.Equal(expected.Branches.Count, assignment.Branches.Count);
                for (int index = 0; index < expected.Branches.Count; index++)
                {
                    PlayerFusionEvolutionReferenceBranch expectedBranch = expected.Branches[index];
                    FusionEvolutionAssignedBranch actualBranch = assignment.Branches[index];
                    Assert.Equal(expectedBranch.Side, (int)actualBranch.Side);
                    Assert.Equal(expectedBranch.ComponentBranchIdentity, actualBranch.ComponentBranchIdentity);
                    Assert.Equal(expectedBranch.TargetId, actualBranch.TargetId);
                }
            }
        }
    }

    /// <summary>
    /// Verifies native component candidate lists are bounded, stronger than their source, and safe for one protocol request.
    /// </summary>
    [Fact]
    public void ComponentCandidatesUseSharedPreparedStateAndBoundedPayloads()
    {
        PlayerFusionMappingWorkerCatalog catalog = PlayerFusionMappingWorkerCatalog.Load();
        FusionEvolutionAssignmentWorker worker = new(catalog);
        PlayerFusionEvolutionReferenceMapping reference = catalog.EvolutionVerificationMappings.First(mapping => mapping.Branches.Any(branch => branch.Side == (int)FusionComponentSide.Body));
        FusionEvolutionSourceAssignment source = worker.Generate(reference.Seed, reference.PackedSourceComponents);

        IReadOnlyList<FusionEvolutionCandidateTarget> candidates = worker.GetCandidateTargets(reference.Seed, reference.PackedSourceComponents, FusionComponentSide.Body);
        DebugEvolutionCandidateSearchRequestPayload request = new()
        {
            SpeciesId = $"B{reference.PackedSourceComponents >> MaterialIdBits}H{reference.PackedSourceComponents & ((1 << MaterialIdBits) - 1)}:0",
            Side = EvolutionCandidateSide.Body,
            PackedCandidateAssignments = PackCandidates(candidates)
        };

        string json = TrackerJson.SerializePayload(request).GetRawText();
        string maximumJson = TrackerJson.SerializePayload(new DebugEvolutionCandidateSearchRequestPayload
        {
            SpeciesId = request.SpeciesId,
            Side = request.Side,
            PackedCandidateAssignments = new byte[catalog.CustomFusionPool.Count * sizeof(uint)]
        }).GetRawText();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate => Assert.True(candidate.TargetBst > source.SourceBst));
        Assert.True(json.Length < TrackerProtocol.MaximumMessageCharacters, $"The native candidate request required {json.Length} characters for {candidates.Count} candidates.");
        Assert.True(maximumJson.Length < TrackerProtocol.MaximumMessageCharacters, $"The theoretical full-pool candidate request required {maximumJson.Length} characters.");
    }

    /// <summary>
    /// Packs candidate targets with the production request representation.
    /// </summary>
    /// <param name="candidates">The native candidates.</param>
    /// <returns>The little-endian packed candidates.</returns>
    private static byte[] PackCandidates(IReadOnlyList<FusionEvolutionCandidateTarget> candidates)
    {
        const int CandidateBstBits = 11;
        byte[] result = new byte[candidates.Count * sizeof(uint)];
        for (int index = 0; index < candidates.Count; index++)
        {
            uint value = (uint)candidates[index].TargetId << CandidateBstBits | (uint)candidates[index].TargetBst;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * sizeof(uint), sizeof(uint)), value);
        }

        return result;
    }
}
