namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

/// <summary>
/// Locates the audio of an MP3 file, between the ID3v2 tags at its start and the ID3v1 tags at its end.
/// </summary>
/// <remarks>
/// The reader computes the duration from that audio, and the writer copies exactly that audio between the tags it
/// writes, so both must agree on where it is. Otherwise a write changes the duration that reads back.
/// </remarks>
internal static class Mp3AudioLocator
{
    private const int Id3v1TagSize = 128;

    /// <summary>
    /// Gets the offsets of the audio in a seekable stream whose file starts at offset 0. The stream position is not restored.
    /// </summary>
    /// <returns><see langword="false"/> when the tags are corrupt, and <paramref name="errorMessage"/> tells why.</returns>
    public static bool TryLocate(Stream stream, out long audioStart, out long audioEnd, [NotNullWhen(false)] out string? errorMessage)
    {
        audioStart = 0;
        errorMessage = null;

        audioEnd = stream.Length;
        if (HasId3v1TagAt(stream, audioEnd - Id3v1TagSize))
        {
            audioEnd -= Id3v1TagSize;
        }

        // Some taggers prepend a new ID3v2 tag without removing the old one. Only the first is read, but the audio
        // starts after all of them. Otherwise a write leaves a stale tag at the start of the audio, and removing the
        // tags brings it back.
        while (true)
        {
            stream.Position = audioStart;
            if (!Id3v2Reader.TryGetTagSize(stream, audioEnd, out var tagSize))
            {
                errorMessage = "The ID3v2 tag declares a size that runs past the end of the file.";
                return false;
            }

            if (tagSize is 0)
                break;

            audioStart += tagSize;
        }

        if (audioStart > audioEnd)
        {
            errorMessage = "The ID3v2 and ID3v1 tags overlap; the file contains no audio data.";
            return false;
        }

        // Some taggers append a new ID3v1 tag without removing the old one. Only the last is read, but the audio ends
        // before all of them. Otherwise a write that does not write an ID3v1 tag leaves the stale one at the end of
        // the file, where it reads as the ID3v1 tag of the file.
        while (audioEnd - Id3v1TagSize >= audioStart && HasId3v1TagAt(stream, audioEnd - Id3v1TagSize))
        {
            audioEnd -= Id3v1TagSize;
        }

        return true;
    }

    private static bool HasId3v1TagAt(Stream stream, long offset)
    {
        if (offset < 0)
            return false;

        stream.Position = offset;
        Span<byte> magic = stackalloc byte[3];
        if (stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) < magic.Length)
            return false;

        return magic is [(byte)'T', (byte)'A', (byte)'G'];
    }
}
