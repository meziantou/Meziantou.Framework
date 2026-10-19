using Meziantou.Framework.WebPlatform.Css.Om;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The fonts of a <see cref="FontDescription"/>: the families of its <c>font-family</c> list matched by the font matching
/// algorithm (https://drafts.csswg.org/css-fonts-4/#font-matching-algorithm), followed by the installed font fallback (the
/// bundled fonts). It gives the first available font (the font of the font-relative units and of <c>line-height: normal</c>)
/// and the font of each character (the per-character fallback used to shape the text).
/// </summary>
/// <remarks>
/// A list is created by the <see cref="FontSelector"/> of a document, and discarded when the font faces of the document
/// change (a font face is loaded, added or removed, a style sheet changes). The lookups of a character start the loads of the
/// web fonts whose <c>unicode-range</c> includes it; while a font loads, the next families are used.
/// </remarks>
internal sealed class FontFallbackList
{
    private readonly FontSelector _selector;
    private readonly FamilyEntry[] _families;
    private readonly FamilyEntry[] _fallbacks;
    private readonly Dictionary<int, MatchedFont?> _characterFonts = [];
    private Dictionary<(int CodePoint, int Selector), MatchedFont?>? _sequenceFonts;

    // The fonts of the Latin-1 characters (the most frequent lookups) and the bit set of the cached ones
    private MatchedFont?[]? _latin1Fonts;
    private readonly ulong[] _latin1Cached = new ulong[4];
    private MatchedFont? _primaryFont;
    private TextShapeCache? _shapeCache;

    internal FontFallbackList(FontSelector selector, FontDescription description, FamilyEntry[] families, FamilyEntry[] fallbacks)
    {
        _selector = selector;
        Description = description;
        _families = families;
        _fallbacks = fallbacks;
    }

    /// <summary>The font properties of the list.</summary>
    public FontDescription Description { get; }

    /// <summary>
    /// The first available font (https://drafts.csswg.org/css-fonts-4/#first-available-font): the first font of the family
    /// list whose <c>unicode-range</c> includes U+0020 (whether or not it has a glyph for it), else the default font.
    /// </summary>
    public MatchedFont PrimaryFont
    {
        get
        {
            if (_primaryFont is null)
            {
                foreach (var family in _families)
                {
                    if (family.GetFont(_selector, ' ', firstAvailable: true) is { } font)
                    {
                        _primaryFont = font;
                        break;
                    }
                }

                _primaryFont ??= _fallbacks[0].GetFont(_selector, ' ', firstAvailable: true)!;
            }

            return _primaryFont;
        }
    }

    /// <summary>
    /// Returns the font that renders a character (https://drafts.csswg.org/css-fonts-4/#font-style-matching): the first font
    /// of the families that supports it, else the first bundled font that supports it (the installed font fallback, not used
    /// for the space characters that the shaping can synthesize), or null when no font supports it (the missing glyph of
    /// <see cref="PrimaryFont"/> is then used).
    /// </summary>
    public MatchedFont? FindFontForCharacter(int codePoint)
    {
        if ((uint)codePoint < 256)
        {
            if ((_latin1Cached[codePoint >> 6] & (1UL << codePoint)) != 0)
                return _latin1Fonts![codePoint];
        }
        else if (_characterFonts.TryGetValue(codePoint, out var cached))
        {
            return cached;
        }

        var result = FindFamilyFont(codePoint);

        // U+2011 NON-BREAKING HYPHEN is shaped with the glyph of U+2010 HYPHEN by a font that only has the latter, rather
        // than with another font
        if (result is null && codePoint == 0x2011)
        {
            result = FindFamilyFont(0x2010);
        }

        // The space characters that the fonts of the families do not support are not searched in the installed font fallback
        // (a fallback font may draw a picture of the space, e.g. U+205F in STIX Two Math): the shaping synthesizes them
        // from the space of the font (see Meziantou.Framework.Fonts.TextShaper.IsSynthesizedSpace)
        if (result is null && !Meziantou.Framework.Fonts.TextShaper.IsSynthesizedSpace(codePoint))
        {
            foreach (var family in _fallbacks)
            {
                if (family.GetFont(_selector, codePoint, firstAvailable: false) is { } font)
                {
                    result = font;
                    break;
                }
            }
        }

        if ((uint)codePoint < 256)
        {
            (_latin1Fonts ??= new MatchedFont?[256])[codePoint] = result;
            _latin1Cached[codePoint >> 6] |= 1UL << codePoint;
        }
        else
        {
            _characterFonts[codePoint] = result;
        }

        return result;
    }

    /// <summary>The first font of the families that supports a character, or null.</summary>
    private MatchedFont? FindFamilyFont(int codePoint)
    {
        foreach (var family in _families)
        {
            if (family.GetFont(_selector, codePoint, firstAvailable: false) is { } font)
                return font;
        }

        return null;
    }

    /// <summary>
    /// Returns the font that renders a variation sequence (a character followed by a variation selector,
    /// https://drafts.csswg.org/css-fonts-4/#cluster-matching): the first font of the families, then of the installed font
    /// fallback, that supports the whole sequence (format 14 of its <c>cmap</c> table), or null (the variation selector is
    /// then ignored by the font fallback: <see cref="FindFontForCharacter"/>).
    /// </summary>
    public MatchedFont? FindFontForVariationSequence(int codePoint, int variationSelector)
    {
        var fonts = _sequenceFonts ??= [];
        if (fonts.TryGetValue((codePoint, variationSelector), out var cached))
            return cached;

        MatchedFont? result = null;
        foreach (var families in (ReadOnlySpan<FamilyEntry[]>)[_families, _fallbacks])
        {
            foreach (var family in families)
            {
                if (family.GetFont(_selector, codePoint, firstAvailable: false) is { } font && font.Font.TryGetGlyphId(codePoint, variationSelector, out _))
                {
                    result = font;
                    break;
                }
            }

            if (result is not null)
                break;
        }

        fonts[(codePoint, variationSelector)] = result;
        return result;
    }

    /// <summary>Returns the font that renders a character, or <see cref="PrimaryFont"/> when no font supports it.</summary>
    public MatchedFont GetFontForCharacter(int codePoint) => FindFontForCharacter(codePoint) ?? PrimaryFont;

    /// <summary>The cache of the text shaped with the fonts of the list (<see cref="TextShaper"/>), discarded with the list.</summary>
    internal TextShapeCache ShapeCache => _shapeCache ??= new();

    /// <summary>A family of the list.</summary>
    internal abstract class FamilyEntry
    {
        /// <summary>
        /// Returns the font of the family that renders a character, or null. For the first available font, the font only has
        /// to cover the character with its unicode-range.
        /// </summary>
        public abstract MatchedFont? GetFont(FontSelector selector, int codePoint, bool firstAvailable);
    }

    /// <summary>A family of installed fonts (the bundled fonts): the face selected by the font matching.</summary>
    internal sealed class InstalledFamilyEntry(MatchedFont font) : FamilyEntry
    {
        public MatchedFont Font { get; } = font;

        public override MatchedFont? GetFont(FontSelector selector, int codePoint, bool firstAvailable) => firstAvailable || Font.HasGlyph(codePoint) ? Font : null;
    }

    /// <summary>
    /// A font of the installed font fallback that the host installed (<see cref="InstalledFontCollection.AddFallback"/>): it
    /// is only used for the characters of its ranges, and is never the first available font.
    /// </summary>
    internal sealed class FallbackFontEntry(MatchedFont font, UnicodeRangeSet ranges) : FamilyEntry
    {
        public override MatchedFont? GetFont(FontSelector selector, int codePoint, bool firstAvailable) => !firstAvailable && ranges.Contains(codePoint) && font.HasGlyph(codePoint) ? font : null;
    }

    /// <summary>
    /// A family defined by font faces (@font-face rules or faces added to the font face set): the faces with the descriptors
    /// selected by the font matching, a composite face when their unicode-range differ (the faces are searched in the reverse
    /// order of their definition).
    /// </summary>
    internal sealed class WebFamilyEntry(WebFontFace[] faces, FontDescription description, FontSelectedValues selected, InstalledFamilyEntry? substitute) : FamilyEntry
    {
        private readonly MatchedFont?[] _fonts = new MatchedFont?[faces.Length];

        public override MatchedFont? GetFont(FontSelector selector, int codePoint, bool firstAvailable)
        {
            var pending = false;
            for (var i = faces.Length - 1; i >= 0; i--)
            {
                var face = faces[i];
                if (!face.Ranges.Contains(codePoint))
                    continue;

                switch (face.Face.Status)
                {
                    case FontFaceLoadStatus.Loaded when face.Font is { } font:
                        var matched = _fonts[i] ??= new MatchedFont(font, face.Face, description, selected, face.Descriptors, face.Capabilities);
                        if (firstAvailable || matched.HasGlyph(codePoint))
                            return matched;

                        break;

                    // https://drafts.csswg.org/css-fonts-4/#font-style-matching: the font is loaded when its unicode-range includes the
                    // character; while it loads, the text is rendered with the substituted font
                    case FontFaceLoadStatus.Unloaded:
                        selector.RequestLoad(face.Face);
                        pending = true;
                        break;
                    case FontFaceLoadStatus.Loading:
                        pending = true;
                        break;
                }
            }

            // While the web font loads, the bundled font of the same family (e.g. Ahem) is the substituted font: the bundled
            // fonts are the installed fonts, and the web-platform-tests load the same fonts with @font-face rules
            return pending ? substitute?.GetFont(selector, codePoint, firstAvailable) : null;
        }
    }
}
