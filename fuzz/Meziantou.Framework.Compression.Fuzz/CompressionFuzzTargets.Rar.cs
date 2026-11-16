using System.Security.Cryptography;
using Meziantou.Framework.Compression;
using Meziantou.Framework.Compression.Internals.Rar;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.CompressionFuzz;

public static partial class CompressionFuzzTargets
{
    private const string RarTestDataPrefix = "TestData/Rar/";
    private const string RarPassword = "password";

    // The decoders may allocate 16 MiB, and the targets read at most 4 MiB of content
    private const long RarMaxMemoryUsage = 16 * 1024 * 1024;
    private const int RarMaxOutput = 4 * 1024 * 1024;

    // The key derivation of RAR5 runs 3 * 2^count iterations: the seeds use 2^15
    private const int RarMaxKdfCount = 16;

    private static string[] RarTokens =>
    [
        "Rar!\u001A\u0007\u0000", "Rar!\u001A\u0007\u0001\u0000", "RE~^", "MZ",
        "s", "t", "u", "w", "x", "z", "{", "\u0080\u0080", "\u0000\u0080", "ÿÿÿÿ",
        "CMT", "QO", "ACL", "STM", "RR", "\u0001", "\u0002", "\u0003", "\u0004", "\u0005", "\u0006", "\u0007",
    ];

    private static FuzzTarget[] RarTargets =>
    [
        new("rar-read", RarReadTarget) { Seeds = GetRarArchiveSeeds, Dictionary = RarTokens },
        new("rar-reader", RarReaderTarget) { Seeds = GetRarArchiveSeeds, Dictionary = RarTokens },
        new("rar5-unpack", Rar5UnpackTarget) { Seeds = () => GetRarUnpackSeeds(rar5: true), Dictionary = RarTokens },
        new("rar29-unpack", Rar29UnpackTarget) { Seeds = () => GetRarUnpackSeeds(rar5: false), Dictionary = RarTokens },
    ];

    /// <summary>
    /// <c>options, data</c>: a RAR archive, read by <see cref="RarArchive"/> with the password <c>password</c> when bit 0 of
    /// <c>options</c> is set. Every entry is read, with at most 4 MiB of content in total. The archive rejects invalid data
    /// with an <see cref="InvalidDataException"/>, an <see cref="ArchivePasswordException"/> or a <see cref="NotSupportedException"/>,
    /// and an entry read to its end has its length.
    /// </summary>
    private static void RarReadTarget(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        ReadRarArchive(data.ToArray(), (options & 1) != 0 ? RarPassword : null);
    }

    /// <summary>
    /// <c>options, data</c>: a RAR archive, read by the forward-only reader from a stream that cannot seek and whose reads
    /// return at most <c>1 + options / 2</c> bytes. When both the forward-only reader and <see cref="RarArchive"/> read the
    /// whole archive, they return the same entries, and the same content for the entries they both read to their end.
    /// </summary>
    private static void RarReaderTarget(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var password = (options & 1) != 0 ? RarPassword : null;
        var bytes = data.ToArray();
        var expected = ReadRarArchive(bytes, password);
        List<RarEntrySummary>? actual = [];
        try
        {
            using var reader = RarArchive.CreateForwardReader(new RarShortReadStream(bytes, 1 + (options >> 1)), CreateRarOptions(password), leaveOpen: false);
            var total = 0L;
            while (reader.MoveToNextEntry())
            {
                actual.Add(ReadRarEntry((RarArchiveEntry)reader.Entry!, ref total));
            }
        }
        catch (Exception ex) when (IsRarError(ex))
        {
            actual = null;
        }

        if (expected is null || actual is null)
            return;

        FuzzAssert.Equal(string.Join('\n', expected.Select(entry => entry.Name)), string.Join('\n', actual.Select(entry => entry.Name)), "The forward-only reader returns other entries than RarArchive");
        for (var i = 0; i < expected.Count; i++)
        {
            FuzzAssert.Equal(expected[i].Length, actual[i].Length, $"The forward-only reader returns another length for the entry '{expected[i].Name}'");
            if (expected[i].Hash is not null && actual[i].Hash is not null && !expected[i].IsHardLink)
            {
                FuzzAssert.Equal(expected[i].Hash, actual[i].Hash, $"The forward-only reader returns another content for the entry '{expected[i].Name}'");
            }
        }
    }

    /// <summary>
    /// <c>dictionary, size (3 bytes), chunk, data</c>: the compressed data of a RAR5 file. The decoder rejects invalid data
    /// with an <see cref="InvalidDataException"/>, returns at most the size of the file, and returns the same bytes
    /// whatever the size of the reads.
    /// </summary>
    private static void Rar5UnpackTarget(ReadOnlySpan<byte> data)
    {
        var dictionary = FuzzInput.ReadByte(ref data);
        var header = new RarFileHeader
        {
            Format = RarFormatVersion.Rar5,
            UnpackVersion = (dictionary & 0x80) != 0 ? 70 : 50,
            Method = 3,
            DictionarySize = 0x20000L << (dictionary & 0xF),
            UnpackedSize = ReadRarSize(ref data),
        };

        CheckRarUnpack(header, ref data);
    }

    /// <summary>
    /// <c>version, size (3 bytes), chunk, data</c>: the compressed data of a RAR 2.9 file (or a RAR 2.0 file when bit 0
    /// of <c>version</c> is set). The decoder rejects invalid data with an <see cref="InvalidDataException"/>, or a
    /// <see cref="NotSupportedException"/> for a RarVM program, returns at most the size of the file, and returns the same
    /// bytes whatever the size of the reads.
    /// </summary>
    private static void Rar29UnpackTarget(ReadOnlySpan<byte> data)
    {
        var version = FuzzInput.ReadByte(ref data);
        var header = new RarFileHeader
        {
            Format = RarFormatVersion.Rar4,
            UnpackVersion = (version & 1) != 0 ? 20 : 29,
            Method = 3,
            DictionarySize = 0x10000L << ((version >> 1) & 7),
            UnpackedSize = ReadRarSize(ref data),
        };

        CheckRarUnpack(header, ref data);
    }

    private static long ReadRarSize(ref ReadOnlySpan<byte> data)
    {
        return (FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8) | (FuzzInput.ReadByte(ref data) << 16)) % (RarMaxOutput + 1);
    }

    private static void CheckRarUnpack(RarFileHeader header, ref ReadOnlySpan<byte> data)
    {
        var chunk = 1 + FuzzInput.ReadByte(ref data);
        var input = data.ToArray();
        var whole = DecodeRar(header, input, header.UnpackedSize is 0 ? 1 : (int)header.UnpackedSize);
        if (whole is null)
            return;

        FuzzAssert.True(whole.Length <= header.UnpackedSize, "The decoder returns more than the size of the file");
        var chunked = DecodeRar(header, input, chunk);
        FuzzAssert.True(chunked is not null, "The decoder fails when the reads are smaller");
        FuzzAssert.True(whole.AsSpan().SequenceEqual(chunked), "The decoder returns other bytes when the reads are smaller");
    }

    private static byte[]? DecodeRar(RarFileHeader header, byte[] input, int chunk)
    {
        try
        {
            var unpacker = RarDecoding.CreateUnpacker(header, RarMaxMemoryUsage);
            using var stream = RarDecoding.OpenDecoded(new MemoryStream(input, writable: false), header, unpacker, solid: false);
            using var output = new MemoryStream();
            var buffer = new byte[chunk];
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    private static ArchiveReaderOptions CreateRarOptions(string? password)
    {
        RarKeyStore.MaxKdfCount = RarMaxKdfCount;
        return new ArchiveReaderOptions { Password = password, MaxMemoryUsage = RarMaxMemoryUsage };
    }

    private static bool IsRarError(Exception ex) => ex is InvalidDataException or ArchivePasswordException or NotSupportedException;

    /// <summary>Reads the entries of an archive with <see cref="RarArchive"/>, or returns <see langword="null"/> when it rejects the archive.</summary>
    private static List<RarEntrySummary>? ReadRarArchive(byte[] data, string? password)
    {
        try
        {
            using var archive = new RarArchive(new MemoryStream(data, writable: false), CreateRarOptions(password));
            _ = archive.Comment;
            var result = new List<RarEntrySummary>();
            var total = 0L;
            foreach (var entry in archive.Entries)
            {
                result.Add(ReadRarEntry(entry, ref total));
            }

            return result;
        }
        catch (Exception ex) when (IsRarError(ex))
        {
            return null;
        }
    }

    /// <summary>Reads an entry; its hash is <see langword="null"/> when its content is not read to its end.</summary>
    private static RarEntrySummary ReadRarEntry(RarArchiveEntry entry, ref long total)
    {
        _ = entry.LastWriteTime;
        _ = entry.Attributes;
        _ = entry.UnixFileMode;
        _ = entry.Crc32;
        _ = entry.Blake2spHash;
        _ = entry.CompressedLength;
        string? hash = null;
        try
        {
            _ = entry.LinkTarget;
            using var stream = entry.Open();
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[16384];
            long length = 0;
            var complete = false;
            while (total < RarMaxOutput)
            {
                var read = stream.Read(buffer);
                if (read is 0)
                {
                    complete = true;
                    break;
                }

                sha.AppendData(buffer, 0, read);
                length += read;
                total += read;
            }

            if (complete)
            {
                // The length of a link of RAR5 is the length of its target, and its content is empty (or the content of its target)
                if (entry.Length >= 0 && entry.HardLinkTarget is null && entry.LinkTarget is null)
                {
                    FuzzAssert.Equal(entry.Length, length, $"The content of the entry '{entry.FullName}' does not have the length of the entry");
                }

                hash = Convert.ToHexString(sha.GetHashAndReset());
            }
        }
        catch (Exception ex) when (IsRarError(ex))
        {
        }

        return new RarEntrySummary(entry.FullName, entry.Length, hash, entry.HardLinkTarget is not null);
    }

    private static IEnumerable<FuzzSeed> GetRarArchiveSeeds()
    {
        foreach (var (name, data) in GetRarTestFiles())
        {
            yield return new FuzzSeed(name, (byte[])[0, .. data]);
            yield return new FuzzSeed(name + "-password", (byte[])[7, .. data]);
        }
    }

    private static IEnumerable<FuzzSeed> GetRarUnpackSeeds(bool rar5)
    {
        foreach (var (name, data) in GetRarTestFiles())
        {
            List<(string Name, RarFileHeader Header, byte[] Packed)> files = [];
            try
            {
                using var archive = new RarArchive(new MemoryStream(data, writable: false));
                foreach (var entry in archive.Entries)
                {
                    var header = entry.Data.Header;
                    if (entry.IsEncrypted || !entry.Data.IsCompressed || entry.Data.Parts.Count is not 1 || header.UnpackedSize > RarMaxOutput || entry.IsSolid)
                        continue;

                    var part = entry.Data.Parts[0];
                    files.Add((entry.FullName, header, data.AsSpan((int)part.Offset, (int)part.Length).ToArray()));
                }
            }
            catch (Exception ex) when (IsRarError(ex))
            {
                continue;
            }

            foreach (var (entryName, header, packed) in files)
            {
                if ((header.Format is RarFormatVersion.Rar5) != rar5)
                    continue;

                var size = (int)header.UnpackedSize;
                byte options;
                if (rar5)
                {
                    options = (byte)(Math.Min(15, Math.Max(0, (int)Math.Log2(header.DictionarySize / 0x20000))) | (header.UnpackVersion is 70 ? 0x80 : 0));
                }
                else if (header.UnpackVersion is 20 or 26)
                {
                    options = 1;
                }
                else if (header.UnpackVersion is 29 or 36)
                {
                    options = 0;
                }
                else
                {
                    continue;
                }

                yield return new FuzzSeed(name + "-" + entryName.Replace('/', '-'), (byte[])[options, (byte)size, (byte)(size >> 8), (byte)(size >> 16), 63, .. packed]);
            }
        }
    }

    private static IEnumerable<(string Name, byte[] Data)> GetRarTestFiles()
    {
        var assembly = typeof(CompressionFuzzTargets).Assembly;
        foreach (var resourceName in assembly.GetManifestResourceNames().Where(name => name.StartsWith(RarTestDataPrefix, StringComparison.Ordinal) && name.EndsWith(".rar", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = FuzzResources.Open(assembly, resourceName);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return (resourceName[RarTestDataPrefix.Length..], memory.ToArray());
        }
    }

    private sealed record RarEntrySummary(string Name, long Length, string? Hash, bool IsHardLink);

    /// <summary>A stream that cannot seek and whose reads return at most <c>maxRead</c> bytes.</summary>
    private sealed class RarShortReadStream(byte[] data, int maxRead) : Stream
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = Math.Min(Math.Min(count, maxRead), data.Length - _position);
            data.AsSpan(_position, read).CopyTo(buffer.AsSpan(offset));
            _position += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
