using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Hashing.Tests;

// CRC-32 (ISO 3309), the CRC-32 of Ogg pages (RFC 3533) and Adler-32 (RFC 1950)
public sealed class ChecksumTests
{
    [Theory]
    [InlineData("", 0x00000000u)]
    [InlineData("123456789", 0xCBF43926u)]
    [InlineData("IEND", 0xAE426082u)]
    public void Crc32_PublishedVectors(string text, uint expected)
    {
        Assert.Equal(expected, Crc32.Compute(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void Crc32_Usage()
    {
        #region Crc32_Usage
        var crc = Crc32.Compute("123456789"u8);
        Assert.Equal(0xCBF43926u, crc);

        // The CRC of data received in several parts
        var running = Crc32.Compute("1234"u8);
        running = Crc32.Append(running, "56789"u8);
        Assert.Equal(crc, running);
        #endregion
    }

    [Fact]
    public void Crc32_AppendToEmptyData()
    {
        Assert.Equal(Crc32.Compute("123456789"u8), Crc32.Append(0, "123456789"u8));
        Assert.Equal(Crc32.Compute("123456789"u8), Crc32.Append(Crc32.Compute("123456789"u8), []));
    }

    // Known answers for the Ogg variant of CRC-32: polynomial 0x04C11DB7, initial value 0, not reflected, no final XOR.
    // They come from an independent implementation of the specification, so a wrong table, a wrong shift direction or an
    // off-by-one fails here rather than silently producing files that every Ogg player rejects.
    [Theory]
    [InlineData("", 0x00000000u)]
    [InlineData("123456789", 0x89A1897Fu)]
    [InlineData("OggS", 0x5FB0A94Fu)]
    public void OggCrc32_KnownAnswer(string text, uint expected)
    {
        Assert.Equal(expected, OggCrc32.Compute(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void OggCrc32_SingleByte_KnownAnswer()
    {
        // A single 0x01 byte shifts the polynomial into the register exactly once
        Assert.Equal(0x04C11DB7u, OggCrc32.Compute([0x01]));
    }

    [Fact]
    public void OggCrc32_Append_MatchesComputeOverTheWholeInput()
    {
        byte[] data = [0x4F, 0x67, 0x67, 0x53, 0x00, 0x02, 0xFF, 0x10];

        var whole = OggCrc32.Compute(data);
        var incremental = OggCrc32.Append(OggCrc32.Compute(data.AsSpan(0, 3)), data.AsSpan(3));

        Assert.Equal(whole, incremental);
    }

    [Theory]
    [InlineData("", 0x00000001u)]
    [InlineData("Wikipedia", 0x11E60398u)]
    public void Adler32_PublishedVectors(string text, uint expected)
    {
        Assert.Equal(expected, Adler32.Compute(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void Adler32_Usage()
    {
        #region Adler32_Usage
        var checksum = Adler32.Compute("Wikipedia"u8);
        Assert.Equal(0x11E60398u, checksum);

        // The checksum of data received in several parts
        var running = Adler32.Compute("Wiki"u8);
        running = Adler32.Append(running, "pedia"u8);
        Assert.Equal(checksum, running);
        #endregion
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Append_MatchesComputeOverTheWholeInput()
    {
        var random = new Random(31);
        var data = new byte[20_000];
        random.NextBytes(data);
        int[] splits = [0, 1, 7, 64, 5552, 10_000, 19_999, 20_000];
        foreach (var split in splits)
        {
            Assert.Equal(Crc32.Compute(data), Crc32.Append(Crc32.Compute(data.AsSpan(0, split)), data.AsSpan(split)));
            Assert.Equal(OggCrc32.Compute(data), OggCrc32.Append(OggCrc32.Compute(data.AsSpan(0, split)), data.AsSpan(split)));
            Assert.Equal(Adler32.Compute(data), Adler32.Append(Adler32.Compute(data.AsSpan(0, split)), data.AsSpan(split)));
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void VectorPathsMatchTheByteAtATimeReferences()
    {
        var random = new Random(32);
        var data = new byte[70_000];
        random.NextBytes(data);
        int[] lengths = [0, 1, 7, 8, 9, 15, 16, 17, 63, 64, 65, 79, 80, 81, 127, 128, 129, 191, 192, 193, 255, 5535, 5536, 5537, 5552, 11_073, 70_000];
        foreach (var length in lengths)
        {
            foreach (var initial in new uint[] { 1, 0xFFF0FFF0, 0x12345678 % 65521, Crc32.Initial })
            {
                var span = data.AsSpan(0, length);
                AssertCrc32PathsMatchTheReference(initial, span);

                var adler = ((initial >> 16) % 65521 << 16) | ((initial & 0xFFFF) % 65521);
                AssertAdler32PathsMatchTheReference(adler, span);
            }
        }

        // Extreme bytes (all 0xFF) maximize every partial sum
        var ones = new byte[200_000];
        Array.Fill(ones, (byte)0xFF);
        AssertAdler32PathsMatchTheReference(0xFFF0FFF0, ones);
        AssertCrc32PathsMatchTheReference(Crc32.Initial, ones);
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void Adler32PathsMatchTheByteAtATimeReferenceForEveryShortLength()
    {
        // Every length up to 3000 covers every tail of the 16-byte and 32-byte steps; an odd start makes the loads unaligned
        var random = new Random(33);
        var data = new byte[3001];
        random.NextBytes(data);
        var ones = new byte[3001];
        Array.Fill(ones, (byte)0xFF);
        for (var length = 0; length <= 3000; length++)
        {
            AssertAdler32PathsMatchTheReference(1, data.AsSpan(1, length));
            AssertAdler32PathsMatchTheReference(0x12345678 % 65521, data.AsSpan(0, length));
            AssertAdler32PathsMatchTheReference(0xFFF0FFF0, ones.AsSpan(0, length));
        }
    }

    private static void AssertCrc32PathsMatchTheReference(uint crc, ReadOnlySpan<byte> data)
    {
        var expected = Crc32.UpdateScalar(crc, data);
        Assert.Equal(expected, Crc32.Update(crc, data));
        Assert.Equal(expected, Crc32.UpdateTables(crc, data));
        if (Pclmulqdq.IsSupported)
        {
            Assert.Equal(expected, Crc32.UpdateCarrylessMultiply(crc, data));
        }
    }

    private static void AssertAdler32PathsMatchTheReference(uint adler, ReadOnlySpan<byte> data)
    {
        var expected = Adler32.UpdateScalar(adler, data);
        Assert.Equal(expected, Adler32.Update(adler, data));
        if (Vector128.IsHardwareAccelerated)
        {
            Assert.Equal(expected, Adler32.UpdateVector128(adler, data));
        }

        if (System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported)
        {
            Assert.Equal(expected, Adler32.UpdateAdvSimd(adler, data));
        }

        if (Ssse3.IsSupported)
        {
            Assert.Equal(expected, Adler32.UpdateSsse3(adler, data));
        }

        if (Avx2.IsSupported)
        {
            Assert.Equal(expected, Adler32.UpdateAvx2(adler, data));
        }
    }
}
