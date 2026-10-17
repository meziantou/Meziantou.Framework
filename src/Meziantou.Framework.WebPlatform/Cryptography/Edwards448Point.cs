using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// A point of the Edwards curve edwards448, x^2 + y^2 = 1 + d*x^2*y^2 (RFC 8032 section 5.2), in extended homogeneous
/// coordinates (X:Y:Z:T) with x = X/Z, y = Y/Z and x*y = T/Z.
/// </summary>
/// <remarks>
/// The addition and doubling formulas are the complete formulas of "Twisted Edwards Curves Revisited" (Hisil, Wong,
/// Carter, Dawson) with a = 1: they are valid for every pair of points, including the doubling and the identity, because
/// d is not a square (RFC 8032 section 5.2.4 uses the equivalent projective formulas).
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct Edwards448Point
{
    public const int EncodedSize = 57;

    // The y-coordinate of the base point (RFC 8032 section 5.2)
    private const string BasePointY = "298819210078481492676017930443930673437544040154080242095928241372331506189835876003536878655418784733982303233503462500531545062832660";

    // d = -39081 (RFC 8032 section 5.2)
    private static readonly Field448 D = Field448.Negate(Field448.FromUInt64(39081));

    private readonly Field448 _x;
    private readonly Field448 _y;
    private readonly Field448 _z;
    private readonly Field448 _t;

    private Edwards448Point(Field448 x, Field448 y, Field448 z, Field448 t)
    {
        _x = x;
        _y = y;
        _z = z;
        _t = t;
    }

    /// <summary>The neutral element (0, 1).</summary>
    public static Edwards448Point Identity => new(Field448.Zero, Field448.One, Field448.One, Field448.Zero);

    /// <summary>The base point B (RFC 8032 section 5.2), whose x-coordinate is even.</summary>
    public static Edwards448Point BasePoint { get; } = CreateBasePoint();

    /// <summary>
    /// Decodes a point (RFC 8032 section 5.2.3). The decoding fails when the y-coordinate (the 455 low bits) is not
    /// canonical (at least p), when x^2 has no square root, and when x is 0 while its encoded sign bit is 1.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> encoded, out Edwards448Point point)
    {
        point = default;
        if (encoded.Length != EncodedSize)
        {
            return false;
        }

        // Bit 455 is x_0; the bits 448 to 454 belong to y, which is at least 2^448 > p when one of them is set
        var sign = encoded[56] >> 7;
        if ((encoded[56] & 0x7F) != 0)
        {
            return false;
        }

        var y = Field448.FromBytes(encoded);
        Span<byte> canonical = stackalloc byte[Field448.EncodedSize];
        Field448.ToBytes(y, canonical);
        if (!canonical.SequenceEqual(encoded[..Field448.EncodedSize]))
        {
            return false;
        }

        // x^2 = (y^2 - 1) / (d y^2 - 1) = u / v; the candidate root is x = u^3 v (u^5 v^3)^((p-3)/4)
        var y2 = Field448.Square(y);
        var u = Field448.Subtract(y2, Field448.One);
        var v = Field448.Subtract(Field448.Multiply(D, y2), Field448.One);
        var u2 = Field448.Square(u);
        var u3 = Field448.Multiply(u2, u);
        var u5 = Field448.Multiply(u3, u2);
        var v3 = Field448.Multiply(Field448.Square(v), v);
        var x = Field448.Multiply(Field448.Multiply(u3, v), Field448.PowP34(Field448.Multiply(u5, v3)));
        if (!Field448.AreEqual(Field448.Multiply(v, Field448.Square(x)), u))
        {
            return false;
        }

        if (Field448.IsZero(x) && sign == 1)
        {
            return false;
        }

        if ((Field448.IsOdd(x) ? 1 : 0) != sign)
        {
            x = Field448.Negate(x);
        }

        point = new(x, y, Field448.One, Field448.Multiply(x, y));
        return true;
    }

    /// <summary>Encodes the point: the y-coordinate in 56 bytes, and a last byte that contains the least significant bit of x in its last bit (RFC 8032 section 5.2.2).</summary>
    public byte[] Encode()
    {
        var inverse = Field448.Invert(_z);
        var x = Field448.Multiply(_x, inverse);
        var y = Field448.Multiply(_y, inverse);
        var result = new byte[EncodedSize];
        Field448.ToBytes(y, result);
        if (Field448.IsOdd(x))
        {
            result[56] = 0x80;
        }

        return result;
    }

    public static Edwards448Point Add(in Edwards448Point p, in Edwards448Point q)
    {
        // add-2008-hwcd with a = 1
        var a = Field448.Multiply(p._x, q._x);
        var b = Field448.Multiply(p._y, q._y);
        var c = Field448.Multiply(Field448.Multiply(p._t, D), q._t);
        var d = Field448.Multiply(p._z, q._z);
        var e = Field448.Subtract(Field448.Subtract(Field448.Multiply(Field448.Add(p._x, p._y), Field448.Add(q._x, q._y)), a), b);
        var f = Field448.Subtract(d, c);
        var g = Field448.Add(d, c);
        var h = Field448.Subtract(b, a);
        return new(Field448.Multiply(e, f), Field448.Multiply(g, h), Field448.Multiply(f, g), Field448.Multiply(e, h));
    }

    public static Edwards448Point Double(in Edwards448Point p)
    {
        // dbl-2008-hwcd with a = 1
        var a = Field448.Square(p._x);
        var b = Field448.Square(p._y);
        var z2 = Field448.Square(p._z);
        var c = Field448.Add(z2, z2);
        var e = Field448.Subtract(Field448.Subtract(Field448.Square(Field448.Add(p._x, p._y)), a), b);
        var g = Field448.Add(a, b);
        var f = Field448.Subtract(g, c);
        var h = Field448.Subtract(a, b);
        return new(Field448.Multiply(e, f), Field448.Multiply(g, h), Field448.Multiply(f, g), Field448.Multiply(e, h));
    }

    /// <summary>
    /// Computes [k]P, k being a little-endian scalar, with a Montgomery ladder: the same operations are performed for
    /// every bit, and the points are exchanged with conditional swaps that do not branch on the scalar.
    /// </summary>
    public static Edwards448Point Multiply(in Edwards448Point p, ReadOnlySpan<byte> scalar)
    {
        var r0 = Identity;
        var r1 = p;
        for (var i = scalar.Length * 8 - 1; i >= 0; i--)
        {
            var bit = (ulong)((scalar[i >> 3] >> (i & 7)) & 1);
            ConditionalSwap(ref r0, ref r1, bit);
            r1 = Add(r0, r1);
            r0 = Double(r0);
            ConditionalSwap(ref r0, ref r1, bit);
        }

        return r0;
    }

    /// <summary>Returns whether the point is the identity (X = 0 and Y = Z).</summary>
    public bool IsIdentity() => Field448.IsZero(_x) && Field448.AreEqual(_y, _z);

    /// <summary>Returns whether the order of the point divides the cofactor 4, that is whether [4]P is the identity.</summary>
    public bool HasSmallOrder() => Double(Double(this)).IsIdentity();

    /// <summary>Returns whether two points are equal, comparing X1*Z2 to X2*Z1 and Y1*Z2 to Y2*Z1.</summary>
    public static bool AreEqual(in Edwards448Point p, in Edwards448Point q)
    {
        return Field448.AreEqual(Field448.Multiply(p._x, q._z), Field448.Multiply(q._x, p._z))
            && Field448.AreEqual(Field448.Multiply(p._y, q._z), Field448.Multiply(q._y, p._z));
    }

    private static void ConditionalSwap(ref Edwards448Point p, ref Edwards448Point q, ulong swap)
    {
        var px = p._x;
        var py = p._y;
        var pz = p._z;
        var pt = p._t;
        var qx = q._x;
        var qy = q._y;
        var qz = q._z;
        var qt = q._t;
        Field448.ConditionalSwap(ref px, ref qx, swap);
        Field448.ConditionalSwap(ref py, ref qy, swap);
        Field448.ConditionalSwap(ref pz, ref qz, swap);
        Field448.ConditionalSwap(ref pt, ref qt, swap);
        p = new(px, py, pz, pt);
        q = new(qx, qy, qz, qt);
    }

    private static Edwards448Point CreateBasePoint()
    {
        Span<byte> encoded = stackalloc byte[EncodedSize];
        encoded.Clear();
        var y = BigInteger.Parse(BasePointY, CultureInfo.InvariantCulture);
        if (!y.TryWriteBytes(encoded, out _, isUnsigned: true, isBigEndian: false) || !TryDecode(encoded, out var point))
        {
            throw new InvalidOperationException("The base point of edwards448 cannot be decoded.");
        }

        return point;
    }
}
