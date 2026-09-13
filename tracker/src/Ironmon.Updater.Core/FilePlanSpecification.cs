namespace Ironmon.Updater.Core;

/// <summary>
/// Carries reviewed planner inputs across the tracker and recovery process boundary.
/// </summary>
/// <remarks>
/// Constructs a serializable specification. Callers must authenticate it before using it as write authority.
/// </remarks>
/// <param name="Baseline">The prior managed inventory.</param>
/// <param name="Local">The complete original installation snapshot.</param>
/// <param name="Target">The desired managed inventory.</param>
/// <param name="ApprovedPaths">The exact file conflicts approved for backed-up replacement.</param>
/// <param name="ProtectedPaths">Additional immutable policy exclusions.</param>
public sealed record FilePlanSpecification(ManagedFile[] Baseline, LocalFileEntry[] Local, ManagedFile[] Target, string[] ApprovedPaths, string[] ProtectedPaths)
{
    /// <summary>
    /// Reconstructs the plan through normal ownership and path validation instead of trusting serialized operations.
    /// </summary>
    /// <returns>The fully validated plan with the captured review decisions.</returns>
    public FileUpdatePlan Rebuild()
    {
        var plan = new FileUpdatePlanner(new FileManagementPolicy(ProtectedPaths)).Create(Baseline, Local, Target);
        return ApprovedPaths.Length == 0 ? plan : plan.ApproveReplacements(ApprovedPaths);
    }
}
