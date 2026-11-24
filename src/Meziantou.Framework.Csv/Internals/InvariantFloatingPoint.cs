using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Csv.Internals;

/// <summary>Parses the floating-point numbers written with the invariant culture faster than the BCL, with the same result.</summary>
/// <remarks>
/// <para>
/// This is the fast path of Clinger (1990), also the first step of fast_float: when the digits form an integer that the type represents exactly and
/// the power of ten is also exact, one IEEE multiplication or division gives the correctly rounded value, which is what the BCL returns.
/// </para>
/// <para>
/// A <see cref="double"/> or a <see cref="float"/> with more digits, such as the round-trip format of most values, goes through the algorithm of
/// Eisel and Lemire, the second step of fast_float (Lemire, "Number Parsing at a Gigabyte per Second", 2021): the digits are multiplied by the 128
/// most significant bits of the power of five, which is always enough to round correctly (Mushtak and Lemire, "Fast Number Parsing Without
/// Fallback", 2023).
/// </para>
/// <para>
/// The other values, such as values with more than 19 digits, large exponents, subnormal values, white spaces, group separators or <c>NaN</c>, are
/// left to the BCL.
/// </para>
/// </remarks>
internal static class InvariantFloatingPoint
{
    // 10^22 is the largest power of ten that a double represents exactly (5^22 < 2^53)
    private static readonly double[] DoublePowersOf10 =
    [
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
        1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
    ];

    // 10^10 is the largest power of ten that a float represents exactly (5^10 < 2^24)
    private static readonly float[] SinglePowersOf10 = [1e0f, 1e1f, 1e2f, 1e3f, 1e4f, 1e5f, 1e6f, 1e7f, 1e8f, 1e9f, 1e10f];

    // More digits may not fit in a ulong
    private const int MaxDigits = 19;

    // Larger exponents are left to the BCL, which also prevents the overflow of the exponent
    private const int MaxExponentDigits = 4;

    // The powers of ten of PowersOfFive: the round-trip format of a double has at most 17 digits, so this covers the values from about 1e-48 to 1e80
    internal const int MinPowerOfFive = -64;
    internal const int MaxPowerOfFive = 64;

    /// <summary>Gets the 128 most significant bits of 5^q, for q from <see cref="MinPowerOfFive"/> to <see cref="MaxPowerOfFive"/>, the high half first.</summary>
    /// <remarks>
    /// These are the values of <c>power_of_five_128</c> of fast_float: truncated for a positive q, and rounded up for a negative q, whose 5^q is the
    /// reciprocal of an integer. The tests compute them again.
    /// </remarks>
    internal static ReadOnlySpan<ulong> PowersOfFive =>
    [
        0xA87FEA27A539E9A5, 0x3F2398D747B36224, // 5^-64
        0xD29FE4B18E88640E, 0x8EEC7F0D19A03AAD, // 5^-63
        0x83A3EEEEF9153E89, 0x1953CF68300424AC, // 5^-62
        0xA48CEAAAB75A8E2B, 0x5FA8C3423C052DD7, // 5^-61
        0xCDB02555653131B6, 0x3792F412CB06794D, // 5^-60
        0x808E17555F3EBF11, 0xE2BBD88BBEE40BD0, // 5^-59
        0xA0B19D2AB70E6ED6, 0x5B6ACEAEAE9D0EC4, // 5^-58
        0xC8DE047564D20A8B, 0xF245825A5A445275, // 5^-57
        0xFB158592BE068D2E, 0xEED6E2F0F0D56712, // 5^-56
        0x9CED737BB6C4183D, 0x55464DD69685606B, // 5^-55
        0xC428D05AA4751E4C, 0xAA97E14C3C26B886, // 5^-54
        0xF53304714D9265DF, 0xD53DD99F4B3066A8, // 5^-53
        0x993FE2C6D07B7FAB, 0xE546A8038EFE4029, // 5^-52
        0xBF8FDB78849A5F96, 0xDE98520472BDD033, // 5^-51
        0xEF73D256A5C0F77C, 0x963E66858F6D4440, // 5^-50
        0x95A8637627989AAD, 0xDDE7001379A44AA8, // 5^-49
        0xBB127C53B17EC159, 0x5560C018580D5D52, // 5^-48
        0xE9D71B689DDE71AF, 0xAAB8F01E6E10B4A6, // 5^-47
        0x9226712162AB070D, 0xCAB3961304CA70E8, // 5^-46
        0xB6B00D69BB55C8D1, 0x3D607B97C5FD0D22, // 5^-45
        0xE45C10C42A2B3B05, 0x8CB89A7DB77C506A, // 5^-44
        0x8EB98A7A9A5B04E3, 0x77F3608E92ADB242, // 5^-43
        0xB267ED1940F1C61C, 0x55F038B237591ED3, // 5^-42
        0xDF01E85F912E37A3, 0x6B6C46DEC52F6688, // 5^-41
        0x8B61313BBABCE2C6, 0x2323AC4B3B3DA015, // 5^-40
        0xAE397D8AA96C1B77, 0xABEC975E0A0D081A, // 5^-39
        0xD9C7DCED53C72255, 0x96E7BD358C904A21, // 5^-38
        0x881CEA14545C7575, 0x7E50D64177DA2E54, // 5^-37
        0xAA242499697392D2, 0xDDE50BD1D5D0B9E9, // 5^-36
        0xD4AD2DBFC3D07787, 0x955E4EC64B44E864, // 5^-35
        0x84EC3C97DA624AB4, 0xBD5AF13BEF0B113E, // 5^-34
        0xA6274BBDD0FADD61, 0xECB1AD8AEACDD58E, // 5^-33
        0xCFB11EAD453994BA, 0x67DE18EDA5814AF2, // 5^-32
        0x81CEB32C4B43FCF4, 0x80EACF948770CED7, // 5^-31
        0xA2425FF75E14FC31, 0xA1258379A94D028D, // 5^-30
        0xCAD2F7F5359A3B3E, 0x096EE45813A04330, // 5^-29
        0xFD87B5F28300CA0D, 0x8BCA9D6E188853FC, // 5^-28
        0x9E74D1B791E07E48, 0x775EA264CF55347E, // 5^-27
        0xC612062576589DDA, 0x95364AFE032A819E, // 5^-26
        0xF79687AED3EEC551, 0x3A83DDBD83F52205, // 5^-25
        0x9ABE14CD44753B52, 0xC4926A9672793543, // 5^-24
        0xC16D9A0095928A27, 0x75B7053C0F178294, // 5^-23
        0xF1C90080BAF72CB1, 0x5324C68B12DD6339, // 5^-22
        0x971DA05074DA7BEE, 0xD3F6FC16EBCA5E04, // 5^-21
        0xBCE5086492111AEA, 0x88F4BB1CA6BCF585, // 5^-20
        0xEC1E4A7DB69561A5, 0x2B31E9E3D06C32E6, // 5^-19
        0x9392EE8E921D5D07, 0x3AFF322E62439FD0, // 5^-18
        0xB877AA3236A4B449, 0x09BEFEB9FAD487C3, // 5^-17
        0xE69594BEC44DE15B, 0x4C2EBE687989A9B4, // 5^-16
        0x901D7CF73AB0ACD9, 0x0F9D37014BF60A11, // 5^-15
        0xB424DC35095CD80F, 0x538484C19EF38C95, // 5^-14
        0xE12E13424BB40E13, 0x2865A5F206B06FBA, // 5^-13
        0x8CBCCC096F5088CB, 0xF93F87B7442E45D4, // 5^-12
        0xAFEBFF0BCB24AAFE, 0xF78F69A51539D749, // 5^-11
        0xDBE6FECEBDEDD5BE, 0xB573440E5A884D1C, // 5^-10
        0x89705F4136B4A597, 0x31680A88F8953031, // 5^-9
        0xABCC77118461CEFC, 0xFDC20D2B36BA7C3E, // 5^-8
        0xD6BF94D5E57A42BC, 0x3D32907604691B4D, // 5^-7
        0x8637BD05AF6C69B5, 0xA63F9A49C2C1B110, // 5^-6
        0xA7C5AC471B478423, 0x0FCF80DC33721D54, // 5^-5
        0xD1B71758E219652B, 0xD3C36113404EA4A9, // 5^-4
        0x83126E978D4FDF3B, 0x645A1CAC083126EA, // 5^-3
        0xA3D70A3D70A3D70A, 0x3D70A3D70A3D70A4, // 5^-2
        0xCCCCCCCCCCCCCCCC, 0xCCCCCCCCCCCCCCCD, // 5^-1
        0x8000000000000000, 0x0000000000000000, // 5^0
        0xA000000000000000, 0x0000000000000000, // 5^1
        0xC800000000000000, 0x0000000000000000, // 5^2
        0xFA00000000000000, 0x0000000000000000, // 5^3
        0x9C40000000000000, 0x0000000000000000, // 5^4
        0xC350000000000000, 0x0000000000000000, // 5^5
        0xF424000000000000, 0x0000000000000000, // 5^6
        0x9896800000000000, 0x0000000000000000, // 5^7
        0xBEBC200000000000, 0x0000000000000000, // 5^8
        0xEE6B280000000000, 0x0000000000000000, // 5^9
        0x9502F90000000000, 0x0000000000000000, // 5^10
        0xBA43B74000000000, 0x0000000000000000, // 5^11
        0xE8D4A51000000000, 0x0000000000000000, // 5^12
        0x9184E72A00000000, 0x0000000000000000, // 5^13
        0xB5E620F480000000, 0x0000000000000000, // 5^14
        0xE35FA931A0000000, 0x0000000000000000, // 5^15
        0x8E1BC9BF04000000, 0x0000000000000000, // 5^16
        0xB1A2BC2EC5000000, 0x0000000000000000, // 5^17
        0xDE0B6B3A76400000, 0x0000000000000000, // 5^18
        0x8AC7230489E80000, 0x0000000000000000, // 5^19
        0xAD78EBC5AC620000, 0x0000000000000000, // 5^20
        0xD8D726B7177A8000, 0x0000000000000000, // 5^21
        0x878678326EAC9000, 0x0000000000000000, // 5^22
        0xA968163F0A57B400, 0x0000000000000000, // 5^23
        0xD3C21BCECCEDA100, 0x0000000000000000, // 5^24
        0x84595161401484A0, 0x0000000000000000, // 5^25
        0xA56FA5B99019A5C8, 0x0000000000000000, // 5^26
        0xCECB8F27F4200F3A, 0x0000000000000000, // 5^27
        0x813F3978F8940984, 0x4000000000000000, // 5^28
        0xA18F07D736B90BE5, 0x5000000000000000, // 5^29
        0xC9F2C9CD04674EDE, 0xA400000000000000, // 5^30
        0xFC6F7C4045812296, 0x4D00000000000000, // 5^31
        0x9DC5ADA82B70B59D, 0xF020000000000000, // 5^32
        0xC5371912364CE305, 0x6C28000000000000, // 5^33
        0xF684DF56C3E01BC6, 0xC732000000000000, // 5^34
        0x9A130B963A6C115C, 0x3C7F400000000000, // 5^35
        0xC097CE7BC90715B3, 0x4B9F100000000000, // 5^36
        0xF0BDC21ABB48DB20, 0x1E86D40000000000, // 5^37
        0x96769950B50D88F4, 0x1314448000000000, // 5^38
        0xBC143FA4E250EB31, 0x17D955A000000000, // 5^39
        0xEB194F8E1AE525FD, 0x5DCFAB0800000000, // 5^40
        0x92EFD1B8D0CF37BE, 0x5AA1CAE500000000, // 5^41
        0xB7ABC627050305AD, 0xF14A3D9E40000000, // 5^42
        0xE596B7B0C643C719, 0x6D9CCD05D0000000, // 5^43
        0x8F7E32CE7BEA5C6F, 0xE4820023A2000000, // 5^44
        0xB35DBF821AE4F38B, 0xDDA2802C8A800000, // 5^45
        0xE0352F62A19E306E, 0xD50B2037AD200000, // 5^46
        0x8C213D9DA502DE45, 0x4526F422CC340000, // 5^47
        0xAF298D050E4395D6, 0x9670B12B7F410000, // 5^48
        0xDAF3F04651D47B4C, 0x3C0CDD765F114000, // 5^49
        0x88D8762BF324CD0F, 0xA5880A69FB6AC800, // 5^50
        0xAB0E93B6EFEE0053, 0x8EEA0D047A457A00, // 5^51
        0xD5D238A4ABE98068, 0x72A4904598D6D880, // 5^52
        0x85A36366EB71F041, 0x47A6DA2B7F864750, // 5^53
        0xA70C3C40A64E6C51, 0x999090B65F67D924, // 5^54
        0xD0CF4B50CFE20765, 0xFFF4B4E3F741CF6D, // 5^55
        0x82818F1281ED449F, 0xBFF8F10E7A8921A4, // 5^56
        0xA321F2D7226895C7, 0xAFF72D52192B6A0D, // 5^57
        0xCBEA6F8CEB02BB39, 0x9BF4F8A69F764490, // 5^58
        0xFEE50B7025C36A08, 0x02F236D04753D5B4, // 5^59
        0x9F4F2726179A2245, 0x01D762422C946590, // 5^60
        0xC722F0EF9D80AAD6, 0x424D3AD2B7B97EF5, // 5^61
        0xF8EBAD2B84E0D58B, 0xD2E0898765A7DEB2, // 5^62
        0x9B934C3B330C8577, 0x63CC55F49F88EB2F, // 5^63
        0xC2781F49FFCFA6D5, 0x3CBF6B71C76B25FB, // 5^64
    ];

    /// <summary>Gets a value indicating whether <typeparamref name="T"/> has a fast path.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSupported<T>() => typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(Half);

    /// <summary>Tries to parse a <see cref="double"/>, a <see cref="float"/> or a <see cref="Half"/> written with the invariant culture.</summary>
    /// <returns>
    /// <see langword="true"/> when the value is parsed, with the value that <c>T.Parse(value, CultureInfo.InvariantCulture)</c> returns;
    /// <see langword="false"/> when the value must be parsed by the BCL, valid or not.
    /// </returns>
    /// <remarks>
    /// The accepted values are an optional sign, digits with an optional dot, at least one digit, and an optional exponent: <c>-123.45</c>,
    /// <c>.5</c>, <c>1e-3</c>.
    /// </remarks>
    public static bool TryParse<TChar, T>(ReadOnlySpan<TChar> value, out T result)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = default!;
        if (!TryParseParts(value, out var negative, out var mantissa, out var exponent))
            return false;

        if (typeof(T) == typeof(double))
        {
            double number;
            if (mantissa <= (1UL << 53) && exponent is >= -22 and <= 22)
            {
                number = (double)mantissa;
                number = exponent < 0 ? number / DoublePowersOf10[-exponent] : number * DoublePowersOf10[exponent];
            }
            else if (TryCompute(mantissa, exponent, mantissaBits: 52, bias: 1023, minRoundToEven: -4, maxRoundToEven: 23, out var bits))
            {
                number = BitConverter.UInt64BitsToDouble(bits);
            }
            else
            {
                return false;
            }

            result = (T)(object)(negative ? -number : number);
            return true;
        }

        if (typeof(T) == typeof(float))
        {
            float number;
            if (mantissa <= (1UL << 24) && exponent is >= -10 and <= 10)
            {
                number = (float)mantissa;
                number = exponent < 0 ? number / SinglePowersOf10[-exponent] : number * SinglePowersOf10[exponent];
            }
            else if (TryCompute(mantissa, exponent, mantissaBits: 23, bias: 127, minRoundToEven: -17, maxRoundToEven: 10, out var bits))
            {
                number = BitConverter.UInt32BitsToSingle((uint)bits);
            }
            else
            {
                return false;
            }

            result = (T)(object)(negative ? -number : number);
            return true;
        }

        if (typeof(T) == typeof(Half))
        {
            // The mantissa and 10^4 are exact Half values. The operation is rounded to a double, then to a Half: rounding twice gives the correctly
            // rounded value when the first precision is at least twice the second one plus two bits (Figueroa, 1995), and 53 >= 2 * 11 + 2.
            if (mantissa > (1UL << 11) || exponent is < -4 or > 4)
                return false;

            var number = (double)mantissa;
            number = exponent < 0 ? number / DoublePowersOf10[-exponent] : number * DoublePowersOf10[exponent];
            var half = (Half)(negative ? -number : number);

            // The BCL decides how an overflow is reported
            if (Half.IsInfinity(half))
                return false;

            result = (T)(object)half;
            return true;
        }

        return false;
    }

    /// <summary>Computes the bits of the IEEE binary number nearest to <paramref name="digits"/> × 10^<paramref name="exponent"/> (Eisel-Lemire).</summary>
    /// <param name="digits">The decimal digits, as an integer.</param>
    /// <param name="exponent">The power of ten.</param>
    /// <param name="mantissaBits">The explicit bits of the mantissa of the type: 52 for a <see cref="double"/>, 23 for a <see cref="float"/>.</param>
    /// <param name="bias">The bias of the exponent of the type.</param>
    /// <param name="minRoundToEven">The smallest power of ten whose product can be exactly between two values of the type.</param>
    /// <param name="maxRoundToEven">The largest power of ten whose product can be exactly between two values of the type.</param>
    /// <param name="bits">The bits of the value, without the sign.</param>
    /// <returns><see langword="false"/> when the BCL must compute it: the power of ten is not in the table, or the value is subnormal or infinite.</returns>
    /// <remarks>This is <c>compute_float</c> of fast_float, without the subnormal values.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryCompute(ulong digits, int exponent, int mantissaBits, int bias, int minRoundToEven, int maxRoundToEven, out ulong bits)
    {
        bits = 0;
        if (digits == 0)
            return true;

        if (exponent is < MinPowerOfFive or > MaxPowerOfFive)
            return false;

        // The digits are normalized so that the product has its most significant bit in one of the two highest positions
        var leadingZeros = BitOperations.LeadingZeroCount(digits);
        digits <<= leadingZeros;
        var powers = PowersOfFive;
        var index = 2 * (exponent - MinPowerOfFive);
        var high = Math.BigMul(digits, powers[index], out var low);

        // The low half of the power of five only matters when the bits below the ones that are kept are all set: it could carry into them
        var precisionMask = ulong.MaxValue >> (mantissaBits + 3);
        if ((high & precisionMask) == precisionMask)
        {
            var secondHigh = Math.BigMul(digits, powers[index + 1], out _);
            low += secondHigh;
            if (secondHigh > low)
            {
                high++;
            }
        }

        // The bits of the mantissa, the implicit bit, and a bit to round
        var upperBit = (int)(high >> 63);
        var shift = upperBit + 64 - mantissaBits - 3;
        var mantissa = high >> shift;

        // The binary exponent of 10^exponent is about exponent × log2(10), which is 217706 / 2^16
        var binaryExponent = ((217706 * exponent) >> 16) + 63 + upperBit - leadingZeros + bias;
        if (binaryExponent <= 0)
            return false;

        // A value exactly between two values rounds to the even one. It can only be exact when the product of 5^exponent is: from 5^-4 to 5^23 for
        // a double.
        if (low <= 1 && exponent >= minRoundToEven && exponent <= maxRoundToEven && (mantissa & 3) == 1 && (mantissa << shift) == high)
        {
            mantissa &= ~1UL;
        }

        mantissa += mantissa & 1;
        mantissa >>= 1;

        // Rounding up can carry into a new bit
        if (mantissa >= (2UL << mantissaBits))
        {
            mantissa = 1UL << mantissaBits;
            binaryExponent++;
        }

        // The largest exponent is the one of the infinities
        if (binaryExponent >= (2 * bias) + 1)
            return false;

        bits = (mantissa & ~(1UL << mantissaBits)) | ((ulong)binaryExponent << mantissaBits);
        return true;
    }

    /// <summary>Splits <c>[+-]digits[.digits][(e|E)[+-]digits]</c> into a sign, the digits as an integer, and the power of ten to apply to it.</summary>
    private static bool TryParseParts<TChar>(ReadOnlySpan<TChar> value, out bool negative, out ulong mantissa, out int exponent)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        negative = false;
        mantissa = 0;
        exponent = 0;

        var index = 0;
        if (index < value.Length && (value[index] == TChar.CreateTruncating('-') || value[index] == TChar.CreateTruncating('+')))
        {
            negative = value[index] == TChar.CreateTruncating('-');
            index++;
        }

        // A value with more digits is left to the BCL as soon as it is found, so that a long value is not read twice. With more digits, the
        // mantissa could overflow.
        var digits = 0;
        while (index < value.Length && uint.CreateTruncating(value[index]) - '0' <= 9)
        {
            if (++digits > MaxDigits)
                return false;

            mantissa = (mantissa * 10) + (uint.CreateTruncating(value[index]) - '0');
            index++;
        }

        var fractionDigits = 0;
        if (index < value.Length && value[index] == TChar.CreateTruncating('.'))
        {
            index++;
            while (index < value.Length && uint.CreateTruncating(value[index]) - '0' <= 9)
            {
                if (++digits > MaxDigits)
                    return false;

                mantissa = (mantissa * 10) + (uint.CreateTruncating(value[index]) - '0');
                index++;
                fractionDigits++;
            }
        }

        if (digits == 0)
            return false;

        if (index < value.Length && (value[index] == TChar.CreateTruncating('e') || value[index] == TChar.CreateTruncating('E')))
        {
            index++;
            var negativeExponent = false;
            if (index < value.Length && (value[index] == TChar.CreateTruncating('-') || value[index] == TChar.CreateTruncating('+')))
            {
                negativeExponent = value[index] == TChar.CreateTruncating('-');
                index++;
            }

            var exponentStart = index;
            while (index < value.Length && uint.CreateTruncating(value[index]) - '0' <= 9)
            {
                if (index - exponentStart == MaxExponentDigits)
                    return false;

                exponent = (exponent * 10) + (int)(uint.CreateTruncating(value[index]) - '0');
                index++;
            }

            if (index == exponentStart)
                return false;

            if (negativeExponent)
            {
                exponent = -exponent;
            }
        }

        exponent -= fractionDigits;
        return index == value.Length;
    }
}
