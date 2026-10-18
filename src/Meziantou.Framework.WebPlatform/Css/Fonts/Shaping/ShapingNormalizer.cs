using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The normalization of the characters of a run against the font (HarfBuzz's hb-ot-shape-normalize.cc, MIT license): the
/// characters are decomposed only when the font does not support them and supports their decomposition, the marks are
/// reordered by modified combining class, then the marks are composed with their base when the font supports the
/// composition. Sets <see cref="GlyphInfo.GlyphIndex"/>, the glyph of the font for each character: the spaces that the font
/// does not support use its space glyph, and the variation sequences their glyph of the cmap format 14.
/// </summary>
internal static class ShapingNormalizer
{
    /// <summary>The maximum number of marks reordered together.</summary>
    private const int MaxCombiningMarks = 32;

    private const int SBase = 0xAC00;
    private const int LBase = 0x1100;
    private const int VBase = 0x1161;
    private const int TBase = 0x11A7;
    private const int LCount = 19;
    private const int VCount = 21;
    private const int TCount = 28;
    private const int NCount = VCount * TCount;
    private const int SCount = LCount * NCount;

    /// <summary>The canonical decomposition mapping of a character (one step; Hangul syllables are decomposed algorithmically).</summary>
    public static bool DecomposeUnicode(int ab, out int a, out int b)
    {
        // Hangul: LV to L + V, LVT to LV + T
        var sIndex = ab - SBase;
        if ((uint)sIndex < SCount)
        {
            if (sIndex % TCount != 0)
            {
                a = SBase + (sIndex / TCount * TCount);
                b = TBase + (sIndex % TCount);
            }
            else
            {
                a = LBase + (sIndex / NCount);
                b = VBase + (sIndex % NCount / TCount);
            }

            return true;
        }

        if (ab < 0xC0)
        {
            a = ab;
            b = 0;
            return false;
        }

        var index = NormalizationData.CanonicalDecompositionKeys.BinarySearch(ab);
        if (index < 0)
        {
            a = ab;
            b = 0;
            return false;
        }

        a = NormalizationData.CanonicalDecompositionFirst[index];
        b = NormalizationData.CanonicalDecompositionSecond[index];
        return true;
    }

    /// <summary>The primary composite of two characters (the canonical composition, without the composition exclusions).</summary>
    public static bool ComposeUnicode(int a, int b, out int ab)
    {
        // Hangul: L + V to LV, LV + T to LVT
        if (a - LBase is var lIndex && (uint)lIndex < LCount && b - VBase is var vIndex && (uint)vIndex < VCount)
        {
            ab = SBase + (((lIndex * VCount) + vIndex) * TCount);
            return true;
        }

        if (a - SBase is var sIndex && (uint)sIndex < SCount && sIndex % TCount == 0 && b - TBase is var tIndex and > 0 and < TCount)
        {
            ab = a + tIndex;
            return true;
        }

        var key = ((long)a << 21) | (uint)b;
        var index = NormalizationData.CompositionKeys.BinarySearch(key);
        ab = index >= 0 ? NormalizationData.CompositionValues[index] : 0;
        return index >= 0;
    }

    /// <summary>Normalizes the characters of the buffer for a plan and a font (HarfBuzz's <c>_hb_ot_shape_normalize</c>).</summary>
    public static void Normalize(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font)
    {
        if (buffer.Length == 0)
            return;

        var shaper = plan.Shaper;
        var mode = shaper.NormalizationPreference;
        if (mode == NormalizationMode.Auto)
        {
            mode = NormalizationMode.ComposedDiacritics;
        }

        var alwaysShortCircuit = mode == NormalizationMode.None;
        var mightShortCircuit = alwaysShortCircuit || (mode is not (NormalizationMode.Decomposed or NormalizationMode.ComposedDiacriticsNoShortCircuit));

        // Decompose: the runs of characters without marks are mapped to glyphs directly ("simple clusters")
        var allSimple = true;
        buffer.ClearOutput();
        var count = buffer.Length;
        buffer.Index = 0;
        do
        {
            int end;
            for (end = buffer.Index + 1; end < count; end++)
            {
                if (buffer.Info[end].IsUnicodeMark)
                    break;
            }

            // Leave one base for the marks to cluster with
            if (end < count)
            {
                end--;
            }

            if (mightShortCircuit)
            {
                // The characters the font supports are kept
                var info = buffer.Info;
                var done = 0;
                for (var i = buffer.Index; i < end; i++)
                {
                    if (!font.TryGetNominalGlyph(info[i].Codepoint, out var glyph))
                        break;

                    info[i].GlyphIndex = glyph;
                    done++;
                }

                if (!buffer.NextGlyphs(done))
                    break;
            }

            while (buffer.Index < end && buffer.Successful)
            {
                DecomposeCurrentCharacter(plan, buffer, font, mightShortCircuit);
            }

            if (buffer.Index == count || !buffer.Successful)
                break;

            allSimple = false;

            // A base with its marks
            for (end = buffer.Index + 1; end < count; end++)
            {
                if (!buffer.Info[end].IsUnicodeMark)
                    break;
            }

            DecomposeMultiCharacterCluster(plan, buffer, font, end, alwaysShortCircuit);
        }
        while (buffer.Index < count && buffer.Successful);

        buffer.Sync();

        // Reorder the marks by modified combining class (HarfBuzz's "reorder")
        if (!allSimple)
        {
            count = buffer.Length;
            for (var i = 0; i < count; i++)
            {
                if (buffer.Info[i].ModifiedCombiningClass == 0)
                    continue;

                int end;
                for (end = i + 1; end < count; end++)
                {
                    if (buffer.Info[end].ModifiedCombiningClass == 0)
                        break;
                }

                if (end - i > MaxCombiningMarks)
                {
                    i = end;
                    continue;
                }

                buffer.Sort(i, end, static (a, b) => a.ModifiedCombiningClass.CompareTo(b.ModifiedCombiningClass));
                shaper.ReorderMarks(plan, buffer, i, end);
                i = end;
            }
        }

        // A combining grapheme joiner that does not prevent a reordering is skippable
        if ((buffer.ScratchFlags & ShapingScratchFlags.HasCombiningGraphemeJoiner) != ShapingScratchFlags.None)
        {
            var info = buffer.Info;
            for (var i = 1; i + 1 < buffer.Length; i++)
            {
                if (info[i].Codepoint == 0x034F && (info[i + 1].ModifiedCombiningClass == 0 || info[i - 1].ModifiedCombiningClass <= info[i + 1].ModifiedCombiningClass))
                {
                    info[i].Unhide();
                }
            }
        }

        // Compose the marks with their base (never two bases: e.g. the Hangul jamos stay decomposed)
        if (!allSimple && buffer.Successful && mode is NormalizationMode.ComposedDiacritics or NormalizationMode.ComposedDiacriticsNoShortCircuit)
        {
            buffer.ClearOutput();
            count = buffer.Length;
            var starter = 0;
            buffer.NextGlyph();
            while (buffer.Index < count)
            {
                if (buffer.Current.IsUnicodeMark)
                {
                    // Anything between the starter and this character must have a lower combining class (not blocked)
                    var output = buffer.OutInfo;
                    if ((starter == buffer.OutLength - 1 || buffer.Previous.ModifiedCombiningClass < buffer.Current.ModifiedCombiningClass)
                        && shaper.Compose(plan, font, output[starter].Codepoint, buffer.Current.Codepoint, out var composed)
                        && font.TryGetNominalGlyph(composed, out var glyph))
                    {
                        // Composes, and the font has the composite: the mark is removed and the starter modified
                        if (!buffer.NextGlyph())
                            break;

                        buffer.MergeOutClusters(starter, buffer.OutLength);
                        buffer.RemoveLastOutput();
                        output = buffer.OutInfo;
                        output[starter].Codepoint = composed;
                        output[starter].GlyphIndex = glyph;
                        OpenTypeShapingEngine.SetUnicodeProperties(ref output[starter], buffer);
                        continue;
                    }
                }

                if (!buffer.NextGlyph())
                    break;

                if (buffer.Previous.ModifiedCombiningClass == 0)
                {
                    starter = buffer.OutLength - 1;
                }
            }

            buffer.Sync();
        }
    }

    private static void SetGlyph(ref GlyphInfo info, in ShapingFont font)
    {
        font.TryGetNominalGlyph(info.Codepoint, out var glyph);
        info.GlyphIndex = glyph;
    }

    /// <summary>Writes a character of a decomposition to the output.</summary>
    private static void OutputCharacter(ShapingBuffer buffer, int character, int glyph)
    {
        buffer.Current.GlyphIndex = glyph;
        buffer.OutputGlyph(character);
        OpenTypeShapingEngine.SetUnicodeProperties(ref buffer.Previous, buffer);
    }

    private static void NextCharacter(ShapingBuffer buffer, int glyph)
    {
        buffer.Current.GlyphIndex = glyph;
        buffer.NextGlyph();
    }

    /// <summary>Decomposes a character recursively while the font does not support it; returns the number of characters written.</summary>
    private static int Decompose(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font, bool shortest, int ab)
    {
        var bGlyph = 0;
        if (!plan.Shaper.Decompose(plan, font, ab, out var a, out var b) || (b != 0 && !font.TryGetNominalGlyph(b, out bGlyph)))
            return 0;

        var hasA = font.TryGetNominalGlyph(a, out var aGlyph);
        if (shortest && hasA)
        {
            OutputCharacter(buffer, a, aGlyph);
            if (b != 0)
            {
                OutputCharacter(buffer, b, bGlyph);
                return 2;
            }

            return 1;
        }

        if (Decompose(plan, buffer, font, shortest, a) is var result and > 0)
        {
            if (b != 0)
            {
                OutputCharacter(buffer, b, bGlyph);
                return result + 1;
            }

            return result;
        }

        if (hasA)
        {
            OutputCharacter(buffer, a, aGlyph);
            if (b != 0)
            {
                OutputCharacter(buffer, b, bGlyph);
                return 2;
            }

            return 1;
        }

        return 0;
    }

    private static void DecomposeCurrentCharacter(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font, bool shortest)
    {
        var u = buffer.Current.Codepoint;
        if (shortest && font.TryGetNominalGlyph(u, out var glyph))
        {
            NextCharacter(buffer, glyph);
            return;
        }

        if (Decompose(plan, buffer, font, shortest, u) > 0)
        {
            buffer.SkipGlyph();
            return;
        }

        if (!shortest && font.TryGetNominalGlyph(u, out glyph))
        {
            NextCharacter(buffer, glyph);
            return;
        }

        // A space that the font does not support uses its space glyph, with the advance of its type
        if (buffer.Current.IsUnicodeSpace)
        {
            var spaceType = ShapingUnicode.GetSpaceFallback(u);
            if (spaceType != SpaceFallback.NotSpace && font.TryGetNominalGlyph(' ', out var spaceGlyph))
            {
                buffer.Current.SetSpaceFallback(spaceType);
                NextCharacter(buffer, spaceGlyph);
                buffer.ScratchFlags |= ShapingScratchFlags.HasSpaceFallback;
                return;
            }
        }

        // U+2011 is the only sensible character that is a no-break version of another character and is not a space
        if (u == 0x2011 && font.TryGetNominalGlyph(0x2010, out var hyphen))
        {
            NextCharacter(buffer, hyphen);
            return;
        }

        // The glyph 0 (.notdef)
        NextCharacter(buffer, 0);
    }

    private static void HandleVariationSelectorCluster(ShapingBuffer buffer, in ShapingFont font, int end)
    {
        while (buffer.Index < end - 1 && buffer.Successful)
        {
            var next = buffer.Info[buffer.Index + 1].Codepoint;
            if (ShapingUnicode.IsVariationSelector(next))
            {
                if (font.TryGetVariationGlyph(buffer.Current.Codepoint, next, out var variant))
                {
                    // The sequence is one glyph
                    buffer.Current.GlyphIndex = variant;
                    buffer.ReplaceGlyphs(2, [buffer.Current.Codepoint]);
                }
                else
                {
                    // The base with its default glyph; the selector is hidden
                    SetGlyph(ref buffer.Current, font);
                    buffer.NextGlyph();
                    buffer.ScratchFlags |= ShapingScratchFlags.HasVariationSelectorFallback;
                    buffer.Current.SetVariationSelector(customize: true);
                    SetGlyph(ref buffer.Current, font);
                    buffer.NextGlyph();
                }

                // The other selectors that follow
                while (buffer.Index < end && buffer.Successful && ShapingUnicode.IsVariationSelector(buffer.Current.Codepoint))
                {
                    SetGlyph(ref buffer.Current, font);
                    buffer.NextGlyph();
                }
            }
            else
            {
                SetGlyph(ref buffer.Current, font);
                buffer.NextGlyph();
            }
        }

        if (buffer.Index < end)
        {
            SetGlyph(ref buffer.Current, font);
            buffer.NextGlyph();
        }
    }

    private static void DecomposeMultiCharacterCluster(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font, int end, bool shortCircuit)
    {
        for (var i = buffer.Index; i < end && buffer.Successful; i++)
        {
            if (ShapingUnicode.IsVariationSelector(buffer.Info[i].Codepoint))
            {
                HandleVariationSelectorCluster(buffer, font, end);
                return;
            }
        }

        while (buffer.Index < end && buffer.Successful)
        {
            DecomposeCurrentCharacter(plan, buffer, font, shortCircuit);
        }
    }
}
