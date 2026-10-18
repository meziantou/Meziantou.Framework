using System.Security.Cryptography;
using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Tests.Cryptography;

// ML-DSA (FIPS 204)
public sealed class MlDsaTests
{
    // WPT WebCryptoAPI/sign_verify/mldsa_vectors.js: a hedged ML-DSA-44 signature of the message, without context, by the
    // key of WebCryptoAPI/util/mldsa_key_fixtures.js
    private const string WptSeed44 = "99155f633096da7cbe087a8948b84f767b10f901c823c240b1dd2bc87005c93e";
    private const string WptMessage = "ML-DSA test message for signing and verification. This is a test of ML-DSA implementation.";
    private const string WptSignature44 =
        "38a0eea5ceb9fdfaf0319d50abb630fedae45d3c41c4b1cf3266c304bf11ad53cb8eab8aa0c9ee963f6f19f5e17abc54eea85c39756b701d7b434ee9690d0b2f5d3d2d467297f37ea19e8db081900cd2" +
        "98d91cd72fd1f4a71ffa9c1fbf40668fbd883b0b1122ea9d16ab18c0c977dc798fec4811aabfacf28c47ca1635a19cd05933360097c002b0738000ac468d97587a55d96b02d90061bf9caa09356c6f3e" +
        "9d1c455047dd53c2b6b8465b329532566f96c342449706d058cb87d7e7d03d36703845dd9a3c3e472de1445b3c68b9b6273bc7f7aa75e3645c552aadee22739214f8ae6ba4204916afd76a7aa4864241" +
        "e3535d44d3b8a7066deca0b7e704874d48a328af695fcd7870a9b604b095b2484b74d6c6e1e6ee3f603c5ea9de74401f5f22c66ab61b4739502e12343437711c80bfb095568f09c50f27b4e32f85d3dc" +
        "4dbaf71a722ab1bec0393c45363cb3ae01273ff732eb1dbcfe54d774e75e4fc832d726290ad305c58893b19f683431a2b2e81ef01827a0d2c18941d9ff39c476674a79a1db54407b04e5cee72902a89f" +
        "404573023d01d381ec73baeca31cce0b6abd4689980d2a740beb0163b187f08d1bfe8f6b5425311fcdb5d041952584b007ebd1e4cebe91a6516a635808be42a7d92db1b9871859eba426646f55b2871b" +
        "0f0964227027dbd3af455d9d8a4adb0dabcf9890af2bae265833720aeb4fe9e604058f2cd6d8c20661b8b7dbcff9b26a5b3cc97e86f480e71dcb10303d85658502279b8e42946a3d7572b888d6bf2ce3" +
        "7aa39d5832ecce51e1cc59584d4e9438ff0d0876b99bd6e9b3ec9fcf13fe62b0c1cc47bf6466385f4f8b540d4d46000d340dd20f062c25b03d2783410e9d6ad6393e8b8e20d2c69cf14a2667b3225c2e" +
        "15a0de852feff0c6058e2f1df7cea02ffaa166f4f0b8c9e034353397af05afd394ec1b3be0dba8e57543c60e2b6a25a1b3293ba1919fbdc44a947c74febf95108b76f00890581cd70ebfdb069ea5f5ae" +
        "ed07efb1b63aafb48e8f28a939c14e4259d6de02063ba21e95ff0da9cbc07a1021aeaa8f92353a1c039c124fb994296a3be4890ae8a987eaf810a34a67705f6537cf989b305c3b737463ae4339a1a63c" +
        "ebb3d1d83254b1ad35d277e6eb38634087f58925928c40a08d787268a0fdcc992f8beba64257b55001fa82d89f8f4fc092def3eed0a3be7cdad63b25db498de0c2d194cd8f0f2a2fe88422c84f58a4cb" +
        "462940fa1204b68b491ba07a8427c1644f1cf95c8b3243c280534ae7c24b7662122f24046efa8482af886ee2a814f23fc3afb390c1033e7e55c44589e442391438951626f7f33857cf56fd15e3587a77" +
        "34555dcd70d69e13f6da7fe1f49f638cf87b24f3308a972fed18bc49e3b4be7a1aa94188c3afa8d488332f5109b6393ef4b4b66602546fd03515b27959ed3455e7a7aec900b5cc6146c8121395d5fec2" +
        "8ee0fd7f12774433d8c9e36f55fed8cfd93d58946c8b0ee29ec29b764c6cde1e1aa6a77741102c6e0b1828debe59256ddd98b9739baf0dd4110cdc8e48ce11a6defd926bfda322e648da2aa67248e616" +
        "21493a33d47ddb89015bde9c583a427fccbb75056b4d12cf23ca64c4f725d4c310f3794799058a67635291afb52cd7916164fcac6400eddeb658f2bb64ae208dd5b0e3275e0aba212eb08ddabd5e214a" +
        "6fb2d761751fe7ce1f61bbf14d1364deed82e8212b03c3244107c5680077021ebffd15af218b6d58c48354f1160670298590f1e4d5d1ad129a0231179ca72bcef925860e9969b127580c6b68f9df02cd" +
        "d9d078ecf1b9651d701673a5c537f5bfa4163712d851c80bf8bb8f751b02a28f85caad9521630ece61a55929f0cbb0a85ff648e026c58b421a20bb1101e57dad40590e1705d48d28976b34e70a510c01" +
        "bef30e8e26eea190bc41f5629c947df791c157f99cb9f004b86dbe059701a5c67bf6ebaf4bd0ff267b722bf65713c5f632a3513f5acecbf42fa4dde9477b4ade5c4eee2b7862ba49bbdf99ec3a4e567c" +
        "58f1fa1fef2ef3303441cc0d7f9d75ebd1acf2607b9071d5841ba84d684956d20e644215bd3978d921fa64f6b2222bdb577ab41fcd13c333ef394a2bab97e3295aee59bbfd609a88dd7224373c89bdd1" +
        "c3c3037b8222047a26e9729b1e0f5e3b3502152f1c000cc9a60a47627d39f18a407f7d92e2e1f1423b9481ede21fd1afb63f21b4c18feed98c31c8d18bfaece7eb3b5cf843a15d3aca2894e5d7c57e93" +
        "0953fa4b86c70dff17b8a7b50763dc9a36e1c7d61bfe9a3daa86eee3c1e5121a643d9f9d600e0d8cfe4f6d71d0899b479da9826a8a08ab744dcd66a4fa638b3832541db0abd6cd2c9c9007eca7b6df86" +
        "4065d426613e7aedc1bdfd3962d3efd597b58d62da407257949e5efef7a7c1c19c2345d7278d5d663930d1d83b9e9f01a74d64aac291afc05148ca5e830562fca2236e3ee29a30e676f74f2660579718" +
        "06f48b1d05a2e3d65cc80c91ecb6c6a74be6bb63bc3911c2d452102e4f6f2df684f311209e264edae17340d5c9b9bf99207c2a47799638ea9851fb55d5e87f8df944f26473f79bcb7b004cc219684f4c" +
        "1f0318b9180edf149b87ec65d06f694f86730d90a14a6da32f18efe69b36e8f97ff368c122ef5ec8b6e5d50a99ad08af11db068248e574fafa31024d773f57934a495d21e9e7fa16bcac49945a7d9b7e" +
        "f35de3d163de8664631bb3c3828a089242e081a3eeeb30e558709b5358fd792af91fd95109e2ca65e87b5888d6d73f249e6eab16ead97d55a91915f20c8c3782283d0875405fc49dbe26da9c26dd86df" +
        "ce9c0d6446b5f70fbafb2aead9bf9f37b8fbb3f04dc02a68d0f69b382d52b4bb8a43d812b4bd746fe85fca994fd2ab00e6c07e2975dab6be11e14785a1076c183fbc50f8e1c116ad3cd68db6b98c495e" +
        "b88f71634164e806227cf38244b7cab750aaf3463b0e153a89239adf0067568d898075396efdfe0371c79f4038efc44e2dca19bdf9eafdd984178b7462ec10e109d876bc3ef4ab22fd8ad1d2f9f94dfc" +
        "90d60348421ec42763f1ec8849a7fa6ed7d9cc8c61f7a1b03b2d7527ec5089c2dc345f9f2e1203ad8ac4f6705476ba70db6cf1533f673ad5d1fe1f6bc530351e41f747135e7da9658bdfdb713adda86e" +
        "52814329c00ece186e53b05a06727e37ec4cfaa0f95c9b46bae4087690e8d942bd215d27d239ebf0a548dcebd26b9e6abc66c53789549f019474137f46a6c121ca6ac6e9d965c3c49017f9085e9f86a7" +
        "b87c7309902f86da63d3a2e9bc251f22194a5051556b7784858d96a0a3a7b2c7d1d4204363656d70717b8e97b8cbcddffd1d1f232a3e474b5a68888a959fa0abbdbfc0ccd9edf1171d29618b94aada00" +
        "000000000000000000000000000000001221373f";

    // The raw seeds and the SHA3-256 of the raw public keys of WPT WebCryptoAPI/import_export/ML-DSA_importKey_fixtures.js
    public static TheoryData<int, string, string> KeyGenVectors => new()
    {
        { 44, WptSeed44, "4ad260bb9187ef10605d1e05a75c6e4693eb5a3e4979e9a0de37b961e4768106" },
        { 65, "84a4894b7b46a4b2039cce10c31a85bab0c36630fe231d42671143982607828b", "670de1161d233ea25d384a59eca7065f273db92115b66dc52f4c1afee464f3ff" },
        { 87, "a1506d914c6d658c7527e43397dd6d4c25f6a47974335a4cd03bfe6983441251", "8dc26f2309517f26449fdc436275b924178c9a641569117ebf339b2f95a7b52d" },
    };

    // Generated with OpenSSL 3.6 (genpkey -pkeyopt hexseed, pkeyutl -sign with -pkeyopt deterministic:1 or
    // -pkeyopt hextest-entropy): the seed is (p, p + 11, p + 22...) where p is the parameter set; the SHA3-256 of the public
    // key; the message ("long" is the 300 bytes 01 0E 1B .. (step 13)); the context; rnd; the SHA3-256 of the signature
    public static TheoryData<int, string, string, string, string, string> SignatureVectors => new()
    {
        { 44, "a2e9e9a0f12c79c51128201c1c7a24c2d7d65b042fa2ff20b60948e35e718fd5", "6d65737361676520746f207369676e", "", "0000000000000000000000000000000000000000000000000000000000000000", "d84c8fa4c08cbf0771070d4364b82ed38b400f6cdfd00ba6e9c267b34e9119f4" },
        { 44, "a2e9e9a0f12c79c51128201c1c7a24c2d7d65b042fa2ff20b60948e35e718fd5", "long", "636f6e74657874", "80858a8f94999ea3a8adb2b7bcc1c6cbd0d5dadfe4e9eef3f8fd02070c11161b", "3ee239c8fab1e56ee06145fb5ccc0608a1fbae32248dfc7243c6944d2dff174f" },
        { 65, "a48c163165c3e8989ce5d996705bdf339f3c9b064450f6155063457e9e814c4b", "6d65737361676520746f207369676e", "", "0000000000000000000000000000000000000000000000000000000000000000", "82bf9b57f744bb5f0b15e7b759f9ba6c0c1d01c5292795d30a9ef0a6b827e23a" },
        { 65, "a48c163165c3e8989ce5d996705bdf339f3c9b064450f6155063457e9e814c4b", "long", "636f6e74657874", "80858a8f94999ea3a8adb2b7bcc1c6cbd0d5dadfe4e9eef3f8fd02070c11161b", "6aec5d5c6a7ce2e960040b88662ee6749b1cb220a4d4e68053ad83d9bfecf148" },
        { 87, "47b87477a6956069f18af2bdf5669c0288112a3abf89a5e59cc217c25693a473", "6d65737361676520746f207369676e", "", "0000000000000000000000000000000000000000000000000000000000000000", "8fcf6227fcbd26eb0352e362e537a2100cde2b04279745c5b145c10ba493d39b" },
        { 87, "47b87477a6956069f18af2bdf5669c0288112a3abf89a5e59cc217c25693a473", "long", "636f6e74657874", "80858a8f94999ea3a8adb2b7bcc1c6cbd0d5dadfe4e9eef3f8fd02070c11161b", "7345c753d1018399d493d30e572b7b3d603e85dc1be2a66c52b70fe937fe4e83" },
    };

    public static TheoryData<int> ParameterSets => [44, 65, 87];

    [Theory]
    [MemberData(nameof(KeyGenVectors))]
    public void GenerateKeyFromSeed(int parameterSet, string seed, string expectedPublicKeyHash)
    {
        var (publicKey, privateKey) = MlDsa.GenerateKeyFromSeed(parameterSet, Convert.FromHexString(seed));
        Assert.Equal(expectedPublicKeyHash, Sha3(publicKey));
        Assert.Equal(MlDsa.GetPublicKeyLength(parameterSet), publicKey.Length);
        Assert.Equal(MlDsa.GetPrivateKeyLength(parameterSet), privateKey.Length);

        // skEncode starts with ρ (the start of the public key)
        Assert.Equal(publicKey.AsSpan(0, 32).ToArray(), privateKey.AsSpan(0, 32).ToArray());
    }

    [Fact]
    public void Verify_WptVector()
    {
        var (publicKey, _) = MlDsa.GenerateKeyFromSeed(44, Convert.FromHexString(WptSeed44));
        var message = System.Text.Encoding.UTF8.GetBytes(WptMessage);
        var signature = Convert.FromHexString(WptSignature44);
        Assert.True(MlDsa.Verify(44, publicKey, message, signature, []));
        Assert.False(MlDsa.Verify(44, publicKey, message, signature, [0]));
        Assert.False(MlDsa.Verify(44, publicKey, message.AsSpan(1), signature, []));
    }

    [Theory]
    [MemberData(nameof(SignatureVectors))]
    public void Sign_KnownAnswer(int parameterSet, string expectedPublicKeyHash, string message, string context, string randomness, string expectedSignatureHash)
    {
        var seed = Enumerable.Range(0, 32).Select(i => (byte)(parameterSet + (i * 11))).ToArray();
        var messageBytes = message == "long" ? Enumerable.Range(0, 300).Select(i => (byte)(1 + (i * 13))).ToArray() : Convert.FromHexString(message);
        var contextBytes = Convert.FromHexString(context);
        var (publicKey, _) = MlDsa.GenerateKeyFromSeed(parameterSet, seed);
        Assert.Equal(expectedPublicKeyHash, Sha3(publicKey));

        var signature = MlDsa.Sign(parameterSet, seed, messageBytes, contextBytes, Convert.FromHexString(randomness));
        Assert.Equal(MlDsa.GetSignatureLength(parameterSet), signature.Length);
        Assert.Equal(expectedSignatureHash, Sha3(signature));
        Assert.True(MlDsa.Verify(parameterSet, publicKey, messageBytes, signature, contextBytes));
    }

    [Theory]
    [MemberData(nameof(ParameterSets))]
    public void Sign_RoundTrip(int parameterSet)
    {
        var seed = RandomNumberGenerator.GetBytes(MlDsa.SeedLength);
        var (publicKey, _) = MlDsa.GenerateKeyFromSeed(parameterSet, seed);
        var message = "Hello, world!"u8.ToArray();
        var context = new byte[MlDsa.MaxContextLength];
        var signature1 = MlDsa.Sign(parameterSet, seed, message, context);
        var signature2 = MlDsa.Sign(parameterSet, seed, message, context);

        // The hedged signatures are randomized
        Assert.NotEqual(signature1, signature2);
        Assert.True(MlDsa.Verify(parameterSet, publicKey, message, signature1, context));
        Assert.True(MlDsa.Verify(parameterSet, publicKey, message, signature2, context));
        Assert.True(MlDsa.Verify(parameterSet, publicKey, [], MlDsa.Sign(parameterSet, seed, [], []), []));
    }

    [Theory]
    [MemberData(nameof(ParameterSets))]
    public void Verify_InvalidInput(int parameterSet)
    {
        var seed = new byte[MlDsa.SeedLength];
        var (publicKey, _) = MlDsa.GenerateKeyFromSeed(parameterSet, seed);
        var message = new byte[] { 1, 2, 3 };
        var signature = MlDsa.Sign(parameterSet, seed, message, [], new byte[32]);
        Assert.True(MlDsa.Verify(parameterSet, publicKey, message, signature, []));

        // Wrong message, context, public key, lengths
        Assert.False(MlDsa.Verify(parameterSet, publicKey, [1, 2, 4], signature, []));
        Assert.False(MlDsa.Verify(parameterSet, publicKey, message, signature, [1]));
        Assert.False(MlDsa.Verify(parameterSet, publicKey, message, signature, new byte[256]));
        Assert.False(MlDsa.Verify(parameterSet, MlDsa.GenerateKeyFromSeed(parameterSet, new byte[32].Select(_ => (byte)1).ToArray()).PublicKey, message, signature, []));
        Assert.False(MlDsa.Verify(parameterSet, publicKey, message, signature.AsSpan(1), []));
        Assert.False(MlDsa.Verify(parameterSet, publicKey.AsSpan(1), message, signature, []));
        Assert.False(MlDsa.Verify(parameterSet, publicKey, message, [1, 2, 3, 4, 5], []));
        Assert.False(MlDsa.Verify(parameterSet, publicKey, message, new byte[signature.Length], []));

        // Every modified byte of the signature (c̃, z, the hint indices and the hint counts) makes it invalid
        for (var i = 0; i < signature.Length; i += 7)
        {
            var modified = signature.ToArray();
            modified[i] ^= 0x10;
            Assert.False(MlDsa.Verify(parameterSet, publicKey, message, modified, []));
        }

        // A malformed hint: the last count is greater than ω
        var invalidHint = signature.ToArray();
        invalidHint[^1] = 0xFF;
        Assert.False(MlDsa.Verify(parameterSet, publicKey, message, invalidHint, []));
    }

    [Fact]
    public void InvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MlDsa.GenerateKeyFromSeed(42, new byte[32]));
        Assert.Throws<ArgumentException>(() => MlDsa.GenerateKeyFromSeed(44, new byte[64]));
        Assert.Throws<ArgumentException>(() => MlDsa.Sign(44, new byte[32], [], new byte[256]));
    }

    private static string Sha3(byte[] data) => Convert.ToHexStringLower(Keccak.Sha3(256, data));
}
