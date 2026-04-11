namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Specifies the media file format.
/// </summary>
public enum MediaFormat
{
    /// <summary>MPEG Audio Layer III with ID3v1/v2 tags.</summary>
    Mp3,

    /// <summary>OGG container with Vorbis audio and Vorbis Comments.</summary>
    OggVorbis,

    /// <summary>OGG container with Opus audio and Vorbis Comments.</summary>
    OggOpus,

    /// <summary>Free Lossless Audio Codec with Vorbis Comments and PICTURE metadata blocks.</summary>
    Flac,

    /// <summary>MPEG-4 container (M4A/MP4) with iTunes-style metadata atoms.</summary>
    Mp4,

    /// <summary>Waveform Audio File Format (RIFF container) with LIST/INFO or ID3v2 tags.</summary>
    Wav,

    /// <summary>Audio Interchange File Format with ID3v2 tags.</summary>
    Aiff,
}
