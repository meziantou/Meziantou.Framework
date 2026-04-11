namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Specifies the type of an embedded picture, as defined by the ID3v2 APIC frame specification.
/// </summary>
public enum MediaPictureType : byte
{
    /// <summary>Other.</summary>
    Other = 0,
    /// <summary>32x32 pixels file icon (PNG only).</summary>
    FileIcon = 1,
    /// <summary>Other file icon.</summary>
    OtherFileIcon = 2,
    /// <summary>Cover (front).</summary>
    FrontCover = 3,
    /// <summary>Cover (back).</summary>
    BackCover = 4,
    /// <summary>Leaflet page.</summary>
    LeafletPage = 5,
    /// <summary>Media (e.g. label side of CD).</summary>
    Media = 6,
    /// <summary>Lead artist/lead performer/soloist.</summary>
    LeadArtist = 7,
    /// <summary>Artist/performer.</summary>
    Artist = 8,
    /// <summary>Conductor.</summary>
    Conductor = 9,
    /// <summary>Band/Orchestra.</summary>
    Band = 10,
    /// <summary>Composer.</summary>
    Composer = 11,
    /// <summary>Lyricist/text writer.</summary>
    Lyricist = 12,
    /// <summary>Recording location.</summary>
    RecordingLocation = 13,
    /// <summary>During recording.</summary>
    DuringRecording = 14,
    /// <summary>During performance.</summary>
    DuringPerformance = 15,
    /// <summary>Movie/video screen capture.</summary>
    MovieScreenCapture = 16,
    /// <summary>A bright coloured fish.</summary>
    BrightColouredFish = 17,
    /// <summary>Illustration.</summary>
    Illustration = 18,
    /// <summary>Band/artist logotype.</summary>
    BandLogo = 19,
    /// <summary>Publisher/Studio logotype.</summary>
    PublisherLogo = 20,
}
