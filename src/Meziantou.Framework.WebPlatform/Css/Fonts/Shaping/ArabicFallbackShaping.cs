using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The fallback shaping of the Arabic fonts that have no Arabic joining features (HarfBuzz's hb-ot-shaper-arabic-fallback.hh,
/// MIT license): GSUB lookups are synthesized from the Arabic presentation forms the font maps in its cmap table (the
/// initial, medial, final and isolated forms of the letters, the lam-alef ligatures and the shadda ligatures of
/// <see cref="ArabicPresentationFormsData"/>) and applied with the masks of the joining forms and of <c>rlig</c>. The fonts
/// encoded in Windows-1256 (HarfBuzz's hand-written lookups) are not supported.
/// </summary>
internal sealed class ArabicFallbackPlan
{
    private const ushort IgnoreMarks = (ushort)LookupFlags.IgnoreMarks;

    // The features of the lookups (the order of the forms of ArabicPresentationFormsData.Forms, then rlig three times)
    private static readonly uint[] Features =
    [
        ShapePlanBuilder.Tag("init"),
        ShapePlanBuilder.Tag("medi"),
        ShapePlanBuilder.Tag("fina"),
        ShapePlanBuilder.Tag("isol"),
        ShapePlanBuilder.Tag("rlig"),
        ShapePlanBuilder.Tag("rlig"),
        ShapePlanBuilder.Tag("rlig"),
    ];

    private readonly OpenTypeLayoutTable? _table;
    private readonly uint[] _masks;

    private ArabicFallbackPlan(OpenTypeLayoutTable? table, uint[] masks)
    {
        _table = table;
        _masks = masks;
    }

    /// <summary>Synthesizes the lookups of a plan from the presentation forms of its font (HarfBuzz's <c>arabic_fallback_plan_init_unicode</c>).</summary>
    public static ArabicFallbackPlan Create(ShapePlan plan, in ShapingFont font)
    {
        var lookups = new List<byte[]>();
        var masks = new List<uint>();
        for (var i = 0; i < Features.Length; i++)
        {
            var mask = plan.GetOneMask(Features[i]);
            if (mask == 0)
                continue;

            var lookup = i switch
            {
                < 4 => CreateSingleLookup(font, i),
                4 => CreateLigatureLookup(font, ArabicPresentationFormsData.Ligatures3, componentCount: 3, IgnoreMarks),
                5 => CreateLigatureLookup(font, ArabicPresentationFormsData.Ligatures2, componentCount: 2, IgnoreMarks),
                _ => CreateLigatureLookup(font, ArabicPresentationFormsData.MarkLigatures, componentCount: 2, flags: 0),
            };

            if (lookup is not null)
            {
                lookups.Add(lookup);
                masks.Add(mask);
            }
        }

        return new ArabicFallbackPlan(lookups.Count == 0 ? null : OpenTypeLayoutTable.CreateSynthetic(plan.Font, CreateTable(lookups)), [.. masks]);
    }

    /// <summary>Applies the synthesized lookups to the buffer (HarfBuzz's <c>arabic_fallback_plan_shape</c>); returns whether there are lookups.</summary>
    public bool Shape(ShapePlan plan, in ShapingFont font, ShapingBuffer buffer)
    {
        if (_table is null)
            return false;

        // A context of its own: the buffer's context is applying the lookups of the font
        var context = new OpenTypeApplyContext(_table, plan.Gdef, buffer, font.Instance);
        for (var i = 0; i < _masks.Length; i++)
        {
            if (_table.GetLookup(i) is not { } lookup)
                continue;

            context.LookupIndex = i;
            context.SetLookupMask(_masks[i]);
            context.ApplyLookup(lookup);
        }

        return true;
    }

    /// <summary>A single substitution of the characters by one of their presentation forms (HarfBuzz's <c>arabic_fallback_synthesize_lookup_single</c>).</summary>
    private static byte[]? CreateSingleLookup(in ShapingFont font, int form)
    {
        var forms = ArabicPresentationFormsData.Forms;
        var pairs = new SortedDictionary<int, int>();
        for (var u = ArabicPresentationFormsData.First; u <= ArabicPresentationFormsData.Last; u++)
        {
            var s = forms[((u - ArabicPresentationFormsData.First) * 4) + form];
            if (s == 0 || !font.TryGetNominalGlyph(u, out var glyph) || !font.TryGetNominalGlyph(s, out var substitute) || glyph == substitute || glyph > ushort.MaxValue || substitute > ushort.MaxValue)
                continue;

            pairs.TryAdd(glyph, substitute);
        }

        if (pairs.Count == 0)
            return null;

        // https://learn.microsoft.com/typography/opentype/spec/gsub#single-substitution-format-2
        var subtable = new TableWriter();
        subtable.U16(2);
        subtable.U16(6 + (pairs.Count * 2));
        subtable.U16(pairs.Count);
        foreach (var substitute in pairs.Values)
        {
            subtable.U16(substitute);
        }

        WriteCoverage(subtable, pairs.Keys);
        return CreateLookup(OpenTypeLookup.SingleSubstitution, IgnoreMarks, subtable.ToArray());
    }

    /// <summary>
    /// A ligature substitution of the presentation forms (HarfBuzz's <c>arabic_fallback_synthesize_lookup_ligature</c>): the
    /// data lists the first form, the number of ligatures, then the other forms and the ligature of each.
    /// </summary>
    private static byte[]? CreateLigatureLookup(in ShapingFont font, ReadOnlySpan<ushort> data, int componentCount, ushort flags)
    {
        // The ligatures by first glyph, sorted by glyph (in the order of the data for a glyph)
        var sets = new SortedDictionary<int, List<int[]>>();
        var entrySize = componentCount;
        for (var i = 0; i < data.Length; i += 2 + (data[i + 1] * entrySize))
        {
            if (!font.TryGetNominalGlyph(data[i], out var first) || first > ushort.MaxValue || sets.ContainsKey(first))
                continue;

            var ligatures = new List<int[]>();
            for (var j = 0; j < data[i + 1]; j++)
            {
                var entry = data.Slice(i + 2 + (j * entrySize), entrySize);
                if (!font.TryGetNominalGlyph(entry[^1], out var ligature))
                    continue;

                // The ligature glyph, then the components after the first one
                var glyphs = new int[componentCount];
                glyphs[0] = ligature;
                var matched = true;
                for (var k = 0; k < componentCount - 1 && matched; k++)
                {
                    matched = entry[k] != 0 && font.TryGetNominalGlyph(entry[k], out glyphs[k + 1]);
                }

                if (matched)
                {
                    ligatures.Add(glyphs);
                }
            }

            if (ligatures.Count > 0)
            {
                sets[first] = ligatures;
            }
        }

        if (sets.Count == 0)
            return null;

        // https://learn.microsoft.com/typography/opentype/spec/gsub#lookuptype-4-ligature-substitution-subtable
        var subtable = new TableWriter();
        subtable.U16(1);
        var coverageOffset = subtable.Reserve();
        subtable.U16(sets.Count);
        var setOffsets = new List<int>();
        foreach (var _ in sets)
        {
            setOffsets.Add(subtable.Reserve());
        }

        var setIndex = 0;
        foreach (var ligatures in sets.Values)
        {
            var set = subtable.Length;
            subtable.Patch(setOffsets[setIndex++], set);
            subtable.U16(ligatures.Count);
            var ligatureOffsets = new List<int>();
            foreach (var _ in ligatures)
            {
                ligatureOffsets.Add(subtable.Reserve());
            }

            for (var i = 0; i < ligatures.Count; i++)
            {
                subtable.Patch(ligatureOffsets[i], subtable.Length - set);
                subtable.U16(ligatures[i][0]);
                subtable.U16(componentCount);
                for (var k = 1; k < componentCount; k++)
                {
                    subtable.U16(ligatures[i][k]);
                }
            }
        }

        subtable.Patch(coverageOffset, subtable.Length);
        WriteCoverage(subtable, sets.Keys);
        return CreateLookup(OpenTypeLookup.LigatureSubstitution, flags, subtable.ToArray());
    }

    private static void WriteCoverage(TableWriter writer, IEnumerable<int> glyphs)
    {
        // https://learn.microsoft.com/typography/opentype/spec/chapter2#coverage-format-1
        var list = glyphs.ToList();
        writer.U16(1);
        writer.U16(list.Count);
        foreach (var glyph in list)
        {
            writer.U16(glyph);
        }
    }

    /// <summary>A lookup table with one subtable (https://learn.microsoft.com/typography/opentype/spec/chapter2#lookup-table).</summary>
    private static byte[] CreateLookup(int type, ushort flags, byte[] subtable)
    {
        var writer = new TableWriter();
        writer.U16(type);
        writer.U16(flags);
        writer.U16(1);
        writer.U16(8);
        writer.Bytes(subtable);
        return writer.ToArray();
    }

    /// <summary>A GSUB table with the lookups and an empty script list and feature list.</summary>
    private static byte[] CreateTable(List<byte[]> lookups)
    {
        // https://learn.microsoft.com/typography/opentype/spec/gsub#gsub-header
        var writer = new TableWriter();
        writer.U16(1);
        writer.U16(0);
        writer.U16(10);
        writer.U16(12);
        writer.U16(14);
        writer.U16(0);
        writer.U16(0);

        // The lookup list
        writer.U16(lookups.Count);
        var offset = 2 + (lookups.Count * 2);
        foreach (var lookup in lookups)
        {
            writer.U16(offset);
            offset += lookup.Length;
        }

        foreach (var lookup in lookups)
        {
            writer.Bytes(lookup);
        }

        return writer.ToArray();
    }

    /// <summary>Writes big-endian data.</summary>
    private sealed class TableWriter
    {
        private readonly List<byte> _bytes = [];

        public int Length => _bytes.Count;

        public void U16(int value)
        {
            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)value);
        }

        public void Bytes(byte[] value) => _bytes.AddRange(value);

        /// <summary>Reserves an offset of 16 bits, returns its position.</summary>
        public int Reserve()
        {
            U16(0);
            return _bytes.Count - 2;
        }

        public void Patch(int position, int value)
        {
            _bytes[position] = (byte)(value >> 8);
            _bytes[position + 1] = (byte)value;
        }

        // The tables are small: the offsets of 16 bits do not overflow
        public byte[] ToArray() => [.. _bytes];
    }
}
