using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.MediaTags.Formats.Id3v1;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

internal static class Id3v2Reader
{
    public static bool TryReadTag(Stream stream, MediaTagInfo tags)
    {
        var originalPosition = stream.Position;

        // Read header
        Span<byte> headerBytes = stackalloc byte[10];
        if (stream.ReadAtLeast(headerBytes, 10, throwOnEndOfStream: false) < 10)
        {
            stream.Position = originalPosition;
            return false;
        }

        if (!Id3v2Header.TryParse(headerBytes, out var header))
        {
            stream.Position = originalPosition;
            return false;
        }

        // Read tag data
        var tagData = new byte[header.TagSize];
        if (stream.ReadAtLeast(tagData, header.TagSize, throwOnEndOfStream: false) < header.TagSize)
        {
            stream.Position = originalPosition;
            return false;
        }

        // Undo unsynchronisation if needed
        ReadOnlySpan<byte> data = header.Unsynchronisation
            ? UndoUnsynchronisation(tagData)
            : tagData;

        var offset = 0;

        // Skip extended header if present
        if (header.ExtendedHeader)
        {
            if (data.Length < offset + 4)
                return true;

            int extHeaderSize;
            if (header.MajorVersion == 4)
            {
                extHeaderSize = SynchsafeInteger.Decode(data.Slice(offset, 4));
            }
            else
            {
                extHeaderSize = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4)) + 4;
            }
            offset += extHeaderSize;
        }

        // Parse frames
        while (offset < data.Length)
        {
            if (header.MajorVersion == 2)
            {
                if (!TryReadFrameV22(data, ref offset, tags))
                    break;
            }
            else
            {
                if (!TryReadFrameV23V24(data, ref offset, header.MajorVersion, tags))
                    break;
            }
        }

        return true;
    }

    public static int GetTagSize(Stream stream)
    {
        var originalPosition = stream.Position;
        Span<byte> headerBytes = stackalloc byte[10];
        if (stream.ReadAtLeast(headerBytes, 10, throwOnEndOfStream: false) < 10)
        {
            stream.Position = originalPosition;
            return 0;
        }

        if (!Id3v2Header.TryParse(headerBytes, out var header))
        {
            stream.Position = originalPosition;
            return 0;
        }

        stream.Position = originalPosition;
        return 10 + header.TagSize + (header.FooterPresent ? 10 : 0);
    }

    private static bool TryReadFrameV22(ReadOnlySpan<byte> data, ref int offset, MediaTagInfo tags)
    {
        // ID3v2.2 frame: 3-byte ID + 3-byte size
        if (offset + 6 > data.Length)
            return false;

        var frameId = System.Text.Encoding.ASCII.GetString(data.Slice(offset, 3));
        if (frameId[0] == '\0')
            return false; // Padding

        var frameSize = (data[offset + 3] << 16) | (data[offset + 4] << 8) | data[offset + 5];
        offset += 6;

        if (frameSize <= 0 || offset + frameSize > data.Length)
            return false;

        var frameData = data.Slice(offset, frameSize);
        offset += frameSize;

        ProcessFrame(ConvertV22ToV24FrameId(frameId), frameData, tags);
        return true;
    }

    private static bool TryReadFrameV23V24(ReadOnlySpan<byte> data, ref int offset, byte version, MediaTagInfo tags)
    {
        // ID3v2.3/v2.4 frame: 4-byte ID + 4-byte size + 2-byte flags
        if (offset + 10 > data.Length)
            return false;

        var frameId = System.Text.Encoding.ASCII.GetString(data.Slice(offset, 4));
        if (frameId[0] == '\0')
            return false; // Padding

        int frameSize;
        if (version == 4)
        {
            frameSize = SynchsafeInteger.Decode(data.Slice(offset + 4, 4));
        }
        else
        {
            frameSize = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset + 4, 4));
        }

        // var flags = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 8, 2));
        offset += 10;

        if (frameSize <= 0 || offset + frameSize > data.Length)
            return false;

        var frameData = data.Slice(offset, frameSize);
        offset += frameSize;

        // Convert v2.3 year frame to v2.4 equivalent
        var normalizedFrameId = frameId == Id3v2FrameId.YearV23 ? Id3v2FrameId.Year : frameId;
        ProcessFrame(normalizedFrameId, frameData, tags);
        return true;
    }

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
                    ParseTrackNumber(ReadTextFrame(data), tags);
                break;

            case Id3v2FrameId.DiscNumber:
                if (tags.DiscNumber is null)
                    ParseDiscNumberPair(ReadTextFrame(data), tags);
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

            case Id3v2FrameId.Compilation:
                if (tags.IsCompilation is null)
                {
                    var compStr = ReadTextFrame(data);
                    tags.IsCompilation = compStr == "1";
                }
                break;

            case Id3v2FrameId.Comment:
                tags.Comment ??= ReadCommentFrame(data);
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

    private static string? ReadCommentFrame(ReadOnlySpan<byte> data)
    {
        // COMM frame: encoding(1) + language(3) + short description(null-terminated) + text
        if (data.Length < 4)
            return null;

        var encoding = data[0];
        // Skip language (3 bytes)
        var remaining = data[4..];

        // Find null terminator for short description
        var nullPos = Id3v2TextEncoding.FindNullTerminator(remaining, encoding, 0);
        if (nullPos < 0)
            return Id3v2TextEncoding.DecodeString(encoding, remaining);

        var textStart = nullPos + Id3v2TextEncoding.NullTerminatorSize(encoding);
        if (textStart >= remaining.Length)
            return string.Empty;

        return Id3v2TextEncoding.DecodeString(encoding, remaining[textStart..]);
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
        else if (string.Equals(description, "REPLAYGAIN_TRACK_GAIN", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseReplayGainValue(value, out var gain))
                tags.ReplayGain = (tags.ReplayGain ?? default) with { TrackGain = gain };
        }
        else if (string.Equals(description, "REPLAYGAIN_TRACK_PEAK", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var peak))
                tags.ReplayGain = (tags.ReplayGain ?? default) with { TrackPeak = peak };
        }
        else if (string.Equals(description, "REPLAYGAIN_ALBUM_GAIN", StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseReplayGainValue(value, out var gain))
                tags.ReplayGain = (tags.ReplayGain ?? default) with { AlbumGain = gain };
        }
        else if (string.Equals(description, "REPLAYGAIN_ALBUM_PEAK", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var peak))
                tags.ReplayGain = (tags.ReplayGain ?? default) with { AlbumPeak = peak };
        }
        else if (string.Equals(description, "MusicBrainz Track Id", StringComparison.OrdinalIgnoreCase))
        {
            tags.MusicBrainzTrackId ??= value;
        }
        else if (string.Equals(description, "MusicBrainz Artist Id", StringComparison.OrdinalIgnoreCase))
        {
            tags.MusicBrainzArtistId ??= value;
        }
        else if (string.Equals(description, "MusicBrainz Album Id", StringComparison.OrdinalIgnoreCase))
        {
            tags.MusicBrainzAlbumId ??= value;
        }
        else if (string.Equals(description, "MusicBrainz Release Group Id", StringComparison.OrdinalIgnoreCase))
        {
            tags.MusicBrainzReleaseGroupId ??= value;
        }
        else
        {
            tags.CustomFields.TryAdd(description, value);
        }
    }

    private static bool TryParseReplayGainValue(string value, out double result)
    {
        // ReplayGain values are like "+1.23 dB" or "-4.56 dB"
        var trimmed = value.AsSpan().Trim();
        if (trimmed.EndsWith(" dB", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3].Trim();

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
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

    internal static void ParseTrackNumber(string value, MediaTagInfo tags)
    {
        var slashIdx = value.IndexOf('/', StringComparison.Ordinal);
        if (slashIdx >= 0)
        {
            if (int.TryParse(value.AsSpan(0, slashIdx), NumberStyles.None, CultureInfo.InvariantCulture, out var num))
                tags.TrackNumber = num;
            if (int.TryParse(value.AsSpan(slashIdx + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var total))
                tags.TrackTotal = total;
        }
        else
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var num))
                tags.TrackNumber = num;
        }
    }

    private static void ParseDiscNumberPair(string value, MediaTagInfo tags)
    {
        var slashIdx = value.IndexOf('/', StringComparison.Ordinal);
        if (slashIdx >= 0)
        {
            if (int.TryParse(value.AsSpan(0, slashIdx), NumberStyles.None, CultureInfo.InvariantCulture, out var num))
                tags.DiscNumber = num;
            if (int.TryParse(value.AsSpan(slashIdx + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var total))
                tags.DiscTotal = total;
        }
        else
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var num))
                tags.DiscNumber = num;
        }
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
        Id3v2FrameId.CommentV22 => Id3v2FrameId.Comment,
        Id3v2FrameId.PictureV22 => Id3v2FrameId.Picture,
        Id3v2FrameId.UserDefinedTextV22 => Id3v2FrameId.UserDefinedText,
        _ => v22Id,
    };

    private static byte[] UndoUnsynchronisation(byte[] data)
    {
        var result = new List<byte>(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            result.Add(data[i]);
            if (data[i] == 0xFF && i + 1 < data.Length && data[i + 1] == 0x00)
            {
                i++; // Skip the inserted 0x00
            }
        }
        return result.ToArray();
    }
}
