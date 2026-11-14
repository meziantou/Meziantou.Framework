using System.Security.Cryptography;
using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The key pair of a public key credential source of a virtual authenticator: the signature algorithms of the COSE
/// algorithm identifiers (https://w3c.github.io/webauthn/#sctn-alg-identifier) that the user agents must understand
/// (https://w3c.github.io/webauthn/#sctn-public-key-easy): ES256 (-7), EdDSA with Ed25519 (-8) and RS256 (-257).
/// </summary>
internal abstract class CredentialKey
{
    public const int ES256 = -7;
    public const int EdDsa = -8;
    public const int RS256 = -257;

    /// <summary>The COSE algorithm identifier of the key.</summary>
    public abstract int Algorithm { get; }

    /// <summary>Whether a virtual authenticator can create a key of the algorithm.</summary>
    public static bool IsSupported(int algorithm) => algorithm is ES256 or EdDsa or RS256;

#pragma warning disable CA2000 // The keys of the BCL are owned by the credential key, for the lifetime of the credential
    /// <summary>Generates a key pair of a supported algorithm.</summary>
    public static CredentialKey Generate(int algorithm) => algorithm switch
    {
        ES256 => new EcdsaCredentialKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)),
        RS256 => new RsaCredentialKey(RSA.Create(2048)),
        EdDsa => new Ed25519CredentialKey(OkpKeys.GeneratePrivateKey("Ed25519")),
        _ => throw new NotSupportedException($"The COSE algorithm {algorithm} is not supported"),
    };

    /// <summary>
    /// Imports an asymmetric key package (RFC 5958, the PKCS #8 PrivateKeyInfo) containing a single ECDSA private key on the
    /// P-256 curve, an RSA private key or an Ed25519 private key. Throws a <see cref="CryptographicException"/> when the key
    /// is not valid.
    /// </summary>
    public static CredentialKey Import(ReadOnlySpan<byte> pkcs8)
    {
        var info = KeyEncodings.ParsePrivateKeyInfo(pkcs8.ToArray());
        switch (info.Algorithm)
        {
            case KeyEncodings.Ed25519:
                return new Ed25519CredentialKey(OkpKeys.ParsePrivateKeyInfo("Ed25519", pkcs8.ToArray()));

            case KeyEncodings.EcPublicKey:
            {
                // The BCL rejects the curves that the platform does not know with a PlatformNotSupportedException, and
                // the platforms do not know the same curves: the named curve must be P-256 before the import
                if (info.Parameters is not null && KeyEncodings.TryDecodeObjectIdentifier(info.Parameters) is not KeyEncodings.Secp256r1)
                    throw new CryptographicException("The key is not an ECDSA private key on the P-256 curve");

                var ecdsa = ECDsa.Create();
                try
                {
                    ecdsa.ImportPkcs8PrivateKey(pkcs8, out var read);
                    if (read != pkcs8.Length || !IsP256(ecdsa.ExportParameters(includePrivateParameters: false).Curve))
                        throw new CryptographicException("The key is not an ECDSA private key on the P-256 curve");
                }
                catch (PlatformNotSupportedException ex)
                {
                    // The ECPrivateKey structure may name another curve than the AlgorithmIdentifier
                    ecdsa.Dispose();
                    throw new CryptographicException("The key is not an ECDSA private key on the P-256 curve", ex);
                }
                catch
                {
                    ecdsa.Dispose();
                    throw;
                }

                return new EcdsaCredentialKey(ecdsa);
            }

            case KeyEncodings.RsaEncryption:
            {
                var rsa = RSA.Create();
                try
                {
                    rsa.ImportPkcs8PrivateKey(pkcs8, out var read);
                    if (read != pkcs8.Length)
                        throw new CryptographicException("The key package has trailing data");
                }
                catch
                {
                    rsa.Dispose();
                    throw;
                }

                return new RsaCredentialKey(rsa);
            }

            default:
                throw new CryptographicException("The key is not an ECDSA P-256, RSA or Ed25519 private key");
        }
    }
#pragma warning restore CA2000

    private static bool IsP256(ECCurve curve) => curve.IsNamed && (curve.Oid.Value == KeyEncodings.Secp256r1 || curve.Oid.FriendlyName is "nistP256" or "ECDSA_P256" or "secp256r1");

    /// <summary>The asymmetric key package of the private key (RFC 5958).</summary>
    public abstract byte[] ExportPkcs8();

    /// <summary>The DER SubjectPublicKeyInfo of the public key (https://w3c.github.io/webauthn/#sctn-public-key-easy).</summary>
    public abstract byte[] ExportSubjectPublicKeyInfo();

    /// <summary>The credential public key in the COSE_Key format, CTAP2 canonical CBOR (https://w3c.github.io/webauthn/#sctn-encoded-credPubKey-examples).</summary>
    public abstract byte[] ExportCoseKey();

    /// <summary>
    /// The signature of the data (https://w3c.github.io/webauthn/#sctn-signature-attestation-types): an ASN.1 DER
    /// Ecdsa-Sig-Value for ES256, a PKCS #1 v1.5 signature for RS256, the raw signature for EdDSA.
    /// </summary>
    public abstract byte[] Sign(ReadOnlySpan<byte> data);

    private sealed class EcdsaCredentialKey(ECDsa key) : CredentialKey
    {
        public override int Algorithm => ES256;

        public override byte[] ExportPkcs8() => key.ExportPkcs8PrivateKey();

        public override byte[] ExportSubjectPublicKeyInfo() => key.ExportSubjectPublicKeyInfo();

        public override byte[] ExportCoseKey()
        {
            var parameters = key.ExportParameters(includePrivateParameters: false);
            var writer = new CborWriter();
            writer.WriteMapHeader(5);
            writer.WriteInteger(1); // kty: EC2
            writer.WriteInteger(2);
            writer.WriteInteger(3); // alg
            writer.WriteInteger(ES256);
            writer.WriteInteger(-1); // crv: P-256
            writer.WriteInteger(1);
            writer.WriteInteger(-2);
            writer.WriteBytes(parameters.Q.X);
            writer.WriteInteger(-3);
            writer.WriteBytes(parameters.Q.Y);
            return writer.ToArray();
        }

        public override byte[] Sign(ReadOnlySpan<byte> data) => key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    private sealed class RsaCredentialKey(RSA key) : CredentialKey
    {
        public override int Algorithm => RS256;

        public override byte[] ExportPkcs8() => key.ExportPkcs8PrivateKey();

        public override byte[] ExportSubjectPublicKeyInfo() => key.ExportSubjectPublicKeyInfo();

        public override byte[] ExportCoseKey()
        {
            var parameters = key.ExportParameters(includePrivateParameters: false);
            var writer = new CborWriter();
            writer.WriteMapHeader(4);
            writer.WriteInteger(1); // kty: RSA
            writer.WriteInteger(3);
            writer.WriteInteger(3); // alg
            writer.WriteInteger(RS256);
            writer.WriteInteger(-1); // n
            writer.WriteBytes(parameters.Modulus);
            writer.WriteInteger(-2); // e
            writer.WriteBytes(parameters.Exponent);
            return writer.ToArray();
        }

        public override byte[] Sign(ReadOnlySpan<byte> data) => key.SignData(data.ToArray(), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private sealed class Ed25519CredentialKey(byte[] privateKey) : CredentialKey
    {
        private readonly byte[] _publicKey = OkpKeys.GetPublicKey("Ed25519", privateKey);

        public override int Algorithm => EdDsa;

        public override byte[] ExportPkcs8() => OkpKeys.EncodePrivateKeyInfo("Ed25519", privateKey);

        public override byte[] ExportSubjectPublicKeyInfo() => OkpKeys.EncodeSubjectPublicKeyInfo("Ed25519", _publicKey);

        public override byte[] ExportCoseKey()
        {
            var writer = new CborWriter();
            writer.WriteMapHeader(4);
            writer.WriteInteger(1); // kty: OKP
            writer.WriteInteger(1);
            writer.WriteInteger(3); // alg
            writer.WriteInteger(EdDsa);
            writer.WriteInteger(-1); // crv: Ed25519
            writer.WriteInteger(6);
            writer.WriteInteger(-2); // x
            writer.WriteBytes(_publicKey);
            return writer.ToArray();
        }

        public override byte[] Sign(ReadOnlySpan<byte> data) => OkpKeys.Sign("Ed25519", privateKey, data, []);
    }
}
