using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Black point compensation between two ICC profiles: the darkest color of the source is mapped to the darkest color of
/// the destination by a scaling of CIEXYZ toward the white point, so that shadow detail is neither clipped nor lifted.
/// </summary>
/// <remarks>
/// <para>
/// Written from "Adobe Systems' Implementation of Black Point Compensation" (Adobe Systems, 2006), the public
/// description of the algorithm standardized as ISO 18619: section 7.1 (source black point), 7.2 (destination black
/// point) and 7.3 (mapping). Only the lightness L* of the two black points is used.
/// </para>
/// <para>
/// The destination black point of a table-based profile is read from the round trip of an L* ramp through the profile:
/// a parabola is fitted to the shadow section of the round trip, scaled so that the lightness of the round trip of
/// black is 0, and the black point is where this parabola meets 0 while rising. That is the vertex of the parabola
/// when it is tangent to 0, the case the description illustrates and gives the formula of; using the intersection keeps
/// the estimate meaningful when the shadow section is nearly a straight line, where the vertex of the fit is arbitrary.
/// A parabola that stays above 0 has no intersection: its vertex is used.
/// </para>
/// <para>
/// Where the description leaves a case open, this implementation chooses as follows. A black point lightness is kept in
/// [0, 50]. When the destination uses lookup tables but cannot be converted back to the connection space, its black point
/// cannot be measured and no compensation is applied. When the fit is degenerate (fewer than three points, or a curve
/// that neither rises nor has a minimum), the black point is the initial estimate.
/// </para>
/// </remarks>
internal static class IccBlackPoint
{
    /// <summary>The number of intervals of the L* ramp converted through the destination profile (L* from 0 to 100 in steps of 1).</summary>
    private const int RampSteps = 100;

    /// <summary>Creates the compensation stage on CIEXYZ relative to D50.</summary>
    /// <returns>The stage, or <see langword="null"/> when the two black points are the same or cannot be estimated.</returns>
    public static IccStage? CreateCompensation(IccProfileModel source, IccProfileModel destination, IccRenderingIntent intent)
    {
        if (GetDestinationLightness(destination, intent) is not { } destinationLightness)
            return null;

        var sourceBlack = DecodeLightness(GetBlackPoint(source, intent).L);
        var destinationBlack = DecodeLightness(destinationLightness);
        if (sourceBlack == destinationBlack)
            return null;

        // Section 7.3: in CIEXYZ divided by the white point, out = in * scale + (1 - scale)
        var scale = (1 - destinationBlack) / (1 - sourceBlack);
        var offset = 1 - scale;
        return IccMatrixStage.CreateScale(scale, scale, scale, offset * IccColorimetry.D50X, offset * IccColorimetry.D50Y, offset * IccColorimetry.D50Z);
    }

    /// <summary>
    /// Estimates the black point of a profile from its conversion to the connection space (section 7.1): the color of the
    /// device black, or for a CMYK output profile the color of the ink combination its perceptual table gives for L* = 0.
    /// </summary>
    internal static (double L, double A, double B) GetBlackPoint(IccProfileModel profile, IccRenderingIntent intent)
    {
        Span<double> values = stackalloc double[IccPipeline.MaxChannels];
        if (profile.IsCmykOutputProfile && profile.UsesLookupTable(deviceToConnection: false, IccRenderingIntent.Perceptual))
        {
            values.Clear();
            Apply(profile.CreateLabToDevice(IccRenderingIntent.Perceptual), values);
        }
        else
        {
            // The value of black in the device space: no light, or all the inks
            values.Fill(profile.ChannelCount == 4 ? 1 : 0);
        }

        Apply(profile.CreateDeviceToLab(intent), values);
        var (lightness, a, b) = (values[0], values[1], values[2]);
        if (profile.ChannelCount == 4)
        {
            // CMYK profiles sometimes give a black that is not neutral
            (a, b) = (0, 0);
        }

        return (ClipLightness(lightness), a, b);
    }

    /// <summary>Estimates the lightness of the black point of a destination profile (section 7.2).</summary>
    /// <returns>The lightness, or <see langword="null"/> when it cannot be estimated.</returns>
    internal static double? GetDestinationLightness(IccProfileModel profile, IccRenderingIntent intent)
    {
        if (!profile.HasConversion(deviceToConnection: true, intent))
            return null;

        if (!profile.UsesLookupTable(deviceToConnection: false, intent))
            return GetBlackPoint(profile, intent).L;

        // Step 1: the initial estimate
        var initial = intent == IccRenderingIntent.RelativeColorimetric ? GetBlackPoint(profile, intent) : (L: 0, A: 0, B: 0);

        // Step 2: the round trip of an L* ramp through the destination, out with the intent and back colorimetrically
        var labToDevice = profile.CreateLabToDevice(intent);
        var deviceToLab = profile.CreateDeviceToLab(IccRenderingIntent.RelativeColorimetric);
        Span<double> ramp = stackalloc double[RampSteps + 1];
        Span<double> values = stackalloc double[IccPipeline.MaxChannels];
        for (var i = 0; i <= RampSteps; i++)
        {
            values[0] = i;
            values[1] = initial.A;
            values[2] = initial.B;
            Apply(labToDevice, values);
            Apply(deviceToLab, values);
            ramp[i] = values[0];
        }

        var minimum = ramp[0];
        var maximum = ramp[RampSteps];
        if (intent == IccRenderingIntent.RelativeColorimetric)
        {
            // Step 3: when the mid-range of the round trip is close to the identity, the initial estimate is good enough
            var nearlyStraight = true;
            for (var i = 0; i <= RampSteps; i++)
            {
                if (ramp[i] > minimum + (0.2 * (maximum - minimum)) && Math.Abs(ramp[i] - i) > 4)
                {
                    nearlyStraight = false;
                    break;
                }
            }

            if (nearlyStraight)
                return initial.L;
        }

        // Step 4: fit a parabola y = t x^2 + u x + c through the shadow section of the round trip, scaled to [0, 1]; the
        // black point is where the extrapolated curve meets the constant section, that is y = 0
        var (low, high) = intent == IccRenderingIntent.RelativeColorimetric ? (0.1, 0.5) : (0.03, 0.25);
        double count = 0;
        double sum = 0;
        for (var i = 0; i <= RampSteps; i++)
        {
            var y = (ramp[i] - minimum) / (maximum - minimum);
            if (y >= low && y < high)
            {
                count++;
                sum += i;
            }
        }

        if (count < 3)
            return initial.L;

        // The fit is done on x minus its mean: the normal equations are then well conditioned in double precision
        var mean = sum / count;
        double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, t0 = 0, t1 = 0, t2 = 0;
        for (var i = 0; i <= RampSteps; i++)
        {
            var y = (ramp[i] - minimum) / (maximum - minimum);
            if (!(y >= low && y < high))
                continue;

            var x = i - mean;
            s0++;
            s1 += x;
            s2 += x * x;
            s3 += x * x * x;
            s4 += x * x * x * x;
            t0 += y;
            t1 += x * y;
            t2 += x * x * y;
        }

        Span<double> inverse = stackalloc double[9];
        if (!IccMatrixStage.TryInvert([s4, s3, s2, s3, s2, s1, s2, s1, s0], inverse))
            return initial.L;

        var t = (inverse[0] * t2) + (inverse[1] * t1) + (inverse[2] * t0);
        var u = (inverse[3] * t2) + (inverse[4] * t1) + (inverse[5] * t0);
        var c = (inverse[6] * t2) + (inverse[7] * t1) + (inverse[8] * t0);

        // The root where the curve rises through 0 is (-u + sqrt(u^2 - 4 t c)) / 2t, written without the cancellation of
        // a small t (for a straight line it is -c / u); without a root, the vertex -u / 2t of a parabola that has a minimum
        var discriminant = (u * u) - (4 * t * c);
        double position;
        if (u > 0 && discriminant >= 0)
        {
            position = -2 * c / (u + Math.Sqrt(discriminant));
        }
        else if (t > 0 && discriminant < 0)
        {
            position = -u / (2 * t);
        }
        else
        {
            return initial.L;
        }

        return double.IsFinite(position) ? ClipLightness(mean + position) : initial.L;
    }

    /// <summary>Converts a lightness L* to the luminance Y relative to white (section 7.3, "DecodeL").</summary>
    internal static double DecodeLightness(double lightness)
    {
        if (lightness < 0)
            return -DecodeLightness(-lightness);

        var linear = (8.0 + 16) / 116;
        var cube = (lightness + 16) / 116;
        return lightness <= 8 ? lightness * (linear * linear * linear) / 8 : cube * cube * cube;
    }

    private static double ClipLightness(double lightness) => lightness >= 50 ? 50 : lightness > 0 ? lightness : 0;

    private static void Apply(IccStage[] stages, Span<double> values)
    {
        foreach (var stage in stages)
        {
            stage.Apply(values);
        }
    }
}
