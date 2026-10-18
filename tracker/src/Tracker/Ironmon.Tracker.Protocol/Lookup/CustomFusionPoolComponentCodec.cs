using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Identifies the normal-species materials of one eligible custom fusion.
/// </summary>
public sealed class CustomFusionPair
{
    /// <summary>
    /// Initializes a custom-fusion material pair.
    /// </summary>
    /// <param name="bodyId">The one-based body species identifier.</param>
    /// <param name="headId">The one-based head species identifier.</param>
    public CustomFusionPair(int bodyId, int headId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bodyId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(headId);
        BodyId = bodyId;
        HeadId = headId;
    }

    /// <summary>
    /// Gets the one-based body species identifier.
    /// </summary>
    public int BodyId { get; }

    /// <summary>
    /// Gets the one-based head species identifier.
    /// </summary>
    public int HeadId { get; }
}

/// <summary>
/// Provides validated access to one decoded custom-fusion eligibility component.
/// </summary>
public sealed class CustomFusionPoolComponent
{
    private readonly byte[] _bitset;

    /// <summary>
    /// Initializes a decoded custom-fusion eligibility component.
    /// </summary>
    /// <param name="schemaVersion">The component storage schema.</param>
    /// <param name="normalSpeciesCount">The number of one-based normal species represented by each axis.</param>
    /// <param name="eligibleCount">The number of set eligibility bits.</param>
    /// <param name="bitset">The row-major eligibility bitset.</param>
    internal CustomFusionPoolComponent(int schemaVersion, int normalSpeciesCount, int eligibleCount, byte[] bitset)
    {
        SchemaVersion = schemaVersion;
        NormalSpeciesCount = normalSpeciesCount;
        EligibleCount = eligibleCount;
        _bitset = bitset;
    }

    /// <summary>
    /// Gets the component storage schema.
    /// </summary>
    public int SchemaVersion { get; }

    /// <summary>
    /// Gets the number of one-based normal species represented by each axis.
    /// </summary>
    public int NormalSpeciesCount { get; }

    /// <summary>
    /// Gets the number of eligible custom fusions.
    /// </summary>
    public int EligibleCount { get; }

    /// <summary>
    /// Determines whether the ordered material pair has an eligible custom fusion.
    /// </summary>
    /// <param name="bodyId">The one-based body species identifier.</param>
    /// <param name="headId">The one-based head species identifier.</param>
    /// <returns>Whether the pair is eligible.</returns>
    public bool Contains(int bodyId, int headId)
    {
        ValidateMaterialId(bodyId, nameof(bodyId));
        ValidateMaterialId(headId, nameof(headId));
        int index = CustomFusionPoolComponentCodec.GetBitIndex(bodyId, headId, NormalSpeciesCount);
        return (_bitset[index >> 3] & (1 << (index & 7))) != 0;
    }

    /// <summary>
    /// Enumerates eligible material pairs in deterministic body-major, head-minor order.
    /// </summary>
    /// <returns>The ordered eligible material pairs.</returns>
    public IReadOnlyList<CustomFusionPair> Enumerate()
    {
        List<CustomFusionPair> result = new(EligibleCount);
        for (int bodyId = 1; bodyId <= NormalSpeciesCount; bodyId++)
        {
            for (int headId = 1; headId <= NormalSpeciesCount; headId++)
            {
                if (Contains(bodyId, headId))
                    result.Add(new CustomFusionPair(bodyId, headId));
            }
        }

        return result;
    }

    /// <summary>
    /// Validates one material identifier against this component's normal-species axis.
    /// </summary>
    /// <param name="materialId">The identifier to validate.</param>
    /// <param name="parameterName">The public parameter name used by an exception.</param>
    private void ValidateMaterialId(int materialId, string parameterName)
    {
        if (materialId < 1 || materialId > NormalSpeciesCount)
            throw new ArgumentOutOfRangeException(parameterName, materialId, "The custom-fusion material is outside the normal species catalog.");
    }
}

/// <summary>
/// Encodes and validates the compact custom-fusion eligibility component format.
/// </summary>
public static class CustomFusionPoolComponentCodec
{
    private const string MagicValue = "IFCFPOOL";
    private const int HeaderBytes = 20;
    private static readonly byte[] _magic = Encoding.ASCII.GetBytes(MagicValue);

    /// <summary>
    /// Gets the only custom-fusion component schema currently supported.
    /// </summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Encodes eligible custom fusions into the canonical packed component format.
    /// </summary>
    /// <param name="normalSpeciesCount">The number of one-based normal species represented by each axis.</param>
    /// <param name="pairs">The eligible ordered material pairs.</param>
    /// <returns>The canonical component bytes.</returns>
    public static byte[] Encode(int normalSpeciesCount, IEnumerable<CustomFusionPair> pairs)
    {
        ValidateSpeciesCount(normalSpeciesCount);
        ArgumentNullException.ThrowIfNull(pairs);
        int bitsetLength = checked(((normalSpeciesCount * normalSpeciesCount) + 7) / 8);
        byte[] bitset = new byte[bitsetLength];
        int eligibleCount = 0;
        foreach (CustomFusionPair pair in pairs)
        {
            ArgumentNullException.ThrowIfNull(pair);
            if (pair.BodyId > normalSpeciesCount || pair.HeadId > normalSpeciesCount)
                throw new ArgumentOutOfRangeException(nameof(pairs), "A custom-fusion material is outside the normal species catalog.");

            int index = GetBitIndex(pair.BodyId, pair.HeadId, normalSpeciesCount);
            byte mask = (byte)(1 << (index & 7));
            if ((bitset[index >> 3] & mask) != 0)
                throw new ArgumentException("Custom-fusion material pairs must be unique.", nameof(pairs));

            bitset[index >> 3] |= mask;
            eligibleCount++;
        }

        if ((eligibleCount & 1) != 0)
            throw new ArgumentException("The custom-fusion eligibility count must be even.", nameof(pairs));

        byte[] result = new byte[HeaderBytes + bitsetLength];
        _magic.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8, 2), SchemaVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10, 2), checked((ushort)normalSpeciesCount));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12, 4), checked((uint)eligibleCount));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16, 4), checked((uint)bitsetLength));
        bitset.CopyTo(result, HeaderBytes);
        return result;
    }

    /// <summary>
    /// Decodes and validates an exact packed custom-fusion eligibility component.
    /// </summary>
    /// <param name="bytes">The component bytes.</param>
    /// <returns>The validated component.</returns>
    public static CustomFusionPoolComponent Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes)
            throw new ArgumentException("The custom-fusion component header is truncated.", nameof(bytes));

        if (!bytes[..8].SequenceEqual(_magic))
            throw new ArgumentException("The custom-fusion component magic is invalid.", nameof(bytes));

        int schemaVersion = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8, 2));
        if (schemaVersion != SchemaVersion)
            throw new ArgumentException($"Unsupported custom-fusion component schema {schemaVersion}.", nameof(bytes));

        int normalSpeciesCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(10, 2));
        ValidateSpeciesCount(normalSpeciesCount);
        int eligibleCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(12, 4)));
        int bitsetLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(16, 4)));
        int expectedBitsetLength = checked(((normalSpeciesCount * normalSpeciesCount) + 7) / 8);
        if (bitsetLength != expectedBitsetLength || bytes.Length != HeaderBytes + bitsetLength)
            throw new ArgumentException("The custom-fusion component length is invalid.", nameof(bytes));

        if ((eligibleCount & 1) != 0)
            throw new ArgumentException("The custom-fusion eligibility count must be even.", nameof(bytes));

        byte[] bitset = bytes[HeaderBytes..].ToArray();
        int usedBits = checked(normalSpeciesCount * normalSpeciesCount);
        int remainder = usedBits & 7;
        if (remainder != 0 && (bitset[^1] & (byte)(byte.MaxValue << remainder)) != 0)
            throw new ArgumentException("Custom-fusion component padding bits must be zero.", nameof(bytes));

        int actualCount = bitset.Sum(value => BitOperations.PopCount(value));
        if (actualCount != eligibleCount)
            throw new ArgumentException("The custom-fusion eligibility count does not match its bitset.", nameof(bytes));

        return new CustomFusionPoolComponent(schemaVersion, normalSpeciesCount, eligibleCount, bitset);
    }

    /// <summary>
    /// Calculates the row-major zero-based bit index for an ordered material pair.
    /// </summary>
    /// <param name="bodyId">The one-based body species identifier.</param>
    /// <param name="headId">The one-based head species identifier.</param>
    /// <param name="normalSpeciesCount">The number of normal species represented by each axis.</param>
    /// <returns>The zero-based bit index.</returns>
    internal static int GetBitIndex(int bodyId, int headId, int normalSpeciesCount)
    {
        return checked(((bodyId - 1) * normalSpeciesCount) + headId - 1);
    }

    /// <summary>
    /// Validates the normal-species axis against the binary format.
    /// </summary>
    /// <param name="normalSpeciesCount">The axis size to validate.</param>
    private static void ValidateSpeciesCount(int normalSpeciesCount)
    {
        if (normalSpeciesCount is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(normalSpeciesCount), normalSpeciesCount, "The normal species count is outside the custom-fusion component format.");
    }
}
