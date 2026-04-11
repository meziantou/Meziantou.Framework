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
    public const string Year = "\u00A9day";
    public const string TrackNumber = "trkn";
    public const string DiscNumber = "disk";
    public const string Composer = "\u00A9wrt";
    public const string Comment = "\u00A9cmt";
    public const string Copyright = "cprt";
    public const string Bpm = "tmpo";
    public const string Compilation = "cpil";
    public const string CoverArt = "covr";

    // Custom freeform atoms use "----" with mean/name sub-atoms
    public const string Freeform = "----";
}
