using System.Text;
using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Tests.Cryptography;

// Argon2 (RFC 9106), used by the Argon2d, Argon2i and Argon2id algorithms (https://wicg.github.io/webcrypto-modern-algos/#argon2)
public sealed class Argon2Tests
{
    // https://www.rfc-editor.org/rfc/rfc9106#section-5, also in WPT WebCryptoAPI/derive_bits_keys/argon2_vectors.js
    [Theory]
    [InlineData("Argon2d", "512b391b6f1162975371d30919734294f868e3be3984f3c1a13a4db9fabe4acb")]
    [InlineData("Argon2i", "c814d9d1dc7f37aa13f0d77f2494bda1c8de6b016dd388d29952a4c4672b6ce8")]
    [InlineData("Argon2id", "0d640df58d78766c08c037a34a8b53c9d01ef0452d75b65eb52520e96b01e659")]
    public void DeriveBytes_Rfc9106(string type, string expected)
    {
        var password = Enumerable.Repeat((byte)0x01, 32).ToArray();
        var nonce = Enumerable.Repeat((byte)0x02, 16).ToArray();
        var secret = Enumerable.Repeat((byte)0x03, 8).ToArray();
        var associatedData = Enumerable.Repeat((byte)0x04, 12).ToArray();
        var result = Argon2.DeriveBytes(Enum.Parse<Argon2Type>(type), password, nonce, parallelism: 4, memoryKiB: 32, passes: 3, Argon2.Version13, secret, associatedData, outputLength: 32);
        Assert.Equal(expected, Convert.ToHexStringLower(result));
    }

    // The expected values are computed by OpenSSL (openssl kdf ... ARGON2I/ARGON2D/ARGON2ID); the first ones are the test
    // vectors of the reference implementation (password "password", salt "somesalt", t = 2, m = 2^16, p = 1)
    [Theory]
    [InlineData("Argon2i", 0x13, 1, 65536, 2, "", "", 32, "c1628832147d9720c5bd1cfd61367078729f6dfb6f8fea9ff98158e0d7816ed0")]
    [InlineData("Argon2i", 0x10, 1, 65536, 2, "", "", 32, "f6c4db4a54e2a370627aff3db6176b94a2a209a62c8e36152711802f7b30c694")]
    [InlineData("Argon2id", 0x13, 1, 65536, 2, "", "", 32, "09316115d5cf24ed5a15a31a3ba326e5cf32edc24702987c02b6566f61913cf7")]
    [InlineData("Argon2d", 0x13, 1, 65536, 2, "", "", 32, "955e5d5b163a1b60bba35fc36d0496474fba4f6b59ad53628666f07fb2f93eaf")]
    [InlineData("Argon2id", 0x13, 3, 37, 1, "", "", 100, "0a4d9e55c70a3e77deeb4d1c636f22817f1b17a622017b77290ca78bdf8e66df4515bdb8d2d16ebfec9483e6c0bfd0554bb2792b66b3399b2c876ba51d293821ba32feefce9beef01f3abfc33afd1a4bb810d7c4715c417a979f24b53a12693aa7d9f85a")]
    [InlineData("Argon2d", 0x10, 2, 1000, 4, "", "", 64, "0061e7ed889702498408728dafcf90b0eb9f455efaa3dd47aaa5a9faf05e9afa0d74c55f7e77ab03275345a3c87b5603730fcbc2ab063e4fa87caf915773f763")]
    [InlineData("Argon2i", 0x13, 1, 300, 3, "key", "data", 65, "511195b44fe2bbc12d2ed3e7ed0aa3701aef8e48c163845d0fb07ec3c462e3394bb2d97dfdd0bbfe30aa9780efb2dca08415e5d93447b6f6b50144e95ce283b8de")]
    [InlineData("Argon2id", 0x13, 1, 8, 1, "", "", 4, "6b7a947d")]
    public void DeriveBytes_OpenSsl(string type, int version, int parallelism, int memoryKiB, int passes, string secret, string associatedData, int outputLength, string expected)
    {
        var result = Argon2.DeriveBytes(Enum.Parse<Argon2Type>(type), "password"u8, "somesalt"u8, parallelism, memoryKiB, passes, version, Encoding.ASCII.GetBytes(secret), Encoding.ASCII.GetBytes(associatedData), outputLength);
        Assert.Equal(expected, Convert.ToHexStringLower(result));
    }

    [Theory]
    [InlineData(0, 8, 1, 0x13, 32)]
    [InlineData(16777216, 134217728, 1, 0x13, 32)]
    [InlineData(4, 31, 1, 0x13, 32)]
    [InlineData(1, 8, 0, 0x13, 32)]
    [InlineData(1, 8, 1, 0x12, 32)]
    [InlineData(1, 8, 1, 0x13, 3)]
    [InlineData(1, -1, 1, 0x13, 32)]
    public void DeriveBytes_InvalidParameters_Throws(int parallelism, int memoryKiB, int passes, int version, int outputLength)
    {
        Assert.ThrowsAny<ArgumentException>(() => Argon2.DeriveBytes(Argon2Type.Argon2id, "password"u8, "somesalt"u8, parallelism, memoryKiB, passes, version, [], [], outputLength));
    }

    [Fact]
    public void DeriveBytes_InvalidType_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => Argon2.DeriveBytes((Argon2Type)3, "password"u8, "somesalt"u8, 1, 8, 1, Argon2.Version13, [], [], 32));
    }

    [Fact]
    public void DeriveBytes_EmptyInputs()
    {
        // RFC 9106 does not require a minimum length for the password and the nonce
        var result = Argon2.DeriveBytes(Argon2Type.Argon2id, [], [], 1, 8, 1, Argon2.Version13, [], [], 32);
        Assert.Equal(32, result.Length);
    }
}
