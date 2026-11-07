using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>
/// Reads the gitlinks (submodule commits) recorded in a git index through an <see cref="IFileSystem"/>.
/// Supports index versions 2, 3 and 4, SHA-1 and SHA-256 repositories, and split indexes.
/// See https://git-scm.com/docs/index-format
/// </summary>
internal static class GitIndexReader
{
    private const uint GitLinkMode = 0xE000;
    private const ushort ExtendedFlagsMask = 0x4000;
    private const int Sha1Size = 20;
    private const int Sha256Size = 32;

    /// <summary>Returns the gitlinks of the index, or <see langword="null"/> when the index is unavailable, unsupported or malformed.</summary>
    public static async ValueTask<Dictionary<string, string>?> ReadGitLinksAsync(IFileSystem fileSystem, string gitDirectory, CancellationToken cancellationToken)
    {
        var indexContent = await GitFileSystemUtilities.TryReadAllBytesAsync(fileSystem, Path.Combine(gitDirectory, "index"), cancellationToken).ConfigureAwait(false);
        if (indexContent is null)
            return null;

        var hashSize = await GetHashSizeAsync(fileSystem, gitDirectory, cancellationToken).ConfigureAwait(false);
        if (hashSize is 0)
            return null;

        var index = ParseIndex(indexContent, hashSize);
        if (index is null)
            return null;

        List<IndexEntry> entries;
        if (index.SharedIndexHash is { } sharedIndexHash)
        {
            // Split index (core.splitIndex): most entries live in the shared index next to the index file
            var sharedIndexPath = Path.Combine(gitDirectory, "sharedindex." + Convert.ToHexStringLower(sharedIndexHash.Span));
            var sharedIndexContent = await GitFileSystemUtilities.TryReadAllBytesAsync(fileSystem, sharedIndexPath, cancellationToken).ConfigureAwait(false);
            if (sharedIndexContent is null)
                return null;

            var sharedIndex = ParseIndex(sharedIndexContent, hashSize);
            if (sharedIndex is null)
                return null;

            var mergedEntries = MergeSplitIndex(index, sharedIndex);
            if (mergedEntries is null)
                return null;

            entries = mergedEntries;
        }
        else
        {
            entries = index.Entries;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.Mode != GitLinkMode)
                continue;

            var path = NormalizeGitPath(Encoding.UTF8.GetString(entry.Name.Span));
            result[path] = Convert.ToHexStringLower(entry.Hash.Span);
        }

        return result;
    }

    internal static string NormalizeGitPath(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        while (normalizedPath.StartsWith("./", StringComparison.Ordinal))
        {
            normalizedPath = normalizedPath[2..];
        }

        return normalizedPath.TrimEnd('/');
    }

    private static async ValueTask<int> GetHashSizeAsync(IFileSystem fileSystem, string gitDirectory, CancellationToken cancellationToken)
    {
        var commonDirectory = await GitFileSystemUtilities.GetCommonDirectoryAsync(fileSystem, gitDirectory, cancellationToken).ConfigureAwait(false);
        if (commonDirectory is null)
            return 0;

        var config = await GitFileSystemUtilities.TryReadAllTextAsync(fileSystem, Path.Combine(commonDirectory, "config"), cancellationToken).ConfigureAwait(false);
        if (config is null)
            return Sha1Size;

        string? objectFormat = null;
        foreach (var entry in GitConfigParser.Parse(config))
        {
            if (entry is { Section: "extensions", Subsection: null, Key: "objectformat" })
            {
                objectFormat = entry.Value;
            }
        }

        return objectFormat switch
        {
            null => Sha1Size,
            _ when objectFormat.Equals("sha1", StringComparison.OrdinalIgnoreCase) => Sha1Size,
            _ when objectFormat.Equals("sha256", StringComparison.OrdinalIgnoreCase) => Sha256Size,
            _ => 0,
        };
    }

    private static IndexFile? ParseIndex(byte[] content, int hashSize)
    {
        var data = content.AsSpan();
        if (data.Length < 12 + hashSize || !data[..4].SequenceEqual("DIRC"u8))
            return null;

        var version = BinaryPrimitives.ReadUInt32BigEndian(data[4..8]);
        if (version is not 2 and not 3 and not 4)
            return null;

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(data[8..12]);
        var dataEnd = data.Length - hashSize;
        var fixedEntrySize = 40 + hashSize + 2;

        // Do not trust the entry count for the initial capacity, the file may be truncated or corrupted
        var entries = new List<IndexEntry>((int)Math.Min(entryCount, (uint)(dataEnd / fixedEntrySize)));
        var position = 12;
        ReadOnlyMemory<byte> previousName = ReadOnlyMemory<byte>.Empty;
        var allocatedNameBytes = 0L;
        var maxAllocatedNameBytes = (64L * content.Length) + (16 * 1024 * 1024);
        for (uint i = 0; i < entryCount; i++)
        {
            var entryStart = position;
            if (dataEnd - position < fixedEntrySize)
                return null;

            var mode = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(position + 24, 4));
            var hash = content.AsMemory(position + 40, hashSize);
            var flags = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 40 + hashSize, 2));
            position += fixedEntrySize;

            if ((flags & ExtendedFlagsMask) != 0)
            {
                if (dataEnd - position < 2)
                    return null;

                position += 2;
            }

            ReadOnlyMemory<byte> name;
            if (version is 4)
            {
                // Path prefix compression: strip N bytes from the previous name, then append the NUL-terminated suffix
                if (!TryReadOffsetVarint(data[..dataEnd], ref position, out var stripLength) || stripLength > (ulong)previousName.Length)
                    return null;

                var suffixLength = data[position..dataEnd].IndexOf((byte)0);
                if (suffixLength < 0)
                    return null;

                var prefixLength = previousName.Length - (int)stripLength;
                if (suffixLength is 0)
                {
                    name = previousName[..prefixLength];
                }
                else
                {
                    // A crafted index can make every entry copy a long prefix; bound the total so it cannot exhaust memory
                    allocatedNameBytes += prefixLength + suffixLength;
                    if (allocatedNameBytes > maxAllocatedNameBytes)
                        return null;

                    var nameBytes = new byte[prefixLength + suffixLength];
                    previousName.Span[..prefixLength].CopyTo(nameBytes);
                    data.Slice(position, suffixLength).CopyTo(nameBytes.AsSpan(prefixLength));
                    name = nameBytes;
                }

                position += suffixLength + 1;
            }
            else
            {
                var nameLength = data[position..dataEnd].IndexOf((byte)0);
                if (nameLength < 0)
                    return null;

                name = content.AsMemory(position, nameLength);

                // 1-8 NUL bytes pad the entry to a multiple of 8 bytes while keeping the name NUL-terminated
                position = entryStart + ((position - entryStart + nameLength + 8) & ~7);
                if (position > dataEnd)
                    return null;
            }

            previousName = name;
            entries.Add(new IndexEntry(mode, (flags >> 12) & 3, name, hash));
        }

        ReadOnlyMemory<byte>? sharedIndexHash = null;
        EwahBitmap? deleteBitmap = null;
        EwahBitmap? replaceBitmap = null;
        while (dataEnd - position >= 8)
        {
            var signature = data.Slice(position, 4);
            var size = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(position + 4, 4));
            position += 8;
            if (size > (uint)(dataEnd - position))
                return null;

            if (signature.SequenceEqual("link"u8))
            {
                ReadOnlyMemory<byte> extension = content.AsMemory(position, (int)size);
                if (extension.Length < hashSize)
                    return null;

                var baseHash = extension[..hashSize];
                if (baseHash.Span.ContainsAnyExcept((byte)0))
                {
                    sharedIndexHash = baseHash;
                }

                ReadOnlyMemory<byte> bitmaps = extension[hashSize..];
                if (bitmaps.Length > 0)
                {
                    if (!EwahBitmap.TryRead(ref bitmaps, out deleteBitmap) || !EwahBitmap.TryRead(ref bitmaps, out replaceBitmap))
                        return null;
                }
            }

            position += (int)size;
        }

        return new IndexFile(entries, sharedIndexHash, deleteBitmap, replaceBitmap);
    }

    // Same logic as git's merge_base_index (split-index.c)
    private static List<IndexEntry>? MergeSplitIndex(IndexFile splitIndex, IndexFile sharedIndex)
    {
        var baseEntries = sharedIndex.Entries.ToArray();
        var splitEntries = splitIndex.Entries;
        var deleted = new bool[baseEntries.Length];

        var replacementCount = 0;
        if (splitIndex.ReplaceBitmap is { } replaceBitmap)
        {
            var valid = replaceBitmap.ForEachSetBit(baseEntries.Length, position =>
            {
                if (replacementCount >= splitEntries.Count)
                    return false;

                // Replacement entries have no name: they take the name of the entry they replace
                var replacement = splitEntries[replacementCount];
                if (replacement.Name.Length > 0)
                    return false;

                baseEntries[position] = replacement with { Name = baseEntries[position].Name };
                replacementCount++;
                return true;
            });

            if (!valid)
                return null;
        }

        if (splitIndex.DeleteBitmap is { } deleteBitmap)
        {
            var valid = deleteBitmap.ForEachSetBit(baseEntries.Length, position =>
            {
                deleted[position] = true;
                return true;
            });

            if (!valid)
                return null;
        }

        var result = new List<IndexEntry>(baseEntries.Length + splitEntries.Count - replacementCount);
        var positions = new Dictionary<(string Name, int Stage), int>();
        for (var i = 0; i < baseEntries.Length; i++)
        {
            if (!deleted[i])
            {
                AddOrReplace(baseEntries[i]);
            }
        }

        for (var i = replacementCount; i < splitEntries.Count; i++)
        {
            if (splitEntries[i].Name.Length is 0)
                return null;

            AddOrReplace(splitEntries[i]);
        }

        return result;

        void AddOrReplace(IndexEntry entry)
        {
            var key = (Encoding.UTF8.GetString(entry.Name.Span), entry.Stage);
            if (positions.TryGetValue(key, out var existingPosition))
            {
                result[existingPosition] = entry;
            }
            else
            {
                positions.Add(key, result.Count);
                result.Add(entry);
            }
        }
    }

    // git's offset varint (varint.c): each continuation adds 1 before shifting, so it is not plain LEB128
    private static bool TryReadOffsetVarint(ReadOnlySpan<byte> data, ref int position, out ulong value)
    {
        value = 0;
        if (position >= data.Length)
            return false;

        var c = data[position++];
        value = (ulong)(c & 127);
        while ((c & 128) != 0)
        {
            value++;
            if (value is 0 || (value >> 57) != 0 || position >= data.Length)
                return false;

            c = data[position++];
            value = (value << 7) + (ulong)(c & 127);
        }

        return true;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct IndexEntry(uint Mode, int Stage, ReadOnlyMemory<byte> Name, ReadOnlyMemory<byte> Hash);

    private sealed record IndexFile(List<IndexEntry> Entries, ReadOnlyMemory<byte>? SharedIndexHash, EwahBitmap? DeleteBitmap, EwahBitmap? ReplaceBitmap);

    // EWAH compressed bitmap, as serialized by git's ewah_serialize_to
    private sealed class EwahBitmap
    {
        private const int BitsInWord = 64;
        private const int RunningLengthBits = 32;

        private readonly ReadOnlyMemory<byte> _words;

        private EwahBitmap(ReadOnlyMemory<byte> words)
        {
            _words = words;
        }

        public static bool TryRead(ref ReadOnlyMemory<byte> data, out EwahBitmap? bitmap)
        {
            bitmap = null;
            if (data.Length < 8)
                return false;

            // uint32 bit count, uint32 word count, words, uint32 position of the last running length word
            var wordCount = BinaryPrimitives.ReadUInt32BigEndian(data.Span[4..8]);
            var totalLength = 8L + (wordCount * 8L) + 4;
            if (totalLength > data.Length)
                return false;

            bitmap = new EwahBitmap(data.Slice(8, (int)wordCount * 8));
            data = data[(int)totalLength..];
            return true;
        }

        /// <summary>Calls <paramref name="callback"/> for each set bit. Returns <see langword="false"/> when a set bit is out of range or the callback fails.</summary>
        public bool ForEachSetBit(int bitCount, Func<int, bool> callback)
        {
            var wordCount = _words.Length / 8;
            var position = 0L;
            var pointer = 0;
            while (pointer < wordCount)
            {
                var runningLengthWord = ReadWord(pointer);
                var runningBit = (runningLengthWord & 1) != 0;
                var runningLength = (long)((runningLengthWord >> 1) & uint.MaxValue);
                var literalWordCount = (long)(runningLengthWord >> (1 + RunningLengthBits));

                if (runningBit)
                {
                    var end = position + (runningLength * BitsInWord);
                    if (end > bitCount)
                        return false;

                    for (; position < end; position++)
                    {
                        if (!callback((int)position))
                            return false;
                    }
                }
                else
                {
                    position += runningLength * BitsInWord;
                }

                pointer++;
                if (literalWordCount > wordCount - pointer)
                    return false;

                for (var k = 0; k < literalWordCount; k++)
                {
                    var word = ReadWord(pointer);
                    for (var bit = 0; bit < BitsInWord; bit++, position++)
                    {
                        if ((word & (1UL << bit)) is 0)
                            continue;

                        if (position >= bitCount || !callback((int)position))
                            return false;
                    }

                    pointer++;
                }
            }

            return true;
        }

        private ulong ReadWord(int index) => BinaryPrimitives.ReadUInt64BigEndian(_words.Span.Slice(index * 8, 8));
    }
}
