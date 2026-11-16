using System.Buffers.Binary;
using Meziantou.Framework.Compression;
using Meziantou.Framework.Compression.Internals.Zip;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.CompressionFuzz;

public static partial class CompressionFuzzTargets
{
    private const string ZipTestDataPrefix = "TestData/Zip/";
    private const string ZipLegacyTestDataPrefix = "TestData/ZipLegacy/";

    // The content read from an entry is at most 1 MB: deflate expands its input up to 1032 times
    private const int MaxZipEntryOutput = 1 << 20;

    private static string[] ZipTokens =>
    [
        "PK\u0003\u0004", "PK\u0001\u0002", "PK\u0005\u0006", "PK\u0006\u0006", "PK\u0006\u0007", "PK\u0007\u0008", "PK00",
        "\u0001\u0000", "\n\u0000", "UT", "ux", "up", "uc", "AE", "\u0001\u0000AE", "\u0002\u0000AE",
        "\u0000\u0000", "\b\u0000", "\t\u0000", "\f\u0000", "\u000E\u0000", "]\u0000", "_\u0000", "b\u0000", "c\u0000",
        "\u0001\u0000\u0018\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000\u0000",
    ];

    private static FuzzTarget[] ZipTargets =>
    [
        new("zip-read", ZipReadTarget) { Seeds = GetZipReadSeeds, Dictionary = ZipTokens },
        new("zip-reader", ZipReaderTarget) { Seeds = GetZipReaderSeeds, Dictionary = ZipTokens },
    ];

    /// <summary>
    /// <c>data</c>: a zip archive, read by <see cref="ZipArchive"/> with the password <c>password</c>. It rejects
    /// invalid data with an <see cref="InvalidDataException"/> (an <see cref="ArchivePasswordException"/> for a wrong
    /// password, a <see cref="NotSupportedException"/> for a method it does not support), and never throws anything else.
    /// The content of an entry that reads to its end has the length of the entry.
    /// </summary>
    private static void ZipReadTarget(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes, writable: false), System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false, CreateZipOptions(), compressionOptions: null);
            FuzzAssert.True(archive.Entries.Count <= bytes.Length / 46, "The archive has more entries than its central directory can hold");
            foreach (var entry in archive.Entries)
            {
                _ = entry.Name;
                _ = entry.IsDirectory;
                _ = entry.LastWriteTime;
                _ = entry.Attributes;
                _ = entry.UnixFileMode;
                FuzzAssert.True(entry.CompressedLength >= 0 && entry.Length >= 0, "An entry of the central directory has a negative length");
                FuzzAssert.True(!entry.FullName.Contains('\\', StringComparison.Ordinal) || entry.Attributes is null, "A name of an MS-DOS entry has a backslash");
                var (content, error, complete) = ReadZipEntry(entry.Open);
                if (error is null && complete)
                {
                    FuzzAssert.Equal(entry.Length, (long)content!.Length, "The content of an entry does not have the length of the entry");
                }

                try
                {
                    _ = entry.LinkTarget;
                }
                catch (Exception ex) when (IsZipFuzzException(ex))
                {
                }
            }
        }
        catch (Exception ex) when (IsZipFuzzException(ex))
        {
        }
    }

    /// <summary>
    /// <c>options, data</c>: a zip archive, read by the forward-only reader from a stream that is not seekable and whose reads
    /// return at most <c>1 + options * 16</c> bytes. It throws the exceptions of <c>zip-read</c> only. When the archive is
    /// made of the local headers and the contents of the entries of its central directory, in order and without data
    /// descriptors, and the local headers have the names, the methods, the encryption, the CRC and the sizes of the central
    /// directory (so that both readers decode the contents alike), followed by the central directory, and
    /// <see cref="ZipArchive"/> reads it, the forward-only reader returns the same entries with the same contents,
    /// or the same exception for each content.
    /// </summary>
    private static void ZipReaderTarget(ReadOnlySpan<byte> data)
    {
        var maxRead = 1 + (FuzzInput.ReadByte(ref data) * 16);
        var bytes = data.ToArray();
        var forward = new List<(string Name, byte[]? Content, Type? Error, bool Complete)>();
        Type? forwardError = null;
        try
        {
            using var reader = ZipArchive.CreateForwardReader(new ZipForwardOnlyStream(bytes, maxRead), CreateZipOptions(), leaveOpen: false);
            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry!;
                _ = entry.Length;
                _ = entry.LastWriteTime;
                var (content, error, complete) = ReadZipEntry(reader.OpenEntryStream);
                forward.Add((entry.FullName, content, error, complete));
            }
        }
        catch (Exception ex) when (IsZipFuzzException(ex))
        {
            forwardError = ex.GetType();
        }

        if (!TryGetSimpleZipLayout(bytes, out var expected))
            return;

        FuzzAssert.Equal(null, forwardError, "The forward-only reader fails on an archive that the random-access reader reads");
        FuzzAssert.Equal(expected.Count, forward.Count, "The forward-only reader does not return the entries of the central directory");
        for (var i = 0; i < expected.Count; i++)
        {
            var (name, content, error, complete) = expected[i];
            FuzzAssert.Equal(name.Replace('\\', '/'), forward[i].Name, "The forward-only reader returns another name than the central directory");
            if (!complete || !forward[i].Complete)
                continue;

            FuzzAssert.Equal(error, forward[i].Error, "The readers do not fail alike on the content of an entry");
            if (error is null)
            {
                FuzzAssert.True(content.AsSpan().SequenceEqual(forward[i].Content), "The readers do not return the same content");
            }
        }
    }

    /// <summary>
    /// Reads the archive with <see cref="ZipArchive"/>, and returns its entries when the archive starts with the local
    /// header of its first entry, each entry is followed by the next one, the last one by the central directory, no entry has
    /// a data descriptor, and each local header has the fields of the central directory that the content depends on.
    /// </summary>
    private static bool TryGetSimpleZipLayout(byte[] bytes, out List<(string Name, byte[]? Content, Type? Error, bool Complete)> entries)
    {
        entries = [];
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes, writable: false), System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false, CreateZipOptions(), compressionOptions: null);
            var position = 0L;
            foreach (var entry in archive.Entries)
            {
                var info = entry.Info;
                if (info.LocalHeaderOffset != position || info.HasDataDescriptor || !HasSameZipLocalHeader(bytes, entry))
                    return false;

                var header = bytes.AsSpan((int)position);
                position += 30 + BinaryPrimitives.ReadUInt16LittleEndian(header[26..]) + BinaryPrimitives.ReadUInt16LittleEndian(header[28..]) + info.CompressedSize;
                var (content, error, complete) = ReadZipEntry(entry.Open);
                entries.Add((entry.FullName, content, error, complete));
            }

            // The forward-only reader stops at the central directory
            return position + 4 <= bytes.Length && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)position)) is 0x02014B50 or 0x06054B50;
        }
        catch (Exception ex) when (IsZipFuzzException(ex))
        {
            return false;
        }
    }

    private static bool HasSameZipLocalHeader(byte[] bytes, ZipArchiveEntry entry)
    {
        var info = entry.Info;
        var local = bytes.AsSpan((int)info.LocalHeaderOffset);
        if (local.Length < 30 || BinaryPrimitives.ReadUInt32LittleEndian(local) is not 0x04034B50)
            return false;

        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(local[26..]);
        var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(local[28..]);
        if (local.Length < 30 + nameLength + extraLength)
            return false;

        // The fields that the forward-only reader reads from the local header to decode the content
        ZipEntryInfo localInfo;
        try
        {
            localInfo = ZipEntryInfo.ParseLocal(local[4..30], local.Slice(30, nameLength), local.Slice(30 + nameLength, extraLength), CreateZipOptions());
        }
        catch (InvalidDataException)
        {
            return false;
        }

        return localInfo.Flags == info.Flags
            && localInfo.RawMethod == info.RawMethod
            && localInfo.Method == info.Method
            && localInfo.Encryption == info.Encryption
            && localInfo.HasAesExtra == info.HasAesExtra
            && localInfo.AesVendorVersion == info.AesVendorVersion
            && localInfo.Crc == info.Crc
            && localInfo.DosTime == info.DosTime
            && localInfo.CompressedSize == info.CompressedSize
            && localInfo.UncompressedSize == info.UncompressedSize
            && string.Equals(localInfo.FullName, info.FullName.Replace('\\', '/'), StringComparison.Ordinal);
    }

    private static ArchiveReaderOptions CreateZipOptions() => new() { Password = "password", MaxMemoryUsage = 1 << 20 };

    private static bool IsZipFuzzException(Exception ex) => ex is InvalidDataException or ArchivePasswordException or NotSupportedException;

    /// <summary>Reads the content of an entry, up to <see cref="MaxZipEntryOutput"/> bytes.</summary>
    private static (byte[]? Content, Type? Error, bool Complete) ReadZipEntry(Func<Stream> open)
    {
        try
        {
            using var stream = open();
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > MaxZipEntryOutput)
                    return (null, null, false);
            }

            return (output.ToArray(), null, true);
        }
        catch (Exception ex) when (IsZipFuzzException(ex))
        {
            return (null, ex.GetType(), true);
        }
    }

    private static IEnumerable<FuzzSeed> GetZipReadSeeds()
    {
        foreach (var (name, data) in GetZipTestFiles())
        {
            yield return new FuzzSeed(name, data);
        }
    }

    private static IEnumerable<FuzzSeed> GetZipReaderSeeds()
    {
        foreach (var (name, data) in GetZipTestFiles())
        {
            yield return new FuzzSeed(name, (byte[])[0, .. data]);
            yield return new FuzzSeed(name + "-large-reads", (byte[])[255, .. data]);
        }
    }

    private static IEnumerable<(string Name, byte[] Data)> GetZipTestFiles()
    {
        var assembly = typeof(CompressionFuzzTargets).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ZipTestDataPrefix, StringComparison.Ordinal) || name.StartsWith(ZipLegacyTestDataPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, resourceName);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return ("zip-" + resourceName["TestData/".Length..].Replace('/', '-'), memory.ToArray());
        }
    }

    /// <summary>A stream that is not seekable, whose reads return at most a number of bytes.</summary>
    private sealed class ZipForwardOnlyStream(byte[] data, int maxRead) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, maxRead));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
