using System.Security.Cryptography;
using Meziantou.Framework.DnsClient.Response.Records;

namespace Meziantou.Framework.DnsClient.Internal;

internal static class DnssecCrypto
{
    public static bool IsSupportedAlgorithm(byte algorithm)
    {
        return algorithm is 8 or 10 or 13 or 14;
    }

    public static DnssecSignatureVerificationStatus VerifySignature(DnsDnskeyRecord key, byte[] data, byte[] signature)
    {
        return key.Algorithm switch
        {
            8 => VerifyRsa(key.PublicKey, data, signature, HashAlgorithmName.SHA256),
            10 => VerifyRsa(key.PublicKey, data, signature, HashAlgorithmName.SHA512),
            13 => VerifyEcdsa(key.PublicKey, data, signature, HashAlgorithmName.SHA256, 32, ECCurve.NamedCurves.nistP256),
            14 => VerifyEcdsa(key.PublicKey, data, signature, HashAlgorithmName.SHA384, 48, ECCurve.NamedCurves.nistP384),
            _ => DnssecSignatureVerificationStatus.UnsupportedAlgorithm,
        };
    }

    private static DnssecSignatureVerificationStatus VerifyRsa(byte[] publicKey, byte[] data, byte[] signature, HashAlgorithmName hashAlgorithmName)
    {
        if (!TryReadRsaPublicKey(publicKey, out var parameters))
            return DnssecSignatureVerificationStatus.InvalidKey;

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportParameters(parameters);
            return rsa.VerifyData(data, signature, hashAlgorithmName, RSASignaturePadding.Pkcs1)
                ? DnssecSignatureVerificationStatus.Valid
                : DnssecSignatureVerificationStatus.Invalid;
        }
        catch (CryptographicException)
        {
            return DnssecSignatureVerificationStatus.InvalidKey;
        }
    }

    private static DnssecSignatureVerificationStatus VerifyEcdsa(byte[] publicKey, byte[] data, byte[] signature, HashAlgorithmName hashAlgorithmName, int coordinateLength, ECCurve curve)
    {
        if (publicKey.Length != coordinateLength * 2)
            return DnssecSignatureVerificationStatus.InvalidKey;

        try
        {
            var parameters = new ECParameters
            {
                Curve = curve,
                Q = new ECPoint
                {
                    X = publicKey.AsSpan(0, coordinateLength).ToArray(),
                    Y = publicKey.AsSpan(coordinateLength, coordinateLength).ToArray(),
                },
            };

            using var ecdsa = ECDsa.Create(parameters);
            return ecdsa.VerifyData(data, signature, hashAlgorithmName, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                ? DnssecSignatureVerificationStatus.Valid
                : DnssecSignatureVerificationStatus.Invalid;
        }
        catch (CryptographicException)
        {
            return DnssecSignatureVerificationStatus.InvalidKey;
        }
    }

    private static bool TryReadRsaPublicKey(byte[] publicKey, out RSAParameters parameters)
    {
        parameters = default;
        if (publicKey.Length < 3)
            return false;

        var offset = 0;
        int exponentLength;
        if (publicKey[offset] is 0)
        {
            if (publicKey.Length < 3)
                return false;

            exponentLength = (publicKey[1] << 8) | publicKey[2];
            offset = 3;
        }
        else
        {
            exponentLength = publicKey[offset];
            offset++;
        }

        if (exponentLength <= 0 || offset + exponentLength >= publicKey.Length)
            return false;

        parameters = new RSAParameters
        {
            Exponent = publicKey.AsSpan(offset, exponentLength).ToArray(),
            Modulus = publicKey.AsSpan(offset + exponentLength).ToArray(),
        };

        return parameters.Modulus.Length > 0;
    }
}
