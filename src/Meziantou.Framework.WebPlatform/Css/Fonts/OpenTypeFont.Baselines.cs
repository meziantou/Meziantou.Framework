using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The positions of the baselines of a font in the horizontal axis, in font units above the alphabetic baseline, or null when the font does not define them.</summary>
internal sealed record FontBaselines(double? Hanging, double? Ideographic, double? Alphabetic)
{
    public static FontBaselines None { get; } = new(null, null, null);

    /// <summary>The line-over edge of the ideographic em box (<c>idtp</c>).</summary>
    public double? IdeographicOver { get; init; }

    /// <summary>The line-under edge of the ideographic character face (<c>icfb</c>).</summary>
    public double? IdeographicInkUnder { get; init; }

    /// <summary>The line-over edge of the ideographic character face (<c>icft</c>).</summary>
    public double? IdeographicInkOver { get; init; }

    /// <summary>The baseline around which the mathematical characters are centered (<c>math</c>).</summary>
    public double? Mathematical { get; init; }
}

/// <summary>
/// The baselines of a font at a font size (https://drafts.csswg.org/css-inline-3/#baseline-types), in CSS px above the origin of
/// the glyphs (see <see cref="MatchedFont.GetBaselines"/>).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct UsedFontBaselines
{
    /// <summary>The alphabetic baseline (<c>romn</c>): the origin of the glyphs when the font does not define it.</summary>
    public double Alphabetic { get; init; }

    /// <summary>The ideographic-under baseline (<c>ideo</c>).</summary>
    public double IdeographicUnder { get; init; }

    /// <summary>The ideographic-over baseline (<c>idtp</c>).</summary>
    public double IdeographicOver { get; init; }

    /// <summary>The central baseline: halfway between the ideographic-under and the ideographic-over baselines.</summary>
    public double Central => (IdeographicUnder + IdeographicOver) / 2;

    /// <summary>The ideographic-ink-under baseline (<c>icfb</c>).</summary>
    public double IdeographicInkUnder { get; init; }

    /// <summary>The ideographic-ink-over baseline (<c>icft</c>).</summary>
    public double IdeographicInkOver { get; init; }

    /// <summary>The hanging baseline (<c>hang</c>).</summary>
    public double Hanging { get; init; }

    /// <summary>The math baseline (<c>math</c>).</summary>
    public double Mathematical { get; init; }

    /// <summary>The x-middle baseline: halfway between the alphabetic baseline and the x-height.</summary>
    public double XMiddle { get; init; }

    /// <summary>Whether the font defines an ideographic baseline (the ideographic baselines are not synthesized from the ascent and the descent).</summary>
    public bool HasIdeographic { get; init; }
}

/// <summary>The baselines of the fonts: the BASE table (https://learn.microsoft.com/typography/opentype/spec/base).</summary>
internal sealed partial class OpenTypeFont
{
    private const uint BaseTableTag = 0x42415345; // BASE
    private const uint HangingBaselineTag = 0x68616E67; // hang
    private const uint IdeographicBaselineTag = 0x6964656F; // ideo
    private const uint RomanBaselineTag = 0x726F6D6E; // romn
    private const uint IdeographicTopBaselineTag = 0x69647470; // idtp
    private const uint IdeographicFaceBottomBaselineTag = 0x69636662; // icfb
    private const uint IdeographicFaceTopBaselineTag = 0x69636674; // icft
    private const uint MathBaselineTag = 0x6D617468; // math
    private const uint DefaultScriptTag = 0x44464C54; // DFLT
    private const uint LatinScriptTag = 0x6C61746E; // latn

    private FontBaselines? _baselines;

    /// <summary>
    /// Returns the baselines of the horizontal axis of the BASE table: the coordinates of the baseline tags <c>hang</c>,
    /// <c>ideo</c>, <c>idtp</c>, <c>icfb</c>, <c>icft</c>, <c>math</c> and <c>romn</c> of the default script (<c>DFLT</c>, else
    /// <c>latn</c>, else the first script).
    /// </summary>
    public FontBaselines GetBaselines() => _baselines ??= ReadBaselines();

    private FontBaselines ReadBaselines()
    {
        var table = GetTable(BaseTableTag);
        if (table.Length < 8)
            return FontBaselines.None;

        // The horizontal axis table: the list of the baseline tags and the list of the scripts
        var axis = FontBinary.U16(table, 4);
        if (axis == 0)
            return FontBaselines.None;

        var tagList = FontBinary.U16(table, axis);
        var scriptList = FontBinary.U16(table, axis + 2);
        if (tagList == 0 || scriptList == 0)
            return FontBaselines.None;

        var tagCount = FontBinary.U16(table, axis + tagList);
        var scriptCount = FontBinary.U16(table, axis + scriptList);
        var scriptOffset = 0;
        for (var i = 0; i < scriptCount; i++)
        {
            var record = axis + scriptList + 2 + (i * 6);
            var tag = FontBinary.U32(table, record);
            var offset = FontBinary.U16(table, record + 4);
            if (tag == DefaultScriptTag || (tag == LatinScriptTag && scriptOffset == 0) || i == 0)
            {
                scriptOffset = axis + scriptList + offset;
                if (tag == DefaultScriptTag)
                    break;
            }
        }

        // The base values of the script: a coordinate per baseline tag
        var valuesOffset = scriptOffset == 0 ? 0 : FontBinary.U16(table, scriptOffset);
        if (valuesOffset == 0)
            return FontBaselines.None;

        var values = scriptOffset + valuesOffset;
        var coordCount = FontBinary.U16(table, values + 2);
        double? hanging = null, ideographic = null, alphabetic = null, ideographicOver = null, inkUnder = null, inkOver = null, mathematical = null;
        for (var i = 0; i < Math.Min(coordCount, tagCount); i++)
        {
            var tag = FontBinary.U32(table, axis + tagList + 2 + (i * 4));
            var coordOffset = FontBinary.U16(table, values + 4 + (i * 2));
            if (coordOffset == 0)
                continue;

            // All the formats of the BaseCoord tables start with the format and the coordinate
            double coordinate = FontBinary.I16(table, values + coordOffset + 2);
            switch (tag)
            {
                case HangingBaselineTag:
                    hanging = coordinate;
                    break;
                case IdeographicBaselineTag:
                    ideographic = coordinate;
                    break;
                case RomanBaselineTag:
                    alphabetic = coordinate;
                    break;
                case IdeographicTopBaselineTag:
                    ideographicOver = coordinate;
                    break;
                case IdeographicFaceBottomBaselineTag:
                    inkUnder = coordinate;
                    break;
                case IdeographicFaceTopBaselineTag:
                    inkOver = coordinate;
                    break;
                case MathBaselineTag:
                    mathematical = coordinate;
                    break;
            }
        }

        return new FontBaselines(hanging, ideographic, alphabetic)
        {
            IdeographicOver = ideographicOver,
            IdeographicInkUnder = inkUnder,
            IdeographicInkOver = inkOver,
            Mathematical = mathematical,
        };
    }
}
