namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>A font file bundled with the library (an embedded resource of <c>Css/Fonts/Bundled</c>), loaded on first use.</summary>
internal sealed class BundledFont
{
    private readonly Lazy<OpenTypeFont> _font;

    // Not a nullable field assigned on first use: the bundled fonts are shared by the documents of all the threads, and a
    // struct is not written atomically (another thread could read a partially written value)
    private readonly Lazy<FontSelectionCapabilities> _capabilities;

    internal BundledFont(string family, string fileName)
    {
        Family = family;
        FileName = fileName;
        _font = new Lazy<OpenTypeFont>(Load, LazyThreadSafetyMode.ExecutionAndPublication);
        _capabilities = new Lazy<FontSelectionCapabilities>(() => FontSelectionCapabilities.FromFont(Font), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The family name of the font.</summary>
    public string Family { get; }

    /// <summary>The name of the font file in <c>Css/Fonts/Bundled</c>.</summary>
    public string FileName { get; }

    /// <summary>The font (decoded and validated on first use).</summary>
    public OpenTypeFont Font => _font.Value;

    /// <summary>The styles of the font (its weight, width and style, or the ranges of its variation axes), for the font matching.</summary>
    public FontSelectionCapabilities Capabilities => _capabilities.Value;

    private OpenTypeFont Load()
    {
        using var stream = typeof(BundledFont).Assembly.GetManifestResourceStream(BundledFonts.ResourcePrefix + FileName) ?? throw new InvalidOperationException($"The bundled font '{FileName}' is missing");
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return FontFile.Load(data)?.Faces[0] ?? throw new InvalidOperationException($"The bundled font '{FileName}' is invalid");
    }

    /// <inheritdoc />
    public override string ToString() => FileName;
}

/// <summary>
/// The fonts bundled with the library: the Ahem font of the web-platform-tests (whose metrics are exact), and the fallbacks
/// of the generic font families (https://drafts.csswg.org/css-fonts-4/#generic-font-families), metric-compatible with the
/// default fonts of the browsers: Tinos (serif, compatible with Times New Roman), Arimo (sans-serif, compatible with Arial;
/// a variable font of weight 400 to 700) and Cousine (monospace, compatible with Courier New), and the math font STIX Two
/// Math (math, with an OpenType MATH table for the layout of MathML). The fallbacks are subsets
/// (Latin, Greek and Cyrillic) produced by <c>eng/webplatform/update-fonts.sh</c>; see <c>Css/Fonts/Bundled/README.md</c> for their
/// sources and licenses.
/// </summary>
/// <remarks>
/// The fonts are immutable and shared by all the documents. The system fonts are never used, so that the layout is the
/// same on every machine.
/// </remarks>
internal static class BundledFonts
{
    internal const string ResourcePrefix = "Meziantou.Framework.WebPlatform.Css.Fonts.Bundled.";

    /// <summary>The family of the serif fallback.</summary>
    public const string SerifFamily = "Tinos";

    /// <summary>The family of the sans-serif fallback.</summary>
    public const string SansSerifFamily = "Arimo";

    /// <summary>The family of the monospace fallback.</summary>
    public const string MonospaceFamily = "Cousine";

    /// <summary>The family of the math font (the generic family <c>math</c>, https://drafts.csswg.org/css-fonts-4/#valdef-font-family-math).</summary>
    public const string MathFamily = "STIX Two Math";

    /// <summary>The family of the Ahem font.</summary>
    public const string AhemFamily = "Ahem";

    /// <summary>The Ahem font (https://web-platform-tests.org/writing-tests/ahem.html).</summary>
    public static BundledFont Ahem { get; } = new(AhemFamily, "Ahem.ttf");

    /// <summary>All the bundled fonts.</summary>
    public static IReadOnlyList<BundledFont> All { get; } =
    [
        Ahem,
        new(SerifFamily, "Tinos-Regular.woff2"),
        new(SerifFamily, "Tinos-Bold.woff2"),
        new(SerifFamily, "Tinos-Italic.woff2"),
        new(SerifFamily, "Tinos-BoldItalic.woff2"),
        new(SansSerifFamily, "Arimo-VF.woff2"),
        new(SansSerifFamily, "Arimo-Italic-VF.woff2"),
        new(MonospaceFamily, "Cousine-Regular.woff2"),
        new(MonospaceFamily, "Cousine-Bold.woff2"),
        new(MonospaceFamily, "Cousine-Italic.woff2"),
        new(MonospaceFamily, "Cousine-BoldItalic.woff2"),
        new(MathFamily, "STIXTwoMath-Regular.woff2"),
    ];

    /// <summary>The family of the initial value of <c>font-family</c> (the default font of the browsers is a serif font).</summary>
    public static string DefaultFamily => SerifFamily;

    /// <summary>
    /// The families of the installed font fallback (https://drafts.csswg.org/css-fonts-4/#font-style-matching), in order: the
    /// default family, then the other generic fallbacks, then the math font (for the mathematical symbols and the combining marks
    /// that the other fonts do not have). Ahem is a test font: it is used only when it is named.
    /// </summary>
    public static IReadOnlyList<string> FallbackFamilies { get; } = [SerifFamily, SansSerifFamily, MonospaceFamily, MathFamily];

    /// <summary>The bundled fonts of a family (case-insensitive), in the order of <see cref="All"/>.</summary>
    public static IEnumerable<BundledFont> GetFamily(string family) => GetFamilyFonts(family);

    /// <summary>The bundled fonts of a family (case-insensitive), in the order of <see cref="All"/>; empty when the family is not bundled.</summary>
    public static IReadOnlyList<BundledFont> GetFamilyFonts(string family)
    {
        foreach (var (name, fonts) in Families)
        {
            if (string.Equals(name, family, StringComparison.OrdinalIgnoreCase))
                return fonts;
        }

        return [];
    }

    /// <summary>The bundled fonts by family.</summary>
    private static (string Family, BundledFont[] Fonts)[] Families { get; } = [.. All.GroupBy(font => font.Family, StringComparer.Ordinal).Select(group => (group.Key, group.ToArray()))];

    /// <summary>
    /// The ratios to the font size of the font-relative units of the default font (the regular face of the default family),
    /// used where there is no element: in the media queries, the sizes attribute of the images
    /// (https://drafts.csswg.org/css-values-4/#font-relative-lengths: the initial values of the font properties).
    /// </summary>
    public static DefaultFontRatios DefaultRatios => LazyDefaultRatios.Value;

    private static readonly Lazy<DefaultFontRatios> LazyDefaultRatios = new(() =>
    {
        var font = GetFamilyFonts(DefaultFamily)[0].Font;
        var metrics = font.Metrics;
        double unitsPerEm = font.UnitsPerEm;
        var zero = font.GetGlyphId('0');
        var water = font.GetGlyphId(MatchedFont.WaterIdeograph);
        return new DefaultFontRatios(
            metrics.XHeight is { } xHeight ? xHeight / unitsPerEm : 0.5,
            (metrics.CapHeight ?? metrics.Ascent) / unitsPerEm,
            zero != 0 ? font.GetAdvanceWidth(zero) / unitsPerEm : 0.5,
            water != 0 ? font.GetAdvanceWidth(water) / unitsPerEm : 1,
            (metrics.Ascent + metrics.Descent + metrics.LineGap) / unitsPerEm);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The bundled family of a generic font family, or null when there is none (<c>cursive</c>, <c>fantasy</c>, <c>emoji</c>,
    /// <c>fangsong</c>: the font matching falls back to the default family).
    /// </summary>
    public static string? GetGenericFamily(string generic) => generic switch
    {
        "serif" or "ui-serif" => SerifFamily,
        "sans-serif" or "system-ui" or "ui-sans-serif" or "ui-rounded" => SansSerifFamily,
        "monospace" or "ui-monospace" => MonospaceFamily,
        "math" => MathFamily,
        _ => null,
    };
}

/// <summary>The ratios to the font size of the font-relative units of a font (ex, cap, ch, ic, and line-height: normal).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct DefaultFontRatios(double XHeight, double CapHeight, double ZeroAdvance, double IdeographicAdvance, double NormalLineHeight);
