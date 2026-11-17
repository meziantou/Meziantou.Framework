using Meziantou.Framework.Compression.Internals.Rar;

namespace Meziantou.Framework.Compression.Tests;

public sealed class Blake2spTests
{
    // The expected values come from Python's hashlib.blake2s with the tree parameters of BLAKE2sp (fanout 8, depth 2, inner size 32)
    [Theory]
    [InlineData(0, "dd0e891776933f43c7d032b08a917e25741f8aa9a12c12e1cac8801500f2ca4f")]
    [InlineData(512, "8d9e357863298dd8364b7caf4234317f8a49f180d788b7abffb521925f1e1ff1")]
    [InlineData(513, "8a4bc3330497e681f15daf24fc496044a1c32bf0a837a210399e1ae4af7e92be")]
    public void Hash_Modulo251(int length, string expected)
    {
        var data = new byte[length];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        Assert.Equal(expected, Convert.ToHexStringLower(Blake2sp.Hash(data)));
    }

    [Fact]
    public void Hash_Abc()
    {
        Assert.Equal("70f75b58f1fecab821db43c88ad84edde5a52600616cd22517b7bb14d440a7d5", Convert.ToHexStringLower(Blake2sp.Hash("abc"u8)));
    }

    [Fact]
    public void Hash_Incremental()
    {
        var data = new byte[256 * 20];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)i;
        }

        // Append the data in chunks that do not align with the 64-byte blocks of the leaves
        var hash = new Blake2sp();
        var offset = 0;
        var size = 1;
        while (offset < data.Length)
        {
            var count = Math.Min(size, data.Length - offset);
            hash.Append(data.AsSpan(offset, count));
            offset += count;
            size = (size * 3) + 1;
        }

        Assert.Equal("804b2bbfd689bdb86037e7cf35fb6fdc3f9dfbb993dd63e9fd11fe79c576344e", Convert.ToHexStringLower(hash.GetHash()));
        Assert.Equal("804b2bbfd689bdb86037e7cf35fb6fdc3f9dfbb993dd63e9fd11fe79c576344e", Convert.ToHexStringLower(Blake2sp.Hash(data)));
    }

    [Fact]
    public void Rar29Sha1_MatchesSha1()
    {
        // Without the RAR 2.9 variant, the managed SHA-1 is the standard SHA-1
        var data = new byte[1000];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 7);
        }

        var sha1 = new Rar29Sha1();
        sha1.Append(data.AsSpan(0, 3));
        sha1.Append(data.AsSpan(3, 200));
        sha1.Append(data.AsSpan(203));
        var actual = new byte[20];
        sha1.GetHash(actual);
        using var reference = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA1);
        reference.AppendData(data);
        Assert.Equal(Convert.ToHexString(reference.GetHashAndReset()), Convert.ToHexString(actual));
    }
}
