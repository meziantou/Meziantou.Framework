using System.Numerics;
using System.Security.Cryptography;
using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Tests.Cryptography;

// The RSA and NIST curve keys of the Web Cryptography API, checked against the base class library
public sealed class AsymmetricKeyTests
{
    private static readonly byte[] Message = "The quick brown fox jumps over the lazy dog"u8.ToArray();

    [Fact]
    public void Rsa_Pkcs1SignatureMatchesBaseClassLibrary()
    {
        using var rsa = RSA.Create(2048);
        var key = ImportRsa(rsa);

        // RSASSA-PKCS1-v1_5 is deterministic
        Assert.Equal(rsa.SignData(Message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), key.SignPkcs1("SHA-256", Message));
        var signature = key.SignPkcs1("SHA-384", Message);
        Assert.True(rsa.VerifyData(Message, signature, HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1));
        Assert.True(key.ToPublicKey().VerifyPkcs1("SHA-384", Message, signature));
        signature[10] ^= 1;
        Assert.False(key.ToPublicKey().VerifyPkcs1("SHA-384", Message, signature));
    }

    [Fact]
    public void Rsa_PssSignature()
    {
        using var rsa = RSA.Create(2048);
        var key = ImportRsa(rsa);

        // The base class library uses a salt of the length of the hash
        Assert.True(key.ToPublicKey().VerifyPss("SHA-256", Message, rsa.SignData(Message, HashAlgorithmName.SHA256, RSASignaturePadding.Pss), saltLength: 32));
        Assert.True(rsa.VerifyData(Message, key.SignPss("SHA-512", Message, 64), HashAlgorithmName.SHA512, RSASignaturePadding.Pss));

        // Other salt lengths
        foreach (var saltLength in (int[])[0, 20, 100])
        {
            var signature = key.SignPss("SHA-256", Message, saltLength);
            Assert.True(key.ToPublicKey().VerifyPss("SHA-256", Message, signature, saltLength));
            Assert.False(key.ToPublicKey().VerifyPss("SHA-256", Message, signature, saltLength + 1));
        }

        Assert.Throws<WebCryptoException>(() => key.SignPss("SHA-512", Message, 256));
    }

    [Fact]
    public void Rsa_Oaep()
    {
        using var rsa = RSA.Create(2048);
        var key = ImportRsa(rsa);
        Assert.Equal(Message, key.DecryptOaep("SHA-256", [], rsa.Encrypt(Message, RSAEncryptionPadding.OaepSHA256)));
        Assert.Equal(Message, rsa.Decrypt(key.ToPublicKey().EncryptOaep("SHA-1", [], Message), RSAEncryptionPadding.OaepSHA1));

        // A label (not supported by the base class library)
        var ciphertext = key.ToPublicKey().EncryptOaep("SHA-384", [1, 2, 3], Message);
        Assert.Equal(Message, key.DecryptOaep("SHA-384", [1, 2, 3], ciphertext));
        Assert.Throws<WebCryptoException>(() => key.DecryptOaep("SHA-384", [1, 2], ciphertext));

        // The longest message is k - 2 * hLen - 2 bytes
        Assert.Throws<WebCryptoException>(() => key.ToPublicKey().EncryptOaep("SHA-256", [], new byte[256 - 64 - 1]));
    }

    [Fact]
    public void Rsa_GenerateWithAnyPublicExponent()
    {
        var key = RsaKey.Generate(512, 3);
        Assert.Equal(512, key.Length);
        Assert.Equal(3, key.E);
        var signature = key.SignPkcs1("SHA-256", Message);
        Assert.True(key.ToPublicKey().VerifyPkcs1("SHA-256", Message, signature));

        Assert.Throws<WebCryptoException>(() => RsaKey.Generate(1024, 65536));
        Assert.Throws<WebCryptoException>(() => RsaKey.Generate(1024, 1));
    }

    [Fact]
    public void Rsa_KeyStructures()
    {
        using var rsa = RSA.Create(1024);
        var key = ImportRsa(rsa);
        Assert.Equal(rsa.ExportRSAPrivateKey(), key.EncodeRsaPrivateKey());
        Assert.Equal(rsa.ExportRSAPublicKey(), key.EncodeRsaPublicKey());
        Assert.Equal(key.N, RsaKey.ParseRsaPrivateKey(rsa.ExportRSAPrivateKey()).N);
        Assert.Equal(key.E, RsaKey.ParseRsaPublicKey(rsa.ExportRSAPublicKey()).E);

        // Inconsistent components and trailing data are DataErrors
        var parameters = rsa.ExportParameters(includePrivateParameters: true);
        parameters.DP![0] ^= 1;
        var exception = Assert.Throws<WebCryptoException>(() => RsaKey.Create(parameters.Modulus!, parameters.Exponent!, parameters.D, parameters.P, parameters.Q, parameters.DP, parameters.DQ, parameters.InverseQ));
        Assert.Equal("DataError", exception.Name);
        Assert.Equal("DataError", Assert.Throws<WebCryptoException>(() => RsaKey.ParseRsaPublicKey([.. rsa.ExportRSAPublicKey(), 0])).Name);
    }

    [Theory]
    [InlineData("P-256")]
    [InlineData("P-384")]
    [InlineData("P-521")]
    public void Ec_PublicKeyDerivationMatchesBaseClassLibrary(string curveName)
    {
        var curve = EcCurve.FromName(curveName)!;
        var key = EcKey.Generate(curve);
        var derived = EcKey.CreatePrivate(curve, key.D!, x: null, y: null);
        Assert.Equal(key.X, derived.X);
        Assert.Equal(key.Y, derived.Y);

        // The compressed form decodes to the same point
        var compressed = new byte[1 + curve.FieldLength];
        compressed[0] = (byte)(2 + (key.Y[^1] & 1));
        key.X.CopyTo(compressed, 1);
        var decompressed = EcKey.DecodePublicPoint(curve, compressed);
        Assert.Equal(key.Y, decompressed.Y);

        // A point that is not on the curve
        var invalid = key.EncodePublicPoint();
        invalid[^1] ^= 1;
        Assert.Equal("DataError", Assert.Throws<WebCryptoException>(() => EcKey.DecodePublicPoint(curve, invalid)).Name);
    }

    [Theory]
    [InlineData("P-256")]
    [InlineData("P-384")]
    [InlineData("P-521")]
    public void Ec_BlindedGeneratorMultiplication(string curveName)
    {
        var curve = EcCurve.FromName(curveName)!;

        // The blinding scalar is random: several keys exercise several blinded scalars
        for (var i = 0; i < 4; i++)
        {
            var key = EcKey.Generate(curve);
            var point = curve.MultiplyGeneratorBlinded(key.D!);
            Assert.Equal(new BigInteger(key.X, isUnsigned: true, isBigEndian: true), point.X);
            Assert.Equal(new BigInteger(key.Y, isUnsigned: true, isBigEndian: true), point.Y);
        }

        // The extreme scalars 1 and n - 1: G and -G
        Assert.Equal(curve.G, curve.MultiplyGeneratorBlinded(RsaKey.I2Osp(BigInteger.One, curve.OrderLength)));
        Assert.Equal((curve.G.X, curve.P - curve.G.Y), curve.MultiplyGeneratorBlinded(RsaKey.I2Osp(curve.N - 1, curve.OrderLength)));
    }

    [Fact]
    public void Ec_SignatureAndKeyAgreement()
    {
        var key = EcKey.Generate(EcCurve.P256);
        var signature = key.Sign("SHA-256", Message);
        Assert.Equal(64, signature.Length);
        Assert.True(key.ToPublicKey().Verify("SHA-256", Message, signature));
        Assert.False(key.ToPublicKey().Verify("SHA-256", Message, signature.AsSpan(1).ToArray()));

        var other = EcKey.Generate(EcCurve.P256);
        Assert.Equal(key.DeriveSharedSecret(other.ToPublicKey()), other.DeriveSharedSecret(key.ToPublicKey()));
    }

    [Fact]
    public void Ec_KeyStructuresRoundTrip()
    {
        var key = EcKey.Generate(EcCurve.P384);
        var spki = KeyEncodings.ParseSubjectPublicKeyInfo(key.ToPublicKey().EncodeSubjectPublicKeyInfo());
        Assert.Equal(key.X, EcKey.ParseSubjectPublicKeyInfo(spki)!.X);

        var pkcs8 = key.EncodePrivateKeyInfo();
        Assert.Equal(key.D, EcKey.ParsePrivateKeyInfo(KeyEncodings.ParsePrivateKeyInfo(pkcs8))!.D);

        // The base class library reads the structure
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(pkcs8, out var bytesRead);
        Assert.Equal(pkcs8.Length, bytesRead);
        Assert.Equal(key.X, ecdsa.ExportParameters(includePrivateParameters: false).Q.X);
    }

    private static RsaKey ImportRsa(RSA rsa)
    {
        var p = rsa.ExportParameters(includePrivateParameters: true);
        var key = RsaKey.Create(p.Modulus!, p.Exponent!, p.D, p.P, p.Q, p.DP, p.DQ, p.InverseQ);
        Assert.Equal(new BigInteger(p.Modulus, isUnsigned: true, isBigEndian: true), key.N);
        return key;
    }
}
