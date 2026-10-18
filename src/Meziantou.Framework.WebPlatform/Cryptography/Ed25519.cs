using System.Numerics;
using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// The Ed25519 signature scheme (RFC 8032 section 5.1, https://www.rfc-editor.org/rfc/rfc8032#section-5.1), used by the
/// Ed25519 algorithm of Web Cryptography (https://w3c.github.io/webcrypto/#ed25519). The base class library does not
/// provide it.
/// </summary>
/// <remarks>
/// The verification follows https://w3c.github.io/webcrypto/#ed25519-operations: it fails when the public key or the
/// point R of the signature is not a valid encoding (including the non-canonical encodings) or is a point of small
/// order, when S is not less than L, and it checks the cofactorless equation [S]B = R + [k]A (RFC 8032 section 5.1.7
/// allows both the cofactored and the cofactorless equations; Web Cryptography requires the cofactorless one).
/// </remarks>
internal static class Ed25519
{
    /// <summary>The length of the private keys (seeds) and the public keys.</summary>
    public const int KeySize = 32;

    /// <summary>The length of the signatures, R followed by S.</summary>
    public const int SignatureSize = 64;

    // The order of the base point, L = 2^252 + 27742317777372353535851937790883648493 (RFC 8032 section 5.1)
    private static readonly BigInteger L = (BigInteger.One << 252) + BigInteger.Parse("27742317777372353535851937790883648493", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Computes the public key of a private key (RFC 8032 section 5.1.5).</summary>
    public static byte[] GetPublicKey(ReadOnlySpan<byte> privateKey)
    {
        ValidatePrivateKey(privateKey);
        Span<byte> hash = stackalloc byte[SHA512.HashSizeInBytes];
        ExpandPrivateKey(privateKey, hash);
        var publicKey = Edwards25519Point.Multiply(Edwards25519Point.BasePoint, hash[..32]).Encode();
        CryptographicOperations.ZeroMemory(hash);
        return publicKey;
    }

    /// <summary>Signs a message (RFC 8032 section 5.1.6).</summary>
    public static byte[] Sign(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> message)
    {
        ValidatePrivateKey(privateKey);
        Span<byte> hash = stackalloc byte[SHA512.HashSizeInBytes];
        ExpandPrivateKey(privateKey, hash);
        var secretScalar = hash[..32];
        var prefix = hash[32..];
        var publicKey = Edwards25519Point.Multiply(Edwards25519Point.BasePoint, secretScalar).Encode();

        // r = SHA-512(prefix || M) mod L, R = [r]B
        var r = Reduce(HashToScalar(prefix, [], message));
        Span<byte> rBytes = stackalloc byte[32];
        WriteScalar(r, rBytes);
        var encodedR = Edwards25519Point.Multiply(Edwards25519Point.BasePoint, rBytes).Encode();

        // k = SHA-512(R || A || M) mod L, S = (r + k * s) mod L
        var k = Reduce(HashToScalar(encodedR, publicKey, message));
        var s = new BigInteger(secretScalar, isUnsigned: true, isBigEndian: false);
        var signatureS = (r + (k * s)) % L;

        var signature = new byte[SignatureSize];
        encodedR.CopyTo(signature, 0);
        WriteScalar(signatureS, signature.AsSpan(32));
        CryptographicOperations.ZeroMemory(hash);
        CryptographicOperations.ZeroMemory(rBytes);
        return signature;
    }

    /// <summary>
    /// Verifies a signature (RFC 8032 section 5.1.7 with the checks of https://w3c.github.io/webcrypto/#ed25519-operations).
    /// Returns <see langword="false"/> for malformed public keys and signatures.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        if (publicKey.Length != KeySize || signature.Length != SignatureSize)
        {
            return false;
        }

        if (!Edwards25519Point.TryDecode(publicKey, out var a) || a.HasSmallOrder())
        {
            return false;
        }

        var encodedR = signature[..32];
        if (!Edwards25519Point.TryDecode(encodedR, out var r) || r.HasSmallOrder())
        {
            return false;
        }

        var s = new BigInteger(signature[32..], isUnsigned: true, isBigEndian: false);
        if (s >= L)
        {
            return false;
        }

        // k = SHA-512(R || A || M) mod L, computed on the encodings as received
        var k = Reduce(HashToScalar(encodedR, publicKey, message));
        Span<byte> kBytes = stackalloc byte[32];
        WriteScalar(k, kBytes);

        // [S]B = R + [k]A (cofactorless)
        var left = Edwards25519Point.Multiply(Edwards25519Point.BasePoint, signature[32..]);
        var right = Edwards25519Point.Add(r, Edwards25519Point.Multiply(a, kBytes));
        return Edwards25519Point.AreEqual(left, right);
    }

    private static void ValidatePrivateKey(ReadOnlySpan<byte> privateKey)
    {
        if (privateKey.Length != KeySize)
        {
            throw new ArgumentException("The private key must be 32 bytes long.", nameof(privateKey));
        }
    }

    // The hash of the private key: the first half, pruned, is the secret scalar s, the second half is the prefix
    // (RFC 8032 section 5.1.5)
    private static void ExpandPrivateKey(ReadOnlySpan<byte> privateKey, Span<byte> hash)
    {
        SHA512.HashData(privateKey, hash);
        hash[0] &= 248;
        hash[31] &= 127;
        hash[31] |= 64;
    }

    private static byte[] HashToScalar(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, ReadOnlySpan<byte> message)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        hash.AppendData(first);
        hash.AppendData(second);
        hash.AppendData(message);
        return hash.GetHashAndReset();
    }

    private static BigInteger Reduce(ReadOnlySpan<byte> littleEndian) => new BigInteger(littleEndian, isUnsigned: true, isBigEndian: false) % L;

    private static void WriteScalar(BigInteger value, Span<byte> destination)
    {
        destination.Clear();
        if (!value.TryWriteBytes(destination, out _, isUnsigned: true, isBigEndian: false))
        {
            throw new InvalidOperationException("The scalar does not fit in the destination.");
        }
    }
}
