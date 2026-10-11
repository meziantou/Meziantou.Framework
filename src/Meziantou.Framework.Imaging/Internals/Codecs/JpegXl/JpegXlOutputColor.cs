namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Converts the samples of an image stored in the XYB color space to the color encoding the image declares (ISO/IEC
/// 18181-1, annex L): the inverse of the XYB transform, to linear samples of the primaries and of the white point of the
/// encoding (the luminance, for a grayscale image), then its transfer function.
/// </summary>
/// <remarks>
/// <para>
/// XYB is defined from linear sRGB; other primaries are reached through CIE XYZ adapted to D50 with the Bradford
/// transform, as an ICC profile would describe them. The chromaticities of the enumerated primaries and white points,
/// and the transfer functions, were taken from libjxl 0.12.0 (<c>color_encoding_cms.h</c>, <c>transfer_functions-inl.h</c>,
/// <c>tone_mapping-inl.h</c>): the standard is not publicly available.
/// </para>
/// <para>
/// High dynamic range content is returned as it is encoded: the value 1 of a linear sample is the intensity target of
/// the image, which the PQ and HLG transfer functions take into account. Nothing is tone mapped.
/// </para>
/// </remarks>
internal sealed class JpegXlOutputColor
{
    private static ReadOnlySpan<double> SrgbPrimaries => [0.639998686, 0.330010138, 0.300003784, 0.600003357, 0.150002046, 0.059997204];

    private readonly float[] _matrix = new float[9];
    private readonly float[] _bias = new float[3];
    private readonly float[] _biasRoot = new float[3];
    private readonly float[] _luminances = [0.2126f, 0.7152f, 0.0722f];
    private readonly int _transfer;
    private readonly float _gamma;
    private readonly float _intensityTarget;
    private readonly float _hlgExponent;

    private JpegXlOutputColor(JpegXlImageHeader header)
    {
        var encoding = header.ColorEncoding;
        IsGray = encoding.IsGray;
        _intensityTarget = header.IntensityTarget;
        var transform = header.TransformData;
        transform.OpsinBias.CopyTo(_bias, 0);
        for (var i = 0; i < 3; i++)
        {
            _biasRoot[i] = MathF.Cbrt(_bias[i]);
        }

        // An image described by an ICC profile is only decoded when the profile is sRGB
        Span<double> matrix = stackalloc double[9];
        for (var i = 0; i < 9; i++)
        {
            matrix[i] = transform.OpsinInverseMatrix[i];
        }

        var isSrgbPrimaries = encoding.WantIcc || (encoding.WhitePoint == JpegXlColorEncoding.WhitePointD65 && (IsGray || encoding.Primaries == JpegXlColorEncoding.PrimariesSrgb));
        if (!isSrgbPrimaries && !IsGray)
        {
            // From linear sRGB to XYZ adapted to D50, then to the primaries of the image
            var (whiteX, whiteY) = encoding.GetWhitePointChromaticity();
            Span<double> primaries = stackalloc double[6];
            encoding.GetPrimariesChromaticities(primaries);
            ReadOnlySpan<double> srgbPrimaries = SrgbPrimaries;
            ReadOnlySpan<double> d50 = [0.96422, 1.0, 0.82521];
            Span<double> srgbToXyz = stackalloc double[9];
            Span<double> adaptation = stackalloc double[9];
            Span<double> srgbToXyzD50 = stackalloc double[9];
            Span<double> targetToXyz = stackalloc double[9];
            Span<double> targetToXyzD50 = stackalloc double[9];
            Span<double> xyzD50ToTarget = stackalloc double[9];
            if (!ChromaticityMath.TryGetRgbToXyz(srgbPrimaries, 0.3127, 0.3290, srgbToXyz) || !ChromaticityMath.TryGetBradfordAdaptation(0.3127, 0.3290, d50, adaptation))
                throw new InvalidOperationException("The sRGB color space is not valid.");

            ChromaticityMath.Multiply(adaptation, srgbToXyz, srgbToXyzD50);
            if (!ChromaticityMath.TryGetRgbToXyz(primaries, whiteX, whiteY, targetToXyz) || !ChromaticityMath.TryGetBradfordAdaptation(whiteX, whiteY, d50, adaptation))
                throw JpegXlFormat.Invalid("the primaries or the white point of the color encoding are not valid.");

            ChromaticityMath.Multiply(adaptation, targetToXyz, targetToXyzD50);
            if (!ChromaticityMath.TryInvert(targetToXyzD50, xyzD50ToTarget))
                throw JpegXlFormat.Invalid("the primaries or the white point of the color encoding are not valid.");

            _luminances = [(float)targetToXyz[3], (float)targetToXyz[4], (float)targetToXyz[5]];
            Span<double> srgbToTarget = stackalloc double[9];
            Span<double> combined = stackalloc double[9];
            ChromaticityMath.Multiply(xyzD50ToTarget, srgbToXyzD50, srgbToTarget);
            ChromaticityMath.Multiply(srgbToTarget, matrix, combined);
            combined.CopyTo(matrix);
        }

        if (IsGray)
        {
            // Every row is the luminance of the linear sRGB samples
            Span<double> luminance = stackalloc double[3];
            for (var column = 0; column < 3; column++)
            {
                luminance[column] = (_luminances[0] * matrix[column]) + (_luminances[1] * matrix[3 + column]) + (_luminances[2] * matrix[6 + column]);
            }

            for (var i = 0; i < 9; i++)
            {
                matrix[i] = luminance[i % 3];
            }
        }

        // XYB is defined for a display of 255 nits
        var scale = 255.0 / _intensityTarget;
        for (var i = 0; i < 9; i++)
        {
            _matrix[i] = (float)(matrix[i] * scale);
        }

        if (encoding.WantIcc)
        {
            _transfer = JpegXlColorEncoding.TransferSrgb;
        }
        else if (encoding.HasGamma)
        {
            _transfer = -1;
            _gamma = (float)(encoding.Gamma * JpegXlColorEncoding.GammaScale);
        }
        else if (encoding.TransferFunction == JpegXlColorEncoding.TransferDci)
        {
            _transfer = -1;
            _gamma = 1f / 2.6f;
        }
        else
        {
            _transfer = encoding.TransferFunction;
            if (_transfer is not (JpegXlColorEncoding.TransferSrgb or JpegXlColorEncoding.TransferLinear or JpegXlColorEncoding.TransferBt709 or JpegXlColorEncoding.TransferPq or JpegXlColorEncoding.TransferHlg))
                throw JpegXlFormat.Unsupported("The JPEG XL image is stored in the XYB color space and has an unknown transfer function.", "XYB with an unknown transfer function");
        }

        // The samples of an HLG image describe the scene: the system gamma of the display is undone
        var hlgGamma = (1f / 1.2f) * MathF.Pow(1.111f, -MathF.Log2(_intensityTarget / 1000f));
        _hlgExponent = hlgGamma - 1;
    }

    /// <summary>Gets a value indicating whether the output is a single channel, written to the first one.</summary>
    public bool IsGray { get; }

    /// <summary>Creates the conversion of an image stored in XYB.</summary>
    /// <exception cref="UnsupportedImageFeatureException">The color encoding of the image cannot be produced.</exception>
    public static JpegXlOutputColor Create(JpegXlImageHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var encoding = header.ColorEncoding;
        if (!encoding.WantIcc && encoding.ColorSpace is not (JpegXlColorEncoding.ColorSpaceRgb or JpegXlColorEncoding.ColorSpaceGray))
            throw JpegXlFormat.Unsupported("The JPEG XL image is stored in the XYB color space and described by a color space that is not RGB nor grayscale.", "XYB with an unknown color space");

        if (encoding.IsGray && !encoding.WantIcc && encoding.WhitePoint != JpegXlColorEncoding.WhitePointD65)
            throw JpegXlFormat.Unsupported("The JPEG XL image is a grayscale image stored in the XYB color space with a white point other than D65.", "XYB grayscale with a white point other than D65");

        return new JpegXlOutputColor(header);
    }

    /// <summary>Converts rows of XYB samples in place: the samples of the three channels, or the gray samples in the first one.</summary>
    public void Apply(Span<float> x, Span<float> y, Span<float> b)
    {
        var m = _matrix;
        for (var i = 0; i < x.Length; i++)
        {
            // The sum and the difference of Y and X, and B, are the cube roots of the responses of three cone types
            var gammaR = (y[i] + x[i]) - _biasRoot[0];
            var gammaG = (y[i] - x[i]) - _biasRoot[1];
            var gammaB = b[i] - _biasRoot[2];
            var mixedR = (gammaR * gammaR * gammaR) + _bias[0];
            var mixedG = (gammaG * gammaG * gammaG) + _bias[1];
            var mixedB = (gammaB * gammaB * gammaB) + _bias[2];
            x[i] = (m[0] * mixedR) + (m[1] * mixedG) + (m[2] * mixedB);
            y[i] = (m[3] * mixedR) + (m[4] * mixedG) + (m[5] * mixedB);
            b[i] = (m[6] * mixedR) + (m[7] * mixedG) + (m[8] * mixedB);
        }

        if (_transfer == JpegXlColorEncoding.TransferLinear)
            return;

        if (_transfer == JpegXlColorEncoding.TransferHlg && MathF.Abs(_hlgExponent) > 0.01f && !IsGray)
        {
            for (var i = 0; i < x.Length; i++)
            {
                var luminance = (_luminances[0] * x[i]) + (_luminances[1] * y[i]) + (_luminances[2] * b[i]);
                var ratio = MathF.Min(MathF.Pow(luminance, _hlgExponent), 1e9f);
                x[i] *= ratio;
                y[i] *= ratio;
                b[i] *= ratio;
            }
        }

        Encode(x);
        if (!IsGray)
        {
            Encode(y);
            Encode(b);
        }
    }

    /// <summary>Applies the transfer function to linear samples; a negative sample is the opposite of the encoding of its opposite.</summary>
    private void Encode(Span<float> samples)
    {
        switch (_transfer)
        {
            case JpegXlColorEncoding.TransferSrgb:
                JpegXlColorTransform.LinearToSrgb(samples);
                break;

            case JpegXlColorEncoding.TransferBt709:
                for (var i = 0; i < samples.Length; i++)
                {
                    var magnitude = MathF.Abs(samples[i]);
                    var encoded = magnitude < 0.018f ? 4.5f * magnitude : (1.099f * MathF.Pow(magnitude, 0.45f)) - 0.099f;
                    samples[i] = MathF.CopySign(encoded, samples[i]);
                }

                break;

            case JpegXlColorEncoding.TransferPq:
            {
                // SMPTE ST 2084, for a linear sample of 1 at the intensity target of the image
                const float M1 = 2610f / 16384;
                const float M2 = 2523f / 4096 * 128;
                const float C1 = 3424f / 4096;
                const float C2 = 2413f / 4096 * 32;
                const float C3 = 2392f / 4096 * 32;
                var scale = _intensityTarget / 10000f;
                for (var i = 0; i < samples.Length; i++)
                {
                    var power = MathF.Pow(MathF.Abs(samples[i]) * scale, M1);
                    var encoded = MathF.Pow((C1 + (C2 * power)) / (1 + (C3 * power)), M2);
                    samples[i] = MathF.CopySign(encoded, samples[i]);
                }

                break;
            }

            case JpegXlColorEncoding.TransferHlg:
            {
                // ARIB STD-B67
                const float A = 0.17883277f;
                const float B = 0.28466892f;
                const float C = 0.55991073f;
                for (var i = 0; i < samples.Length; i++)
                {
                    var magnitude = MathF.Abs(samples[i]);
                    var encoded = magnitude <= 1f / 12 ? MathF.Sqrt(3 * magnitude) : (A * MathF.Log((12 * magnitude) - B)) + C;
                    samples[i] = MathF.CopySign(encoded, samples[i]);
                }

                break;
            }

            default:
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = samples[i] <= 1e-5f ? 0 : MathF.Pow(samples[i], _gamma);
                }

                break;
        }
    }
}
