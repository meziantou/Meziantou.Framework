using System.Text;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>A font face installed by the host (<see cref="InstalledFontCollection"/>).</summary>
internal sealed class InstalledFont
{
    private FontSelectionCapabilities? _capabilities;

    internal InstalledFont(OpenTypeFont font)
    {
        Font = font;
    }

    /// <summary>The font.</summary>
    public OpenTypeFont Font { get; }

    /// <summary>The styles of the font (its weight, width and style, or the ranges of its variation axes), for the font matching.</summary>
    public FontSelectionCapabilities Capabilities => _capabilities ??= FontSelectionCapabilities.FromFont(Font);

    /// <inheritdoc />
    public override string ToString() => Font.ToString();
}

/// <summary>
/// The fonts that a host installs in addition to the bundled fonts (<see cref="BundledFonts"/>), for the documents of a
/// browsing context (<see cref="Dom.BrowsingContextOptions.InstalledFonts"/>):
/// <list type="bullet">
/// <item>the installed families (<see cref="Add"/>), which a <c>font-family</c> and a <c>local()</c> source can name
/// (https://drafts.csswg.org/css-fonts-4/#font-family-prop): a bundled family hides an installed family of the same
/// name;</item>
/// <item>the fonts of the installed font fallback (<see cref="AddFallback"/>, https://drafts.csswg.org/css-fonts-4/#installed-font-fallback),
/// searched after the bundled fonts for the characters of their ranges that no other font supports. They cannot be named.</item>
/// </list>
/// The bundled fonts are always searched first: the collection changes neither the default font nor the fonts of the
/// characters that the bundled fonts support.
/// </summary>
/// <remarks>
/// The collection is filled before it is given to a browsing context: it must not change afterwards (the font matching
/// caches its results). The fonts are immutable: a collection can be shared by the browsing contexts of several threads.
/// </remarks>
internal sealed class InstalledFontCollection
{
    private readonly Dictionary<string, List<InstalledFont>> _families = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<InstalledFont> _fonts = [];
    private readonly List<FallbackFont> _fallbacks = [];

    /// <summary>A font of the installed font fallback: it is only used for the characters of its ranges.</summary>
    internal readonly record struct FallbackFont(InstalledFont Font, UnicodeRangeSet Ranges);

    /// <summary>The fonts of the installed families, in the order they were added.</summary>
    public IReadOnlyList<InstalledFont> Fonts => _fonts;

    /// <summary>The fonts of the installed font fallback, in the order they are searched.</summary>
    public IReadOnlyList<FallbackFont> Fallbacks => _fallbacks;

    /// <summary>
    /// Installs the faces of a font file (OpenType, TrueType, a collection, WOFF or WOFF2). A face belongs to the families of
    /// all its family names (https://drafts.csswg.org/css-fonts-4/#localized-name-matching: the localized names match too).
    /// </summary>
    /// <returns>false when the data is not a valid font file.</returns>
    public bool Add(ReadOnlySpan<byte> data)
    {
        if (FontFile.Load(data) is not { } file)
            return false;

        foreach (var face in file.Faces)
        {
            var font = new InstalledFont(face);
            _fonts.Add(font);
            foreach (var family in face.GetFamilyNames())
            {
                if (!_families.TryGetValue(family, out var fonts))
                {
                    fonts = [];
                    _families.Add(family, fonts);
                }

                fonts.Add(font);
            }
        }

        return true;
    }

    /// <summary>
    /// Adds the first face of a font file to the installed font fallback, for the characters of a list of ranges (the syntax
    /// of the <c>unicode-range</c> descriptor; all the characters when it is null).
    /// </summary>
    /// <returns>false when the data is not a valid font file.</returns>
    public bool AddFallback(ReadOnlySpan<byte> data, string? unicodeRange = null)
    {
        if (FontFile.Load(data) is not { Faces: [var face, ..] })
            return false;

        _fallbacks.Add(new FallbackFont(new InstalledFont(face), unicodeRange is null ? UnicodeRangeSet.All : UnicodeRangeSet.Parse(unicodeRange)));
        return true;
    }

    /// <summary>The fonts of an installed family (case-insensitive); empty when the family is not installed.</summary>
    public IReadOnlyList<InstalledFont> GetFamilyFonts(string family)
    {
        if (_families.TryGetValue(family, out var fonts))
            return fonts;

        // The names that are not ASCII are compared with the case folding of the font matching
        if (!Ascii.IsValid(family))
        {
            foreach (var (name, candidates) in _families)
            {
                if (FontSelector.FamilyNamesEqual(name, family))
                    return candidates;
            }
        }

        return [];
    }
}
