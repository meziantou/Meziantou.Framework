namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A table of a WOFF2 file written by <see cref="FontWoff2Writer"/>.</summary>
/// <param name="Tag">The tag of the table.</param>
/// <param name="Data">The bytes stored in the compressed stream: the table, or its transformed form.</param>
/// <param name="Version">The transformation version: 0 or 3 for glyf and loca, 0 or 1 for hmtx, 0 otherwise.</param>
/// <param name="OriginalLength">The length the directory declares for the table before its transformation.</param>
internal sealed record FontWoff2Table(string Tag, byte[] Data, int Version = 0, int? OriginalLength = null)
{
    /// <summary>Gets a value indicating whether the directory stores the length of a transformed form.</summary>
    public bool IsTransformed => Tag is "glyf" or "loca" ? Version != 3 : Version != 0;
}
