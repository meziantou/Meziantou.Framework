namespace Meziantou.Framework.Imaging.TestHarness.Color;

/// <summary>
/// Black point estimation in <see cref="decimal"/> arithmetic, transcribed from "Adobe Systems' Implementation of Black
/// Point Compensation" (2006), sections 7.1 and 7.2, with the choices documented by the library: the black point of a
/// table-based destination is where the parabola fitted to the shadow section of the round trip rises through the
/// lightness of the round trip of black (its vertex when it never reaches it); lightness is kept in [0, 50]; there is no
/// estimate when a table-based destination has no conversion to the connection space; the initial estimate is used when
/// the fit is degenerate.
/// </summary>
internal static class ReferenceIccBlackPoint
{
    /// <summary>Section 7.1: the CIELAB color of the device black; a CMYK black is made neutral and L* is limited to 50.</summary>
    public static decimal[] GetBlackPoint(ReferenceIccProfile profile, int intent)
    {
        decimal[] device;
        if (profile.IsCmykOutputProfile && profile.UsesLookupTable(deviceToConnection: false, intent: 0))
        {
            device = profile.LabToDevice([0, 0, 0], intent: 0);
        }
        else
        {
            device = profile.ChannelCount == 4 ? [1, 1, 1, 1] : new decimal[profile.ChannelCount];
        }

        var lab = profile.DeviceToLab(device, intent);
        if (profile.ChannelCount == 4)
        {
            lab[1] = 0;
            lab[2] = 0;
        }

        lab[0] = Math.Clamp(lab[0], 0, 50);
        return lab;
    }

    /// <summary>Section 7.2: the lightness of the black point of a destination, or <see langword="null"/> when it cannot be estimated.</summary>
    public static decimal? GetDestinationLightness(ReferenceIccProfile profile, int intent)
    {
        if (!profile.HasConversion(deviceToConnection: true, intent))
            return null;

        if (!profile.UsesLookupTable(deviceToConnection: false, intent))
            return GetBlackPoint(profile, intent)[0];

        decimal[] initial = intent == 1 ? GetBlackPoint(profile, intent) : [0, 0, 0];

        // The L* of the round trip of each L* from 0 to 100: to the device with the intent, back colorimetrically
        var ramp = new decimal[101];
        for (var lightness = 0; lightness <= 100; lightness++)
        {
            ramp[lightness] = profile.DeviceToLab(profile.LabToDevice([lightness, initial[1], initial[2]], intent), intent: 1)[0];
        }

        var black = ramp[0];
        var white = ramp[100];
        if (intent == 1 && Enumerable.Range(0, 101).All(lightness => ramp[lightness] <= black + (0.2m * (white - black)) || Math.Abs(ramp[lightness] - lightness) <= 4))
            return initial[0];

        if (white == black)
            return initial[0];

        // The shadow section, scaled so that the black is 0 and the white is 1
        var (low, high) = intent == 1 ? (0.1m, 0.5m) : (0.03m, 0.25m);
        var points = new List<(decimal X, decimal Y)>();
        for (var lightness = 0; lightness <= 100; lightness++)
        {
            var y = (ramp[lightness] - black) / (white - black);
            if (y >= low && y < high)
            {
                points.Add((lightness, y));
            }
        }

        if (points.Count < 3)
            return initial[0];

        // Least squares parabola y = t x^2 + u x + c by Gaussian elimination of the normal equations
        var rows = new decimal[3][];
        for (var row = 0; row < 3; row++)
        {
            rows[row] = new decimal[4];
            for (var column = 0; column < 3; column++)
            {
                rows[row][column] = points.Sum(point => Power(point.X, 4 - row - column));
            }

            rows[row][3] = points.Sum(point => Power(point.X, 2 - row) * point.Y);
        }

        for (var pivot = 0; pivot < 3; pivot++)
        {
            var best = Enumerable.Range(pivot, 3 - pivot).MaxBy(row => Math.Abs(rows[row][pivot]));
            (rows[pivot], rows[best]) = (rows[best], rows[pivot]);
            if (rows[pivot][pivot] == 0)
                return initial[0];

            for (var row = 0; row < 3; row++)
            {
                if (row == pivot)
                    continue;

                var factor = rows[row][pivot] / rows[pivot][pivot];
                for (var column = pivot; column < 4; column++)
                {
                    rows[row][column] -= factor * rows[pivot][column];
                }
            }
        }

        var t = rows[0][3] / rows[0][0];
        var u = rows[1][3] / rows[1][1];
        var c = rows[2][3] / rows[2][2];

        // Where the parabola rises through y = 0: its slope there is sqrt(u^2 - 4 t c), positive, so the root is
        // (-u + sqrt(...)) / 2t for either sign of t; a straight line (t = 0) crosses at -c / u when it rises
        var discriminant = (u * u) - (4 * t * c);
        if (t == 0)
            return u > 0 ? Math.Clamp(-c / u, 0, 50) : initial[0];

        if (discriminant >= 0)
        {
            // Only when the curve rises at the middle of the shadow section (x = mean of the points, where the slope is
            // 2 t x + u): the library fits around that point and requires a positive slope there
            var mean = points.Sum(point => point.X) / points.Count;
            return (2 * t * mean) + u > 0 ? Math.Clamp((-u + SquareRoot(discriminant)) / (2 * t), 0, 50) : initial[0];
        }

        // No intersection: the vertex of a parabola that has a minimum
        return t > 0 ? Math.Clamp(-u / (2 * t), 0, 50) : initial[0];
    }

    /// <summary>Section 7.3, "DecodeL": the luminance of a lightness.</summary>
    public static decimal DecodeLightness(decimal lightness)
    {
        if (lightness < 0)
            return -DecodeLightness(-lightness);

        if (lightness <= 8)
            return lightness * (24m / 116) * (24m / 116) * (24m / 116) / 8;

        var value = (lightness + 16) / 116;
        return value * value * value;
    }

    /// <summary>The square root by Newton's iteration.</summary>
    private static decimal SquareRoot(decimal value)
    {
        if (value == 0)
            return 0;

        var root = (decimal)Math.Sqrt((double)value);
        for (var i = 0; i < 8; i++)
        {
            root = (root + (value / root)) / 2;
        }

        return root;
    }

    private static decimal Power(decimal value, int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= value;
        }

        return result;
    }
}
