using SharedFontException = Meziantou.Framework.Fonts.FontException;
using SharedFontFace = Meziantou.Framework.Fonts.FontFace;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The OpenType MATH table of a font (https://learn.microsoft.com/typography/opentype/spec/math), read by the font reader:
/// the constants of the mathematical layout, the italics corrections and the top accent attachments of the glyphs, and
/// the variants and the assemblies of the stretchy glyphs. A malformed subtable gives no value.
/// </summary>
internal sealed class OpenTypeMath
{
    private readonly SharedFontFace _face;

    internal OpenTypeMath(SharedFontFace face)
    {
        _face = face;
    }

    /// <summary>Whether the table has the MathConstants subtable.</summary>
    public bool HasConstants => _face.HasMathConstants;

    /// <summary>The scale of the first level of scripts, as a percentage.</summary>
    public int ScriptPercentScaleDown => GetConstant(MathConstant.ScriptPercentScaleDown);

    /// <summary>The scale of the second level of scripts, as a percentage.</summary>
    public int ScriptScriptPercentScaleDown => GetConstant(MathConstant.ScriptScriptPercentScaleDown);

    /// <summary>The minimum height of a sub-formula whose delimiters are stretched, in font units.</summary>
    public int DelimitedSubFormulaMinHeight => GetConstant(MathConstant.DelimitedSubFormulaMinHeight);

    /// <summary>The minimum height of the large operators in display style, in font units.</summary>
    public int DisplayOperatorMinHeight => GetConstant(MathConstant.DisplayOperatorMinHeight);

    /// <summary>The height of the bottom of the degree of a radical, as a percentage of the height of the radical.</summary>
    public int RadicalDegreeBottomRaisePercent => GetConstant(MathConstant.RadicalDegreeBottomRaisePercent);

    /// <summary>A constant of the table, in font units (0 when the table has no constants or the value is malformed).</summary>
    public int GetConstant(MathConstant constant)
    {
        try
        {
            return _face.GetMathConstant(constant);
        }
        catch (SharedFontException)
        {
            return 0;
        }
    }

    /// <summary>The italics correction of a glyph, or null when the font does not define it.</summary>
    public int? GetItalicsCorrection(ushort glyph)
    {
        try
        {
            return _face.TryGetMathItalicsCorrection(glyph, out var value) ? value : null;
        }
        catch (SharedFontException)
        {
            return null;
        }
    }

    /// <summary>The top accent attachment of a glyph, or null when the font does not define it.</summary>
    public int? GetTopAccentAttachment(ushort glyph)
    {
        try
        {
            return _face.TryGetMathTopAccentAttachment(glyph, out var value) ? value : null;
        }
        catch (SharedFontException)
        {
            return null;
        }
    }

    /// <summary>The minimum overlap of the connectors of two parts of an assembly, in font units.</summary>
    public int MinConnectorOverlap
    {
        get
        {
            try
            {
                return _face.MathMinConnectorOverlap;
            }
            catch (SharedFontException)
            {
                return 0;
            }
        }
    }

    /// <summary>The variants and the assembly of a glyph in a direction, or null when the glyph does not stretch in it.</summary>
    public MathGlyphConstruction? GetConstruction(ushort glyph, bool vertical)
    {
        try
        {
            return _face.GetMathGlyphConstruction(glyph, vertical);
        }
        catch (SharedFontException)
        {
            return null;
        }
    }
}
