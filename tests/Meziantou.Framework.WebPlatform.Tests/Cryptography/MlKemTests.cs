using System.Security.Cryptography;
using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Tests.Cryptography;

// ML-KEM (FIPS 203)
public sealed class MlKemTests
{
    // WPT WebCryptoAPI/encap_decap/ml_kem_vectors.js (ML-KEM-512)
    private const string WptCiphertext512 = "0e488758dcd45552565bece5b8bda4523544de14eed13fec29cf926016158913143224db555a2d6d42c2ce30f63b7234b825ab11b6b10bbac19835c23cac07e3b37fed08a1eb886199ee2db95e0135af1c21d375edd0c1346617b66cfcd9cda85a8028da745d9d3653fc5b80c6d29b4cd9b02d6c5ac8e7c027a70258573198bc72404a31de1f443854e7becb3bcf390a6fe7c86c3a0b149e51a5103b2dd84b96d20d1f3b1a5308dd9411fa87754d7d8572ad583627db9aa98d2fe09b6fddca3ca8f94e643b56d92babaf105e52ba7082e334f7b8e4866f45f415a1d46901ba932c298dcdb0e964d18a0b725ad058c2d5ea7213ff7f44afeacf1b6a1029fd93ce50a6b34bf51e8323c7da586006d3c1f78c98ca79ccbcff1c14a0309496bfb4f4bc24402a47912f98005defbc082b96c206dca02c9327d7a4166b9ccfea2c45326a98a1ab84dcac0ea35a2b1a2c401b911cdf27652338e74beafab4d945c3f13ca296507c87f73e348bb443b7fa7ec05fa0e35b9f191ec8bade966092748cda729e25a4cf8d9f8fc06daf4a60f3b98530282eabbf57674d34f96cf537639695357825111e0ca9afc04a789df283b125d9f9f1b55927c93bd48e1bcc62f36e7628ea8bddf448ae0ce298442f1a8c8d31dc23b96832603cf2036fbae96fc0fbd2570aa5d6517cc352dddfa944524a4ca6c5c76db98cc796c8f9cfe392748cb3b22eaf5843457729a7b6a801ca0ca9b4656abc9df0911a3d1a11c85be1f41360e85a47e766dcd99cd0a35f1fa6c0945805c846d2a08655363401ae9886d426c3a15aefd0984d5a128c0318f1818d734f624bc6e73a5d42f81329764c3b5186e7d6872e9e3e22de4faef96ae1a772fa1e8fc195e8601dd5468acb80619c658e06e789fab5fd688f8c08c36ac884135232ce832e68a0c15f0165b84c3882afac0bbe4eef7a42de92174af0f685fe47baf2bc797ced19df1808728ace5786cfc5541719ab0ceba544335c1f02ec8b80dde750749743a8a3b91a8e1d2197956c96a02dc51baded9d8846899dbd4ac557a74c4e7843a4c3431132deafa3f418d7c39d5276";

    // The raw seeds and the SHA3-256 of the raw public keys of WPT WebCryptoAPI/encap_decap/ml_kem_vectors.js and
    // WebCryptoAPI/import_export/ML-KEM_importKey_fixtures.js
    public static TheoryData<int, string, string> KeyGenVectors => new()
    {
        { 512, "3e4262cad144026de9bd35bf203ffb40263c8c765d05d2a4d00a8ae92507d2d2c560b8435a054d816e5f2535a3df91833ccad263e58e3c34555cd8c57325ad9d", "034ac48e9889c306e24109daee5135c1ad2c64e23a3057df66aa7a8dc0ac557e" },
        { 768, "46973b12a054a89f47d53af583ad94676bbe8ac1046b071d666f55813b6898aaaacb5559d007ea6814683b499d8be1a0f5bdf0527ad9dfa8669038fb33be26c5", "f03d3899d9da0484e92b85d9f2edfac17f1e9f8ede28fadb46a6a8a2388f487b" },
        { 1024, "d710a2bc4789368cf20af5feeedc8b9a8d0a88ebd770f91e9a441146526f6b0e711d945a8786bdce9f167ddc5da8c7ab96dc305d6a366319d5f8a6924f183959", "cabb4f159a046ce9c7887f79a267500beb9a417f158e73a41e9cf67f97685b84" },
        { 512, "a526c1a4847a68ad53d6e31fe7b798e4608c4117538308cdf5c07ae2f45e866d763cadcb194bbd901625a3354e95b95082eba1318da00b2898128e1e38fc81d3", "37bb9b0fb546bca20434c7eac24c3b5041fab2f9fae45fca4749654924a8a772" },
        { 768, "a6c81d64c63050a48fd7026c6aa2e76b6f8f020eb7fd9c115bd54bc87059f337c909f0aac05634e47a10498b1e0be0758f027c153f5bd25ea07b63bdac3debbf", "689b9f361e0855693ce74b4386b79391112ac0dd6253cd440ddeff4e742af0f2" },
        { 1024, "9875b2e6b586f481a77da608b55fced28870db7d06767a5326f439cd32623b2ee8421191ccfca2b97474f21c84c1fb4127036591ae15c390ea0e9e2031398e71", "498628effe131af95288cf9f05cbe88d84c04fdecefce304c5e839af44f86f62" },
    };

    // Generated with OpenSSL 3.6 (genpkey -pkeyopt hexseed, pkeyutl -encap -pkeyopt hexikme, pkeyutl -decap): the seed is
    // 00 07 0e .. (step 7); the SHA3-256 of the encapsulation key and of the ciphertext, the shared key, and the shared key
    // of the implicit rejection when the first bit of the ciphertext is flipped
    public static TheoryData<int, string, string, string, string, string> EncapsulationVectors => new()
    {
        { 512, "4245484b4e5154575a5d606366696c6f7275787b7e8184878a8d909396999c9f", "55bebc0f37c7391b88a3fc9a88112b6415f5b403f2d30dd01b51def090563500", "44a93f21ad365c4db64543afd54bce02ecf0cca0ff2de6d527d71753b5f1b91a", "da0c85a527efbb0bb911d55dff7c7e20589429890f347f0e8df407884e90d1e2", "75bcbf41ed14dd588f1faeeb10a2d755590269e1220c43c0846358303264e51b" },
        { 768, "4346494c4f5255585b5e6164676a6d707376797c7f8285888b8e9194979a9da0", "c20a94139c40880de737b8710609763f24bd5680d9991428e5f216ec9eabab36", "b2d7f21baf494d10d476b784eb6eb3668617aebaac94c78d3e69223602a40684", "2e22270b7d5866909eacfaa8da4119639ba812146783f7e495a3975fd378ce96", "5e88e4a7f13e0f9fa16daf676894fc81efec9bb104bfb34dea70ca166cd1e43b" },
        { 1024, "44474a4d505356595c5f6265686b6e7174777a7d808386898c8f9295989b9ea1", "c8155a56f144de85fb240b5d60f6a2a47c1cd11cc750ce74e80cdbfcce58626b", "d9a9c9483eaf5768b4f454639e291c1133855cd09e2befdd8cbfa69799bbaba1", "01a4cd3547224aa3a050209c794d7dbcfc682f10e45dc3d625df8d09091cd5b1", "1c0c5a5b3c52e8bef5186c7f81582202285f4908f947b13b65a03332fd479108" },
    };

    public static TheoryData<int> ParameterSets => [512, 768, 1024];

    [Theory]
    [MemberData(nameof(KeyGenVectors))]
    public void GenerateKeyFromSeed(int parameterSet, string seed, string expectedPublicKeyHash)
    {
        var (encapsulationKey, decapsulationKey) = MlKem.GenerateKeyFromSeed(parameterSet, Convert.FromHexString(seed));
        Assert.Equal(expectedPublicKeyHash, Sha3(encapsulationKey));
        Assert.Equal(MlKem.GetEncapsulationKeyLength(parameterSet), encapsulationKey.Length);
        Assert.Equal(MlKem.GetDecapsulationKeyLength(parameterSet), decapsulationKey.Length);
        Assert.True(MlKem.IsValidEncapsulationKey(parameterSet, encapsulationKey));
        Assert.True(MlKem.IsValidDecapsulationKey(parameterSet, decapsulationKey));
    }

    [Fact]
    public void Decapsulate_WptVector()
    {
        var seed = Convert.FromHexString("3e4262cad144026de9bd35bf203ffb40263c8c765d05d2a4d00a8ae92507d2d2c560b8435a054d816e5f2535a3df91833ccad263e58e3c34555cd8c57325ad9d");
        var (_, decapsulationKey) = MlKem.GenerateKeyFromSeed(512, seed);
        var sharedKey = MlKem.Decapsulate(512, decapsulationKey, Convert.FromHexString(WptCiphertext512));
        Assert.Equal("6b15add5b366b22c668a1d81901d2ee2113303b6afa82eaf6764dbead766b8fc", Convert.ToHexStringLower(sharedKey));
    }

    [Theory]
    [MemberData(nameof(EncapsulationVectors))]
    public void Encapsulate_Deterministic(int parameterSet, string message, string expectedEncapsulationKeyHash, string expectedCiphertextHash, string expectedSharedKey, string expectedRejectionKey)
    {
        var seed = Enumerable.Range(0, 64).Select(i => (byte)(i * 7)).ToArray();
        var (encapsulationKey, decapsulationKey) = MlKem.GenerateKeyFromSeed(parameterSet, seed);
        Assert.Equal(expectedEncapsulationKeyHash, Sha3(encapsulationKey));

        var (sharedKey, ciphertext) = MlKem.Encapsulate(parameterSet, encapsulationKey, Convert.FromHexString(message));
        Assert.Equal(expectedCiphertextHash, Sha3(ciphertext));
        Assert.Equal(expectedSharedKey, Convert.ToHexStringLower(sharedKey));
        Assert.Equal(expectedSharedKey, Convert.ToHexStringLower(MlKem.Decapsulate(parameterSet, decapsulationKey, ciphertext)));

        // Implicit rejection (FIPS 203 §6.3): the shared key of an invalid ciphertext is J(z || c)
        ciphertext[0] ^= 1;
        Assert.Equal(expectedRejectionKey, Convert.ToHexStringLower(MlKem.Decapsulate(parameterSet, decapsulationKey, ciphertext)));
    }

    [Theory]
    [MemberData(nameof(ParameterSets))]
    public void Encapsulate_RoundTrip(int parameterSet)
    {
        var seed = RandomNumberGenerator.GetBytes(MlKem.SeedLength);
        var (encapsulationKey, decapsulationKey) = MlKem.GenerateKeyFromSeed(parameterSet, seed);
        var (sharedKey1, ciphertext1) = MlKem.Encapsulate(parameterSet, encapsulationKey);
        var (sharedKey2, ciphertext2) = MlKem.Encapsulate(parameterSet, encapsulationKey);
        Assert.Equal(MlKem.GetCiphertextLength(parameterSet), ciphertext1.Length);
        Assert.Equal(MlKem.SharedKeyLength, sharedKey1.Length);
        Assert.NotEqual(ciphertext1, ciphertext2);
        Assert.NotEqual(sharedKey1, sharedKey2);
        Assert.Equal(sharedKey1, MlKem.Decapsulate(parameterSet, decapsulationKey, ciphertext1));
        Assert.Equal(sharedKey2, MlKem.Decapsulate(parameterSet, decapsulationKey, ciphertext2));
    }

    [Theory]
    [MemberData(nameof(ParameterSets))]
    public void Encapsulate_InvalidEncapsulationKey(int parameterSet)
    {
        var (encapsulationKey, _) = MlKem.GenerateKeyFromSeed(parameterSet, new byte[MlKem.SeedLength]);

        // Type check: the length
        Assert.False(MlKem.IsValidEncapsulationKey(parameterSet, encapsulationKey.AsSpan(1)));
        Assert.Throws<CryptographicException>(() => MlKem.Encapsulate(parameterSet, encapsulationKey.AsSpan(1).ToArray()));

        // Modulus check: the second coefficient is 0xFFF ≥ q
        var invalid = encapsulationKey.ToArray();
        invalid[1] |= 0xF0;
        invalid[2] = 0xFF;
        Assert.False(MlKem.IsValidEncapsulationKey(parameterSet, invalid));
        Assert.Throws<CryptographicException>(() => MlKem.Encapsulate(parameterSet, invalid));

        // The coefficient q - 1 = 0xD00 is valid, q = 0xD01 is not
        invalid[1] = (byte)((invalid[1] & 0x0F) | 0x00);
        invalid[2] = 0xD0;
        Assert.True(MlKem.IsValidEncapsulationKey(parameterSet, invalid));
        invalid[1] = (byte)((invalid[1] & 0x0F) | 0x10);
        Assert.False(MlKem.IsValidEncapsulationKey(parameterSet, invalid));
    }

    [Theory]
    [MemberData(nameof(ParameterSets))]
    public void Decapsulate_InvalidInput(int parameterSet)
    {
        var (encapsulationKey, decapsulationKey) = MlKem.GenerateKeyFromSeed(parameterSet, new byte[MlKem.SeedLength]);
        var (_, ciphertext) = MlKem.Encapsulate(parameterSet, encapsulationKey);

        // Ciphertext type check
        Assert.Throws<CryptographicException>(() => MlKem.Decapsulate(parameterSet, decapsulationKey, ciphertext.AsSpan(1)));

        // Decapsulation key type check and hash check
        Assert.Throws<CryptographicException>(() => MlKem.Decapsulate(parameterSet, decapsulationKey.AsSpan(1), ciphertext));
        var invalid = decapsulationKey.ToArray();
        invalid[^40] ^= 1;
        Assert.False(MlKem.IsValidDecapsulationKey(parameterSet, invalid));
        Assert.Throws<CryptographicException>(() => MlKem.Decapsulate(parameterSet, invalid, ciphertext));
    }

    [Fact]
    public void InvalidParameterSet()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MlKem.GenerateKeyFromSeed(256, new byte[MlKem.SeedLength]));
        Assert.Throws<ArgumentException>(() => MlKem.GenerateKeyFromSeed(512, new byte[32]));
    }

    private static string Sha3(byte[] data) => Convert.ToHexStringLower(Hashing.Sha3.HashData(256, data));
}
