namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

/// <summary>
/// Locates the audio of an MP3 file, between the ID3v2 tags at its start and the APE and ID3v1 tags at its end.
/// </summary>
/// <remarks>
/// The reader computes the duration from that audio, and the writer copies exactly that audio between the tags it
/// writes, so both must agree on where it is. Otherwise a write changes the duration that reads back.
/// </remarks>
internal static class Mp3AudioLocator
{
    /// <summary>
    /// Gets the offsets of the audio in a seekable stream whose file starts at offset 0. The stream position is not restored.
    /// </summary>
    /// <returns><see langword="false"/> when the tags are corrupt, and <paramref name="errorMessage"/> tells why.</returns>
    public static bool TryLocate(Stream stream, out long audioStart, out long audioEnd, [NotNullWhen(false)] out string? errorMessage)
    {
        if (TryLocate(stream, endsWithId3v1Tag: true, out audioStart, out audioEnd, out errorMessage, out var tagsOverlap))
            return true;

        // "TAG" in the last 128 bytes of the file, inside the ID3v2 tag, is content of that tag, such as a text frame,
        // not an ID3v1 tag. Otherwise a file whose ID3v2 tag is followed by less than 128 bytes of audio is refused.
        if (tagsOverlap)
            return TryLocate(stream, endsWithId3v1Tag: false, out audioStart, out audioEnd, out errorMessage, out _);

        return false;
    }

    private static bool TryLocate(Stream stream, bool endsWithId3v1Tag, out long audioStart, out long audioEnd, [NotNullWhen(false)] out string? errorMessage, out bool tagsOverlap)
    {
        audioStart = 0;
        errorMessage = null;
        tagsOverlap = false;

        // The ID3v2 header must end before the ID3v1 tag, as a write that rebuilds the ID3v1 tag changes its bytes. APE
        // tags are not considered here: whether a footer is one depends on where the ID3v2 tags end, as a tag at the end of
        // the file cannot start inside them.
        audioEnd = stream.Length;
        if (endsWithId3v1Tag && audioEnd >= Ape.ApeTag.Id3v1TagSize)
        {
            stream.Position = audioEnd - Ape.ApeTag.Id3v1TagSize;
            Span<byte> magic = stackalloc byte[3];
            if (stream.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false) == magic.Length && magic.SequenceEqual("TAG"u8))
                audioEnd -= Ape.ApeTag.Id3v1TagSize;
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
            tagsOverlap = true;
            return false;
        }

        // Some taggers append a new ID3v1 tag without removing the old one, and some add an APE tag before it. Only the
        // last ones are read, but the audio ends before all of them. Otherwise a write that does not write an ID3v1 tag
        // leaves the stale one at the end of the file, where it reads as the ID3v1 tag of the file, and the APE tag,
        // whose values the reader merged into the ID3v2 tag that replaces it, would bring removed values back.
        audioEnd = Ape.ApeTag.FindAudioEnd(stream, GetMinimumTailTagStart(stream, audioStart), stream.Length, out _, out _);
        return true;
    }

    /// <summary>Gets the first offset a tag at the end of the file can start at.</summary>
    /// <remarks>
    /// Audio can start with bytes that begin an ID3v2 header the parser could not read. A tag at the end that left only those
    /// bytes as audio would leave bytes that the bytes written after them can turn into a header.
    /// </remarks>
    public static long GetMinimumTailTagStart(Stream stream, long audioStart)
    {
        stream.Position = audioStart;
        Span<byte> header = stackalloc byte[9];
        var length = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        for (var prefixLength = length; prefixLength >= 3; prefixLength--)
        {
            if (Id3v2Header.IsPossiblePrefix(header[..prefixLength]))
                return audioStart + prefixLength + 1;
        }

        return audioStart;
    }
}
