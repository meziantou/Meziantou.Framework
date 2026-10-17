using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Tests.Cryptography;

// X25519 and X448 (RFC 7748 section 5)
public sealed class MontgomeryCurveTests
{
    // RFC 7748 section 5.2
    [Theory]
    [InlineData("a546e36bf0527c9d3b16154b82465edd62144c0ac1fc5a18506a2244ba449ac4", "e6db6867583030db3594c1a424b15f7c726624ec26b3353b10a903a6d0ab1c4c", "c3da55379de9c6908e94ea4df28d084f32eccf03491c71f754b4075577a28552")]
    [InlineData("4b66e9d4d1b4673c5ad22691957d6af5c11b6421e0ea01d42ca4169e7918ba0d", "e5210f12786811d3f4b7959d0538ae2c31dbe7106fc03c3efc4cd549c715a493", "95cbde9476e8907d7aade45cb4b873f88b595a68799fa152e6f8f7647aac7957")]
    public void X25519_Rfc7748Vectors(string scalar, string u, string expected)
    {
        Assert.Equal(expected, Convert.ToHexStringLower(X25519.DeriveSharedSecret(Convert.FromHexString(scalar), Convert.FromHexString(u))));
    }

    // RFC 7748 section 5.2
    [Theory]
    [InlineData(1, "422c8e7a6227d7bca1350b3e2bb7279f7897b87bb6854b783c60e80311ae3079")]
    [InlineData(1000, "684cf59ba83309552800ef566f2f4d3c1c3887c49360e3875f2eb94d99532c51")]
    public void X25519_Rfc7748Iterations(int iterations, string expected)
    {
        var k = new byte[X25519.KeySize];
        k[0] = 9;
        var u = (byte[])k.Clone();
        for (var i = 0; i < iterations; i++)
        {
            var result = X25519.DeriveSharedSecret(k, u);
            u = k;
            k = result;
        }

        Assert.Equal(expected, Convert.ToHexStringLower(k));
    }

    // RFC 7748 section 6.1
    [Fact]
    public void X25519_Rfc7748DiffieHellman()
    {
        var alicePrivate = Convert.FromHexString("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a");
        var bobPrivate = Convert.FromHexString("5dab087e624a8a4b79e17f8b83800ee66f3bb1292618b6fd1c2f8b27ff88e0eb");
        var alicePublic = X25519.GetPublicKey(alicePrivate);
        var bobPublic = X25519.GetPublicKey(bobPrivate);
        Assert.Equal("8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a", Convert.ToHexStringLower(alicePublic));
        Assert.Equal("de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f", Convert.ToHexStringLower(bobPublic));
        Assert.Equal("4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742", Convert.ToHexStringLower(X25519.DeriveSharedSecret(alicePrivate, bobPublic)));
        Assert.Equal("4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742", Convert.ToHexStringLower(X25519.DeriveSharedSecret(bobPrivate, alicePublic)));
    }

    // WPT WebCryptoAPI/derive_bits_keys/cfrg_curves_bits_fixtures.js (the keys are the last bytes of the PKCS #8 and SPKI structures)
    [Fact]
    public void X25519_WptVector()
    {
        var privateKey = Convert.FromHexString("c8838e76d057dfb7d8c95a69e138160add6373fd71a4d276bb56e3a81b64ff61");
        var publicKey = Convert.FromHexString("1cf2b1e6022ec537371ed7f53e54fa1154d83e98eb64ea51fae5b3307cfe9706");
        Assert.Equal("2768409dfab99ec23b8c89b93ff5880295f76176088f89e43dfebe7ea1950008", Convert.ToHexStringLower(X25519.DeriveSharedSecret(privateKey, publicKey)));
    }

    // WPT WebCryptoAPI/derive_bits_keys/cfrg_curves_bits_fixtures.js: kSmallOrderPoint, including the non-canonical u-coordinates
    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("0100000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("e0eb7a7c3b41b8ae1656e3faf19fc46ada098deb9c32b1fd866205165f49b800")]
    [InlineData("ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")]
    [InlineData("edffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")]
    [InlineData("eeffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f")]
    public void X25519_SmallOrderPoint_ReturnsAllZero(string publicKey)
    {
        var privateKey = Convert.FromHexString("c8838e76d057dfb7d8c95a69e138160add6373fd71a4d276bb56e3a81b64ff61");
        Assert.True(X25519.IsAllZero(X25519.DeriveSharedSecret(privateKey, Convert.FromHexString(publicKey))));
    }

    [Fact]
    public void X25519_IgnoresMostSignificantBitOfU()
    {
        var privateKey = Convert.FromHexString("a546e36bf0527c9d3b16154b82465edd62144c0ac1fc5a18506a2244ba449ac4");
        var u = Convert.FromHexString("e6db6867583030db3594c1a424b15f7c726624ec26b3353b10a903a6d0ab1c4c");
        var expected = X25519.DeriveSharedSecret(privateKey, u);
        u[31] |= 0x80;
        Assert.Equal(expected, X25519.DeriveSharedSecret(privateKey, u));
    }

    [Fact]
    public void X25519_InvalidLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => X25519.DeriveSharedSecret(new byte[31], new byte[32]));
        Assert.Throws<ArgumentException>(() => X25519.DeriveSharedSecret(new byte[32], new byte[33]));
    }

    // RFC 7748 section 5.2
    [Theory]
    [InlineData(
        "3d262fddf9ec8e88495266fea19a34d28882acef045104d0d1aae121700a779c984c24f8cdd78fbff44943eba368f54b29259a4f1c600ad3",
        "06fce640fa3487bfda5f6cf2d5263f8aad88334cbd07437f020f08f9814dc031ddbdc38c19c6da2583fa5429db94ada18aa7a7fb4ef8a086",
        "ce3e4ff95a60dc6697da1db1d85e6afbdf79b50a2412d7546d5f239fe14fbaadeb445fc66a01b0779d98223961111e21766282f73dd96b6f")]
    [InlineData(
        "203d494428b8399352665ddca42f9de8fef600908e0d461cb021f8c538345dd77c3e4806e25f46d3315c44e0a5b4371282dd2c8d5be3095f",
        "0fbcc2f993cd56d3305b0b7d9e55d4c1a8fb5dbb52f8e9a1e9b6201b165d015894e56c4d3570bee52fe205e28a78b91cdfbde71ce8d157db",
        "884a02576239ff7a2f2f63b2db6a9ff37047ac13568e1e30fe63c4a7ad1b3ee3a5700df34321d62077e63633c575c1c954514e99da7c179d")]
    public void X448_Rfc7748Vectors(string scalar, string u, string expected)
    {
        Assert.Equal(expected, Convert.ToHexStringLower(X448.DeriveSharedSecret(Convert.FromHexString(scalar), Convert.FromHexString(u))));
    }

    // RFC 7748 section 5.2 (the 1,000 iterations take seconds in the Debug configuration)
    [Theory]
    [InlineData(1, "3f482c8a9f19b01e6c46ee9711d9dc14fd4bf67af30765c2ae2b846a4d23a8cd0db897086239492caf350b51f833868b9bc2b3bca9cf4113")]
    public void X448_Rfc7748Iterations(int iterations, string expected)
    {
        var k = new byte[X448.KeySize];
        k[0] = 5;
        var u = (byte[])k.Clone();
        for (var i = 0; i < iterations; i++)
        {
            var result = X448.DeriveSharedSecret(k, u);
            u = k;
            k = result;
        }

        Assert.Equal(expected, Convert.ToHexStringLower(k));
    }

    // RFC 7748 section 6.2
    [Fact]
    public void X448_Rfc7748DiffieHellman()
    {
        var alicePrivate = Convert.FromHexString("9a8f4925d1519f5775cf46b04b5800d4ee9ee8bae8bc5565d498c28dd9c9baf574a9419744897391006382a6f127ab1d9ac2d8c0a598726b");
        var bobPrivate = Convert.FromHexString("1c306a7ac2a0e2e0990b294470cba339e6453772b075811d8fad0d1d6927c120bb5ee8972b0d3e21374c9c921b09d1b0366f10b65173992d");
        var alicePublic = X448.GetPublicKey(alicePrivate);
        var bobPublic = X448.GetPublicKey(bobPrivate);
        Assert.Equal("9b08f7cc31b7e3e67d22d5aea121074a273bd2b83de09c63faa73d2c22c5d9bbc836647241d953d40c5b12da88120d53177f80e532c41fa0", Convert.ToHexStringLower(alicePublic));
        Assert.Equal("3eb7a829b0cd20f5bcfc0b599b6feccf6da4627107bdb0d4f345b43027d8b972fc3e34fb4232a13ca706dcb57aec3dae07bdc1c67bf33609", Convert.ToHexStringLower(bobPublic));
        Assert.Equal("07fff4181ac6cc95ec1c16a94a0f74d12da232ce40a77552281d282bb60c0b56fd2464c335543936521c24403085d59a449a5037514a879d", Convert.ToHexStringLower(X448.DeriveSharedSecret(alicePrivate, bobPublic)));
        Assert.Equal("07fff4181ac6cc95ec1c16a94a0f74d12da232ce40a77552281d282bb60c0b56fd2464c335543936521c24403085d59a449a5037514a879d", Convert.ToHexStringLower(X448.DeriveSharedSecret(bobPrivate, alicePublic)));
    }

    // WPT WebCryptoAPI/derive_bits_keys/cfrg_curves_bits_fixtures.js
    [Fact]
    public void X448_WptVector()
    {
        var privateKey = Convert.FromHexString("58c7d29a3eb519b29d00cfb191bb64fc6d8a42d8f17176272b89f2272d1819295c6525c0829671b052ef0727530f188e31d0cc53bf26929e");
        var publicKey = Convert.FromHexString("b604a1d1a5cd1d9426d561ef630a9eb16cbe69d5b9ca615edc53633efb52ea31e6e6a0a1dbacc6e76cbce6482d7e4ba3d55d9e802765ce6f");
        Assert.Equal("f0f6c5f17f94f4291eab7178866d37ec8906dd6c514143dc85be7cf28deff39b726e0f6dcf810eb594dca97b4882bd44c43ea7dc67f49a4e", Convert.ToHexStringLower(X448.DeriveSharedSecret(privateKey, publicKey)));
    }

    // WPT WebCryptoAPI/derive_bits_keys/cfrg_curves_bits_fixtures.js: kSmallOrderPoint, including the non-canonical u-coordinates
    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("0100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("fefffffffffffffffffffffffffffffffffffffffffffffffffffffffeffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    [InlineData("fffffffffffffffffffffffffffffffffffffffffffffffffffffffffeffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    [InlineData("00000000000000000000000000000000000000000000000000000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff")]
    public void X448_SmallOrderPoint_ReturnsAllZero(string publicKey)
    {
        var privateKey = Convert.FromHexString("58c7d29a3eb519b29d00cfb191bb64fc6d8a42d8f17176272b89f2272d1819295c6525c0829671b052ef0727530f188e31d0cc53bf26929e");
        Assert.True(X25519.IsAllZero(X448.DeriveSharedSecret(privateKey, Convert.FromHexString(publicKey))));
    }

    [Fact]
    public void X448_InvalidLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => X448.DeriveSharedSecret(new byte[55], new byte[56]));
        Assert.Throws<ArgumentException>(() => X448.DeriveSharedSecret(new byte[56], new byte[57]));
    }
}
