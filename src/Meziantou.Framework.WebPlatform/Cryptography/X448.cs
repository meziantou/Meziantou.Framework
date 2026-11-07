using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// The X448 function (RFC 7748 section 5, https://www.rfc-editor.org/rfc/rfc7748#section-5): the Diffie-Hellman function
/// on Curve448, used by the X448 algorithm of the Secure Curves in the Web Cryptography API
/// (https://wicg.github.io/webcrypto-secure-curves/#x448). The base class library does not provide it.
/// </summary>
/// <remarks>
/// The scalar multiplication is the Montgomery ladder of RFC 7748 with conditional swaps that do not branch on the
/// private key.
/// </remarks>
internal static class X448
{
    /// <summary>The length of the private keys, the public keys and the shared secrets.</summary>
    public const int KeySize = 56;

    // (156326 - 2) / 4 (RFC 7748 section 5)
    private const ulong A24 = 39081;

    /// <summary>Computes the public key X448(k, 5) of a private key (RFC 7748 section 6.2).</summary>
    public static byte[] GetPublicKey(ReadOnlySpan<byte> privateKey)
    {
        Span<byte> basePoint = stackalloc byte[KeySize];
        basePoint.Clear();
        basePoint[0] = 5;
        return DeriveSharedSecret(privateKey, basePoint);
    }

    /// <summary>
    /// Computes X448(k, u) (RFC 7748 section 5): the scalar is clamped. The result is all zeros when the public key is
    /// a point of small order: the caller checks it (<see cref="X25519.IsAllZero(ReadOnlySpan{byte})"/>).
    /// </summary>
    public static byte[] DeriveSharedSecret(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> publicKey)
    {
        if (privateKey.Length != KeySize)
        {
            throw new ArgumentException("The private key must be 56 bytes long.", nameof(privateKey));
        }

        if (publicKey.Length != KeySize)
        {
            throw new ArgumentException("The public key must be 56 bytes long.", nameof(publicKey));
        }

        // decodeScalar448
        Span<byte> scalar = stackalloc byte[KeySize];
        privateKey.CopyTo(scalar);
        scalar[0] &= 252;
        scalar[55] |= 128;

        // decodeUCoordinate: all the 448 bits are used
        var x1 = Field448.FromBytes(publicKey);
        var x2 = Field448.One;
        var z2 = Field448.Zero;
        var x3 = x1;
        var z3 = Field448.One;
        var a24 = Field448.FromUInt64(A24);
        var swap = 0UL;
        for (var t = 447; t >= 0; t--)
        {
            var bit = (ulong)((scalar[t >> 3] >> (t & 7)) & 1);
            swap ^= bit;
            Field448.ConditionalSwap(ref x2, ref x3, swap);
            Field448.ConditionalSwap(ref z2, ref z3, swap);
            swap = bit;

            var a = Field448.Add(x2, z2);
            var aa = Field448.Square(a);
            var b = Field448.Subtract(x2, z2);
            var bb = Field448.Square(b);
            var e = Field448.Subtract(aa, bb);
            var c = Field448.Add(x3, z3);
            var d = Field448.Subtract(x3, z3);
            var da = Field448.Multiply(d, a);
            var cb = Field448.Multiply(c, b);
            x3 = Field448.Square(Field448.Add(da, cb));
            z3 = Field448.Multiply(x1, Field448.Square(Field448.Subtract(da, cb)));
            x2 = Field448.Multiply(aa, bb);
            z2 = Field448.Multiply(e, Field448.Add(aa, Field448.Multiply(a24, e)));
        }

        Field448.ConditionalSwap(ref x2, ref x3, swap);
        Field448.ConditionalSwap(ref z2, ref z3, swap);

        CryptographicOperations.ZeroMemory(scalar);
        var result = new byte[KeySize];
        Field448.ToBytes(Field448.Multiply(x2, Field448.Invert(z2)), result);
        return result;
    }
}
