using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// A point of the twisted Edwards curve edwards25519, -x^2 + y^2 = 1 + d*x^2*y^2 (RFC 8032 section 5.1), in extended
/// homogeneous coordinates (X:Y:Z:T) with x = X/Z, y = Y/Z and x*y = T/Z (RFC 8032 section 5.1.4).
/// </summary>
/// <remarks>
/// The addition and doubling formulas are the complete formulas of "Twisted Edwards Curves Revisited" (Hisil, Wong,
/// Carter, Dawson), also given by RFC 8032 section 5.1.4: they are valid for every pair of points, including the
/// doubling and the identity.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct Edwards25519Point
{
    public const int EncodedSize = 32;

    // d = -121665 / 121666 (RFC 8032 section 5.1)
    private static readonly Field25519 D = Field25519.Multiply(Field25519.Negate(Field25519.FromUInt64(121665)), Field25519.Invert(Field25519.FromUInt64(121666)));

    private readonly Field25519 _x;
    private readonly Field25519 _y;
    private readonly Field25519 _z;
    private readonly Field25519 _t;

    private Edwards25519Point(Field25519 x, Field25519 y, Field25519 z, Field25519 t)
    {
        _x = x;
        _y = y;
        _z = z;
        _t = t;
    }

    /// <summary>The neutral element (0, 1).</summary>
    public static Edwards25519Point Identity => new(Field25519.Zero, Field25519.One, Field25519.One, Field25519.Zero);

    /// <summary>The base point B, (x, 4/5) with a positive x (RFC 8032 section 5.1).</summary>
    public static Edwards25519Point BasePoint { get; } = CreateBasePoint();

    /// <summary>
    /// Decodes a point (RFC 8032 section 5.1.3). The decoding fails when the y-coordinate is not canonical (at least p),
    /// when x^2 has no square root, and when x is 0 while its encoded sign bit is 1.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<byte> encoded, out Edwards25519Point point)
    {
        point = default;
        if (encoded.Length != EncodedSize)
        {
            return false;
        }

        var sign = encoded[31] >> 7;

        // FromBytes ignores the most significant bit (x_0); the y-coordinate must be less than p
        var y = Field25519.FromBytes(encoded);
        Span<byte> canonical = stackalloc byte[EncodedSize];
        y.ToBytes(canonical);
        canonical[31] |= (byte)(sign << 7);
        if (!canonical.SequenceEqual(encoded))
        {
            return false;
        }

        // x^2 = (y^2 - 1) / (d y^2 + 1) = u / v; the candidate root is x = u v^3 (u v^7)^((p-5)/8)
        var y2 = Field25519.Square(y);
        var u = Field25519.Subtract(y2, Field25519.One);
        var v = Field25519.Add(Field25519.Multiply(D, y2), Field25519.One);
        var v3 = Field25519.Multiply(Field25519.Square(v), v);
        var v7 = Field25519.Multiply(Field25519.Square(v3), v);
        var x = Field25519.Multiply(Field25519.Multiply(u, v3), Field25519.PowP58(Field25519.Multiply(u, v7)));
        var vx2 = Field25519.Multiply(v, Field25519.Square(x));
        if (!Field25519.AreEqual(vx2, u))
        {
            if (!Field25519.AreEqual(vx2, Field25519.Negate(u)))
            {
                return false;
            }

            x = Field25519.Multiply(x, Field25519.SquareRootOfMinusOne);
        }

        if (x.IsZero() && sign == 1)
        {
            return false;
        }

        if ((x.IsOdd() ? 1 : 0) != sign)
        {
            x = Field25519.Negate(x);
        }

        point = new(x, y, Field25519.One, Field25519.Multiply(x, y));
        return true;
    }

    /// <summary>Encodes the point: the y-coordinate, and the least significant bit of x in the last bit (RFC 8032 section 5.1.2).</summary>
    public byte[] Encode()
    {
        var inverse = Field25519.Invert(_z);
        var x = Field25519.Multiply(_x, inverse);
        var y = Field25519.Multiply(_y, inverse);
        var result = new byte[EncodedSize];
        y.ToBytes(result);
        if (x.IsOdd())
        {
            result[31] |= 0x80;
        }

        return result;
    }

    public static Edwards25519Point Add(in Edwards25519Point p, in Edwards25519Point q)
    {
        // add-2008-hwcd with a = -1
        var a = Field25519.Multiply(p._x, q._x);
        var b = Field25519.Multiply(p._y, q._y);
        var c = Field25519.Multiply(Field25519.Multiply(p._t, D), q._t);
        var d = Field25519.Multiply(p._z, q._z);
        var e = Field25519.Subtract(Field25519.Subtract(Field25519.Multiply(Field25519.Add(p._x, p._y), Field25519.Add(q._x, q._y)), a), b);
        var f = Field25519.Subtract(d, c);
        var g = Field25519.Add(d, c);
        var h = Field25519.Add(b, a);
        return new(Field25519.Multiply(e, f), Field25519.Multiply(g, h), Field25519.Multiply(f, g), Field25519.Multiply(e, h));
    }

    public static Edwards25519Point Double(in Edwards25519Point p)
    {
        // dbl-2008-hwcd with a = -1
        var a = Field25519.Square(p._x);
        var b = Field25519.Square(p._y);
        var z2 = Field25519.Square(p._z);
        var c = Field25519.Add(z2, z2);
        var e = Field25519.Subtract(Field25519.Subtract(Field25519.Square(Field25519.Add(p._x, p._y)), a), b);
        var g = Field25519.Subtract(b, a);
        var f = Field25519.Subtract(g, c);
        var h = Field25519.Negate(Field25519.Add(a, b));
        return new(Field25519.Multiply(e, f), Field25519.Multiply(g, h), Field25519.Multiply(f, g), Field25519.Multiply(e, h));
    }

    /// <summary>
    /// Computes [k]P, k being a little-endian scalar, with a Montgomery ladder: the same operations are performed for
    /// every bit, and the points are exchanged with conditional swaps that do not branch on the scalar.
    /// </summary>
    public static Edwards25519Point Multiply(in Edwards25519Point p, ReadOnlySpan<byte> scalar)
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
    public bool IsIdentity() => _x.IsZero() && Field25519.AreEqual(_y, _z);

    /// <summary>Returns whether the order of the point divides the cofactor 8, that is whether [8]P is the identity.</summary>
    public bool HasSmallOrder() => Double(Double(Double(this))).IsIdentity();

    /// <summary>Returns whether two points are equal, comparing X1*Z2 to X2*Z1 and Y1*Z2 to Y2*Z1.</summary>
    public static bool AreEqual(in Edwards25519Point p, in Edwards25519Point q)
    {
        return Field25519.AreEqual(Field25519.Multiply(p._x, q._z), Field25519.Multiply(q._x, p._z))
            && Field25519.AreEqual(Field25519.Multiply(p._y, q._z), Field25519.Multiply(q._y, p._z));
    }

    private static void ConditionalSwap(ref Edwards25519Point p, ref Edwards25519Point q, ulong swap)
    {
        var px = p._x;
        var py = p._y;
        var pz = p._z;
        var pt = p._t;
        var qx = q._x;
        var qy = q._y;
        var qz = q._z;
        var qt = q._t;
        Field25519.ConditionalSwap(ref px, ref qx, swap);
        Field25519.ConditionalSwap(ref py, ref qy, swap);
        Field25519.ConditionalSwap(ref pz, ref qz, swap);
        Field25519.ConditionalSwap(ref pt, ref qt, swap);
        p = new(px, py, pz, pt);
        q = new(qx, qy, qz, qt);
    }

    private static Edwards25519Point CreateBasePoint()
    {
        // The encoding of B: y = 4/5 = 0x6666...6658, and the sign bit of x is 0
        Span<byte> encoded = stackalloc byte[EncodedSize];
        encoded.Fill(0x66);
        encoded[0] = 0x58;
        if (!TryDecode(encoded, out var point))
        {
            throw new InvalidOperationException("The base point of edwards25519 cannot be decoded.");
        }

        return point;
    }
}
