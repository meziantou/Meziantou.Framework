using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Id3v1;

internal static class Id3v1Writer
{
    private const int TagSize = 128;

    public static byte[] BuildTag(MediaTagInfo tags)
    {
        var buffer = new byte[TagSize];

        // "TAG" magic
        buffer[0] = (byte)'T';
        buffer[1] = (byte)'A';
        buffer[2] = (byte)'G';

        WriteFixedString(buffer.AsSpan(3, 30), tags.Title);
        WriteFixedString(buffer.AsSpan(33, 30), tags.Artist);
        WriteFixedString(buffer.AsSpan(63, 30), tags.Album);
        WriteFixedString(buffer.AsSpan(93, 4), tags.Year?.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));

        // ID3v1.1: comment (28 bytes) + zero byte + track number
        if (tags.TrackNumber is not null and > 0 and <= 255)
        {
            WriteFixedString(buffer.AsSpan(97, 28), tags.Comment);
            buffer[125] = 0;
            buffer[126] = (byte)tags.TrackNumber.Value;
        }
        else
        {
            WriteFixedString(buffer.AsSpan(97, 30), tags.Comment);
        }

        // Genre
        if (tags.Genre is not null)
        {
            var genreIndex = Id3v1Genres.GetGenreIndex(tags.Genre);
            buffer[127] = genreIndex ?? 0xFF;
        }
        else
        {
            buffer[127] = 0xFF;
        }

        return buffer;
    }

    public static bool HasId3v1Tag(Stream stream)
    {
        if (stream.Length < TagSize)
            return false;

        stream.Seek(-TagSize, SeekOrigin.End);
        Span<byte> magic = stackalloc byte[3];
        if (stream.ReadAtLeast(magic, 3, throwOnEndOfStream: false) < 3)
            return false;

        return magic[0] == 'T' && magic[1] == 'A' && magic[2] == 'G';
    }

    private static void WriteFixedString(Span<byte> destination, string? value)
    {
        destination.Clear();
        if (string.IsNullOrEmpty(value))
            return;

        var bytes = Latin1Encoding.GetBytes(value);
        var length = Math.Min(bytes.Length, destination.Length);
        bytes.AsSpan(0, length).CopyTo(destination);
    }
}
