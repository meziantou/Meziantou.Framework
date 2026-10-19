#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using System.IO.Compression;
using System.Security.Cryptography;
using Meziantou.Framework;

/// <summary>Vendors the conformance test files of the pinned Unicode version into the test project.</summary>
/// <remarks>
/// The files are stored compressed (they total more than 20 MB) and unmodified: the hash recorded in the notice
/// file is the one of the decompressed content.
/// </remarks>
internal static class ConformanceFilesWriter
{
    private static readonly string[] Files =
    [
        "ucd/auxiliary/GraphemeBreakTest.txt",
        "ucd/auxiliary/WordBreakTest.txt",
        "ucd/auxiliary/LineBreakTest.txt",
        "ucd/BidiTest.txt",
        "ucd/BidiCharacterTest.txt",
        "ucd/NormalizationTest.txt",
        "idna/IdnaTestV2.txt",
    ];

    public static async Task<bool> WriteAsync(FullPath root)
    {
        Console.WriteLine("Loading Unicode conformance files...");
        var directory = root / "tests" / "Meziantou.Framework.Unicode.Tests" / "files";
        var updated = false;
        var rows = new StringBuilder();
        foreach (var file in Files)
        {
            var content = await Ucd.DownloadBytesAsync(file);
            var name = Path.GetFileName(file);
            var path = directory / (name + ".gz");
            var existing = File.Exists(path) ? Decompress(await File.ReadAllBytesAsync(path)) : null;
            if (existing is null || !content.AsSpan().SequenceEqual(existing))
            {
                path.CreateParentDirectory();
                await File.WriteAllBytesAsync(path, Compress(content));
                updated = true;
            }

            rows.Append(CultureInfo.InvariantCulture, $"| `{name}.gz` | <{Ucd.GetUrl(file)}> | {content.Length:N0} | `{Convert.ToHexStringLower(SHA256.HashData(content))}` |\n");
        }

        using var licenseResponse = await SharedHttpClient.Instance.GetAsync("https://www.unicode.org/license.txt");
        licenseResponse.EnsureSuccessStatusCode();
        var license = (await licenseResponse.Content.ReadAsStringAsync()).ReplaceLineEndings("\n").Trim();

        var notice = string.Create(CultureInfo.InvariantCulture, $$"""
            # Unicode conformance test files

            The `*.txt.gz` files of this directory are the conformance test files of the Unicode Character Database
            and of Unicode IDNA Compatibility Processing {{Ucd.Version}}, the version the tables of
            `Meziantou.Framework.Unicode` are generated from. They are compressed with gzip and otherwise unmodified.

            | File | Source | Size (bytes) | SHA-256 of the decompressed content |
            | --- | --- | --- | --- |
            {{rows.ToString().TrimEnd()}}

            Do not edit these files or this notice by hand: they are written by
            `tools/Meziantou.Framework.Unicode.Generator`, which replaces them when the pinned version changes
            (`dotnet run --project tools/Meziantou.Framework.Unicode.Generator`).

            ## License

            The files are distributed under the Unicode License v3 (<https://www.unicode.org/license.txt>):

            ```text
            {{license.Replace("\n", "\n            ", StringComparison.Ordinal)}}
            ```

            """).ReplaceLineEndings("\n");

        // The interpolated license text carries the indentation of the template on its continuation lines.
        notice = string.Join('\n', notice.Split('\n').Select(line => line.StartsWith("            ", StringComparison.Ordinal) ? line[12..] : line).Select(line => line.TrimEnd()));
        updated |= await GeneratorFiles.WriteTextIfChanged(directory / "UnicodeConformance.LICENSE.md", notice);

        // The notice of the data the package itself is generated from.
        var packageNotice = string.Create(CultureInfo.InvariantCulture, $$"""
            # Third-Party Notices

            This project includes data derived from third-party sources. The original copyright
            notices and the licenses under which the respective authors made such data
            available are reproduced below.

            ---

            ## Unicode Character Database

            - Project: Unicode Character Database {{Ucd.Version}}
            - Source: <{{Ucd.GetUrl("")}}>
            - License: <https://www.unicode.org/license.txt>

            The tables of this package (`*.g.cs`) are generated from the data files of
            the Unicode Character Database, of the Unicode security mechanisms and of Unicode IDNA Compatibility
            Processing by `tools/Meziantou.Framework.Unicode.Generator`. The algorithms are written from the Unicode
            Standard Annexes #9, #14, #15, #24 and #29 and the Unicode Technical Standards #39 and #46.

            ```
            {{license.Replace("\n", "\n            ", StringComparison.Ordinal)}}
            ```

            """).ReplaceLineEndings("\n");
        packageNotice = string.Join('\n', packageNotice.Split('\n').Select(line => line.StartsWith("            ", StringComparison.Ordinal) ? line[12..] : line).Select(line => line.TrimEnd()));
        updated |= await GeneratorFiles.WriteTextIfChanged(root / "src" / "Meziantou.Framework.Unicode" / "THIRD-PARTY-NOTICES.md", packageNotice);
        return updated;
    }

    private static byte[] Compress(byte[] content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(content);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(byte[] content)
    {
        using var input = new MemoryStream(content);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
