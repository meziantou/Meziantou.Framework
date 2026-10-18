namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The shaper of the Hebrew script (HarfBuzz's hb-ot-shaper-hebrew.cc, MIT license): the marks are composed with the
/// presentation forms of the Alphabetic Presentation Forms block (which the canonical composition excludes) when the font has
/// no GPOS mark positioning (the old fonts); the sequences patah or qamats, sheva or hiriq, meteg or a below mark are
/// reordered as the fonts expect. GPOS applies only to the fonts that have the <c>hebr</c> script
/// (https://github.com/harfbuzz/harfbuzz/issues/347#issuecomment-267838368).
/// </summary>
internal sealed class HebrewShaper : OpenTypeShaper
{
    public static HebrewShaper Instance { get; } = new();

    private static readonly uint HebrewTag = ShapePlanBuilder.Tag("hebr");

    // The modified combining classes of the Hebrew marks (HarfBuzz's HB_MODIFIED_COMBINING_CLASS_CCC10...)
    private const int ModifiedSheva = 22;
    private const int ModifiedHiriq = 23;
    private const int ModifiedPatah = 20;
    private const int ModifiedQamats = 21;
    private const int ModifiedMeteg = 25;

    private HebrewShaper()
    {
    }

    public override uint GposTag => HebrewTag;

    /// <summary>
    /// The presentation forms with dagesh of U+05D0..U+05EA (https://bugzilla.mozilla.org/show_bug.cgi?id=728866), 0 for the
    /// letters that have none.
    /// </summary>
    private static ReadOnlySpan<ushort> DageshForms =>
    [
        0xFB30, // ALEF
        0xFB31, // BET
        0xFB32, // GIMEL
        0xFB33, // DALET
        0xFB34, // HE
        0xFB35, // VAV
        0xFB36, // ZAYIN
        0x0000, // HET
        0xFB38, // TET
        0xFB39, // YOD
        0xFB3A, // FINAL KAF
        0xFB3B, // KAF
        0xFB3C, // LAMED
        0x0000, // FINAL MEM
        0xFB3E, // MEM
        0x0000, // FINAL NUN
        0xFB40, // NUN
        0xFB41, // SAMEKH
        0x0000, // AYIN
        0xFB43, // FINAL PE
        0xFB44, // PE
        0x0000, // FINAL TSADI
        0xFB46, // TSADI
        0xFB47, // QOF
        0xFB48, // RESH
        0xFB49, // SHIN
        0xFB4A, // TAV
    ];

    public override bool Compose(ShapePlan plan, in ShapingFont font, int a, int b, out int ab)
    {
        if (ShapingNormalizer.ComposeUnicode(a, b, out ab))
            return true;

        // The presentation forms that the standard normalization excludes, for the old fonts
        if (plan.HasGposMark)
            return false;

        ab = (b, a) switch
        {
            (0x05B4, 0x05D9) => 0xFB1D, // HIRIQ: YOD
            (0x05B7, 0x05F2) => 0xFB1F, // PATAH: YIDDISH YOD YOD
            (0x05B7, 0x05D0) => 0xFB2E, // PATAH: ALEF
            (0x05B8, 0x05D0) => 0xFB2F, // QAMATS: ALEF
            (0x05B9, 0x05D5) => 0xFB4B, // HOLAM: VAV
            (0x05BC, >= 0x05D0 and <= 0x05EA) => DageshForms[a - 0x05D0], // DAGESH
            (0x05BC, 0xFB2A) => 0xFB2C, // DAGESH: SHIN WITH SHIN DOT
            (0x05BC, 0xFB2B) => 0xFB2D, // DAGESH: SHIN WITH SIN DOT
            (0x05BF, 0x05D1) => 0xFB4C, // RAFE: BET
            (0x05BF, 0x05DB) => 0xFB4D, // RAFE: KAF
            (0x05BF, 0x05E4) => 0xFB4E, // RAFE: PE
            (0x05C1, 0x05E9) => 0xFB2A, // SHIN DOT: SHIN
            (0x05C1, 0xFB49) => 0xFB2C, // SHIN DOT: SHIN WITH DAGESH
            (0x05C2, 0x05E9) => 0xFB2B, // SIN DOT: SHIN
            (0x05C2, 0xFB49) => 0xFB2D, // SIN DOT: SHIN WITH DAGESH
            _ => 0,
        };

        return ab != 0;
    }

    /// <summary>
    /// Reorders the marks (HarfBuzz's <c>reorder_marks_hebrew</c>): in patah or qamats (classes 17 and 18), sheva or hiriq (10
    /// and 14), meteg or a below mark (22 and 220), the last two marks are swapped. The classes are the modified classes of
    /// <see cref="ShapingUnicode.GetModifiedCombiningClass"/>.
    /// </summary>
    public override void ReorderMarks(ShapePlan plan, ShapingBuffer buffer, int start, int end)
    {
        var info = buffer.Info;
        for (var i = start + 2; i < end; i++)
        {
            var c0 = info[i - 2].ModifiedCombiningClass;
            var c1 = info[i - 1].ModifiedCombiningClass;
            var c2 = info[i].ModifiedCombiningClass;
            if (c0 is ModifiedPatah or ModifiedQamats && c1 is ModifiedSheva or ModifiedHiriq && c2 is ModifiedMeteg or 220)
            {
                buffer.MergeClusters(i - 1, i + 1);
                (info[i - 1], info[i]) = (info[i], info[i - 1]);
                break;
            }
        }
    }
}
