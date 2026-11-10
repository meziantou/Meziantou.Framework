namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>
/// The trigonometric functions of fdlibm (http://www.netlib.org/fdlibm, see THIRD-PARTY-NOTICES.md), as V8 ports them.
/// The minimization of the layout amplifies the last bits of these functions, which differ between the C runtimes that
/// <see cref="Math"/> calls: these give the same layout on every platform, and the one of Mermaid in V8.
/// </summary>
internal static class VennMath
{
    private const double PiO2Hi = 1.57079632679489655800e+00;
    private const double PiO2Lo = 6.12323399573676603587e-17;
    private const double Pi = 3.14159265358979311600e+00;

    private static readonly double[] AtanHi = [4.63647609000806093515e-01, 7.85398163397448278999e-01, 9.82793723247329054082e-01, 1.57079632679489655800e+00];
    private static readonly double[] AtanLo = [2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17];

    private static readonly double[] AT =
    [
        3.33333333333329318027e-01, -1.99999999998764832476e-01, 1.42857142725034663711e-01, -1.11111104054623557880e-01,
        9.09088713343650656196e-02, -7.69187620504482999495e-02, 6.66107313738753120669e-02, -5.83357013379057348645e-02,
        4.97687799461593236017e-02, -3.65315727442169155270e-02, 1.62858201153657823623e-02,
    ];

    private static readonly int[] NPiO2Hw =
    [
        0x3FF921FB, 0x400921FB, 0x4012D97C, 0x401921FB, 0x401F6A7A, 0x4022D97C, 0x4025FDBB, 0x402921FB, 0x402C463A, 0x402F6A7A,
        0x4031475C, 0x4032D97C, 0x40346B9C, 0x4035FDBB, 0x40378FDB, 0x403921FB, 0x403AB41B, 0x403C463A, 0x403DD85A, 0x403F6A7A,
        0x40407E4C, 0x4041475C, 0x4042106C, 0x4042D97C, 0x4043A28C, 0x40446B9C, 0x404534AC, 0x4045FDBB, 0x4046C6CB, 0x40478FDB,
        0x404858EB, 0x404921FB,
    ];

    public static double Acos(double x)
    {
        const double PS0 = 1.66666666666666657415e-01;
        const double PS1 = -3.25565818622400915405e-01;
        const double PS2 = 2.01212532134862925881e-01;
        const double PS3 = -4.00555345006794114027e-02;
        const double PS4 = 7.91534994289814532176e-04;
        const double PS5 = 3.47933107596021167570e-05;
        const double QS1 = -2.40339491173441421878e+00;
        const double QS2 = 2.02094576023350569471e+00;
        const double QS3 = -6.88283971605453293030e-01;
        const double QS4 = 7.70381505559019352791e-02;

        var hx = HighWord(x);
        var ix = hx & 0x7FFFFFFF;
        if (ix >= 0x3FF00000)
        {
            // |x| >= 1
            if (((ix - 0x3FF00000) | LowWord(x)) == 0)
                return hx > 0 ? 0.0 : Pi + (2.0 * PiO2Lo);

            return double.NaN;
        }

        double z, p, q, r;
        if (ix < 0x3FE00000)
        {
            // |x| < 0.5
            if (ix <= 0x3C600000)
                return PiO2Hi + PiO2Lo;

            z = x * x;
            p = z * (PS0 + (z * (PS1 + (z * (PS2 + (z * (PS3 + (z * (PS4 + (z * PS5))))))))));
            q = 1.0 + (z * (QS1 + (z * (QS2 + (z * (QS3 + (z * QS4)))))));
            r = p / q;
            return PiO2Hi - (x - (PiO2Lo - (x * r)));
        }

        if (hx < 0)
        {
            // x < -0.5
            z = (1.0 + x) * 0.5;
            p = z * (PS0 + (z * (PS1 + (z * (PS2 + (z * (PS3 + (z * (PS4 + (z * PS5))))))))));
            q = 1.0 + (z * (QS1 + (z * (QS2 + (z * (QS3 + (z * QS4)))))));
            var s = Math.Sqrt(z);
            r = p / q;
            var w = (r * s) - PiO2Lo;
            return Pi - (2.0 * (s + w));
        }
        else
        {
            // x > 0.5
            z = (1.0 - x) * 0.5;
            var s = Math.Sqrt(z);
            var df = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(s) & unchecked((long)0xFFFFFFFF00000000));
            var c = (z - (df * df)) / (s + df);
            p = z * (PS0 + (z * (PS1 + (z * (PS2 + (z * (PS3 + (z * (PS4 + (z * PS5))))))))));
            q = 1.0 + (z * (QS1 + (z * (QS2 + (z * (QS3 + (z * QS4)))))));
            r = p / q;
            var w = (r * s) + c;
            return 2.0 * (df + w);
        }
    }

    public static double Atan(double x)
    {
        var hx = HighWord(x);
        var ix = hx & 0x7FFFFFFF;
        if (ix >= 0x44100000)
        {
            // |x| >= 2^66
            if (double.IsNaN(x))
                return x + x;

            return hx > 0 ? AtanHi[3] + AtanLo[3] : -AtanHi[3] - AtanLo[3];
        }

        int id;
        if (ix < 0x3FDC0000)
        {
            // |x| < 0.4375
            if (ix < 0x3E400000)
                return x;

            id = -1;
        }
        else
        {
            x = Math.Abs(x);
            if (ix < 0x3FF30000)
            {
                if (ix < 0x3FE60000)
                {
                    id = 0;
                    x = ((2.0 * x) - 1.0) / (2.0 + x);
                }
                else
                {
                    id = 1;
                    x = (x - 1.0) / (x + 1.0);
                }
            }
            else if (ix < 0x40038000)
            {
                id = 2;
                x = (x - 1.5) / (1.0 + (1.5 * x));
            }
            else
            {
                id = 3;
                x = -1.0 / x;
            }
        }

        var z = x * x;
        var w = z * z;
        var s1 = z * (AT[0] + (w * (AT[2] + (w * (AT[4] + (w * (AT[6] + (w * (AT[8] + (w * AT[10]))))))))));
        var s2 = w * (AT[1] + (w * (AT[3] + (w * (AT[5] + (w * (AT[7] + (w * AT[9]))))))));
        if (id < 0)
            return x - (x * (s1 + s2));

        z = AtanHi[id] - (((x * (s1 + s2)) - AtanLo[id]) - x);
        return hx < 0 ? -z : z;
    }

    public static double Atan2(double y, double x)
    {
        const double PiO4 = 7.8539816339744827900E-01;
        const double PiO2 = 1.5707963267948965580E+00;
        const double PiLo = 1.2246467991473531772E-16;

        if (double.IsNaN(x) || double.IsNaN(y))
            return x + y;

        if (x == 1.0)
            return Atan(y);

        var hx = HighWord(x);
        var ix = hx & 0x7FFFFFFF;
        var lx = LowWord(x);
        var hy = HighWord(y);
        var iy = hy & 0x7FFFFFFF;
        var ly = LowWord(y);
        var m = ((hy >> 31) & 1) | ((hx >> 30) & 2);

        // y = 0
        if ((iy | ly) == 0)
        {
            return m switch
            {
                0 or 1 => y,
                2 => Pi,
                _ => -Pi,
            };
        }

        // x = 0
        if ((ix | lx) == 0)
            return hy < 0 ? -PiO2 : PiO2;

        // x is infinite
        if (ix == 0x7FF00000)
        {
            if (iy == 0x7FF00000)
            {
                return m switch
                {
                    0 => PiO4,
                    1 => -PiO4,
                    2 => 3.0 * PiO4,
                    _ => -3.0 * PiO4,
                };
            }

            return m switch
            {
                0 => 0.0,
                1 => -0.0,
                2 => Pi,
                _ => -Pi,
            };
        }

        // y is infinite
        if (iy == 0x7FF00000)
            return hy < 0 ? -PiO2 : PiO2;

        double z;
        var k = (iy - ix) >> 20;
        if (k > 60)
        {
            // |y/x| > 2^60
            z = PiO2 + (0.5 * PiLo);
            m &= 1;
        }
        else if (hx < 0 && k < -60)
        {
            z = 0.0;
        }
        else
        {
            z = Atan(Math.Abs(y / x));
        }

        return m switch
        {
            0 => z,
            1 => -z,
            2 => Pi - (z - PiLo),
            _ => (z - PiLo) - Pi,
        };
    }

    public static double Sin(double x)
    {
        var ix = HighWord(x) & 0x7FFFFFFF;
        if (ix <= 0x3FE921FB)
            return KernelSin(x, 0, 0);

        if (ix >= 0x7FF00000)
            return x - x;

        var n = RemPiO2(x, out var y0, out var y1);
        return (n & 3) switch
        {
            0 => KernelSin(y0, y1, 1),
            1 => KernelCos(y0, y1),
            2 => -KernelSin(y0, y1, 1),
            _ => -KernelCos(y0, y1),
        };
    }

    public static double Cos(double x)
    {
        var ix = HighWord(x) & 0x7FFFFFFF;
        if (ix <= 0x3FE921FB)
            return KernelCos(x, 0);

        if (ix >= 0x7FF00000)
            return x - x;

        var n = RemPiO2(x, out var y0, out var y1);
        return (n & 3) switch
        {
            0 => KernelCos(y0, y1),
            1 => -KernelSin(y0, y1, 1),
            2 => -KernelCos(y0, y1),
            _ => KernelSin(y0, y1, 1),
        };
    }

    private static double KernelCos(double x, double y)
    {
        const double C1 = 4.16666666666666019037e-02;
        const double C2 = -1.38888888888741095749e-03;
        const double C3 = 2.48015872894767294178e-05;
        const double C4 = -2.75573143513906633035e-07;
        const double C5 = 2.08757232129817482790e-09;
        const double C6 = -1.13596475577881948265e-11;

        var ix = HighWord(x) & 0x7FFFFFFF;
        if (ix < 0x3E400000 && (int)x == 0)
            return 1.0;

        var z = x * x;
        var r = z * (C1 + (z * (C2 + (z * (C3 + (z * (C4 + (z * (C5 + (z * C6))))))))));
        if (ix < 0x3FD33333)
            return 1.0 - ((0.5 * z) - ((z * r) - (x * y)));

        var qx = ix > 0x3FE90000 ? 0.28125 : BitConverter.Int64BitsToDouble((long)(ix - 0x00200000) << 32);
        var iz = (0.5 * z) - qx;
        var a = 1.0 - qx;
        return a - (iz - ((z * r) - (x * y)));
    }

    private static double KernelSin(double x, double y, int iy)
    {
        const double S1 = -1.66666666666666324348e-01;
        const double S2 = 8.33333333332248946124e-03;
        const double S3 = -1.98412698298579493134e-04;
        const double S4 = 2.75573137070700676789e-06;
        const double S5 = -2.50507602534068634195e-08;
        const double S6 = 1.58969099521155010221e-10;

        var ix = HighWord(x) & 0x7FFFFFFF;
        if (ix < 0x3E400000 && (int)x == 0)
            return x;

        var z = x * x;
        var v = z * x;
        var r = S2 + (z * (S3 + (z * (S4 + (z * (S5 + (z * S6)))))));
        if (iy == 0)
            return x + (v * (S1 + (z * r)));

        return x - (((z * ((0.5 * y) - (v * r))) - y) - (v * S1));
    }

    /// <summary>Returns the remainder of x divided by pi/2, in two parts, and the quotient; the arguments of more than 2^19 pi/2, which the layout never has, use the runtime.</summary>
    private static int RemPiO2(double x, out double y0, out double y1)
    {
        const double InvPiO2 = 6.36619772367581382433e-01;
        const double PiO21 = 1.57079632673412561417e+00;
        const double PiO21T = 6.07710050650619224932e-11;
        const double PiO22 = 6.07710050630396597660e-11;
        const double PiO22T = 2.02226624879595063154e-21;
        const double PiO23 = 2.02226624871116645580e-21;
        const double PiO23T = 8.47842766036889956997e-32;

        var hx = HighWord(x);
        var ix = hx & 0x7FFFFFFF;
        if (ix <= 0x3FE921FB)
        {
            y0 = x;
            y1 = 0;
            return 0;
        }

        double z;
        if (ix < 0x4002D97C)
        {
            // |x| < 3pi/4
            if (hx > 0)
            {
                z = x - PiO21;
                if (ix != 0x3FF921FB)
                {
                    y0 = z - PiO21T;
                    y1 = (z - y0) - PiO21T;
                }
                else
                {
                    z -= PiO22;
                    y0 = z - PiO22T;
                    y1 = (z - y0) - PiO22T;
                }

                return 1;
            }

            z = x + PiO21;
            if (ix != 0x3FF921FB)
            {
                y0 = z + PiO21T;
                y1 = (z - y0) + PiO21T;
            }
            else
            {
                z += PiO22;
                y0 = z + PiO22T;
                y1 = (z - y0) + PiO22T;
            }

            return -1;
        }

        if (ix <= 0x413921FB)
        {
            // |x| <= 2^19 pi/2
            var t = Math.Abs(x);
            var n = (int)((t * InvPiO2) + 0.5);
            var fn = (double)n;
            var r = t - (fn * PiO21);
            var w = fn * PiO21T;
            if (n < 32 && ix != NPiO2Hw[n - 1])
            {
                y0 = r - w;
            }
            else
            {
                var j = ix >> 20;
                y0 = r - w;
                var i = j - ((HighWord(y0) >> 20) & 0x7FF);
                if (i > 16)
                {
                    t = r;
                    w = fn * PiO22;
                    r = t - w;
                    w = (fn * PiO22T) - ((t - r) - w);
                    y0 = r - w;
                    i = j - ((HighWord(y0) >> 20) & 0x7FF);
                    if (i > 49)
                    {
                        t = r;
                        w = fn * PiO23;
                        r = t - w;
                        w = (fn * PiO23T) - ((t - r) - w);
                        y0 = r - w;
                    }
                }
            }

            y1 = (r - y0) - w;
            if (hx < 0)
            {
                y0 = -y0;
                y1 = -y1;
                return -n;
            }

            return n;
        }

        // A large argument
        var remainder = Math.IEEERemainder(x, Math.PI / 2);
        y0 = remainder;
        y1 = 0;
        return (int)Math.Round((x - remainder) / (Math.PI / 2), MidpointRounding.ToEven) & 3;
    }

    private static int HighWord(double x) => (int)(BitConverter.DoubleToInt64Bits(x) >> 32);

    private static int LowWord(double x) => (int)(BitConverter.DoubleToInt64Bits(x) & 0xFFFFFFFF);
}
