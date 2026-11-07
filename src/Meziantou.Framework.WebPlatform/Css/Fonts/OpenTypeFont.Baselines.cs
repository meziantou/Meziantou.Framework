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
    /// <c>latn</c>), as the font reader reads them.
    /// </summary>
    public FontBaselines GetBaselines() => _baselines ??= ReadBaselines();

    private FontBaselines ReadBaselines()
    {
        try
        {
            ReadOnlySpan<uint> scripts = [DefaultScriptTag, LatinScriptTag];
            var hanging = Get(Face, scripts, HangingBaselineTag);
            var ideographic = Get(Face, scripts, IdeographicBaselineTag);
            var alphabetic = Get(Face, scripts, RomanBaselineTag);
            return new FontBaselines(hanging, ideographic, alphabetic)
            {
                IdeographicOver = Get(Face, scripts, IdeographicTopBaselineTag),
                IdeographicInkUnder = Get(Face, scripts, IdeographicFaceBottomBaselineTag),
                IdeographicInkOver = Get(Face, scripts, IdeographicFaceTopBaselineTag),
                Mathematical = Get(Face, scripts, MathBaselineTag),
            };
        }
        catch (Meziantou.Framework.Fonts.FontException)
        {
            return FontBaselines.None;
        }

        static double? Get(Meziantou.Framework.Fonts.FontFace face, ReadOnlySpan<uint> scripts, uint baseline)
            => face.TryGetBaseline(isVertical: false, scripts, baseline, out var coordinate) ? coordinate : null;
    }
}
