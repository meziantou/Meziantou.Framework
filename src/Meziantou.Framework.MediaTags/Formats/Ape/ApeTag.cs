using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Ape;

/// <summary>
/// An APEv1 or APEv2 tag: a list of key/value items, between an optional 32-byte header and a 32-byte footer.
/// </summary>
/// <remarks>See https://wiki.hydrogenaud.io/index.php?title=APEv2_specification.</remarks>
internal static class ApeTag
{
    public const int HeaderSize = 32;

    /// <summary>The largest tag read into memory. Cover art makes a tag much larger than the 8 KB the specification recommends.</summary>
    public const int MaxTagSize = 64 * 1024 * 1024;

    // Tag flags
    public const uint HasHeaderFlag = 0x8000_0000;
    public const uint HasNoFooterFlag = 0x4000_0000;
    public const uint IsHeaderFlag = 0x2000_0000;

    // Item flags
    public const int ReadOnlyFlag = 0x01;
    public const int ItemTypeMask = 0x06;
    public const int TextItemType = 0x00;
    public const int BinaryItemType = 0x02;
    public const int LinkItemType = 0x04;

    /// <summary>The flags an item may carry: the read-only flag and the item type.</summary>
    public const int ItemFlagsMask = ReadOnlyFlag | ItemTypeMask;

    public const int MinKeyLength = 2;
    public const int MaxKeyLength = 255;

    private const int Version1 = 1000;
    private const int Version2 = 2000;

    public const int Id3v1TagSize = 128;

    /// <summary>
    /// Gets the end of the audio of a file that may end with APE and ID3v1 tags, in any order and any number.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="audioStart">The first byte the tags cannot cover.</param>
    /// <param name="end">The end of the file.</param>
    /// <param name="lastApeTag">The APE tag closest to the end of the file, the one readers use, or <see langword="null"/> when there is none.</param>
    /// <param name="lastId3v1Tag">The offset of the ID3v1 tag closest to the end of the file, or <see langword="null"/> when there is none.</param>
    /// <remarks>
    /// Some taggers append a new tag without removing the old one, so all of them are skipped: a write that kept a stale one
    /// in the audio would bring its values back once the new tag is removed.
    /// </remarks>
    public static long FindAudioEnd(Stream stream, long audioStart, long end, out ApeTagLocation? lastApeTag, out long? lastId3v1Tag)
    {
        lastApeTag = null;
        lastId3v1Tag = null;
        while (true)
        {
            // The footer is checked first: a value of the APE tag can hold "TAG" where an ID3v1 tag would start.
            if (TryLocate(stream, audioStart, end, out var location))
            {
                lastApeTag ??= location;
                end = location.Start;
                continue;
            }

            if (end - Id3v1TagSize >= audioStart && HasId3v1TagAt(stream, end - Id3v1TagSize))
            {
                end -= Id3v1TagSize;
                lastId3v1Tag ??= end;
                continue;
            }

            return end;
        }
    }

    /// <summary>Locates an APE tag whose footer ends at <paramref name="end"/>, and that does not start before <paramref name="minStart"/>.</summary>
    public static bool TryLocate(Stream stream, long minStart, long end, out ApeTagLocation location)
    {
        location = default;
        if (end - HeaderSize < minStart)
            return false;

        Span<byte> footer = stackalloc byte[HeaderSize];
        stream.Position = end - HeaderSize;
        if (stream.ReadAtLeast(footer, footer.Length, throwOnEndOfStream: false) < footer.Length)
            return false;

        if (!footer.StartsWith("APETAGEX"u8))
            return false;

        var version = BinaryPrimitives.ReadInt32LittleEndian(footer[8..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(footer[12..]);
        var itemCount = BinaryPrimitives.ReadUInt32LittleEndian(footer[16..]);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(footer[20..]);
        if (version is not (Version1 or Version2) || (flags & IsHeaderFlag) != 0 || size < HeaderSize)
            return false;

        // The size covers the items and the footer. An APEv1 tag has no header, whatever its flags say.
        var hasHeader = version == Version2 && (flags & HasHeaderFlag) != 0;
        var start = end - size - (hasHeader ? HeaderSize : 0);
        if (start < minStart)
            return false;

        location = new ApeTagLocation(start, ItemsStart: end - size, ItemsLength: size - HeaderSize, itemCount, IsVersion1: version == Version1);
        return true;
    }

    /// <summary>Reads the items of a tag. Reading stops at the first malformed item: the items before it are still usable.</summary>
    /// <returns><see langword="false"/> when the tag is too large to read.</returns>
    public static bool TryReadItems(Stream stream, in ApeTagLocation location, [NotNullWhen(true)] out List<ApeItem>? items)
    {
        items = null;
        stream.Position = location.ItemsStart;
        if (!StreamHelpers.TryReadExact(stream, location.ItemsLength, MaxTagSize, out var data))
            return false;

        items = ParseItems(data, location.ItemCount, location.IsVersion1);
        return true;
    }

    private static List<ApeItem> ParseItems(byte[] data, uint itemCount, bool isVersion1)
    {
        var items = new List<ApeItem>();
        var span = data.AsSpan();
        var offset = 0;
        for (var i = 0u; i < itemCount && offset + 8 < span.Length; i++)
        {
            var valueSize = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            var itemFlags = BinaryPrimitives.ReadInt32LittleEndian(span[(offset + 4)..]);
            var keyStart = offset + 8;
            var keyLength = span[keyStart..].IndexOf((byte)0);
            if (keyLength < 0)
                break;

            var valueStart = keyStart + keyLength + 1;
            if (valueSize > (uint)(span.Length - valueStart))
                break;

            // A key the writer refuses (such as "TAG") is skipped, so the tags read can always be written back.
            var key = span.Slice(keyStart, keyLength);
            if (IsValidKey(key) && !IsForbiddenKey(Encoding.ASCII.GetString(key)))
            {
                // APEv1 items are all text, and their flags are reserved.
                items.Add(new ApeItem(Encoding.ASCII.GetString(key), isVersion1 ? TextItemType : itemFlags, data.AsMemory(valueStart, (int)valueSize)));
            }

            offset = valueStart + (int)valueSize;
        }

        return items;
    }

    /// <summary>Gets whether a key is valid: 2 to 255 printable ASCII characters, other than the ones the specification forbids.</summary>
    public static bool IsValidKey(string key)
    {
        if (key.Length is < MinKeyLength or > MaxKeyLength)
            return false;

        foreach (var c in key)
        {
            if (c is < (char)0x20 or > (char)0x7E)
                return false;
        }

        return !IsForbiddenKey(key);
    }

    private static bool IsValidKey(ReadOnlySpan<byte> key)
    {
        if (key.Length is < MinKeyLength or > MaxKeyLength)
            return false;

        foreach (var c in key)
        {
            if (c is < 0x20 or > 0x7E)
                return false;
        }

        return true;
    }

    private static bool IsForbiddenKey(string key) => key.Equals("ID3", StringComparison.OrdinalIgnoreCase) || key.Equals("TAG", StringComparison.OrdinalIgnoreCase)
        || key.Equals("OggS", StringComparison.OrdinalIgnoreCase) || key.Equals("MP+", StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds an APEv2 tag, with a header and a footer.</summary>
    public static byte[] Build(IReadOnlyList<ApeItem> items)
    {
        var itemsLength = 0L;
        foreach (var item in items)
        {
            itemsLength += 8 + item.Key.Length + 1 + item.Value.Length;
        }

        var result = new byte[HeaderSize + itemsLength + HeaderSize];
        var size = (uint)(itemsLength + HeaderSize);
        WriteHeader(result.AsSpan(0, HeaderSize), size, items.Count, HasHeaderFlag | IsHeaderFlag);

        var offset = HeaderSize;
        foreach (var item in items)
        {
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset), item.Value.Length);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(offset + 4), item.Flags);
            offset += 8;
            offset += Encoding.ASCII.GetBytes(item.Key, result.AsSpan(offset));
            result[offset++] = 0;
            item.Value.Span.CopyTo(result.AsSpan(offset));
            offset += item.Value.Length;
        }

        WriteHeader(result.AsSpan(offset, HeaderSize), size, items.Count, HasHeaderFlag);
        return result;

        static void WriteHeader(Span<byte> buffer, uint size, int itemCount, uint flags)
        {
            "APETAGEX"u8.CopyTo(buffer);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[8..], Version2);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[12..], size);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[16..], itemCount);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[20..], flags);
        }
    }

    private static bool HasId3v1TagAt(Stream stream, long offset)
    {
        if (offset < 0)
            return false;

        stream.Position = offset;
        Span<byte> magic = stackalloc byte[3];
        return stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) == magic.Length && magic is [(byte)'T', (byte)'A', (byte)'G'];
    }
}
