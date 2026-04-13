using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.MediaTags.Formats.VorbisComment;

internal static class VorbisCommentReader
{
    /// <summary>
    /// Parses Vorbis Comments from a span of bytes.
    /// Format: vendor string length (LE uint32) + vendor string + comment count (LE uint32) + comments
    /// Each comment: length (LE uint32) + "FIELD=value" UTF-8 string
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> data, MediaTagInfo tags)
    {
        if (data.Length < 4)
            return false;

        var offset = 0;

        // Vendor string
        var vendorLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
        if (offset + vendorLength > data.Length)
            return false;
        offset += vendorLength; // Skip vendor string

        // Comment count
        if (offset + 4 > data.Length)
            return false;
        var commentCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;

        for (var i = 0; i < commentCount; i++)
        {
            if (offset + 4 > data.Length)
                break;

            var commentLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;

            if (commentLength < 0 || offset + commentLength > data.Length)
                break;

            var comment = Encoding.UTF8.GetString(data.Slice(offset, commentLength));
            offset += commentLength;

            var eqIdx = comment.IndexOf('=', StringComparison.Ordinal);
            if (eqIdx < 0)
                continue;

            var fieldName = comment[..eqIdx];
            var value = comment[(eqIdx + 1)..];

            ProcessField(fieldName, value, tags);
        }

        return true;
    }

    private static void ProcessField(string fieldName, string value, MediaTagInfo tags)
    {
        if (string.Equals(fieldName, VorbisCommentFieldNames.Title, StringComparison.OrdinalIgnoreCase))
            tags.Title ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Artist, StringComparison.OrdinalIgnoreCase))
            tags.Artist ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Album, StringComparison.OrdinalIgnoreCase))
            tags.Album ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.AlbumArtist, StringComparison.OrdinalIgnoreCase))
            tags.AlbumArtist ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Genre, StringComparison.OrdinalIgnoreCase))
            tags.Genre ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Date, StringComparison.OrdinalIgnoreCase))
        {
            if (tags.Year is null && value.Length >= 4 && int.TryParse(value.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
                tags.Year = year;
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.TrackNumber, StringComparison.OrdinalIgnoreCase))
        {
            if (tags.TrackNumber is null && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var num))
                tags.TrackNumber = num;
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.TrackTotal, StringComparison.OrdinalIgnoreCase))
        {
            if (tags.TrackTotal is null && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var total))
                tags.TrackTotal = total;
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.DiscNumber, StringComparison.OrdinalIgnoreCase))
        {
            if (tags.DiscNumber is null && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var num))
                tags.DiscNumber = num;
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.DiscTotal, StringComparison.OrdinalIgnoreCase))
        {
            if (tags.DiscTotal is null && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var total))
                tags.DiscTotal = total;
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Comment, StringComparison.OrdinalIgnoreCase)
              || string.Equals(fieldName, VorbisCommentFieldNames.Description, StringComparison.OrdinalIgnoreCase))
            tags.Comment ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Lyrics, StringComparison.OrdinalIgnoreCase)
              || string.Equals(fieldName, VorbisCommentFieldNames.UnsyncedLyrics, StringComparison.OrdinalIgnoreCase))
            tags.Lyrics ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Isrc, StringComparison.OrdinalIgnoreCase))
            tags.Isrc ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Composer, StringComparison.OrdinalIgnoreCase))
            tags.Composer ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Conductor, StringComparison.OrdinalIgnoreCase))
            tags.Conductor ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Copyright, StringComparison.OrdinalIgnoreCase))
            tags.Copyright ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Bpm, StringComparison.OrdinalIgnoreCase))
        {
            if (tags.Bpm is null && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var bpm))
                tags.Bpm = bpm;
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Compilation, StringComparison.OrdinalIgnoreCase))
            tags.IsCompilation ??= value == "1";
        else if (string.Equals(fieldName, VorbisCommentFieldNames.ReplayGainTrackGain, StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseReplayGainValue(value, out var gain))
                tags.ReplayGain = (tags.ReplayGain ?? default) with { TrackGain = gain };
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.ReplayGainTrackPeak, StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var peak))
                tags.ReplayGain = (tags.ReplayGain ?? default) with { TrackPeak = peak };
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.ReplayGainAlbumGain, StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseReplayGainValue(value, out var gain))
                tags.ReplayGain = (tags.ReplayGain ?? default) with { AlbumGain = gain };
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.ReplayGainAlbumPeak, StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var peak))
                tags.ReplayGain = (tags.ReplayGain ?? default) with { AlbumPeak = peak };
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.MusicBrainzTrackId, StringComparison.OrdinalIgnoreCase))
            tags.MusicBrainzTrackId ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.MusicBrainzArtistId, StringComparison.OrdinalIgnoreCase))
            tags.MusicBrainzArtistId ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.MusicBrainzAlbumId, StringComparison.OrdinalIgnoreCase))
            tags.MusicBrainzAlbumId ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.MusicBrainzReleaseGroupId, StringComparison.OrdinalIgnoreCase))
            tags.MusicBrainzReleaseGroupId ??= value;
        else if (string.Equals(fieldName, VorbisCommentFieldNames.MetadataBlockPicture, StringComparison.OrdinalIgnoreCase))
            TryParseMetadataBlockPicture(value, tags);
        else
            tags.CustomFields.TryAdd(fieldName, value);
    }

    private static bool TryParseReplayGainValue(string value, out double result)
    {
        var trimmed = value.AsSpan().Trim();
        if (trimmed.EndsWith(" dB", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3].Trim();

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static void TryParseMetadataBlockPicture(string base64Value, MediaTagInfo tags)
    {
        try
        {
            var data = Convert.FromBase64String(base64Value);
            Flac.FlacPictureBlock.TryParse(data, tags);
        }
        catch (FormatException)
        {
            // Invalid base64
        }
    }
}
