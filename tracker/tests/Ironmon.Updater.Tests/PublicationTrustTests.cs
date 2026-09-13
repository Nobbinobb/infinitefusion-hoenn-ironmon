using System.Security.Cryptography;
using Ironmon.ReleaseTool;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Verifies publication trust across platform formatting differences without accepting changed keys.
/// </summary>
public sealed class PublicationTrustTests
{
    private const string WindowsNewLine = "\r\n";
    private const string UnixNewLine = "\n";
    private const string OtherKeyId = "another-fixture";
    private const string CandidateTrustName = "update-trusted-keys.json";

    /// <summary>
    /// Signs unchanged candidate bytes when the approved checkout uses different line endings.
    /// </summary>
    /// <param name="windowsCandidate">Whether the candidate was built with Windows line endings.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicationAcceptsEquivalentTrustAcrossPlatforms(bool windowsCandidate)
    {
        using var fixture = new ReleaseBundleFixture();
        var json = File.ReadAllText(fixture.Trust);
        File.WriteAllText(fixture.Trust, json + (windowsCandidate ? WindowsNewLine : UnixNewLine));
        fixture.Build(ReleaseBundleFixture.VersionA);
        var candidatePath = Path.Combine(fixture.DirectoryFor(ReleaseBundleFixture.VersionA), CandidateTrustName);
        var frozen = File.ReadAllBytes(candidatePath);
        File.WriteAllText(fixture.Trust, json + (windowsCandidate ? UnixNewLine : WindowsNewLine));
        fixture.Sign(ReleaseBundleFixture.VersionA);
        Assert.Equal(frozen, File.ReadAllBytes(candidatePath));
        ReleaseBundle.Verify(fixture.DirectoryFor(ReleaseBundleFixture.VersionA), fixture.Trust, true);
    }

    /// <summary>
    /// Rejects a changed key identity, key value, or additional trusted key even before signing.
    /// </summary>
    /// <param name="change">The independently approved trust mismatch to introduce.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PublicationRejectsDifferentTrust(int change)
    {
        using var fixture = new ReleaseBundleFixture();
        fixture.Build(ReleaseBundleFixture.VersionA);
        var keys = ReleaseBundle.ReadTrust(fixture.Trust).ToDictionary(pair => pair.Key, pair => Convert.ToBase64String(pair.Value));
        using var replacement = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (change == 0)
        {
            keys.Add(OtherKeyId, keys[ReleaseBundleFixture.KeyId]);
            keys.Remove(ReleaseBundleFixture.KeyId);
        }
        else if (change == 1)
        {
            keys[ReleaseBundleFixture.KeyId] = Convert.ToBase64String(replacement.ExportSubjectPublicKeyInfo());
        }
        else
        {
            keys.Add(OtherKeyId, Convert.ToBase64String(replacement.ExportSubjectPublicKeyInfo()));
        }

        File.WriteAllBytes(fixture.Trust, ReleaseJson.Serialize(keys));
        Assert.Throws<InvalidDataException>(() => ReleaseBundle.Verify(fixture.DirectoryFor(ReleaseBundleFixture.VersionA), fixture.Trust, false));
    }
}
