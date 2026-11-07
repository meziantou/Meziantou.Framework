using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// An element of the prime field GF(2^255 - 19) of Curve25519 and edwards25519 (RFC 7748 section 4.1, RFC 8032 section
/// 5.1), in radix 2^51: five 64-bit limbs of 51 bits, multiplied with 128-bit products.
/// </summary>
/// <remarks>
/// The limbs are loosely reduced: every operation returns limbs that are slightly larger than 2^51 at most, and only
/// <see cref="ToBytes(Span{byte})"/> computes the canonical value (less than p). The arithmetic does not branch on the
/// values, but the multiplications are not guaranteed to run in constant time on every processor.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct Field25519
{
    private const ulong Mask51 = (1UL << 51) - 1;

    private static readonly BigInteger Prime = (BigInteger.One << 255) - 19;

    // The exponents of the inversion (Fermat's little theorem) and of the square root (RFC 8032 section 5.1.3)
    private static readonly byte[] InverseExponent = ToExponent(Prime - 2);
    private static readonly byte[] SquareRootExponent = ToExponent((Prime - 5) / 8);

    private readonly ulong _l0;
    private readonly ulong _l1;
    private readonly ulong _l2;
    private readonly ulong _l3;
    private readonly ulong _l4;

    private Field25519(ulong l0, ulong l1, ulong l2, ulong l3, ulong l4)
    {
        _l0 = l0;
        _l1 = l1;
        _l2 = l2;
        _l3 = l3;
        _l4 = l4;
    }

    public static Field25519 Zero => default;

    public static Field25519 One => new(1, 0, 0, 0, 0);

    /// <summary>The square root of -1, 2^((p-1)/4) (RFC 8032 section 5.1.3).</summary>
    public static Field25519 SquareRootOfMinusOne { get; } = Pow(FromUInt64(2), ToExponent((Prime - 1) / 4));

    public static Field25519 FromUInt64(ulong value) => new(value & Mask51, value >> 51, 0, 0, 0);

    /// <summary>
    /// Decodes a little-endian 32-byte string, ignoring the most significant bit (RFC 7748 section 5). The values from p
    /// to 2^255 - 1 are accepted and represent their value modulo p.
    /// </summary>
    public static Field25519 FromBytes(ReadOnlySpan<byte> bytes)
    {
        var w0 = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        var w1 = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        var w2 = BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]);
        var w3 = BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]);
        return new(
            w0 & Mask51,
            ((w0 >> 51) | (w1 << 13)) & Mask51,
            ((w1 >> 38) | (w2 << 26)) & Mask51,
            ((w2 >> 25) | (w3 << 39)) & Mask51,
            (w3 >> 12) & Mask51);
    }

    /// <summary>Encodes the canonical value (less than p) as a little-endian 32-byte string.</summary>
    public void ToBytes(Span<byte> destination)
    {
        var reduced = Carry(_l0, _l1, _l2, _l3, _l4);
        var l0 = reduced._l0;
        var l1 = reduced._l1;
        var l2 = reduced._l2;
        var l3 = reduced._l3;
        var l4 = reduced._l4;

        // The value is less than 2p: q is 1 when value + 19 >= 2^255, that is when the value is at least p
        var q = (l0 + 19) >> 51;
        q = (l1 + q) >> 51;
        q = (l2 + q) >> 51;
        q = (l3 + q) >> 51;
        q = (l4 + q) >> 51;

        // value - q * p = value + 19q - q * 2^255: the carry out of the last limb (2^255) is dropped
        l0 += 19 * q;
        l1 += l0 >> 51;
        l0 &= Mask51;
        l2 += l1 >> 51;
        l1 &= Mask51;
        l3 += l2 >> 51;
        l2 &= Mask51;
        l4 += l3 >> 51;
        l3 &= Mask51;
        l4 &= Mask51;

        BinaryPrimitives.WriteUInt64LittleEndian(destination, l0 | (l1 << 51));
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], (l1 >> 13) | (l2 << 38));
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], (l2 >> 26) | (l3 << 25));
        BinaryPrimitives.WriteUInt64LittleEndian(destination[24..], (l3 >> 39) | (l4 << 12));
    }

    /// <summary>Returns whether the canonical value is odd, the "sign" of RFC 8032 section 5.1.2.</summary>
    public bool IsOdd()
    {
        Span<byte> bytes = stackalloc byte[32];
        ToBytes(bytes);
        return (bytes[0] & 1) != 0;
    }

    public bool IsZero()
    {
        Span<byte> bytes = stackalloc byte[32];
        ToBytes(bytes);
        return X25519.IsAllZero(bytes);
    }

    public static bool AreEqual(in Field25519 a, in Field25519 b) => Subtract(a, b).IsZero();

    public static Field25519 Add(in Field25519 a, in Field25519 b)
    {
        return Carry(a._l0 + b._l0, a._l1 + b._l1, a._l2 + b._l2, a._l3 + b._l3, a._l4 + b._l4);
    }

    public static Field25519 Subtract(in Field25519 a, in Field25519 b)
    {
        // a + 4p - b: the limbs of 4p (2^53 - 76, 2^53 - 4...) are larger than the limbs of b
        return Carry(
            a._l0 + 0x1FFFFFFFFFFFB4UL - b._l0,
            a._l1 + 0x1FFFFFFFFFFFFCUL - b._l1,
            a._l2 + 0x1FFFFFFFFFFFFCUL - b._l2,
            a._l3 + 0x1FFFFFFFFFFFFCUL - b._l3,
            a._l4 + 0x1FFFFFFFFFFFFCUL - b._l4);
    }

    public static Field25519 Negate(in Field25519 a) => Subtract(Zero, a);

    public static Field25519 Multiply(in Field25519 a, in Field25519 b)
    {
        var a0 = a._l0;
        var a1 = a._l1;
        var a2 = a._l2;
        var a3 = a._l3;
        var a4 = a._l4;
        var b0 = b._l0;
        var b1 = b._l1;
        var b2 = b._l2;
        var b3 = b._l3;
        var b4 = b._l4;

        // 2^255 = 19 (mod p): the products of weight 2^255 or more are folded with a factor 19
        var b1x19 = b1 * 19;
        var b2x19 = b2 * 19;
        var b3x19 = b3 * 19;
        var b4x19 = b4 * 19;

        var r0 = Math.BigMul(a0, b0) + Math.BigMul(a1, b4x19) + Math.BigMul(a2, b3x19) + Math.BigMul(a3, b2x19) + Math.BigMul(a4, b1x19);
        var r1 = Math.BigMul(a0, b1) + Math.BigMul(a1, b0) + Math.BigMul(a2, b4x19) + Math.BigMul(a3, b3x19) + Math.BigMul(a4, b2x19);
        var r2 = Math.BigMul(a0, b2) + Math.BigMul(a1, b1) + Math.BigMul(a2, b0) + Math.BigMul(a3, b4x19) + Math.BigMul(a4, b3x19);
        var r3 = Math.BigMul(a0, b3) + Math.BigMul(a1, b2) + Math.BigMul(a2, b1) + Math.BigMul(a3, b0) + Math.BigMul(a4, b4x19);
        var r4 = Math.BigMul(a0, b4) + Math.BigMul(a1, b3) + Math.BigMul(a2, b2) + Math.BigMul(a3, b1) + Math.BigMul(a4, b0);

        r1 += r0 >> 51;
        var l0 = (ulong)r0 & Mask51;
        r2 += r1 >> 51;
        var l1 = (ulong)r1 & Mask51;
        r3 += r2 >> 51;
        var l2 = (ulong)r2 & Mask51;
        r4 += r3 >> 51;
        var l3 = (ulong)r3 & Mask51;
        var l4 = (ulong)r4 & Mask51;
        var t = (UInt128)l0 + ((r4 >> 51) * 19);
        l0 = (ulong)t & Mask51;
        l1 += (ulong)(t >> 51);
        return new(l0, l1, l2, l3, l4);
    }

    public static Field25519 Square(in Field25519 a) => Multiply(a, a);

    /// <summary>Computes a^(p-2), the inverse of a (0 for 0).</summary>
    public static Field25519 Invert(in Field25519 a) => Pow(a, InverseExponent);

    /// <summary>Computes a^((p-5)/8), used by the square root of RFC 8032 section 5.1.3.</summary>
    public static Field25519 PowP58(in Field25519 a) => Pow(a, SquareRootExponent);

    /// <summary>Swaps a and b when <paramref name="swap"/> is 1, without branching on it.</summary>
    public static void ConditionalSwap(ref Field25519 a, ref Field25519 b, ulong swap)
    {
        var mask = 0UL - swap;
        var t0 = mask & (a._l0 ^ b._l0);
        var t1 = mask & (a._l1 ^ b._l1);
        var t2 = mask & (a._l2 ^ b._l2);
        var t3 = mask & (a._l3 ^ b._l3);
        var t4 = mask & (a._l4 ^ b._l4);
        a = new(a._l0 ^ t0, a._l1 ^ t1, a._l2 ^ t2, a._l3 ^ t3, a._l4 ^ t4);
        b = new(b._l0 ^ t0, b._l1 ^ t1, b._l2 ^ t2, b._l3 ^ t3, b._l4 ^ t4);
    }

    private static Field25519 Pow(in Field25519 a, byte[] exponent)
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

    private static Field25519 Carry(ulong l0, ulong l1, ulong l2, ulong l3, ulong l4)
    {
        l1 += l0 >> 51;
        l0 &= Mask51;
        l2 += l1 >> 51;
        l1 &= Mask51;
        l3 += l2 >> 51;
        l2 &= Mask51;
        l4 += l3 >> 51;
        l3 &= Mask51;
        l0 += (l4 >> 51) * 19;
        l4 &= Mask51;
        return new(l0, l1, l2, l3, l4);
    }

    private static byte[] ToExponent(BigInteger value) => value.ToByteArray(isUnsigned: true, isBigEndian: false);
}
