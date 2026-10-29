using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Converts the samples of an image to the XYB color space (ISO/IEC 18181-1, annex L, in the direction the standard does
/// not describe): the inverse of the transfer function of the color encoding, the responses of the three cone types, their
/// cube roots, then the sum and the difference of the first two.
/// </summary>
/// <remarks>
/// Nothing is transcribed a second time: the matrix is the inverse of the one <see cref="JpegXlOutputColor"/> computes for
/// the image header that was written, and the biases are the ones of that header, so that the decoder undoes what is done
/// here.
/// </remarks>
internal sealed class JpegXlForwardColor : IDisposable
{
    private readonly float[] _matrix = new float[9];
    private readonly float[] _bias = new float[3];
    private readonly float[] _biasRoot = new float[3];
    private Av1PooledArray<float>? _linear;

    /// <param name="header">The image header the decoder will read: an image stored in XYB, whose color encoding is enumerated or an sRGB profile.</param>
    /// <param name="bitsPerSample">The depth of the samples that are converted: 8 or 16.</param>
    /// <param name="scope">The scope of the table of the transfer function.</param>
    /// <exception cref="UnsupportedImageFeatureException">The transfer function of the color encoding has no inverse here.</exception>
    public JpegXlForwardColor(JpegXlImageHeader header, int bitsPerSample, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(scope);
        var encoding = header.ColorEncoding;
        header.TransformData.OpsinBias.CopyTo(_bias, 0);
        for (var i = 0; i < 3; i++)
        {
            _biasRoot[i] = MathF.Cbrt(_bias[i]);
        }

        // From the cone responses to linear samples, as the decoder computes it; inverted here
        Span<double> matrix = stackalloc double[9];
        Span<double> inverse = stackalloc double[9];
        Span<float> luminances = stackalloc float[3];
        JpegXlOutputColor.ComputeMatrix(header, matrix, luminances);
        var scale = 255.0 / header.IntensityTarget;
        for (var i = 0; i < 9; i++)
        {
            matrix[i] *= scale;
        }

        if (!ChromaticityMath.TryInvert(matrix, inverse))
            throw new InvalidOperationException("The color matrix of the image cannot be inverted.");

        for (var i = 0; i < 9; i++)
        {
            _matrix[i] = (float)inverse[i];
        }

        // The linear value of every sample value
        Func<double, double> decode;
        if (encoding.WantIcc)
        {
            decode = DecodeSrgb;
        }
        else if (encoding.HasGamma)
        {
            var exponent = 1 / (encoding.Gamma * JpegXlColorEncoding.GammaScale);
            decode = value => Math.Pow(value, exponent);
        }
        else
        {
            decode = encoding.TransferFunction switch
            {
                JpegXlColorEncoding.TransferSrgb => DecodeSrgb,
                JpegXlColorEncoding.TransferLinear => value => value,
                JpegXlColorEncoding.TransferBt709 => value => value < 4.5 * 0.018 ? value / 4.5 : Math.Pow((value + 0.099) / 1.099, 1 / 0.45),
                JpegXlColorEncoding.TransferDci => value => Math.Pow(value, 2.6),
                _ => throw JpegXlFormat.Unsupported("Lossy JPEG XL output is not implemented for this transfer function.", "lossy JPEG XL encoding of this transfer function"),
            };
        }

        var count = 1 << bitsPerSample;
        _linear = Av1PooledArray<float>.Rent(scope, count, clear: false, AllocationKind.Temporary);
        var linear = _linear.Span;
        var max = (double)(count - 1);
        for (var i = 0; i < count; i++)
        {
            linear[i] = (float)decode(i / max);
        }
    }

    /// <summary>Converts pixels of a row to XYB.</summary>
    /// <param name="row">The bytes of the row; a grayscale sample is the value of the three primaries of sRGB.</param>
    /// <param name="format">The pixel format of the row.</param>
    /// <param name="start">The first pixel to convert.</param>
    /// <param name="x">Receives the X samples: one per pixel to convert.</param>
    /// <param name="y">Receives the Y samples.</param>
    /// <param name="b">Receives the B samples.</param>
    public void Convert(ReadOnlySpan<byte> row, PixelFormat format, int start, Span<float> x, Span<float> y, Span<float> b)
    {
        // The linear values of the three primaries first, in the three outputs, then their conversion in place
        var linear = _linear!.Span;
        var count = x.Length;
        y = y[..count];
        b = b[..count];
        switch (format)
        {
            case PixelFormat.Gray8:
                for (var i = 0; i < count; i++)
                {
                    var value = linear[row[start + i]];
                    x[i] = value;
                    y[i] = value;
                    b[i] = value;
                }

                break;

            case PixelFormat.Gray16:
            {
                var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(start, count);
                for (var i = 0; i < count; i++)
                {
                    var value = linear[samples[i]];
                    x[i] = value;
                    y[i] = value;
                    b[i] = value;
                }

                break;
            }

            case PixelFormat.Rgb24:
            {
                var pixels = row.Slice(start * 3, count * 3);
                for (var i = 0; i < count; i++)
                {
                    x[i] = linear[pixels[i * 3]];
                    y[i] = linear[pixels[(i * 3) + 1]];
                    b[i] = linear[pixels[(i * 3) + 2]];
                }

                break;
            }

            case PixelFormat.Rgba32 or PixelFormat.Bgra32:
            {
                var pixels = row.Slice(start * 4, count * 4);
                var redOffset = format == PixelFormat.Bgra32 ? 2 : 0;
                for (var i = 0; i < count; i++)
                {
                    x[i] = linear[pixels[(i * 4) + redOffset]];
                    y[i] = linear[pixels[(i * 4) + 1]];
                    b[i] = linear[pixels[(i * 4) + 2 - redOffset]];
                }

                break;
            }

            default:
            {
                var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(start * 4, count * 4);
                for (var i = 0; i < count; i++)
                {
                    x[i] = linear[samples[i * 4]];
                    y[i] = linear[samples[(i * 4) + 1]];
                    b[i] = linear[samples[(i * 4) + 2]];
                }

                break;
            }
        }

        ToXyb(x, y, b);
    }

    /// <summary>
    /// Gets the cube root of a value that is not negative, as <see cref="ToXyb"/> computes it: an estimate from the bits of
    /// the value, then three Newton steps (within 3e-7 relative of <see cref="MathF.Cbrt"/>; below 2^-100 the root is 0).
    /// The scalar and the vector forms run the same operations, so the result never depends on the vector width.
    /// </summary>
    internal static float CubeRoot(float value)
    {
        var estimate = BitConverter.Int32BitsToSingle((int)(BitConverter.SingleToInt32Bits(value) * OneThird) + CubeRootMagic);
        estimate = (estimate + estimate + (value / (estimate * estimate))) * OneThird;
        estimate = (estimate + estimate + (value / (estimate * estimate))) * OneThird;
        estimate = (estimate + estimate + (value / (estimate * estimate))) * OneThird;
        return value < CubeRootFloor ? 0 : estimate;
    }

    /// <inheritdoc cref="CubeRoot(float)"/>
    internal static Vector<float> CubeRoot(Vector<float> value)
    {
        var third = new Vector<float>(OneThird);
        var bits = Vector.ConvertToSingle(Vector.AsVectorInt32(value)) * third;
        var estimate = Vector.AsVectorSingle(Vector.ConvertToInt32(bits) + new Vector<int>(CubeRootMagic));
        estimate = (estimate + estimate + (value / (estimate * estimate))) * third;
        estimate = (estimate + estimate + (value / (estimate * estimate))) * third;
        estimate = (estimate + estimate + (value / (estimate * estimate))) * third;
        return Vector.ConditionalSelect(Vector.LessThan(value, new Vector<float>(CubeRootFloor)), Vector<float>.Zero, estimate);
    }

    // The bits of a float divided by three, plus this constant, are within a few percent of its cube root
    private const int CubeRootMagic = 0x2a5137a0;
    private const float OneThird = 1f / 3;
    private const float CubeRootFloor = 7.888609e-31f;

    public void Dispose()
    {
        _linear?.Dispose();
        _linear = null;
    }

    private static double DecodeSrgb(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    /// <summary>Converts linear values of the primaries (red in <paramref name="x"/>, green in <paramref name="y"/>, blue in <paramref name="b"/>) to XYB, in place.</summary>
    private void ToXyb(Span<float> x, Span<float> y, Span<float> b)
    {
        var m = _matrix;
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var m0 = new Vector<float>(m[0]);
            var m1 = new Vector<float>(m[1]);
            var m2 = new Vector<float>(m[2]);
            var m3 = new Vector<float>(m[3]);
            var m4 = new Vector<float>(m[4]);
            var m5 = new Vector<float>(m[5]);
            var m6 = new Vector<float>(m[6]);
            var m7 = new Vector<float>(m[7]);
            var m8 = new Vector<float>(m[8]);
            var bias0 = new Vector<float>(_bias[0]);
            var bias1 = new Vector<float>(_bias[1]);
            var bias2 = new Vector<float>(_bias[2]);
            var root0 = new Vector<float>(_biasRoot[0]);
            var root1 = new Vector<float>(_biasRoot[1]);
            var root2 = new Vector<float>(_biasRoot[2]);
            var half = new Vector<float>(0.5f);
            for (; i <= x.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                var red = new Vector<float>(x[i..]);
                var green = new Vector<float>(y[i..]);
                var blue = new Vector<float>(b[i..]);
                var mixedL = (m0 * red) + (m1 * green) + (m2 * blue);
                var mixedM = (m3 * red) + (m4 * green) + (m5 * blue);
                var mixedS = (m6 * red) + (m7 * green) + (m8 * blue);
                var l = CubeRoot(Vector.Max(mixedL - bias0, Vector<float>.Zero)) + root0;
                var mm = CubeRoot(Vector.Max(mixedM - bias1, Vector<float>.Zero)) + root1;
                var s = CubeRoot(Vector.Max(mixedS - bias2, Vector<float>.Zero)) + root2;
                ((l - mm) * half).CopyTo(x[i..]);
                ((l + mm) * half).CopyTo(y[i..]);
                s.CopyTo(b[i..]);
            }
        }

        for (; i < x.Length; i++)
        {
            var red = x[i];
            var green = y[i];
            var blue = b[i];
            var mixedL = (m[0] * red) + (m[1] * green) + (m[2] * blue);
            var mixedM = (m[3] * red) + (m[4] * green) + (m[5] * blue);
            var mixedS = (m[6] * red) + (m[7] * green) + (m[8] * blue);
            var l = CubeRoot(MathF.Max(mixedL - _bias[0], 0)) + _biasRoot[0];
            var mm = CubeRoot(MathF.Max(mixedM - _bias[1], 0)) + _biasRoot[1];
            var s = CubeRoot(MathF.Max(mixedS - _bias[2], 0)) + _biasRoot[2];
            x[i] = (l - mm) * 0.5f;
            y[i] = (l + mm) * 0.5f;
            b[i] = s;
        }
    }
}
