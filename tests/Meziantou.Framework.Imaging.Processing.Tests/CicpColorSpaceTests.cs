using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Color;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The transfer characteristics and the color primaries of Rec. ITU-T H.273 (<see cref="CicpTransfer"/>,
/// <see cref="CicpPrimaries"/>). Every expected value is a literal computed outside the library, from the equations of
/// the recommendation evaluated with 40 decimal digits (Python <c>decimal</c>), and several are also published:
/// 100, 203 and 1,000 cd/m2 are 50.8 %, 58 % and 75.2 % of the PQ signal and the reference white of HLG is 75 % of its
/// signal (Rec. ITU-R BT.2100 and Report ITU-R BT.2408); the luma coefficients are those of Rec. ITU-R BT.709
/// (0.2126, 0.7152, 0.0722) and BT.2020 (0.2627, 0.6780, 0.0593).
/// </summary>
public sealed class CicpColorSpaceTests
{
    private const double Tolerance = 1e-12;

    // SMPTE ST 2084 encodes no light as c1^m2, not as 0: the value is below half a 16-bit step
    private const double PqOfBlack = 0.000000730955902578;

    [Theory]
    [InlineData(1, 0.05, 0.011111111111111111)] // the linear segment: V / 4.5
    [InlineData(1, 0.5, 0.259719437101177522)]
    [InlineData(1, 0.9, 0.809011173787285092)]
    [InlineData(4, 0.5, 0.217637640824031035)] // V^2.2
    [InlineData(5, 0.5, 0.143587294374629376)] // V^2.8
    [InlineData(7, 0.05, 0.0125)] // the linear segment: V / 4
    [InlineData(7, 0.5, 0.265035733578677137)]
    [InlineData(8, 0.5, 0.5)]
    [InlineData(9, 0.75, 0.316227766016837933)] // 10^((V - 1) * 2)
    [InlineData(10, 0.75, 0.237137370566165526)] // 10^((V - 1) * 2.5)
    [InlineData(13, 0.5, 0.214041140482232442)]
    [InlineData(16, 0.1, 0.000032456559146449)]
    [InlineData(16, 0.25, 0.000515417600983305)]
    [InlineData(16, 0.5, 0.009224570899406408)] // 92.2457 cd/m2
    [InlineData(16, 0.75, 0.098337785558709773)]
    [InlineData(17, 0.5, 0.179954763768688730)]
    [InlineData(17, 1, 1.091041666666666667)] // 52.37 / 48: the peak is above the reference white
    [InlineData(18, 0.25, 0.020833333333333333)] // the square segment: V^2 / 3
    [InlineData(18, 0.5, 0.083333333333333333)] // 1 / 12
    [InlineData(18, 0.75, 0.264962560421007179)] // the reference white of HLG
    [InlineData(18, 0.9, 0.581837590961869215)]
    public void DecodeIsTheInverseOfTheFunctionOfTheTable(byte code, double encoded, double expected)
    {
        Assert.Equal(expected, CicpTransfer.Decode(code, encoded), Tolerance);
    }

    [Theory]
    [InlineData(1, 0.01, 0.045)] // the linear segment: 4.5 L
    [InlineData(1, 0.18, 0.408848108891225099)]
    [InlineData(1, 0.5, 0.705435553055618310)]
    [InlineData(4, 0.5, 0.729740052840723098)]
    [InlineData(5, 0.5, 0.780709182155710092)]
    [InlineData(7, 0.01, 0.04)] // the linear segment: 4 L
    [InlineData(7, 0.5, 0.702165625521781328)]
    [InlineData(8, 0.5, 0.5)]
    [InlineData(9, 0.1, 0.5)] // 1 + log10(L) / 2
    [InlineData(9, 0.005, 0)] // below 0.01
    [InlineData(10, 0.1, 0.6)] // 1 + log10(L) / 2.5
    [InlineData(10, 0.003, 0)] // below sqrt(10) / 1000
    [InlineData(16, 0.0000005, 0.015076399042368021)] // 0.005 cd/m2
    [InlineData(16, 0.01, 0.508078421517394855)] // 100 cd/m2
    [InlineData(16, 0.0203, 0.580688881041607838)] // 203 cd/m2
    [InlineData(16, 0.1, 0.751827096247041773)] // 1,000 cd/m2
    [InlineData(16, 0.4, 0.902572393310940493)] // 4,000 cd/m2
    [InlineData(17, 0.5, 0.740738422347624768)]
    [InlineData(17, 1, 0.967042675317933542)] // the reference white of 48 cd/m2
    [InlineData(18, 0.01, 0.173205080756887729)] // the square root segment: sqrt(3 L)
    [InlineData(18, 0.25, 0.738549267595389371)]
    [InlineData(18, 0.5, 0.871643470874177183)]
    public void EncodeIsTheFunctionOfTheTable(byte code, double linear, double expected)
    {
        Assert.Equal(expected, CicpTransfer.Encode(code, linear), Tolerance);
    }

    [Fact]
    public void TheCodePointsOfTheBt709FunctionShareIt()
    {
        foreach (var code in new byte[] { 6, 11, 12, 14, 15 })
        {
            for (var i = 0; i <= 100; i++)
            {
                Assert.Equal(CicpTransfer.Decode(1, i / 100d), CicpTransfer.Decode(code, i / 100d));
                Assert.Equal(CicpTransfer.Encode(1, i / 100d), CicpTransfer.Encode(code, i / 100d));
            }

            Assert.True(CicpTransfer.GetDecode8(code) == CicpTransfer.GetDecode8(1));
            Assert.True(CicpTransfer.GetDecode16(code) == CicpTransfer.GetDecode16(1));
        }
    }

    [Fact]
    public void TheSrgbCodePointIsTheSrgbFunctionOfTheLibrary()
    {
        for (var i = 0; i <= 1000; i++)
        {
            Assert.Equal(SrgbTransfer.Decode(i / 1000d), CicpTransfer.Decode(13, i / 1000d));
            Assert.Equal(SrgbTransfer.Encode(i / 1000d), CicpTransfer.Encode(13, i / 1000d));
        }

        Assert.True(CicpTransfer.GetDecode8(13) == SrgbTransfer.Decode8);
        Assert.True(CicpTransfer.GetDecode16(13) == SrgbTransfer.Decode16);
    }

    [Fact]
    public void TheSrgbFunctionOfFiltersIsWithinAFewUnitsInTheLastPlaceOfTheExactFunction()
    {
        // The sRGB function in decimal arithmetic (about 27 digits) is the exact function. The inputs are every 8-bit
        // sample, every seventh 16-bit sample, and the start and the middle of every segment of the polynomials of
        // DeterministicSrgbTransfer, for each exponent of the base of the power. The junctions of the linear segments are
        // left out: the two parts of the function do not meet (by about 1e-8), and a double and a decimal threshold may
        // put a value on different sides
        var inputs = new List<double>();
        for (var sample = 0; sample <= ushort.MaxValue; sample += 7)
        {
            inputs.Add(sample / 65535.0);
        }

        for (var sample = 0; sample <= byte.MaxValue; sample++)
        {
            inputs.Add(sample / 255.0);
        }

        var segments = new List<double>();
        for (var exponent = -9; exponent < 0; exponent++)
        {
            for (var segment = 0; segment < 512; segment++)
            {
                segments.Add(Math.ScaleB(1 + (segment / 512.0), exponent));
            }
        }

        foreach (var linear in inputs.Concat(segments))
        {
            AssertWithinBound(linear, DeterministicSrgbTransfer.Encode(linear), ReferenceCicpTransfer.Encode(13, ToDecimal(linear)));
        }

        // The base of the power of decoding is (v + 0.055) / 1.055
        foreach (var encoded in inputs.Concat(segments.Where(x => x > 0.0905).Select(x => (x * 1.055) - 0.055)))
        {
            AssertWithinBound(encoded, DeterministicSrgbTransfer.Decode(encoded), ReferenceCicpTransfer.Decode(13, ToDecimal(encoded)));
        }

        // 1e-15 is a few units in the last place of a double (the transfer function with Math.Pow is within 5e-16), and
        // the result rounded to a float, as filters keep it, is the float nearest to the exact value
        static void AssertWithinBound(double input, double actual, decimal exact)
        {
            var error = Math.Abs((double)(ToDecimal(actual) - exact));
            Assert.True(error <= 1e-15, $"The error at {input.ToString("R", CultureInfo.InvariantCulture)} is {error.ToString("R", CultureInfo.InvariantCulture)}.");
            Assert.Equal(GetNearestFloat(exact), (float)actual);
        }

        static float GetNearestFloat(decimal value)
        {
            var guess = (float)(double)value;
            var nearest = guess;
            foreach (var candidate in (float[])[MathF.BitDecrement(guess), MathF.BitIncrement(guess)])
            {
                if (Math.Abs(ToDecimal(candidate) - value) < Math.Abs(ToDecimal(nearest) - value))
                {
                    nearest = candidate;
                }
            }

            return nearest;
        }

        // The value of a positive double below 2, to 28 decimal places: its significand divided by a power of two
        static decimal ToDecimal(double value)
        {
            if (value == 0)
                return 0;

            var bits = BitConverter.DoubleToInt64Bits(value);
            decimal result = (bits & ((1L << 52) - 1)) | (1L << 52);
            for (var shift = 1075 - (int)(bits >> 52); shift > 0; shift -= 62)
            {
                result /= 1L << Math.Min(shift, 62);
            }

            return result;
        }
    }

    [Fact]
    public void TheSrgbFunctionOfFiltersGivesTheSameBitsOnEveryPlatform()
    {
        // A hash of the bits of every 16-bit sample, encoded and decoded: the polynomials and their tables are built with
        // additions, multiplications and divisions only, so the value does not depend on the platform
        var hash = 0UL;
        for (var sample = 0; sample <= ushort.MaxValue; sample++)
        {
            var value = sample / 65535.0;
            hash = (hash * 31) + (ulong)BitConverter.DoubleToInt64Bits(DeterministicSrgbTransfer.Encode(value));
            hash = (hash * 31) + (ulong)BitConverter.DoubleToInt64Bits(DeterministicSrgbTransfer.Decode(value));
        }

        Assert.Equal(15625745378631040713UL, hash);
    }

    [Fact]
    public void EveryFunctionIsMonotonicAndRoundTrips()
    {
        for (var code = 0; code <= byte.MaxValue; code++)
        {
            if (!CicpTransfer.IsSupported((byte)code))
                continue;

            Assert.Equal(0, CicpTransfer.Decode((byte)code, 0));
            Assert.Equal(code is 16 ? PqOfBlack : 0, CicpTransfer.Encode((byte)code, 0), Tolerance);
            var previous = 0d;
            for (var i = 1; i <= 4096; i++)
            {
                var encoded = i / 4096d;
                var linear = CicpTransfer.Decode((byte)code, encoded);
                Assert.True(linear > previous, $"code {code}: decoding {encoded} gives {linear}, not above {previous}");
                Assert.Equal(encoded, CicpTransfer.Encode((byte)code, linear), 1e-9);
                previous = linear;
            }
        }
    }

    [Fact]
    public void ValuesOutOfRangeAreClipped()
    {
        foreach (var code in new byte[] { 1, 4, 13, 16, 17, 18 })
        {
            Assert.Equal(0, CicpTransfer.Decode(code, -0.5));
            Assert.Equal(0, CicpTransfer.Decode(code, double.NaN));
            Assert.Equal(CicpTransfer.Decode(code, 1), CicpTransfer.Decode(code, 1.5));
            Assert.Equal(CicpTransfer.Encode(code, 0), CicpTransfer.Encode(code, -0.5));
            Assert.Equal(CicpTransfer.Encode(code, 0), CicpTransfer.Encode(code, double.NaN));
            Assert.Equal(1, CicpTransfer.Encode(code, 100));
        }
    }

    [Fact]
    public void OnlyTheCodePointsOfTheTableAreSupported()
    {
        byte[] supported = [1, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18];
        for (var code = 0; code <= byte.MaxValue; code++)
        {
            Assert.Equal(supported.Contains((byte)code), CicpTransfer.IsSupported((byte)code));
            Assert.Equal(code is 16 or 18, CicpTransfer.IsHdr((byte)code));
        }

        // Unspecified (2) and reserved code points have no function
        foreach (var code in new byte[] { 0, 2, 3, 19, 255 })
        {
            Assert.Throws<ArgumentOutOfRangeException>("code", () => CicpTransfer.Decode(code, 0.5));
            Assert.Throws<ArgumentOutOfRangeException>("code", () => CicpTransfer.Encode(code, 0.5));
            Assert.Throws<ArgumentOutOfRangeException>("code", () => CicpTransfer.GetDecode8(code).Length);
            Assert.Throws<ArgumentOutOfRangeException>("code", () => CicpTransfer.GetDecode16(code).Length);
        }
    }

    [Fact]
    public void DecodeTablesHoldTheValueOfEverySample()
    {
        foreach (var code in new byte[] { 1, 4, 16, 18 })
        {
            var table8 = CicpTransfer.GetDecode8(code);
            Assert.HasCount(256, table8);
            for (var i = 0; i < table8.Length; i++)
            {
                Assert.Equal(CicpTransfer.Decode(code, i / 255d), table8[i]);
            }

            var table16 = CicpTransfer.GetDecode16(code);
            Assert.HasCount(65536, table16);
            foreach (var i in new[] { 0, 1, 257, 32768, 65534, 65535 })
            {
                Assert.Equal(CicpTransfer.Decode(code, i / 65535d), table16[i]);
            }

            // Built once
            Assert.True(table8 == CicpTransfer.GetDecode8(code));
            Assert.True(table16 == CicpTransfer.GetDecode16(code));
        }
    }

    [Theory]
    [InlineData(1, 0.640, 0.330, 0.300, 0.600, 0.150, 0.060, 0.3127, 0.3290)] // Rec. ITU-R BT.709
    [InlineData(4, 0.67, 0.33, 0.21, 0.71, 0.14, 0.08, 0.310, 0.316)] // Rec. ITU-R BT.470 System M
    [InlineData(5, 0.64, 0.33, 0.29, 0.60, 0.15, 0.06, 0.3127, 0.3290)] // Rec. ITU-R BT.470 System B, G
    [InlineData(6, 0.630, 0.340, 0.310, 0.595, 0.155, 0.070, 0.3127, 0.3290)] // Rec. ITU-R BT.601 525
    [InlineData(7, 0.630, 0.340, 0.310, 0.595, 0.155, 0.070, 0.3127, 0.3290)] // SMPTE 240M
    [InlineData(8, 0.681, 0.319, 0.243, 0.692, 0.145, 0.049, 0.310, 0.316)] // generic film
    [InlineData(9, 0.708, 0.292, 0.170, 0.797, 0.131, 0.046, 0.3127, 0.3290)] // Rec. ITU-R BT.2020
    [InlineData(11, 0.680, 0.320, 0.265, 0.690, 0.150, 0.060, 0.314, 0.351)] // SMPTE RP 431-2
    [InlineData(12, 0.680, 0.320, 0.265, 0.690, 0.150, 0.060, 0.3127, 0.3290)] // SMPTE EG 432-1
    [InlineData(22, 0.630, 0.340, 0.295, 0.605, 0.155, 0.077, 0.3127, 0.3290)] // EBU Tech. 3213-E
    public void PrimariesAreTheChromaticitiesOfTheTable(byte code, double redX, double redY, double greenX, double greenY, double blueX, double blueY, double whiteX, double whiteY)
    {
        Span<double> primaries = stackalloc double[6];
        Assert.True(CicpPrimaries.IsSupported(code));
        Assert.True(CicpPrimaries.TryGet(code, primaries, out var actualWhiteX, out var actualWhiteY));
        Assert.Equal([redX, redY, greenX, greenY, blueX, blueY], primaries.ToArray());
        Assert.Equal(whiteX, actualWhiteX);
        Assert.Equal(whiteY, actualWhiteY);

        // The integers are the same decimal numbers
        Span<int> integers = stackalloc int[8];
        Assert.True(CicpPrimaries.TryGetChromaticities(code, integers));
        Assert.Equal(100_000, CicpPrimaries.GetDenominator(code));
        double[] expected = [redX, redY, greenX, greenY, blueX, blueY, whiteX, whiteY];
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal((int)Math.Round(expected[i] * 100_000, MidpointRounding.AwayFromZero), integers[i]);
        }
    }

    [Fact]
    public void TheXyzPrimariesAreTheAxesOfTheColorSpaceWithTheEqualEnergyWhite()
    {
        // SMPTE ST 428-1: thirds have no decimal form, so the denominator is 3
        Span<int> integers = stackalloc int[8];
        Assert.True(CicpPrimaries.TryGetChromaticities(10, integers));
        Assert.Equal(3, CicpPrimaries.GetDenominator(10));
        Assert.Equal([3, 0, 0, 3, 0, 0, 1, 1], integers.ToArray());

        Span<double> coefficients = stackalloc double[3];
        Assert.True(CicpPrimaries.TryGetLuminanceCoefficients(10, coefficients));
        Assert.Equal(0, coefficients[0], Tolerance);
        Assert.Equal(1, coefficients[1], Tolerance);
        Assert.Equal(0, coefficients[2], Tolerance);
    }

    [Theory]
    [InlineData(1, 0.212639005872, 0.715168678768, 0.072192315361)]
    [InlineData(4, 0.298966618125, 0.586421210133, 0.114612171742)]
    [InlineData(5, 0.222004309998, 0.706654765925, 0.071340924076)]
    [InlineData(6, 0.212376360705, 0.701059856926, 0.086563782369)]
    [InlineData(8, 0.253585363434, 0.678335775697, 0.068078860869)]
    [InlineData(9, 0.262700212011, 0.677998071519, 0.059301716470)]
    [InlineData(11, 0.209491677913, 0.721595254161, 0.068913067926)]
    [InlineData(12, 0.228974564070, 0.691738521837, 0.079286914094)]
    [InlineData(22, 0.231750545672, 0.672250772805, 0.095998681523)]
    public void LuminanceCoefficientsAreDerivedFromTheChromaticities(byte code, double red, double green, double blue)
    {
        Span<double> coefficients = stackalloc double[3];
        Assert.True(CicpPrimaries.TryGetLuminanceCoefficients(code, coefficients));
        Assert.Equal(red, coefficients[0], 1e-11);
        Assert.Equal(green, coefficients[1], 1e-11);
        Assert.Equal(blue, coefficients[2], 1e-11);
        Assert.Equal(1, coefficients[0] + coefficients[1] + coefficients[2], 1e-12);
    }

    [Fact]
    public void UnspecifiedAndReservedPrimariesHaveNoChromaticities()
    {
        byte[] supported = [1, 4, 5, 6, 7, 8, 9, 10, 11, 12, 22];
        Span<double> primaries = stackalloc double[6];
        Span<int> integers = stackalloc int[8];
        Span<double> coefficients = stackalloc double[3];
        for (var code = 0; code <= byte.MaxValue; code++)
        {
            var expected = supported.Contains((byte)code);
            Assert.Equal(expected, CicpPrimaries.IsSupported((byte)code));
            Assert.Equal(expected, CicpPrimaries.TryGet((byte)code, primaries, out _, out _));
            Assert.Equal(expected, CicpPrimaries.TryGetChromaticities((byte)code, integers));
            Assert.Equal(expected, CicpPrimaries.TryGetLuminanceCoefficients((byte)code, coefficients));
        }
    }
}
