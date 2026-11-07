namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>The subtables a <see cref="FontBuilder"/> writes in the character map.</summary>
[Flags]
internal enum FontCmapFormats
{
    None = 0,

    /// <summary>Windows, Unicode BMP: segments (format 4).</summary>
    Format4 = 1,

    /// <summary>Windows, Unicode full repertoire: groups (format 12).</summary>
    Format12 = 2,

    /// <summary>Macintosh, Roman: trimmed table (format 6) over the code points below 256.</summary>
    MacintoshFormat6 = 4,

    /// <summary>Windows, Unicode full repertoire: many-to-one groups (format 13).</summary>
    Format13 = 8,

    /// <summary>Unicode variation sequences (format 14).</summary>
    Format14 = 16,
}
