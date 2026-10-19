using System.Collections.Concurrent;
using Meziantou.Framework.WebPlatform.Unicode;
using FontException = Meziantou.Framework.Fonts.FontException;
using LayoutQueries = Meziantou.Framework.Fonts.LayoutQueries;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// Whether the GSUB features of the font substitute the glyphs of characters: the font synthesis of <c>font-variant-caps</c>
/// and <c>font-variant-position</c> (https://drafts.csswg.org/css-fonts-4/#font-synthesis-small-caps) applies when the font
/// has no glyph for them.
/// </summary>
internal sealed partial class OpenTypeFont
{
    private readonly ConcurrentDictionary<(UnicodeScript Script, uint Feature), bool> _substitutionFeatures = new();

    /// <summary>
    /// Whether the lookups of a GSUB feature of the default language system of a script (or of the fallback scripts) may
    /// substitute a glyph on its own (as Firefox's check of the subscripts and superscripts). The contexts of the
    /// contextual lookups are not matched. A malformed GSUB table substitutes nothing.
    /// </summary>
    public bool WouldSubstitute(UnicodeScript script, uint feature, ushort glyph)
    {
        if (!HasSubstitutionFeature(script, feature))
            return false;

        try
        {
            return LayoutQueries.WouldSubstitute(Face, ShapingWorkspace.GetScript(script), feature, glyph);
        }
        catch (FontException)
        {
            return false;
        }
    }

    /// <summary>Whether the default language system of a script (or of the fallback scripts) has a GSUB feature.</summary>
    public bool HasSubstitutionFeature(UnicodeScript script, uint feature)
    {
        return _substitutionFeatures.GetOrAdd((script, feature), static (key, face) =>
        {
            try
            {
                return LayoutQueries.HasFeature(face, isPositioning: false, ShapingWorkspace.GetScript(key.Script), key.Feature);
            }
            catch (FontException)
            {
                return false;
            }
        }, Face);
    }
}
