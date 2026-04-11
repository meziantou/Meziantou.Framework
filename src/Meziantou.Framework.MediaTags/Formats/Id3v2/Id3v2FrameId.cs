namespace Meziantou.Framework.MediaTags.Formats.Id3v2;

internal static class Id3v2FrameId
{
    // Text information frames
    public const string Title = "TIT2";
    public const string Artist = "TPE1";
    public const string Album = "TALB";
    public const string AlbumArtist = "TPE2";
    public const string Genre = "TCON";
    public const string Year = "TDRC";         // ID3v2.4: Recording time
    public const string YearV23 = "TYER";      // ID3v2.3: Year
    public const string TrackNumber = "TRCK";
    public const string DiscNumber = "TPOS";
    public const string Composer = "TCOM";
    public const string Conductor = "TPE3";
    public const string Copyright = "TCOP";
    public const string Bpm = "TBPM";
    public const string Compilation = "TCMP";  // iTunes non-standard

    // Other frames
    public const string Comment = "COMM";
    public const string Picture = "APIC";
    public const string UserDefinedText = "TXXX";

    // ID3v2.2 equivalents (3-char IDs)
    public const string TitleV22 = "TT2";
    public const string ArtistV22 = "TP1";
    public const string AlbumV22 = "TAL";
    public const string AlbumArtistV22 = "TP2";
    public const string GenreV22 = "TCO";
    public const string YearV22 = "TYE";
    public const string TrackNumberV22 = "TRK";
    public const string DiscNumberV22 = "TPA";
    public const string ComposerV22 = "TCM";
    public const string ConductorV22 = "TP3";
    public const string CopyrightV22 = "TCR";
    public const string BpmV22 = "TBP";
    public const string CommentV22 = "COM";
    public const string PictureV22 = "PIC";
    public const string UserDefinedTextV22 = "TXX";
}
