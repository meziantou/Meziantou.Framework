namespace Meziantou.Framework.Fonts;

public sealed partial class AfmFontMetrics
{
    // The PostScript names of the 14 standard fonts (ISO 32000-2, 9.6.2.2), in ordinal order
    private static readonly string[] StandardNames =
    [
        "Courier",
        "Courier-Bold",
        "Courier-BoldOblique",
        "Courier-Oblique",
        "Helvetica",
        "Helvetica-Bold",
        "Helvetica-BoldOblique",
        "Helvetica-Oblique",
        "Symbol",
        "Times-Bold",
        "Times-BoldItalic",
        "Times-Italic",
        "Times-Roman",
        "ZapfDingbats",
    ];

    private static readonly AfmFontMetrics?[] StandardFonts = new AfmFontMetrics?[StandardNames.Length];

    /// <summary>
    /// Gets the PostScript names of the 14 standard fonts of PDF (Courier, Helvetica and Times in four styles, Symbol and
    /// ZapfDingbats), whose metrics <see cref="GetStandardFont(string)"/> returns.
    /// </summary>
    public static IReadOnlyList<string> StandardFontNames { get; } = Array.AsReadOnly(StandardNames);

    /// <summary>
    /// Gets the metrics of one of the 14 standard fonts of PDF (ISO 32000-2, 9.6.2.2), read from the AFM files Adobe
    /// published (<c>Core14_AFMs.zip</c>), which the library embeds.
    /// </summary>
    /// <param name="fontName">The PostScript name of the font, such as <c>Helvetica-Bold</c> (see <see cref="StandardFontNames"/>). The comparison is ordinal.</param>
    /// <returns>The metrics, read once and shared, or <see langword="null"/> when the name is not the name of a standard font.</returns>
    public static AfmFontMetrics? GetStandardFont(string fontName)
    {
        ArgumentNullException.ThrowIfNull(fontName);
        var index = Array.IndexOf(StandardNames, fontName);
        if (index < 0)
            return null;

        var metrics = Volatile.Read(ref StandardFonts[index]);
        if (metrics is null)
        {
            using var stream = OpenStandardFontFile(fontName + ".afm") ?? throw new InvalidOperationException($"The metrics of the standard font '{fontName}' are not embedded in the assembly");
            metrics = Load(stream);
            metrics = Interlocked.CompareExchange(ref StandardFonts[index], metrics, comparand: null) ?? metrics;
        }

        return metrics;
    }

    /// <summary>Opens a file of the embedded AFM files of the standard fonts (<c>Helvetica.afm</c>, <c>MustRead.html</c>), or returns <see langword="null"/>.</summary>
    internal static Stream? OpenStandardFontFile(string fileName) => typeof(AfmFontMetrics).Assembly.GetManifestResourceStream("Meziantou.Framework.Fonts.Afm." + fileName);
}
