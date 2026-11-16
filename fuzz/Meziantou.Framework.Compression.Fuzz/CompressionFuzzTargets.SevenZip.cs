using System.Reflection;
using System.Text;
using Meziantou.Framework.Compression;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.CompressionFuzz;

public static partial class CompressionFuzzTargets
{
    private const string SevenZipTestDataPrefix = "TestData/SevenZip/";
    private const string SevenZipPassword = "password";

    // The decoders of the read target allocate at most 16 MiB
    private const long SevenZipMaxMemoryUsage = 16 * 1024 * 1024;

    // The invalid UTF-8 bytes are written as U+DC00 + byte (FuzzText)
    private static string[] SevenZipTokens =>
    [
        "7z\uDCBC\uDCAF'\u001C", "\u0000\u0004", "\u0001", "\u0002", "\u0003", "\u0004", "\u0005", "\u0006", "\u0007", "\u0008", "\u0009", "\u000A",
        "\u000B", "\u000C", "\u000D", "\u000E", "\u000F", "\u0010", "\u0011", "\u0012", "\u0013", "\u0014", "\u0015", "\u0016", "\u0017", "\u0018", "\u0019",
        "\u0003\u0001\u0001", "!", "\u0003\u0004\u0001", "\u0004\u0002\u0002", "\u0004\u0001\u0008", "\u0004\u0001\u0009", "\u0003\u0003\u0001\u001B",
        "\u0003\u0003\u0001\u0003", "\u0003\u0003\u0002\u0005", "\u0003\u0003\u0004\u0001", "\u0003\u0003\u0005\u0001", "\u0003\u0003\u0007\u0001",
        "\u0003\u0003\u0008\u0005", "\u0006\uDCF1\u0007\u0001", "\u0013", "#", "$", "D", "\uDCFF\uDCFF\uDCFF\uDCFF",
    ];

    private static FuzzTarget[] SevenZipTargets =>
    [
        new("7z-read", SevenZipReadTarget) { Seeds = GetSevenZipReadSeeds, Dictionary = SevenZipTokens },
        new("7z-roundtrip", SevenZipRoundTripTarget) { Seeds = GetSevenZipRoundTripSeeds, Dictionary = SevenZipTokens },
    ];

    /// <summary>
    /// <c>options, data</c>: a 7z archive, read with the password "password" when bit 0 of <c>options</c> is set. The reader
    /// rejects an invalid archive with an <see cref="InvalidDataException"/>, a wrong password with an
    /// <see cref="ArchivePasswordException"/>, or a method it does not support with a <see cref="NotSupportedException"/>.
    /// Reading an archive twice gives the same entries, the content of each entry has its length and its CRC-32, and the
    /// entries can be read in any order.
    /// </summary>
    private static void SevenZipReadTarget(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var bytes = data.ToArray();
        var readerOptions = new ArchiveReaderOptions
        {
            Password = (options & 1) is not 0 ? SevenZipPassword : null,
            MaxMemoryUsage = SevenZipMaxMemoryUsage,
        };

        List<(string Name, byte[]? Content)> first;
        try
        {
            first = ReadSevenZip(bytes, readerOptions, reverse: false);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArchivePasswordException or NotSupportedException)
        {
            return;
        }

        // The content is the same when the entries are read in reverse order (each folder is decoded again from its start)
        var second = ReadSevenZip(bytes, readerOptions, reverse: true);
        FuzzAssert.Equal(first.Count, second.Count, "The number of entries changes between two reads");
        for (var i = 0; i < first.Count; i++)
        {
            FuzzAssert.Equal(first[i].Name, second[i].Name, "The name of an entry changes between two reads");
            FuzzAssert.True(SameContent(first[i].Content, second[i].Content), $"The content of the entry '{first[i].Name}' changes when the entries are read in another order");
        }
    }

    private static List<(string Name, byte[]? Content)> ReadSevenZip(byte[] bytes, ArchiveReaderOptions options, bool reverse)
    {
        using var archive = new SevenZipArchive(new MemoryStream(bytes, writable: false), SevenZipArchiveMode.Read, leaveOpen: false, options, compressionOptions: null);
        var entries = archive.Entries;
        FuzzAssert.True(entries.Count <= bytes.Length * 8, "The archive has more entries than its headers can describe");
        var result = new (string Name, byte[]? Content)[entries.Count];
        var order = Enumerable.Range(0, entries.Count);
        if (reverse)
        {
            order = order.Reverse();
        }

        foreach (var index in order)
        {
            var entry = entries[index];
            FuzzAssert.True(ReferenceEquals(entry.Archive, archive), "An entry does not belong to its archive");
            _ = entry.LastWriteTime;
            _ = entry.CreationTime;
            _ = entry.LastAccessTime;
            _ = entry.Attributes;
            _ = entry.UnixFileMode;
            _ = entry.CompressedLength;
            if (entry.IsDirectory)
            {
                FuzzAssert.Equal(0L, entry.Length, "A directory has a length");
                FuzzAssert.True(entry.FullName.EndsWith('/', StringComparison.Ordinal), "The name of a directory does not end with '/'");
                result[index] = (entry.FullName, null);
                continue;
            }

            _ = entry.LinkTarget;
            using var stream = entry.Open();
            using var content = new MemoryStream();
            stream.CopyTo(content);
            FuzzAssert.Equal(entry.Length, content.Length, "The content of an entry does not have the length of the entry");
            if (entry.Crc32 is { } crc)
            {
                FuzzAssert.Equal(crc, System.IO.Hashing.Crc32.HashToUInt32(content.GetBuffer().AsSpan(0, (int)content.Length)), "The content of an entry does not have the CRC-32 of the entry");
            }

            result[index] = (entry.FullName, content.ToArray());
        }

        return [.. result];
    }

    private static bool SameContent(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);

    /// <summary>
    /// <c>method, flags, filter, level, entries</c>: entries separated by <c>FF</c>, each <c>kind, name, 00, content</c> (kind 0:
    /// a file, 1: a directory, 2: a file that is not opened). The writer writes an archive that the reader reads back with
    /// the same entries, in the same order, with the same content. Bits of <c>flags</c>: 0 solid, 1 encrypted, 2 encrypted
    /// headers, 3 uncompressed headers, 4-7 the number of files of a solid folder.
    /// </summary>
    private static void SevenZipRoundTripTarget(ReadOnlySpan<byte> data)
    {
        var method = FuzzInput.Pick(Enum.GetValues<SevenZipCompressionMethod>(), FuzzInput.ReadByte(ref data));
        var flags = FuzzInput.ReadByte(ref data);
        var filter = FuzzInput.Pick(Enum.GetValues<SevenZipFilter>(), FuzzInput.ReadByte(ref data));
        var level = FuzzInput.ReadByte(ref data) % 10;
        var encrypted = (flags & 2) is not 0;
        var options = new SevenZipCompressionOptions
        {
            Method = method,
            Filter = filter,
            Level = level,
            Solid = (flags & 1) is not 0,
            Password = encrypted ? SevenZipPassword : null,
            EncryptHeaders = encrypted && (flags & 4) is not 0,
            CompressHeaders = (flags & 8) is 0,
            SolidFileCount = flags >> 4,

            // The smallest dictionaries keep each input fast
            DictionarySize = 64 * 1024,
        };

        var entries = new List<(string Name, int Kind, byte[] Content)>();
        foreach (var part in Split(data, 0xFF))
        {
            ReadOnlySpan<byte> span = part;
            var kind = FuzzInput.ReadByte(ref span) % 3;
            var name = SanitizeName(Encoding.Latin1.GetString(FuzzInput.ReadUntil(ref span, 0)));
            entries.Add((name, kind, kind is 0 ? span.ToArray() : []));
        }

        using var stream = new MemoryStream();
        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Create, leaveOpen: true, readerOptions: null, options))
        {
            foreach (var (name, kind, content) in entries)
            {
                var entry = archive.CreateEntry(kind is 1 ? name + "/" : name);
                if (kind is 0)
                {
                    using var entryStream = entry.Open();
                    entryStream.Write(content);
                }
            }
        }

        stream.Position = 0;
        var readerOptions = new ArchiveReaderOptions { Password = options.Password };
        using var result = new SevenZipArchive(stream, SevenZipArchiveMode.Read, leaveOpen: false, readerOptions, compressionOptions: null);
        FuzzAssert.Equal(entries.Count, result.Entries.Count, "The archive does not have the entries that are written");
        for (var i = 0; i < entries.Count; i++)
        {
            var (name, kind, content) = entries[i];
            var entry = result.Entries[i];
            FuzzAssert.Equal(kind is 1 ? name + "/" : name, entry.FullName, "The name of an entry changes");
            FuzzAssert.Equal(kind is 1, entry.IsDirectory, "An entry changes from a file to a directory, or the reverse");
            if (kind is 1)
                continue;

            FuzzAssert.Equal((long)content.Length, entry.Length, "The length of an entry changes");
            using var entryStream = entry.Open();
            using var actual = new MemoryStream();
            entryStream.CopyTo(actual);
            FuzzAssert.True(actual.ToArray().AsSpan().SequenceEqual(content), $"The content of the entry '{name}' changes");
        }
    }

    private static string SanitizeName(string name)
    {
        name = name.Replace('\0', '_').Replace('\\', '/').TrimEnd('/');
        return name.Length is 0 ? "file" : name;
    }

    private static List<byte[]> Split(ReadOnlySpan<byte> data, byte separator)
    {
        var result = new List<byte[]>();
        while (!data.IsEmpty && result.Count < 64)
        {
            result.Add(FuzzInput.ReadUntil(ref data, separator).ToArray());
        }

        return result;
    }

    private static IEnumerable<FuzzSeed> GetSevenZipReadSeeds()
    {
        var assembly = typeof(CompressionFuzzTargets).Assembly;
        foreach (var resourceName in GetSevenZipResources(assembly))
        {
            using var stream = FuzzResources.Open(assembly, resourceName);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var name = resourceName[SevenZipTestDataPrefix.Length..];
            var options = (byte)(name.Contains("encrypted", StringComparison.Ordinal) ? 1 : 0);
            yield return new FuzzSeed(name, (byte[])[options, .. buffer.ToArray()]);
        }
    }

    private static IEnumerable<string> GetSevenZipResources(Assembly assembly)
    {
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(SevenZipTestDataPrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
    }

    private static IEnumerable<FuzzSeed> GetSevenZipRoundTripSeeds()
    {
        byte[] entries = [0, .. "a.txt"u8, 0, .. "Hello, 7z! Hello, 7z!"u8, 0xFF, 1, .. "dir"u8, 0, 0xFF, 2, .. "dir/empty.txt"u8, 0, 0xFF, 0, .. "app.exe"u8, 0, 0xE8, 1, 2, 3, 4, 0x90, 0xE8, 1, 2, 3, 4];
        var methods = Enum.GetValues<SevenZipCompressionMethod>();
        for (var i = 0; i < methods.Length; i++)
        {
            yield return new FuzzSeed($"{methods[i]}-solid", (byte[])[(byte)i, 0x01, 0, 5, .. entries]);
            yield return new FuzzSeed($"{methods[i]}-encrypted", (byte[])[(byte)i, 0x02 | 0x04, 0, 1, .. entries]);
        }

        yield return new FuzzSeed("filter-x86-uncompressed-headers", (byte[])[0, 0x01 | 0x08 | 0x20, 2, 5, .. entries]);
        yield return new FuzzSeed("empty", (byte[])[0, 0, 0, 5]);
    }
}
