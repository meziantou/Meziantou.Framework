using System.Numerics;
using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Tests.Cryptography;

// Ed25519 and Ed448 (RFC 8032), with the verification checks of Web Cryptography (https://w3c.github.io/webcrypto/#ed25519-operations)
public sealed class EdwardsCurveTests
{
    private static readonly BigInteger Ed25519Order = (BigInteger.One << 252) + BigInteger.Parse("27742317777372353535851937790883648493", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly BigInteger Ed448Order = (BigInteger.One << 446) - BigInteger.Parse("13818066809895115352007386748515426880336692474882178609894547503885", System.Globalization.CultureInfo.InvariantCulture);

    // RFC 8032 section 7.1: TEST 1, TEST 2, TEST 3 and TEST SHA(abc)
    [Theory]
    [InlineData(
        "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60",
        "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a",
        "",
        "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b")]
    [InlineData(
        "4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb",
        "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c",
        "72",
        "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00")]
    [InlineData(
        "c5aa8df43f9f837bedb7442f31dcb7b166d38535076f094b85ce3a2e0b4458f7",
        "fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025",
        "af82",
        "6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a")]
    [InlineData(
        "833fe62409237b9d62ec77587520911e9a759cec1d19755b7da901b96dca3d42",
        "ec172b93ad5e563bf4932c70e1245034c35467ef2efd4d64ebf819683467e2bf",
        "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f",
        "dc2a4459e7369633a52b1bf277839a00201009a3efbf3ecb69bea2186c26b58909351fc9ac90b3ecfdfbc7c66431e0303dca179c138ac17ad9bef1177331a704")]
    // WPT WebCryptoAPI/sign_verify/eddsa_vectors.js (the keys are the last bytes of the PKCS #8 and SPKI structures)
    [InlineData(
        "f3c8f4c48df878146e8cd3bf6df4e50e389ba7074e15c2352dcd5d308d4ca81f",
        "d8e18963d809d487d9549accaec6742e7eeba24d8a0d3b14b7e3caea06893dcc",
        "2b7ed0bc7795694ab4acd35903fe8cd7d80f6a1c8688a6c3414409457514a1457855bbb219e30a1beea8fe869082d99fc8282f9050d024e59eaf0730ba9db70a",
        "3d90de5e5743dfc28225bfadb341b116cbf8a3f1ceedbf4adc350ef5d3471843a418614dcb6e614862614cf7af1496f9340b3c844ea4dceab1d3d155eb7ecc00")]
    public void Ed25519_Vectors(string privateKey, string publicKey, string message, string signature)
    {
        var privateKeyBytes = Convert.FromHexString(privateKey);
        var messageBytes = Convert.FromHexString(message);
        Assert.Equal(publicKey, Convert.ToHexStringLower(Ed25519.GetPublicKey(privateKeyBytes)));
        Assert.Equal(signature, Convert.ToHexStringLower(Ed25519.Sign(privateKeyBytes, messageBytes)));
        Assert.True(Ed25519.Verify(Convert.FromHexString(publicKey), messageBytes, Convert.FromHexString(signature)));

        var tamperedMessage = messageBytes.Length == 0 ? [0] : messageBytes.ToArray();
        if (messageBytes.Length != 0)
        {
            tamperedMessage[0] ^= 1;
        }

        Assert.False(Ed25519.Verify(Convert.FromHexString(publicKey), tamperedMessage, Convert.FromHexString(signature)));
    }

    // WPT WebCryptoAPI/sign_verify/eddsa_vectors.js: kSmallOrderTestCases (https://eprint.iacr.org/2020/1244.pdf)
    [Theory]
    [InlineData(0, "8c93255d71dcab10e8f379c26200f3c7bd5f09d9bc3068d3ef4edeb4853022b6", "c7176a703d4dd84fba3c0b760d10670f2a2053fa2c39ccc64ec7fd7792ac03fa", "c7176a703d4dd84fba3c0b760d10670f2a2053fa2c39ccc64ec7fd7792ac037a0000000000000000000000000000000000000000000000000000000000000000", false)]
    [InlineData(1, "9bd9f44f4dcc75bd531b56b2cd280b0bb38fc1cd6d1230e14861d861de092e79", "c7176a703d4dd84fba3c0b760d10670f2a2053fa2c39ccc64ec7fd7792ac03fa", "f7badec5b8abeaf699583992219b7b223f1df3fbbea919844e3f7c554a43dd43a5bb704786be79fc476f91d3f3f89b03984d8068dcf1bb7dfc6637b45450ac04", false)]
    [InlineData(2, "aebf3f2601a0c8c5d39cc7d8911642f740b78168218da8471772b35f9d35b9ab", "f7badec5b8abeaf699583992219b7b223f1df3fbbea919844e3f7c554a43dd43", "c7176a703d4dd84fba3c0b760d10670f2a2053fa2c39ccc64ec7fd7792ac03fa8c4bd45aecaca5b24fb97bc10ac27ac8751a7dfe1baff8b953ec9f5833ca260e", false)]
    [InlineData(3, "9bd9f44f4dcc75bd531b56b2cd280b0bb38fc1cd6d1230e14861d861de092e79", "cdb267ce40c5cd45306fa5d2f29731459387dbf9eb933b7bd5aed9a765b88d4d", "9046a64750444938de19f227bb80485e92b83fdb4b6506c160484c016cc1852f87909e14428a7a1d62e9f22f3d3ad7802db02eb2e688b6c52fcd6648a98bd009", true)]
    [InlineData(4, "e47d62c63f830dc7a6851a0b1f33ae4bb2f507fb6cffec4011eaccd55b53f56c", "cdb267ce40c5cd45306fa5d2f29731459387dbf9eb933b7bd5aed9a765b88d4d", "160a1cb0dc9c0258cd0a7d23e94d8fa878bcb1925f2c64246b2dee1796bed5125ec6bc982a269b723e0668e540911a9a6a58921d6925e434ab10aa7940551a09", false)]
    [InlineData(5, "e47d62c63f830dc7a6851a0b1f33ae4bb2f507fb6cffec4011eaccd55b53f56c", "cdb267ce40c5cd45306fa5d2f29731459387dbf9eb933b7bd5aed9a765b88d4d", "21122a84e0b5fca4052f5b1235c80a537878b38f3142356b2c2384ebad4668b7e40bc836dac0f71076f9abe3a53f9c03c1ceeeddb658d0030494ace586687405", false)]
    [InlineData(6, "85e241a07d148b41e47d62c63f830dc7a6851a0b1f33ae4bb2f507fb6cffec40", "442aad9f089ad9e14647b1ef9099a1ff4798d78589e66f28eca69c11f582a623", "e96f66be976d82e60150baecff9906684aebb1ef181f67a7189ac78ea23b6c0e547f7690a0e2ddcd04d87dbc3490dc19b3b3052f7ff0538cb68afb369ba3a514", false)]
    [InlineData(7, "85e241a07d148b41e47d62c63f830dc7a6851a0b1f33ae4bb2f507fb6cffec40", "442aad9f089ad9e14647b1ef9099a1ff4798d78589e66f28eca69c11f582a623", "8ce5b96c8f26d0ab6c47958c9e68b937104cd36e13c33566acd2fe8d38aa19427e71f98a4734e74f2f13f06f97c20d58cc3f54b8bd0d272f42b695dd7e89a8c202", false)]
    [InlineData(8, "9bedc267423725d473888631ebf45988bad3db83851ee85c85e241a07d148b41", "f7badec5b8abeaf699583992219b7b223f1df3fbbea919844e3f7c554a43dd43", "ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff03be9678ac102edcd92b0210bb34d7428d12ffc5df5f37e359941266a4e35f0f", false)]
    [InlineData(9, "9bedc267423725d473888631ebf45988bad3db83851ee85c85e241a07d148b41", "f7badec5b8abeaf699583992219b7b223f1df3fbbea919844e3f7c554a43dd43", "ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffca8c5b64cd208982aa38d4936621a4775aa233aa0505711d8fdcfdaa943d4908", false)]
    [InlineData(10, "e96b7021eb39c1a163b6da4e3093dcd3f21387da4cc4572be588fafae23c155b", "ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "a9d55260f765261eb9b84e106f665e00b867287a761990d7135963ee0a7d59dca5bb704786be79fc476f91d3f3f89b03984d8068dcf1bb7dfc6637b45450ac04", false)]
    [InlineData(11, "39a591f5321bbe07fd5a23dc2f39d025d74526615746727ceefd6e82ae65c06f", "ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", "a9d55260f765261eb9b84e106f665e00b867287a761990d7135963ee0a7d59dca5bb704786be79fc476f91d3f3f89b03984d8068dcf1bb7dfc6637b45450ac04", false)]
    [InlineData(12, "53656e64203130302055534420746f20416c696365", "ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f", "a9d55260f765261eb9b84e106f665e00b867287a761990d7135963ee0a7d59dca5bb704786be79fc476f91d3f3f89b03984d8068dcf1bb7dfc6637b45450ac04", false)]
    [InlineData(13, "53656e64203130303030302055534420746f20416c696365", "ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f", "a9d55260f765261eb9b84e106f665e00b867287a761990d7135963ee0a7d59dca5bb704786be79fc476f91d3f3f89b03984d8068dcf1bb7dfc6637b45450ac04", false)]
    public void Ed25519_SmallOrderTestCases(int id, string message, string publicKey, string signature, bool expected)
    {
        _ = id; // The identifier of the WPT test case, shown in the name of the test
        Assert.Equal(expected, Ed25519.Verify(Convert.FromHexString(publicKey), Convert.FromHexString(message), Convert.FromHexString(signature)));
    }

    [Fact]
    public void Ed25519_Verify_NonCanonicalS_ReturnsFalse()
    {
        // RFC 8032 TEST 1 with S + L: the equation still holds, but S must be less than L
        var publicKey = Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        var signature = Convert.FromHexString("e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");
        var s = new BigInteger(signature.AsSpan(32), isUnsigned: true) + Ed25519Order;
        Assert.True(s.TryWriteBytes(signature.AsSpan(32), out _, isUnsigned: true));
        Assert.False(Ed25519.Verify(publicKey, [], signature));
    }

    [Fact]
    public void Ed25519_Verify_MalformedInputs_ReturnsFalse()
    {
        var publicKey = Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        var signature = Convert.FromHexString("e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");
        Assert.False(Ed25519.Verify(publicKey.AsSpan(1), [], signature));
        Assert.False(Ed25519.Verify(publicKey, [], signature.AsSpan(1)));
        Assert.False(Ed25519.Verify([], [], []));

        // y = 2 is not the y-coordinate of a point of the curve
        var notOnCurve = new byte[32];
        notOnCurve[0] = 2;
        Assert.False(Ed25519.Verify(notOnCurve, [], signature));

        // y = p + 1 (non-canonical encoding of the identity)
        Assert.False(Ed25519.Verify(Convert.FromHexString("eeffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f"), [], signature));
    }

    [Fact]
    public void Ed25519_SignVerify_RoundTrip()
    {
        var privateKey = new byte[Ed25519.KeySize];
        for (var i = 0; i < privateKey.Length; i++)
        {
            privateKey[i] = (byte)(i * 7);
        }

        var publicKey = Ed25519.GetPublicKey(privateKey);
        var message = "The quick brown fox jumps over the lazy dog"u8.ToArray();
        var signature = Ed25519.Sign(privateKey, message);
        Assert.Equal(Ed25519.SignatureSize, signature.Length);
        Assert.True(Ed25519.Verify(publicKey, message, signature));
        signature[63] ^= 0x01;
        Assert.False(Ed25519.Verify(publicKey, message, signature));
    }

    [Fact]
    public void Ed25519_InvalidPrivateKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => Ed25519.GetPublicKey(new byte[31]));
        Assert.Throws<ArgumentException>(() => Ed25519.Sign(new byte[33], []));
    }

    // RFC 8032 section 7.4: Blank, 1 octet, 1 octet (with context), 11 octets, 12 octets, 13 octets
    [Theory]
    [InlineData(
        "6c82a562cb808d10d632be89c8513ebf6c929f34ddfa8c9f63c9960ef6e348a3528c8a3fcc2f044e39a3fc5b94492f8f032e7549a20098f95b",
        "5fd7449b59b461fd2ce787ec616ad46a1da1342485a70e1f8a0ea75d80e96778edf124769b46c7061bd6783df1e50f6cd1fa1abeafe8256180",
        "",
        "",
        "533a37f6bbe457251f023c0d88f976ae2dfb504a843e34d2074fd823d41a591f2b233f034f628281f2fd7a22ddd47d7828c59bd0a21bfd3980ff0d2028d4b18a9df63e006c5d1c2d345b925d8dc00b4104852db99ac5c7cdda8530a113a0f4dbb61149f05a7363268c71d95808ff2e652600")]
    [InlineData(
        "c4eab05d357007c632f3dbb48489924d552b08fe0c353a0d4a1f00acda2c463afbea67c5e8d2877c5e3bc397a659949ef8021e954e0a12274e",
        "43ba28f430cdff456ae531545f7ecd0ac834a55d9358c0372bfa0c6c6798c0866aea01eb00742802b8438ea4cb82169c235160627b4c3a9480",
        "03",
        "",
        "26b8f91727bd62897af15e41eb43c377efb9c610d48f2335cb0bd0087810f4352541b143c4b981b7e18f62de8ccdf633fc1bf037ab7cd779805e0dbcc0aae1cbcee1afb2e027df36bc04dcecbf154336c19f0af7e0a6472905e799f1953d2a0ff3348ab21aa4adafd1d234441cf807c03a00")]
    [InlineData(
        "c4eab05d357007c632f3dbb48489924d552b08fe0c353a0d4a1f00acda2c463afbea67c5e8d2877c5e3bc397a659949ef8021e954e0a12274e",
        "43ba28f430cdff456ae531545f7ecd0ac834a55d9358c0372bfa0c6c6798c0866aea01eb00742802b8438ea4cb82169c235160627b4c3a9480",
        "03",
        "666f6f",
        "d4f8f6131770dd46f40867d6fd5d5055de43541f8c5e35abbcd001b32a89f7d2151f7647f11d8ca2ae279fb842d607217fce6e042f6815ea000c85741de5c8da1144a6a1aba7f96de42505d7a7298524fda538fccbbb754f578c1cad10d54d0d5428407e85dcbc98a49155c13764e66c3c00")]
    [InlineData(
        "cd23d24f714274e744343237b93290f511f6425f98e64459ff203e8985083ffdf60500553abc0e05cd02184bdb89c4ccd67e187951267eb328",
        "dcea9e78f35a1bf3499a831b10b86c90aac01cd84b67a0109b55a36e9328b1e365fce161d71ce7131a543ea4cb5f7e9f1d8b00696447001400",
        "0c3e544074ec63b0265e0c",
        "",
        "1f0a8888ce25e8d458a21130879b840a9089d999aaba039eaf3e3afa090a09d389dba82c4ff2ae8ac5cdfb7c55e94d5d961a29fe0109941e00b8dbdeea6d3b051068df7254c0cdc129cbe62db2dc957dbb47b51fd3f213fb8698f064774250a5028961c9bf8ffd973fe5d5c206492b140e00")]
    [InlineData(
        "258cdd4ada32ed9c9ff54e63756ae582fb8fab2ac721f2c8e676a72768513d939f63dddb55609133f29adf86ec9929dccb52c1c5fd2ff7e21b",
        "3ba16da0c6f2cc1f30187740756f5e798d6bc5fc015d7c63cc9510ee3fd44adc24d8e968b6e46e6f94d19b945361726bd75e149ef09817f580",
        "64a65f3cdedcdd66811e2915",
        "",
        "7eeeab7c4e50fb799b418ee5e3197ff6bf15d43a14c34389b59dd1a7b1b85b4ae90438aca634bea45e3a2695f1270f07fdcdf7c62b8efeaf00b45c2c96ba457eb1a8bf075a3db28e5c24f6b923ed4ad747c3c9e03c7079efb87cb110d3a99861e72003cbae6d6b8b827e4e6c143064ff3c00")]
    [InlineData(
        "7ef4e84544236752fbb56b8f31a23a10e42814f5f55ca037cdcc11c64c9a3b2949c1bb60700314611732a6c2fea98eebc0266a11a93970100e",
        "b3da079b0aa493a5772029f0467baebee5a8112d9d3a22532361da294f7bb3815c5dc59e176b4d9f381ca0938e13c6c07b174be65dfa578e80",
        "64a65f3cdedcdd66811e2915e7",
        "",
        "6a12066f55331b6c22acd5d5bfc5d71228fbda80ae8dec26bdd306743c5027cb4890810c162c027468675ecf645a83176c0d7323a2ccde2d80efe5a1268e8aca1d6fbc194d3f77c44986eb4ab4177919ad8bec33eb47bbb5fc6e28196fd1caf56b4e7e0ba5519234d047155ac727a1053100")]
    // WPT WebCryptoAPI/sign_verify/eddsa_vectors.js (the keys are the last bytes of the PKCS #8 and SPKI structures)
    [InlineData(
        "0eff03458c28e0179c521de312c969b7834348ecab991a60e3b2e9a79e4cd9e480ef291712d2c83d047272d5c9f428664f696d2670458f1d2e",
        "ab4bb885fd7d2c5af24e83710cffa0c74a57e274801db2057b0bdc5ea032b6fe6bc78b8045365aeb26e86e1f14fd349d07c48495f5a46a5a80",
        "2b7ed0bc7795694ab4acd35903fe8cd7d80f6a1c8688a6c3414409457514a1457855bbb219e30a1beea8fe869082d99fc8282f9050d024e59eaf0730ba9db70a",
        "",
        "76897e8c50ac6b1132735c09c55f506c0149d2677c75664f8bc10b826fbd9df0a03cd986bce8339e64c7d1720ea9361784dc73837765ac2980c0dac0814a8bc187d1c9c907c5dcc07956f85b70930fe42de764177217cb2d52bab7c1debe0ca89ccecbcd63f7025a2a5a572b9d23b0642f00")]
    public void Ed448_Vectors(string privateKey, string publicKey, string message, string context, string signature)
    {
        var privateKeyBytes = Convert.FromHexString(privateKey);
        var publicKeyBytes = Convert.FromHexString(publicKey);
        var messageBytes = Convert.FromHexString(message);
        var contextBytes = Convert.FromHexString(context);
        var signatureBytes = Convert.FromHexString(signature);
        Assert.Equal(publicKey, Convert.ToHexStringLower(Ed448.GetPublicKey(privateKeyBytes)));
        Assert.Equal(signature, Convert.ToHexStringLower(Ed448.Sign(privateKeyBytes, messageBytes, contextBytes)));
        Assert.True(Ed448.Verify(publicKeyBytes, messageBytes, signatureBytes, contextBytes));

        // Another context, or another message
        Assert.False(Ed448.Verify(publicKeyBytes, messageBytes, signatureBytes, [.. contextBytes, 0]));
        Assert.False(Ed448.Verify(publicKeyBytes, [.. messageBytes, 0], signatureBytes, contextBytes));
    }

    [Fact]
    public void Ed448_Verify_NonCanonicalS_ReturnsFalse()
    {
        // RFC 8032 "Blank" with S + L: the equation still holds, but S must be less than L
        var publicKey = Convert.FromHexString("5fd7449b59b461fd2ce787ec616ad46a1da1342485a70e1f8a0ea75d80e96778edf124769b46c7061bd6783df1e50f6cd1fa1abeafe8256180");
        var signature = Convert.FromHexString("533a37f6bbe457251f023c0d88f976ae2dfb504a843e34d2074fd823d41a591f2b233f034f628281f2fd7a22ddd47d7828c59bd0a21bfd3980ff0d2028d4b18a9df63e006c5d1c2d345b925d8dc00b4104852db99ac5c7cdda8530a113a0f4dbb61149f05a7363268c71d95808ff2e652600");
        Assert.True(Ed448.Verify(publicKey, [], signature, []));
        var s = new BigInteger(signature.AsSpan(57), isUnsigned: true) + Ed448Order;
        Assert.True(s.TryWriteBytes(signature.AsSpan(57), out _, isUnsigned: true));
        Assert.False(Ed448.Verify(publicKey, [], signature, []));
    }

    // The points of order 1, 2 and 4 of edwards448: (0, 1), (0, -1), (1, 0) and (-1, 0), and non-canonical encodings
    [Theory]
    [InlineData("010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("fefffffffffffffffffffffffffffffffffffffffffffffffffffffffeffffffffffffffffffffffffffffffffffffffffffffffffffffff00")]
    [InlineData("000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000080")]
    [InlineData("000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("00000000000000000000000000000000000000000000000000000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff00")]
    [InlineData("010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001")]
    public void Ed448_Verify_SmallOrderOrInvalidPublicKey_ReturnsFalse(string publicKey)
    {
        var signature = Convert.FromHexString("533a37f6bbe457251f023c0d88f976ae2dfb504a843e34d2074fd823d41a591f2b233f034f628281f2fd7a22ddd47d7828c59bd0a21bfd3980ff0d2028d4b18a9df63e006c5d1c2d345b925d8dc00b4104852db99ac5c7cdda8530a113a0f4dbb61149f05a7363268c71d95808ff2e652600");
        Assert.False(Ed448.Verify(Convert.FromHexString(publicKey), [], signature, []));
    }

    [Fact]
    public void Ed448_Verify_SmallOrderR_ReturnsFalse()
    {
        // S = 0 and R = (0, 1): [0]B = R + [k]A only holds when [k]A is the identity, but R is rejected first
        var publicKey = Convert.FromHexString("5fd7449b59b461fd2ce787ec616ad46a1da1342485a70e1f8a0ea75d80e96778edf124769b46c7061bd6783df1e50f6cd1fa1abeafe8256180");
        var signature = new byte[Ed448.SignatureSize];
        signature[0] = 1;
        Assert.False(Ed448.Verify(publicKey, [], signature, []));
    }

    [Fact]
    public void Ed448_Verify_MalformedInputs_ReturnsFalse()
    {
        var publicKey = Convert.FromHexString("5fd7449b59b461fd2ce787ec616ad46a1da1342485a70e1f8a0ea75d80e96778edf124769b46c7061bd6783df1e50f6cd1fa1abeafe8256180");
        var signature = Convert.FromHexString("533a37f6bbe457251f023c0d88f976ae2dfb504a843e34d2074fd823d41a591f2b233f034f628281f2fd7a22ddd47d7828c59bd0a21bfd3980ff0d2028d4b18a9df63e006c5d1c2d345b925d8dc00b4104852db99ac5c7cdda8530a113a0f4dbb61149f05a7363268c71d95808ff2e652600");
        Assert.False(Ed448.Verify(publicKey.AsSpan(1), [], signature, []));
        Assert.False(Ed448.Verify(publicKey, [], signature.AsSpan(1), []));

        // A last byte with bits other than the sign of x
        var invalidKey = publicKey.ToArray();
        invalidKey[56] |= 0x01;
        Assert.False(Ed448.Verify(invalidKey, [], signature, []));
    }

    [Fact]
    public void Ed448_ContextTooLong_Throws()
    {
        var privateKey = new byte[Ed448.KeySize];
        Assert.Throws<ArgumentException>(() => Ed448.Sign(privateKey, [], new byte[256]));
        Assert.Throws<ArgumentException>(() => Ed448.Verify(new byte[Ed448.KeySize], [], new byte[Ed448.SignatureSize], new byte[256]));
        Assert.Equal(Ed448.SignatureSize, Ed448.Sign(privateKey, [], new byte[255]).Length);
    }

    [Fact]
    public void Ed448_InvalidPrivateKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => Ed448.GetPublicKey(new byte[56]));
        Assert.Throws<ArgumentException>(() => Ed448.Sign(new byte[58], [], []));
    }
}
