using System.Security.Cryptography;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// The hash functions of the Web Cryptography API and the functions built on them: SHA (https://w3c.github.io/webcrypto/#sha),
/// HMAC (https://w3c.github.io/webcrypto/#hmac), HKDF (https://w3c.github.io/webcrypto/#hkdf) and PBKDF2
/// (https://w3c.github.io/webcrypto/#pbkdf2). The hash functions are identified by their normalized algorithm names.
/// </summary>
internal static class Hashes
{
    /// <summary>The digest of the data with the hash function.</summary>
    public static byte[] Digest(string hash, ReadOnlySpan<byte> data) => hash switch
    {
#pragma warning disable CA5350 // SHA-1 is an algorithm of the Web Cryptography API
        "SHA-1" => SHA1.HashData(data),
#pragma warning restore CA5350
        "SHA-256" => SHA256.HashData(data),
        "SHA-384" => SHA384.HashData(data),
        "SHA-512" => SHA512.HashData(data),
        "SHA3-256" => Sha3.HashData(256, data),
        "SHA3-384" => Sha3.HashData(384, data),
        "SHA3-512" => Sha3.HashData(512, data),
        _ => throw new NotSupportedException($"The hash function {hash} is not supported"),
    };

    /// <summary>
    /// The extendable-output functions of https://wicg.github.io/webcrypto-modern-algos/: cSHAKE (<paramref name="functionName"/>
    /// and <paramref name="customization"/>), TurboSHAKE (<paramref name="domainSeparation"/>) and KangarooTwelve
    /// (<paramref name="customization"/>).
    /// </summary>
    public static byte[] ExtendableOutput(string algorithm, ReadOnlySpan<byte> data, int outputLength, ReadOnlySpan<byte> functionName, ReadOnlySpan<byte> customization, byte domainSeparation) => algorithm switch
    {
        "cSHAKE128" => Shake.HashData(128, data, outputLength, functionName, customization),
        "cSHAKE256" => Shake.HashData(256, data, outputLength, functionName, customization),
        "TurboSHAKE128" => TurboShake.HashData(128, data, outputLength, domainSeparation),
        "TurboSHAKE256" => TurboShake.HashData(256, data, outputLength, domainSeparation),
        "KT128" => KangarooTwelve.HashData(128, data, outputLength, customization),
        "KT256" => KangarooTwelve.HashData(256, data, outputLength, customization),
        _ => throw new NotSupportedException($"The {algorithm} algorithm is not supported"),
    };

    /// <summary>The MAC generation of HMAC (FIPS 198-1 section 4).</summary>
    public static byte[] Hmac(string hash, ReadOnlySpan<byte> key, ReadOnlySpan<byte> data) => hash switch
    {
#pragma warning disable CA5350 // SHA-1 is an algorithm of the Web Cryptography API
        "SHA-1" => HMACSHA1.HashData(key, data),
#pragma warning restore CA5350
        "SHA-256" => HMACSHA256.HashData(key, data),
        "SHA-384" => HMACSHA384.HashData(key, data),
        "SHA-512" => HMACSHA512.HashData(key, data),
        _ => throw new NotSupportedException($"The hash function {hash} is not supported"),
    };

    /// <summary>HKDF extract then expand (RFC 5869 section 2).</summary>
    public static byte[] Hkdf(string hash, byte[] inputKeyMaterial, byte[] salt, byte[] info, int outputLength)
    {
        if (outputLength == 0)
            return [];

        return HKDF.DeriveKey(GetHashAlgorithmName(hash), inputKeyMaterial, outputLength, salt, info);
    }

    /// <summary>PBKDF2 with HMAC (RFC 8018 section 5.2).</summary>
    public static byte[] Pbkdf2(string hash, byte[] password, byte[] salt, double iterations, int outputLength)
    {
        if (iterations is < 1 or > int.MaxValue)
            throw new CryptographicException("The number of iterations is not supported");

        if (outputLength == 0)
            return [];

        return Rfc2898DeriveBytes.Pbkdf2(password, salt, (int)iterations, GetHashAlgorithmName(hash), outputLength);
    }

    /// <summary>The .NET name of a hash function of the SHA family.</summary>
    public static HashAlgorithmName GetHashAlgorithmName(string hash) => hash switch
    {
        "SHA-1" => HashAlgorithmName.SHA1,
        "SHA-256" => HashAlgorithmName.SHA256,
        "SHA-384" => HashAlgorithmName.SHA384,
        "SHA-512" => HashAlgorithmName.SHA512,
        _ => throw new NotSupportedException($"The hash function {hash} is not supported"),
    };
}
