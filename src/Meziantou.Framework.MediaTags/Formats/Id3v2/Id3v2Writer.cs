using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

internal static class Id3v2Writer
{
    /// <summary>The largest value a synchsafe tag size can hold.</summary>
    private const int MaxTagSize = 0x0FFFFFFF;

    /// <summary>
    /// Builds an ID3v2.4 tag.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the tags do not fit in an ID3v2 tag. Truncating the size field instead
    /// would produce a file that reads back as corrupt.
    /// </returns>
    public static bool TryBuildTag(MediaTagInfo tags, int paddingSize, [NotNullWhen(true)] out byte[]? tag, out string? errorMessage)
    {
        tag = null;
        errorMessage = null;

        var frames = new List<byte[]>();

        AddTextFrame(frames, Id3v2FrameId.Title, tags.Title);
        AddTextFrame(frames, Id3v2FrameId.Artist, tags.Artist);
        AddTextFrame(frames, Id3v2FrameId.Album, tags.Album);
        AddTextFrame(frames, Id3v2FrameId.AlbumArtist, tags.AlbumArtist);

        if (tags.Genre is not null)
            AddTextFrame(frames, Id3v2FrameId.Genre, tags.Genre);

        if (tags.Year is not null)
            AddTextFrame(frames, Id3v2FrameId.Year, tags.Year.Value.ToString("D4", CultureInfo.InvariantCulture));

        // The number and the total share one frame, so writing the frame only when the number is set would
        // discard a total that was supplied on its own.
        AddTextFrame(frames, Id3v2FrameId.TrackNumber, FormatNumberPair(tags.TrackNumber, tags.TrackTotal));
        AddTextFrame(frames, Id3v2FrameId.DiscNumber, FormatNumberPair(tags.DiscNumber, tags.DiscTotal));

        AddTextFrame(frames, Id3v2FrameId.Composer, tags.Composer);
        AddTextFrame(frames, Id3v2FrameId.Conductor, tags.Conductor);
        AddTextFrame(frames, Id3v2FrameId.Copyright, tags.Copyright);

        if (tags.Bpm is not null)
            AddTextFrame(frames, Id3v2FrameId.Bpm, tags.Bpm.Value.ToString(CultureInfo.InvariantCulture));

        // TLEN is parsed by Id3v2Reader, so not writing it would drop the value on a read-modify-write.
        if (tags.Duration is { } duration && duration > TimeSpan.Zero)
            AddTextFrame(frames, Id3v2FrameId.Duration, ((long)duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));

        AddTextFrame(frames, Id3v2FrameId.Isrc, tags.Isrc);

        if (tags.IsCompilation is not null)
            AddTextFrame(frames, Id3v2FrameId.Compilation, tags.IsCompilation.Value ? "1" : "0");

        if (tags.Comment is not null)
            AddCommentFrame(frames, tags.Comment);

        if (tags.Lyrics is not null)
            AddUnsynchronizedLyricsFrame(frames, tags.Lyrics);

        foreach (var picture in tags.Pictures)
        {
            if (picture.Data.Length > MaxTagSize)
            {
                errorMessage = $"A picture is too large for an ID3v2 frame ({picture.Data.Length} bytes, the maximum is {MaxTagSize}).";
                return false;
            }

            AddPictureFrame(frames, picture);
        }

        foreach (var (key, value) in TagFieldMapping.EnumerateReplayGainFields(tags))
        {
            AddUserDefinedTextFrame(frames, key, value);
        }

        foreach (var (key, value) in TagFieldMapping.EnumerateMusicBrainzFields(tags, useVorbisNames: false))
        {
            AddUserDefinedTextFrame(frames, key, value);
        }

        // Custom fields as TXXX
        foreach (var (key, value) in tags.CustomFields)
        {
            AddUserDefinedTextFrame(frames, key, value);
        }

        // Calculate total frame size
        var totalFrameSize = 0L;
        foreach (var frame in frames)
        {
            totalFrameSize += frame.Length;
        }

        // A caller removing tags asks for no frames and no padding: produce no tag at all rather than an
        // empty one, so the file really does come back untagged.
        if (totalFrameSize == 0 && paddingSize == 0)
        {
            tag = [];
            return true;
        }

        var tagSize = totalFrameSize + paddingSize;
        if (tagSize > MaxTagSize)
        {
            errorMessage = $"The tags are too large for an ID3v2 tag ({tagSize} bytes, the maximum is {MaxTagSize}).";
            return false;
        }

        // Build the complete tag
        var result = new byte[10 + tagSize];

        // Header
        result[0] = (byte)'I';
        result[1] = (byte)'D';
        result[2] = (byte)'3';
        result[3] = 4; // Version 2.4
        result[4] = 0; // Revision
        result[5] = 0; // Flags
        SynchsafeInteger.Encode((int)tagSize, result.AsSpan(6, 4));

        // Write frames
        var offset = 10;
        foreach (var frame in frames)
        {
            frame.CopyTo(result, offset);
            offset += frame.Length;
        }

        // Remaining bytes are already 0 (padding)
        tag = result;
        return true;
    }

    /// <summary>
    /// Formats an ID3v2 "number/total" value. Returns <see langword="null"/> when neither part is set.
    /// </summary>
    private static string? FormatNumberPair(int? number, int? total)
    {
        if (number is null && total is null)
            return null;

        if (total is null)
            return number!.Value.ToString(CultureInfo.InvariantCulture);

        // "0/total" is how the format expresses a known total with an unknown position.
        var numberPart = (number ?? 0).ToString(CultureInfo.InvariantCulture);
        return numberPart + "/" + total.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static void AddTextFrame(List<byte[]> frames, string frameId, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        var textData = Id3v2TextEncoding.EncodeString(value);
        frames.Add(BuildFrame(frameId, textData));
    }

    private static void AddCommentFrame(List<byte[]> frames, string value)
    {
        // COMM frame: encoding(1) + language(3) + short description(null-terminated) + text
        var textBytes = Encoding.UTF8.GetBytes(value);
        var frameData = new byte[1 + 3 + 1 + textBytes.Length]; // encoding + "eng" + null desc + text
        frameData[0] = Id3v2TextEncoding.Utf8;
        frameData[1] = (byte)'e';
        frameData[2] = (byte)'n';
        frameData[3] = (byte)'g';
        frameData[4] = 0; // Empty description null terminator
        textBytes.CopyTo(frameData, 5);
        frames.Add(BuildFrame(Id3v2FrameId.Comment, frameData));
    }

    private static void AddUnsynchronizedLyricsFrame(List<byte[]> frames, string value)
    {
        // USLT frame: encoding(1) + language(3) + content descriptor(null-terminated) + lyrics text
        var textBytes = Encoding.UTF8.GetBytes(value);
        var frameData = new byte[1 + 3 + 1 + textBytes.Length]; // encoding + "eng" + null descriptor + text
        frameData[0] = Id3v2TextEncoding.Utf8;
        frameData[1] = (byte)'e';
        frameData[2] = (byte)'n';
        frameData[3] = (byte)'g';
        frameData[4] = 0; // Empty descriptor null terminator
        textBytes.CopyTo(frameData, 5);
        frames.Add(BuildFrame(Id3v2FrameId.Lyrics, frameData));
    }

    private static void AddPictureFrame(List<byte[]> frames, MediaPicture picture)
    {
        // APIC frame: encoding(1) + MIME(null-terminated) + type(1) + description(null-terminated) + data
        var mimeBytes = Encoding.ASCII.GetBytes(picture.MimeType ?? "image/jpeg");
        var descBytes = Encoding.UTF8.GetBytes(picture.Description ?? "");
        var frameData = new byte[1 + mimeBytes.Length + 1 + 1 + descBytes.Length + 1 + picture.Data.Length];
        var pos = 0;

        frameData[pos++] = Id3v2TextEncoding.Utf8; // encoding
        mimeBytes.CopyTo(frameData, pos);
        pos += mimeBytes.Length;
        frameData[pos++] = 0; // null terminator for MIME
        frameData[pos++] = (byte)picture.PictureType;
        descBytes.CopyTo(frameData, pos);
        pos += descBytes.Length;
        frameData[pos++] = 0; // null terminator for description
        picture.Data.CopyTo(frameData, pos);

        frames.Add(BuildFrame(Id3v2FrameId.Picture, frameData));
    }

    private static void AddUserDefinedTextFrame(List<byte[]> frames, string description, string value)
    {
        // TXXX frame: encoding(1) + description(null-terminated) + value
        var descBytes = Encoding.UTF8.GetBytes(description);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var frameData = new byte[1 + descBytes.Length + 1 + valueBytes.Length];
        frameData[0] = Id3v2TextEncoding.Utf8;
        descBytes.CopyTo(frameData, 1);
        frameData[1 + descBytes.Length] = 0;
        valueBytes.CopyTo(frameData, 1 + descBytes.Length + 1);
        frames.Add(BuildFrame(Id3v2FrameId.UserDefinedText, frameData));
    }

    private static byte[] BuildFrame(string frameId, byte[] data)
    {
        // ID3v2.4 frame: 4-byte ID + 4-byte synchsafe size + 2-byte flags + data
        var frame = new byte[10 + data.Length];
        Encoding.ASCII.GetBytes(frameId, frame.AsSpan(0, 4));
        SynchsafeInteger.Encode(data.Length, frame.AsSpan(4, 4));
        // Flags are 0
        data.CopyTo(frame, 10);
        return frame;
    }
}
