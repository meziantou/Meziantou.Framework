using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

internal static class Id3v2Writer
{
    private const int PaddingSize = 1024;

    public static byte[] BuildTag(MediaTagInfo tags)
    {
        var frames = new List<byte[]>();

        AddTextFrame(frames, Id3v2FrameId.Title, tags.Title);
        AddTextFrame(frames, Id3v2FrameId.Artist, tags.Artist);
        AddTextFrame(frames, Id3v2FrameId.Album, tags.Album);
        AddTextFrame(frames, Id3v2FrameId.AlbumArtist, tags.AlbumArtist);

        if (tags.Genre is not null)
            AddTextFrame(frames, Id3v2FrameId.Genre, tags.Genre);

        if (tags.Year is not null)
            AddTextFrame(frames, Id3v2FrameId.Year, tags.Year.Value.ToString("D4", CultureInfo.InvariantCulture));

        if (tags.TrackNumber is not null)
        {
            var trackStr = tags.TrackTotal is not null
                ? $"{tags.TrackNumber}/{tags.TrackTotal}"
                : tags.TrackNumber.Value.ToString(CultureInfo.InvariantCulture);
            AddTextFrame(frames, Id3v2FrameId.TrackNumber, trackStr);
        }

        if (tags.DiscNumber is not null)
        {
            var discStr = tags.DiscTotal is not null
                ? $"{tags.DiscNumber}/{tags.DiscTotal}"
                : tags.DiscNumber.Value.ToString(CultureInfo.InvariantCulture);
            AddTextFrame(frames, Id3v2FrameId.DiscNumber, discStr);
        }

        AddTextFrame(frames, Id3v2FrameId.Composer, tags.Composer);
        AddTextFrame(frames, Id3v2FrameId.Conductor, tags.Conductor);
        AddTextFrame(frames, Id3v2FrameId.Copyright, tags.Copyright);

        if (tags.Bpm is not null)
            AddTextFrame(frames, Id3v2FrameId.Bpm, tags.Bpm.Value.ToString(CultureInfo.InvariantCulture));

        if (tags.IsCompilation is not null)
            AddTextFrame(frames, Id3v2FrameId.Compilation, tags.IsCompilation.Value ? "1" : "0");

        if (tags.Comment is not null)
            AddCommentFrame(frames, tags.Comment);

        foreach (var picture in tags.Pictures)
        {
            AddPictureFrame(frames, picture);
        }

        // ReplayGain as TXXX
        if (tags.ReplayGain is not null)
        {
            var rg = tags.ReplayGain.Value;
            if (rg.TrackGain is not null)
                AddUserDefinedTextFrame(frames, "REPLAYGAIN_TRACK_GAIN", rg.TrackGain.Value.ToString("F2", CultureInfo.InvariantCulture) + " dB");
            if (rg.TrackPeak is not null)
                AddUserDefinedTextFrame(frames, "REPLAYGAIN_TRACK_PEAK", rg.TrackPeak.Value.ToString("F6", CultureInfo.InvariantCulture));
            if (rg.AlbumGain is not null)
                AddUserDefinedTextFrame(frames, "REPLAYGAIN_ALBUM_GAIN", rg.AlbumGain.Value.ToString("F2", CultureInfo.InvariantCulture) + " dB");
            if (rg.AlbumPeak is not null)
                AddUserDefinedTextFrame(frames, "REPLAYGAIN_ALBUM_PEAK", rg.AlbumPeak.Value.ToString("F6", CultureInfo.InvariantCulture));
        }

        // MusicBrainz as TXXX
        if (tags.MusicBrainzTrackId is not null)
            AddUserDefinedTextFrame(frames, "MusicBrainz Track Id", tags.MusicBrainzTrackId);
        if (tags.MusicBrainzArtistId is not null)
            AddUserDefinedTextFrame(frames, "MusicBrainz Artist Id", tags.MusicBrainzArtistId);
        if (tags.MusicBrainzAlbumId is not null)
            AddUserDefinedTextFrame(frames, "MusicBrainz Album Id", tags.MusicBrainzAlbumId);
        if (tags.MusicBrainzReleaseGroupId is not null)
            AddUserDefinedTextFrame(frames, "MusicBrainz Release Group Id", tags.MusicBrainzReleaseGroupId);

        // Custom fields as TXXX
        foreach (var (key, value) in tags.CustomFields)
        {
            AddUserDefinedTextFrame(frames, key, value);
        }

        // Calculate total frame size
        var totalFrameSize = 0;
        foreach (var frame in frames)
        {
            totalFrameSize += frame.Length;
        }

        // Build the complete tag
        var tagSize = totalFrameSize + PaddingSize;
        var result = new byte[10 + tagSize];

        // Header
        result[0] = (byte)'I';
        result[1] = (byte)'D';
        result[2] = (byte)'3';
        result[3] = 4; // Version 2.4
        result[4] = 0; // Revision
        result[5] = 0; // Flags
        SynchsafeInteger.Encode(tagSize, result.AsSpan(6, 4));

        // Write frames
        var offset = 10;
        foreach (var frame in frames)
        {
            frame.CopyTo(result, offset);
            offset += frame.Length;
        }

        // Remaining bytes are already 0 (padding)
        return result;
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
