using System.Text;

namespace Meziantou.Framework.Compression.Tests;

public sealed class UnixCompressStreamTests
{
    private static string GetTestDataPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "UnixCompress", fileName);

    private static byte[] GetSampleText() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "BZip2", "sample.txt"));

    private static byte[] Decompress(byte[] data, int readSize = 81920)
    {
        using var stream = new UnixCompressStream(new MemoryStream(data));
        using var output = new MemoryStream();
        var buffer = new byte[readSize];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    // The data of clear-b9.Z: "ab" 6000 times, then 24000 bytes 'a' or 'b' from a linear congruential generator
    private static byte[] CreateClearData()
    {
        var data = new List<byte>();
        for (var i = 0; i < 6000; i++)
        {
            data.Add((byte)'a');
            data.Add((byte)'b');
        }

        var x = 1L;
        for (var i = 0; i < 24000; i++)
        {
            x = ((x * 1103515245) + 12345) & 0x7FFFFFFF;
            data.Add((byte)('a' + ((x >> 16) & 1)));
        }

        return [.. data];
    }

    // The text of words-b16.Z: 6000 words chosen by a linear congruential generator
    private static byte[] CreateWordsText()
    {
        var words = "the quick brown fox jumps over lazy dog compression stream block huffman table selector run length move front burrows wheeler transform data archive".Split(' ');
        var parts = new List<string>();
        var x = 1L;
        for (var i = 0; i < 6000; i++)
        {
            x = ((x * 1103515245) + 12345) & 0x7FFFFFFF;
            parts.Add(words[(x >> 16) % words.Length]);
        }

        return Encoding.ASCII.GetBytes(string.Join(' ', parts));
    }

    [Fact]
    public void Decompress_NonSeekableStream()
    {
        using var stream = new UnixCompressStream(ZipTestData.CreateForwardOnlyStream(File.ReadAllBytes(GetTestDataPath("sample-b16.Z")), 1));
        using var output = new MemoryStream();
        stream.CopyTo(output);
        Assert.Equal(GetSampleText(), output.ToArray());
    }

    [Theory]
    [InlineData("sample-b9.Z")]
    [InlineData("sample-b10.Z")]
    [InlineData("sample-b11.Z")]
    [InlineData("sample-b12.Z")]
    [InlineData("sample-b16.Z")]
    public void Decompress_CompressOutput(string fileName)
    {
        var actual = Decompress(File.ReadAllBytes(GetTestDataPath(fileName)));
        Assert.Equal(GetSampleText(), actual);
    }

    [Fact]
    public void Decompress_CompressOutput_ClearCodes()
    {
        var actual = Decompress(File.ReadAllBytes(GetTestDataPath("clear-b9.Z")));
        Assert.Equal(CreateClearData(), actual);
    }

    [Fact]
    public void Decompress_CompressOutput_WideCodes()
    {
        var actual = Decompress(File.ReadAllBytes(GetTestDataPath("words-b16.Z")));
        Assert.Equal(CreateWordsText(), actual);
    }

    [Theory]
    [InlineData("sample-b9.Z")]
    [InlineData("clear-b9.Z")]
    [InlineData("words-b16.Z")]
    public void Decompress_OneByteReads(string fileName)
    {
        var data = File.ReadAllBytes(GetTestDataPath(fileName));
        Assert.Equal(Decompress(data), Decompress(data, readSize: 1));
    }

    [Fact]
    public async Task Decompress_Async()
    {
        await using var stream = new UnixCompressStream(File.OpenRead(GetTestDataPath("sample-b12.Z")));
        using var output = new MemoryStream();
        await stream.CopyToAsync(output, TestContext.Current.CancellationToken);
        Assert.Equal(GetSampleText(), output.ToArray());
    }

    [Fact]
    public void Decompress_HeaderOnly()
    {
        Assert.Empty(Decompress([0x1F, 0x9D, 0x90]));
    }

    [Fact]
    public void Decompress_NotBlockMode()
    {
        // The codes 'a', 'b', 256 ("ab": a regular code without the block mode), 257 ("ba"), in 9 bits
        var actual = Decompress([0x1F, 0x9D, 0x10, .. PackCodes([(byte)'a', (byte)'b', 256, 257])]);
        Assert.Equal("ababba"u8.ToArray(), actual);
    }

    [Fact]
    public void Decompress_KwKwK()
    {
        // 'a', then 257: the code being added ("aa")
        var actual = Decompress([0x1F, 0x9D, 0x90, .. PackCodes([(byte)'a', 257])]);
        Assert.Equal("aaa"u8.ToArray(), actual);
    }

    [Theory]
    [InlineData(new byte[] { 0x1F })]
    [InlineData(new byte[] { 0x1F, 0x8B, 0x90 })]
    [InlineData(new byte[] { 0x1F, 0x9D })]
    [InlineData(new byte[] { 0x1F, 0x9D, 0x08 })]
    [InlineData(new byte[] { 0x1F, 0x9D, 0x11 })]
    public void Decompress_InvalidHeader_Throws(byte[] data)
    {
        Assert.Throws<InvalidDataException>(() => Decompress(data));
    }

    [Fact]
    public void Decompress_UndefinedCode_Throws()
    {
        Assert.Throws<InvalidDataException>(() => Decompress([0x1F, 0x9D, 0x90, .. PackCodes([(byte)'a', 300])]));
        Assert.Throws<InvalidDataException>(() => Decompress([0x1F, 0x9D, 0x90, .. PackCodes([300])]));
    }

    [Fact]
    public void Write_Throws()
    {
        using var stream = new UnixCompressStream(new MemoryStream());
        Assert.False(stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.Write([1]));
    }

    // Packs codes of 9 bits, least significant bit first
    private static byte[] PackCodes(int[] codes)
    {
        var bytes = new List<byte>();
        var bits = 0;
        var count = 0;
        foreach (var code in codes)
        {
            bits |= code << count;
            count += 9;
            while (count >= 8)
            {
                bytes.Add((byte)bits);
                bits >>= 8;
                count -= 8;
            }
        }

        if (count > 0)
        {
            bytes.Add((byte)bits);
        }

        return [.. bytes];
    }
}
