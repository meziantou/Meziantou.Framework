using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Tests.Cryptography;

// BLAKE2b (RFC 7693), used by Argon2
public sealed class Blake2bTests
{
    [Fact]
    public void Hash_Abc()
    {
        // https://www.rfc-editor.org/rfc/rfc7693#appendix-A
        var digest = new byte[64];
        Blake2b.Hash("abc"u8, digest);
        Assert.Equal("ba80a53f981c4d0d6a2797b69f12f6e94c212f14685ac4b74b12bb6fdbffa2d17d87c5392aab792dc252d5de4533cc9518d38aa8dbf1925ab92386edd4009923", Convert.ToHexStringLower(digest));
    }

    // The expected values are computed by hashlib.blake2b of Python
    [Theory]
    [InlineData(0, 64, "786a02f742015903c6c6fd852552d272912f4740e15847618a86e217f71f5419d25e1031afee585313896444934eb04b903a685b1448b755d56f701afe9be2ce")]
    [InlineData(1, 1, "a1")]
    [InlineData(127, 32, "f2fe67ff342e21b8f45e8f2e0bcd1d9243245d50ee6c78042e9c491388791c72")]
    [InlineData(128, 64, "2319e3789c47e2daa5fe807f61bec2a1a6537fa03f19ff32e87eecbfd64b7e0e8ccff439ac333b040f19b0c4ddd11a61e24ac1fe0f10a039806c5dcc0da3d115")]
    [InlineData(129, 48, "a95db6e5ccd191793ad20179bfd63e8c7aedf0cc1084549f73127e3fccc738b405ac2a93d692e76214320089121073e5")]
    [InlineData(256, 64, "93463ac058b6163eb43be3f5bb32b28541498f4e3366f1effe253ad44e1e076e41c3616046027c82a7124f8f4746668ad10b12e8e25a95ac8f3151df01cd5a93")]
    [InlineData(1000, 17, "825409d4f8f96ba454bab52cc819d5e720")]
    public void Hash_Lengths(int length, int hashSize, string expected)
    {
        var data = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        var digest = new byte[hashSize];
        Blake2b.Hash(data, digest);
        Assert.Equal(expected, Convert.ToHexStringLower(digest));

        // The same digest when the data is appended in several parts
        var blake2b = new Blake2b(hashSize);
        for (var i = 0; i < length; i += 7)
        {
            blake2b.Append(data.AsSpan(i, Math.Min(7, length - i)));
        }

        var incremental = new byte[hashSize];
        blake2b.Finish(incremental);
        Assert.Equal(expected, Convert.ToHexStringLower(incremental));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void Constructor_InvalidHashSize_Throws(int hashSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Blake2b(hashSize));
    }
}
