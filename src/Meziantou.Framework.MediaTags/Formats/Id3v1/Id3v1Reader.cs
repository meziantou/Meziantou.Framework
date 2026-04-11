using System.Globalization;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Id3v1;

internal static class Id3v1Reader
{
    private const int TagSize = 128;

    public static bool TryReadTag(Stream stream, MediaTagInfo tags)
    {
        if (stream.Length < TagSize)
            return false;

        stream.Seek(-TagSize, SeekOrigin.End);
        var buffer = new byte[TagSize];
        if (stream.ReadAtLeast(buffer, TagSize, throwOnEndOfStream: false) < TagSize)
            return false;

        return TryParseTag(buffer, tags);
    }

    internal static bool TryParseTag(ReadOnlySpan<byte> data, MediaTagInfo tags)
    {
        if (data.Length < TagSize)
            return false;

        // Check for "TAG" magic
        if (data[0] != 'T' || data[1] != 'A' || data[2] != 'G')
            return false;

        var title = ReadFixedString(data.Slice(3, 30));
        var artist = ReadFixedString(data.Slice(33, 30));
        var album = ReadFixedString(data.Slice(63, 30));
        var year = ReadFixedString(data.Slice(93, 4));
        var comment = data.Slice(97, 30);
        var genreIndex = data[127];

        if (!string.IsNullOrEmpty(title))
            tags.Title ??= title;
        if (!string.IsNullOrEmpty(artist))
            tags.Artist ??= artist;
        if (!string.IsNullOrEmpty(album))
            tags.Album ??= album;

        if (!string.IsNullOrEmpty(year) && int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var yearValue) && yearValue > 0)
            tags.Year ??= yearValue;

        // ID3v1.1: if byte 125 is 0 and byte 126 is non-zero, then byte 126 is the track number
        if (comment[28] == 0 && comment[29] != 0)
        {
            tags.TrackNumber ??= comment[29];
            var commentText = ReadFixedString(comment[..28]);
            if (!string.IsNullOrEmpty(commentText))
                tags.Comment ??= commentText;
        }
        else
        {
            var commentText = ReadFixedString(comment);
            if (!string.IsNullOrEmpty(commentText))
                tags.Comment ??= commentText;
        }

        var genre = Id3v1Genres.GetGenre(genreIndex);
        if (genre is not null)
            tags.Genre ??= genre;

        return true;
    }

    private static string ReadFixedString(ReadOnlySpan<byte> data)
    {
        // Find the end of the string (null-terminated or trailing spaces)
        var length = data.Length;
        while (length > 0 && (data[length - 1] == 0 || data[length - 1] == ' '))
        {
            length--;
        }

        if (length == 0)
            return string.Empty;

        return Latin1Encoding.GetString(data[..length]);
    }
}
