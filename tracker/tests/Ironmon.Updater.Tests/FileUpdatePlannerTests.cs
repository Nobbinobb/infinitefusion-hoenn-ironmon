using Ironmon.Updater.Core;
using static Ironmon.Updater.Tests.FilePlannerFixture;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Verifies three-way updates, explicit conflict decisions and preservation of unrelated content.
/// </summary>
public sealed class FileUpdatePlannerTests
{
    private const string LegacyReadme = "README.md";
    private const string ParentPath = "Data/shape";
    private const string ChildPath = "Data/shape/owned.bin";
    private const string ExtraChild = "Data/shape/personal.bin";
    private const string EmptyChild = "Data/shape/personal";

    /// <summary>
    /// Covers every distinct missing/equal/changed content relationship in the three-way decision table.
    /// </summary>
    /// <param name="baseline">The old content byte, or zero for absence.</param>
    /// <param name="local">The local content byte, or zero for absence.</param>
    /// <param name="target">The new content byte, or zero for absence.</param>
    /// <param name="action">The required action.</param>
    /// <param name="conflict">The required conflict reason.</param>
    [Theory]
    [InlineData(0, 1, 0, FilePlanAction.Keep, FileConflictKind.None)]
    [InlineData(0, 0, 1, FilePlanAction.Write, FileConflictKind.None)]
    [InlineData(0, 1, 1, FilePlanAction.Keep, FileConflictKind.None)]
    [InlineData(0, 2, 1, FilePlanAction.Conflict, FileConflictKind.NewFileCollision)]
    [InlineData(1, 0, 0, FilePlanAction.Keep, FileConflictKind.None)]
    [InlineData(1, 1, 0, FilePlanAction.Delete, FileConflictKind.None)]
    [InlineData(1, 2, 0, FilePlanAction.Conflict, FileConflictKind.ModifiedObsoleteFile)]
    [InlineData(1, 0, 1, FilePlanAction.Keep, FileConflictKind.None)]
    [InlineData(1, 1, 1, FilePlanAction.Keep, FileConflictKind.None)]
    [InlineData(1, 2, 1, FilePlanAction.Keep, FileConflictKind.None)]
    [InlineData(1, 0, 2, FilePlanAction.Conflict, FileConflictKind.LocalDeletion)]
    [InlineData(1, 1, 2, FilePlanAction.Write, FileConflictKind.None)]
    [InlineData(1, 2, 2, FilePlanAction.Keep, FileConflictKind.None)]
    [InlineData(1, 3, 2, FilePlanAction.Conflict, FileConflictKind.LocalModification)]
    public void PlansThreeWayContent(int baseline, int local, int target, FilePlanAction action, FileConflictKind conflict)
    {
        ManagedFile[] before = baseline == 0 ? [] : [File(GamePath, baseline)];
        ManagedFile[] after = target == 0 ? [] : [File(GamePath, target)];
        var snapshot = local == 0 ? [] : Snapshot((GamePath, local));
        var plan = Planner().Create(before, snapshot, after);
        var entry = Assert.Single(plan.Entries);
        Assert.Equal(action, entry.Action);
        Assert.Equal(conflict, entry.Conflict);
        Assert.Equal(action != FilePlanAction.Conflict, plan.CanApply);
        if (action == FilePlanAction.Conflict)
        {
            Assert.Throws<InvalidOperationException>(() => plan.GetOperations());
        }
        else if (action is FilePlanAction.Write or FilePlanAction.Delete)
        {
            var operation = Assert.Single(plan.GetOperations(), operation => operation.Kind is FileOperationKind.WriteFile or FileOperationKind.DeleteFile);
            Assert.Equal(local == 0 ? null : Content(local), operation.ExpectedBefore);
            Assert.Equal(target == 0 ? null : Content(target), operation.ExpectedAfter);
            Assert.Equal(local != 0, operation.RequiresBackup);
        }
        else
        {
            Assert.Empty(plan.GetOperations());
        }
    }

    /// <summary>
    /// Blocks the entire update until every replaceable conflict receives exact explicit consent.
    /// </summary>
    [Fact]
    public void PartialConsentCannotExposeAnyOperations()
    {
        var original = Planner().Create([File(GamePath, 1), File(OtherPath, 1)], Snapshot((GamePath, 3), (OtherPath, 3), (ExtraPath, 3)), [File(GamePath, 2), File(OtherPath, 2)]);
        var partial = original.ApproveReplacements([GamePath]);
        Assert.False(partial.CanApply);
        Assert.Throws<InvalidOperationException>(() => partial.GetOperations());
        var approved = partial.ApproveReplacements([OtherPath]);
        Assert.True(approved.CanApply);
        Assert.All(approved.GetOperations(), operation => Assert.True(operation.RequiresBackup));
        Assert.Equal(2, approved.GetOperations().Count);
        Assert.DoesNotContain(approved.GetOperations(), operation => operation.Path == ExtraPath);
        Assert.False(original.CanApply);
        Assert.Throws<InvalidOperationException>(() => original.ApproveReplacements([ExtraPath]));
        Assert.Equal(FilePlanApplicability.Changed, approved.Assess(Snapshot((GamePath, 2), (OtherPath, 3), (ExtraPath, 3))));
    }

    /// <summary>
    /// Converts only explicitly approved file conflicts into the correct backed-up replacement or deletion.
    /// </summary>
    /// <param name="baseline">The old content byte, or zero for absence.</param>
    /// <param name="local">The local content byte, or zero for absence.</param>
    /// <param name="target">The target content byte, or zero for deletion.</param>
    [Theory]
    [InlineData(1, 3, 2)]
    [InlineData(1, 0, 2)]
    [InlineData(1, 3, 0)]
    [InlineData(0, 3, 2)]
    public void ExplicitReplacementRetainsExactBackupRequirements(int baseline, int local, int target)
    {
        ManagedFile[] before = baseline == 0 ? [] : [File(GamePath, baseline)];
        ManagedFile[] after = target == 0 ? [] : [File(GamePath, target)];
        var snapshot = local == 0 ? [] : Snapshot((GamePath, local));
        var plan = Planner().Create(before, snapshot, after).ApproveReplacements([GamePath]);
        var operation = Assert.Single(plan.GetOperations(), operation => operation.Kind is FileOperationKind.WriteFile or FileOperationKind.DeleteFile);
        Assert.Equal(target == 0 ? FileOperationKind.DeleteFile : FileOperationKind.WriteFile, operation.Kind);
        Assert.Equal(local != 0, operation.RequiresBackup);
        Assert.Equal(local == 0 ? null : Content(local), operation.ExpectedBefore);
        Assert.Equal(FilePlanApplicability.Ready, plan.Assess(snapshot));
    }

    /// <summary>
    /// Records real local text bytes for backup while accepting an explicitly approved line-ending alternative.
    /// </summary>
    [Fact]
    public void UsesExactLocalBytesForAlternativeContent()
    {
        var oldFile = File(GamePath, 1) with { AlternateContent = Content(3) };
        var plan = Planner().Create([oldFile], Snapshot((GamePath, 3)), [File(GamePath, 2)]);
        var operation = Assert.Single(plan.GetOperations());
        Assert.Equal(Content(3), operation.ExpectedBefore);
        Assert.Equal(Content(2), operation.ExpectedAfter);
    }

    /// <summary>
    /// Adds new profiles but never deletes historical profiles or overwrites an existing identity.
    /// </summary>
    [Fact]
    public void RetainsImmutableProfilesAndRejectsSameIdentityChanges()
    {
        var profile = new ManagedFile(ProfilePath, Content(1), ManagedFileOwner.Ironmon, ManagedFilePolicy.Retain);
        var added = Planner().Create([], [], [profile]);
        Assert.Single(added.GetOperations(), operation => operation.Kind == FileOperationKind.WriteFile);
        Assert.Empty(Planner().Create([profile], Snapshot((ProfilePath, 1)), []).GetOperations());
        var changed = Planner().Create([profile], Snapshot((ProfilePath, 3)), [profile]);
        Assert.Equal(FileConflictKind.RetainedContent, Assert.Single(changed.Entries).Conflict);
        Assert.Throws<InvalidOperationException>(() => changed.ApproveReplacements([ProfilePath]));
        Assert.Throws<InvalidDataException>(() => Planner().Create([profile], Snapshot((ProfilePath, 1)), [profile with { Content = Content(2) }]));
        Assert.Throws<InvalidDataException>(() => Planner().Create([profile], Snapshot((ProfilePath, 1)), [profile with { Policy = ManagedFilePolicy.Replace }]));
        Assert.Throws<InvalidDataException>(() => Planner().Create([], [], [profile with { AlternateContent = Content(3) }]));
    }

    /// <summary>
    /// Deletes only unchanged obsolete scripts and preserves unrelated additions and legacy root documents.
    /// </summary>
    [Fact]
    public void RemovesOwnedObsoleteScriptsOnly()
    {
        var script = new ManagedFile(ScriptPath, Content(1), ManagedFileOwner.Ironmon);
        var legacy = new ManagedFile(LegacyReadme, Content(1), ManagedFileOwner.Ironmon);
        var plan = Planner().Create([script, legacy], Snapshot((ScriptPath, 1), (LegacyReadme, 3), (ExtraPath, 2)), []);
        var operation = Assert.Single(plan.GetOperations());
        Assert.Equal(ScriptPath, operation.Path);
        Assert.Equal(FileOperationKind.DeleteFile, operation.Kind);
        Assert.True(operation.RequiresBackup);
    }

    /// <summary>
    /// Defines empty and unchanged updates as already applied without generating mutations.
    /// </summary>
    [Fact]
    public void NoOpAndInputOrderingAreDeterministic()
    {
        var files = new[] { File(GamePath, 1), File(OtherPath, 2) };
        var snapshot = Snapshot((GamePath, 3), (OtherPath, 2), (ExtraPath, 1));
        var first = Planner().Create(files, snapshot, files);
        var reversed = Planner().Create(files.Reverse(), snapshot.Reverse(), files.Reverse());
        Assert.Equal(first.Entries, reversed.Entries);
        Assert.Empty(first.GetOperations());
        Assert.Equal(FilePlanApplicability.AlreadyApplied, first.Assess(snapshot));
        Assert.Equal(FilePlanApplicability.AlreadyApplied, Planner().Create([], [], []).Assess([]));
        files[0] = File(GamePath, 2);
        Assert.Equal(Content(1), first.Entries.Single(entry => entry.Path == GamePath).Target!.Content);
    }

    /// <summary>
    /// Orders a clean managed file-to-directory transition without deleting unrelated files.
    /// </summary>
    [Fact]
    public void PlansOwnedFileToDirectoryTransition()
    {
        var plan = Planner().Create([File(ParentPath, 1)], Snapshot((ParentPath, 1)), [File(ChildPath, 2)]);
        Assert.Equal(new[] { FileOperationKind.DeleteFile, FileOperationKind.CreateDirectory, FileOperationKind.WriteFile }, plan.GetOperations().Select(operation => operation.Kind));
        Assert.Equal(FilePlanApplicability.AlreadyApplied, plan.Assess(Snapshot((ChildPath, 2))));
    }

    /// <summary>
    /// Removes only known empty directories after all their old managed files are deleted.
    /// </summary>
    [Fact]
    public void PlansOwnedDirectoryToFileTransition()
    {
        var plan = Planner().Create([File(ChildPath, 1)], Snapshot((ChildPath, 1)), [File(ParentPath, 2)]);
        Assert.Equal([FileOperationKind.DeleteFile, FileOperationKind.RemoveDirectory, FileOperationKind.WriteFile], plan.GetOperations().Select(operation => operation.Kind));
        Assert.Equal(FilePlanApplicability.AlreadyApplied, plan.Assess(Snapshot((ParentPath, 2))));
    }

    /// <summary>
    /// Keeps a directory transition blocked until a modified obsolete child is explicitly approved.
    /// </summary>
    [Fact]
    public void ChildConflictConsentReevaluatesDirectoryTransition()
    {
        var plan = Planner().Create([File(ChildPath, 1)], Snapshot((ChildPath, 3)), [File(ParentPath, 2)]);
        Assert.False(plan.CanApply);
        var approved = plan.ApproveReplacements([ChildPath]);
        Assert.True(approved.CanApply);
        Assert.Equal(Content(3), approved.GetOperations()[0].ExpectedBefore);
    }

    /// <summary>
    /// Prevents structural replacement from sweeping away unrelated files or empty directories.
    /// </summary>
    /// <param name="emptyDirectory">Whether the unrelated addition is an empty directory.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryReplacementPreservesUnrelatedDescendants(bool emptyDirectory)
    {
        var local = emptyDirectory ? Snapshot((ChildPath, 1)).Append(new LocalFileEntry(EmptyChild, null)) : Snapshot((ChildPath, 1), (ExtraChild, 3));
        var plan = Planner().Create([File(ChildPath, 1)], local, [File(ParentPath, 2)]);
        Assert.False(plan.CanApply);
        Assert.Equal(FileConflictKind.PathCollision, plan.Entries.Single(entry => entry.Path == ParentPath).Conflict);
        Assert.Throws<InvalidOperationException>(() => plan.ApproveReplacements([ParentPath]));
        Assert.Throws<InvalidOperationException>(() => plan.GetOperations());
    }

    /// <summary>
    /// Blocks new child files beneath an unrelated existing parent file.
    /// </summary>
    [Fact]
    public void ChildWriteCannotDeleteAnUnrelatedParent()
    {
        var plan = Planner().Create([], Snapshot((ParentPath, 3)), [File(ChildPath, 2)]);
        Assert.False(plan.CanApply);
        Assert.Equal(FilePlanAction.Keep, plan.Entries.Single(entry => entry.Path == ParentPath).Action);
        Assert.Throws<InvalidOperationException>(() => plan.ApproveReplacements([ChildPath]));
    }
}
