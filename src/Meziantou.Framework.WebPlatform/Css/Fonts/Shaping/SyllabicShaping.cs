using System.Buffers;
using System.Diagnostics;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The parts common to the syllabic shapers (Indic, Khmer, Myanmar and the Universal Shaping Engine), ported from HarfBuzz
/// (hb-ot-shaper-syllabic.cc, the syllable machines of hb-ot-shaper-*-machine.rl and hb-ot-shaper-vowel-constraints.cc, MIT
/// license): the syllables found by the machines (<see cref="GlyphInfo.Syllable"/> is the serial number of the syllable in
/// the high 4 bits and its type in the low 4 bits), the dotted circles inserted in the broken syllables and between the
/// prohibited vowel sequences, and the pauses that clear the syllables and the substitution flags.
/// </summary>
internal static class SyllabicShaping
{
    /// <summary>U+25CC DOTTED CIRCLE, the base of the marks that have none.</summary>
    public const int DottedCircle = 0x25CC;

    /// <summary>The type of the syllable of a glyph (the low 4 bits of <see cref="GlyphInfo.Syllable"/>).</summary>
    public static int GetSyllableType(in GlyphInfo info) => info.Syllable & 0x0F;

    /// <summary>
    /// Finds the syllables of the buffer with a syllable machine (the categories of the glyphs are <see cref="GlyphInfo.ShaperCategory"/>):
    /// the longest token that a pattern of the machine matches at each position, as the Ragel scanners of HarfBuzz.
    /// </summary>
    public static void FindSyllables(ShapingBuffer buffer, ReadOnlySpan<byte> classes, int classCount, ReadOnlySpan<byte> transitions, ReadOnlySpan<byte> accepts, int brokenType)
    {
        var info = buffer.Info;
        var count = buffer.Length;
        var serial = 1;
        var start = 0;
        while (start < count)
        {
            Match(info, start, count, null, classes, classCount, transitions, accepts, out var end, out var type);
            SetSyllable(buffer, start, end, ref serial, type, brokenType);
            start = end;
        }
    }

    /// <summary>
    /// Finds the syllables of the Universal Shaping Engine (HarfBuzz's <c>find_syllables_use</c>): the machine does not see
    /// the CGJ characters (the default ignorable characters that are not joiners) and the ZWNJ that are followed by a mark;
    /// these characters belong to the syllable before them.
    /// </summary>
    public static void FindUseSyllables(ShapingBuffer buffer)
    {
        var info = buffer.Info;
        var count = buffer.Length;
        var indexes = ArrayPool<int>.Shared.Rent(count + 1);
        try
        {
            var length = 0;
            for (var i = 0; i < count; i++)
            {
                var category = info[i].ShaperCategory;
                if (category == UseCategory.Cgj)
                    continue;

                if (category == UseCategory.Zwnj && IsFollowedByMark(info, i, count))
                    continue;

                indexes[length++] = i;
            }

            indexes[length] = count;
            var serial = 1;
            var start = 0;
            while (start < length)
            {
                Match(info, start, length, indexes, UseSyllableMachine.Classes, UseSyllableMachine.ClassCount, UseSyllableMachine.Transitions, UseSyllableMachine.Accepts, out var end, out var type);
                SetSyllable(buffer, indexes[start], indexes[end], ref serial, type, (int)UseSyllableMachine.BrokenSyllable);
                start = end;
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(indexes);
        }

        static bool IsFollowedByMark(GlyphInfo[] info, int index, int count)
        {
            for (var i = index + 1; i < count; i++)
            {
                if (info[i].ShaperCategory != UseCategory.Cgj)
                    return info[i].IsUnicodeMark;
            }

            return false;
        }
    }

    /// <summary>Runs a machine from a position: the end of the longest token and its syllable type.</summary>
    private static void Match(GlyphInfo[] info, int start, int count, int[]? indexes, ReadOnlySpan<byte> classes, int classCount, ReadOnlySpan<byte> transitions, ReadOnlySpan<byte> accepts, out int end, out int type)
    {
        end = start + 1;
        type = -1;
        var state = 1;
        for (var i = start; i < count; i++)
        {
            var category = info[indexes is null ? i : indexes[i]].ShaperCategory;
            state = transitions[(state * classCount) + classes[category & 63]];
            if (state == 0)
                break;

            if (accepts[state] != 0)
            {
                end = i + 1;
                type = accepts[state] - 1;
            }
        }

        // The machines match any character (their "other" pattern)
        Debug.Assert(type >= 0);
    }

    private static void SetSyllable(ShapingBuffer buffer, int start, int end, ref int serial, int type, int brokenType)
    {
        var info = buffer.Info;
        var syllable = (byte)((serial << 4) | type);
        for (var i = start; i < end; i++)
        {
            info[i].Syllable = syllable;
        }

        serial++;
        if (serial == 16)
        {
            serial = 1;
        }

        if (type == brokenType)
        {
            buffer.ScratchFlags |= ShapingScratchFlags.HasBrokenSyllable;
        }
    }

    /// <summary>Marks the glyphs of each syllable as unsafe to break.</summary>
    public static void SetUnsafeToBreak(ShapingBuffer buffer)
    {
        for (int start = 0, end; start < buffer.Length; start = end)
        {
            end = buffer.NextSyllable(start);
            buffer.UnsafeToBreak(start, end);
        }
    }

    /// <summary>
    /// Inserts a dotted circle at the start of each broken syllable (HarfBuzz's <c>hb_syllabic_insert_dotted_circles</c>), after
    /// its repha when <paramref name="rephaCategory"/> is set: the marks without a base attach to it. Returns whether glyphs
    /// were inserted. The font must support U+25CC.
    /// </summary>
    public static bool InsertDottedCircles(in ShapingFont font, ShapingBuffer buffer, int brokenType, byte dottedCircleCategory, int rephaCategory = -1, int dottedCirclePosition = -1)
    {
        if ((buffer.ScratchFlags & ShapingScratchFlags.HasBrokenSyllable) == ShapingScratchFlags.None || !font.TryGetNominalGlyph(DottedCircle, out var glyph))
            return false;

        // A glyph whose properties are zero, as HarfBuzz (its general category is the first one of HarfBuzz, Cc)
        var dottedCircle = new GlyphInfo { Codepoint = glyph, ShaperCategory = dottedCircleCategory };
        dottedCircle.SetGeneralCategory(UnicodeCategory.Control);
        if (dottedCirclePosition >= 0)
        {
            dottedCircle.ShaperAuxiliary = (byte)dottedCirclePosition;
        }

        buffer.ClearOutput();
        var lastSyllable = 0;
        while (buffer.Index < buffer.Length && buffer.Successful)
        {
            var syllable = buffer.Current.Syllable;
            if (lastSyllable != syllable && (syllable & 0x0F) == brokenType)
            {
                lastSyllable = syllable;
                var info = dottedCircle;
                info.Cluster = buffer.Current.Cluster;
                info.Mask = buffer.Current.Mask;
                info.Syllable = syllable;

                // Insert the dotted circle after the repha
                if (rephaCategory >= 0)
                {
                    while (buffer.Index < buffer.Length && buffer.Successful && lastSyllable == buffer.Current.Syllable && buffer.Current.ShaperCategory == rephaCategory)
                    {
                        buffer.NextGlyph();
                    }
                }

                buffer.OutputInfo(info);
            }
            else
            {
                buffer.NextGlyph();
            }
        }

        buffer.Sync();
        return true;
    }

    /// <summary>A pause after the syllables are no longer needed (HarfBuzz's <c>hb_syllabic_clear_var</c>, which keeps them).</summary>
    public static ShapingPause ClearSyllables { get; } = static (ShapePlan _, in ShapingFont _, ShapingBuffer _) => false;

    /// <summary>A pause that clears the substitution flags of the glyphs (HarfBuzz's <c>_hb_clear_substitution_flags</c>).</summary>
    public static ShapingPause ClearSubstitutionFlags { get; } = static (ShapePlan _, in ShapingFont _, ShapingBuffer buffer) =>
    {
        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            info[i].GlyphProperties &= ~GlyphProperties.Substituted;
        }

        return false;
    };

    /// <summary>
    /// Inserts a dotted circle in the vowel sequences of the Indic scripts that look like another vowel (HarfBuzz's
    /// <c>_hb_preprocess_text_vowel_constraints</c>, from IndicShapingInvalidCluster.txt of Microsoft): before the last
    /// character of each prohibited sequence (e.g. DEVANAGARI LETTER A followed by DEVANAGARI VOWEL SIGN AA, which looks like
    /// DEVANAGARI LETTER AA).
    /// </summary>
    public static void PreprocessVowelConstraints(ShapingBuffer buffer)
    {
        var sequences = VowelConstraintData.GetSequences(buffer.Script);
        if (sequences.IsEmpty)
            return;

        buffer.ClearOutput();
        var count = buffer.Length;
        while (buffer.Index + 1 < count && buffer.Successful)
        {
            var length = FindProhibitedSequence(buffer, sequences);
            if (length == 0)
            {
                buffer.NextGlyph();
                continue;
            }

            for (var i = 0; i < length - 1; i++)
            {
                buffer.NextGlyph();
            }

            // A copy of the last character with the dotted circle
            buffer.OutputGlyph(DottedCircle);
            buffer.Previous.UnicodeProperties &= ~UnicodeGlyphProperties.Continuation;
            buffer.NextGlyph();
        }

        buffer.Sync();
    }

    /// <summary>The length of the prohibited sequence that starts at the current character, or 0.</summary>
    private static int FindProhibitedSequence(ShapingBuffer buffer, ReadOnlySpan<int> sequences)
    {
        var info = buffer.Info;
        for (var i = 0; i < sequences.Length; i += sequences[i] + 1)
        {
            var length = sequences[i];
            if (buffer.Index + length > buffer.Length)
                continue;

            var matched = true;
            for (var j = 0; j < length && matched; j++)
            {
                matched = info[buffer.Index + j].Codepoint == sequences[i + 1 + j];
            }

            if (matched)
                return length;
        }

        return 0;
    }
}
