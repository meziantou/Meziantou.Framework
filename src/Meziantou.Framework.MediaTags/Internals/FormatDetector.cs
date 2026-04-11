namespace Meziantou.Framework.MediaTags.Internals;

internal static class FormatDetector
{
    // Minimum bytes needed for format detection (need enough for OGG page header + Opus/Vorbis identification)
    internal const int MinHeaderSize = 64;

    /// <summary>
    /// Detects the media format from the first bytes of the file.
    /// </summary>
    public static MediaFormat? DetectFromHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4)
            return null;

        // FLAC: "fLaC"
        if (header[0] == 'f' && header[1] == 'L' && header[2] == 'a' && header[3] == 'C')
            return MediaFormat.Flac;

        // OGG: "OggS"
        if (header[0] == 'O' && header[1] == 'g' && header[2] == 'g' && header[3] == 'S')
            return DetectOggSubFormat(header);

        // RIFF/WAV: "RIFF" + size + "WAVE"
        if (header.Length >= 12 && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
            && header[8] == 'W' && header[9] == 'A' && header[10] == 'V' && header[11] == 'E')
            return MediaFormat.Wav;

        // AIFF: "FORM" + size + "AIFF" or "AIFC"
        if (header.Length >= 12 && header[0] == 'F' && header[1] == 'O' && header[2] == 'R' && header[3] == 'M'
            && header[8] == 'A' && header[9] == 'I' && header[10] == 'F' && (header[11] == 'F' || header[11] == 'C'))
            return MediaFormat.Aiff;

        // MP4/M4A: check for ftyp box
        if (header.Length >= 8 && header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p')
            return MediaFormat.Mp4;

        // ID3v2 tag: "ID3" — this is MP3
        if (header[0] == 'I' && header[1] == 'D' && header[2] == '3')
            return MediaFormat.Mp3;

        // MP3 frame sync: 0xFF 0xFB, 0xFF 0xFA, 0xFF 0xF3, 0xFF 0xF2, 0xFF 0xE3, 0xFF 0xE2
        // The first 11 bits should be all 1s (sync word)
        if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
            return MediaFormat.Mp3;

        return null;
    }

    /// <summary>
    /// Detects the media format from a file extension.
    /// </summary>
    public static MediaFormat? DetectFromExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.ToLowerInvariant() switch
        {
            ".mp3" => MediaFormat.Mp3,
            ".ogg" or ".oga" => MediaFormat.OggVorbis,
            ".opus" => MediaFormat.OggOpus,
            ".flac" => MediaFormat.Flac,
            ".m4a" or ".mp4" or ".m4b" or ".m4p" or ".m4r" => MediaFormat.Mp4,
            ".wav" or ".wave" => MediaFormat.Wav,
            ".aif" or ".aiff" or ".aifc" => MediaFormat.Aiff,
            _ => null,
        };
    }

    private static MediaFormat? DetectOggSubFormat(ReadOnlySpan<byte> header)
    {
        // Need to look at the first page's payload to determine codec
        // OGG page header is at least 27 bytes + segment table
        if (header.Length < 28)
            return MediaFormat.OggVorbis; // Default assumption

        var numSegments = header[26];
        var headerSize = 27 + numSegments;

        if (header.Length < headerSize + 8)
            return MediaFormat.OggVorbis;

        var payload = header[headerSize..];

        // Opus: "OpusHead"
        if (payload.Length >= 8
            && payload[0] == 'O' && payload[1] == 'p' && payload[2] == 'u' && payload[3] == 's'
            && payload[4] == 'H' && payload[5] == 'e' && payload[6] == 'a' && payload[7] == 'd')
            return MediaFormat.OggOpus;

        // Vorbis: "\x01vorbis", or default to OggVorbis for unknown OGG streams
        return MediaFormat.OggVorbis;
    }
}
