using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Tests.Cryptography;

// AES-OCB (RFC 7253), used by the AES-OCB algorithm (https://wicg.github.io/webcrypto-modern-algos/#aes-ocb)
public sealed class AesOcbTests
{
    // WPT WebCryptoAPI/encrypt_decrypt/aes_common_fixtures.js and aes_ocb_fixtures.js
    private const string WptPlaintext = "This specification describes a JavaScript API for performing basic cryptographic operations in web applications, such as hashing, signature generation and verification, and encryption and decryption. Additionally, it describes an API for applications to generate and/or manage the keying material necessary to perform these operations. Uses for this API range from user or service authentication, document or code signing, and the confidentiality and integrity of communications.";
    private const string WptAdditionalData = "There are 7 further editorial notes in the document.";
    private const string WptIv = "3a92732aa6ea39bf3986e0c76c742e";

    // https://www.rfc-editor.org/rfc/rfc7253#appendix-A: K = 000102...0F, N = BBAA998877665544332211xx, TAGLEN = 128
    [Theory]
    [InlineData(0x00, 0, 0, "785407BFFFC8AD9EDCC5520AC9111EE6")]
    [InlineData(0x01, 8, 8, "6820B3657B6F615A5725BDA0D3B4EB3A257C9AF1F8F03009")]
    [InlineData(0x02, 8, 0, "81017F8203F081277152FADE694A0A00")]
    [InlineData(0x03, 0, 8, "45DD69F8F5AAE72414054CD1F35D82760B2CD00D2F99BFA9")]
    [InlineData(0x04, 16, 16, "571D535B60B277188BE5147170A9A22C3AD7A4FF3835B8C5701C1CCEC8FC3358")]
    [InlineData(0x05, 16, 0, "8CF761B6902EF764462AD86498CA6B97")]
    [InlineData(0x06, 0, 16, "5CE88EC2E0692706A915C00AEB8B2396F40E1C743F52436BDF06D8FA1ECA343D")]
    [InlineData(0x07, 24, 24, "1CA2207308C87C010756104D8840CE1952F09673A448A122C92C62241051F57356D7F3C90BB0E07F")]
    [InlineData(0x08, 24, 0, "6DC225A071FC1B9F7C69F93B0F1E10DE")]
    [InlineData(0x09, 0, 24, "221BD0DE7FA6FE993ECCD769460A0AF2D6CDED0C395B1C3CE725F32494B9F914D85C0B1EB38357FF")]
    [InlineData(0x0A, 32, 32, "BD6F6C496201C69296C11EFD138A467ABD3C707924B964DEAFFC40319AF5A48540FBBA186C5553C68AD9F592A79A4240")]
    [InlineData(0x0B, 32, 0, "FE80690BEE8A485D11F32965BC9D2A32")]
    [InlineData(0x0C, 0, 32, "2942BFC773BDA23CABC6ACFD9BFD5835BD300F0973792EF46040C53F1432BCDFB5E1DDE3BC18A5F840B52E653444D5DF")]
    [InlineData(0x0D, 40, 40, "D5CA91748410C1751FF8A2F618255B68A0A12E093FF454606E59F9C1D0DDC54B65E8628E568BAD7AED07BA06A4A69483A7035490C5769E60")]
    [InlineData(0x0E, 40, 0, "C5CD9D1850C141E358649994EE701B68")]
    [InlineData(0x0F, 0, 40, "4412923493C57D5DE0D700F753CCE0D1D2D95060122E9F15A5DDBFC5787E50B5CC55EE507BCB084E479AD363AC366B95A98CA5F3000B1479")]
    public void Rfc7253_SampleResults(byte nonceSuffix, int associatedDataLength, int plaintextLength, string expected)
    {
        var key = Sequence(16);
        byte[] nonce = [0xBB, 0xAA, 0x99, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, nonceSuffix];
        var associatedData = Sequence(associatedDataLength);
        var plaintext = Sequence(plaintextLength);

        var ciphertext = AesOcb.Encrypt(key, nonce, associatedData, plaintext, 16);
        Assert.Equal(expected, Convert.ToHexString(ciphertext));
        Assert.Equal(plaintext, AesOcb.Decrypt(key, nonce, associatedData, ciphertext, 16));
    }

    [Fact]
    public void Rfc7253_SampleResult_96BitTag()
    {
        var key = Convert.FromHexString("0F0E0D0C0B0A09080706050403020100");
        var nonce = Convert.FromHexString("BBAA9988776655443322110D");
        var data = Sequence(40);

        var ciphertext = AesOcb.Encrypt(key, nonce, data, data, 12);
        Assert.Equal("1792A4E31E0755FB03E31B22116E6C2DDF9EFD6E33D536F1A0124B0A55BAE884ED93481529C76B6AD0C515F4D1CDD4FDAC4F02AA", Convert.ToHexString(ciphertext));
        Assert.Equal(data, AesOcb.Decrypt(key, nonce, data, ciphertext, 12));
    }

    // https://www.rfc-editor.org/rfc/rfc7253#appendix-A: the iterative test of all the parameter sets
    [Theory]
    [InlineData(128, 128, "67E944D23256C5E0B6C61FA22FDF1EA2")]
    [InlineData(192, 128, "F673F2C3E7174AAE7BAE986CA9F29E17")]
    [InlineData(256, 128, "D90EB8E9C977C88B79DD793D7FFA161C")]
    [InlineData(128, 96, "77A3D8E73589158D25D01209")]
    [InlineData(192, 96, "05D56EAD2752C86BE6932C5E")]
    [InlineData(256, 96, "5458359AC23B0CBA9E6330DD")]
    [InlineData(128, 64, "192C9B7BD90BA06A")]
    [InlineData(192, 64, "0066BC6E0EF34E24")]
    [InlineData(256, 64, "7D4EA5D445501CBE")]
    public void Rfc7253_IterativeTest(int keyLength, int tagLength, string expected)
    {
        // K = zeros(KEYLEN-8) || num2str(TAGLEN,8)
        var key = new byte[keyLength / 8];
        key[^1] = (byte)tagLength;
        var tagLengthBytes = tagLength / 8;

        var c = new List<byte>();
        for (var i = 0; i < 128; i++)
        {
            var s = new byte[i];
            c.AddRange(AesOcb.Encrypt(key, Nonce96(3 * i + 1), s, s, tagLengthBytes));
            c.AddRange(AesOcb.Encrypt(key, Nonce96(3 * i + 2), [], s, tagLengthBytes));
            c.AddRange(AesOcb.Encrypt(key, Nonce96(3 * i + 3), s, [], tagLengthBytes));
        }

        var output = AesOcb.Encrypt(key, Nonce96(385), c.ToArray(), [], tagLengthBytes);
        Assert.Equal(expected, Convert.ToHexString(output));
    }

    // WPT WebCryptoAPI/encrypt_decrypt/aes_ocb_fixtures.js: 120-bit nonce
    [Theory]
    [InlineData("dec0d4fcbf3c4741c892dabd1cd4c04e", 64, "41ddbbd277cf8d6d9061527c3cbdb002fa30401dc5163b60f6d39bb87e744f25a86dbf264d5a4a70de69adbeb6e2f42f82bfcfbe69ba993a84d46c9c6c1e38b4078590a9f978bf10280d9d7faa86c10c2754750d441a0434bc331e37abb5fffc0cee2ec572ff7dab06a2282ad9dd7fa40eabf9690c85f6cda161eed0b31ea1b6866470cd5d6545fefdc9f4430257acf7908d5850ecbcbe49b3fdd38e628d1061a60247e0e82cee52211020e06fbf599d637f1c5faf0f560a2cb47fc8a2258a454e77a560626bd37a15317c3c9c261416c45614b6ad172a2a1ad55415a09c6014ead0b12c706ea5e914387b6e81399e9462f12f9471c60a5010dd5b08c0c2beb7714386eaef146c96536ad1705dc40c8702b197a82ecac5161830694cda15e52d7f6d5d26bdd77b6e176e5ee82ea73205d47845e62395e41873860abb494fc52f05c88ea69f339ac7b2587ff0e48d147abfad427882dfb4da6b86e0fb7fb411c4dfb6e605d9179203fb811ba2df13486c75334d35159e87729d08a650f6a2673d74bc1ec970ef0df42e7bc1ce5b11bc4e5115e2da38b18b11045ea5facfaa8dd3f7c9657ac6796318b162bcc0fc60b56e6f9a90398d7232e4c09a7bf18f4efa9c8c80c3979199822b9a22bbf2f3375b9a95c1d652f9d44f83e6b304fb0687d2", "555c7995756983ec", "1dd03dd5f5674e00")]
    [InlineData("dec0d4fcbf3c4741c892dabd1cd4c04e", 96, "74c34bd46bb35fa14ba0f9e1bbeea50de159df59f7f10b5cc410dab98448c95a97b038a7e34bfbab09e68865a58c901682b22d7728ca49cc3bed132f45227f22c496410688b29fa6eac38b7c47b79abd8e22d1136d959a1a249e6d1fbe274a76a742f51fc23a402ed520333d63ffec865743cdc04d1ed433cd3730613a7a56a521201a6b7a2090c066351bcfa63d08537ba2edd134dc6463fc9d736189620d25428d5e1b8418a106929f40f00cec2dd379c2b88f2348cce7a8e2242d4a2f0e06914477c40621b89b0a675eb4af0d3a7925615095aff0a703fe7a0376bc8920937bf51dbd612c8c893691f1d8a02f8c1e95ef7a673613bb493b22202b23dca7b4dc7c6e3317407f061e266a2d2a6e09b49efbff8112f374fb9f8be56c306978de76dc7f45c6747e9da4a1e178dcfd9e5c9fd6af745039750aae85a6b62bbae90aae5207a1c5a53c4671960f20a50b07a06e3d012dd83381ab9c232e15a015d987dfe7b41ea34bfb970d02a5470c26b9d067848849ad6343a97eb9b43dd8cec045e4bef4e9ac61204adbfe46b1b3c44b9d0b6c5ef0541adbc8ffec6d8bbc6326dd0aad6c192a57010fabc5ffbede151ba296344f523d9c5fe7aaf7bdb15b3e181a4271cf42d065d7126aa1f042d85b0cf732e8f36099e20055eada18d15b2877", "27049adb58b6b61bb87ee245", "6f88de9bd8b87bf7a850f0df")]
    [InlineData("dec0d4fcbf3c4741c892dabd1cd4c04e", 128, "315cd0c5593de2047c2ed779e4d3ea2ae4afec8b30584b51c2f29071316b995b87ddd26678ae6790c9df49dc34754764c81d35560fdf15d0cdeb8f891477c14dca753e14279d5530874c73f127473aafe3be4015405e2e17ec44e4e809adbda26300e46e7e08c9b725acbf956260be5d156cfa5395eb262fcd0f7ebc45c8b2843f740959e36e73fa52297f2c2c5f02bc431f7b17793c0b3992f50e3f83d44627ed94e27ef504aeef407f6cf6d3ac4c81857290f39d557e4365c1cd73939a525c20520728a398873e891d75a452dca9e08f768274d2c02d15cab4fc48df25e09a0e34fc99f3f165e4a9bc5b147731d770b4f4661a7b5efa3f249cfe48e121b46c60902b1a2c93c33a4abc30b2e99eb8fbcf462a7cbf0a8e0d82de6118d80d6fbcd191095120359ff7a56d17d483507d2ced3f686f540c16c330ab01ddac41844b36eb8198f8e351aadc2da8fe6ca6efc047c777fc655a9f85f8d6f55308d9cad71d4549fabae69e26d5b35cb7f1c5ba725a6c00f6c2f27b7ff406a103ee7feaf51e4f5dca675d5a7b2a6985e6509fd1c67a99c826017759282aa2b7054d0aa18f80fb680484acfc929005b71f8c361f9bea6e81290c7de7bb31d2c4d5559b8781216047fdf48c7550246becc6aa5d957040a7208f598f97fd763b98f6c59457", "beaa6f0b3fb1957d7c90cadd33f5cbbc", "f6262b4bbfbf58916cbed84729445a69")]
    [InlineData("d0ee83413f44c43fbad03dcfa61263981d6ddd5ff01e1cf6", 64, "e67ed2401bdf7b577a1bf0ffdc09d971d136688f4b4116228fd97d25161de43b7b2327a8d822b21b17026c9c4a3b442548bf67aa050287e3b8d3a499da305a2588795640045291f6ebc6a96be51bf7e687969fb69029b33ea55efec9009a4141d84806a8490c4a8096a826a33e4db35878bf46b9bc0e5b74b9808796327bc89f641127fc1ccdb2215926347b3e553563078ffbc223997faaaac4311aad05be0fb92203fea95ffe6d6b07fe95bd36ac155939fa8e6b348f1953f27da46c7b8726488b2143f61647d80180eef4b7906ab198a9c16139da7935c8553d1a9cb94a8e0cb6703cd61f93684a32442f01f595d01cd549f4d866415e40d8e0ec65701bc5ad18397163b18e01ab48feab16469e9726dbbe6b76781227c8112cfa794e312cbbdf8696cf7bfad1fa5bcb83ec8352a5aff608376287698dcf83a95c94ea0d21d77e794faee52a87acc71d8efe49936e526b497a266a851c2163738e49667ac297f368c87a34c9b405c3df13ddc3263eee42c5b408c2f98bb857cdcf4fa0eb89c1513114dae2f920c5c18fad879298c6bd1550a1e1142c1c5797deb4bf93594f9f2e08aef2ad363a6c70335f5a8ca232a2f2efdcbe4c4e079c19bec3478c20856230cb91d6cfd46dba0c7ad158c9e1f7e459d97c28882869362df322ca7a37", "64aaab03d1ad1d09", "a9e41948ce4ced3c")]
    [InlineData("d0ee83413f44c43fbad03dcfa61263981d6ddd5ff01e1cf6", 96, "840dcbe2c89fff86b2fbc007a88b45970f4ca73ae9706496542d1d5e7a63e629227a1d803e83836a38b255f6953e3a4983455f097d4486656b7fa4404f80d85ed3a38cc5c9ae5f2fd7431c5d7d57059d9d96eb36befdbe9727478abcc437d6c2d8c5c11e123775a8cb7bc554be04870dd46c3b0e08ff31a69dfdd8674a9844b3157c3862020d3885b9ef6cf6ad72c2b79da6aa3df9f0fd4e6bf4fb725ac903400544a446028b1b4be53ca80f6dd95d260e7ae1f571b9253d08993ce10631f86510a237294fbf4f0068a7abaf604574e12c0475ff52a8ff5f3e274dfbd32add5ac08e34f077041a0088bb085c2e45d96b5cc0ce7ae4737be0eb1bcc480ff6f3ac8d81816078a7d0baaa10030a92645f1c29a3d3284dbbd98d938dd765320ae116669473dceb9f8800f5c92dae8c803c39ee214791c3bb62734f46550b0a25591e3bf19615149e0f6f37e38aea71ed105c2055ad4f509701ca9404f704137211533cae0df2ff4d6770dcaf8f49ffa420d1a50711335f626209aa88b3b37ab4733c4c5c7c618622d18aad3490c6d241f0e8c266d739d1f9a2bafb6bc2375d319e8d971d55b2bb49aecec58707754cd03b25a8c4967d522065e52f14e7e0d0cd672788c5084219135b9d7bd58ecf35226492c4b53673f286479940209725b5b383", "eb66616623f7b31430b25668", "2628d32d3c16432100dff9e4")]
    [InlineData("d0ee83413f44c43fbad03dcfa61263981d6ddd5ff01e1cf6", 128, "acde11358e83107094a04fbf05ac81704f5a0aa2f0d1c9cf6434993d25d889238e63d152f44142195233b1d7cdba1ae7234db79e025555362dadab4037a3559cc3e9cab5f204c9c0b851e5044159533a8ce9dc1edd18689b71b3a4c28f646cff859eba51f613d59814b59cdd509b34a56e6e3362da437e464a1965b536203e1626f3dad9e0310fb560b15d24da138e190c14427a4860b645c28fad488428610062ced676ea20b9ad4168dc5180cfba3ce3ab50c443edda67109a123a1d7c56a1d83bcd7a17a616885043e4841464eb7d38e70a1d6ff9017d3bad193c6b7623d35dfcbd8705fba0aa79c6cbf26c9a8f8fa74d1bcaa23adb9fd04a749208e39120e7914e1d6e4ff66ada05ca044f45708b24894d840f1a7dbb036295ac0bc9f2d405f4d23fc5a0d8c2234a74357f0b464519c39d4ba9f0b8ec8368f30c208bf6141695fd89670bc7559158981f6608e2b973d299f16929a441d2f71cb9d81a4a20d014fec9ad2dc0f3979c161d04fa3b0f7d7026dae659b6d8fb8d5641934eabdc7b4f0e1c4923e3d63d765b38b6abbe00c4b7cde0b31d7b4f43f89cfaad5c2059fc1470fadbbb336dad14135895ce6696fb8b8968b31d80a5e40e37b855494ec670898584df31ab6428b16199993256529917e33710efe04aefd05040ee2440", "e42a32eb37fc57c8f947cc8c5a09266d", "296480a0281da7fdc92a63006ece79e5")]
    [InlineData("67693823fb1d58073f91ece9cc3af910e5532616a4d27b13eb7b74d8000bbf30", 64, "2a7ac32ff29765a265ff639ae0c35cf83ee04986da12cf4022bc258b8784b250f49516becd0e7824f149c4e690300a34e3848e25c7c1a7f292ac7dbe6f6b1dda5f0a18a2faed2af06bd6fd0b12eca7f8e9627a4a5e1d5a626b62b29466ee5d1cc5778c3ecdcce554c6a8aa2f617fe8a29c552e025160994317dfc940d70898ee27cdf2dea17d5164a667bf4604187eccccd84dd2277e44d01fdd9915a6f50c8beed715445b3b6acd148aa4d8eb128bee17dca6f736a6b966da4d144591519d203ea7e8c151dd058a5df05e09df602e41a7e71c2f103057efb9481584e3cefb25a63276c5f840815419b84a667e3eafdb45e953b470e76811a2781392e6d3b5bb79976e539858e051eaa7bfb491452a61bd3b202479176a02c6d0c8e8c57f68ee1e3125dc9919246035fd143b7a4832c5100966bebc5f71fffd88f727232b7c5d9f34f80c04e9d8fdaa7b6f4ce02371357ec2ace5d8a3b137e11ff2db3b68545fe6ad8b66d9ceaa4fdc21d8641836e18af1b8b4be416540d154526b63c7bc7e55ed6a55616d987244d60dd7d04de47d24718f2868692aa08ac93aad611f4960dcda3f6583a163246b46e88fe9406d0359504f93dfbab4e863892f8282e3aea183d30506a9d2443198d246d24b2e85188a3f29fe2e5c1d948b967c359a287db8", "58a1300fdaf14f09", "cf64baa62863035f")]
    [InlineData("67693823fb1d58073f91ece9cc3af910e5532616a4d27b13eb7b74d8000bbf30", 96, "086655181219e01b4788ae312c460386328e76be1a86601c1d975ad47c215649f7ac654963331c7b6514baecf9115969308097325d972e716259c1b55b5f4de03e6755e3e1964aba6fd2bf608bdbc5cab99c765fe62bba5c85555a6bb079a419e8164f621525967a971631dd95e4a535a39dda2880f77d742afc508f2c4f3499ddfe73e7ff1df9eb3c95d40e678fef2739b7178bbe92cf54aa751ba06dd976c5380ee46c3b0ba6542ecf576dd471069e31fc70265ffd3b12c37c6c3ce68a67c79e228c023c685d0f7f0dd54852f24633bcdb7b947d00e277da874adb007481e63185bae472aa98dee73c55e5924c225c39a1d36adffb101f2aa3be1343ed4e52f287988c0c22e8d73875e15c1d38f90c882cc373af56b0fbf695114312e2d7347e8c77cd098ab90b15d2d734ea0dab0da847b218afd1a4b65a846858af81193533842be1520c7db10844b8a8874f7fa070127589af87177aee6ee020b1a202c8aa3e8bb760d28416e6c5a6047fba040b937d7b1f47dcce195b87b03796949f498e896db8ac8fdf76dc155c169091ccdea4283890fdc8db469ffb590d1b3ed32c04592206517b8e903f7a4a794262a0e2b2b80d38d4b1a2d66bef704361d364263059372bad4a51f748c41f9884a784e8f049e1fcba6741c55f4fbecac7b61e", "7b4fe3229590a8741d7b8636", "ec8a698b6702e422d029c2cf")]
    [InlineData("67693823fb1d58073f91ece9cc3af910e5532616a4d27b13eb7b74d8000bbf30", 128, "f7b738e6f62ea246f7f6d6aaf301c549bcbcfd4f053feb203d0287ae420d6e4d59f9b4cbe1a17006d93e60deee2a4c51290cbb8ce131c1a5337d4f5f4f8cc135933a49ce4b1cb2ff7cdc8df80b82d3942473428572db295ac9e1f019e9ef6eafe9cfdf0d81f57c4dad133e0716374f389451d5efff1784e1cf82c32d73ed24696a5f3efab7173eac570f8b3c0d080c3cd4450289ce9d0505d058ef2642d159f9e9cceaf3963e27ef069318df59c81c4771526f9e6150a69ec1253df3e2244a626450177402c5a6d70fd4c8279ec06129787ec80a0ec92f969b76057745a97a650035aed5f1716f2de3c099393b94cccb0472ad7a2380564a752bf9f5f5bdde2662a0d3c04123056848e453063f9d68e264cdad43c3ae88977902b90920b14e4576877e85bcaea747911a30177390357c05244cf42074239b6bc1a129517a6f344ad582003c6ad5ce031fa32e59a37d09a6228c3ac51ca16bd6edc0eb713ede307ced6608d7467470c915bc63a800f58d61a3f0eae62a864dfa091e49886c645b36c113f2daae81959538dca88895a5c4c1d609bdb2797deee1965172d7f402ab7b2d53af41787d4162427a08fdb7515d0d4793a3f5c64e8dc80301013c222e342ed5de8fb1ecbc1b0cc4e2db3f7ef511a9531d562ffbcddf61a422bb5289ba", "8ff25e5dd2add40de95e0fbb056fd88e", "1837d4f4203f985b240c4b424591ec82")]
    public void Wpt_Fixtures(string key, int tagLength, string ciphertext, string tag, string tagWithEmptyAdditionalData)
    {
        var keyBytes = Convert.FromHexString(key);
        var iv = Convert.FromHexString(WptIv);
        var plaintext = Encoding.ASCII.GetBytes(WptPlaintext);
        var additionalData = Encoding.ASCII.GetBytes(WptAdditionalData);
        var tagLengthBytes = tagLength / 8;

        var result = AesOcb.Encrypt(keyBytes, iv, additionalData, plaintext, tagLengthBytes);
        Assert.Equal(ciphertext + tag, Convert.ToHexStringLower(result));
        Assert.Equal(plaintext, AesOcb.Decrypt(keyBytes, iv, additionalData, result, tagLengthBytes));

        var resultWithoutAdditionalData = AesOcb.Encrypt(keyBytes, iv, [], plaintext, tagLengthBytes);
        Assert.Equal(ciphertext + tagWithEmptyAdditionalData, Convert.ToHexStringLower(resultWithoutAdditionalData));
        Assert.Equal(plaintext, AesOcb.Decrypt(keyBytes, iv, [], resultWithoutAdditionalData, tagLengthBytes));
    }

    // Long inputs (several batches of blocks), the expected SHA-256 of the output is computed with the AES-OCB of Node.js (OpenSSL)
    [Theory]
    [InlineData(128, 12, 3000, 5000, 16, "7b979efef3525b623eacf388e5a018decf5bb9313432d7e6ea21a729434b96cb")]
    [InlineData(256, 15, 1027, 1041, 12, "1557b77f86d93aad71f4238bc9234c4a04c71c103a0ce1417ba4279c64a23634")]
    [InlineData(192, 1, 0, 2049, 8, "3fbe33bf8fb1da3f3970c0c31b16903a673c2234929b3913fe378bd48df8f003")]
    [InlineData(128, 7, 1024, 1024, 16, "ae4b79ebc30d73ea1540aed10d68c99cbb1802ebb4937973c14f3ed164d59589")]
    public void LongInputs(int keyLength, int nonceLength, int associatedDataLength, int plaintextLength, int tagLengthBytes, string expectedSha256)
    {
        var key = Generate(keyLength / 8, 7, 3);
        var nonce = Generate(nonceLength, 13, 5);
        var associatedData = Generate(associatedDataLength, 31, 1);
        var plaintext = Generate(plaintextLength, 17, 11);

        var result = AesOcb.Encrypt(key, nonce, associatedData, plaintext, tagLengthBytes);
        Assert.Equal(plaintextLength + tagLengthBytes, result.Length);
        Assert.Equal(expectedSha256, Convert.ToHexStringLower(SHA256.HashData(result)));
        Assert.Equal(plaintext, AesOcb.Decrypt(key, nonce, associatedData, result, tagLengthBytes));
    }

    [Fact]
    public void Decrypt_ModifiedInput_ReturnsNull()
    {
        var key = Sequence(16);
        var nonce = Sequence(12);
        var associatedData = Sequence(20);
        var ciphertext = AesOcb.Encrypt(key, nonce, associatedData, Sequence(37), 16);

        for (var i = 0; i < ciphertext.Length; i++)
        {
            var modified = ciphertext.ToArray();
            modified[i] ^= 0x01;
            Assert.Null(AesOcb.Decrypt(key, nonce, associatedData, modified, 16));
        }

        Assert.Null(AesOcb.Decrypt(key, nonce, Sequence(19), ciphertext, 16));
        Assert.Null(AesOcb.Decrypt(key, Sequence(11), associatedData, ciphertext, 16));
        Assert.Null(AesOcb.Decrypt(key, nonce, associatedData, ciphertext.AsSpan(0, ciphertext.Length - 4), 12));
    }

    [Fact]
    public void Decrypt_TagOnly()
    {
        var key = Sequence(32);
        var nonce = Sequence(1);
        var tag = AesOcb.Encrypt(key, nonce, "data"u8, [], 8);
        Assert.Equal(8, tag.Length);
        var plaintext = AesOcb.Decrypt(key, nonce, "data"u8, tag, 8);
        Assert.NotNull(plaintext);
        Assert.Empty(plaintext);
    }

    [Theory]
    [InlineData(15, 12, 16)]
    [InlineData(17, 12, 16)]
    [InlineData(16, 0, 16)]
    [InlineData(16, 16, 16)]
    [InlineData(16, 12, 0)]
    [InlineData(16, 12, 3)]
    [InlineData(16, 12, 4)]
    [InlineData(16, 12, 6)]
    [InlineData(16, 12, 9)]
    [InlineData(16, 12, 17)]
    public void InvalidParameters_Throws(int keyLength, int nonceLength, int tagLengthBytes)
    {
        Assert.ThrowsAny<ArgumentException>(() => AesOcb.Encrypt(new byte[keyLength], new byte[nonceLength], [], [1, 2, 3], tagLengthBytes));
        Assert.ThrowsAny<ArgumentException>(() => AesOcb.Decrypt(new byte[keyLength], new byte[nonceLength], [], new byte[32], tagLengthBytes));
    }

    [Fact]
    public void Decrypt_ShorterThanTag_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => AesOcb.Decrypt(new byte[16], new byte[12], [], new byte[15], 16));
    }

    private static byte[] Sequence(int length) => Enumerable.Range(0, length).Select(i => (byte)i).ToArray();

    private static byte[] Generate(int length, int multiplier, int increment) => Enumerable.Range(0, length).Select(i => (byte)(((i * multiplier) + increment) % 256)).ToArray();

    // num2str(value, 96)
    private static byte[] Nonce96(int value)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteInt32BigEndian(nonce.AsSpan(8), value);
        return nonce;
    }
}
