namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Specifies the tag format a <see cref="MediaNativeField"/> belongs to.
/// </summary>
public enum MediaTagType
{
    /// <summary>An ID3v2 frame, in MP3, WAV and AIFF files.</summary>
    Id3v2,

    /// <summary>An item of the iTunes-style metadata list (<c>ilst</c>) of an MP4 file.</summary>
    Mp4,

    /// <summary>An item of an APEv2 tag, in Monkey's Audio, WavPack and Musepack files.</summary>
    Ape,

    /// <summary>An attribute of an ASF (WMA/WMV) file.</summary>
    Asf,
}
