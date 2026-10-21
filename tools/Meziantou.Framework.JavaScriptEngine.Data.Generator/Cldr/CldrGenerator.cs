#pragma warning disable MA0004 // Use Task.ConfigureAwait
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Data.Generator.Cldr;

/// <summary>Generates src/Meziantou.Framework.JavaScriptEngine/Intl/Data/{locales,supplemental,collation}.bin and CldrData.g.cs.</summary>
internal static class CldrGenerator
{
    public static string GetCldrJsonUrl(string version) => $"https://github.com/unicode-org/cldr-json/releases/download/{version}/cldr-{version}-json-full.zip";

    public static string GetCldrCommonUrl(string version) => $"https://unicode.org/Public/cldr/{version}/cldr-common-{version}.zip";

    public static string GetPropListUrl(string version) => $"https://www.unicode.org/Public/{version}/ucd/PropList.txt";

    /// <summary>Gets the version of the cldr-common archive of a release tag of unicode-org/cldr ("release-48-2" -> "48.2", "release-49" -> "49").</summary>
    public static string GetCldrCommonVersion(string tag)
    {
        if (!tag.StartsWith("release-", StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected CLDR release tag: " + tag);

        return tag["release-".Length..].Replace('-', '.');
    }

    /// <summary>Gets the version of UCA (and of the UCD it is built on) of a cldr-common archive.</summary>
    public static string GetUcaVersion(byte[] commonArchive)
    {
        using var zip = new ZipArchive(new MemoryStream(commonArchive), ZipArchiveMode.Read);
        return CollationGenerator.GetUcaVersion(CollationGenerator.ReadEntry(zip, "common/uca/allkeys_CLDR.txt"));
    }

    public static async Task<int> RunAsync(FullPath root, JsonObject pins, FullPath downloadCache)
    {
        var jsonVersion = (string)pins["cldr-json"]!["version"]!;
        var commonVersion = (string)pins["cldr-common"]!["version"]!;
        var ucdVersion = (string)pins["ucd"]!["version"]!;
        Console.WriteLine($"cldr-json {jsonVersion}, cldr-common {commonVersion}, UCD {ucdVersion}");

        var stopwatch = Stopwatch.StartNew();
        var jsonArchive = await Downloads.DownloadCachedAsync(GetCldrJsonUrl(jsonVersion), (string)pins["cldr-json"]!["sha256"]!, downloadCache);
        var commonArchive = await Downloads.DownloadCachedAsync(GetCldrCommonUrl(commonVersion), (string)pins["cldr-common"]!["sha256"]!, downloadCache);
        var propList = Encoding.UTF8.GetString(await Downloads.DownloadCachedAsync(GetPropListUrl(ucdVersion), (string)pins["ucd"]!["sha256"]!, downloadCache));

        var source = new CldrJsonSource(jsonArchive);
        var supplementalChunks = SupplementalGenerator.Build(source);
        var supplemental = new CldrSupplemental(CldrChunkedFile.FromBytes(ChunkedFileWriter.Write(supplementalChunks)));
        Console.WriteLine($"Supplemental data: {stopwatch.Elapsed}");

        var locales = new LocaleSet(source, supplemental.LikelySubtags);
        Console.WriteLine($"{locales.Locales.Count} locales, {locales.Aliases.Count} aliases");
        TreeTable rootNumbers;
        using (var common = new ZipArchive(new MemoryStream(commonArchive), ZipArchiveMode.Read))
        {
            rootNumbers = LocaleDataGenerator.ReadRootNumbers(common);
        }

        var localeGenerator = new LocaleDataGenerator(source, locales, rootNumbers);
        var localeChunks = localeGenerator.Build();
        var localesFile = ChunkedFileWriter.Write(localeChunks);
        Console.WriteLine($"Locale data: {stopwatch.Elapsed}, {localeGenerator.MissingInChildCount} values of a parent missing in the resolved data of a child");

        var collationParents = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var parents = source.Read("cldr-core/supplemental/parentLocales.json"))
        {
            foreach (var property in parents.RootElement.GetProperty("supplemental").GetProperty("parentLocales").GetProperty("collations").EnumerateObject())
            {
                collationParents.Add(property.Name, property.Value.GetString()!);
            }
        }

        List<byte[]> collationChunks;
        string ucaVersion;
        using (var common = new ZipArchive(new MemoryStream(commonArchive), ZipArchiveMode.Read))
        {
            collationChunks = CollationGenerator.Build(common, propList, locales, collationParents, out ucaVersion);
        }

        if (!string.Equals(ucaVersion, ucdVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"The UCA version of allkeys_CLDR.txt ({ucaVersion}) differs from the pinned UCD ({ucdVersion}): run with --update");

        var collationFile = ChunkedFileWriter.Write(collationChunks);
        Console.WriteLine($"Collation data: {stopwatch.Elapsed}");

        // The runtime must resolve the same values as cldr-json
        var data = new CldrData(CldrChunkedFile.FromBytes(localesFile), CldrChunkedFile.FromBytes(ChunkedFileWriter.Write(supplementalChunks)), CldrChunkedFile.FromBytes(collationFile));
        var errors = localeGenerator.Verify(data.Locales);
        Console.WriteLine($"Verification: {errors} errors, {stopwatch.Elapsed}");
        if (errors > 0)
            return 3;

        _ = data.Collation.Root;

        var outputDirectory = root / "src" / "Meziantou.Framework.JavaScriptEngine" / "Intl" / "Data";
        var updated = false;
        updated |= await WriteIfChangedAsync(outputDirectory / "locales.bin", localeChunks, localesFile);
        updated |= await WriteIfChangedAsync(outputDirectory / "supplemental.bin", supplementalChunks, ChunkedFileWriter.Write(supplementalChunks));
        updated |= await WriteIfChangedAsync(outputDirectory / "collation.bin", collationChunks, collationFile);
        Console.WriteLine($"locales.bin: {localesFile.Length} bytes ({localeChunks.Count} chunks, {localeChunks.Sum(chunk => (long)chunk.Length)} bytes uncompressed)");
        var compressedLocales = CldrChunkedFile.FromBytes(localesFile);
        foreach (var group in Enumerable.Range(0, localeChunks.Count).GroupBy(index => localeGenerator.ChunkCategories[index]))
        {
            Console.WriteLine($"  {group.Key?.ToString() ?? "Index"}: {group.Sum(compressedLocales.GetCompressedChunkLength)} bytes ({group.Sum(index => (long)localeChunks[index].Length)} bytes uncompressed)");
        }

        Console.WriteLine($"supplemental.bin: {ChunkedFileWriter.Write(supplementalChunks).Length} bytes ({supplementalChunks.Sum(chunk => (long)chunk.Length)} bytes uncompressed)");
        Console.WriteLine($"collation.bin: {collationFile.Length} bytes ({collationChunks.Sum(chunk => (long)chunk.Length)} bytes uncompressed)");

        var versionFile = $$"""
            // <auto-generated/>
            // Generated by tools/Meziantou.Framework.JavaScriptEngine.Data.Generator from data-versions.json
            namespace Meziantou.Framework.JavaScriptEngine.Intl.Data;

            internal sealed partial class CldrData
            {
                /// <summary>The release of cldr-json of the locale and supplemental data.</summary>
                public const string EmbeddedCldrJsonVersion = "{{jsonVersion}}";

                /// <summary>The release of CLDR (cldr-common) of the collation data.</summary>
                public const string EmbeddedCldrVersion = "{{commonVersion}}";

                /// <summary>The version of UCA of the root collation, and of the UCD of its implicit weights.</summary>
                public const string EmbeddedUcaVersion = "{{ucaVersion}}";
            }

            """;
        var versionFilePath = outputDirectory / "CldrData.g.cs";
        if (!File.Exists(versionFilePath) || (await File.ReadAllTextAsync(versionFilePath)).ReplaceLineEndings("\n") != versionFile)
        {
            await File.WriteAllTextAsync(versionFilePath, versionFile);
            updated = true;
        }

        return updated ? 1 : 0;
    }

    private static async Task<bool> WriteIfChangedAsync(FullPath path, IReadOnlyList<byte[]> chunks, byte[] content)
    {
        if (ChunkedFileWriter.IsSameContent(path, chunks))
            return false;

        Directory.CreateDirectory(path.Parent);
        await File.WriteAllBytesAsync(path, content);
        Console.WriteLine($"Updated {path} ({content.Length} bytes)");
        return true;
    }
}
