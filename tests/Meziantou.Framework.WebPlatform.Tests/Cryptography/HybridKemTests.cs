using System.Security.Cryptography;
using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Tests.Cryptography;

// Hybrid KEMs MLKEM768-P256, MLKEM768-X25519 and MLKEM1024-P384 (draft-irtf-cfrg-concrete-hybrid-kems-04)
public sealed class HybridKemTests
{
    // The test vectors of draft-irtf-cfrg-concrete-hybrid-kems-04 appendix B: vector i has the seed (i i .. i) and the
    // randomness (0x64+i 0x64+i ..). The SHA-256 of the encapsulation key and of the ciphertext, and the shared secret.
    // Vector 0 is also the one of WPT WebCryptoAPI/encap_decap/hybrid_kem_vectors.js.
    public static TheoryData<string, int, string, string, string> DraftVectors => new()
    {
        { "MLKEM768-P256", 0, "2627712d7aa5b010d292dfaef982ff9dd66ccdadb35804780b9d891c7fe29526", "5e695b18578462c0d8ab9bbefbe733040379dc08598f17fc2a7627fdeb9d2c42", "9bd018e869bb01b63fb8f5da374a73d347ea14cb2bc570b13d0908e2288ec456" },
        { "MLKEM768-P256", 1, "782cbdde9ed5a6d73d34aee4866d4f0bbb36c2cb1a29cf475fabf9b074a024c0", "44bb6fc64ab6f02b020ccea70aa846049382cded47e372bf68c6d5469dcb07e5", "d33a83edb4fb5a7508686ba4ddff001c17ba8890e9e3be067d50c959b512f9be" },
        { "MLKEM768-P256", 2, "7efbb25cf08810bb3cc6e8cad253b5298acd40685fa76e2775cb62d3b5e930fd", "527fb74b221a3c8c1879425b57ac6dcd95e8bb0316ddc22549620b3a914e3400", "d17a7ca859a18cfbb5fc7bcba2ec54aa0e9ac44959023199fff5b60db9f24401" },
        { "MLKEM768-P256", 3, "ee3fc47e702167f7cc17036cb6f803702667244fca759c4023ef4bcd5342e188", "6adba3f1ae5c5559e69ed0d0219013d67b1098933a967ab7cedaad179e95c273", "b3db69cc0385b1ba716128b70bd4b93288f945f418cef51d122ca7a4de564a81" },
        { "MLKEM768-P256", 4, "a9048ec8900cd0230203b15caa2271c143b01261cd12c0be4cca4ab9a3392a03", "720a4020ac27af3e6ea32ec23db91bd6f250455012ef2dc1fc55966db8d81898", "794e2f99d9a890c338c24a018035f8f51db576faf0cde96cf777a81bdccce864" },
        { "MLKEM768-P256", 5, "964bdee66a8990c254b15100b43018e113327f072b9ab6c3df7be579169c4f3a", "bf805b15e49334f4cea489fcb0b8c0b88d33374aec3406e2d0f7fce639420091", "6f1c317d16608f309e6b59a48be62722081f4e2de9e2742a6d0e94679ba856f7" },
        { "MLKEM768-P256", 6, "339aff50f21f9f48214ae8be4e7c09333fd5ace9023230da5529c74e849ce6a2", "07458f4bdc82542445b6e940c6fc395f752bf6189fa258e8f84488d88b14b7df", "e324b3ae9780cc6d0a54aa4262db9c9c5948ebd45df2f3d10eb22ee23bdaddea" },
        { "MLKEM768-P256", 7, "3e007f14c24b94d5908170a9c6978323db3c469f8d803fc0729b2ec61a8daccb", "27e1484d79c0fc5b4cb199c86f23093f8a1dec19802c5021dcf8fd997b6021f6", "5f8deab6c11f05ee208ba968d28917f8219ba2c0c30d41651c3382f4d56483c6" },
        { "MLKEM768-P256", 8, "18d2efb08062e8e21766e262d775c8c9523531c65c404dbd0b06243d4f8444f9", "30c1efb5956d029af905fa09c14d7b139db37b3f02191e772928a103d20aa6b7", "f9d4fe89be29e5438f8ed2ee8d368c365e550b6336c55a4d91f3d96dafb3191a" },
        { "MLKEM768-P256", 9, "29370a7b2117953ac73e395160daa701999d7da86991bdbfb060d0d6d15b5302", "8e365995103f0cd2ee93dfb32f37c8bd1df4e2202274964f0f2e8b26523d6cc3", "a0071e94b4d0275ed91dfe4f3fe87dd4a534269c7a5991fdc27ac31933fad24c" },
        { "MLKEM768-X25519", 0, "3bb0b003f553f49f38bab31546b7f4fdd323c74cbf4aaf97d9703ede4e83eff7", "89ef4ccf41483c6fa62744d23a0fd6072a2e637f74564db7f618d1516ed8a6cd", "e5ba94031ea6efd69c09c254f6d9783136ba6037e2d4c43bcccf19d6f3f4343a" },
        { "MLKEM768-X25519", 1, "f37b99b2db732a7df55f61fa442606133b1aeea462df9f8225fb5c82674523db", "bb0f04a0f45979d8b398d71ba4302639ac354cfe384fff4c95ebacf1a4da1688", "750300db25bff9620e893c2c6fcab9bf04d7f2e543b5b39420485626fa274908" },
        { "MLKEM768-X25519", 2, "01eba7f63ebe3bde984385c499280859aac9a8c403804fe47ebbf31982d74353", "f848429823954ce959cce81de71f65f6a55335b9792d528ba852611b41e0b7da", "87292f18b2e7af74bb8839ddee15e832d2f4bfac14dc84f824906d951436aafa" },
        { "MLKEM768-X25519", 3, "5b8e64e6709228bd7e4ed2942858a210fa08a85e83ee31c65ef17e119f77755c", "82dc3c66ad732607ff6ec73b7463039aede7b946f14a0e957c20b776e706e41a", "38c469d91f19aeb79dbf1aaae4d1195216c86186b00a798ea3a6e544cf9c074a" },
        { "MLKEM768-X25519", 4, "bb4ce870113dc866909cc20bb80054c0b811cd3d20ad40ee0e60d9e256188509", "133677580de2007af9e00b2de832a2eb76f6a8f2f37b9d9c797432945e96985f", "a638747e2b93607e6a651d528435afe07e8733ccd150507b96b639f6ff4a10ee" },
        { "MLKEM768-X25519", 5, "ceb8809f4271307be44f8593fa698810057cfba2164a6dc772fea43223ea7e51", "42b31d85164d1b3215cc60e6f99965f080c2e5766ff8d0c3ac346aebe6d02c89", "0594af7fe7a398ffe68f37a9f73506d6a3f7fd82b24e26f4b93269684ed47172" },
        { "MLKEM768-X25519", 6, "01402bf2cd8c09854fc80f9e07259f9c7ca840f6e0fc498e7a3999c4ee9aaebb", "e8da3a6c8c32f2768311c309573f5670cbab84dba561075eae5da8fe086f5a83", "d11018de4d36cb4544612190b4613a7b76c703fc4cb782fb26de49adde017e97" },
        { "MLKEM768-X25519", 7, "74943b4ecd441ec7f92d22c3fb377a29cd7fb96687efda8fae74ae2219a1c279", "55b3619873d0c800e832fc9a7fd98f4d7c58d397748f3b22061b75632689e39b", "eae6cff2b4d6971efa91c6333986693db4f5c46207af27ce6f5964c0eb4aef50" },
        { "MLKEM768-X25519", 8, "d3fb4ce4f463fd1f51172cc01526d7ec8b0950b762b05933d15d646ebf7d7ccd", "33da7e954dc18a7dea221771abac5d82ef783178775c30875bf1bec23680fe75", "83ec9c8227c5306cb0e60bdf12678a5de9e24fe51855290a64f49e9795415661" },
        { "MLKEM768-X25519", 9, "22572a534ca03a68e5cacdc1ed7527ea136db8793e20138eebb0e5ae2a5e2fb9", "26595ec53499e7ea9d12924431aeaf84978d9108f270a6d42653b6323752722d", "d1b5f13316ff420d4ca22c9a8e3f93d27f735d1da53ebc979cce23f9747e3261" },
        { "MLKEM1024-P384", 0, "9275d0f9dfe19bd24a15364a9e010baf1ec4b78211c03263c3d6a94025de98f8", "05ec1e48c1dc04c88929fbf38892f556c3ce729fd86af361dc5d4aff555dc59c", "8c028c6ea72a1c59408e2b15dd8fed8008517e861cd2329b159bda1919ea656c" },
        { "MLKEM1024-P384", 1, "9dc39aca2c16481bfe7f75e65d52d4052d2c3e7572dc17153dd384c2448c1a63", "e227fce3fbfa52fe30ac30364b49911dad04a9081effd0a1cdc9a61969a9d3d2", "b258b8edf4851b7c1a86cc820743811c52d4bff678e8b94cf6bb5f3714b7dbd6" },
        { "MLKEM1024-P384", 2, "51abfadd05972874111e16e4209274579b5647f1dd05a12c3a4be2454b78b4c5", "a747e354ffa2b7528dace6aad1c1bc3c5cd9d4d0755072596900cbc34b0a0a69", "64a60015ced50f3972d4bb5cfb566f2a800056693945b309abd0ddeee3c1062a" },
        { "MLKEM1024-P384", 3, "37c3ef08ea83790e33823ffe52e445c8ed3cbdba4f5b301101efb00ffbad5416", "72c268964f7bd897f49ca024ee844a53b93830f7c67bb9241ae10057a0298fdc", "fc5e3871973b49b494090579634603099faf8f0bb022c4daba472cb771f1bac7" },
        { "MLKEM1024-P384", 4, "91164b282ca3c5e9f0fe5223428958e77af12640127144029cf236637e865333", "d6576b449bea440bd75bff05a209b3edec857f7d57c018451b13b5d97f68de88", "c3f7adc77f966a24f642197c478583b6eb8c2fe3fab1980925b628b9ed5ffeef" },
        { "MLKEM1024-P384", 5, "29a615a5636111d876bbce8b048f84b88317e130d9e384a11554a3d827512ceb", "444a1fe4aa09ecfbb7f33e3ccf92b15bc728d63d12a8929f69dfd107787d1a63", "a75ad7dfa8fdd4756fe5c0bdcf287e258562c5b3b394bfd949231966dcac02df" },
        { "MLKEM1024-P384", 6, "016a2e1b688cf7b991b55bc8e3ae0a4b57a9a00698bb930067605411750b8cc4", "84538b0780ffe36b49f9ee544b102bc6e13263a7d262655f4d2a4ea3e1fc6fa3", "8f81b7d72b0c023dbe66df377cfaed177c7516a06650aafec6046410dd0da9b0" },
        { "MLKEM1024-P384", 7, "f7182fa70ff38f82bc48a28133949ce6888c79e636093437cc5dfb949e0df276", "4f5c59bfa1c25e3d85a6f5e5814f3f4de04ff8b42acddb64eca279ce39cc18e9", "0e5a2eb3ae0f3af82978e6a71b5e2a0830850af49d877a0deec5144061ce44f0" },
        { "MLKEM1024-P384", 8, "223a7c15b518df3cb59f6753c964bc18e338a0c358e4a2133371657968b38596", "30ddc62d792d5485a73c2e1aa804b60740c2390b99a91f5bce42a52b35ae415e", "3fa279b00ab2798d2d70a2578ea2c76539cbea4bd86d1aa09e92bf0cc40e6626" },
        { "MLKEM1024-P384", 9, "d7d9553f12e250d8cb9c7f246d629c505afd22ef2b5ae105ae0d19460667f6c5", "52d90171337c24dae0830082337c51a5473cce5a079fd9341a8bb0c94777d39e", "3722070676e89cc0c97613a0207dbd89222219920caf5e9a8c6aa9d1e1a42bd5" },
    };

    // Nek, Nct and Nrandom (concrete draft section 4, appendix A)
    public static TheoryData<string, int, int, int> Instances => new()
    {
        { "MLKEM768-P256", 1249, 1153, 32 + 128 },
        { "MLKEM768-X25519", 1216, 1120, 32 + 32 },
        { "MLKEM1024-P384", 1665, 1665, 32 + 48 },
    };

    public static TheoryData<string> Names => ["MLKEM768-P256", "MLKEM768-X25519", "MLKEM1024-P384"];

    public static TheoryData<string> NistNames => ["MLKEM768-P256", "MLKEM1024-P384"];

    [Theory]
    [MemberData(nameof(Instances))]
    public void Constants(string name, int encapsulationKeyLength, int ciphertextLength, int randomnessLength)
    {
        Assert.True(HybridKem.IsSupported(name));
        Assert.Equal(encapsulationKeyLength, HybridKem.GetEncapsulationKeyLength(name));
        Assert.Equal(ciphertextLength, HybridKem.GetCiphertextLength(name));
        Assert.Equal(randomnessLength, HybridKem.GetRandomnessLength(name));
    }

    [Fact]
    public void IsSupported_UnknownNames()
    {
        Assert.False(HybridKem.IsSupported("MLKEM768"));
        Assert.False(HybridKem.IsSupported("X-Wing"));
        Assert.False(HybridKem.IsSupported("mlkem768-x25519"));
        Assert.Throws<ArgumentException>(() => HybridKem.GetEncapsulationKeyLength("MLKEM512-P256"));
    }

    [Theory]
    [MemberData(nameof(DraftVectors))]
    public void DraftVector(string name, int index, string expectedEncapsulationKeyHash, string expectedCiphertextHash, string expectedSharedKey)
    {
        var seed = Enumerable.Repeat((byte)index, HybridKem.SeedLength).ToArray();
        var randomness = Enumerable.Repeat((byte)(0x64 + index), HybridKem.GetRandomnessLength(name)).ToArray();

        // DeriveKeyPair(seed).ek
        var encapsulationKey = HybridKem.GetEncapsulationKey(name, seed);
        Assert.Equal(HybridKem.GetEncapsulationKeyLength(name), encapsulationKey.Length);
        Assert.Equal(expectedEncapsulationKeyHash, Sha256(encapsulationKey));

        // EncapsDerand(ek, randomness)
        var (sharedKey, ciphertext) = HybridKem.Encapsulate(name, encapsulationKey, randomness);
        Assert.Equal(HybridKem.GetCiphertextLength(name), ciphertext.Length);
        Assert.Equal(expectedCiphertextHash, Sha256(ciphertext));
        Assert.Equal(expectedSharedKey, Convert.ToHexStringLower(sharedKey));

        // Decaps(dk, ct): the ciphertext is the one of the vector (same hash)
        Assert.Equal(expectedSharedKey, Convert.ToHexStringLower(HybridKem.Decapsulate(name, seed, ciphertext)));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Encapsulate_RoundTrip(string name)
    {
        var seed = RandomNumberGenerator.GetBytes(HybridKem.SeedLength);
        var encapsulationKey = HybridKem.GetEncapsulationKey(name, seed);
        Assert.Equal(encapsulationKey, HybridKem.GetEncapsulationKey(name, seed));

        var (sharedKey, ciphertext) = HybridKem.Encapsulate(name, encapsulationKey);
        Assert.Equal(HybridKem.SharedKeyLength, sharedKey.Length);
        Assert.Equal(HybridKem.GetCiphertextLength(name), ciphertext.Length);
        Assert.Equal(sharedKey, HybridKem.Decapsulate(name, seed, ciphertext));

        // Encapsulation is randomized
        var (otherSharedKey, otherCiphertext) = HybridKem.Encapsulate(name, encapsulationKey);
        Assert.NotEqual(sharedKey, otherSharedKey);
        Assert.NotEqual(ciphertext, otherCiphertext);

        // Another decapsulation key gives another shared key (ML-KEM implicit rejection, other scalar)
        var otherSeed = RandomNumberGenerator.GetBytes(HybridKem.SeedLength);
        Assert.NotEqual(sharedKey, HybridKem.Decapsulate(name, otherSeed, ciphertext));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Decapsulate_ModifiedPqCiphertext_ImplicitRejection(string name)
    {
        var seed = RandomNumberGenerator.GetBytes(HybridKem.SeedLength);
        var (sharedKey, ciphertext) = HybridKem.Encapsulate(name, HybridKem.GetEncapsulationKey(name, seed));

        // ML-KEM rejects implicitly: a modified ct_PQ gives a pseudorandom shared key, not an error
        ciphertext[0] ^= 1;
        var rejected = HybridKem.Decapsulate(name, seed, ciphertext);
        Assert.NotEqual(sharedKey, rejected);
        Assert.Equal(rejected, HybridKem.Decapsulate(name, seed, ciphertext));
    }

    [Fact]
    public void Decapsulate_ModifiedX25519Ciphertext()
    {
        // Every 32-byte string is a Curve25519 element: a modified ct_T only changes the shared key
        const string Name = "MLKEM768-X25519";
        var seed = RandomNumberGenerator.GetBytes(HybridKem.SeedLength);
        var (sharedKey, ciphertext) = HybridKem.Encapsulate(Name, HybridKem.GetEncapsulationKey(Name, seed));
        ciphertext[^1] ^= 1;
        Assert.NotEqual(sharedKey, HybridKem.Decapsulate(Name, seed, ciphertext));
    }

    [Theory]
    [MemberData(nameof(NistNames))]
    public void Decapsulate_InvalidPoint(string name)
    {
        var seed = RandomNumberGenerator.GetBytes(HybridKem.SeedLength);
        var (_, ciphertext) = HybridKem.Encapsulate(name, HybridKem.GetEncapsulationKey(name, seed));
        var elementOffset = ciphertext.Length - ElementLength(name);

        // Not on the curve
        var invalid = ciphertext.ToArray();
        invalid[^1] ^= 1;
        AssertOperationError(() => HybridKem.Decapsulate(name, seed, invalid));

        // Compressed or hybrid point prefix
        invalid = ciphertext.ToArray();
        invalid[elementOffset] = 2;
        AssertOperationError(() => HybridKem.Decapsulate(name, seed, invalid));

        // Coordinates out of the field
        invalid = ciphertext.ToArray();
        invalid.AsSpan(elementOffset + 1).Fill(0xFF);
        AssertOperationError(() => HybridKem.Decapsulate(name, seed, invalid));

        // The identity point cannot be encoded: all zeros is not on the curve
        invalid = ciphertext.ToArray();
        invalid.AsSpan(elementOffset + 1).Clear();
        AssertOperationError(() => HybridKem.Decapsulate(name, seed, invalid));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Decapsulate_InvalidLength(string name)
    {
        var seed = RandomNumberGenerator.GetBytes(HybridKem.SeedLength);
        var (_, ciphertext) = HybridKem.Encapsulate(name, HybridKem.GetEncapsulationKey(name, seed));
        AssertOperationError(() => HybridKem.Decapsulate(name, seed, ciphertext.AsSpan(1)));
        AssertOperationError(() => HybridKem.Decapsulate(name, seed, [.. ciphertext, 0]));
        AssertOperationError(() => HybridKem.Decapsulate(name, seed, []));
        Assert.Throws<ArgumentException>(() => HybridKem.Decapsulate(name, seed.AsSpan(1), ciphertext));
        Assert.Throws<ArgumentException>(() => HybridKem.Decapsulate(name, new byte[64], ciphertext));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Encapsulate_InvalidEncapsulationKey(string name)
    {
        var encapsulationKey = HybridKem.GetEncapsulationKey(name, RandomNumberGenerator.GetBytes(HybridKem.SeedLength));
        AssertOperationError(() => HybridKem.Encapsulate(name, encapsulationKey.AsSpan(1)));
        AssertOperationError(() => HybridKem.Encapsulate(name, [.. encapsulationKey, 0]));
        AssertOperationError(() => HybridKem.Encapsulate(name, []));

        // ek_PQ fails the modulus check of ML-KEM.Encaps (FIPS 203 §7.2): the first coefficient is 0xFFF >= q
        var invalid = encapsulationKey.ToArray();
        invalid[0] = 0xFF;
        invalid[1] |= 0x0F;
        AssertOperationError(() => HybridKem.Encapsulate(name, invalid));
    }

    [Theory]
    [MemberData(nameof(NistNames))]
    public void Encapsulate_InvalidPoint(string name)
    {
        var encapsulationKey = HybridKem.GetEncapsulationKey(name, RandomNumberGenerator.GetBytes(HybridKem.SeedLength));
        var elementOffset = encapsulationKey.Length - ElementLength(name);

        var invalid = encapsulationKey.ToArray();
        invalid[^1] ^= 1;
        AssertOperationError(() => HybridKem.Encapsulate(name, invalid));

        invalid = encapsulationKey.ToArray();
        invalid[elementOffset] = 3;
        AssertOperationError(() => HybridKem.Encapsulate(name, invalid));

        invalid = encapsulationKey.ToArray();
        invalid.AsSpan(elementOffset + 1).Clear();
        AssertOperationError(() => HybridKem.Encapsulate(name, invalid));
    }

    [Theory]
    [MemberData(nameof(NistNames))]
    public void Encapsulate_RandomScalarRejectionSampling(string name)
    {
        // RandomScalar (concrete draft section 3.1.1) skips the Nscalar-byte blocks that are 0 or >= order: a seed starting
        // with a rejected block gives the scalar of the next block
        var scalarLength = name == "MLKEM768-P256" ? 32 : 48;
        var encapsulationKey = HybridKem.GetEncapsulationKey(name, new byte[HybridKem.SeedLength]);
        var randomnessLength = HybridKem.GetRandomnessLength(name);
        var reference = Enumerable.Repeat((byte)0x42, randomnessLength).ToArray();
        var expected = HybridKem.Encapsulate(name, encapsulationKey, reference);

        if (randomnessLength - 32 >= 2 * scalarLength)
        {
            // P-256: Nseed = 128, room for up to 3 rejected blocks
            foreach (var rejectedByte in new byte[] { 0x00, 0xFF })
            {
                var randomness = reference.ToArray();
                randomness.AsSpan(32, scalarLength).Fill(rejectedByte);
                reference.AsSpan(32, randomnessLength - 32 - scalarLength).CopyTo(randomness.AsSpan(32 + scalarLength));
                var actual = HybridKem.Encapsulate(name, encapsulationKey, randomness);
                Assert.Equal(expected.SharedKey, actual.SharedKey);
                Assert.Equal(expected.Ciphertext, actual.Ciphertext);
            }

            // The order itself is rejected
            var withOrder = reference.ToArray();
            Convert.FromHexString("ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551").CopyTo(withOrder, 32);
            reference.AsSpan(32, randomnessLength - 32 - scalarLength).CopyTo(withOrder.AsSpan(32 + scalarLength));
            Assert.Equal(expected.Ciphertext, HybridKem.Encapsulate(name, encapsulationKey, withOrder).Ciphertext);

            // order - 1 is accepted
            var withOrderMinusOne = withOrder.ToArray();
            withOrderMinusOne[32 + scalarLength - 1]--;
            Assert.NotEqual(expected.Ciphertext, HybridKem.Encapsulate(name, encapsulationKey, withOrderMinusOne).Ciphertext);
        }

        // Every block rejected: the rejection sampling fails
        var allRejected = reference.ToArray();
        allRejected.AsSpan(32).Fill(0xFF);
        AssertOperationError(() => HybridKem.Encapsulate(name, encapsulationKey, allRejected));
        allRejected.AsSpan(32).Clear();
        AssertOperationError(() => HybridKem.Encapsulate(name, encapsulationKey, allRejected));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Encapsulate_InvalidRandomnessLength(string name)
    {
        var encapsulationKey = HybridKem.GetEncapsulationKey(name, new byte[HybridKem.SeedLength]);
        Assert.Throws<ArgumentException>(() => HybridKem.Encapsulate(name, encapsulationKey, new byte[HybridKem.GetRandomnessLength(name) - 1]));
        Assert.Throws<ArgumentException>(() => HybridKem.Encapsulate(name, encapsulationKey, new byte[HybridKem.GetRandomnessLength(name) + 1]));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void GetEncapsulationKey_InvalidSeedLength(string name)
    {
        Assert.Throws<ArgumentException>(() => HybridKem.GetEncapsulationKey(name, new byte[31]));
        Assert.Throws<ArgumentException>(() => HybridKem.GetEncapsulationKey(name, new byte[64]));
    }

    private static int ElementLength(string name) => name switch
    {
        "MLKEM768-P256" => 65,
        "MLKEM1024-P384" => 97,
        _ => 32,
    };

    private static void AssertOperationError(Action action)
    {
        var exception = Assert.Throws<WebCryptoException>(action);
        Assert.Equal("OperationError", exception.Name);
    }

    private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
