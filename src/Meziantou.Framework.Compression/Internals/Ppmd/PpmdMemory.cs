using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>
/// The memory of a PPMd model: one byte array that holds the text area and the 12-byte units of the sub-allocator, addressed
/// by 32-bit offsets (0 is the null reference). Every access checks that the structure lies inside the array, so an
/// inconsistent model reports an <see cref="InvalidDataException"/> instead of reading out of bounds.
/// </summary>
internal static class PpmdMemory
{
    public const int UnitSize = 12;
    public const int NumIndexes = 38;

    /// <summary>The number of units of each block size index (Indx2Units).</summary>
    public static readonly byte[] IndexToUnits = CreateIndexToUnits();

    /// <summary>The block size index of each number of units minus one, from 1 to 128 units (Units2Indx).</summary>
    public static readonly byte[] UnitsToIndex = CreateUnitsToIndex();

    /// <summary>Allocates the memory of a model: <paramref name="alignOffset"/> + <paramref name="size"/> + one spare unit.</summary>
    public static byte[] Allocate(uint size, out uint alignOffset)
    {
        alignOffset = 4 - (size & 3);
        var length = (ulong)alignOffset + size + UnitSize;
        if (length > (ulong)Array.MaxLength)
            throw new InvalidDataException($"The PPMd memory size ({size} bytes) is not supported.");

        return new byte[length];
    }

    /// <summary>
    /// Returns a reference to the <typeparamref name="T"/> at <paramref name="offset"/>, after checking that all its bytes lie
    /// inside <paramref name="memory"/>: the reference never escapes the array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T At<T>(byte[] memory, uint offset)
        where T : unmanaged
    {
        if (offset > (uint)memory.Length - (uint)Unsafe.SizeOf<T>())
            ThrowCorrupted();

        unsafe
        {
            return ref Unsafe.As<byte, T>(ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(memory), (nint)offset));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SwapStates(ref PpmdState s1, ref PpmdState s2) => (s1, s2) = (s2, s1);

    /// <summary>Copies <paramref name="units"/> units (MyMem12Cpy).</summary>
    public static void CopyUnits(byte[] memory, uint destination, uint source, uint units)
    {
        var length = units * UnitSize;
        if ((ulong)destination + length > (uint)memory.Length || (ulong)source + length > (uint)memory.Length)
            ThrowCorrupted();

        memory.AsSpan((int)source, (int)length).CopyTo(memory.AsSpan((int)destination, (int)length));
    }

    [DoesNotReturn]
    public static void ThrowCorrupted() => throw new InvalidDataException("The PPMd data is corrupted.");

    private static byte[] CreateIndexToUnits()
    {
        var result = new byte[NumIndexes];
        for (int i = 0, k = 0; i < NumIndexes; i++)
        {
            var step = i >= 12 ? 4 : (i >> 2) + 1;
            k += step;
            result[i] = (byte)k;
        }

        return result;
    }

    private static byte[] CreateUnitsToIndex()
    {
        var result = new byte[128];
        for (int i = 0, k = 0; i < NumIndexes; i++)
        {
            var step = i >= 12 ? 4 : (i >> 2) + 1;
            do
            {
                result[k++] = (byte)i;
            }
            while (--step != 0);
        }

        return result;
    }
}
