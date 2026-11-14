using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.WebAudio.Rendering;

/// <summary>
/// The vectorized loops of the rendering. The element-wise operations round as the scalar expressions do (the
/// multiplications and additions are not fused), and the destination may be one of the sources.
/// </summary>
internal static class AudioVectors
{
    /// <summary>Computes <c>destination[i] = x[i] * y</c>.</summary>
    public static void Multiply(ReadOnlySpan<float> x, float y, Span<float> destination)
    {
        var length = x.Length;
        destination = destination[..length];
        ref var source = ref unsafe(MemoryMarshal.GetReference(x));
        ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
        var factor = new Vector<float>(y);
        var i = 0;
        for (; i <= length - Vector<float>.Count; i += Vector<float>.Count)
        {
            var value = unsafe(Vector.LoadUnsafe(ref source, (nuint)i)) * factor;
            unsafe { value.StoreUnsafe(ref target, (nuint)i); }
        }

        for (; i < length; i++)
        {
            destination[i] = x[i] * y;
        }
    }

    /// <summary>Computes <c>destination[i] = x[i] * y[i]</c>.</summary>
    public static void Multiply(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination)
    {
        var length = x.Length;
        y = y[..length];
        destination = destination[..length];
        ref var left = ref unsafe(MemoryMarshal.GetReference(x));
        ref var right = ref unsafe(MemoryMarshal.GetReference(y));
        ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
        var i = 0;
        for (; i <= length - Vector<float>.Count; i += Vector<float>.Count)
        {
            var value = unsafe(Vector.LoadUnsafe(ref left, (nuint)i)) * unsafe(Vector.LoadUnsafe(ref right, (nuint)i));
            unsafe { value.StoreUnsafe(ref target, (nuint)i); }
        }

        for (; i < length; i++)
        {
            destination[i] = x[i] * y[i];
        }
    }

    /// <summary>Computes <c>destination[i] = x[i] + y[i]</c>.</summary>
    public static void Add(ReadOnlySpan<float> x, ReadOnlySpan<float> y, Span<float> destination)
    {
        var length = x.Length;
        y = y[..length];
        destination = destination[..length];
        ref var left = ref unsafe(MemoryMarshal.GetReference(x));
        ref var right = ref unsafe(MemoryMarshal.GetReference(y));
        ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
        var i = 0;
        for (; i <= length - Vector<float>.Count; i += Vector<float>.Count)
        {
            var value = unsafe(Vector.LoadUnsafe(ref left, (nuint)i)) + unsafe(Vector.LoadUnsafe(ref right, (nuint)i));
            unsafe { value.StoreUnsafe(ref target, (nuint)i); }
        }

        for (; i < length; i++)
        {
            destination[i] = x[i] + y[i];
        }
    }

    /// <summary>Computes <c>destination[i] = (x[i] * y) + addend[i]</c>, with the product rounded before the addition.</summary>
    public static void MultiplyAdd(ReadOnlySpan<float> x, float y, ReadOnlySpan<float> addend, Span<float> destination)
    {
        var length = x.Length;
        addend = addend[..length];
        destination = destination[..length];
        ref var source = ref unsafe(MemoryMarshal.GetReference(x));
        ref var add = ref unsafe(MemoryMarshal.GetReference(addend));
        ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
        var factor = new Vector<float>(y);
        var i = 0;
        for (; i <= length - Vector<float>.Count; i += Vector<float>.Count)
        {
            var value = (unsafe(Vector.LoadUnsafe(ref source, (nuint)i)) * factor) + unsafe(Vector.LoadUnsafe(ref add, (nuint)i));
            unsafe { value.StoreUnsafe(ref target, (nuint)i); }
        }

        for (; i < length; i++)
        {
            destination[i] = (x[i] * y) + addend[i];
        }
    }

    /// <summary>Computes the sum of <c>x[i] * y[i]</c> (the additions are not in the order of the indices).</summary>
    public static double Dot(ReadOnlySpan<double> x, ReadOnlySpan<double> y)
    {
        var length = x.Length;
        y = y[..length];
        ref var left = ref unsafe(MemoryMarshal.GetReference(x));
        ref var right = ref unsafe(MemoryMarshal.GetReference(y));
        // Four independent sums: one chain of dependent additions would be bound by the latency of the addition
        var count = Vector<double>.Count;
        var sum0 = Vector<double>.Zero;
        var sum1 = Vector<double>.Zero;
        var sum2 = Vector<double>.Zero;
        var sum3 = Vector<double>.Zero;
        var i = 0;
        for (; i <= length - (4 * count); i += 4 * count)
        {
            sum0 += unsafe(Vector.LoadUnsafe(ref left, (nuint)i)) * unsafe(Vector.LoadUnsafe(ref right, (nuint)i));
            sum1 += unsafe(Vector.LoadUnsafe(ref left, (nuint)(i + count))) * unsafe(Vector.LoadUnsafe(ref right, (nuint)(i + count)));
            sum2 += unsafe(Vector.LoadUnsafe(ref left, (nuint)(i + (2 * count)))) * unsafe(Vector.LoadUnsafe(ref right, (nuint)(i + (2 * count))));
            sum3 += unsafe(Vector.LoadUnsafe(ref left, (nuint)(i + (3 * count)))) * unsafe(Vector.LoadUnsafe(ref right, (nuint)(i + (3 * count))));
        }

        for (; i <= length - count; i += count)
        {
            sum0 += unsafe(Vector.LoadUnsafe(ref left, (nuint)i)) * unsafe(Vector.LoadUnsafe(ref right, (nuint)i));
        }

        var sum = Vector.Sum((sum0 + sum1) + (sum2 + sum3));
        for (; i < length; i++)
        {
            sum += x[i] * y[i];
        }

        return sum;
    }
}
