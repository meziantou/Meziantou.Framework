namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The shaper of the Hangul script (HarfBuzz's hb-ot-shaper-hangul.cc, MIT license;
/// https://learn.microsoft.com/typography/script-development/hangul): a syllable of jamos is composed to its precomposed
/// syllable when the font supports it, else it is fully decomposed and its jamos get the <c>ljmo</c>, <c>vjmo</c> and
/// <c>tjmo</c> features (the Old Hangul syllables); a tone mark after a syllable moves before it (unless it has no width), or
/// gets a dotted circle without a syllable. The <c>calt</c> feature does not apply to the jamos (some fonts apply their jamo
/// lookups in it).
/// </summary>
internal sealed class HangulShaper : OpenTypeShaper
{
    public static HangulShaper Instance { get; } = new();

    // The features of the jamos, stored in GlyphInfo.ShaperAuxiliary (0: none)
    private const byte Ljmo = 1;
    private const byte Vjmo = 2;
    private const byte Tjmo = 3;

    private static readonly uint[] Features = [0, ShapePlanBuilder.Tag("ljmo"), ShapePlanBuilder.Tag("vjmo"), ShapePlanBuilder.Tag("tjmo")];
    private static readonly uint CaltTag = ShapePlanBuilder.Tag("calt");

    // The algorithmic composition of the Hangul syllables
    private const int LBase = 0x1100;
    private const int VBase = 0x1161;
    private const int TBase = 0x11A7;
    private const int LCount = 19;
    private const int VCount = 21;
    private const int TCount = 28;
    private const int SBase = 0xAC00;
    private const int NCount = VCount * TCount;
    private const int SCount = LCount * NCount;

    private HangulShaper()
    {
    }

    public override NormalizationMode NormalizationPreference => NormalizationMode.None;

    public override ZeroWidthMarks ZeroWidthMarks => ZeroWidthMarks.None;

    public override void CollectFeatures(ShapePlanBuilder builder)
    {
        for (var i = Ljmo; i <= Tjmo; i++)
        {
            builder.AddFeature(Features[i]);
        }
    }

    public override void OverrideFeatures(ShapePlanBuilder builder)
    {
        // Uniscribe does not apply calt to Hangul, and some fonts (Noto Sans CJK, Source Han Sans) apply all their jamo lookups
        // in calt: calt gets its own mask, which the jamos do not have (https://github.com/harfbuzz/harfbuzz/discussions/4853)
        builder.AddFeature(CaltTag);
    }

    public override object? CreateData(ShapePlan plan)
    {
        var masks = new uint[Features.Length + 1];
        for (var i = 0; i < Features.Length; i++)
        {
            masks[i] = Features[i] == 0 ? 0 : plan.GetOneMask(Features[i]);
        }

        masks[^1] = plan.GetOneMask(CaltTag);
        return masks;
    }

    private static bool IsCombiningL(int u) => u is >= LBase and < LBase + LCount;

    private static bool IsCombiningV(int u) => u is >= VBase and < VBase + VCount;

    private static bool IsCombiningT(int u) => u is > TBase and < TBase + TCount;

    private static bool IsCombinedS(int u) => u is >= SBase and < SBase + SCount;

    private static bool IsL(int u) => u is (>= 0x1100 and <= 0x115F) or (>= 0xA960 and <= 0xA97C);

    private static bool IsV(int u) => u is (>= 0x1160 and <= 0x11A7) or (>= 0xD7B0 and <= 0xD7C6);

    private static bool IsT(int u) => u is (>= 0x11A8 and <= 0x11FF) or (>= 0xD7CB and <= 0xD7FB);

    private static bool IsHangulTone(int u) => u is 0x302E or 0x302F;

    private static bool IsZeroWidth(in ShapingFont font, int u) => font.TryGetNominalGlyph(u, out var glyph) && font.GetHorizontalAdvance(glyph) == 0;

    /// <summary>
    /// Composes or decomposes the syllables (HarfBuzz's <c>preprocess_text_hangul</c>): a syllable is &lt;L&gt;, &lt;L,V&gt;,
    /// &lt;L,V,T&gt;, &lt;LV&gt;, &lt;LVT&gt; or &lt;LV,T&gt;; the whole syllable is precomposed when the font supports it,
    /// else it is fully decomposed (the jamos get their features).
    /// </summary>
    public override void PreprocessText(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font)
    {
        buffer.ClearOutput();

        // The extent of the last syllable in the output (valid when start < end)
        int start = 0, end = 0;
        var count = buffer.Length;
        Span<int> characters = stackalloc int[3];
        while (buffer.Index < count && buffer.Successful)
        {
            var u = buffer.Current.Codepoint;
            if (IsHangulTone(u))
            {
                if (start < end && end == buffer.OutLength)
                {
                    // A tone mark after a valid syllable moves before it, unless it has no width (it overstrikes)
                    buffer.UnsafeToBreakFromOutput(start, buffer.Index);
                    if (!buffer.NextGlyph())
                        break;

                    if (!IsZeroWidth(font, u))
                    {
                        buffer.MergeOutClusters(start, end + 1);
                        var output = buffer.OutInfo;
                        var tone = output[end];
                        Array.Copy(output, start, output, start + 1, end - start);
                        output[start] = tone;
                    }
                }
                else if (font.HasGlyph(SyllabicShaping.DottedCircle))
                {
                    // No syllable: the tone mark gets a dotted circle
                    if (!IsZeroWidth(font, u))
                    {
                        characters[0] = u;
                        characters[1] = SyllabicShaping.DottedCircle;
                    }
                    else
                    {
                        characters[0] = SyllabicShaping.DottedCircle;
                        characters[1] = u;
                    }

                    buffer.ReplaceGlyphs(1, characters[..2]);
                }
                else
                {
                    buffer.NextGlyph();
                }

                start = end = buffer.OutLength;
                continue;
            }

            // A potential start of a syllable, used when end is set after it
            start = buffer.OutLength;
            if (IsL(u) && buffer.Index + 1 < count)
            {
                var l = u;
                var v = buffer.Info[buffer.Index + 1].Codepoint;
                if (IsV(v))
                {
                    // <L,V> or <L,V,T>
                    var t = 0;
                    var tIndex = 0;
                    if (buffer.Index + 2 < count)
                    {
                        t = buffer.Info[buffer.Index + 2].Codepoint;
                        if (IsT(t))
                        {
                            tIndex = t - TBase;
                        }
                        else
                        {
                            t = 0;
                        }
                    }

                    buffer.UnsafeToBreak(buffer.Index, buffer.Index + (t != 0 ? 3 : 2));

                    // Compose when the font has the syllable
                    if (IsCombiningL(l) && IsCombiningV(v) && (t == 0 || IsCombiningT(t)))
                    {
                        var s = SBase + ((l - LBase) * NCount) + ((v - VBase) * TCount) + tIndex;
                        if (font.HasGlyph(s))
                        {
                            characters[0] = s;
                            buffer.ReplaceGlyphs(t != 0 ? 3 : 2, characters[..1]);
                            end = start + 1;
                            continue;
                        }
                    }

                    // An Old Hangul syllable without a precomposed character, or the font does not have it: the jamos get
                    // their features
                    buffer.Current.ShaperAuxiliary = Ljmo;
                    buffer.NextGlyph();
                    buffer.Current.ShaperAuxiliary = Vjmo;
                    buffer.NextGlyph();
                    if (t != 0)
                    {
                        buffer.Current.ShaperAuxiliary = Tjmo;
                        buffer.NextGlyph();
                        end = start + 3;
                    }
                    else
                    {
                        end = start + 2;
                    }

                    if (!buffer.Successful)
                        break;

                    buffer.MergeOutClusters(start, end);
                    continue;
                }
            }
            else if (IsCombinedS(u))
            {
                // <LV>, <LVT> or <LV,T>
                var s = u;
                var hasGlyph = font.HasGlyph(s);
                var lIndex = (s - SBase) / NCount;
                var nIndex = (s - SBase) % NCount;
                var vIndex = nIndex / TCount;
                var tIndex = nIndex % TCount;
                if (tIndex == 0 && buffer.Index + 1 < count && IsCombiningT(buffer.Info[buffer.Index + 1].Codepoint))
                {
                    // <LV,T>: compose when the font has the syllable
                    var newS = s + buffer.Info[buffer.Index + 1].Codepoint - TBase;
                    if (font.HasGlyph(newS))
                    {
                        characters[0] = newS;
                        buffer.ReplaceGlyphs(2, characters[..1]);
                        end = start + 1;
                        continue;
                    }

                    buffer.UnsafeToBreak(buffer.Index, buffer.Index + 2);
                }

                // Decompose when the font does not have <LV> or <LVT>, or before a non-combining T (the combining <LV,T> is
                // composed above)
                if (!hasGlyph || (tIndex == 0 && buffer.Index + 1 < count && IsT(buffer.Info[buffer.Index + 1].Codepoint)))
                {
                    characters[0] = LBase + lIndex;
                    characters[1] = VBase + vIndex;
                    characters[2] = TBase + tIndex;
                    if (font.HasGlyph(characters[0]) && font.HasGlyph(characters[1]) && (tIndex == 0 || font.HasGlyph(characters[2])))
                    {
                        var length = tIndex != 0 ? 3 : 2;
                        buffer.ReplaceGlyphs(1, characters[..length]);

                        // An <LV> decomposed because of a non-combining T: the T is in the syllable
                        if (hasGlyph && tIndex == 0)
                        {
                            buffer.NextGlyph();
                            length++;
                        }

                        if (!buffer.Successful)
                            break;

                        // The jamos of the decomposed syllable get their features
                        var output = buffer.OutInfo;
                        end = start + length;
                        var i = start;
                        output[i++].ShaperAuxiliary = Ljmo;
                        output[i++].ShaperAuxiliary = Vjmo;
                        if (i < end)
                        {
                            output[i].ShaperAuxiliary = Tjmo;
                        }

                        buffer.MergeOutClusters(start, end);
                        continue;
                    }

                    if (tIndex == 0 && buffer.Index + 1 < count && IsT(buffer.Info[buffer.Index + 1].Codepoint))
                    {
                        buffer.UnsafeToBreak(buffer.Index, buffer.Index + 2);
                    }
                }

                if (hasGlyph)
                {
                    // The syllable is kept
                    end = start + 1;
                }
            }

            // Not a syllable (end <= start: no reordering of the tone marks)
            buffer.NextGlyph();
        }

        buffer.Sync();
    }

    public override void SetupMasks(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font)
    {
        var masks = (uint[])plan.ShaperData!;
        var caltMask = masks[^1];
        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            info[i].Mask |= masks[info[i].ShaperAuxiliary];

            // No calt on the jamos
            var u = info[i].Codepoint;
            if (IsL(u) || IsV(u) || IsT(u))
            {
                info[i].Mask &= ~caltMask;
            }
        }
    }
}
