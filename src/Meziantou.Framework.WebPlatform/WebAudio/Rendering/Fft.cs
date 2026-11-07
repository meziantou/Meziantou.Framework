using System.Collections.Concurrent;
using System.Numerics;

namespace Meziantou.Framework.WebPlatform.WebAudio.Rendering;

/// <summary>
/// A radix-2 complex fast Fourier transform (iterative Cooley-Tukey), in double precision: the forward transform computes
/// <c>X[k] = sum x[n] e^(-2πikn/N)</c>, and the inverse transform <c>x[n] = sum X[k] e^(2πikn/N)</c> (not scaled by 1/N).
/// </summary>
internal sealed class Fft
{
    private static readonly ConcurrentDictionary<int, Fft> Instances = new();

    private readonly int[] _bitReversed;
    private readonly double[] _cos;
    private readonly double[] _sin;

    private Fft(int size)
    {
        Size = size;
        var bits = BitOperations.Log2((uint)size);
        _bitReversed = new int[size];
        for (var i = 0; i < size; i++)
        {
            var reversed = 0;
            var value = i;
            for (var b = 0; b < bits; b++)
            {
                reversed = (reversed << 1) | (value & 1);
                value >>= 1;
            }

            _bitReversed[i] = reversed;
        }

        _cos = new double[size / 2];
        _sin = new double[size / 2];
        for (var i = 0; i < size / 2; i++)
        {
            var angle = 2 * Math.PI * i / size;
            _cos[i] = Math.Cos(angle);
            _sin[i] = Math.Sin(angle);
        }
    }

    /// <summary>The number of points of the transform (a power of two).</summary>
    public int Size { get; }

    /// <summary>Gets the transform of a size (a power of two, at least 2).</summary>
    public static Fft Get(int size)
    {
        if (size < 2 || !BitOperations.IsPow2(size))
            throw new ArgumentOutOfRangeException(nameof(size), size, "The size of the FFT must be a power of two");

        return Instances.GetOrAdd(size, static size => new Fft(size));
    }

    /// <summary>Transforms the complex values in place.</summary>
    public void Transform(Span<double> real, Span<double> imaginary, bool inverse)
    {
        var n = Size;
        for (var i = 0; i < n; i++)
        {
            var j = _bitReversed[i];
            if (j > i)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        var sign = inverse ? 1.0 : -1.0;
        for (var length = 2; length <= n; length <<= 1)
        {
            var half = length >> 1;
            var step = n / length;
            for (var start = 0; start < n; start += length)
            {
                for (var k = 0; k < half; k++)
                {
                    var wr = _cos[k * step];
                    var wi = sign * _sin[k * step];
                    var a = start + k;
                    var b = a + half;
                    var xr = (real[b] * wr) - (imaginary[b] * wi);
                    var xi = (real[b] * wi) + (imaginary[b] * wr);
                    real[b] = real[a] - xr;
                    imaginary[b] = imaginary[a] - xi;
                    real[a] += xr;
                    imaginary[a] += xi;
                }
            }
        }
    }
}
