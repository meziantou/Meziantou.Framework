using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// The keys of the CFRG curves (the "OKP" key type of RFC 8037): Ed25519 and X25519 (https://w3c.github.io/webcrypto/#ed25519),
/// Ed448 and X448 (https://wicg.github.io/webcrypto-secure-curves/). A public key is its encoding (RFC 8032, RFC 7748) and a
/// private key its secret bytes.
/// </summary>
internal static class OkpKeys
{
    /// <summary>The length of the keys of the algorithm, or 0 when it is not an OKP algorithm.</summary>
    public static int GetKeyLength(string algorithm) => algorithm switch
    {
        "Ed25519" => Cryptography.Ed25519.KeySize,
        "X25519" => Cryptography.X25519.KeySize,
        "Ed448" => Cryptography.Ed448.KeySize,
        "X448" => Cryptography.X448.KeySize,
        _ => 0,
    };

    /// <summary>The object identifier of the algorithm (RFC 8410 section 3).</summary>
    public static string GetOid(string algorithm) => algorithm switch
    {
        "Ed25519" => KeyEncodings.Ed25519,
        "X25519" => KeyEncodings.X25519,
        "Ed448" => KeyEncodings.Ed448,
        "X448" => KeyEncodings.X448,
        _ => throw new NotSupportedException($"The {algorithm} algorithm is not supported"),
    };

    /// <summary>Generates a private key.</summary>
    public static byte[] GeneratePrivateKey(string algorithm) => RandomNumberGenerator.GetBytes(GetKeyLength(algorithm));

    /// <summary>The public key of a private key.</summary>
    public static byte[] GetPublicKey(string algorithm, byte[] privateKey) => algorithm switch
    {
        "Ed25519" => Cryptography.Ed25519.GetPublicKey(privateKey),
        "X25519" => Cryptography.X25519.GetPublicKey(privateKey),
        "Ed448" => Cryptography.Ed448.GetPublicKey(privateKey),
        "X448" => Cryptography.X448.GetPublicKey(privateKey),
        _ => throw new NotSupportedException($"The {algorithm} algorithm is not supported"),
    };

    /// <summary>
    /// The public key of a SubjectPublicKeyInfo (RFC 8410 section 4): the algorithm must be the one of the key, without
    /// parameters, and the key must have the length of the algorithm.
    /// </summary>
    public static byte[] ParseSubjectPublicKeyInfo(string algorithm, byte[] data)
    {
        var spki = KeyEncodings.ParseSubjectPublicKeyInfo(data);
        if (spki.Algorithm != GetOid(algorithm))
            throw WebCryptoException.DataError($"The algorithm of the key is not {algorithm}");

        if (spki.Parameters is not null)
            throw WebCryptoException.DataError("The algorithm of the key must not have parameters");

        if (spki.PublicKey.Length != GetKeyLength(algorithm))
            throw WebCryptoException.DataError("The public key does not have the length of the algorithm");

        return spki.PublicKey;
    }

    /// <summary>The private key of a PrivateKeyInfo, whose privateKey is a CurvePrivateKey (RFC 8410 section 7).</summary>
    public static byte[] ParsePrivateKeyInfo(string algorithm, byte[] data)
    {
        var privateKeyInfo = KeyEncodings.ParsePrivateKeyInfo(data);
        if (privateKeyInfo.Algorithm != GetOid(algorithm))
            throw WebCryptoException.DataError($"The algorithm of the key is not {algorithm}");

        if (privateKeyInfo.Parameters is not null)
            throw WebCryptoException.DataError("The algorithm of the key must not have parameters");

        var privateKey = KeyEncodings.ParseOctetString(privateKeyInfo.PrivateKey);
        if (privateKey.Length != GetKeyLength(algorithm))
            throw WebCryptoException.DataError("The private key does not have the length of the algorithm");

        return privateKey;
    }

    public static byte[] EncodeSubjectPublicKeyInfo(string algorithm, byte[] publicKey) => KeyEncodings.EncodeSubjectPublicKeyInfo(GetOid(algorithm), parameters: null, publicKey);

    public static byte[] EncodePrivateKeyInfo(string algorithm, byte[] privateKey) => KeyEncodings.EncodePrivateKeyInfo(GetOid(algorithm), parameters: null, KeyEncodings.EncodeOctetString(privateKey));

    /// <summary>The EdDSA signature of a message.</summary>
    public static byte[] Sign(string algorithm, byte[] privateKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> context) => algorithm switch
    {
        "Ed25519" => Cryptography.Ed25519.Sign(privateKey, message),
        "Ed448" => Cryptography.Ed448.Sign(privateKey, message, context),
        _ => throw new NotSupportedException($"The {algorithm} algorithm is not supported"),
    };

    /// <summary>Verifies an EdDSA signature (the small-order keys and R are rejected).</summary>
    public static bool Verify(string algorithm, byte[] publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> context) => algorithm switch
    {
        "Ed25519" => Cryptography.Ed25519.Verify(publicKey, message, signature),
        "Ed448" => Cryptography.Ed448.Verify(publicKey, message, signature, context),
        _ => throw new NotSupportedException($"The {algorithm} algorithm is not supported"),
    };

    /// <summary>
    /// The X25519 or X448 shared secret (RFC 7748 section 6): an all-zero secret (a small-order public key) is an
    /// OperationError (https://w3c.github.io/webcrypto/#x25519-operations-derive-bits).
    /// </summary>
    public static byte[] DeriveSharedSecret(string algorithm, byte[] privateKey, byte[] publicKey)
    {
        var secret = algorithm switch
        {
            "X25519" => Cryptography.X25519.DeriveSharedSecret(privateKey, publicKey),
            "X448" => Cryptography.X448.DeriveSharedSecret(privateKey, publicKey),
            _ => throw new NotSupportedException($"The {algorithm} algorithm is not supported"),
        };

        if (Cryptography.X25519.IsAllZero(secret))
            throw WebCryptoException.OperationError("The shared secret is all zeros (the public key has a small order)");

        return secret;
    }
}
