using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// An element of the prime field GF(2^448 - 2^224 - 1) of Curve448 and edwards448 (RFC 7748 section 4.2, RFC 8032
/// section 5.2), in radix 2^56: eight 64-bit limbs of 56 bits, multiplied with 128-bit products. The reduction uses
/// 2^448 = 2^224 + 1 (mod p).
/// </summary>
/// <remarks>
/// The limbs are loosely reduced: every operation returns limbs that are slightly larger than 2^56 at most, and only
/// <see cref="ToBytes(in Field448, Span{byte})"/> computes the canonical value (less than p). The arithmetic does not
/// branch on the values, but the multiplications are not guaranteed to run in constant time on every processor.
/// </remarks>
[InlineArray(LimbCount)]
internal struct Field448
{
    public const int EncodedSize = 56;

    private const int LimbCount = 8;
    private const ulong Mask56 = (1UL << 56) - 1;

    private static readonly BigInteger Prime = (BigInteger.One << 448) - (BigInteger.One << 224) - 1;

    // The exponents of the inversion (Fermat's little theorem) and of the square root (RFC 8032 section 5.2.3)
    private static readonly byte[] InverseExponent = ToExponent(Prime - 2);
    private static readonly byte[] SquareRootExponent = ToExponent((Prime - 3) / 4);

    private ulong _limb;

    public static Field448 Zero => default;

    public static Field448 One => FromUInt64(1);

    public static Field448 FromUInt64(ulong value)
    {
        var result = default(Field448);
        result[0] = value & Mask56;
        result[1] = value >> 56;
        return result;
    }

    /// <summary>
    /// Decodes a little-endian 56-byte string (RFC 7748 section 5). The values from p to 2^448 - 1 are accepted and
    /// represent their value modulo p.
    /// </summary>
    public static Field448 FromBytes(ReadOnlySpan<byte> bytes)
    {
        var result = default(Field448);
        for (var i = 0; i < LimbCount; i++)
        {
            var limb = 0UL;
            for (var j = 6; j >= 0; j--)
            {
                limb = (limb << 8) | bytes[(i * 7) + j];
            }

            result[i] = limb;
        }

        return result;
    }

    /// <summary>Encodes the canonical value (less than p) as a little-endian 56-byte string.</summary>
    public static void ToBytes(in Field448 value, Span<byte> destination)
    {
        // Carry until every limb is less than 2^56, that is until the value is less than 2^448 (< 2p)
        var v = value;
        for (var pass = 0; pass < 4; pass++)
        {
            var carry = 0UL;
            for (var i = 0; i < LimbCount; i++)
            {
                var limb = v[i] + carry;
                v[i] = limb & Mask56;
                carry = limb >> 56;
            }

            v[0] += carry;
            v[4] += carry;
        }

        // w = v + 2^224 + 1 = v - p + 2^448: when the addition carries out of 2^448, v is at least p and w mod 2^448 is v - p
        var w = default(Field448);
        var c = 1UL;
        for (var i = 0; i < LimbCount; i++)
        {
            var limb = v[i] + c + (i == 4 ? 1UL : 0UL);
            w[i] = limb & Mask56;
            c = limb >> 56;
        }

        var mask = 0UL - c;
        for (var i = 0; i < LimbCount; i++)
        {
            var limb = (w[i] & mask) | (v[i] & ~mask);
            for (var j = 0; j < 7; j++)
            {
                destination[(i * 7) + j] = (byte)(limb >> (8 * j));
            }
        }
    }

    /// <summary>Returns whether the canonical value is odd, the "sign" of RFC 8032 section 5.2.2.</summary>
    public static bool IsOdd(in Field448 value)
    {
        Span<byte> bytes = stackalloc byte[EncodedSize];
        ToBytes(value, bytes);
        return (bytes[0] & 1) != 0;
    }

    public static bool IsZero(in Field448 value)
    {
        Span<byte> bytes = stackalloc byte[EncodedSize];
        ToBytes(value, bytes);
        return X25519.IsAllZero(bytes);
    }

    public static bool AreEqual(in Field448 a, in Field448 b) => IsZero(Subtract(a, b));

    public static Field448 Add(in Field448 a, in Field448 b)
    {
        var result = default(Field448);
        for (var i = 0; i < LimbCount; i++)
        {
            result[i] = a[i] + b[i];
        }

        Carry(ref result);
        return result;
    }

    public static Field448 Subtract(in Field448 a, in Field448 b)
    {
        // a + 4p - b: the limbs of 4p (4 * (2^56 - 1), except 4 * (2^56 - 2) for the limb of 2^224) are larger than the limbs of b
        var result = default(Field448);
        for (var i = 0; i < LimbCount; i++)
        {
            result[i] = a[i] + (i == 4 ? 0x3FFFFFFFFFFFFF8UL : 0x3FFFFFFFFFFFFFCUL) - b[i];
        }

        Carry(ref result);
        return result;
    }

    public static Field448 Negate(in Field448 a) => Subtract(Zero, a);

    public static Field448 Multiply(in Field448 a, in Field448 b)
    {
        Span<UInt128> products = stackalloc UInt128[(2 * LimbCount) - 1];
        products.Clear();
        for (var i = 0; i < LimbCount; i++)
        {
            var ai = a[i];
            for (var j = 0; j < LimbCount; j++)
            {
                products[i + j] += Math.BigMul(ai, b[j]);
            }
        }

        // 2^448 = 2^224 + 1 (mod p): the limb k >= 8 is added to the limbs k - 8 and k - 4. The limbs 12 to 14 fold into
        // the limbs 8 to 10, which are folded afterward.
        for (var k = (2 * LimbCount) - 2; k >= LimbCount; k--)
        {
            products[k - 8] += products[k];
            products[k - 4] += products[k];
        }

        var result = default(Field448);
        UInt128 carry = 0;
        for (var i = 0; i < LimbCount; i++)
        {
            var limb = products[i] + carry;
            result[i] = (ulong)limb & Mask56;
            carry = limb >> 56;
        }

        // The carry out of 2^448 is added to the limbs 0 and 4
        UInt128 propagated = 0;
        for (var i = 0; i < LimbCount; i++)
        {
            var limb = result[i] + propagated + (i == 0 || i == 4 ? carry : UInt128.Zero);
            result[i] = (ulong)limb & Mask56;
            propagated = limb >> 56;
        }

        var top = (ulong)propagated;
        result[0] += top;
        result[4] += top;
        return result;
    }

    public static Field448 Square(in Field448 a) => Multiply(a, a);

    /// <summary>Computes a^(p-2), the inverse of a (0 for 0).</summary>
    public static Field448 Invert(in Field448 a) => Pow(a, InverseExponent);

    /// <summary>Computes a^((p-3)/4), used by the square root of RFC 8032 section 5.2.3.</summary>
    public static Field448 PowP34(in Field448 a) => Pow(a, SquareRootExponent);

    /// <summary>Swaps a and b when <paramref name="swap"/> is 1, without branching on it.</summary>
    public static void ConditionalSwap(ref Field448 a, ref Field448 b, ulong swap)
    {
        var mask = 0UL - swap;
        for (var i = 0; i < LimbCount; i++)
        {
            var t = mask & (a[i] ^ b[i]);
            a[i] ^= t;
            b[i] ^= t;
        }
    }

    private static Field448 Pow(in Field448 a, byte[] exponent)
    {
        var result = One;
        for (var i = exponent.Length * 8 - 1; i >= 0; i--)
        {
            result = Square(result);
            if (((exponent[i >> 3] >> (i & 7)) & 1) != 0)
            {
                result = Multiply(result, a);
            }
        }

        return result;
    }

    private static void Carry(ref Field448 value)
    {
        var carry = 0UL;
        for (var i = 0; i < LimbCount; i++)
        {
            var limb = value[i] + carry;
            value[i] = limb & Mask56;
            carry = limb >> 56;
        }

        value[0] += carry;
        value[4] += carry;
    }

    private static byte[] ToExponent(BigInteger value) => value.ToByteArray(isUnsigned: true, isBigEndian: false);
}
