using System.IO.Compression;

namespace Meziantou.Framework.Compression.InteropTests;

/// <summary>
/// The zip archives that 7-Zip writes with every method and encryption are read by the library, and the XZ, BZip2 and LZMA
/// streams round trip between 7-Zip and the library.
/// </summary>
public sealed class ZipAndStreamInteropTests
{
    // The switches of 7zz a -tzip
    public static TheoryData<string, string?> ZipCases() => new()
    {
        { "-mm=Deflate", null },
        { "-mm=Deflate64", null },
        { "-mm=BZip2", null },
        { "-mm=LZMA", null },
        { "-mm=PPMd", null },
        { "-mm=Copy", null },
        { "-mm=Deflate -mem=ZipCrypto -psecret", "secret" },
        { "-mm=Deflate -mem=AES128 -psecret", "secret" },
        { "-mm=LZMA -mem=AES256 -psecret", "secret" },
        { "-mm=BZip2 -mem=AES192 -psecret", "secret" },
    };

    [Theory]
    [MemberData(nameof(ZipCases))]
    public async Task Zip_ArchivesOf7ZipAreRead(string switches, string? password)
    {
        var tools = await CompressionTools.GetAsync();
        using var folder = TestFiles.CreateTemporaryFolder();
        var source = TestFiles.CreateSourceTree(folder.FullPath);
        var archive = folder.GetPath("7zz.zip");
        await tools.RunSevenZipAsync(["a", "-tzip", .. switches.Split(' '), archive, Path.Combine(source, "*")]);

        var options = new ArchiveReaderOptions { Password = password };
        var randomAccess = folder.GetPath("random-access");
        ArchiveFile.ExtractToDirectory(archive, randomAccess, options: options);
        Assert.Equal(TestFiles.GetTree(source), TestFiles.GetTree(randomAccess));

        var forward = folder.GetPath("forward");
        using (var reader = ArchiveReader.Create(new ForwardOnlyStream(File.ReadAllBytes(archive)), options))
        {
            reader.ExtractToDirectory(forward);
        }

        Assert.Equal(TestFiles.GetTree(source), TestFiles.GetTree(forward));
    }

    [Theory]
    [InlineData("xz")]
    [InlineData("bzip2")]
    public async Task Streams_OfTheLibraryAreExtractedBy7Zip(string format)
    {
        var tools = await CompressionTools.GetAsync();
        using var folder = TestFiles.CreateTemporaryFolder();
        var data = CreateData();
        var path = folder.GetPath("data.bin." + (format is "xz" ? "xz" : "bz2"));
        using (var file = File.Create(path))
        using (var stream = CreateCompressor(format, file))
        {
            stream.Write(data);
        }

        await tools.RunSevenZipAsync("t", path);
        var destination = folder.GetPath("extracted");
        await tools.RunSevenZipAsync("x", "-y", "-o" + destination, path);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(destination, "data.bin")));
    }

    [Theory]
    [InlineData("xz", "-mx=1")]
    [InlineData("xz", "-mx=9")]
    [InlineData("xz", "-mf=ARM64")]
    [InlineData("bzip2", "-mx=9")]
    [InlineData("bzip2", "-mmt=4")]
    public async Task Streams_Of7ZipAreDecompressed(string format, string switches)
    {
        var tools = await CompressionTools.GetAsync();
        using var folder = TestFiles.CreateTemporaryFolder();
        var data = CreateData();
        var input = folder.GetPath("data.bin");
        File.WriteAllBytes(input, data);
        var path = input + (format is "xz" ? ".xz" : ".bz2");
        await tools.RunSevenZipAsync("a", "-t" + format, switches, path, input);

        using var file = File.OpenRead(path);
        using Stream stream = format is "xz" ? new XzStream(file, CompressionMode.Decompress) : new BZip2Stream(file, CompressionMode.Decompress);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        Assert.Equal(data, output.ToArray());
    }

    private static Stream CreateCompressor(string format, Stream output) => format switch
    {
        "xz" => new XzStream(output, new XzCompressionOptions { Preset = 6, BlockSize = 256 * 1024 }, leaveOpen: true),
        _ => new BZip2Stream(output, CompressionLevel.SmallestSize, leaveOpen: true),
    };

    private static byte[] CreateData()
    {
        var text = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 20000).Select(i => $"record {i}; value {i * 7 % 1013}\n")));
        return [.. text, .. TestFiles.CreatePseudoRandom(100_000, seed: 3), .. text];
    }

    /// <summary>A non-seekable stream over bytes.</summary>
    private sealed class ForwardOnlyStream(byte[] data) : MemoryStream(data, writable: false)
    {
        public override bool CanSeek => false;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override long Length => throw new NotSupportedException();
    }
}
