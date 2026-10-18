using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Tests.Cryptography;

// SHA-3 and SHAKE (FIPS 202), cSHAKE and KMAC (NIST SP 800-185), TurboSHAKE and KangarooTwelve (RFC 9861)
public sealed class KeccakTests
{
    // WPT WebCryptoAPI/digest/digest_test_data.js
    private const string ShortData = "156eea7cc14c56cb94db030a4a9d95ff";
    private const string MediumData = "b6c8f9df648cd088b70f38e74197b18cb81e1e4350d50bccb8fb5a7379c87bb2e3d6ed5461ed1e9f36f340a3962a446b815b794b4bd43a4403502077b2256cc807837f3aacd118eb4b9c2baeb897068625abaca193";

    // WPT WebCryptoAPI/digest/sha3.tentative.https.any.js
    public static TheoryData<int, string, string> Sha3Vectors => new()
    {
        { 256, "empty", "a7ffc6f8bf1ed76651c14756a061d662f580ff4de43b49fa82d80a4b80f8434a" },
        { 384, "empty", "0c63a75b845e4f7d01107d852e4c2485c51a50aaaa94fc61995e71bbee983a2ac3713831264adb47fb6bd1e058d5f004" },
        { 512, "empty", "a69f73cca23a9ac5c8b567dc185a756e97c982164fe25859e0d1dcc1475c80a615b2123af1f5f94c11e3e9402c3ac558f500199d95b6d3e301758586281dcd26" },
        { 256, "short", "3059af7aa33b517084e8ad7bbc4fb208a44c28ef32b4698d103dd540e4f91aa1" },
        { 384, "short", "54b8f0e4cf4974de740098f66b3024479b01631315a6773606c33eadc32556a6e778e08f0225ae79265aec666cb2390b" },
        { 512, "short", "2dd2e07a62e6ad0498ba84f313c4d4024cb46001f78f75db336b0d4d8bd2a9ec152c4ad20878735d82ba0872ecf59608ef3ced2b2a8669427e7da31e362333d8" },
        { 256, "medium", "1fa7cd1da74cd8046417508c8314e74a9a4a9d38f9f18e6cb215b8c891a0a80e" },
        { 384, "medium", "437b7d8b68b250b5c1739ea4cc86db2033879dfb18de292c9c50d9c193a4c79a08a6cae3f4e483c2795ea5d1ef7e69d2" },
        { 512, "medium", "e640a21909536640369e9b0a48931c5cb2efcbc91fecf247306bc96a0e4ca33307cb8e1b9af367946dd01c243f3907508d04f1692a3161df1f898de8ee25febe" },
        { 256, "long", "b2cfc61e0386cdaef5e10a2be189891f5ef52a7624bfcd8edc893acc64fec600" },
        { 384, "long", "3b39c4c97ad87613305d0ccc987181713e2d5e84b1f9760011bcce0c297499005bdce8a3d2409b5ad0164f32bb8778d0" },
        { 512, "long", "bd262cecf565c338032de5ba0138f0aacfe7dde83d272d0d37d952829ed25de1a1342d98659ef7d2fa4aca7ce2b1aa0784d8fc1dcbf81bcec7a7431a3da36bf7" },
    };

    // WPT WebCryptoAPI/digest/cshake.tentative.https.any.js (cSHAKE without N and S is SHAKE), 512-bit outputs
    public static TheoryData<int, string, string> ShakeVectors => new()
    {
        { 128, "empty", "7f9c2ba4e88f827d616045507605853ed73b8093f6efbc88eb1a6eacfa66ef263cb1eea988004b93103cfb0aeefd2a686e01fa4a58e8a3639ca8a1e3f9ae57e2" },
        { 256, "empty", "46b9dd2b0ba88d13233b3feb743eeb243fcd52ea62b81b82b50c27646ed5762fd75dc4ddd8c0f200cb05019d67b592f6fc821c49479ab48640292eacb3b7c4be" },
        { 128, "short", "dea62d73e6b59cf725d0320d660089a4475cbbd3b8539e36691f150d47556794f0374001b7888abc3698d40b89ae3134e933f51a84ca7fda880c3bfdd9dc3a5e" },
        { 256, "short", "1738113f5abb3ee5320ee18aa266c3617a7475dbd8ed9a985994fddd6112ad999ec8e2ebdfeafb96e76f6bb3a3adba43da60f00cd12496df5af3e28ae6d3de42" },
        { 128, "medium", "b1acd53a03e76a221e52ea578e042f686a68c3d1c9832ab18285cf4f304ca32d3faa09fc82aae142d3dfcd79058a5d5c3c11bd2d11c3f8a9331f62acddbae15d" },
        { 256, "medium", "4146c13d86d9bc186b0b309ab6a124ee0c74ba26b8c60dcc7b3ed505969aa8d19028c6317999a085b1e6b6a785ce4ff632aeb27493227e44232fb7b39521417b" },
    };

    // NIST SP 800-185 examples (cSHAKE_samples.pdf): X = 00 01 02 03 or 00..C7, N = "", S = "Email Signature"
    public static TheoryData<int, int, int, string> CShakeVectors => new()
    {
        { 128, 4, 32, "c1c36925b6409a04f1b504fcbca9d82b4017277cb5ed2b2065fc1d3814d5aaf5" },
        { 128, 200, 32, "c5221d50e4f822d96a2e8881a961420f294b7b24fe3d2094baed2c6524cc166b" },
        { 256, 4, 64, "d008828e2b80ac9d2218ffee1d070c48b8e4c87bff32c9699d5b6896eee0edd164020e2be0560858d9c00c037e34a96937c561a74c412bb4c746469527281c8c" },
        { 256, 200, 64, "07dc27b11e51fbac75bc7b3c1d983e8b4b85fb1defaf218912ac86430273091727f42b17ed1df63e8ec118f04b23633c1dfb1574c8fb55cb45da8e25afb092bb" },
    };

    // WPT WebCryptoAPI/sign_verify/kmac_vectors.js (NIST SP 800-185 KMAC_samples.pdf): K = 40..5F
    public static TheoryData<int, int, bool, int, string> KmacVectors => new()
    {
        { 128, 4, false, 32, "e5780b0d3ea6f7d3a429c5706aa43a00fadbd7d49628839e3187243f456ee14e" },
        { 128, 4, true, 32, "3b1fba963cd8b0b59e8c1a6d71888b7143651af8ba0a7070c0979e2811324aa5" },
        { 128, 200, true, 32, "1f5b4e6cca02209e0dcb5ca635b89a15e271ecc760071dfd805faa38f9729230" },
        { 256, 4, true, 64, "20c570c31346f703c9ac36c61c03cb64c3970d0cfc787e9b79599d273a68d2f7f69d4cc3de9d104a351689f27cf6f5951f0103f33f4f24871024d9c27773a8dd" },
        { 256, 200, false, 64, "75358cf39e41494e949707927cee0af20a3ff553904c86b08f21cc414bcfd691589d27cf5e15369cbbff8b9a4c2eb17800855d0235ff635da82533ec6b759b69" },
        { 256, 200, true, 64, "b58618f71f92e1d56c1b8c55ddd7cd188b97b4ca4d99831eb2699a837da2e4d970fbacfde50033aea585f1a2708510c32d07880801bd182898fe476876fc8965" },
    };

    // RFC 9861 §5 (WPT WebCryptoAPI/digest/turboshake.tentative.https.any.js): input ("ptn:<n>" or hex), D, output
    public static TheoryData<int, string, byte, string> TurboShakeVectors => new()
    {
        { 128, "", 0x1F, "1e415f1c5983aff2169217277d17bb538cd945a397ddec541f1ce41af2c1b74c3e8ccae2a4dae56c84a04c2385c03c15e8193bdf58737363321691c05462c8df" },
        { 128, "ptn:1", 0x1F, "55cedd6f60af7bb29a4042ae832ef3f58db7299f893ebb9247247d856958daa9" },
        { 128, "ptn:17", 0x1F, "9c97d036a3bac819db70ede0ca554ec6e4c2a1a4ffbfd9ec269ca6a111161233" },
        { 128, "ptn:289", 0x1F, "96c77c279e0126f7fc07c9b07f5cdae1e0be60bdbe10620040e75d7223a624d2" },
        { 128, "ptn:4913", 0x1F, "d4976eb56bcf118520582b709f73e1d6853e001fdaf80e1b13e0d0599d5fb372" },
        { 128, "ptn:83521", 0x1F, "da67c7039e98bf530cf7a37830c6664e14cbab7f540f58403b1b82951318ee5c" },
        { 128, "ptn:1419857", 0x1F, "b97a906fbf83ef7c812517abf3b2d0aea0c4f60318ce11cf103925127f59eecd" },
        { 128, "ffffff", 0x01, "bf323f940494e88ee1c540fe660be8a0c93f43d15ec006998462fa994eed5dab" },
        { 128, "ff", 0x06, "8ec9c66465ed0d4a6c35d13506718d687a25cb05c74cca1e42501abd83874a67" },
        { 128, "ffffff", 0x07, "b658576001cad9b1e5f399a9f77723bba05458042d68206f7252682dba3663ed" },
        { 128, "ffffffffffffff", 0x0B, "8deeaa1aec47ccee569f659c21dfa8e112db3cee37b18178b2acd805b799cc37" },
        { 128, "ff", 0x30, "553122e2135e363c3292bed2c6421fa232bab03daa07c7d6636603286506325b" },
        { 128, "ffffff", 0x7F, "16274cc656d44cefd422395d0f9053bda6d28e122aba15c765e5ad0e6eaf26f9" },
        { 256, "", 0x1F, "367a329dafea871c7802ec67f905ae13c57695dc2c6663c61035f59a18f8e7db11edc0e12e91ea60eb6b32df06dd7f002fbafabb6e13ec1cc20d995547600db0" },
        { 256, "ptn:1", 0x1F, "3e1712f928f8eaf1054632b2aa0a246ed8b0c378728f60bc970410155c28820e90cc90d8a3006aa2372c5c5ea176b0682bf22bae7467ac94f74d43d39b0482e2" },
        { 256, "ptn:17", 0x1F, "b3bab0300e6a191fbe6137939835923578794ea54843f5011090fa2f3780a9e5cb22c59d78b40a0fbff9e672c0fbe0970bd2c845091c6044d687054da5d8e9c7" },
        { 256, "ptn:289", 0x1F, "66b810db8e90780424c0847372fdc95710882fde31c6df75beb9d4cd9305cfcae35e7b83e8b7e6eb4b78605880116316fe2c078a09b94ad7b8213c0a738b65c0" },
        { 256, "ptn:4913", 0x1F, "c74ebc919a5b3b0dd1228185ba02d29ef442d69d3d4276a93efe0bf9a16a7dc0cd4eabadab8cd7a5edd96695f5d360abe09e2c6511a3ec397da3b76b9e1674fb" },
        { 256, "ptn:83521", 0x1F, "02cc3a8897e6f4f6ccb6fd46631b1f5207b66c6de9c7b55b2d1a23134a170afdac234eaba9a77cff88c1f020b73724618c5687b362c430b248cd38647f848a1d" },
        { 256, "ptn:1419857", 0x1F, "add53b06543e584b5823f626996aee50fe45ed15f20243a7165485acb4aa76b4ffda75cedf6d8cdc95c332bd56f4b986b58bb17d1778bfc1b1a97545cdf4ec9f" },
        { 256, "ffffff", 0x01, "d21c6fbbf587fa2282f29aea620175fb0257413af78a0b1b2a87419ce031d933ae7a4d383327a8a17641a34f8a1d1003ad7da6b72dba84bb62fef28f62f12424" },
        { 256, "ff", 0x06, "738d7b4e37d18b7f22ad1b5313e357e3dd7d07056a26a303c433fa3533455280f4f5a7d4f700efb437fe6d281405e07be32a0a972e22e63adc1b090daefe004b" },
        { 256, "ffffff", 0x07, "18b3b5b7061c2e67c1753a00e6ad7ed7ba1c906cf93efb7092eaf27fbeebb755ae6e292493c110e48d260028492b8e09b5500612b8f2578985ded5357d00ec67" },
        { 256, "ffffffffffffff", 0x0B, "bb36764951ec97e9d85f7ee9a67a7718fc005cf42556be79ce12c0bde50e5736d6632b0d0dfb202d1bbb8ffe3dd74cb00834fa756cb03471bab13a1e2c16b3c0" },
        { 256, "ff", 0x30, "f3fe12873d34bcbb2e608779d6b70e7f86bec7e90bf113cbd4fdd0c4e2f4625e148dd7ee1a52776cf77f240514d9ccfc3b5ddab8ee255e39ee389072962c111a" },
        { 256, "ffffff", 0x7F, "abe569c1f77ec340f02705e7d37c9ab7e155516e4a6a150021d70b6fac0bb40c069f9a9828a0d575cd99f9bae435ab1acf7ed9110ba97ce0388d074bac768776" },
    };

    // RFC 9861 §5 (WPT WebCryptoAPI/digest/kangarootwelve.tentative.https.any.js): input, customization, output
    public static TheoryData<int, string, string, string> KangarooTwelveVectors => new()
    {
        { 128, "", "", "1ac2d450fc3b4205d19da7bfca1b37513c0803577ac7167f06fe2ce1f0ef39e54269c056b8c82e48276038b6d292966cc07a3d4645272e31ff38508139eb0a71" },
        { 128, "ptn:1", "", "2bda92450e8b147f8a7cb629e784a058efca7cf7d8218e02d345dfaa65244a1f" },
        { 128, "ptn:17", "", "6bf75fa2239198db4772e36478f8e19b0f371205f6a9a93a273f51df37122888" },
        { 128, "ptn:289", "", "0c315ebcdedbf61426de7dcf8fb725d1e74675d7f5327a5067f367b108ecb67c" },
        { 128, "ptn:4913", "", "cb552e2ec77d9910701d578b457ddf772c12e322e4ee7fe417f92c758f0d59d0" },
        { 128, "ptn:83521", "", "8701045e22205345ff4dda05555cbb5c3af1a771c2b89baef37db43d9998b9fe" },
        { 128, "ptn:1419857", "", "844d610933b1b9963cbdeb5ae3b6b05cc7cbd67ceedf883eb678a0a8e0371682" },
        { 128, "", "ptn:1", "fab658db63e94a246188bf7af69a133045f46ee984c56e3c3328caaf1aa1a583" },
        { 128, "ff", "ptn:41", "d848c5068ced736f4462159b9867fd4c20b808acc3d5bc48e0b06ba0a3762ec4" },
        { 128, "ffffff", "ptn:1681", "c389e5009ae57120854c2e8c64670ac01358cf4c1baf89447a724234dc7ced74" },
        { 128, "ffffffffffffff", "ptn:68921", "75d2f86a2e644566726b4fbcfc5657b9dbcf070c7b0dca06450ab291d7443bcf" },
        { 128, "ptn:8191", "", "1b577636f723643e990cc7d6a659837436fd6a103626600eb8301cd1dbe553d6" },
        { 128, "ptn:8192", "", "48f256f6772f9edfb6a8b661ec92dc93b95ebd05a08a17b39ae3490870c926c3" },
        { 128, "ptn:8192", "ptn:8189", "3ed12f70fb05ddb58689510ab3e4d23c6c6033849aa01e1d8c220a297fedcd0b" },
        { 128, "ptn:8192", "ptn:8190", "6a7c1b6a5cd0d8c9ca943a4a216cc64604559a2ea45f78570a15253d67ba00ae" },
        { 256, "", "", "b23d2e9cea9f4904e02bec06817fc10ce38ce8e93ef4c89e6537076af8646404e3e8b68107b8833a5d30490aa33482353fd4adc7148ecb782855003aaebde4a9b0925319d8ea1e121a609821ec19efea89e6d08daee1662b69c840289f188ba860f55760b61f82114c030c97e5178449608ccd2cd2d919fc7829ff69931ac4d0" },
        { 256, "ptn:1", "", "0d005a194085360217128cf17f91e1f71314efa5564539d444912e3437efa17f82db6f6ffe76e781eaa068bce01f2bbf81eacb983d7230f2fb02834a21b1ddd0" },
        { 256, "ptn:17", "", "1ba3c02b1fc514474f06c8979978a9056c8483f4a1b63d0dccefe3a28a2f323e1cdcca40ebf006ac76ef0397152346837b1277d3e7faa9c9653b19075098527b" },
        { 256, "ptn:289", "", "de8ccbc63e0f133ebb4416814d4c66f691bbf8b6a61ec0a7700f836b086cb029d54f12ac7159472c72db118c35b4e6aa213c6562caaa9dcc518959e69b10f3ba" },
        { 256, "ptn:4913", "", "647efb49fe9d717500171b41e7f11bd491544443209997ce1c2530d15eb1ffbb598935ef954528ffc152b1e4d731ee2683680674365cd191d562bae753b84aa5" },
        { 256, "ptn:83521", "", "b06275d284cd1cf205bcbe57dccd3ec1ff6686e3ed15776383e1f2fa3c6ac8f08bf8a162829db1a44b2a43ff83dd89c3cf1ceb61ede659766d5ccf817a62ba8d" },
        { 256, "ptn:1419857", "", "9473831d76a4c7bf77ace45b59f1458b1673d64bcd877a7c66b2664aa6dd149e60eab71b5c2bab858c074ded81ddce2b4022b5215935c0d4d19bf511aeeb0772" },
        { 256, "", "ptn:1", "9280f5cc39b54a5a594ec63de0bb99371e4609d44bf845c2f5b8c316d72b159811f748f23e3fabbe5c3226ec96c62186df2d33e9df74c5069ceecbb4dd10eff6" },
        { 256, "ff", "ptn:41", "47ef96dd616f200937aa7847e34ec2feae8087e3761dc0f8c1a154f51dc9ccf845d7adbce57ff64b639722c6a1672e3bf5372d87e00aff89be97240756998853" },
        { 256, "ffffff", "ptn:1681", "3b48667a5051c5966c53c5d42b95de451e05584e7806e2fb765eda959074172cb438a9e91dde337c98e9c41bed94c4e0aef431d0b64ef2324f7932caa6f54969" },
        { 256, "ffffffffffffff", "ptn:68921", "e0911cc00025e1540831e266d94add9b98712142b80d2629e643aac4efaf5a3a30a88cbf4ac2a91a2432743054fbcc9897670e86ba8cec2fc2ace9c966369724" },
        { 256, "ptn:8191", "", "3081434d93a4108d8d8a3305b89682cebedc7ca4ea8a3ce869fbb73cbe4a58eef6f24de38ffc170514c70e7ab2d01f03812616e863d769afb3753193ba045b20" },
        { 256, "ptn:8192", "", "c6ee8e2ad3200c018ac87aaa031cdac22121b412d07dc6e0dccbb53423747e9a1c18834d99df596cf0cf4b8dfafb7bf02d139d0c9035725adc1a01b7230a41fa" },
        { 256, "ptn:8192", "ptn:8189", "74e47879f10a9c5d11bd2da7e194fe57e86378bf3c3f7448eff3c576a0f18c5caae0999979512090a7f348af4260d4de3c37f1ecaf8d2c2c96c1d16c64b12496" },
        { 256, "ptn:8192", "ptn:8190", "f4b5908b929ffe01e0f79ec2f21243d41a396b2e7303a6af1d6399cd6c7a0a2dd7c4f607e8277f9c9b1cb4ab9ddc59d4b92d1fc7558441f1832c3279a4241b8b" },
    };

    [Theory]
    [MemberData(nameof(Sha3Vectors))]
    public void Sha3(int bits, string source, string expected)
    {
        Assert.Equal(expected, Convert.ToHexStringLower(Keccak.Sha3(bits, GetSourceData(source))));
    }

    [Theory]
    [MemberData(nameof(ShakeVectors))]
    public void Shake(int securityBits, string source, string expected)
    {
        var data = GetSourceData(source);
        Assert.Equal(expected, Convert.ToHexStringLower(Keccak.Shake(securityBits, data, 64)));
        Assert.Equal(expected, Convert.ToHexStringLower(Keccak.CShake(securityBits, data, 64, [], [])));

        // The output of an extendable-output function is a prefix of the longer outputs
        Assert.Equal(expected[..64], Convert.ToHexStringLower(Keccak.CShake(securityBits, data, 32, [], [])));
        Assert.Empty(Keccak.CShake(securityBits, data, 0, [], []));
    }

    [Theory]
    [MemberData(nameof(CShakeVectors))]
    public void CShake(int securityBits, int dataLength, int outputLength, string expected)
    {
        var data = Enumerable.Range(0, dataLength).Select(i => (byte)i).ToArray();
        var result = Keccak.CShake(securityBits, data, outputLength, [], "Email Signature"u8);
        Assert.Equal(expected, Convert.ToHexStringLower(result));
    }

    [Theory]
    [MemberData(nameof(KmacVectors))]
    public void Kmac(int securityBits, int dataLength, bool customized, int outputLength, string expected)
    {
        var key = Enumerable.Range(0x40, 32).Select(i => (byte)i).ToArray();
        var data = Enumerable.Range(0, dataLength).Select(i => (byte)i).ToArray();
        var customization = customized ? "My Tagged Application"u8 : [];
        var result = Keccak.Kmac(securityBits, key, data, outputLength, customization);
        Assert.Equal(expected, Convert.ToHexStringLower(result));
    }

    [Fact]
    public void Kmac_OutputLengthIsPartOfTheInput()
    {
        // KMAC (not KMACXOF): the requested length is absorbed, so a shorter output is not a prefix of a longer one
        var key = new byte[32];
        var longer = Keccak.Kmac(128, key, [1, 2, 3], 64, []);
        var shorter = Keccak.Kmac(128, key, [1, 2, 3], 32, []);
        Assert.NotEqual(longer.AsSpan(0, 32).ToArray(), shorter);
    }

    [Theory]
    [MemberData(nameof(TurboShakeVectors))]
    public void TurboShake(int securityBits, string input, byte domainSeparation, string expected)
    {
        var result = Keccak.TurboShake(securityBits, GetData(input), expected.Length / 2, domainSeparation);
        Assert.Equal(expected, Convert.ToHexStringLower(result));
    }

    [Theory]
    [InlineData(128, "a3b9b0385900ce761f22aed548e754da10a5242d62e8c658e3f3a923a7555607")]
    [InlineData(256, "abefa11630c661269249742685ec082f207265dccf2f43534e9c61ba0c9d1d75")]
    public void TurboShake_LongOutput(int securityBits, string expectedEnd)
    {
        // RFC 9861 §5: the last 32 bytes of the 10032 bytes of output of the empty message
        var result = Keccak.TurboShake(securityBits, [], 10032);
        Assert.Equal(expectedEnd, Convert.ToHexStringLower(result.AsSpan(10000)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x80)]
    public void TurboShake_InvalidDomainSeparation(byte domainSeparation)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Keccak.TurboShake(128, [], 32, domainSeparation));
    }

    [Theory]
    [MemberData(nameof(KangarooTwelveVectors))]
    public void KangarooTwelve(int securityBits, string input, string customization, string expected)
    {
        var result = Keccak.KangarooTwelve(securityBits, GetData(input), expected.Length / 2, GetData(customization));
        Assert.Equal(expected, Convert.ToHexStringLower(result));
    }

    [Theory]
    [InlineData(128, 10032, "e8dc563642f7228c84684c898405d3a834799158c079b12880277a1d28e2ff6d")]
    [InlineData(256, 10064, "ad4a1d718cf950506709a4c33396139b4449041fc79a05d68da35f1e453522e056c64fe94958e7085f2964888259b9932752f3ccd855288efee5fcbb8b563069")]
    public void KangarooTwelve_LongOutput(int securityBits, int outputLength, string expectedEnd)
    {
        // RFC 9861 §5: the end of a long output of the empty message
        var result = Keccak.KangarooTwelve(securityBits, [], outputLength, []);
        Assert.Equal(expectedEnd, Convert.ToHexStringLower(result.AsSpan(outputLength - (expectedEnd.Length / 2))));
    }

    [Fact]
    public void Sponge_IncrementalAbsorbAndSqueeze()
    {
        // Absorbing and squeezing in pieces of any size gives the same result as a single call, across block boundaries
        var data = GetData("ptn:1000");
        var expected = Keccak.Shake(128, data, 500);
        foreach (var step in new[] { 1, 3, 7, 8, 100, 167, 168, 169, 1000 })
        {
            var sponge = KeccakSponge.CreateShake128();
            for (var i = 0; i < data.Length; i += step)
            {
                sponge.Absorb(data.AsSpan(i, Math.Min(step, data.Length - i)));
            }

            var output = new byte[expected.Length];
            for (var i = 0; i < output.Length; i += step)
            {
                sponge.Squeeze(output.AsSpan(i, Math.Min(step, output.Length - i)));
            }

            Assert.Equal(expected, output);
        }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(192)]
    public void InvalidSecurityStrength(int securityBits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Keccak.CShake(securityBits, [], 32, [], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => Keccak.Kmac(securityBits, [], [], 32, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => Keccak.TurboShake(securityBits, [], 32));
        Assert.Throws<ArgumentOutOfRangeException>(() => Keccak.KangarooTwelve(securityBits, [], 32, []));
    }

    [Fact]
    public void Sha3_InvalidLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Keccak.Sha3(128, []));
    }

    private static byte[] GetSourceData(string source)
    {
        return source switch
        {
            "empty" => [],
            "short" => Convert.FromHexString(ShortData),
            "medium" => Convert.FromHexString(MediumData),
            "long" => Enumerable.Repeat(Convert.FromHexString(MediumData), 1024).SelectMany(bytes => bytes).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
    }

    // "ptn:<n>" is the pattern 00 01 02 .. F9 FA repeated (RFC 9861 §5), otherwise the hexadecimal bytes
    private static byte[] GetData(string value)
    {
        if (value.StartsWith("ptn:", StringComparison.Ordinal))
        {
            var length = int.Parse(value.AsSpan(4), System.Globalization.CultureInfo.InvariantCulture);
            return Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        }

        return Convert.FromHexString(value);
    }
}
