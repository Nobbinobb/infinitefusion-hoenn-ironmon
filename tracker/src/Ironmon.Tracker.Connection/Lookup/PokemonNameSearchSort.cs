using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ironmon.Tracker.Connection.Lookup;

/// <summary>
/// Uses the verified bundled peer's MSVCRT sort to reproduce query-dependent equal-name ordering.
/// </summary>
/// <remarks>
/// Matching indices must enter the sort in original game pool order. The sort is deliberately
/// unstable for equal keys so its result agrees with the supported Ruby runtime.
/// </remarks>
internal static partial class PokemonNameSearchSort
{
    private const string RuntimeLibrary = "msvcrt.dll";
    private const string SortEntryPoint = "qsort_s";

    /// <summary>
    /// Sorts matching indices in place with the CRT routine used by the supported Ruby peer.
    /// </summary>
    /// <param name="indices">The matching indices in original game pool order.</param>
    /// <param name="comparison">The comparison of name-prefix priority followed by normalized name order.</param>
    /// <exception cref="DllNotFoundException">Thrown when the native CRT library is unavailable and sorting is required.</exception>
    /// <exception cref="EntryPointNotFoundException">Thrown when the native sort entry point is unavailable and sorting is required.</exception>
    internal static void Sort(int[] indices, Comparison<int> comparison)
    {
        if (indices.Length < 2)
            return;

        IntPtr memory = Marshal.AllocHGlobal(checked(indices.Length * sizeof(int)));
        try
        {
            Marshal.Copy(indices, 0, memory, indices.Length);
            NativeComparison comparator = (_context, left, right) => comparison(Marshal.ReadInt32(left), Marshal.ReadInt32(right));
            SortNative(memory, (nuint)indices.Length, sizeof(int), comparator, IntPtr.Zero);
            Marshal.Copy(memory, indices, 0, indices.Length);
            GC.KeepAlive(comparator);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    /// <summary>
    /// Defines the MSVCRT callback that compares two stored matching indices.
    /// </summary>
    /// <param name="context">The context pointer supplied to the native sort.</param>
    /// <param name="left">The pointer to the first stored index.</param>
    /// <param name="right">The pointer to the second stored index.</param>
    /// <returns>A negative value, zero, or a positive value when the first index precedes, equals, or follows the second.</returns>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeComparison(IntPtr context, IntPtr left, IntPtr right);

    /// <summary>
    /// Invokes the imported CRT sort over an unmanaged array of matching indices.
    /// </summary>
    /// <param name="memory">The address of the index array to reorder in place.</param>
    /// <param name="count">The number of array elements.</param>
    /// <param name="size">The size of each array element in bytes.</param>
    /// <param name="comparison">The callback that compares two stored indices.</param>
    /// <param name="context">The context pointer passed unchanged to each comparison callback.</param>
    [LibraryImport(RuntimeLibrary, EntryPoint = SortEntryPoint)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void SortNative(IntPtr memory, nuint count, nuint size, NativeComparison comparison, IntPtr context);
}
