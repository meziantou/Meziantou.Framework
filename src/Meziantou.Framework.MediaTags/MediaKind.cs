namespace Meziantou.Framework.MediaTags;

/// <summary>
/// Specifies the kind of media of an MP4 file, as stored in its <c>stik</c> item.
/// </summary>
/// <remarks>Other values are kept as they are read.</remarks>
[SuppressMessage("Design", "CA1028:Enum storage should be Int32", Justification = "The value is stored in one byte")]
public enum MediaKind : byte
{
    /// <summary>A movie, as written by older versions of iTunes.</summary>
    LegacyMovie = 0,

    /// <summary>Music.</summary>
    Music = 1,

    /// <summary>An audiobook.</summary>
    Audiobook = 2,

    /// <summary>A bookmark.</summary>
    Bookmark = 5,

    /// <summary>A music video.</summary>
    MusicVideo = 6,

    /// <summary>A movie.</summary>
    Movie = 9,

    /// <summary>An episode of a TV show.</summary>
    TvShow = 10,

    /// <summary>A booklet.</summary>
    Booklet = 11,

    /// <summary>A ringtone.</summary>
    Ringtone = 14,

    /// <summary>A podcast.</summary>
    Podcast = 21,

    /// <summary>An iTunes U item.</summary>
    ITunesU = 23,
}
