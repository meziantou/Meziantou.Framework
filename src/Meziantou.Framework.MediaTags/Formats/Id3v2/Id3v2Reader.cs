using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.MediaTags.Formats.Id3v1;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

internal static class Id3v2Reader
{
    // ID3v2.4 frame format flags (the low byte of the frame flags)
    internal const ushort V24GroupingFlag = 0x0040;
    internal const ushort V24CompressionFlag = 0x0008;
    internal const ushort V24EncryptionFlag = 0x0004;
    internal const ushort V24UnsynchronisationFlag = 0x0002;
    internal const ushort V24DataLengthIndicatorFlag = 0x0001;

    // ID3v2.3 frame format flags (the low byte of the frame flags)
    internal const ushort V23CompressionFlag = 0x0080;
    internal const ushort V23EncryptionFlag = 0x0040;
    internal const ushort V23GroupingFlag = 0x0020;

    /// <summary>The maximum number of frames read from one tag.</summary>
    /// <remarks>
    /// A frame costs ten bytes in the file but a retained object here, so an unbounded count lets a small tag
    /// force a disproportionate allocation. Real tags hold a few dozen frames.
    /// </remarks>
    private const int MaxFrameCount = 65536;

    public static bool TryReadTag(Stream stream, MediaTagInfo tags)
    {
        // An art-bearing tag is large enough to land on the large object heap, and it does not outlive this
        // method, so the buffer is rented rather than allocated on every read.
        if (!TryReadTagData(stream, rentBuffer: true, out var header, out var buffer, out var length))
            return false;

        try
        {
            var frames = ParseFrames(buffer.AsMemory(0, length), header);
            ApplyFrames(frames, header.MajorVersion, tags);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return true;
    }

    /// <summary>
    /// Reads the frames of the ID3v2 tag at the current position of the stream.
    /// </summary>
    /// <returns><see langword="false"/> when there is no readable tag at the current position.</returns>
    public static bool TryReadFrames(Stream stream, out Id3v2Header header, out List<Id3v2Frame> frames)
    {
        frames = [];
        if (!TryReadTagData(stream, rentBuffer: false, out header, out var buffer, out var length))
            return false;

        frames = ParseFrames(buffer.AsMemory(0, length), header);
        return true;
    }

    private static bool TryReadTagData(Stream stream, bool rentBuffer, out Id3v2Header header, [NotNullWhen(true)] out byte[]? buffer, out int length)
    {
        header = default;
        buffer = null;
        length = 0;

        var originalPosition = stream.Position;

        Span<byte> headerBytes = stackalloc byte[10];
        if (stream.ReadAtLeast(headerBytes, 10, throwOnEndOfStream: false) < 10 || !Id3v2Header.TryParse(headerBytes, out header))
        {
            stream.Position = originalPosition;
            return false;
        }

        // The declared size comes from the file and reaches 256 MB, so it must be checked against the bytes
        // that are actually there before it is used as an allocation size.
        if (stream.CanSeek && header.TagSize > stream.Length - stream.Position)
        {
            stream.Position = originalPosition;
            return false;
        }

        var data = rentBuffer ? ArrayPool<byte>.Shared.Rent(header.TagSize) : new byte[header.TagSize];
        if (stream.ReadAtLeast(data.AsSpan(0, header.TagSize), header.TagSize, throwOnEndOfStream: false) < header.TagSize)
        {
            if (rentBuffer)
                ArrayPool<byte>.Shared.Return(data);

            stream.Position = originalPosition;
            return false;
        }

        length = header.TagSize;

        // ID3v2.2 and ID3v2.3 unsynchronise the whole tag, frame headers included. ID3v2.4 unsynchronises the
        // content of each frame instead, and its frame sizes count the unsynchronised bytes: undoing it over
        // the whole tag would shift every frame that follows the first 0xFF 0x00 pair.
        if (header.Unsynchronisation && header.MajorVersion < 4)
            length = UndoUnsynchronisation(data.AsSpan(0, length));

        buffer = data;
        return true;
    }

    private static List<Id3v2Frame> ParseFrames(ReadOnlyMemory<byte> tagData, in Id3v2Header header)
    {
        var frames = new List<Id3v2Frame>();
        var data = tagData.Span;
        var offset = 0L;

        if (header.ExtendedHeader)
        {
            // In ID3v2.2 this flag means the whole tag is compressed, with a scheme the specification never
            // defined, so such a tag cannot be read.
            if (header.MajorVersion == 2 || data.Length < 4)
                return frames;

            // The ID3v2.4 size includes the size field itself, the ID3v2.3 size does not.
            var extendedHeaderSize = header.MajorVersion == 4
                ? SynchsafeInteger.Decode(data[..4])
                : BinaryPrimitives.ReadUInt32BigEndian(data[..4]) + 4L;

            if (extendedHeaderSize > data.Length)
                return frames;

            offset = extendedHeaderSize;
        }

        var idLength = header.MajorVersion == 2 ? 3 : 4;
        var frameHeaderSize = header.MajorVersion == 2 ? 6 : 10;
        while (offset + frameHeaderSize <= data.Length && frames.Count < MaxFrameCount)
        {
            var frameHeader = data.Slice((int)offset, frameHeaderSize);

            // Padding, or bytes that are not a frame. Anything read past this point would be garbage.
            if (!IsValidFrameId(frameHeader[..idLength]))
                break;

            var frameId = Encoding.ASCII.GetString(frameHeader[..idLength]);
            long frameSize;
            ushort flags;
            switch (header.MajorVersion)
            {
                case 2:
                    frameSize = (frameHeader[3] << 16) | (frameHeader[4] << 8) | frameHeader[5];
                    flags = 0;
                    break;

                case 3:
                    frameSize = BinaryPrimitives.ReadUInt32BigEndian(frameHeader[4..8]);
                    flags = BinaryPrimitives.ReadUInt16BigEndian(frameHeader[8..10]);
                    break;

                default:
                    frameSize = SynchsafeInteger.Decode(frameHeader[4..8]);
                    flags = BinaryPrimitives.ReadUInt16BigEndian(frameHeader[8..10]);
                    break;
            }

            offset += frameHeaderSize;
            if (frameSize > data.Length - offset)
                break;

            var storedData = tagData.Slice((int)offset, (int)frameSize);
            offset += frameSize;

            frames.Add(new Id3v2Frame
            {
                Id = frameId,
                Flags = flags,
                StoredData = storedData,
                Payload = DecodePayload(storedData, flags, header),
            });
        }

        return frames;
    }

    private static bool IsValidFrameId(ReadOnlySpan<byte> id)
    {
        foreach (var c in id)
        {
            if (c is not ((>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9')))
                return false;
        }

        return true;
    }

    private static ReadOnlyMemory<byte>? DecodePayload(ReadOnlyMemory<byte> storedData, ushort flags, in Id3v2Header header)
    {
        return header.MajorVersion switch
        {
            4 => DecodeV24Payload(storedData, flags, header.Unsynchronisation),
            3 => DecodeV23Payload(storedData, flags),
            _ => storedData,
        };
    }

    private static ReadOnlyMemory<byte>? DecodeV24Payload(ReadOnlyMemory<byte> storedData, ushort flags, bool tagIsUnsynchronised)
    {
        if ((flags & V24EncryptionFlag) != 0)
            return null;

        // Unsynchronisation covers everything after the frame header, including the data length indicator.
        var data = storedData;
        if ((flags & V24UnsynchronisationFlag) != 0 || tagIsUnsynchronised)
        {
            var copy = storedData.ToArray();
            data = copy.AsMemory(0, UndoUnsynchronisation(copy));
        }

        // The additional data is stored in the order of the flags: group identifier, then data length indicator.
        var prefixLength = 0;
        if ((flags & V24GroupingFlag) != 0)
            prefixLength++;

        if ((flags & V24DataLengthIndicatorFlag) != 0)
            prefixLength += 4;

        if (prefixLength > data.Length)
            return null;

        data = data[prefixLength..];
        return (flags & V24CompressionFlag) != 0 ? Inflate(data) : data;
    }

    private static ReadOnlyMemory<byte>? DecodeV23Payload(ReadOnlyMemory<byte> storedData, ushort flags)
    {
        if ((flags & V23EncryptionFlag) != 0)
            return null;

        // The additional data is stored in the order of the flags: decompressed size, then group identifier.
        var prefixLength = 0;
        if ((flags & V23CompressionFlag) != 0)
            prefixLength += 4;

        if ((flags & V23GroupingFlag) != 0)
            prefixLength++;

        if (prefixLength > storedData.Length)
            return null;

        var data = storedData[prefixLength..];
        return (flags & V23CompressionFlag) != 0 ? Inflate(data) : data;
    }

    private static ReadOnlyMemory<byte>? Inflate(ReadOnlyMemory<byte> compressed)
    {
        try
        {
            using var input = new MemoryStream(compressed.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            // A few bytes of zlib data can inflate to gigabytes, so the output is bounded like any other record.
            var buffer = new byte[8192];
            int read;
            while ((read = zlib.Read(buffer)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > StreamHelpers.MaxRecordDataSize)
                    return null;
            }

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static void ApplyFrames(List<Id3v2Frame> frames, byte majorVersion, MediaTagInfo tags)
    {
        var commentIndex = SelectDescribedTextFrame(frames, majorVersion, Id3v2FrameId.Comment);
        var lyricsIndex = SelectDescribedTextFrame(frames, majorVersion, Id3v2FrameId.Lyrics);

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (frame.Payload is not { } payload)
                continue;

            // A v2.2 PIC frame carries a fixed 3-character image format where APIC carries a null-terminated MIME
            // type, so it cannot be parsed with the APIC layout.
            if (majorVersion == 2 && frame.Id == Id3v2FrameId.PictureV22)
            {
                ReadPictureFrameV22(payload.Span, tags);
                continue;
            }

            var frameId = NormalizeFrameId(frame.Id, majorVersion);
            if ((frameId == Id3v2FrameId.Comment && i != commentIndex) || (frameId == Id3v2FrameId.Lyrics && i != lyricsIndex))
                continue;

            ProcessFrame(frameId, payload.Span, tags);
        }
    }

    /// <summary>
    /// Gets the ID3v2.4 identifier of a frame read from a tag of the given version.
    /// </summary>
    internal static string NormalizeFrameId(string frameId, byte majorVersion) => majorVersion switch
    {
        2 => ConvertV22ToV24FrameId(frameId),
        3 when frameId == Id3v2FrameId.YearV23 => Id3v2FrameId.Year,
        _ => frameId,
    };

    /// <summary>
    /// Selects the COMM or USLT frame that holds the comment or the lyrics.
    /// </summary>
    /// <remarks>
    /// A tag can hold several of them, told apart by their description. The one with an empty description is
    /// the user's. iTunes stores its own data (<c>iTunNORM</c>, <c>iTunSMPB</c>, ...) in described COMM frames,
    /// which must never be mistaken for the comment.
    /// </remarks>
    /// <returns>The index of the frame, or -1 when there is none.</returns>
    internal static int SelectDescribedTextFrame(IReadOnlyList<Id3v2Frame> frames, byte majorVersion, string frameId)
    {
        var fallbackIndex = -1;
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (frame.Payload is not { } payload || NormalizeFrameId(frame.Id, majorVersion) != frameId)
                continue;

            if (!TryReadDescribedText(payload.Span, out var description, out _))
                continue;

            if (description.Length == 0)
                return i;

            if (fallbackIndex < 0 && !(frameId == Id3v2FrameId.Comment && IsItunesInternalComment(description)))
                fallbackIndex = i;
        }

        return fallbackIndex;
    }

    /// <summary>Gets the description of a COMM or USLT frame, or <see langword="null"/> when it cannot be read.</summary>
    internal static string? GetDescription(ReadOnlySpan<byte> payload) => TryReadDescribedText(payload, out var description, out _) ? description : null;

    private static bool IsItunesInternalComment(string description) => description.StartsWith("iTun", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the number of bytes the ID3v2 tag at the current position occupies, or 0 when there is none.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when a tag header is present but its declared size runs past the end of the
    /// stream. A writer must not treat those bytes as audio, and must not treat the tag as absent either: doing
    /// the first drops the audio and doing the second embeds a stale tag in the audio stream.
    /// </returns>
    public static bool TryGetTagSize(Stream stream, out int tagSize)
    {
        tagSize = 0;

        var originalPosition = stream.Position;
        Span<byte> headerBytes = stackalloc byte[10];
        if (stream.ReadAtLeast(headerBytes, 10, throwOnEndOfStream: false) < 10)
        {
            stream.Position = originalPosition;
            return true;
        }

        if (!Id3v2Header.TryParse(headerBytes, out var header))
        {
            stream.Position = originalPosition;
            return true;
        }

        stream.Position = originalPosition;

        var size = 10L + header.TagSize + (header.FooterPresent ? 10 : 0);
        if (stream.CanSeek && size > stream.Length - originalPosition)
            return false;

        tagSize = (int)size;
        return true;
    }

    /// <summary>
    /// Gets the size of the ID3v2 tag at the current position, treating an unusable declared size as no tag.
    /// </summary>
    public static int GetTagSize(Stream stream) => TryGetTagSize(stream, out var tagSize) ? tagSize : 0;

    private static void ProcessFrame(string frameId, ReadOnlySpan<byte> data, MediaTagInfo tags)
    {
        if (data.IsEmpty)
            return;

        switch (frameId)
        {
            case Id3v2FrameId.Title:
                tags.Title ??= ReadTextFrame(data);
                break;

            case Id3v2FrameId.Artist:
                tags.Artist ??= ReadTextFrame(data);
                break;

            case Id3v2FrameId.Album:
                tags.Album ??= ReadTextFrame(data);
                break;

            case Id3v2FrameId.AlbumArtist:
                tags.AlbumArtist ??= ReadTextFrame(data);
                break;

            case Id3v2FrameId.Genre:
                tags.Genre ??= ParseGenre(ReadTextFrame(data));
                break;

            case Id3v2FrameId.Year:
                if (tags.Year is null)
                {
                    var yearStr = ReadTextFrame(data);
                    if (yearStr.Length >= 4 && int.TryParse(yearStr.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
                        tags.Year = year;
                }
                break;

            case Id3v2FrameId.TrackNumber:
                if (tags.TrackNumber is null)
                {
                    // Lenient: a malformed part does not discard the part that could be read.
                    _ = TagFieldMapping.TryParseNumberPair(ReadTextFrame(data), out var number, out var total);
                    tags.TrackNumber = number;
                    tags.TrackTotal ??= total;
                }
                break;

            case Id3v2FrameId.DiscNumber:
                if (tags.DiscNumber is null)
                {
                    _ = TagFieldMapping.TryParseNumberPair(ReadTextFrame(data), out var number, out var total);
                    tags.DiscNumber = number;
                    tags.DiscTotal ??= total;
                }
                break;

            case Id3v2FrameId.Composer:
                tags.Composer ??= ReadTextFrame(data);
                break;

            case Id3v2FrameId.Conductor:
                tags.Conductor ??= ReadTextFrame(data);
                break;

            case Id3v2FrameId.Copyright:
                tags.Copyright ??= ReadTextFrame(data);
                break;

            case Id3v2FrameId.Bpm:
                if (tags.Bpm is null)
                {
                    var bpmStr = ReadTextFrame(data);
                    if (int.TryParse(bpmStr, NumberStyles.None, CultureInfo.InvariantCulture, out var bpm))
                        tags.Bpm = bpm;
                }
                break;

            case Id3v2FrameId.Duration:
                if (tags.Duration is null)
                    ParseDuration(ReadTextFrame(data), tags);
                break;

            case Id3v2FrameId.Isrc:
                tags.Isrc ??= ReadTextFrame(data);
                break;

            case Id3v2FrameId.Compilation:
                if (tags.IsCompilation is null)
                {
                    var compStr = ReadTextFrame(data);
                    tags.IsCompilation = compStr == "1";
                }
                break;

            case Id3v2FrameId.Comment:
                if (tags.Comment is null && TryReadDescribedText(data, out _, out var comment))
                    tags.Comment = comment;
                break;

            case Id3v2FrameId.Lyrics:
                if (tags.Lyrics is null && TryReadDescribedText(data, out _, out var lyrics))
                    tags.Lyrics = lyrics;
                break;

            case Id3v2FrameId.Picture:
                ReadPictureFrame(data, tags);
                break;

            case Id3v2FrameId.UserDefinedText:
                ReadUserDefinedTextFrame(data, tags);
                break;
        }
    }

    private static string ReadTextFrame(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
            return string.Empty;

        var encoding = data[0];
        return Id3v2TextEncoding.DecodeString(encoding, data[1..]);
    }

    /// <summary>
    /// Reads a COMM or USLT frame: encoding(1) + language(3) + description(null-terminated) + text.
    /// </summary>
    private static bool TryReadDescribedText(ReadOnlySpan<byte> data, out string description, out string text)
    {
        description = string.Empty;
        text = string.Empty;

        if (data.Length < 4)
            return false;

        var encoding = data[0];

        // Skip language (3 bytes)
        var remaining = data[4..];

        // Find null terminator for the description
        var nullPos = Id3v2TextEncoding.FindNullTerminator(remaining, encoding, 0);
        if (nullPos < 0)
        {
            text = Id3v2TextEncoding.DecodeString(encoding, remaining);
            return true;
        }

        description = Id3v2TextEncoding.DecodeString(encoding, remaining[..nullPos]);
        var textStart = nullPos + Id3v2TextEncoding.NullTerminatorSize(encoding);
        if (textStart < remaining.Length)
            text = Id3v2TextEncoding.DecodeString(encoding, remaining[textStart..]);

        return true;
    }

    private static void ReadPictureFrame(ReadOnlySpan<byte> data, MediaTagInfo tags)
    {
        // APIC frame: encoding(1) + MIME type(null-terminated) + picture type(1) + description(null-terminated) + picture data
        if (data.Length < 4)
            return;

        var encoding = data[0];
        var pos = 1;

        // Read MIME type (always Latin-1, null-terminated)
        var mimeEnd = data[pos..].IndexOf((byte)0);
        if (mimeEnd < 0)
            return;

        var mimeType = System.Text.Encoding.ASCII.GetString(data.Slice(pos, mimeEnd));
        pos += mimeEnd + 1;

        if (pos >= data.Length)
            return;

        // Picture type
        var pictureType = (MediaPictureType)data[pos];
        pos++;

        // Description (null-terminated, using frame encoding)
        var descNullPos = Id3v2TextEncoding.FindNullTerminator(data, encoding, pos);
        string description;
        if (descNullPos < 0)
            return;

        description = Id3v2TextEncoding.DecodeString(encoding, data[pos..descNullPos]);
        pos = descNullPos + Id3v2TextEncoding.NullTerminatorSize(encoding);

        if (pos >= data.Length)
            return;

        // Picture data
        var pictureData = data[pos..].ToArray();

        tags.Pictures.Add(new MediaPicture
        {
            PictureType = pictureType,
            MimeType = mimeType,
            Description = description,
            Data = pictureData,
        });
    }

    private static void ReadPictureFrameV22(ReadOnlySpan<byte> data, MediaTagInfo tags)
    {
        // PIC frame: encoding(1) + image format(3 characters) + picture type(1) + description(null-terminated) + picture data
        if (data.Length < 6)
            return;

        var encoding = data[0];
        var imageFormat = System.Text.Encoding.ASCII.GetString(data.Slice(1, 3));
        var pictureType = (MediaPictureType)data[4];
        var pos = 5;

        var descNullPos = Id3v2TextEncoding.FindNullTerminator(data, encoding, pos);
        if (descNullPos < 0)
            return;

        var description = Id3v2TextEncoding.DecodeString(encoding, data[pos..descNullPos]);
        pos = descNullPos + Id3v2TextEncoding.NullTerminatorSize(encoding);

        if (pos >= data.Length)
            return;

        tags.Pictures.Add(new MediaPicture
        {
            PictureType = pictureType,
            MimeType = GetMimeTypeFromV22ImageFormat(imageFormat),
            Description = description,
            Data = data[pos..].ToArray(),
        });
    }

    private static string GetMimeTypeFromV22ImageFormat(string imageFormat)
    {
        if (string.Equals(imageFormat, "PNG", StringComparison.OrdinalIgnoreCase))
            return "image/png";

        if (string.Equals(imageFormat, "GIF", StringComparison.OrdinalIgnoreCase))
            return "image/gif";

        if (string.Equals(imageFormat, "BMP", StringComparison.OrdinalIgnoreCase))
            return "image/bmp";

        return "image/jpeg";
    }

    private static void ReadUserDefinedTextFrame(ReadOnlySpan<byte> data, MediaTagInfo tags)
    {
        // TXXX frame: encoding(1) + description(null-terminated) + value
        if (data.Length < 2)
            return;

        var encoding = data[0];
        var remaining = data[1..];

        var nullPos = Id3v2TextEncoding.FindNullTerminator(remaining, encoding, 0);
        if (nullPos < 0)
            return;

        var description = Id3v2TextEncoding.DecodeString(encoding, remaining[..nullPos]);
        var textStart = nullPos + Id3v2TextEncoding.NullTerminatorSize(encoding);

        var value = textStart < remaining.Length
            ? Id3v2TextEncoding.DecodeString(encoding, remaining[textStart..])
            : string.Empty;

        // Map well-known TXXX descriptions
        if (string.Equals(description, "comment", StringComparison.OrdinalIgnoreCase))
        {
            tags.Comment ??= value;
        }
        else if (!TagFieldMapping.TryApplySharedField(description, value, tags))
        {
            tags.CustomFields.TryAdd(description, value);
        }
    }

    private static void ParseDuration(string value, MediaTagInfo tags)
    {
        // TimeSpan cannot represent every value a long can hold, and one bad frame must not fail the whole read.
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds) && milliseconds >= 0 && milliseconds <= TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMillisecond)
            tags.Duration = TimeSpan.FromMilliseconds(milliseconds);
    }

    private static string ParseGenre(string genre)
    {
        // ID3v2 genre can be "(12)" for index, "(12)Rock" for index+text, or just text
        if (genre.Length >= 3 && genre[0] == '(' && genre.IndexOf(')', StringComparison.Ordinal) is var closeIdx and > 0)
        {
            var indexStr = genre.AsSpan(1, closeIdx - 1);
            if (byte.TryParse(indexStr, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                var remainder = genre[(closeIdx + 1)..];
                if (!string.IsNullOrEmpty(remainder))
                    return remainder;

                return Id3v1Genres.GetGenre(index) ?? genre;
            }
        }

        return genre;
    }

    private static string ConvertV22ToV24FrameId(string v22Id) => v22Id switch
    {
        Id3v2FrameId.TitleV22 => Id3v2FrameId.Title,
        Id3v2FrameId.ArtistV22 => Id3v2FrameId.Artist,
        Id3v2FrameId.AlbumV22 => Id3v2FrameId.Album,
        Id3v2FrameId.AlbumArtistV22 => Id3v2FrameId.AlbumArtist,
        Id3v2FrameId.GenreV22 => Id3v2FrameId.Genre,
        Id3v2FrameId.YearV22 => Id3v2FrameId.Year,
        Id3v2FrameId.TrackNumberV22 => Id3v2FrameId.TrackNumber,
        Id3v2FrameId.DiscNumberV22 => Id3v2FrameId.DiscNumber,
        Id3v2FrameId.ComposerV22 => Id3v2FrameId.Composer,
        Id3v2FrameId.ConductorV22 => Id3v2FrameId.Conductor,
        Id3v2FrameId.CopyrightV22 => Id3v2FrameId.Copyright,
        Id3v2FrameId.BpmV22 => Id3v2FrameId.Bpm,
        Id3v2FrameId.DurationV22 => Id3v2FrameId.Duration,
        Id3v2FrameId.IsrcV22 => Id3v2FrameId.Isrc,
        Id3v2FrameId.CommentV22 => Id3v2FrameId.Comment,
        Id3v2FrameId.LyricsV22 => Id3v2FrameId.Lyrics,
        Id3v2FrameId.PictureV22 => Id3v2FrameId.Picture,
        Id3v2FrameId.UserDefinedTextV22 => Id3v2FrameId.UserDefinedText,
        _ => v22Id,
    };

    /// <summary>
    /// Removes the zero bytes inserted after every 0xFF, in place. The result is never longer than the input,
    /// so it is compacted into the same buffer rather than copied into new ones.
    /// </summary>
    /// <returns>The length of the data after unsynchronisation.</returns>
    private static int UndoUnsynchronisation(Span<byte> data)
    {
        var write = 0;
        for (var read = 0; read < data.Length; read++)
        {
            data[write++] = data[read];
            if (data[read] == 0xFF && read + 1 < data.Length && data[read + 1] == 0x00)
            {
                read++; // Skip the inserted 0x00
            }
        }

        return write;
    }
}
