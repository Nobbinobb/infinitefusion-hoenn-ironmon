using System.Security.Cryptography;

namespace Ironmon.Tracker.Seeds;

/// <summary>
/// Provides the public integrity material shared by ordinary seeded-run tokens.
/// </summary>
/// <remarks>
/// This material identifies the token family and is not secret or maintainer authority.
/// </remarks>
public static class SeedTokenSharedKey
{
    private static readonly byte[] _material = SHA256.HashData("Ironmon seeded-run shared integrity material v1"u8);

    /// <summary>
    /// Gets the stable public HS256 key material used by ordinary trackers.
    /// </summary>
    public static ReadOnlySpan<byte> Material => _material;
}
