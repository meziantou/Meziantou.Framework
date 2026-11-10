namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Specifies the media file format.
/// </summary>
public enum MediaFormat
{
    /// <summary>MPEG Audio Layer III with ID3v1/v2 tags, and APEv2 tags on read.</summary>
    Mp3,

    /// <summary>OGG container with Vorbis audio and Vorbis Comments.</summary>
    OggVorbis,

    /// <summary>OGG container with Opus audio and Vorbis Comments.</summary>
    OggOpus,

    /// <summary>Free Lossless Audio Codec with Vorbis Comments and PICTURE metadata blocks.</summary>
    Flac,

    /// <summary>MPEG-4 and QuickTime containers (M4A/MP4/M4V/MOV), audio or video, with iTunes-style metadata atoms.</summary>
    Mp4,

    /// <summary>Waveform Audio File Format (RIFF container) with LIST/INFO or ID3v2 tags.</summary>
    Wav,

    /// <summary>Audio Interchange File Format with ID3v2 tags.</summary>
    Aiff,

    /// <summary>Monkey's Audio (APE) with APEv2 tags.</summary>
    MonkeysAudio,

    /// <summary>WavPack with APEv2 tags.</summary>
    WavPack,

    /// <summary>Musepack (stream versions 7 and 8) with APEv2 tags.</summary>
    Musepack,

    /// <summary>Advanced Systems Format (WMA/WMV) with ASF attributes.</summary>
    Asf,
}
