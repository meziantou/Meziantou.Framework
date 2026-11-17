namespace Meziantou.Framework.Compression.InteropTests;

/// <summary>The RAR archives of the unit tests are extracted by unrar and by the library, to the same files.</summary>
public sealed class RarInteropTests
{
    private static readonly string RarDirectory = Path.Combine(AppContext.BaseDirectory, "TestData", "Rar");

    // The archives without a password whose first volume is the file itself
    public static TheoryData<string> Archives()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(RarDirectory, "*.rar").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (name.Contains("encrypted", StringComparison.Ordinal) || name.Contains("psw", StringComparison.Ordinal))
                continue;

            if (name.Contains(".part", StringComparison.Ordinal) && !name.Contains(".part1.", StringComparison.Ordinal) && !name.Contains(".part01.", StringComparison.Ordinal) && !name.Contains(".part0001.", StringComparison.Ordinal))
                continue;

            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Archives))]
    public async Task ExtractsTheFilesOfUnrar(string name)
    {
        var tools = await CompressionTools.GetAsync();
        using var folder = TestFiles.CreateTemporaryFolder();
        var path = Path.Combine(RarDirectory, name);
        var expected = folder.GetPath("unrar");
        Directory.CreateDirectory(expected);

        // -ol-: symbolic links are not created, so that only the files are compared; -ver: the old versions of a file are
        // extracted as name;N, as the library names them. unrar fails on the truncated test data
        var unrarSucceeded = await TryRunUnrarAsync(tools, ["x", "-y", "-p-", "-ol-", "-ver", path, expected + Path.DirectorySeparatorChar]);

        var actual = folder.GetPath("library");
        if (!unrarSucceeded)
        {
            // unrar rejects the archive (truncated test data): so does the library
            var error = Record(() => RarFile.ExtractToDirectory(path, actual));
            Assert.True(error is InvalidDataException or NotSupportedException or ArchivePasswordException, $"unrar rejects {name}, the library throws {error?.GetType().Name ?? "nothing"}");
            return;
        }

        // Entry by entry, without the links: ExtractToDirectory rejects the links of the test data that point outside of the directory
        var ansiDecodedNames = new HashSet<string>(StringComparer.Ordinal);
        using (var archive = RarFile.OpenRead(path))
        {
            foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory && entry.LinkTarget is null && entry.HardLinkTarget is null))
            {
                // A RAR4 name from Unix without the Unicode name field is UTF-8, which the library decodes. unrar for Windows
                // decodes it with the ANSI code page, so it extracts the file under another name
                if (OperatingSystem.IsWindows() && archive.FormatVersion is RarFormatVersion.Rar4 && entry.HostOperatingSystem is RarHostOperatingSystem.Unix && !System.Text.Ascii.IsValid(entry.FullName))
                {
                    ansiDecodedNames.Add(entry.FullName);
                }

                var file = Path.Combine(actual, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                entry.ExtractToFile(file, overwrite: true);
            }
        }

        var expectedFiles = TestFiles.GetTree(expected).Where(item => item.Value is not "<directory>").ToDictionary(StringComparer.Ordinal);
        var actualFiles = TestFiles.GetTree(actual).Where(item => item.Value is not "<directory>").ToDictionary(StringComparer.Ordinal);

        // unrar extracts the hard links and the file copies, which are not extracted here; every file extracted here is the file of unrar
        Assert.NotEmpty(actualFiles);
        foreach (var (file, hash) in actualFiles)
        {
            if (!expectedFiles.ContainsKey(file) && ansiDecodedNames.Contains(file))
                continue;

            Assert.True(expectedFiles.TryGetValue(file, out var expectedHash), $"unrar does not extract '{file}' of {name}");
            Assert.Equal(expectedHash, hash);
        }
    }

    private static async Task<bool> TryRunUnrarAsync(CompressionTools tools, string[] arguments)
    {
        try
        {
            await tools.RunUnrarAsync(arguments);
            return true;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("exit code", StringComparison.Ordinal))
        {
            return false;
        }
    }

    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
