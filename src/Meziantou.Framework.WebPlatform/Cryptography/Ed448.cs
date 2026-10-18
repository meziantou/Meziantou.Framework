using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// The Ed448 signature scheme (PureEdDSA with a context string, RFC 8032 section 5.2,
/// https://www.rfc-editor.org/rfc/rfc8032#section-5.2), used by the Ed448 algorithm of the Secure Curves in the Web
/// Cryptography API (https://wicg.github.io/webcrypto-secure-curves/#ed448). The base class library does not provide it.
/// </summary>
/// <remarks>
/// The verification applies the checks of Ed25519 (https://w3c.github.io/webcrypto/#ed25519-operations): it fails when
/// the public key or the point R of the signature is not a valid encoding (including the non-canonical encodings) or is
/// a point of small order, when S is not less than L, and it checks the cofactorless equation [S]B = R + [k]A.
/// </remarks>
internal static class Ed448
{
    /// <summary>The length of the private keys (seeds) and the public keys.</summary>
    public const int KeySize = 57;

    /// <summary>The length of the signatures, R followed by S.</summary>
    public const int SignatureSize = 114;

    /// <summary>The maximum length of the context string (RFC 8032 section 5.2).</summary>
    public const int MaxContextLength = 255;

    // The output length of SHAKE256 in the hash function H of Ed448 (RFC 8032 section 5.2)
    private const int HashSize = 114;

    // The order of the base point, L = 2^446 - 13818066809895115352007386748515426880336692474882178609894547503885
    // (RFC 8032 section 5.2)
    private static readonly BigInteger L = (BigInteger.One << 446) - BigInteger.Parse("13818066809895115352007386748515426880336692474882178609894547503885", CultureInfo.InvariantCulture);

    // "SigEd448" in ASCII, the prefix of dom4 (RFC 8032 section 2)
    private static ReadOnlySpan<byte> Dom4Prefix => "SigEd448"u8;

    /// <summary>Computes the public key of a private key (RFC 8032 section 5.2.5).</summary>
    public static byte[] GetPublicKey(ReadOnlySpan<byte> privateKey)
    {
        ValidatePrivateKey(privateKey);
        var hash = ExpandPrivateKey(privateKey);
        var publicKey = Edwards448Point.Multiply(Edwards448Point.BasePoint, hash.AsSpan(0, KeySize)).Encode();
        CryptographicOperations.ZeroMemory(hash);
        return publicKey;
    }

    /// <summary>Signs a message with a context string of at most 255 bytes (RFC 8032 section 5.2.6).</summary>
    public static byte[] Sign(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> context)
    {
        ValidatePrivateKey(privateKey);
        ValidateContext(context);
        var hash = ExpandPrivateKey(privateKey);
        var secretScalar = hash.AsSpan(0, KeySize);
        var prefix = hash.AsSpan(KeySize);
        var publicKey = Edwards448Point.Multiply(Edwards448Point.BasePoint, secretScalar).Encode();

        // r = SHAKE256(dom4(0, C) || prefix || M, 114) mod L, R = [r]B
        var r = Reduce(HashWithDomain(context, prefix, [], message));
        Span<byte> rBytes = stackalloc byte[KeySize];
        WriteScalar(r, rBytes);
        var encodedR = Edwards448Point.Multiply(Edwards448Point.BasePoint, rBytes).Encode();

        // k = SHAKE256(dom4(0, C) || R || A || M, 114) mod L, S = (r + k * s) mod L
        var k = Reduce(HashWithDomain(context, encodedR, publicKey, message));
        var s = new BigInteger(secretScalar, isUnsigned: true, isBigEndian: false);
        var signatureS = (r + (k * s)) % L;

        var signature = new byte[SignatureSize];
        encodedR.CopyTo(signature, 0);
        WriteScalar(signatureS, signature.AsSpan(KeySize));
        CryptographicOperations.ZeroMemory(hash);
        CryptographicOperations.ZeroMemory(rBytes);
        return signature;
    }

    /// <summary>
    /// Verifies a signature with a context string of at most 255 bytes (RFC 8032 section 5.2.7, with the checks
    /// described in the remarks). Returns <see langword="false"/> for malformed public keys and signatures.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> context)
    {
        ValidateContext(context);
        if (publicKey.Length != KeySize || signature.Length != SignatureSize)
        {
            return false;
        }

        if (!Edwards448Point.TryDecode(publicKey, out var a) || a.HasSmallOrder())
        {
            return false;
        }

        var encodedR = signature[..KeySize];
        if (!Edwards448Point.TryDecode(encodedR, out var r) || r.HasSmallOrder())
        {
            return false;
        }

        var encodedS = signature[KeySize..];
        var s = new BigInteger(encodedS, isUnsigned: true, isBigEndian: false);
        if (s >= L)
        {
            return false;
        }

        // k = SHAKE256(dom4(0, C) || R || A || M, 114) mod L, computed on the encodings as received
        var k = Reduce(HashWithDomain(context, encodedR, publicKey, message));
        Span<byte> kBytes = stackalloc byte[KeySize];
        WriteScalar(k, kBytes);

        // [S]B = R + [k]A (cofactorless)
        var left = Edwards448Point.Multiply(Edwards448Point.BasePoint, encodedS);
        var right = Edwards448Point.Add(r, Edwards448Point.Multiply(a, kBytes));
        return Edwards448Point.AreEqual(left, right);
    }

    private static void ValidatePrivateKey(ReadOnlySpan<byte> privateKey)
    {
        if (privateKey.Length != KeySize)
        {
            throw new ArgumentException("The private key must be 57 bytes long.", nameof(privateKey));
        }
    }

    private static void ValidateContext(ReadOnlySpan<byte> context)
    {
        if (context.Length > MaxContextLength)
        {
            throw new ArgumentException("The context must be at most 255 bytes long.", nameof(context));
        }
    }

    // SHAKE256(private key, 114): the first half, pruned, is the secret scalar s, the second half is the prefix
    // (RFC 8032 section 5.2.5)
    private static byte[] ExpandPrivateKey(ReadOnlySpan<byte> privateKey)
    {
        var hash = Keccak.Shake(256, privateKey, HashSize);
        hash[0] &= 252;
        hash[56] = 0;
        hash[55] |= 128;
        return hash;
    }

    // SHAKE256(dom4(0, context) || first || second || message, 114) with phflag = 0 (pure Ed448)
    private static byte[] HashWithDomain(ReadOnlySpan<byte> context, ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, ReadOnlySpan<byte> message)
    {
        var shake = KeccakSponge.CreateShake256();
        shake.Absorb(Dom4Prefix);
        shake.Absorb([0, (byte)context.Length]);
        shake.Absorb(context);
        shake.Absorb(first);
        shake.Absorb(second);
        shake.Absorb(message);
        var result = new byte[HashSize];
        shake.Squeeze(result);
        return result;
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
