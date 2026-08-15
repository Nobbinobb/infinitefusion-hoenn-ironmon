using Microsoft.IdentityModel.Tokens;
using System.Collections.Frozen;

namespace Ironmon.Tracker.Access;

/// <summary>
/// Resolves the public ES256 verification keys trusted for diagnostic-access tokens.
/// </summary>
public sealed class DiagnosticAccessKeyring
{
    private readonly FrozenDictionary<string, ECDsaSecurityKey> _keys;

    /// <summary>
    /// Initializes a public verification-key ring.
    /// </summary>
    /// <param name="keys">The trusted P-256 public keys with unique key IDs.</param>
    /// <exception cref="ArgumentNullException">Thrown when keys is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a key is missing an ID, is not P-256, or duplicates another ID.</exception>
    /// <remarks>The caller retains ownership of each key and its ECDsa instance.</remarks>
    public DiagnosticAccessKeyring(IEnumerable<ECDsaSecurityKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        Dictionary<string, ECDsaSecurityKey> verified = new(StringComparer.Ordinal);
        foreach (ECDsaSecurityKey key in keys)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (string.IsNullOrWhiteSpace(key.KeyId) || key.KeyId.Length > DiagnosticAccessTokenConstants.MaximumKeyIdLength)
                throw new ArgumentException("Every diagnostic access key requires a bounded key ID.", nameof(keys));

            if (key.KeySize != 256)
                throw new ArgumentException("Every diagnostic access key must use the P-256 curve.", nameof(keys));

            if (!verified.TryAdd(key.KeyId, key))
                throw new ArgumentException($"Diagnostic access key ID '{key.KeyId}' is duplicated.", nameof(keys));
        }

        _keys = verified.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the number of trusted public keys.
    /// </summary>
    public int Count => _keys.Count;

    /// <summary>
    /// Resolves one trusted verification key.
    /// </summary>
    /// <param name="keyId">The protected JWT key ID.</param>
    /// <param name="key">The resolved verification key.</param>
    /// <returns>Whether the key ID is trusted.</returns>
    public bool TryGet(string keyId, out ECDsaSecurityKey? key)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            key = null;
            return false;
        }

        return _keys.TryGetValue(keyId, out key);
    }
}
