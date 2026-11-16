using Meziantou.Framework.Compression;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.CompressionFuzz;

public static partial class CompressionFuzzTargets
{
    private const string ArchiveTestDataPrefix = "TestData/Archive/";

    // The content read from an entry is at most 1 MB
    private const int MaxArchiveEntryOutput = 1 << 20;

    // The seeds of the other formats that the detection and the generic readers start from (one or two of each). A
    // property, not a field: All, in another file of the partial class, can be initialized before the fields of this one.
    private static string[] ArchiveSeedFiles =>
    [
        "TestData/Zip/deflate.zip", "TestData/Zip/sfx.zip", "TestData/SevenZip/lzma2.7z", "TestData/Rar/libarchive-rar.rar",
        "TestData/Rar/libarchive-rar5-compressed.rar",
    ];

    // Written as FuzzText encodes bytes: a byte from 0x80 that is not part of UTF-8 is the unpaired surrogate U+DC00 + byte
    private static string[] ArchiveTokens =>
    [
        "PK\u0003\u0004", "PK\u0005\u0006", "7z\uDCBC\uDCAF'\u001C", "Rar!\u001A\u0007\u0000", "Rar!\u001A\u0007\u0001\u0000", "BZh9",
        "\uDCFD7zXZ\u0000", "(\uDCB5/\uDCFD", "LZIP", "\u001F\uDC8B", "\u001F\uDC9D", "ustar\u0000", "ustar  \u0000", "MZ",
        "././@LongLink", "PaxHeader", "path=", "linkpath=", "size=", "mtime=",
    ];

    private static FuzzTarget[] ArchiveTargets =>
    [
        new("archive-detect", ArchiveDetectTarget) { Seeds = GetArchiveDetectSeeds, Dictionary = ArchiveTokens },
        new("archive-reader", ArchiveReaderTarget) { Seeds = GetArchiveReaderSeeds, Dictionary = ArchiveTokens },
    ];

    /// <summary>
    /// <c>data</c>: any data. <see cref="ArchiveFile.DetectFormat(Stream)"/> never throws and restores the position of the
    /// stream; <see cref="ArchiveFile.Open(Stream, ArchiveReaderOptions?, bool)"/> opens the formats it reports, or rejects
    /// invalid data with an <see cref="InvalidDataException"/> (an <see cref="ArchivePasswordException"/> for a wrong password,
    /// a <see cref="NotSupportedException"/> for a method it does not support), and never throws anything else.
    /// </summary>
    private static void ArchiveDetectTarget(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        using var stream = new MemoryStream(bytes, writable: false);
        var info = ArchiveFile.DetectFormat(stream);
        FuzzAssert.Equal(0L, stream.Position, "DetectFormat does not restore the position of the stream");
        FuzzAssert.Equal(info, ArchiveFile.DetectFormat(stream), "DetectFormat returns another format the second time");

        try
        {
            using var archive = ArchiveFile.Open(stream, CreateArchiveOptions(), leaveOpen: true);
            FuzzAssert.True(info.SupportsRandomAccess, "ArchiveFile.Open opens data whose format is unknown");
            FuzzAssert.Equal(info.Format, archive.Format, "ArchiveFile.Open opens another format than the detected one");
            foreach (var entry in archive.Entries)
            {
                ExerciseEntry(entry, entry.Open);
            }
        }
        catch (Exception ex) when (IsArchiveFuzzException(ex))
        {
        }
    }

    /// <summary>
    /// <c>readSize, data</c>: an archive, read forward only by <see cref="ArchiveReader.Create(Stream, ArchiveReaderOptions?, bool)"/>
    /// from a non-seekable stream whose reads return at most <c>1 + readSize * 64</c> bytes. It rejects invalid data as
    /// <see cref="ArchiveFile.Open(Stream, ArchiveReaderOptions?, bool)"/> does. For an uncompressed tar archive that both
    /// read without error, the entries are the same, in the same order, with the same content.
    /// </summary>
    private static void ArchiveReaderTarget(ReadOnlySpan<byte> data)
    {
        var readSize = 1 + (FuzzInput.ReadByte(ref data) * 64);
        var bytes = data.ToArray();
        List<(string Name, byte[]? Content)>? forward = [];
        try
        {
            using var reader = ArchiveReader.Create(new ForwardOnlyFuzzStream(bytes, readSize), CreateArchiveOptions());
            FuzzAssert.True(reader.Format is ArchiveFormat.Zip or ArchiveFormat.Rar or ArchiveFormat.Tar, "ArchiveReader reads a format it does not support");
            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry!;
                forward.Add((entry.FullName, ExerciseEntry(entry, reader.OpenEntryStream)));
            }

            FuzzAssert.False(reader.MoveToNextEntry(), "ArchiveReader moves past the end of the archive");
        }
        catch (Exception ex) when (IsArchiveFuzzException(ex))
        {
            forward = null;
        }

        using var stream = new MemoryStream(bytes, writable: false);
        var info = ArchiveFile.DetectFormat(stream);
        if (forward is null || info is not { Format: ArchiveFormat.Tar, CompressionFormat: CompressionFormat.None })
            return;

        List<(string Name, byte[]? Content)> random = [];
        try
        {
            using var archive = ArchiveFile.Open(stream, CreateArchiveOptions(), leaveOpen: true);
            foreach (var entry in archive.Entries)
            {
                random.Add((entry.FullName, ExerciseEntry(entry, entry.Open)));
            }
        }
        catch (Exception ex) when (IsArchiveFuzzException(ex))
        {
            return;
        }

        FuzzAssert.Equal(random.Count, forward.Count, "ArchiveReader and ArchiveFile.Open read a different number of entries of a tar archive");
        for (var i = 0; i < random.Count; i++)
        {
            FuzzAssert.Equal(random[i].Name, forward[i].Name, "ArchiveReader and ArchiveFile.Open read different names");
            FuzzAssert.True(random[i].Content is null || forward[i].Content is null || random[i].Content.AsSpan().SequenceEqual(forward[i].Content), $"ArchiveReader and ArchiveFile.Open read a different content for '{random[i].Name}'");
        }
    }

    /// <summary>Reads the metadata and up to <see cref="MaxArchiveEntryOutput"/> bytes of the content of an entry; returns the content when it is read to its end without error.</summary>
    private static byte[]? ExerciseEntry(ArchiveEntry entry, Func<Stream> open)
    {
        _ = entry.Name;
        _ = entry.LastWriteTime;
        _ = entry.CreationTime;
        _ = entry.Attributes;
        _ = entry.UnixFileMode;
        _ = entry.HardLinkTarget;
        try
        {
            _ = entry.LinkTarget;
        }
        catch (Exception ex) when (IsArchiveFuzzException(ex))
        {
        }

        if (entry.IsDirectory)
            return null;

        try
        {
            using var stream = open();
            using var content = new MemoryStream();
            var buffer = new byte[8192];
            while (content.Length < MaxArchiveEntryOutput)
            {
                var read = stream.Read(buffer);
                if (read is 0)
                {
                    if (entry.Length >= 0)
                    {
                        FuzzAssert.Equal(entry.Length, content.Length, $"The content of '{entry.FullName}' does not have the length of the entry");
                    }

                    return content.ToArray();
                }

                content.Write(buffer, 0, read);
            }

            return null;
        }
        catch (Exception ex) when (IsArchiveFuzzException(ex))
        {
            return null;
        }
    }

    private static ArchiveReaderOptions CreateArchiveOptions() => new() { Password = "password", MaxMemoryUsage = 1 << 20 };

    private static bool IsArchiveFuzzException(Exception ex) => ex is InvalidDataException or ArchivePasswordException or NotSupportedException;

    private static IEnumerable<FuzzSeed> GetArchiveDetectSeeds()
    {
        foreach (var (name, data) in GetArchiveTestFiles())
        {
            yield return new FuzzSeed(name, data);
        }
    }

    private static IEnumerable<FuzzSeed> GetArchiveReaderSeeds()
    {
        foreach (var (name, data) in GetArchiveTestFiles())
        {
            yield return new FuzzSeed(name, (byte[])[0, .. data]);
            yield return new FuzzSeed(name + "-large-reads", (byte[])[255, .. data]);
        }
    }

    private static IEnumerable<(string Name, byte[] Data)> GetArchiveTestFiles()
    {
        var assembly = typeof(CompressionFuzzTargets).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ArchiveTestDataPrefix, StringComparison.Ordinal) || ArchiveSeedFiles.Contains(name, StringComparer.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, resourceName);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return ("archive-" + resourceName["TestData/".Length..].Replace('/', '-'), memory.ToArray());
        }
    }

    /// <summary>A non-seekable stream over bytes, whose reads return at most a given number of bytes.</summary>
    private sealed class ForwardOnlyFuzzStream(byte[] data, int maxRead) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(Math.Min(buffer.Length, maxRead), data.Length - _position);
            data.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
