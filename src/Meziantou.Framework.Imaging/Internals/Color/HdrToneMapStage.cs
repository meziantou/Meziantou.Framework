namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Converts the linear light that a PQ or HLG transfer function decodes to (<see cref="CicpDecodeStage"/>) into linear
/// light of a standard dynamic range, in [0, 1]: the light of a display, divided by the luminance that becomes white,
/// with the highlights compressed by a tone mapping operator (<see cref="HdrToneMappingOperator"/>, which gives the
/// formula of each curve).
/// </summary>
/// <remarks>
/// <para>
/// PQ (SMPTE ST 2084): the decoded value is a fraction of 10,000 cd/m2. HLG (Rec. ITU-R BT.2100, table 5): the decoded
/// value is the light of the scene, and the light of the display is <c>Lw Ys^(gamma - 1) E</c> for each component, where
/// <c>Ys</c> is the luminance of the scene color, <c>Lw</c> the nominal peak of the display and
/// <c>gamma = 1.2 + 0.42 log10(Lw / 1000)</c>.
/// </para>
/// <para>
/// The curve is evaluated on the largest component and the three components are scaled together. The curve of Report
/// ITU-R BT.2390 (section 5.4.1) works on PQ values normalized by the PQ value of the peak of the source; the black
/// of the source and of the target is no light, so its black level lift does nothing.
/// </para>
/// </remarks>
internal sealed class HdrToneMapStage : IccStage
{
    private const double MobiusKnee = 0.3;

    private readonly bool _isHlg;
    private readonly int _channelCount;
    private readonly HdrToneMappingOperator _operator;
    private readonly double _white;
    private readonly double _peak;
    private readonly double _displayPeak;
    private readonly double _gammaMinusOne;
    private readonly double _kr;
    private readonly double _kg;
    private readonly double _kb;
    private readonly bool _isWhiteUnits;

    // Möbius: s (x + a) / (x + b) above the knee
    private readonly double _mobiusA;
    private readonly double _mobiusB;
    private readonly double _mobiusScale;

    // Hable: 1 / h(peak)
    private readonly double _hableScale;

    // BT.2390: the PQ values of the peak of the source and of the target white, and the knee
    private readonly double _sourcePq;
    private readonly double _maxLum;
    private readonly double _kneeStart;

    /// <param name="colorSpace">The color space of the source, whose transfer characteristics are PQ or HLG.</param>
    /// <param name="toneMapping">The curve.</param>
    /// <param name="peakLuminance">The peak of the source, in candelas per square meter.</param>
    /// <param name="whiteLuminance">The luminance that becomes white, in candelas per square meter.</param>
    public HdrToneMapStage(CicpColorSpace colorSpace, HdrToneMappingOperator toneMapping, double peakLuminance, double whiteLuminance)
        : this(colorSpace.Transfer is CicpTransfer.Hlg, colorSpace.ChannelCount, toneMapping, peakLuminance, whiteLuminance, isWhiteUnits: false)
    {
        // The luminance of a color: the Y row of the matrix of the primaries (CIE XYZ primaries: Y itself)
        Span<double> coefficients = [0, 1, 0];
        if (colorSpace is { IsGray: false, Primaries: not 10 } && !CicpPrimaries.TryGetLuminanceCoefficients(colorSpace.Primaries, coefficients))
            throw new UnsupportedImageFeatureException($"The color primaries {colorSpace.Primaries} of the CICP color description do not define a color space.", ImageFormat.Unknown, "CICP color primaries without a color space");

        (_kr, _kg, _kb) = (coefficients[0], coefficients[1], coefficients[2]);
    }

    private HdrToneMapStage(bool isHlg, int channelCount, HdrToneMappingOperator toneMapping, double peakLuminance, double whiteLuminance, bool isWhiteUnits)
    {
        _isHlg = isHlg;
        _isWhiteUnits = isWhiteUnits;
        _channelCount = channelCount;
        _operator = toneMapping;
        _white = whiteLuminance;
        _displayPeak = peakLuminance;
        _gammaMinusOne = 0.2 + (0.42 * Math.Log10(peakLuminance / 1000));

        // A source that is not brighter than the white is not compressed, and never brightened
        var peak = Math.Max(peakLuminance / whiteLuminance, 1);
        _peak = peak;
        if (peak > 1)
        {
            var j = MobiusKnee;
            _mobiusA = -j * j * (peak - 1) / ((j * j) - (2 * j) + peak);
            _mobiusB = ((j * j) - (2 * j * peak) + peak) / (peak - 1);
            _mobiusScale = ((_mobiusB * _mobiusB) + (2 * _mobiusB * j) + (j * j)) / (_mobiusB - _mobiusA);
        }

        _hableScale = 1 / Hable(peak);
        _sourcePq = CicpTransfer.Encode(CicpTransfer.Pq, peak * whiteLuminance / 10_000);
        _maxLum = CicpTransfer.Encode(CicpTransfer.Pq, whiteLuminance / 10_000) / _sourcePq;
        _kneeStart = Math.Max((1.5 * _maxLum) - 0.5, 0);
    }

    /// <summary>Gets the peak of the source in units of the white (at least 1).</summary>
    public double Peak => _peak;

    /// <summary>
    /// Creates the tone mapping of linear light that is already in units of the white: the color components of the
    /// floating-point pixel formats. <see cref="Apply"/> then scales nothing before the curve.
    /// </summary>
    /// <param name="toneMapping">The curve.</param>
    /// <param name="peak">The peak of the source in units of the white; a value below 1 is 1.</param>
    /// <param name="whiteLuminance">The luminance of the white, in candelas per square meter (the PQ values of the curve of BT.2390 need it).</param>
    /// <returns>The stage, for three components.</returns>
    public static HdrToneMapStage CreateForWhiteUnits(HdrToneMappingOperator toneMapping, double peak, double whiteLuminance)
        => new(isHlg: false, channelCount: 3, toneMapping, Math.Max(peak, 1) * whiteLuminance, whiteLuminance, isWhiteUnits: true);

    public override void Apply(Span<double> values)
    {
        var r = values[0];
        var (g, b) = _channelCount is 1 ? (r, r) : (values[1], values[2]);
        double scale;
        if (_isWhiteUnits)
        {
            scale = 1;
        }
        else if (_isHlg)
        {
            // The light of the display for the light of the scene; a scene without light is black
            var luminance = (_kr * r) + (_kg * g) + (_kb * b);
            scale = luminance > 0 ? _displayPeak * Math.Pow(luminance, _gammaMinusOne) / _white : 0;
        }
        else
        {
            scale = 10_000 / _white;
        }

        // In units of the white
        r *= scale;
        g *= scale;
        b *= scale;
        var x = Math.Max(r, Math.Max(g, b));
        if (!(x > 0))
        {
            values[0] = values[1] = values[2] = 0;
            return;
        }

        var factor = Map(x) / x;
        values[0] = r * factor;
        if (_channelCount is not 1)
        {
            values[1] = g * factor;
            values[2] = b * factor;
        }
    }

    // The value of the curve for a light of x whites, in [0, 1]
    private double Map(double x)
    {
        // Light above the peak of the source is the peak
        if (x > _peak)
            x = _peak;

        double y;
        switch (_operator)
        {
            case HdrToneMappingOperator.Clip:
                y = x;
                break;
            case HdrToneMappingOperator.Reinhard:
                y = x * (1 + (x / (_peak * _peak))) / (1 + x);
                break;
            case HdrToneMappingOperator.Hable:
                y = Hable(x) * _hableScale;
                break;
            case HdrToneMappingOperator.Mobius:
                y = x <= MobiusKnee || !(_peak > 1) ? x : _mobiusScale * (x + _mobiusA) / (x + _mobiusB);
                break;
            default:
            {
                if (!(_maxLum < 1))
                {
                    y = x;
                    break;
                }

                // Report ITU-R BT.2390, section 5.4.1: E1 is the PQ value over the PQ value of the peak; above the knee KS,
                // the Hermite spline P reaches maxLum, the PQ value of the white over the same
                var e1 = CicpTransfer.Encode(CicpTransfer.Pq, x * _white / 10_000) / _sourcePq;
                var e2 = e1;
                if (e1 > _kneeStart)
                {
                    var t = (e1 - _kneeStart) / (1 - _kneeStart);
                    var (t2, t3) = (t * t, t * t * t);
                    e2 = (((2 * t3) - (3 * t2) + 1) * _kneeStart) + ((t3 - (2 * t2) + t) * (1 - _kneeStart)) + (((-2 * t3) + (3 * t2)) * _maxLum);
                }

                y = CicpTransfer.Decode(CicpTransfer.Pq, e2 * _sourcePq) * 10_000 / _white;
                break;
            }
        }

        return y >= 1 ? 1 : y;
    }

    private static double Hable(double x)
    {
        const double A = 0.15;
        const double B = 0.50;
        const double C = 0.10;
        const double D = 0.20;
        const double E = 0.02;
        const double F = 0.30;
        return (((x * ((A * x) + (C * B))) + (D * E)) / ((x * ((A * x) + B)) + (D * F))) - (E / F);
    }
}
