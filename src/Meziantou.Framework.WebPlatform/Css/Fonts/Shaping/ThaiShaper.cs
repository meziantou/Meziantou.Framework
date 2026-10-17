using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The shaper of the Thai and Lao scripts (HarfBuzz's hb-ot-shaper-thai.cc, MIT license;
/// https://linux.thai.net/~thep/th-otf/shaping.html): the SARA AM is decomposed to NIKHAHIT + SARA AA, and the NIKHAHIT moves
/// before the above-base marks (as Uniscribe). The Thai fonts without Thai GSUB get the fallback shaping of the marks with
/// the glyphs of the private use area of the Windows and Mac fonts (the marks shifted down or left, the consonants without
/// descender).
/// </summary>
internal sealed class ThaiShaper : OpenTypeShaper
{
    public static ThaiShaper Instance { get; } = new();

    private ThaiShaper()
    {
    }

    public override bool FallbackPosition => false;

    private enum ConsonantType
    {
        /// <summary>A normal consonant.</summary>
        NC,

        /// <summary>A consonant with an ascender.</summary>
        AC,

        /// <summary>A consonant with a removable descender.</summary>
        RC,

        /// <summary>A consonant with a strict descender.</summary>
        DC,

        NotConsonant,
    }

    private enum MarkType
    {
        /// <summary>An above-base vowel.</summary>
        AV,

        /// <summary>A below-base vowel.</summary>
        BV,

        /// <summary>A tone mark.</summary>
        T,

        NotMark,
    }

    private enum Action : byte
    {
        Nop,

        /// <summary>Shift the combining mark down.</summary>
        SD,

        /// <summary>Shift the combining mark left.</summary>
        SL,

        /// <summary>Shift the combining mark down-left.</summary>
        SDL,

        /// <summary>Remove the descender from the base.</summary>
        RD,
    }

    private static ConsonantType GetConsonantType(int u) => u switch
    {
        0x0E1B or 0x0E1D or 0x0E1F => ConsonantType.AC,
        0x0E0D or 0x0E10 => ConsonantType.RC,
        0x0E0E or 0x0E0F => ConsonantType.DC,
        >= 0x0E01 and <= 0x0E2E => ConsonantType.NC,
        _ => ConsonantType.NotConsonant,
    };

    private static MarkType GetMarkType(int u) => u switch
    {
        0x0E31 or (>= 0x0E34 and <= 0x0E37) or 0x0E47 or 0x0E4D or 0x0E4E => MarkType.AV,
        >= 0x0E38 and <= 0x0E3A => MarkType.BV,
        >= 0x0E48 and <= 0x0E4C => MarkType.T,
        _ => MarkType.NotMark,
    };

    /// <summary>
    /// The glyphs of the private use area (HarfBuzz's <c>thai_pua_shape</c>): the character and its Windows and Mac PUA
    /// variants for each action.
    /// </summary>
    private static ReadOnlySpan<ushort> GetMappings(Action action) => action switch
    {
        Action.SD =>
        [
            0x0E48, 0xF70A, 0xF88B, // MAI EK
            0x0E49, 0xF70B, 0xF88E, // MAI THO
            0x0E4A, 0xF70C, 0xF891, // MAI TRI
            0x0E4B, 0xF70D, 0xF894, // MAI CHATTAWA
            0x0E4C, 0xF70E, 0xF897, // THANTHAKHAT
            0x0E38, 0xF718, 0xF89B, // SARA U
            0x0E39, 0xF719, 0xF89C, // SARA UU
            0x0E3A, 0xF71A, 0xF89D, // PHINTHU
        ],
        Action.SDL =>
        [
            0x0E48, 0xF705, 0xF88C, // MAI EK
            0x0E49, 0xF706, 0xF88F, // MAI THO
            0x0E4A, 0xF707, 0xF892, // MAI TRI
            0x0E4B, 0xF708, 0xF895, // MAI CHATTAWA
            0x0E4C, 0xF709, 0xF898, // THANTHAKHAT
        ],
        Action.SL =>
        [
            0x0E48, 0xF713, 0xF88A, // MAI EK
            0x0E49, 0xF714, 0xF88D, // MAI THO
            0x0E4A, 0xF715, 0xF890, // MAI TRI
            0x0E4B, 0xF716, 0xF893, // MAI CHATTAWA
            0x0E4C, 0xF717, 0xF896, // THANTHAKHAT
            0x0E31, 0xF710, 0xF884, // MAI HAN-AKAT
            0x0E34, 0xF701, 0xF885, // SARA I
            0x0E35, 0xF702, 0xF886, // SARA II
            0x0E36, 0xF703, 0xF887, // SARA UE
            0x0E37, 0xF704, 0xF888, // SARA UEE
            0x0E47, 0xF712, 0xF889, // MAITAIKHU
            0x0E4D, 0xF711, 0xF899, // NIKHAHIT
        ],
        Action.RD =>
        [
            0x0E0D, 0xF70F, 0xF89A, // YO YING
            0x0E10, 0xF700, 0xF89E, // THO THAN
        ],
        _ => [],
    };

    private static int ShapePua(int u, Action action, in ShapingFont font)
    {
        var mappings = GetMappings(action);
        for (var i = 0; i < mappings.Length; i += 3)
        {
            if (mappings[i] != u)
                continue;

            if (font.HasGlyph(mappings[i + 1]))
                return mappings[i + 1];

            if (font.HasGlyph(mappings[i + 2]))
                return mappings[i + 2];

            break;
        }

        return u;
    }

    // The state machines of the marks above (T0 to T3: the cluster above is empty to full) and below (B0 no descender, B1
    // removable descender, B2 strict descender): for each state and mark type (AV, BV, T), the action and the next state
    private static ReadOnlySpan<byte> AboveStartStates => [0, 1, 0, 0, 3];

    private static ReadOnlySpan<byte> AboveStateMachine =>
    [
        (byte)Action.Nop, 3, (byte)Action.Nop, 0, (byte)Action.SD, 3,
        (byte)Action.SL, 2, (byte)Action.Nop, 1, (byte)Action.SDL, 2,
        (byte)Action.Nop, 3, (byte)Action.Nop, 2, (byte)Action.SL, 3,
        (byte)Action.Nop, 3, (byte)Action.Nop, 3, (byte)Action.Nop, 3,
    ];

    private static ReadOnlySpan<byte> BelowStartStates => [0, 0, 1, 2, 2];

    private static ReadOnlySpan<byte> BelowStateMachine =>
    [
        (byte)Action.Nop, 0, (byte)Action.Nop, 2, (byte)Action.Nop, 0,
        (byte)Action.Nop, 1, (byte)Action.RD, 2, (byte)Action.Nop, 1,
        (byte)Action.Nop, 2, (byte)Action.SD, 2, (byte)Action.Nop, 2,
    ];

    /// <summary>The fallback positioning of the marks with the glyphs of the private use area (HarfBuzz's <c>do_thai_pua_shaping</c>).</summary>
    private static void ShapePua(ShapingBuffer buffer, in ShapingFont font)
    {
        var aboveState = AboveStartStates[(int)ConsonantType.NotConsonant];
        var belowState = BelowStartStates[(int)ConsonantType.NotConsonant];
        var baseIndex = 0;
        var info = buffer.Info;
        for (var i = 0; i < buffer.Length; i++)
        {
            var markType = GetMarkType(info[i].Codepoint);
            if (markType == MarkType.NotMark)
            {
                var consonantType = GetConsonantType(info[i].Codepoint);
                aboveState = AboveStartStates[(int)consonantType];
                belowState = BelowStartStates[(int)consonantType];
                baseIndex = i;
                continue;
            }

            var above = ((aboveState * 3) + (int)markType) * 2;
            var below = ((belowState * 3) + (int)markType) * 2;
            aboveState = AboveStateMachine[above + 1];
            belowState = BelowStateMachine[below + 1];

            // At least one of the actions is Nop
            var action = (Action)(AboveStateMachine[above] != (byte)Action.Nop ? AboveStateMachine[above] : BelowStateMachine[below]);
            buffer.UnsafeToBreak(baseIndex, i);
            if (action == Action.RD)
            {
                info[baseIndex].Codepoint = ShapePua(info[baseIndex].Codepoint, action, font);
            }
            else
            {
                info[i].Codepoint = ShapePua(info[i].Codepoint, action, font);
            }
        }
    }

    private static bool IsSaraAm(int u) => (u & ~0x0080) == 0x0E33;

    private static bool IsAboveBaseMark(int u) => (u & ~0x0080) is (>= 0x0E34 and <= 0x0E37) or (>= 0x0E47 and <= 0x0E4E) or 0x0E31 or 0x0E3B;

    /// <summary>
    /// Decomposes the SARA AM to NIKHAHIT + SARA AA, and moves the NIKHAHIT before the above-base marks (as Uniscribe; it is
    /// not in the OpenType specification of Thai): &lt;0E14, 0E4B, 0E33&gt; is &lt;0E14, 0E4D, 0E4B, 0E32&gt;. Same for Lao
    /// (the Lao characters are the Thai ones + 0x80).
    /// </summary>
    public override void PreprocessText(ShapePlan plan, ShapingBuffer buffer, in ShapingFont font)
    {
        buffer.ClearOutput();
        var count = buffer.Length;
        while (buffer.Index < count)
        {
            var u = buffer.Current.Codepoint;
            if (!IsSaraAm(u))
            {
                if (!buffer.NextGlyph())
                    break;

                continue;
            }

            // Decompose and reorder
            buffer.OutputGlyph(u - 0x0E33 + 0x0E4D);
            buffer.Previous.UnicodeProperties |= UnicodeGlyphProperties.Continuation;
            buffer.ScratchFlags |= ShapingScratchFlags.HasContinuations;
            if (!buffer.ReplaceGlyph(u - 1))
                break;

            // The NIKHAHIT is a mark of class 0 when the widths are zeroed
            var end = buffer.OutLength;
            var output = buffer.OutInfo;
            output[end - 2].SetGeneralCategory(UnicodeCategory.NonSpacingMark);

            var start = end - 2;
            while (start > 0 && IsAboveBaseMark(output[start - 1].Codepoint))
            {
                start--;
            }

            if (start + 2 < end)
            {
                // Move the NIKHAHIT to the start
                buffer.MergeOutClusters(start, end);
                output = buffer.OutInfo;
                var nikhahit = output[end - 2];
                Array.Copy(output, start, output, start + 1, end - start - 2);
                output[start] = nikhahit;
            }

            // The NIKHAHIT is a mark: the clusters are merged with the previous one
            if (start > 0)
            {
                buffer.MergeOutClusters(start - 1, end);
            }
        }

        buffer.Sync();

        // The fallback shaping when the font has no Thai GSUB
        if (plan.Script == UnicodeScript.Thai && !plan.FoundScript[0])
        {
            ShapePua(buffer, font);
        }
    }
}
