using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

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

        // Vendor string. Lengths are unsigned 32-bit values, so they are compared as such: cast to int, a large
        // one turns negative and passes the bounds check.
        var vendorLength = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;
        if (vendorLength > (uint)(data.Length - offset))
            return false;
        offset += (int)vendorLength; // Skip vendor string

        // Comment count
        if (offset + 4 > data.Length)
            return false;
        var commentCount = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;

        for (var i = 0u; i < commentCount; i++)
        {
            if (offset + 4 > data.Length)
                break;

            var commentLength = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;

            if (commentLength > (uint)(data.Length - offset))
                break;

            var comment = Encoding.UTF8.GetString(data.Slice(offset, (int)commentLength));
            offset += (int)commentLength;

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
        // A value that does not parse is kept as a custom field. Dropping it would delete it from the file on the
        // next write, since the writer rebuilds the comment from MediaTagInfo.
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Date, StringComparison.OrdinalIgnoreCase))
        {
            if (value.Length >= 4 && int.TryParse(value.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
                tags.Year ??= year;
            else
                tags.CustomFields.TryAdd(fieldName, value);
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.TrackNumber, StringComparison.OrdinalIgnoreCase))
        {
            // Some taggers store the total in the same field, as "3/12".
            if (TagFieldMapping.TryParseNumberPair(value, out var number, out var total))
            {
                tags.TrackNumber ??= number;
                tags.TrackTotal ??= total;
            }
            else
            {
                tags.CustomFields.TryAdd(fieldName, value);
            }
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.TrackTotal, StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var total))
                tags.TrackTotal ??= total;
            else
                tags.CustomFields.TryAdd(fieldName, value);
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.DiscNumber, StringComparison.OrdinalIgnoreCase))
        {
            if (TagFieldMapping.TryParseNumberPair(value, out var number, out var total))
            {
                tags.DiscNumber ??= number;
                tags.DiscTotal ??= total;
            }
            else
            {
                tags.CustomFields.TryAdd(fieldName, value);
            }
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.DiscTotal, StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var total))
                tags.DiscTotal ??= total;
            else
                tags.CustomFields.TryAdd(fieldName, value);
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
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var bpm))
                tags.Bpm ??= bpm;
            else
                tags.CustomFields.TryAdd(fieldName, value);
        }
        else if (string.Equals(fieldName, VorbisCommentFieldNames.Compilation, StringComparison.OrdinalIgnoreCase))
            tags.IsCompilation ??= value == "1";
        else if (string.Equals(fieldName, VorbisCommentFieldNames.MetadataBlockPicture, StringComparison.OrdinalIgnoreCase))
            TryParseMetadataBlockPicture(value, tags);
        else if (!TagFieldMapping.TryApplySharedField(fieldName, value, tags))
            tags.CustomFields.TryAdd(fieldName, value);
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
