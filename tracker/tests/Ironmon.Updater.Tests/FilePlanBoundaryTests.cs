using Ironmon.Updater.Core;
using static Ironmon.Updater.Tests.FilePlannerFixture;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Verifies mandatory path protections and rejects ambiguous ownership before any plan can apply.
/// </summary>
public sealed class FilePlanBoundaryTests
{
    private const string Traversal = "Data/../outside.bin";
    private const string Rooted = "/outside.bin";
    private const string DrivePath = "C:/outside.bin";
    private const string StreamPath = "Data/file.bin:stream";
    private const string Backslash = "Data\\file.bin";
    private const string TrailingDot = "Data/file.bin.";
    private const string TrailingSpace = "Data/file.bin ";
    private const string DevicePath = "Data/CON.txt";
    private const string SuperscriptDevice = "Data/COM¹.txt";
    private const string SpacedDevice = "Data/CON .txt";
    private const string EmptyPart = "Data//file.bin";
    private const string ControlPath = "Data/file\n.bin";
    private const string GameSave = "Game.rxdata";
    private const string SlotSave = "Data/File A.rxdata.bak";
    private const string Checkpoint = "Data/Ironmon/IronmonCheckpoint_File_A.rxdata";
    private const string Runs = "Ironmon Tracker/runs/recipe.json";
    private const string Settings = "Ironmon Tracker/settings/favorite-pokemon.json";
    private const string Diagnostics = "Ironmon Tracker/diagnostics/latest.json";
    private const string SpriteState = "Data/Ironmon/custom_sprite_sheet_sync.json";
    private const string UnavailableState = "Data/Ironmon/unavailable_sprite_sheets.json";
    private const string Sheets = "Graphics/CustomBattlers/spritesheets/spritesheets_custom/1.png";
    private const string SpriteCache = "Graphics/CustomBattlers/local_sprites/IronmonTracker/1.png";
    private const string GitConfig = ".git/config";
    private const string Journal = ".ironmon-update/active.json";
    private const string RuntimeFile = "Data/Ironmon/runtime-progress.json";
    private const string Unauthorized = "OtherMod/entry.rb";
    private const string GameParent = "Data/Scripts";
    private const string AlternateCase = "data/Scripts/game.rb";
    private const string SpriteServiceRoot = "Graphics/CustomBattlers/spritesheets/";

    /// <summary>
    /// Rejects Windows path ambiguity in authenticated manifests rather than sanitizing destinations.
    /// </summary>
    /// <param name="path">The unsafe relative file path.</param>
    [Theory]
    [InlineData(Traversal)]
    [InlineData(Rooted)]
    [InlineData(DrivePath)]
    [InlineData(StreamPath)]
    [InlineData(Backslash)]
    [InlineData(TrailingDot)]
    [InlineData(TrailingSpace)]
    [InlineData(DevicePath)]
    [InlineData(SuperscriptDevice)]
    [InlineData(SpacedDevice)]
    [InlineData(EmptyPart)]
    [InlineData(ControlPath)]
    public void RejectsUnsafePaths(string path)
        => Assert.Throws<InvalidDataException>(() => Planner().Create([], [], [File(path, 1)]));

    /// <summary>
    /// Rejects release ownership over saves, runtime state, sprite storage and transaction metadata.
    /// </summary>
    /// <param name="path">The protected file path.</param>
    [Theory]
    [InlineData(GameSave)]
    [InlineData(SlotSave)]
    [InlineData(Checkpoint)]
    [InlineData(Runs)]
    [InlineData(Settings)]
    [InlineData(Diagnostics)]
    [InlineData(SpriteState)]
    [InlineData(UnavailableState)]
    [InlineData(Sheets)]
    [InlineData(SpriteCache)]
    [InlineData(GitConfig)]
    [InlineData(Journal)]
    public void RejectsProtectedReleaseDestinations(string path)
    {
        Assert.Throws<InvalidDataException>(() => Planner().Create([], [], [File(path, 1)]));
        var ironmon = new ManagedFile(path, Content(1), ManagedFileOwner.Ironmon, ManagedFilePolicy.Retain);
        Assert.Throws<InvalidDataException>(() => Planner().Create([], [], [ironmon]));
    }

    /// <summary>
    /// Protects runtime-discovered user files even when an Ironmon release explicitly claims ownership.
    /// </summary>
    [Fact]
    public void RuntimeProtectionsCannotBeOverriddenByManifestFlags()
    {
        var planner = new FileUpdatePlanner(new FileManagementPolicy([RuntimeFile]));
        var file = new ManagedFile(RuntimeFile, Content(1), ManagedFileOwner.Ironmon);
        Assert.Throws<InvalidDataException>(() => planner.Create([], Snapshot((RuntimeFile, 3)), [file]));
        Assert.Throws<InvalidDataException>(() => planner.Create([file], Snapshot((RuntimeFile, 3)), []));
    }

    /// <summary>
    /// Prevents an inventory file from replacing a protected directory or profile store itself.
    /// </summary>
    [Fact]
    public void RejectsProtectedAncestorsAndMutableProfiles()
    {
        Assert.Throws<InvalidDataException>(() => Planner().Create([], [], [File(GameParent, 1)]));
        var profile = new ManagedFile(ProfilePath, Content(1), ManagedFileOwner.Ironmon);
        Assert.Throws<InvalidDataException>(() => Planner().Create([], [], [profile]));
        Assert.Throws<InvalidDataException>(() => Planner().Create([], [], [new ManagedFile(Unauthorized, Content(1), ManagedFileOwner.Ironmon)]));
    }

    /// <summary>
    /// Rejects duplicate ownership, file/directory conflicts and case-only renames.
    /// </summary>
    [Fact]
    public void RejectsAmbiguousInventoryTrees()
    {
        Assert.Throws<InvalidDataException>(() => Planner().Create([], [], [File(GamePath, 1), File(GamePath, 2)]));
        Assert.Throws<InvalidDataException>(() => Planner().Create([], [], [File(GameParent, 1), File(GamePath, 2)]));
        Assert.Throws<InvalidDataException>(() => Planner().Create([File(GamePath, 1)], Snapshot((GamePath, 1)), [File(AlternateCase, 2)]));
        Assert.Throws<InvalidDataException>(() => Planner().Create([], [new LocalFileEntry(GamePath, Content(1))], []));
    }

    /// <summary>
    /// Requires a fresh review when any existing or previously missing path changes after planning.
    /// </summary>
    [Fact]
    public void RevalidationIncludesPreservedFilesAndMissingTargets()
    {
        var snapshot = Snapshot((GamePath, 1), (ExtraPath, 3));
        var plan = Planner().Create([File(GamePath, 1)], snapshot, [File(GamePath, 2), File(OtherPath, 2)]);
        Assert.Equal(FilePlanApplicability.Ready, plan.Assess(snapshot));
        Assert.Equal(FilePlanApplicability.Changed, plan.Assess(Snapshot((GamePath, 1), (ExtraPath, 2))));
        Assert.Equal(FilePlanApplicability.Changed, plan.Assess(Snapshot((GamePath, 1), (ExtraPath, 3), (OtherPath, 3))));
        Assert.Equal(FilePlanApplicability.Changed, plan.Assess(Snapshot((GamePath, 2), (ExtraPath, 3))));
        Assert.Equal(FilePlanApplicability.AlreadyApplied, plan.Assess(Snapshot((GamePath, 2), (ExtraPath, 3), (OtherPath, 2))));
    }

    /// <summary>
    /// Plans current game program files while preserving the sprite-service tree as a separate component.
    /// </summary>
    [Fact]
    public void PlansCurrentGameProgramFilesWithoutMutations()
    {
        var game = Ironmon.Updater.Infrastructure.HoennGameBaseline.Load();
        var programFiles = game.Files.Where(file => !file.Path.StartsWith(SpriteServiceRoot, StringComparison.OrdinalIgnoreCase));
        var files = programFiles.Select(file => new ManagedFile(file.Path, file.Canonical, ManagedFileOwner.Game, ManagedFilePolicy.Replace, file.WindowsText)).ToArray();
        var entries = new Dictionary<string, LocalFileEntry>(StringComparer.Ordinal);
        foreach (var file in game.Files)
        {
            entries[file.Path] = new LocalFileEntry(file.Path, file.Canonical);
            for (var slash = file.Path.LastIndexOf('/'); slash >= 0; slash = file.Path.LastIndexOf('/', slash - 1))
                entries.TryAdd(file.Path[..slash], new LocalFileEntry(file.Path[..slash], null));
        }

        var plan = Planner().Create(files, entries.Values, files);
        Assert.True(plan.CanApply);
        Assert.Empty(plan.GetOperations());
        Assert.All(plan.Entries.Where(entry => entry.Path.StartsWith(SpriteServiceRoot, StringComparison.OrdinalIgnoreCase)), entry => Assert.Null(entry.Target));
        Assert.Equal(FilePlanApplicability.AlreadyApplied, plan.Assess(entries.Values));
    }
}
