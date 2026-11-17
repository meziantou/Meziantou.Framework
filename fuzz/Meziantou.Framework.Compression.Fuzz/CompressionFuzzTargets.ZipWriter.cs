using System.IO.Compression;
using System.Text;
using Meziantou.Framework.Compression;
using Meziantou.Framework.Compression.Internals.Ppmd;
using Meziantou.Framework.Fuzzing;
using ZipArchive = Meziantou.Framework.Compression.ZipArchive;
using ZipCompressionMethod = Meziantou.Framework.Compression.ZipCompressionMethod;

namespace Meziantou.Framework.CompressionFuzz;

/// <summary>
/// The zip writer (<see cref="ZipArchive"/> in Create and Update modes) and the PPMd var.I encoder: the archives it writes
/// are read back unchanged by both zip readers, and the PPMd data decode to the encoded bytes.
/// </summary>
public static partial class CompressionFuzzTargets
{
    private const string ZipWriterPassword = "pässword";
    private const byte ZipWriterEntrySeparator = 0xFF;

    private static ZipCompressionMethod[] ZipWriterMethods =>
    [
        ZipCompressionMethod.Stored,
        ZipCompressionMethod.Deflate,
        ZipCompressionMethod.BZip2,
        ZipCompressionMethod.Lzma,
        ZipCompressionMethod.Ppmd,
        ZipCompressionMethod.Xz,
#if NET11_0_OR_GREATER
        ZipCompressionMethod.Zstandard,
#endif
    ];

    private static ZipEncryption[] ZipWriterEncryptions => [ZipEncryption.None, ZipEncryption.ZipCrypto, ZipEncryption.Aes128, ZipEncryption.Aes192, ZipEncryption.Aes256];

    private static string[] ZipWriterTokens => ["ÿ", "\u0000", "\u0001", "\u0002", "/", "dir/", "é", "日本", "hello"];

    private static FuzzTarget[] ZipWriterTargets =>
    [
        new("zip-roundtrip", ZipRoundTripTarget) { Seeds = GetZipRoundTripSeeds, Dictionary = ZipWriterTokens },
        new("ppmd8-roundtrip", Ppmd8RoundTripTarget) { Seeds = GetPpmd8RoundTripSeeds, Dictionary = PpmdTokens },
    ];

    /// <summary>
    /// <c>method, encryption, options, entries</c>: the compression method and the encryption (indexes), the options (bit 0:
    /// a seekable output, bit 1: <see cref="Zip64Mode.Always"/>, bit 2: an update that deletes the first entry, bits 3-4: the
    /// compression level), then the entries separated by 0xFF: a kind (0 a file, 1 a directory, 2 a symbolic link, 3 a file
    /// with a Unix mode and a comment), the name, 0, and the content. The archive is read back by <see cref="ZipArchive"/> and
    /// by the forward-only reader, which return the names, the contents, the methods and the metadata written.
    /// </summary>
    private static void ZipRoundTripTarget(ReadOnlySpan<byte> data)
    {
        var method = FuzzInput.Pick(ZipWriterMethods, FuzzInput.ReadByte(ref data));
        var encryption = FuzzInput.Pick(ZipWriterEncryptions, FuzzInput.ReadByte(ref data));
        var flags = FuzzInput.ReadByte(ref data);
        var seekable = (flags & 1) != 0;
        var level = ((flags >> 3) & 3) switch
        {
            0 => CompressionLevel.Fastest,
            1 => CompressionLevel.NoCompression,
            2 => CompressionLevel.Optimal,
            _ => CompressionLevel.SmallestSize,
        };

        // SmallestSize is slow and allocates large models: the fuzzer uses it for the fast methods only
        if (level is CompressionLevel.SmallestSize && method is ZipCompressionMethod.Lzma or ZipCompressionMethod.Xz or ZipCompressionMethod.Ppmd)
        {
            level = CompressionLevel.Fastest;
        }

        var options = new ZipCompressionOptions
        {
            CompressionMethod = method,
            CompressionLevel = level,
            Encryption = encryption,
            Password = encryption is ZipEncryption.None ? null : ZipWriterPassword,
            Zip64 = (flags & 2) != 0 ? Zip64Mode.Always : Zip64Mode.Auto,
        };

        var entries = new List<ZipFuzzEntry>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (!data.IsEmpty && entries.Count < 16)
        {
            var part = FuzzInput.ReadUntil(ref data, ZipWriterEntrySeparator);
            var kind = FuzzInput.ReadByte(ref part) % 4;
            var name = GetZipFuzzName(FuzzInput.ReadUntil(ref part, 0), entries.Count, isDirectory: kind is 1);
            if (!names.Add(name))
                continue;

            entries.Add(new ZipFuzzEntry(name, kind, kind is 1 ? [] : part.ToArray()));
        }

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(seekable ? output : new ZipFuzzNonSeekableStream(output), ZipArchiveMode.Create, leaveOpen: true, readerOptions: null, options))
        {
            archive.Comment = "fuzz é";
            foreach (var entry in entries)
            {
                switch (entry.Kind)
                {
                    case 1:
                        archive.CreateEntry(entry.Name);
                        break;

                    case 2:
                        archive.CreateSymbolicLinkEntry(entry.Name, entry.LinkTarget);
                        break;

                    default:
                        var entryOptions = entry.Kind is 3 ? new ZipEntryOptions { UnixFileMode = (UnixFileMode)0x1A4, Comment = entry.Name } : null;
                        using (var stream = archive.CreateEntry(entry.Name, entryOptions).Open())
                        {
                            // Writes of several sizes
                            var content = entry.Content.AsSpan();
                            var size = 1;
                            while (!content.IsEmpty)
                            {
                                var length = Math.Min(size, content.Length);
                                stream.Write(content[..length]);
                                content = content[length..];
                                size = (size * 5) + 1;
                            }
                        }

                        break;
                }
            }
        }

        var bytes = output.ToArray();
        var readerOptions = new ArchiveReaderOptions { Password = ZipWriterPassword };
        AssertZipRoundTrip(bytes, entries, method, encryption, readerOptions);

        if ((flags & 4) != 0 && entries.Count > 0)
        {
            // An update copies the other entries without decoding them
            var updated = new MemoryStream();
            updated.Write(bytes);
            using (var archive = new ZipArchive(updated, ZipArchiveMode.Update, leaveOpen: true))
            {
                archive.Entries[0].Delete();
            }

            AssertZipRoundTrip(updated.ToArray(), entries.GetRange(1, entries.Count - 1), method, encryption, readerOptions);
        }
    }

    private static void AssertZipRoundTrip(byte[] bytes, List<ZipFuzzEntry> entries, ZipCompressionMethod method, ZipEncryption encryption, ArchiveReaderOptions readerOptions)
    {
        using (var archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read, leaveOpen: false, readerOptions, compressionOptions: null))
        {
            FuzzAssert.Equal("fuzz é", archive.Comment, "The comment of the archive does not round trip");
            FuzzAssert.Equal(entries.Count, archive.Entries.Count, "The archive does not have the entries written");
            for (var i = 0; i < entries.Count; i++)
            {
                var expected = entries[i];
                var entry = archive.Entries[i];
                FuzzAssert.Equal(expected.Name, entry.FullName, "The name of an entry does not round trip");
                FuzzAssert.Equal(expected.Kind is 1, entry.IsDirectory, "An entry is not a directory as written");
                if (expected.Kind is 1)
                    continue;

                if (expected.Kind is 2)
                {
                    FuzzAssert.Equal(expected.LinkTarget, entry.LinkTarget, "The target of a link does not round trip");
                    continue;
                }

                var expectedMethod = expected.Content.Length is 0 ? ZipCompressionMethod.Stored : method;
                FuzzAssert.Equal(expectedMethod, entry.CompressionMethod, "The method of an entry does not round trip");
                FuzzAssert.Equal(encryption, entry.EncryptionMethod, "The encryption of an entry does not round trip");
                FuzzAssert.Equal(expected.Content.LongLength, entry.Length, "The length of an entry does not round trip");
                using var stream = entry.Open();
                FuzzAssert.True(ReadAllZipFuzz(stream).AsSpan().SequenceEqual(expected.Content), "The content of an entry does not round trip");
                if (expected.Kind is 3)
                {
                    FuzzAssert.Equal((UnixFileMode?)(UnixFileMode)0x1A4, entry.UnixFileMode, "The Unix mode of an entry does not round trip");
                    FuzzAssert.Equal(expected.Name, entry.Comment, "The comment of an entry does not round trip");
                }
            }
        }

        // The forward-only reader, from a stream that is not seekable
        using var reader = ArchiveReader.Create(new ZipForwardOnlyStream(bytes, 7), readerOptions);
        var index = 0;
        while (reader.MoveToNextEntry())
        {
            FuzzAssert.True(index < entries.Count, "The forward-only reader returns more entries than written");
            var expected = entries[index++];
            var entry = reader.Entry!;
            FuzzAssert.Equal(expected.Name, entry.FullName, "The forward-only reader returns another name");
            if (expected.Kind is 1)
                continue;

            using var stream = reader.OpenEntryStream();
            var content = ReadAllZipFuzz(stream);
            var expectedContent = expected.Kind is 2 ? Encoding.UTF8.GetBytes(expected.LinkTarget) : expected.Content;
            FuzzAssert.True(content.AsSpan().SequenceEqual(expectedContent), "The forward-only reader returns another content");
        }

        FuzzAssert.Equal(entries.Count, index, "The forward-only reader does not return every entry");
    }

    /// <summary>
    /// <c>order, memory, restore, data</c>: the order (2 to 16), the memory size (64 KiB to 2 MiB), the restore method, then the
    /// data to encode with the PPMd var.I encoder of zip method 98, which decode to themselves with a known length and up to
    /// the end marker.
    /// </summary>
    private static void Ppmd8RoundTripTarget(ReadOnlySpan<byte> data)
    {
        var order = 2 + (FuzzInput.ReadByte(ref data) % 15);
        var memorySize = (1u << 16) << (FuzzInput.ReadByte(ref data) % 6);
        var restoreMethod = FuzzInput.ReadByte(ref data) % 2;
        var input = data.ToArray();
        var encoded = EncodePpmd8(input, order, memorySize, restoreMethod);
        using (var decoder = new Ppmd8DecoderStream(new MemoryStream(encoded), order, memorySize, restoreMethod, input.Length, PpmdMaxMemoryUsage, leaveOpen: false))
        {
            FuzzAssert.True(ReadPpmdOutput(decoder).AsSpan().SequenceEqual(input), "The data does not round trip");
        }

        using (var decoder = new Ppmd8DecoderStream(new MemoryStream(encoded), order, memorySize, restoreMethod, outputLength: -1, PpmdMaxMemoryUsage, leaveOpen: false))
        {
            FuzzAssert.True(ReadPpmdOutput(decoder).AsSpan().SequenceEqual(input), "The data does not round trip up to the end marker");
        }

        FuzzAssert.True(EncodePpmd8(input, order, memorySize, restoreMethod).AsSpan().SequenceEqual(encoded), "The encoding is not deterministic");
    }

    private static byte[] EncodePpmd8(byte[] data, int order, uint memorySize, int restoreMethod)
    {
        using var output = new MemoryStream();
        using (var encoder = new Ppmd8EncoderStream(output, order, memorySize, restoreMethod, leaveOpen: true))
        {
            encoder.Write(data);
        }

        return output.ToArray();
    }

    private static string GetZipFuzzName(ReadOnlySpan<byte> bytes, int index, bool isDirectory)
    {
        // A name that round trips through UTF-8, without the characters the readers change (backslashes) or reject
        var text = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(FuzzInput.ReadText(bytes)));
        var name = new StringBuilder(text.Length + 4);
        foreach (var c in text)
        {
            name.Append(c is '\\' or '\0' or '/' ? '_' : c);
        }

        if (name.Length is 0 or > 64)
        {
            name.Clear().Append("entry").Append(index);
        }

        return isDirectory ? name.Append('/').ToString() : name.ToString();
    }

    private static byte[] ReadAllZipFuzz(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static IEnumerable<FuzzSeed> GetZipRoundTripSeeds()
    {
        // The cases of the unit tests: every method and encryption, on seekable and non-seekable outputs, with the files of
        // ZipArchiveWriteTests (an empty file, text, a small file, a directory and a nested file) and a link
        var text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 40).Select(i => $"Line {i}: the quick brown fox jumps over the lazy dog\n")));
        byte[] entries =
        [
            0, .. "empty.txt"u8, 0, ZipWriterEntrySeparator,
            0, .. "text.txt"u8, 0, .. text, ZipWriterEntrySeparator,
            0, .. "small.txt"u8, 0, .. "small"u8, ZipWriterEntrySeparator,
            1, .. "dir"u8, 0, ZipWriterEntrySeparator,
            3, .. "café.txt"u8.ToArray(), 0, .. "unicode"u8, ZipWriterEntrySeparator,
            2, .. "link"u8, 0, .. "text.txt"u8,
        ];

        for (var method = 0; method < 7; method++)
        {
            for (var encryption = 0; encryption < 5; encryption++)
            {
                var flags = (byte)((method + encryption) % 2 | (encryption is 4 ? 2 : 0) | (method is 1 ? 4 : 0) | ((encryption % 4) << 3));
                yield return new FuzzSeed($"method{method}-encryption{encryption}", (byte[])[(byte)method, (byte)encryption, flags, .. entries]);
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetPpmd8RoundTripSeeds()
    {
        const string Prefix = "TestData/Ppmd/";
        var assembly = typeof(CompressionFuzzTargets).Assembly;
        var parameters = new (byte Order, byte Memory, byte Restore)[] { (6, 0, 0), (14, 1, 1), (0, 5, 0), (3, 2, 1) };
        var index = 0;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var (order, memory, restore) = parameters[index++ % parameters.Length];
            yield return new FuzzSeed(name[Prefix.Length..], (byte[])[order, memory, restore, .. ReadPpmdResource(assembly, name)]);
        }
    }

    private sealed record ZipFuzzEntry(string Name, int Kind, byte[] Content)
    {
        /// <summary>Gets the target of a link: its content, decoded as UTF-8 (not empty).</summary>
        public string LinkTarget => Encoding.UTF8.GetString(Content) is { Length: > 0 } target ? Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(target)) : "target";
    }

    /// <summary>A write-only stream that is not seekable.</summary>
    private sealed class ZipFuzzNonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
