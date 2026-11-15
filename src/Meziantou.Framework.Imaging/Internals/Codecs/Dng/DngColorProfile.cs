namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The camera profile of IFD 0 of a DNG file and the transform it gives from the camera color space (linear reference
/// values) to CIE XYZ with the D50 white point, then to the output of the decoder: linear light with the primaries of
/// ITU-R BT.709 and the D65 white point (DNG specification 1.7.1, chapter 6).
/// </summary>
/// <remarks>
/// <para>
/// One or two calibrations (<c>ColorMatrix1/2</c>, <c>ForwardMatrix1/2</c>, <c>CameraCalibration1/2</c>, for the
/// illuminants <c>CalibrationIlluminant1/2</c>) are interpolated linearly in inverse correlated color temperature, the
/// weight of the white balance given by <c>AsShotNeutral</c> (found by the iteration of chapter 6) or
/// <c>AsShotWhiteXY</c>. The correlated color temperature of a chromaticity is computed by the method of Robertson
/// (1968), from his table of isotemperature lines (as given by Wyszecki and Stiles, "Color Science", 2nd edition, table
/// 1(3.11)); the temperatures of the EXIF light sources are the nominal correlated color temperatures of the CIE
/// illuminants they name (CIE 15:2004), the middle of the range EXIF 2.3 gives for the fluorescent classes, and the
/// illuminant D55 for daylight, flash and fine weather, D65 for cloudy weather, D75 for shade, A for tungsten.
/// </para>
/// <para>
/// Without forward matrices, the white balance is a linear Bradford adaptation of XYZ from the white to D50, as chapter 6
/// recommends. The neutral is scaled so that its largest component is 1, which maps to the D50 white (Y = 1). The
/// camera calibrations are used when <c>CameraCalibrationSignature</c> and <c>ProfileCalibrationSignature</c> are the
/// same (both absent included), identity matrices otherwise. The hue/saturation maps, the look table and the tone curve
/// of the profile are a rendering style, not colorimetry: they are not applied. A third calibration (DNG 1.6) and custom
/// illuminants (255) are not supported.
/// </para>
/// </remarks>
internal sealed class DngColorProfile
{
    /// <summary>The chromaticity of D50 as DNG uses it for the white of its connection space.</summary>
    public const double D50x = 0.3457;

    /// <inheritdoc cref="D50x"/>
    public const double D50y = 0.3585;

    // The linear Bradford cone response matrix (Lam, 1985)
    private static readonly DngMatrix Bradford = new(3, 3, [0.8951, 0.2664, -0.1614, -0.7502, 1.7135, 0.0367, 0.0389, -0.0685, 1.0296]);

    private readonly Calibration[] _calibrations;
    private readonly double[] _analogBalance;
    private readonly double[]? _asShotNeutral;
    private readonly (double X, double Y)? _asShotWhite;
    private readonly bool _useCameraCalibration;

    private DngColorProfile(int planes, Calibration[] calibrations, double[] analogBalance, double[]? asShotNeutral, (double X, double Y)? asShotWhite, bool useCameraCalibration, double exposure)
    {
        Planes = planes;
        _calibrations = calibrations;
        _analogBalance = analogBalance;
        _asShotNeutral = asShotNeutral;
        _asShotWhite = asShotWhite;
        _useCameraCalibration = useCameraCalibration;
        Exposure = exposure;
    }

    /// <summary>Gets the number of color planes of the camera color space.</summary>
    public int Planes { get; }

    /// <summary>Gets the exposure of the rendering, in stops: <c>BaselineExposure</c> plus <c>BaselineExposureOffset</c>.</summary>
    public double Exposure { get; }

    /// <summary>Reads the camera profile of IFD 0 for a camera color space of <paramref name="planes"/> planes.</summary>
    /// <exception cref="InvalidImageContentException">A tag of the profile is malformed, or a color image has no ColorMatrix1.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The profile uses a third calibration or a custom illuminant.</exception>
    public static DngColorProfile Read(TiffDirectory ifd0, int planes)
    {
        var exposure = (DngTagReader.TryReadNumber(ifd0, DngFormat.BaselineExposureTag, "BaselineExposure") ?? 0) + (DngTagReader.TryReadNumber(ifd0, DngFormat.BaselineExposureOffsetTag, "BaselineExposureOffset") ?? 0);
        if (Math.Abs(exposure) > 64)
            throw DngFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DNG file declares a baseline exposure of {exposure} stops."));

        if (ifd0.Find(DngFormat.CalibrationIlluminant3Tag) is not null)
            throw DngFormat.Unsupported("The camera profile of the DNG file has a third calibration (CalibrationIlluminant3, DNG 1.6), which is not supported.", "CalibrationIlluminant3");

        var analogBalance = DngTagReader.TryReadExactNumbers(ifd0, DngFormat.AnalogBalanceTag, "AnalogBalance", planes) ?? [.. Enumerable.Repeat(1.0, planes)];
        foreach (var value in analogBalance)
        {
            if (!(value > 0))
                throw DngFormat.Invalid("The AnalogBalance tag of the DNG file has a value that is not positive.");
        }

        double[]? neutral = null;
        (double, double)? white = null;
        if (DngTagReader.TryReadExactNumbers(ifd0, DngFormat.AsShotNeutralTag, "AsShotNeutral", planes) is { } asShotNeutral)
        {
            var max = asShotNeutral.Max();
            if (asShotNeutral.Any(value => !(value > 0)))
                throw DngFormat.Invalid("The AsShotNeutral tag of the DNG file has a value that is not positive.");

            neutral = [.. asShotNeutral.Select(value => value / max)];
        }
        else if (DngTagReader.TryReadExactNumbers(ifd0, DngFormat.AsShotWhiteXyTag, "AsShotWhiteXY", 2) is { } xy)
        {
            if (!(xy[0] > 0 && xy[1] > 0 && xy[0] + xy[1] < 1))
                throw DngFormat.Invalid("The AsShotWhiteXY tag of the DNG file is not a chromaticity.");

            white = (xy[0], xy[1]);
        }

        var calibrations = new List<Calibration>();
        if (planes > 1 || ifd0.Find(DngFormat.ColorMatrix1Tag) is not null)
        {
            calibrations.Add(ReadCalibration(ifd0, planes, 1, DngFormat.CalibrationIlluminant1Tag, DngFormat.ColorMatrix1Tag, DngFormat.ForwardMatrix1Tag, DngFormat.CameraCalibration1Tag)
                ?? throw DngFormat.Invalid("The DNG file stores color raw data without a ColorMatrix1 tag, which is required."));
            if (ReadCalibration(ifd0, planes, 2, DngFormat.CalibrationIlluminant2Tag, DngFormat.ColorMatrix2Tag, DngFormat.ForwardMatrix2Tag, DngFormat.CameraCalibration2Tag) is { } second)
            {
                calibrations.Add(second);
            }
        }

        // Either every calibration has a forward matrix or none of them is used
        if (calibrations.Exists(calibration => calibration.Forward is null))
        {
            calibrations = [.. calibrations.Select(calibration => calibration with { Forward = null })];
        }

        var cameraSignature = DngTagReader.TryReadString(ifd0, DngFormat.CameraCalibrationSignatureTag, "CameraCalibrationSignature");
        var profileSignature = DngTagReader.TryReadString(ifd0, DngFormat.ProfileCalibrationSignatureTag, "ProfileCalibrationSignature");
        return new DngColorProfile(planes, [.. calibrations], analogBalance, neutral, white, string.Equals(cameraSignature, profileSignature, StringComparison.Ordinal), exposure);
    }

    /// <summary>
    /// Computes the matrix that maps a pixel of the camera color space (<see cref="Planes"/> values) to the output:
    /// linear BT.709 RGB (D65), with the exposure of the rendering applied.
    /// </summary>
    /// <returns>A 3 x <see cref="Planes"/> matrix.</returns>
    /// <exception cref="InvalidImageContentException">The matrices of the profile cannot be inverted.</exception>
    public DngMatrix GetCameraToOutput()
    {
        var gain = Math.Pow(2, Exposure);
        if (_calibrations.Length == 0)
        {
            // Monochrome data: the gray level is the luminance of a neutral color
            var gray = new DngMatrix(3, 1, [1, 1, 1]);
            return gray.Scale(gain);
        }

        var cameraToXyz = GetCameraToXyzD50(out _);
        return GetXyzD50ToOutput().Multiply(cameraToXyz).Scale(gain);
    }

    /// <summary>Computes the camera to XYZ (D50) transform of chapter 6.</summary>
    /// <param name="white">The chromaticity of the white balance.</param>
    /// <returns>A 3 x <see cref="Planes"/> matrix.</returns>
    public DngMatrix GetCameraToXyzD50(out (double X, double Y) white)
    {
        var n = Planes;
        var neutral = _asShotNeutral;
        if (neutral is null)
        {
            // Without a white balance, the white is the one of the connection space
            white = _asShotWhite ?? (D50x, D50y);
            var xyzToCamera = GetXyzToCamera(GetWeight(white));
            var computed = xyzToCamera.Multiply(DngMatrix.Column(ToXyz(white.X, white.Y)));
            neutral = new double[n];
            var max = 0.0;
            for (var i = 0; i < n; i++)
            {
                neutral[i] = computed[i, 0];
                max = Math.Max(max, neutral[i]);
            }

            if (!(max > 0))
                throw DngFormat.Invalid("The color matrix of the DNG file maps its white balance to a camera neutral that is not positive.");

            for (var i = 0; i < n; i++)
            {
                neutral[i] /= max;
            }
        }
        else
        {
            white = FindWhite(neutral);
        }

        var weight = GetWeight(white);
        var analogCalibration = DngMatrix.Diagonal(_analogBalance).Multiply(GetCameraCalibration(weight));
        if (_calibrations[0].Forward is not null)
        {
            // CameraToXYZ_D50 = FM * D * Inverse(AB * CC), D mapping the reference neutral to a unit vector
            var inverseAnalogCalibration = analogCalibration.TryInvert() ?? throw DngFormat.Invalid("The AnalogBalance and CameraCalibration matrices of the DNG file cannot be inverted.");
            var referenceNeutral = inverseAnalogCalibration.Multiply(DngMatrix.Column(neutral));
            var diagonal = new double[n];
            for (var i = 0; i < n; i++)
            {
                if (!(referenceNeutral[i, 0] > 0))
                    throw DngFormat.Invalid("The camera neutral of the DNG file is not positive in the reference camera.");

                diagonal[i] = 1 / referenceNeutral[i, 0];
            }

            return GetForward(weight).Multiply(DngMatrix.Diagonal(diagonal)).Multiply(inverseAnalogCalibration);
        }

        // CameraToXYZ = Inverse(XYZtoCamera) (pseudo-inverse with more than three planes), then CA from the white to D50
        var cameraToXyz = GetXyzToCamera(weight).TryPseudoInvert() ?? throw DngFormat.Invalid("The color matrix of the DNG file cannot be inverted.");
        var adaptation = GetBradfordAdaptation(ToXyz(white.X, white.Y), ToXyz(D50x, D50y));
        var result = adaptation.Multiply(cameraToXyz);

        // The neutral of the camera, whose largest component is 1, is the white of the connection space (Y = 1)
        var neutralXyz = result.Multiply(DngMatrix.Column(neutral));
        if (!(neutralXyz[1, 0] > 0))
            throw DngFormat.Invalid("The color matrix of the DNG file maps its camera neutral to a color without luminance.");

        return result.Scale(1 / neutralXyz[1, 0]);
    }

    /// <summary>Gets the matrix from XYZ (D50) to linear BT.709 RGB (D65): the Bradford adaptation from D50 to D65, then the inverse of the colorants of BT.709.</summary>
    public static DngMatrix GetXyzD50ToOutput()
    {
        var d65 = ToXyz(0.3127, 0.3290);
        var rgbToXyz = GetRgbToXyz(0.64, 0.33, 0.30, 0.60, 0.15, 0.06, d65);
        var xyzToRgb = rgbToXyz.TryInvert()!;
        return xyzToRgb.Multiply(GetBradfordAdaptation(ToXyz(D50x, D50y), d65));
    }

    /// <summary>Gets the CIE XYZ of a chromaticity, with Y = 1.</summary>
    public static double[] ToXyz(double x, double y) => [x / y, 1, (1 - x - y) / y];

    /// <summary>Gets the linear Bradford adaptation of XYZ from a white to another one.</summary>
    public static DngMatrix GetBradfordAdaptation(ReadOnlySpan<double> source, ReadOnlySpan<double> destination)
    {
        var sourceCone = Bradford.Multiply(DngMatrix.Column(source));
        var destinationCone = Bradford.Multiply(DngMatrix.Column(destination));
        var scale = DngMatrix.Diagonal([destinationCone[0, 0] / sourceCone[0, 0], destinationCone[1, 0] / sourceCone[1, 0], destinationCone[2, 0] / sourceCone[2, 0]]);
        return Bradford.TryInvert()!.Multiply(scale).Multiply(Bradford);
    }

    /// <summary>Gets the correlated color temperature of a chromaticity, in kelvins, by the method of Robertson.</summary>
    /// <returns>The temperature, between the ends of the table (1,667 K, and 1E6 K for the line of 0 reciprocal megakelvins).</returns>
    public static double GetTemperature(double x, double y)
    {
        // CIE 1960 UCS
        var denominator = (-2 * x) + (12 * y) + 3;
        var u = 4 * x / denominator;
        var v = 6 * y / denominator;
        var previous = 0.0;
        var first = 0.0;
        for (var i = 0; i < RobertsonTable.Length; i++)
        {
            var (mired, tu, tv, slope) = RobertsonTable[i];
            var distance = ((v - tv) - (slope * (u - tu))) / Math.Sqrt(1 + (slope * slope));
            if (i == 0)
            {
                first = distance;
            }
            else if (previous * distance <= 0)
            {
                // The chromaticity is between two isotemperature lines: interpolate their reciprocal temperatures
                var fraction = previous == distance ? 0 : previous / (previous - distance);
                var previousMired = RobertsonTable[i - 1].Mired;
                var result = previousMired + (fraction * (mired - previousMired));
                return result <= 1 ? 1e6 : 1e6 / result;
            }

            previous = distance;
        }

        // Outside the table: the end it is closer to
        return Math.Abs(first) < Math.Abs(previous) ? 1e6 : 1e6 / RobertsonTable[^1].Mired;
    }

    /// <summary>Gets the nominal correlated color temperature of an EXIF light source, or <see langword="null"/> for an unknown one.</summary>
    public static double? GetIlluminantTemperature(int lightSource) => lightSource switch
    {
        1 or 4 or 9 or 20 => 5503, // daylight, flash, fine weather: D55
        2 or 14 => 4200, // fluorescent, cool white fluorescent (W, 3900 to 4500 K)
        3 or 17 => 2856, // tungsten, standard light A
        10 or 21 => 6504, // cloudy weather: D65
        11 or 22 => 7504, // shade: D75
        12 => 6400, // daylight fluorescent (D, 5700 to 7100 K)
        13 => 5000, // day white fluorescent (N, 4600 to 5400 K)
        15 => 3450, // white fluorescent (WW, 3200 to 3700 K)
        16 => 2925, // warm white fluorescent (L, 2600 to 3250 K)
        18 => 4874, // standard light B
        19 => 6774, // standard light C
        23 => 5003, // D50
        24 => 3200, // ISO studio tungsten
        _ => null,
    };

    private static Calibration? ReadCalibration(TiffDirectory ifd0, int planes, int number, ushort illuminantTag, ushort colorMatrixTag, ushort forwardMatrixTag, ushort cameraCalibrationTag)
    {
        var suffix = number.ToString(CultureInfo.InvariantCulture);
        if (DngTagReader.TryReadExactNumbers(ifd0, colorMatrixTag, "ColorMatrix" + suffix, planes * 3) is not { } colorMatrix)
            return null;

        var illuminant = (int)(DngTagReader.TryReadNumber(ifd0, illuminantTag, "CalibrationIlluminant" + suffix) ?? (number == 1 ? 0 : -1));
        if (illuminant == 255)
            throw DngFormat.Unsupported("The camera profile of the DNG file calibrates a custom illuminant (CalibrationIlluminant 255, DNG 1.6), which is not supported.", "CalibrationIlluminant: 255");

        // The second calibration needs its illuminant to be interpolated with the first one
        if (number == 2 && illuminant < 0)
            return null;

        var forward = DngTagReader.TryReadExactNumbers(ifd0, forwardMatrixTag, "ForwardMatrix" + suffix, 3 * planes);
        var calibration = DngTagReader.TryReadExactNumbers(ifd0, cameraCalibrationTag, "CameraCalibration" + suffix, planes * planes);
        return new Calibration(illuminant, new DngMatrix(planes, 3, colorMatrix), forward is null ? null : new DngMatrix(3, planes, forward), calibration is null ? null : new DngMatrix(planes, planes, calibration));
    }

    internal static DngMatrix GetRgbToXyz(double rx, double ry, double gx, double gy, double bx, double by, double[] white)
    {
        var primaries = new DngMatrix(3, 3, [rx / ry, gx / gy, bx / by, 1, 1, 1, (1 - rx - ry) / ry, (1 - gx - gy) / gy, (1 - bx - by) / by]);
        var scale = primaries.TryInvert()!.Multiply(DngMatrix.Column(white));
        return primaries.Multiply(DngMatrix.Diagonal([scale[0, 0], scale[1, 0], scale[2, 0]]));
    }

    /// <summary>Finds the chromaticity of the white whose camera neutral is <paramref name="neutral"/> (chapter 6, the iteration).</summary>
    private (double X, double Y) FindWhite(double[] neutral)
    {
        var white = (X: D50x, Y: D50y);
        for (var iteration = 0; iteration < 30; iteration++)
        {
            var cameraToXyz = GetXyzToCamera(GetWeight(white)).TryPseudoInvert() ?? throw DngFormat.Invalid("The color matrix of the DNG file cannot be inverted.");
            var xyz = cameraToXyz.Multiply(DngMatrix.Column(neutral));
            var sum = xyz[0, 0] + xyz[1, 0] + xyz[2, 0];
            if (!(sum > 0) || !(xyz[1, 0] > 0))
                throw DngFormat.Invalid("The AsShotNeutral of the DNG file is not the neutral of a color.");

            var next = (X: xyz[0, 0] / sum, Y: xyz[1, 0] / sum);
            if (!(next.X > 0 && next.Y > 0 && next.X + next.Y < 1))
                throw DngFormat.Invalid("The AsShotNeutral of the DNG file is not the neutral of a color.");

            var converged = Math.Abs(next.X - white.X) + Math.Abs(next.Y - white.Y) < 1e-12;
            white = next;
            if (converged)
                break;
        }

        return white;
    }

    /// <summary>Gets the weight of the first calibration for a white (1 with a single calibration).</summary>
    private double GetWeight((double X, double Y) white)
    {
        if (_calibrations.Length < 2)
            return 1;

        var t1 = GetIlluminantTemperature(_calibrations[0].Illuminant);
        var t2 = GetIlluminantTemperature(_calibrations[1].Illuminant);
        if (t1 is not { } temperature1 || t2 is not { } temperature2 || temperature1 == temperature2)
            return 1;

        var temperature = GetTemperature(white.X, white.Y);
        var (low, high) = temperature1 < temperature2 ? (temperature1, temperature2) : (temperature2, temperature1);
        double weightOfLow;
        if (temperature <= low)
        {
            weightOfLow = 1;
        }
        else if (temperature >= high)
        {
            weightOfLow = 0;
        }
        else
        {
            // Linear in inverse temperature
            weightOfLow = ((1 / temperature) - (1 / high)) / ((1 / low) - (1 / high));
        }

        return temperature1 < temperature2 ? weightOfLow : 1 - weightOfLow;
    }

    private DngMatrix GetXyzToCamera(double weight)
        => DngMatrix.Diagonal(_analogBalance).Multiply(GetCameraCalibration(weight)).Multiply(Interpolate(weight, calibration => calibration.ColorMatrix));

    private DngMatrix GetCameraCalibration(double weight)
        => _useCameraCalibration ? Interpolate(weight, calibration => calibration.CameraCalibration ?? DngMatrix.Identity(Planes)) : DngMatrix.Identity(Planes);

    private DngMatrix GetForward(double weight) => Interpolate(weight, calibration => calibration.Forward!);

    private DngMatrix Interpolate(double weight, Func<Calibration, DngMatrix> select)
        => _calibrations.Length == 1 || weight >= 1 ? select(_calibrations[0]) : weight <= 0 ? select(_calibrations[1]) : select(_calibrations[0]).Interpolate(select(_calibrations[1]), weight);

    // Robertson (1968): reciprocal megakelvins, u, v and the slope of the isotemperature line
    private static readonly (double Mired, double U, double V, double Slope)[] RobertsonTable =
    [
        (0, 0.18006, 0.26352, -0.24341),
        (10, 0.18066, 0.26589, -0.25479),
        (20, 0.18133, 0.26846, -0.26876),
        (30, 0.18208, 0.27119, -0.28539),
        (40, 0.18293, 0.27407, -0.30470),
        (50, 0.18388, 0.27709, -0.32675),
        (60, 0.18494, 0.28021, -0.35156),
        (70, 0.18611, 0.28342, -0.37915),
        (80, 0.18740, 0.28668, -0.40955),
        (90, 0.18880, 0.28997, -0.44278),
        (100, 0.19032, 0.29326, -0.47888),
        (125, 0.19462, 0.30141, -0.58204),
        (150, 0.19962, 0.30921, -0.70471),
        (175, 0.20525, 0.31647, -0.84901),
        (200, 0.21142, 0.32312, -1.0182),
        (225, 0.21807, 0.32909, -1.2168),
        (250, 0.22511, 0.33439, -1.4512),
        (275, 0.23247, 0.33904, -1.7298),
        (300, 0.24010, 0.34308, -2.0637),
        (325, 0.24792, 0.34655, -2.4681),
        (350, 0.25591, 0.34951, -2.9641),
        (375, 0.26400, 0.35200, -3.5814),
        (400, 0.27218, 0.35407, -4.3633),
        (425, 0.28039, 0.35577, -5.3762),
        (450, 0.28863, 0.35714, -6.7262),
        (475, 0.29685, 0.35823, -8.5955),
        (500, 0.30505, 0.35907, -11.324),
        (525, 0.31320, 0.35968, -15.628),
        (550, 0.32129, 0.36011, -23.325),
        (575, 0.32931, 0.36038, -40.770),
        (600, 0.33724, 0.36051, -116.45),
    ];

    private sealed record Calibration(int Illuminant, DngMatrix ColorMatrix, DngMatrix? Forward, DngMatrix? CameraCalibration);
}
