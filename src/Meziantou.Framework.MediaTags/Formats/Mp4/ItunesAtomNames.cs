namespace Meziantou.Framework.MediaTags.Formats.Mp4;

internal static class ItunesAtomNames
{
    // Standard iTunes metadata atoms
    // Note: \u00A9 is the copyright symbol (©), byte 0xA9 in Latin-1.
    // Do NOT use \xa9 as it is a greedy hex escape in C# and consumes subsequent hex chars.
    public const string Title = "\u00A9nam";
    public const string Artist = "\u00A9ART";
    public const string Album = "\u00A9alb";
    public const string AlbumArtist = "aART";
    public const string Genre = "\u00A9gen";

    /// <summary>A standard genre, stored as its ID3v1 genre index plus one. iTunes writes this for its built-in genres.</summary>
    public const string GenreId = "gnre";
    public const string Year = "\u00A9day";
    public const string TrackNumber = "trkn";
    public const string DiscNumber = "disk";
    public const string Composer = "\u00A9wrt";
    public const string Conductor = "\u00A9con";
    public const string Comment = "\u00A9cmt";
    public const string Lyrics = "\u00A9lyr";
    public const string Copyright = "cprt";
    public const string Bpm = "tmpo";
    public const string Compilation = "cpil";
    public const string CoverArt = "covr";

    // Video items
    public const string MediaKind = "stik";
    public const string Description = "desc";
    public const string LongDescription = "ldes";
    public const string TvShow = "tvsh";
    public const string TvNetwork = "tvnn";
    public const string TvEpisodeId = "tven";
    public const string TvSeason = "tvsn";
    public const string TvEpisode = "tves";

    // Custom freeform atoms use "----" with mean/name sub-atoms
    public const string Freeform = "----";

    /// <summary>Gets whether the writer regenerates an item from <see cref="MediaTagInfo"/>, rather than keeping it as a native field.</summary>
    /// <remarks>gnre is replaced by the ©gen text the reader reads it into. Keeping it would leave a stale genre that a genre change or removal does not affect.</remarks>
    public static bool IsRegenerated(string atomType)
    {
        return atomType is Title or Artist or Album or AlbumArtist or Genre or GenreId or Year or TrackNumber or DiscNumber
            or Composer or Conductor or Comment or Lyrics or Copyright or Bpm or Compilation or CoverArt or Freeform
            or MediaKind or Description or LongDescription or TvShow or TvNetwork or TvEpisodeId or TvSeason or TvEpisode;
    }
}
