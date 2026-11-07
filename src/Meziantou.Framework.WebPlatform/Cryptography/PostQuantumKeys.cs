using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// The keys of ML-KEM (https://wicg.github.io/webcrypto-modern-algos/#ml-kem) and ML-DSA
/// (https://wicg.github.io/webcrypto-modern-algos/#ml-dsa): a public key is its encoding (ek or pk), and a private key is
/// its seed (d || z for ML-KEM, ξ for ML-DSA), from which the expanded key is derived. The SubjectPublicKeyInfo and the
/// PrivateKeyInfo structures are the ones of RFC 9935 (ML-KEM) and RFC 9881 (ML-DSA).
/// </summary>
internal static class PostQuantumKeys
{
    /// <summary>Whether the algorithm is an ML-KEM or ML-DSA parameter set.</summary>
    public static bool IsSupported(string algorithm) => algorithm is "ML-KEM-512" or "ML-KEM-768" or "ML-KEM-1024" or "ML-DSA-44" or "ML-DSA-65" or "ML-DSA-87";

    private static bool IsMlKem(string algorithm) => algorithm.StartsWith("ML-KEM-", StringComparison.Ordinal);

    private static int GetParameterSet(string algorithm) => algorithm switch
    {
        "ML-KEM-512" => 512,
        "ML-KEM-768" => 768,
        "ML-KEM-1024" => 1024,
        "ML-DSA-44" => 44,
        "ML-DSA-65" => 65,
        "ML-DSA-87" => 87,
        _ => throw new NotSupportedException($"The {algorithm} algorithm is not supported"),
    };

    // id-alg-ml-kem-* (RFC 9935 section 3) and id-ml-dsa-* (RFC 9881 section 2)
    private static string GetOid(string algorithm) => algorithm switch
    {
        "ML-KEM-512" => "2.16.840.1.101.3.4.4.1",
        "ML-KEM-768" => "2.16.840.1.101.3.4.4.2",
        "ML-KEM-1024" => "2.16.840.1.101.3.4.4.3",
        "ML-DSA-44" => "2.16.840.1.101.3.4.3.17",
        "ML-DSA-65" => "2.16.840.1.101.3.4.3.18",
        "ML-DSA-87" => "2.16.840.1.101.3.4.3.19",
        _ => throw new NotSupportedException($"The {algorithm} algorithm is not supported"),
    };

    /// <summary>The length of the seed of a private key.</summary>
    public static int GetSeedLength(string algorithm) => IsMlKem(algorithm) ? MlKem.SeedLength : MlDsa.SeedLength;

    /// <summary>The length of a public key.</summary>
    public static int GetPublicKeyLength(string algorithm) => IsMlKem(algorithm) ? MlKem.GetEncapsulationKeyLength(GetParameterSet(algorithm)) : MlDsa.GetPublicKeyLength(GetParameterSet(algorithm));

    /// <summary>Generates the seed of a private key.</summary>
    public static byte[] GenerateSeed(string algorithm) => RandomNumberGenerator.GetBytes(GetSeedLength(algorithm));

    /// <summary>The public key of a seed (ML-KEM.KeyGen_internal or ML-DSA.KeyGen_internal).</summary>
    public static byte[] GetPublicKey(string algorithm, byte[] seed) => ExpandSeed(algorithm, seed).PublicKey;

    /// <summary>The public key of a SubjectPublicKeyInfo: the algorithm must be the one of the key, without parameters.</summary>
    public static byte[] ParseSubjectPublicKeyInfo(string algorithm, byte[] data)
    {
        var spki = KeyEncodings.ParseSubjectPublicKeyInfo(data);
        if (spki.Algorithm != GetOid(algorithm))
            throw WebCryptoException.DataError($"The algorithm of the key is not {algorithm}");

        if (spki.Parameters is not null)
            throw WebCryptoException.DataError("The algorithm of the key must not have parameters");

        if (spki.PublicKey.Length != GetPublicKeyLength(algorithm))
            throw WebCryptoException.DataError("The public key does not have the length of the algorithm");

        return spki.PublicKey;
    }

    /// <summary>
    /// The seed of a PrivateKeyInfo, whose privateKey is the CHOICE of the seed ([0] IMPLICIT OCTET STRING), the expanded key
    /// (OCTET STRING) or both (SEQUENCE). The expanded key alone is not supported (NotSupportedError), and the expanded key
    /// of both must be the one of the seed (DataError).
    /// </summary>
    public static byte[] ParsePrivateKeyInfo(string algorithm, byte[] data)
    {
        var privateKeyInfo = KeyEncodings.ParsePrivateKeyInfo(data);
        if (privateKeyInfo.Algorithm != GetOid(algorithm))
            throw WebCryptoException.DataError($"The algorithm of the key is not {algorithm}");

        if (privateKeyInfo.Parameters is not null)
            throw WebCryptoException.DataError("The algorithm of the key must not have parameters");

        var seedTag = new Asn1Tag(TagClass.ContextSpecific, 0);
        var (seed, expandedKey) = KeyEncodings.Parse(privateKeyInfo.PrivateKey, reader =>
        {
            var tag = reader.PeekTag();
            if (tag.HasSameClassAndValue(seedTag))
                return (reader.ReadOctetString(seedTag), (byte[]?)null);

            if (tag.HasSameClassAndValue(Asn1Tag.PrimitiveOctetString))
                throw new WebCryptoException("NotSupportedError", "The private keys in the expandedKey format are not supported");

            var sequence = reader.ReadSequence();
            var bothSeed = sequence.ReadOctetString();
            var bothExpandedKey = sequence.ReadOctetString();
            sequence.ThrowIfNotEmpty();
            return (bothSeed, bothExpandedKey);
        });

        if (seed.Length != GetSeedLength(algorithm))
            throw WebCryptoException.DataError("The seed does not have the length of the algorithm");

        if (expandedKey is not null && !CryptographicOperations.FixedTimeEquals(ExpandSeed(algorithm, seed).PrivateKey, expandedKey))
            throw WebCryptoException.DataError("The expanded key does not correspond to the seed");

        return seed;
    }

    public static byte[] EncodeSubjectPublicKeyInfo(string algorithm, byte[] publicKey) => KeyEncodings.EncodeSubjectPublicKeyInfo(GetOid(algorithm), parameters: null, publicKey);

    /// <summary>The PrivateKeyInfo of a private key in the seed-only format.</summary>
    public static byte[] EncodePrivateKeyInfo(string algorithm, byte[] seed)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteOctetString(seed, new Asn1Tag(TagClass.ContextSpecific, 0));
        return KeyEncodings.EncodePrivateKeyInfo(GetOid(algorithm), parameters: null, writer.Encode());
    }

    /// <summary>ML-KEM.Encaps with the encapsulation key check (FIPS 203 section 7.2): an invalid key is an OperationError.</summary>
    public static (byte[] SharedKey, byte[] Ciphertext) Encapsulate(string algorithm, byte[] publicKey)
    {
        var parameterSet = GetParameterSet(algorithm);
        if (!MlKem.IsValidEncapsulationKey(parameterSet, publicKey))
            throw WebCryptoException.OperationError("The encapsulation key is invalid");

        return MlKem.Encapsulate(parameterSet, publicKey);
    }

    /// <summary>ML-KEM.Decaps with the decapsulation input check (FIPS 203 section 7.3): an invalid ciphertext is an OperationError.</summary>
    public static byte[] Decapsulate(string algorithm, byte[] seed, byte[] ciphertext)
    {
        var parameterSet = GetParameterSet(algorithm);
        if (ciphertext.Length != MlKem.GetCiphertextLength(parameterSet))
            throw WebCryptoException.OperationError("The ciphertext does not have the length of the algorithm");

        return MlKem.Decapsulate(parameterSet, ExpandSeed(algorithm, seed).PrivateKey, ciphertext);
    }

    /// <summary>ML-DSA.Sign (FIPS 204 section 5.2), hedged.</summary>
    public static byte[] Sign(string algorithm, byte[] seed, ReadOnlySpan<byte> message, ReadOnlySpan<byte> context) => MlDsa.Sign(GetParameterSet(algorithm), seed, message, context);

    /// <summary>ML-DSA.Verify (FIPS 204 section 5.3).</summary>
    public static bool Verify(string algorithm, byte[] publicKey, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> context) => MlDsa.Verify(GetParameterSet(algorithm), publicKey, message, signature, context);

    private static (byte[] PublicKey, byte[] PrivateKey) ExpandSeed(string algorithm, byte[] seed)
    {
        var parameterSet = GetParameterSet(algorithm);
        if (IsMlKem(algorithm))
        {
            var (encapsulationKey, decapsulationKey) = MlKem.GenerateKeyFromSeed(parameterSet, seed);
            return (encapsulationKey, decapsulationKey);
        }

        var (publicKey, privateKey) = MlDsa.GenerateKeyFromSeed(parameterSet, seed);
        return (publicKey, privateKey);
    }
}
