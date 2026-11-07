using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// The X25519 function (RFC 7748 section 5, https://www.rfc-editor.org/rfc/rfc7748#section-5): the Diffie-Hellman
/// function on Curve25519, used by the X25519 algorithm of Web Cryptography (https://w3c.github.io/webcrypto/#x25519).
/// The base class library does not provide it.
/// </summary>
/// <remarks>
/// The scalar multiplication is the Montgomery ladder of RFC 7748 with conditional swaps that do not branch on the
/// private key.
/// </remarks>
internal static class X25519
{
    /// <summary>The length of the private keys, the public keys and the shared secrets.</summary>
    public const int KeySize = 32;

    // (486662 - 2) / 4 (RFC 7748 section 5)
    private const ulong A24 = 121665;

    /// <summary>Computes the public key X25519(k, 9) of a private key (RFC 7748 section 6.1).</summary>
    public static byte[] GetPublicKey(ReadOnlySpan<byte> privateKey)
    {
        Span<byte> basePoint = stackalloc byte[KeySize];
        basePoint.Clear();
        basePoint[0] = 9;
        return DeriveSharedSecret(privateKey, basePoint);
    }

    /// <summary>
    /// Computes X25519(k, u) (RFC 7748 section 5): the scalar is clamped and the most significant bit of the
    /// u-coordinate is ignored. The result is all zeros when the public key is a point of small order: the caller checks
    /// it (<see cref="IsAllZero(ReadOnlySpan{byte})"/>).
    /// </summary>
    public static byte[] DeriveSharedSecret(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey)
    {
        if (privateKey.Length != KeySize)
        {
            throw new ArgumentException("The private key must be 32 bytes long.", nameof(privateKey));
        }

        if (publicKey.Length != KeySize)
        {
            throw new ArgumentException("The public key must be 32 bytes long.", nameof(publicKey));
        }

        // decodeScalar25519
        Span<byte> scalar = stackalloc byte[KeySize];
        privateKey.CopyTo(scalar);
        scalar[0] &= 248;
        scalar[31] &= 127;
        scalar[31] |= 64;

        // decodeUCoordinate: FromBytes ignores the most significant bit
        var x1 = Field25519.FromBytes(publicKey);
        var x2 = Field25519.One;
        var z2 = Field25519.Zero;
        var x3 = x1;
        var z3 = Field25519.One;
        var a24 = Field25519.FromUInt64(A24);
        var swap = 0UL;
        for (var t = 254; t >= 0; t--)
        {
            var bit = (ulong)((scalar[t >> 3] >> (t & 7)) & 1);
            swap ^= bit;
            Field25519.ConditionalSwap(ref x2, ref x3, swap);
            Field25519.ConditionalSwap(ref z2, ref z3, swap);
            swap = bit;

            var a = Field25519.Add(x2, z2);
            var aa = Field25519.Square(a);
            var b = Field25519.Subtract(x2, z2);
            var bb = Field25519.Square(b);
            var e = Field25519.Subtract(aa, bb);
            var c = Field25519.Add(x3, z3);
            var d = Field25519.Subtract(x3, z3);
            var da = Field25519.Multiply(d, a);
            var cb = Field25519.Multiply(c, b);
            x3 = Field25519.Square(Field25519.Add(da, cb));
            z3 = Field25519.Multiply(x1, Field25519.Square(Field25519.Subtract(da, cb)));
            x2 = Field25519.Multiply(aa, bb);
            z2 = Field25519.Multiply(e, Field25519.Add(aa, Field25519.Multiply(a24, e)));
        }

        Field25519.ConditionalSwap(ref x2, ref x3, swap);
        Field25519.ConditionalSwap(ref z2, ref z3, swap);

        CryptographicOperations.ZeroMemory(scalar);
        var result = new byte[KeySize];
        Field25519.Multiply(x2, Field25519.Invert(z2)).ToBytes(result);
        return result;
    }

    /// <summary>
    /// Returns whether all the bytes are zero, without branching on their values (RFC 7748 section 6.1: the shared
    /// secret is all zeros when the public key is a point of small order).
    /// </summary>
    public static bool IsAllZero(ReadOnlySpan<byte> value)
    {
        var accumulator = 0;
        foreach (var b in value)
        {
            accumulator |= b;
        }

        return accumulator == 0;
    }
}
