using System.Globalization;
using System.Text;

namespace Ironmon.Tracker.Connection.Obtainability;

/// <summary>
/// Reproduces the versioned Ruby fusion formula without changing normal stat rolls.
/// </summary>
internal static class FusionBaseStats
{
    private const string _inheritanceNamespace = "analysis_fusion_inheritance";
    private static readonly int[][] _groups = [[1, 2, 5], [0, 3, 4]];
    private static readonly int[][] _orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
    private static readonly (int Numerator, int Denominator)[] _versionTwoRatios = [(9, 10), (4, 5), (2, 3)];
    private static readonly (int Numerator, int Denominator)[] _versionThreeRatios = [(17, 20), (3, 4), (2, 3)];

    /// <summary>
    /// Calculates the generated BST for one oriented fusion and run seed.
    /// </summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="fusionId">The numeric fusion species identifier.</param>
    /// <param name="body">The body's six randomized base stats in canonical order.</param>
    /// <param name="head">The head's six randomized base stats in canonical order.</param>
    /// <param name="version">One for historical blending, two for 90/80/two-thirds inheritance, or three for 85/75/two-thirds inheritance.</param>
    /// <returns>The sum of the six separately rounded inherited base stats.</returns>
    internal static int Total(long seed, int fusionId, IReadOnlyList<int> body, IReadOnlyList<int> head, int version)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(version, 3);
        (int Numerator, int Denominator)[] ratios = version == 3 ? _versionThreeRatios : _versionTwoRatios;
        int total = 0;
        foreach (int[] group in _groups)
        {
            int[] order = _orders[version == 1 ? 0 : (int)(InheritanceHash(seed, fusionId, group[0]) % 6)];
            for (int index = 0; index < 3; index++)
            {
                int stat = group[order[index]];
                bool headDominant = stat is 0 or 3 or 4;
                int dominant = headDominant ? head[stat] : body[stat];
                int other = headDominant ? body[stat] : head[stat];
                (int numerator, int denominator) = version == 1 ? (2, 3) : ratios[index];
                total += numerator * dominant / denominator + (denominator - numerator) * other / denominator;
            }
        }

        return total;
    }

    /// <summary>
    /// Reproduces the joined FNV seed followed by the unsigned 64-bit avalanche.
    /// </summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="fusionId">The numeric fusion identifier.</param>
    /// <param name="group">The canonical first stat index of the inheritance group.</param>
    /// <returns>The stable unsigned choice hash.</returns>
    private static ulong InheritanceHash(long seed, int fusionId, int group)
    {
        string identity = string.Join('|', 1, seed.ToString(CultureInfo.InvariantCulture), _inheritanceNamespace, fusionId.ToString(CultureInfo.InvariantCulture), group);
        ulong value = 14_695_981_039_346_656_037;
        foreach (byte part in Encoding.UTF8.GetBytes(identity))
            value = unchecked((value ^ part) * 1_099_511_628_211);

        value = unchecked((value ^ (value >> 30)) * 0xbf58476d1ce4e5b9);
        value = unchecked((value ^ (value >> 27)) * 0x94d049bb133111eb);
        return value ^ (value >> 31);
    }
}
