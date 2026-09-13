using System.Text;
using System.Security.Cryptography;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises startup revalidation without trusting local receipts or a Git installation.
/// </summary>
public sealed class GameCompatibilityTests
{
    private const string Commit = "1111111111111111111111111111111111111111";
    private const string Other = "2222222222222222222222222222222222222222";
    private const string Script = "Data/Scripts/game.rb";
    private const string Canonical = "game\n";
    private const string Windows = "game\r\n";
    private const string Mode = "100644";
    private const string GitHead = ".git/HEAD";
    private const string Loose = ".git/refs/heads/releases";
    private const string Packed = ".git/packed-refs";
    private const string Reference = "ref: refs/heads/releases\n";
    private const string PackedSuffix = " refs/heads/releases\n";
    private const string Active = ".ironmon-update/active.json";

    /// <summary>
    /// Accepts ZIP bytes and normal loose, packed and detached Git identities at the approved revision.
    /// </summary>
    /// <param name="layout">Zero for ZIP, one for loose, two for packed or three for detached.</param>
    /// <param name="windows">Whether the file uses its signed Windows representation.</param>
    /// <returns>The completed startup check.</returns>
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public async Task RecognizesApprovedInstallation(int layout, bool windows)
    {
        using var fixture = new TestWorkspace();
        Write(fixture, Script, windows ? Windows : Canonical);
        if (layout > 0)
            Write(fixture, GitHead, layout == 3 ? Commit : Reference);

        if (layout == 1)
            Write(fixture, Loose, Commit);

        if (layout == 2)
            Write(fixture, Packed, Commit + PackedSuffix);

        Assert.True((await GameCompatibilityCheck.InspectAsync(fixture.Root, Baseline())).Compatible);
    }

    /// <summary>
    /// Blocks an incomplete update, external revision change, edited program bytes or missing executable data.
    /// </summary>
    /// <param name="caseNumber">The local incompatibility to simulate.</param>
    /// <returns>The completed rejection check.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task RejectsUnverifiedInstallation(int caseNumber)
    {
        using var fixture = new TestWorkspace();
        if (caseNumber != 3)
            Write(fixture, Script, caseNumber == 2 ? Other : Canonical);

        if (caseNumber == 0)
            Write(fixture, Active, Other);

        if (caseNumber == 1)
            Write(fixture, GitHead, Other);

        Assert.False((await GameCompatibilityCheck.InspectAsync(fixture.Root, Baseline())).Compatible);
    }

    /// <summary>
    /// Creates an independently held executable baseline with explicit text alternatives.
    /// </summary>
    /// <returns>The trusted fixture inventory.</returns>
    private static GameBaseline Baseline()
        => new(Commit, [new(Script, Mode, Commit, Content(Canonical), Content(Windows))]);

    /// <summary>
    /// Fingerprints exact fixture bytes.
    /// </summary>
    /// <param name="value">The canonical text.</param>
    /// <returns>The expected file size and checksum.</returns>
    private static GameFileContent Content(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Writes a fixture file beneath the owned temporary root.
    /// </summary>
    /// <param name="fixture">The disposable root.</param>
    /// <param name="path">The relative fixture file.</param>
    /// <param name="value">Its exact UTF-8 text.</param>
    private static void Write(TestWorkspace fixture, string path, string value)
    {
        var destination = fixture.PathFor(path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, value);
    }
}
