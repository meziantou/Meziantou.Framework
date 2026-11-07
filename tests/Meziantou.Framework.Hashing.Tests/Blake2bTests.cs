namespace Meziantou.Framework.Hashing.Tests;

// BLAKE2b (RFC 7693)
public sealed class Blake2bTests
{
    [Fact]
    public void Hash_Abc()
    {
        // https://www.rfc-editor.org/rfc/rfc7693#appendix-A
        var digest = new byte[64];
        Blake2b.HashData("abc"u8, digest);
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
        Blake2b.HashData(data, digest);
        Assert.Equal(expected, Convert.ToHexStringLower(digest));
        Assert.Equal(expected, Convert.ToHexStringLower(Blake2b.HashData(data, hashSize)));

        // The same digest when the data is appended in several parts
        var blake2b = new Blake2b(hashSize);
        for (var i = 0; i < length; i += 7)
        {
            blake2b.AppendData(data.AsSpan(i, Math.Min(7, length - i)));
        }

        var incremental = new byte[hashSize];
        blake2b.GetHashAndReset(incremental);
        Assert.Equal(expected, Convert.ToHexStringLower(incremental));
    }

    [Fact]
    public void Blake2b_Usage()
    {
        #region Blake2b_Usage
        // A digest of 32 bytes (BLAKE2b-256); the default size is 64 bytes (BLAKE2b-512)
        var digest = Blake2b.HashData("abc"u8, hashSize: 32);

        // The same digest, with the data appended in several parts
        var blake2b = new Blake2b(hashSize: 32);
        blake2b.AppendData("a"u8);
        blake2b.AppendData("bc"u8);
        Assert.Equal(digest, blake2b.GetHashAndReset());
        #endregion
    }

    [Fact]
    public void GetHashAndReset_StartsANewDigestOfTheSameSize()
    {
        var blake2b = new Blake2b(48);
        Assert.Equal(48, blake2b.HashSize);
        blake2b.AppendData([1, 2, 3]);
        Assert.Equal(Blake2b.HashData([1, 2, 3], 48), blake2b.GetHashAndReset());

        blake2b.AppendData([4, 5]);
        Assert.Equal(Blake2b.HashData([4, 5], 48), blake2b.GetHashAndReset());
        Assert.Equal(Blake2b.HashData([], 48), blake2b.GetHashAndReset());
    }

    [Fact]
    public void DefaultHashSize_Is64()
    {
        Assert.Equal(64, new Blake2b().HashSize);
        Assert.HasCount(64, Blake2b.HashData("abc"u8));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void Constructor_InvalidHashSize_Throws(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>("hashSize", () => new Blake2b(size));
        Assert.Throws<ArgumentOutOfRangeException>("hashSize", () => Blake2b.HashData([], size));
        Assert.Throws<ArgumentOutOfRangeException>("destination", () => Blake2b.HashData([], new byte[size]));
    }

    [Fact]
    public void GetHashAndReset_DestinationOfAnotherLength_Throws()
    {
        var blake2b = new Blake2b(32);
        Assert.Throws<ArgumentException>("destination", () => blake2b.GetHashAndReset(new byte[31]));
    }
}
