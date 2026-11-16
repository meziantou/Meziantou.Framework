namespace Meziantou.Framework.Compression.InteropTests;

/// <summary>The 7z archives of the library are tested and extracted by 7-Zip, and the 7z archives of 7-Zip are read by the library.</summary>
public sealed class SevenZipInteropTests
{
    public static TheoryData<SevenZipCompressionMethod, bool, string?> WriterCases()
    {
        var data = new TheoryData<SevenZipCompressionMethod, bool, string?>();
        foreach (var method in Enum.GetValues<SevenZipCompressionMethod>())
        {
            foreach (var solid in new[] { true, false })
            {
                data.Add(method, solid, null);
                data.Add(method, solid, "secret");
            }
        }

        return data;
    }

    public static TheoryData<SevenZipFilter> Filters() => [.. Enum.GetValues<SevenZipFilter>()];

    // The switches of 7zz a: methods, filters, solid or not, header compression and encryption, volumes
    public static TheoryData<string, string?> ReaderCases() => new()
    {
        { "-m0=LZMA2 -mx=5", null },
        { "-m0=LZMA2 -mx=9 -md=4m", null },
        { "-m0=LZMA -mx=1", null },
        { "-m0=PPMd:o=8:mem=16m", null },
        { "-m0=BZip2", null },
        { "-m0=Deflate", null },
        { "-m0=Deflate64", null },
        { "-m0=Copy", null },
        { "-m0=LZMA2 -mf=BCJ2", null },
        { "-m0=LZMA2 -mf=ARM64", null },
        { "-m0=LZMA2 -mf=Delta:4", null },
        { "-m0=LZMA2 -ms=off", null },
        { "-m0=LZMA2 -mhc=off", null },
        { "-m0=LZMA2 -psecret", "secret" },
        { "-m0=LZMA2 -mhe=on -psecret", "secret" },
        { "-m0=PPMd -ms=off -mhe=on -psecret", "secret" },
    };

    [Theory]
    [MemberData(nameof(WriterCases))]
    public async Task Writer_ArchiveIsExtractedBy7Zip(SevenZipCompressionMethod method, bool solid, string? password)
    {
        await AssertWriterAsync(new SevenZipCompressionOptions { Method = method, Solid = solid, Password = password, EncryptHeaders = password is not null }, password);
    }

    [Theory]
    [MemberData(nameof(Filters))]
    public async Task Writer_FiltersAreExtractedBy7Zip(SevenZipFilter filter)
    {
        await AssertWriterAsync(new SevenZipCompressionOptions { Filter = filter }, password: null);
    }

    [Theory]
    [MemberData(nameof(ReaderCases))]
    public async Task Reader_ExtractsArchivesOf7Zip(string switches, string? password)
    {
        var tools = await CompressionTools.GetAsync();
        using var folder = TestFiles.CreateTemporaryFolder();
        var source = TestFiles.CreateSourceTree(folder.FullPath);
        var archive = folder.GetPath("7zz.7z");
        await tools.RunSevenZipAsync(["a", "-t7z", .. switches.Split(' '), archive, Path.Combine(source, "*")]);

        var destination = folder.GetPath("extracted");
        SevenZipFile.ExtractToDirectory(archive, destination, options: new ArchiveReaderOptions { Password = password });
        Assert.Equal(TestFiles.GetTree(source), TestFiles.GetTree(destination));
    }

    [Fact]
    public async Task Reader_ExtractsMultiVolumeArchivesOf7Zip()
    {
        var tools = await CompressionTools.GetAsync();
        using var folder = TestFiles.CreateTemporaryFolder();
        var source = TestFiles.CreateSourceTree(folder.FullPath);
        var archive = folder.GetPath("volumes.7z");
        await tools.RunSevenZipAsync("a", "-t7z", "-mx=1", "-v64k", archive, Path.Combine(source, "*"));
        Assert.True(File.Exists(archive + ".002"));

        var destination = folder.GetPath("extracted");
        ArchiveFile.ExtractToDirectory(archive + ".001", destination);
        Assert.Equal(TestFiles.GetTree(source), TestFiles.GetTree(destination));
    }

    [Fact]
    public async Task Update_ArchiveOf7ZipIsTestedBy7Zip()
    {
        var tools = await CompressionTools.GetAsync();
        using var folder = TestFiles.CreateTemporaryFolder();
        var source = TestFiles.CreateSourceTree(folder.FullPath);
        var archive = folder.GetPath("update.7z");
        await tools.RunSevenZipAsync("a", "-t7z", archive, Path.Combine(source, "*"));

        using (var updated = SevenZipFile.Open(archive, SevenZipArchiveMode.Update))
        {
            updated.GetEntry("random.bin")!.Delete();
            var entry = updated.CreateEntry("added/new.txt");
            using var stream = entry.Open();
            stream.Write("added by the library\n"u8);
        }

        await tools.RunSevenZipAsync("t", archive);
        var destination = folder.GetPath("extracted");
        await tools.RunSevenZipAsync("x", "-y", "-o" + destination, archive);
        Assert.False(File.Exists(Path.Combine(destination, "random.bin")));
        Assert.Equal("added by the library\n", File.ReadAllText(Path.Combine(destination, "added", "new.txt")));
        Assert.Equal(File.ReadAllText(Path.Combine(source, "readme.txt")), File.ReadAllText(Path.Combine(destination, "readme.txt")));
    }

    private static async Task AssertWriterAsync(SevenZipCompressionOptions options, string? password)
    {
        var tools = await CompressionTools.GetAsync();
        using var folder = TestFiles.CreateTemporaryFolder();
        var source = TestFiles.CreateSourceTree(folder.FullPath);
        var archive = folder.GetPath("library.7z");
        SevenZipFile.CreateFromDirectory(source, archive, options);

        var passwordSwitch = "-p" + (password ?? "-");
        await tools.RunSevenZipAsync("t", passwordSwitch, archive);
        var destination = folder.GetPath("extracted");
        await tools.RunSevenZipAsync("x", "-y", passwordSwitch, "-o" + destination, archive);
        Assert.Equal(TestFiles.GetTree(source), TestFiles.GetTree(destination));
    }
}
