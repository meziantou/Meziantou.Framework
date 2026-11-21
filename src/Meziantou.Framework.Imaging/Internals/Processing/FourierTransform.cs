using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The discrete Fourier transform of a fixed length <c>n</c>, <c>X_k = Σ_j x_j exp(-2πi jk / n)</c>, of complex numbers
/// stored as pairs of doubles (the real part, then the imaginary part), for <see cref="WienerDeconvolver"/>.
/// </summary>
/// <remarks>
/// <para>
/// A length whose prime factors are 2, 3 and 5 only (<see cref="IsSmooth"/>) is transformed by the mixed-radix
/// decimation in time of Cooley and Tukey: the sequence is split into its <c>p</c> interleaved subsequences, transformed
/// recursively out of place, and combined in place by butterflies of radix 4 (taken first), 2, 3 and 5. Any other length
/// is transformed by Bluestein's algorithm: the product <c>jk = (j² + k² - (k - j)²) / 2</c> turns the transform into a
/// convolution with a chirp, computed with transforms of a smooth length at least <c>2 n - 1</c>.
/// </para>
/// <para>
/// The twiddle factors and the chirp come from <see cref="DeterministicAngle"/>, and every operation is a scalar IEEE
/// addition, multiplication or division, without fused multiply-adds: the result is the same on every platform. The
/// tables are rented from the scope of the operation. An instance is used by one thread at a time.
/// </para>
/// </remarks>
internal sealed class FourierTransform : IDisposable
{
    private readonly int[] _factors;
    private readonly int _bluesteinLength;
    private readonly FourierTransform? _inner;
    private PooledBuffer? _buffer;

    /// <summary>Initializes a new instance of the <see cref="FourierTransform"/> class, renting its tables from <paramref name="scope"/>.</summary>
    /// <param name="scope">The scope the tables are charged to.</param>
    /// <param name="length">The length of the transform, positive.</param>
    /// <exception cref="ImageResourceLimitException">The tables exceed a limit.</exception>
    public FourierTransform(AllocationScope scope, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        Length = length;
        if (IsSmooth(length))
        {
            _factors = Factor(length);
            _buffer = ResampleWeights.Rent<double>(scope, 2L * length);
            FillTwiddles(Doubles[..(2 * length)], length);
            return;
        }

        // Bluestein: the chirp c_j = exp(-πi j² / n), and the transform of the conjugate chirp b, both lengths of the
        // convolution (b_j and b_{m - j} are conj(c_j)), divided by m, the scale of the inverse transform
        _factors = [];
        var bluesteinLength = GetSmoothLength(checked((2L * length) - 1));
        if (bluesteinLength > int.MaxValue / 2)
            throw new ImageResourceLimitException(ImageResourceLimitKind.LiveAllocationBytes, scope.Limit, bluesteinLength * 2 * sizeof(double));

        _bluesteinLength = (int)bluesteinLength;
        _inner = new FourierTransform(scope, _bluesteinLength);
        try
        {
            _buffer = ResampleWeights.Rent<double>(scope, (2L * length) + (6L * _bluesteinLength));
            var chirp = Chirp;
            for (var j = 0; j < length; j++)
            {
                var square = (long)j * j % (2L * length);
                DeterministicAngle.SinCos(-(double)square / length, out var sin, out var cos);
                chirp[2 * j] = cos;
                chirp[(2 * j) + 1] = sin;
            }

            var work = BluesteinWork;
            work.Clear();
            for (var j = 0; j < length; j++)
            {
                work[2 * j] = chirp[2 * j];
                work[(2 * j) + 1] = -chirp[(2 * j) + 1];
                if (j > 0)
                {
                    work[2 * (_bluesteinLength - j)] = chirp[2 * j];
                    work[(2 * (_bluesteinLength - j)) + 1] = -chirp[(2 * j) + 1];
                }
            }

            var spectrum = BluesteinSpectrum;
            _inner.Forward(work, spectrum);
            var scale = 1d / _bluesteinLength;
            for (var i = 0; i < spectrum.Length; i++)
            {
                spectrum[i] *= scale;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets the length of the transform.</summary>
    public int Length { get; }

    private Span<double> Doubles => unsafe(MemoryMarshal.Cast<byte, double>((_buffer ?? throw new ObjectDisposedException(nameof(FourierTransform))).Span));

    private Span<double> Twiddles => Doubles[..(2 * Length)];

    private Span<double> Chirp => Doubles[..(2 * Length)];

    private Span<double> BluesteinSpectrum => Doubles.Slice(2 * Length, 2 * _bluesteinLength);

    private Span<double> BluesteinWork => Doubles.Slice((2 * Length) + (2 * _bluesteinLength), 2 * _bluesteinLength);

    private Span<double> BluesteinProduct => Doubles.Slice((2 * Length) + (4 * _bluesteinLength), 2 * _bluesteinLength);

    /// <summary>Gets a value indicating whether the only prime factors of a positive length are 2, 3 and 5.</summary>
    public static bool IsSmooth(long length)
    {
        if (length <= 0)
            return false;

        foreach (var prime in (ReadOnlySpan<int>)[2, 3, 5])
        {
            while (length % prime == 0)
            {
                length /= prime;
            }
        }

        return length == 1;
    }

    /// <summary>Gets the smallest length at least <paramref name="minimum"/> whose only prime factors are 2, 3 and 5.</summary>
    public static long GetSmoothLength(long minimum)
    {
        if (minimum <= 1)
            return 1;

        // Every 2^a 3^b, completed by the smallest power of 5 that reaches the minimum
        var best = long.MaxValue;
        for (long powerOf2 = 1; powerOf2 < best; powerOf2 *= 2)
        {
            for (var powerOf3 = powerOf2; powerOf3 < best; powerOf3 *= 3)
            {
                var candidate = powerOf3;
                while (candidate < minimum)
                {
                    candidate *= 5;
                }

                best = Math.Min(best, candidate);
                if (powerOf3 >= minimum)
                    break;
            }

            if (powerOf2 >= minimum)
                break;
        }

        return best;
    }

    /// <summary>Transforms <see cref="Length"/> complex numbers of <paramref name="input"/> into <paramref name="output"/>, which must not overlap it.</summary>
    public void Forward(ReadOnlySpan<double> input, Span<double> output)
    {
        if (input.Length < 2 * Length || output.Length < 2 * Length)
            throw new ArgumentException("The spans are shorter than the transform.", nameof(input));

        if (Length == 1)
        {
            output[0] = input[0];
            output[1] = input[1];
            return;
        }

        if (_inner is null)
        {
            Transform(input, output, Twiddles, Length, 0, 1, 0, 0, Length);
            return;
        }

        // X_k = c_k Σ_j (x_j c_j) conj(c_{k - j}): the convolution of a = x c with b, by the transforms of length m; its
        // inverse is the conjugate of the transform of the conjugate
        var chirp = Chirp;
        var work = BluesteinWork;
        var product = BluesteinProduct;
        var spectrum = BluesteinSpectrum;
        work.Clear();
        for (var j = 0; j < Length; j++)
        {
            Multiply(input[2 * j], input[(2 * j) + 1], chirp[2 * j], chirp[(2 * j) + 1], out work[2 * j], out work[(2 * j) + 1]);
        }

        _inner.Forward(work, product);
        for (var i = 0; i < _bluesteinLength; i++)
        {
            Multiply(product[2 * i], product[(2 * i) + 1], spectrum[2 * i], spectrum[(2 * i) + 1], out var re, out var im);
            product[2 * i] = re;
            product[(2 * i) + 1] = -im;
        }

        _inner.Forward(product, work);
        for (var k = 0; k < Length; k++)
        {
            Multiply(work[2 * k], -work[(2 * k) + 1], chirp[2 * k], chirp[(2 * k) + 1], out output[2 * k], out output[(2 * k) + 1]);
        }
    }

    public void Dispose()
    {
        _inner?.Dispose();
        _buffer?.Dispose();
        _buffer = null;
    }

    /// <summary>Multiplies two complex numbers.</summary>
    internal static void Multiply(double leftRe, double leftIm, double rightRe, double rightIm, out double re, out double im)
    {
        re = (leftRe * rightRe) - (leftIm * rightIm);
        im = (leftRe * rightIm) + (leftIm * rightRe);
    }

    /// <summary>Gets the factors of a smooth length: as many 4 as possible, then 2, 3 and 5.</summary>
    private static int[] Factor(int length)
    {
        var factors = new List<int>();
        foreach (var radix in (ReadOnlySpan<int>)[4, 2, 3, 5])
        {
            while (length % radix == 0 && length > 1)
            {
                factors.Add(radix);
                length /= radix;
            }
        }

        return [.. factors];
    }

    /// <summary>Sets the twiddle factors <c>exp(-2πi k / n)</c> for <c>k</c> from 0 to <c>n - 1</c>.</summary>
    private static void FillTwiddles(Span<double> twiddles, int length)
    {
        for (var k = 0; k < length; k++)
        {
            DeterministicAngle.SinCos(-2d * k / length, out var sin, out var cos);
            twiddles[2 * k] = cos;
            twiddles[(2 * k) + 1] = sin;
        }
    }

    /// <summary>
    /// Sets <paramref name="count"/> elements of <paramref name="output"/> from <paramref name="outputOffset"/> to the transform
    /// of the elements of <paramref name="input"/> from <paramref name="inputOffset"/>, every <paramref name="stride"/>
    /// elements: a subsequence of the transform of length <paramref name="length"/>, whose twiddle factors are those of
    /// that length, every <paramref name="stride"/>.
    /// </summary>
    private void Transform(ReadOnlySpan<double> input, Span<double> output, ReadOnlySpan<double> twiddles, int length, int outputOffset, int stride, int factor, int inputOffset, int count)
    {
        var radix = _factors[factor];
        var m = count / radix;
        if (m == 1)
        {
            for (var q = 0; q < radix; q++)
            {
                var source = 2 * (inputOffset + (q * stride));
                output[2 * (outputOffset + q)] = input[source];
                output[(2 * (outputOffset + q)) + 1] = input[source + 1];
            }
        }
        else
        {
            for (var q = 0; q < radix; q++)
            {
                Transform(input, output, twiddles, length, outputOffset + (q * m), stride * radix, factor + 1, inputOffset + (q * stride), m);
            }
        }

        var block = output.Slice(2 * outputOffset, 2 * count);
        switch (radix)
        {
            case 2:
                Butterfly2(block, twiddles, stride, m);
                break;
            case 4:
                Butterfly4(block, twiddles, stride, m);
                break;
            default:
                ButterflyGeneric(block, twiddles, length, stride, m, radix);
                break;
        }
    }

    private static void Butterfly2(Span<double> block, ReadOnlySpan<double> twiddles, int stride, int m)
    {
        for (var k = 0; k < m; k++)
        {
            var t = 2 * k * stride;
            var first = 2 * k;
            var second = 2 * (k + m);
            Multiply(block[second], block[second + 1], twiddles[t], twiddles[t + 1], out var re, out var im);
            block[second] = block[first] - re;
            block[second + 1] = block[first + 1] - im;
            block[first] += re;
            block[first + 1] += im;
        }
    }

    private static void Butterfly4(Span<double> block, ReadOnlySpan<double> twiddles, int stride, int m)
    {
        for (var k = 0; k < m; k++)
        {
            var i0 = 2 * k;
            var i1 = 2 * (k + m);
            var i2 = 2 * (k + (2 * m));
            var i3 = 2 * (k + (3 * m));
            Multiply(block[i1], block[i1 + 1], twiddles[2 * k * stride], twiddles[(2 * k * stride) + 1], out var t1Re, out var t1Im);
            Multiply(block[i2], block[i2 + 1], twiddles[4 * k * stride], twiddles[(4 * k * stride) + 1], out var t2Re, out var t2Im);
            Multiply(block[i3], block[i3 + 1], twiddles[6 * k * stride], twiddles[(6 * k * stride) + 1], out var t3Re, out var t3Im);

            // X0 = t0 + t1 + t2 + t3, X1 = (t0 - t2) - i (t1 - t3), X2 = (t0 + t2) - (t1 + t3), X3 = (t0 - t2) + i (t1 - t3)
            var evenSumRe = block[i0] + t2Re;
            var evenSumIm = block[i0 + 1] + t2Im;
            var evenDifferenceRe = block[i0] - t2Re;
            var evenDifferenceIm = block[i0 + 1] - t2Im;
            var oddSumRe = t1Re + t3Re;
            var oddSumIm = t1Im + t3Im;
            var oddDifferenceRe = t1Re - t3Re;
            var oddDifferenceIm = t1Im - t3Im;
            block[i0] = evenSumRe + oddSumRe;
            block[i0 + 1] = evenSumIm + oddSumIm;
            block[i2] = evenSumRe - oddSumRe;
            block[i2 + 1] = evenSumIm - oddSumIm;
            block[i1] = evenDifferenceRe + oddDifferenceIm;
            block[i1 + 1] = evenDifferenceIm - oddDifferenceRe;
            block[i3] = evenDifferenceRe - oddDifferenceIm;
            block[i3 + 1] = evenDifferenceIm + oddDifferenceRe;
        }
    }

    /// <summary>A butterfly of any radix (3 and 5), in <c>radix²</c> multiplications per group.</summary>
    private static void ButterflyGeneric(Span<double> block, ReadOnlySpan<double> twiddles, int length, int stride, int m, int radix)
    {
        Span<double> terms = stackalloc double[2 * 5];
        var rootStep = length / radix;
        for (var k = 0; k < m; k++)
        {
            // t_q = F_q W^(q k), then X_s = Σ_q t_q W_radix^(q s), the roots of the radix being every n / radix twiddle
            for (var q = 0; q < radix; q++)
            {
                var index = 2 * (k + (q * m));
                var t = 2 * q * k * stride;
                Multiply(block[index], block[index + 1], twiddles[t], twiddles[t + 1], out terms[2 * q], out terms[(2 * q) + 1]);
            }

            for (var s = 0; s < radix; s++)
            {
                var re = terms[0];
                var im = terms[1];
                var root = 0;
                for (var q = 1; q < radix; q++)
                {
                    root += s * rootStep;
                    if (root >= length)
                    {
                        root -= length;
                    }

                    Multiply(terms[2 * q], terms[(2 * q) + 1], twiddles[2 * root], twiddles[(2 * root) + 1], out var termRe, out var termIm);
                    re += termRe;
                    im += termIm;
                }

                var index = 2 * (k + (s * m));
                block[index] = re;
                block[index + 1] = im;
            }
        }
    }
}
