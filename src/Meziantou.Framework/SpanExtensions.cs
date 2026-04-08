using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Span{T}"/>, arrays, and lists.
/// </summary>
public static class SpanExtensions
{
    private const int MinParallelLength = 4096;
    private const int MinChunkLength = 1024;

    public static void ParallelSort<T>(this Span<T> span)
    {
        ParallelSort(span, Environment.ProcessorCount, comparer: null);
    }

    public static void ParallelSort<T>(this Span<T> span, IComparer<T>? comparer)
    {
        ParallelSort(span, Environment.ProcessorCount, comparer);
    }

    public static void ParallelSort<T>(this Span<T> span, int degreeOfParallelism)
    {
        ParallelSort(span, degreeOfParallelism, comparer: null);
    }

    public static void ParallelSort<T>(this Span<T> span, int degreeOfParallelism, IComparer<T>? comparer)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(degreeOfParallelism, 1);

        if (span.Length <= 1)
            return;

        comparer ??= Comparer<T>.Default;

        if (span.Length < MinParallelLength || degreeOfParallelism is 1)
        {
            span.Sort(comparer);
            return;
        }

        var values = span.ToArray();
        var pooledBuffer = ArrayPool<T>.Shared.Rent(values.Length);
        try
        {
            ParallelSortCore(values, pooledBuffer, degreeOfParallelism, comparer);
            values.AsSpan().CopyTo(span);
        }
        finally
        {
            ArrayPool<T>.Shared.Return(pooledBuffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }

    public static void ParallelStableSort<T>(this Span<T> span)
    {
        ParallelStableSort(span, Environment.ProcessorCount, comparer: null);
    }

    public static void ParallelStableSort<T>(this Span<T> span, IComparer<T>? comparer)
    {
        ParallelStableSort(span, Environment.ProcessorCount, comparer);
    }

    public static void ParallelStableSort<T>(this Span<T> span, int degreeOfParallelism)
    {
        ParallelStableSort(span, degreeOfParallelism, comparer: null);
    }

    public static void ParallelStableSort<T>(this Span<T> span, int degreeOfParallelism, IComparer<T>? comparer)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(degreeOfParallelism, 1);

        if (span.Length <= 1)
            return;

        var useDefaultComparer = comparer is null;
        comparer ??= Comparer<T>.Default;
        if (useDefaultComparer && TypeIsImplicitlyStable<T>())
        {
            ParallelSort(span, degreeOfParallelism, comparer);
            return;
        }

        var values = new StableSortItem<T>[span.Length];
        for (var i = 0; i < span.Length; i++)
        {
            values[i] = new StableSortItem<T>(span[i], i);
        }

        var pooledBuffer = ArrayPool<StableSortItem<T>>.Shared.Rent(values.Length);
        try
        {
            ParallelSortCore(values, pooledBuffer, degreeOfParallelism, new StableSortItemComparer<T>(comparer));

            for (var i = 0; i < values.Length; i++)
            {
                span[i] = values[i].Value;
            }
        }
        finally
        {
            ArrayPool<StableSortItem<T>>.Shared.Return(pooledBuffer, RuntimeHelpers.IsReferenceOrContainsReferences<StableSortItem<T>>());
        }
    }

    public static void ParallelSort<T>(this T[] array)
    {
        ArgumentNullException.ThrowIfNull(array);

        ParallelSort(array.AsSpan(), Environment.ProcessorCount, comparer: null);
    }

    public static void ParallelSort<T>(this T[] array, IComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(array);

        ParallelSort(array.AsSpan(), Environment.ProcessorCount, comparer);
    }

    public static void ParallelSort<T>(this T[] array, int degreeOfParallelism)
    {
        ArgumentNullException.ThrowIfNull(array);

        ParallelSort(array.AsSpan(), degreeOfParallelism, comparer: null);
    }

    public static void ParallelSort<T>(this T[] array, int degreeOfParallelism, IComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(array);

        ParallelSort(array.AsSpan(), degreeOfParallelism, comparer);
    }

    public static void ParallelStableSort<T>(this T[] array)
    {
        ArgumentNullException.ThrowIfNull(array);

        ParallelStableSort(array.AsSpan(), Environment.ProcessorCount, comparer: null);
    }

    public static void ParallelStableSort<T>(this T[] array, IComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(array);

        ParallelStableSort(array.AsSpan(), Environment.ProcessorCount, comparer);
    }

    public static void ParallelStableSort<T>(this T[] array, int degreeOfParallelism)
    {
        ArgumentNullException.ThrowIfNull(array);

        ParallelStableSort(array.AsSpan(), degreeOfParallelism, comparer: null);
    }

    public static void ParallelStableSort<T>(this T[] array, int degreeOfParallelism, IComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(array);

        ParallelStableSort(array.AsSpan(), degreeOfParallelism, comparer);
    }

    public static void ParallelSort<T>(this List<T> list)
    {
        ArgumentNullException.ThrowIfNull(list);

        ParallelSort(CollectionsMarshal.AsSpan(list), Environment.ProcessorCount, comparer: null);
    }

    public static void ParallelSort<T>(this List<T> list, IComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(list);

        ParallelSort(CollectionsMarshal.AsSpan(list), Environment.ProcessorCount, comparer);
    }

    public static void ParallelSort<T>(this List<T> list, int degreeOfParallelism)
    {
        ArgumentNullException.ThrowIfNull(list);

        ParallelSort(CollectionsMarshal.AsSpan(list), degreeOfParallelism, comparer: null);
    }

    public static void ParallelSort<T>(this List<T> list, int degreeOfParallelism, IComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(list);

        ParallelSort(CollectionsMarshal.AsSpan(list), degreeOfParallelism, comparer);
    }

    public static void ParallelStableSort<T>(this List<T> list)
    {
        ArgumentNullException.ThrowIfNull(list);

        ParallelStableSort(CollectionsMarshal.AsSpan(list), Environment.ProcessorCount, comparer: null);
    }

    public static void ParallelStableSort<T>(this List<T> list, IComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(list);

        ParallelStableSort(CollectionsMarshal.AsSpan(list), Environment.ProcessorCount, comparer);
    }

    public static void ParallelStableSort<T>(this List<T> list, int degreeOfParallelism)
    {
        ArgumentNullException.ThrowIfNull(list);

        ParallelStableSort(CollectionsMarshal.AsSpan(list), degreeOfParallelism, comparer: null);
    }

    public static void ParallelStableSort<T>(this List<T> list, int degreeOfParallelism, IComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(list);

        ParallelStableSort(CollectionsMarshal.AsSpan(list), degreeOfParallelism, comparer);
    }

    private static void ParallelSortCore<T>(T[] values, T[] temporaryBuffer, int degreeOfParallelism, IComparer<T> comparer)
    {
        var length = values.Length;
        var effectiveDegreeOfParallelism = Math.Min(Math.Max(1, degreeOfParallelism), length);
        var chunkLength = Math.Max(MinChunkLength, (length + effectiveDegreeOfParallelism - 1) / effectiveDegreeOfParallelism);
        var chunkCount = (length + chunkLength - 1) / chunkLength;

        if (chunkCount is 1)
        {
            Array.Sort(values, 0, length, comparer);
            return;
        }

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = effectiveDegreeOfParallelism,
        };

        Parallel.For(0, chunkCount, options, chunkIndex =>
        {
            var start = chunkIndex * chunkLength;
            var count = Math.Min(chunkLength, length - start);
            Array.Sort(values, start, count, comparer);
        });

        var source = values;
        var destination = temporaryBuffer;
        var runLength = chunkLength;

        while (runLength < length)
        {
            var stride = runLength * 2L;
            var mergeCount = (int)((length + stride - 1) / stride);
            Parallel.For(0, mergeCount, options, mergeIndex =>
            {
                var start = (int)(mergeIndex * stride);
                var middle = Math.Min(start + runLength, length);
                var end = (int)Math.Min(start + stride, length);
                Merge(source, destination, start, middle, end, comparer);
            });

            (source, destination) = (destination, source);
            runLength = stride >= length ? length : (int)stride;
        }

        if (!ReferenceEquals(source, values))
        {
            Array.Copy(source, 0, values, 0, length);
        }
    }

    private static void Merge<T>(T[] source, T[] destination, int start, int middle, int end, IComparer<T> comparer)
    {
        if (middle >= end)
        {
            Array.Copy(source, start, destination, start, end - start);
            return;
        }

        var leftIndex = start;
        var rightIndex = middle;
        var destinationIndex = start;

        while (leftIndex < middle && rightIndex < end)
        {
            if (comparer.Compare(source[leftIndex], source[rightIndex]) <= 0)
            {
                destination[destinationIndex] = source[leftIndex];
                leftIndex++;
            }
            else
            {
                destination[destinationIndex] = source[rightIndex];
                rightIndex++;
            }

            destinationIndex++;
        }

        if (leftIndex < middle)
        {
            Array.Copy(source, leftIndex, destination, destinationIndex, middle - leftIndex);
        }
        else if (rightIndex < end)
        {
            Array.Copy(source, rightIndex, destination, destinationIndex, end - rightIndex);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TypeIsImplicitlyStable<T>()
    {
        var type = typeof(T);
        if (type.IsEnum)
        {
            type = type.GetEnumUnderlyingType();
        }

        return
            type == typeof(sbyte) || type == typeof(byte) || type == typeof(bool) ||
            type == typeof(short) || type == typeof(ushort) || type == typeof(char) ||
            type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong) ||
            type == typeof(Int128) || type == typeof(UInt128) ||
            type == typeof(nint) || type == typeof(nuint);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct StableSortItem<T>(T value, int index)
    {
        public T Value { get; } = value;
        public int Index { get; } = index;
    }

    private sealed class StableSortItemComparer<T>(IComparer<T> comparer) : IComparer<StableSortItem<T>>
    {
        public int Compare(StableSortItem<T> x, StableSortItem<T> y)
        {
            var result = comparer.Compare(x.Value, y.Value);
            if (result is not 0)
                return result;

            return x.Index.CompareTo(y.Index);
        }
    }
}
