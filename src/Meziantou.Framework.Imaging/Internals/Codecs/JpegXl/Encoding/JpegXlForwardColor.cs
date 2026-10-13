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
        var linear = _linear!.Span;
        var count = x.Length;
        switch (format)
        {
            case PixelFormat.Gray8:
                for (var i = 0; i < count; i++)
                {
                    var value = linear[row[start + i]];
                    ToXyb(value, value, value, out x[i], out y[i], out b[i]);
                }

                break;

            case PixelFormat.Gray16:
            {
                var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(start, count);
                for (var i = 0; i < count; i++)
                {
                    var value = linear[samples[i]];
                    ToXyb(value, value, value, out x[i], out y[i], out b[i]);
                }

                break;
            }

            case PixelFormat.Rgb24:
            {
                var pixels = row.Slice(start * 3, count * 3);
                for (var i = 0; i < count; i++)
                {
                    ToXyb(linear[pixels[i * 3]], linear[pixels[(i * 3) + 1]], linear[pixels[(i * 3) + 2]], out x[i], out y[i], out b[i]);
                }

                break;
            }

            case PixelFormat.Rgba32 or PixelFormat.Bgra32:
            {
                var pixels = row.Slice(start * 4, count * 4);
                var redOffset = format == PixelFormat.Bgra32 ? 2 : 0;
                for (var i = 0; i < count; i++)
                {
                    ToXyb(linear[pixels[(i * 4) + redOffset]], linear[pixels[(i * 4) + 1]], linear[pixels[(i * 4) + 2 - redOffset]], out x[i], out y[i], out b[i]);
                }

                break;
            }

            default:
            {
                var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(row)).Slice(start * 4, count * 4);
                for (var i = 0; i < count; i++)
                {
                    ToXyb(linear[samples[i * 4]], linear[samples[(i * 4) + 1]], linear[samples[(i * 4) + 2]], out x[i], out y[i], out b[i]);
                }

                break;
            }
        }
    }

    public void Dispose()
    {
        _linear?.Dispose();
        _linear = null;
    }

    private static double DecodeSrgb(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private void ToXyb(float red, float green, float blue, out float x, out float y, out float b)
    {
        var m = _matrix;
        var mixedL = (m[0] * red) + (m[1] * green) + (m[2] * blue);
        var mixedM = (m[3] * red) + (m[4] * green) + (m[5] * blue);
        var mixedS = (m[6] * red) + (m[7] * green) + (m[8] * blue);
        var l = MathF.Cbrt(MathF.Max(mixedL - _bias[0], 0)) + _biasRoot[0];
        var mm = MathF.Cbrt(MathF.Max(mixedM - _bias[1], 0)) + _biasRoot[1];
        var s = MathF.Cbrt(MathF.Max(mixedS - _bias[2], 0)) + _biasRoot[2];
        x = (l - mm) * 0.5f;
        y = (l + mm) * 0.5f;
        b = s;
    }
}
