using System.Security.Cryptography;
using System.Text.Json;

namespace Ironmon.Tracker.Collection.Achievements;

/// <summary>
/// Opens only eligible nodes from the independently authenticated spoiler-barrier pack.
/// </summary>
internal sealed class AchievementPack
{
    private const string _resource = "Ironmon.Tracker.Collection.Achievements.pack";
    private const string _keyPartOne = "93E2B82EA8F9B85E1C1E98493D49BD58";
    private const string _keyPartTwo = "6333D0A0737C5BD16B4D9E7C8628F239";
    private static readonly byte[] _root = Convert.FromHexString(_keyPartOne + _keyPartTwo);
    private readonly Dictionary<Guid, SealedAchievement> _nodes;
    private readonly Dictionary<Guid, AchievementRule> _opened = [];

    /// <summary>
    /// Loads ciphertext and opaque gates without opening any rule definition.
    /// </summary>
    public AchievementPack()
    {
        using Stream stream = typeof(AchievementPack).Assembly.GetManifestResourceStream(_resource) ?? throw new InvalidDataException("The achievement pack is missing.");
        using MemoryStream bytes = new();
        stream.CopyTo(bytes);
        byte[] payload = bytes.ToArray();
        Binding = SHA256.HashData(payload);
        SealedAchievement[] nodes = JsonSerializer.Deserialize<SealedAchievement[]>(payload) ?? throw new InvalidDataException("The achievement pack is invalid.");
        _nodes = nodes.ToDictionary(node => node.Id);
        if (_nodes.Count == 0 || _nodes.Values.Any(node => node.Gates.Any(gate => gate.Parents.Length == 0 || gate.Parents.Any(parent => !_nodes.ContainsKey(parent)))))
            throw new InvalidDataException("The achievement pack has invalid prerequisite identities.");

        HashSet<Guid> reachable = [];
        while (true)
        {
            Guid[] next = [.. _nodes.Values.Where(node => !reachable.Contains(node.Id) && Eligible(node, reachable)).Select(node => node.Id)];
            if (next.Length == 0)
                break;

            reachable.UnionWith(next);
        }

        if (reachable.Count != _nodes.Count)
            throw new InvalidDataException("The achievement pack has unreachable activation gates.");
    }

    /// <summary>
    /// Gets the exact pack binding authenticated with the private checkpoint.
    /// </summary>
    public byte[] Binding { get; }

    /// <summary>
    /// Gets the opaque nodes without revealing their definitions.
    /// </summary>
    public IEnumerable<SealedAchievement> Nodes
        => _nodes.Values;

    /// <summary>
    /// Tests whether this process already opened an eligible definition.
    /// </summary>
    /// <param name="id">The stable node identity.</param>
    /// <returns>True only for definitions already admitted to the active cache.</returns>
    public bool IsOpen(Guid id)
        => _opened.ContainsKey(id);

    /// <summary>
    /// Tests the approved AND/OR activation groups against permanent earned identities.
    /// </summary>
    /// <param name="node">The sealed node whose gates are checked.</param>
    /// <param name="earned">Post-epoch earned identities.</param>
    /// <returns>True when every activation group is satisfied.</returns>
    public static bool Eligible(SealedAchievement node, IReadOnlySet<Guid> earned)
        => node.Gates.All(gate => gate.Any ? gate.Parents.Any(earned.Contains) : gate.Parents.All(earned.Contains));

    /// <summary>
    /// Opens one definition only after the caller has established its activation eligibility.
    /// </summary>
    /// <param name="id">The eligible or already-earned identity.</param>
    /// <param name="earned">The authenticated earned ledger used to enforce activation gates.</param>
    /// <returns>The internally active definition.</returns>
    public AchievementRule Open(Guid id, IReadOnlySet<Guid> earned)
    {
        if (!earned.Contains(id) && !Eligible(_nodes[id], earned))
            throw new InvalidOperationException("A dormant achievement definition cannot be opened.");

        if (_opened.TryGetValue(id, out AchievementRule? cached))
            return cached;

        SealedAchievement node = _nodes[id];
        byte[] plaintext = new byte[node.Ciphertext.Length];
        using AesGcm cipher = new(NodeKey(id), 16);
        cipher.Decrypt(node.Nonce, node.Ciphertext, node.Tag, plaintext, id.ToByteArray());
        try
        {
            AchievementRule rule = JsonSerializer.Deserialize<AchievementRule>(plaintext) ?? throw new InvalidDataException("An active achievement definition is invalid.");
            _opened[id] = rule;
            return rule;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Derives a profile-and-epoch-bound private checkpoint key without writing plaintext progress.
    /// </summary>
    /// <param name="profile">The exact sixteen-byte collection profile identity.</param>
    /// <param name="epoch">The exact sixteen-byte release-onward collection epoch.</param>
    /// <returns>A thirty-two-byte AES key for this profile and epoch.</returns>
    public static byte[] StateKey(byte[] profile, byte[] epoch)
    {
        byte[] identity = [.. profile, .. epoch];
        return HMACSHA256.HashData(_root, identity);
    }

    /// <summary>
    /// Derives independent node keys; this is a public-client spoiler barrier, not anti-cheat protection.
    /// </summary>
    /// <param name="id">The stable node identity.</param>
    /// <returns>The independent thirty-two-byte node key.</returns>
    private static byte[] NodeKey(Guid id)
        => HMACSHA256.HashData(_root, id.ToByteArray());
}
