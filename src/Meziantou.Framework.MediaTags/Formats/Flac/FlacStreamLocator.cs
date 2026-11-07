using Meziantou.Framework.MediaTags.Formats.Id3v2;

namespace Meziantou.Framework.MediaTags.Formats.Flac;

/// <summary>
/// Locates the start of the FLAC stream in a file, which is not always the start of the file:
/// some taggers prepend an ID3v2 tag to a FLAC file.
/// </summary>
internal static class FlacStreamLocator
{
    /// <summary>
    /// Finds the offset of the "fLaC" signature. The reader and the writer must agree on this offset,
    /// otherwise the writer parses the ID3v2 tag as metadata blocks and destroys the audio.
    /// </summary>
    public static bool TryGetStreamStart(Stream stream, out long offset)
    {
        offset = 0;

        if (!stream.CanSeek)
            return false;

        if (HasFlacMagicAt(stream, 0))
            return true;

        stream.Position = 0;
        Span<byte> id3Header = stackalloc byte[3];
        if (stream.ReadAtLeast(id3Header, id3Header.Length, throwOnEndOfStream: false) < id3Header.Length)
            return false;

        if (id3Header is not [(byte)'I', (byte)'D', (byte)'3'])
            return false;

        if (!Id3v2TagLocator.TryGetAudioDataOffsets(stream, 0, out var primaryOffset, out var secondaryOffset))
            return false;

        if (HasFlacMagicAt(stream, primaryOffset))
        {
            offset = primaryOffset;
            return true;
        }

        if (secondaryOffset >= 0 && HasFlacMagicAt(stream, secondaryOffset))
        {
            offset = secondaryOffset;
            return true;
        }

        return false;
    }

    private static bool HasFlacMagicAt(Stream stream, long offset)
    {
        if (offset < 0 || stream.Length < offset + 4)
            return false;

        stream.Position = offset;
        Span<byte> magic = stackalloc byte[4];
        return stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) == magic.Length
            && magic is [(byte)'f', (byte)'L', (byte)'a', (byte)'C'];
    }
}
