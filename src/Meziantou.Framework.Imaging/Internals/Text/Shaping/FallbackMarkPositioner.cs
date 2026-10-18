namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Places the marks of a font that has no positioning table: each mark is put on the glyph it follows from the shapes
/// of the two glyphs and from the combining class of the mark.
/// </summary>
/// <remarks>
/// <para>
/// A font made before OpenType Layout draws each mark at one place, for a letter of an average shape, on the left of
/// the pen: over the letter before it in a text written from left to right, and over nothing in a text written from
/// right to left. No specification says what a shaper does instead. The Canonical_Combining_Class property of
/// Unicode says where a mark goes (above, below, attached, on a side; Unicode Standard Annex #44, table 15), and for
/// the classes that are a script of their own (Hebrew, Arabic, Syriac, Thai, Lao, Tibetan) the place is the one of
/// the mark in its script. The distances are those of the reference shaper, with which the fonts that have no
/// positioning table are tested:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Horizontally, a mark is placed in the advance of its base, not in its ink: centered, with its left on the origin
/// for the classes of the left, with its right on the end of the advance for the classes of the right; a double mark
/// is centered on the end of the advance, where the next letter starts. The marks of a component of a ligature are
/// placed in the share of the advance of that component.
/// </description></item>
/// <item><description>
/// Vertically, a mark of a class below hangs under the ink of its base, and a mark of a class above stands on it,
/// at a sixteenth of the em from it unless the class is an attached one. A mark below that the font draws lower
/// than that stays where it is; a mark above that the font draws higher is only moved down by half of the distance.
/// </description></item>
/// <item><description>
/// The marks of one class stack: each one is placed from the mark before it. A mark of another class is placed from
/// the base again.
/// </description></item>
/// <item><description>A mark of a class that has no place of its own (an overlay, a nukta, a virama) is centered and keeps its height.</description></item>
/// </list>
/// </remarks>
internal static class FallbackMarkPositioner
{
    private const int AttachedBelowLeft = 200;
    private const int AttachedBelow = 202;
    private const int AttachedAbove = 214;
    private const int AttachedAboveRight = 216;
    private const int BelowLeft = 218;
    private const int Below = 220;
    private const int BelowRight = 222;
    private const int AboveLeft = 228;
    private const int Above = 230;
    private const int AboveRight = 232;
    private const int DoubleBelow = 233;
    private const int DoubleAbove = 234;

    /// <summary>Places the marks of a run on their bases.</summary>
    /// <param name="buffer">The glyphs, in the order of the text, with their advances; the marks have no advance.</param>
    /// <param name="face">The font.</param>
    /// <param name="isRightToLeft">Whether the run is drawn from right to left: a mark is then drawn from the pen of its base, not from the pen after it.</param>
    /// <exception cref="InvalidFontException">The outline of a glyph is malformed.</exception>
    /// <exception cref="FontResourceLimitException">Decoding an outline exceeds a limit of the environment of the font.</exception>
    public static void Apply(GlyphBuffer buffer, FontFace face, bool isRightToLeft)
    {
        var info = buffer.Info;
        GlyphScratch? scratch = null;
        try
        {
            for (var i = 0; i < info.Length; i++)
            {
                // A mark without base stays where the font draws it.
                if (IsMark(info[i]))
                    continue;

                // The marks of the glyph, and the characters without visible form among them: a joiner between a
                // letter and its mark does not take the mark away from the letter.
                var end = i + 1;
                while (end < info.Length && (IsMark(info[end]) || info[end].IsIgnorable))
                {
                    end++;
                }

                if (end > i + 1)
                {
                    scratch ??= new GlyphScratch(face.Data.Environment.CreateAllocator("TextShaper"));
                    PositionAroundBase(buffer, face, scratch, i, end, isRightToLeft);
                }

                i = end - 1;
            }
        }
        finally
        {
            scratch?.Dispose();
        }
    }

    private static bool IsMark(in GlyphInfo info) => info.Category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static void PositionAroundBase(GlyphBuffer buffer, FontFace face, GlyphScratch scratch, int baseIndex, int end, bool isRightToLeft)
    {
        var info = buffer.Info;
        var positions = buffer.Positions;
        GetExtents(face, scratch, info[baseIndex].Glyph, out _, out var baseBottom, out _, out var baseTop);
        var advance = face.GetAdvanceWidth(info[baseIndex].Glyph);
        var gap = face.UnitsPerEm / 16;
        var componentCount = (info[baseIndex].Props & GlyphInfo.LigatureWithComponents) != 0 && info[baseIndex].LigatureId != 0 ? Math.Max((int)info[baseIndex].LigatureComponent, 1) : 1;

        // From the pen of a mark back to the origin of its base: the advance of the base in a run drawn from left to
        // right, nothing in a run drawn from right to left, where the mark is drawn first.
        var penOffset = isRightToLeft ? 0 : -positions[baseIndex].XAdvance;
        var lastClass = -1;
        var lastComponent = -1;
        var left = 0;
        var width = advance;
        var top = baseTop;
        var bottom = baseBottom;
        for (var i = baseIndex + 1; i < end; i++)
        {
            var combiningClass = GetClass(info[i].CodePoint);
            if (combiningClass != 0 && !info[i].IsIgnorable)
            {
                // The component of a ligature the mark follows, when the base is a ligature of several: a mark that
                // follows the whole ligature is a mark of its last component.
                var component = -1;
                if (componentCount > 1)
                {
                    component = info[i].LigatureId != 0 && info[i].LigatureId == info[baseIndex].LigatureId && info[i].LigatureComponent > 0
                        ? Math.Min((int)info[i].LigatureComponent, componentCount) - 1
                        : componentCount - 1;
                }

                if (combiningClass != lastClass || component != lastComponent)
                {
                    // The marks of one class on one component stack; the others start from the base again.
                    lastClass = combiningClass;
                    lastComponent = component;
                    top = baseTop;
                    bottom = baseBottom;
                    left = 0;
                    width = advance;
                    if (component >= 0)
                    {
                        left = (isRightToLeft ? componentCount - 1 - component : component) * advance / componentCount;
                        width = advance / componentCount;
                    }
                }

                GetExtents(face, scratch, info[i].Glyph, out var markLeft, out var markBottom, out var markRight, out var markTop);
                var markWidth = markRight - markLeft;
                var x = combiningClass switch
                {
                    DoubleBelow or DoubleAbove => (isRightToLeft ? left : left + width) - (markWidth / 2) - markLeft,
                    AttachedBelowLeft or BelowLeft or AboveLeft => left - markLeft,
                    AttachedAboveRight or BelowRight or AboveRight => left + width - markWidth - markLeft,
                    _ => left + ((width - markWidth) / 2) - markLeft,
                };

                var y = 0;
                switch (combiningClass)
                {
                    case DoubleBelow or BelowLeft or Below or BelowRight or AttachedBelowLeft or AttachedBelow:
                        if (combiningClass is not (AttachedBelowLeft or AttachedBelow))
                        {
                            bottom -= gap;
                        }

                        y = bottom - markTop;

                        // A mark the font draws lower than that is not brought up: what follows is placed under it.
                        if (y > 0)
                        {
                            bottom -= y;
                            y = 0;
                        }

                        bottom -= markTop - markBottom;
                        break;

                    case DoubleAbove or AboveLeft or Above or AboveRight or AttachedAbove or AttachedAboveRight:
                        if (combiningClass is not (AttachedAbove or AttachedAboveRight))
                        {
                            top += gap;
                        }

                        y = top - markBottom;

                        // A mark the font draws higher than that is brought down by half of the distance only.
                        if (y < 0)
                        {
                            var correction = -y / 2;
                            top += correction;
                            y += correction;
                        }

                        top += markTop - markBottom;
                        break;
                }

                positions[i].XOffset = x + penOffset;
                positions[i].YOffset = y;
            }

            if (!isRightToLeft)
            {
                penOffset -= positions[i].XAdvance;
            }
        }
    }

    /// <summary>
    /// Gets the class that says where a mark is placed: its canonical combining class, or, for the classes that only
    /// say that a mark is one of a script, the class of the place the mark has in that script.
    /// </summary>
    private static int GetClass(int codePoint)
    {
        var combiningClass = UnicodeProperties.GetCanonicalCombiningClass(codePoint);
        return combiningClass switch
        {
            // Hebrew: the points under the letter and the meteg; the holam and the dot of sin, on the left above the
            // letter; the dot of shin, on the right; the rafe, on the letter. The dagesh, inside the letter, is centered.
            (>= 10 and <= 18) or 20 or 22 => Below,
            19 or 25 => AboveLeft,
            23 => AttachedAbove,
            24 => AboveRight,
            26 => Above,

            // Arabic: the kasra and the kasratan under the letter, the other vowel marks, the shadda, the sukun and
            // the superscript alef above it.
            29 or 32 => Below,
            27 or 28 or 30 or 31 or 33 or 34 or 35 => Above,

            // Syriac: the superscript alaph.
            36 => Above,

            // Thai: the vowels below and the phinthu, then the tone marks, each drawn at the right of its consonant.
            103 => BelowRight,
            107 => AboveRight,
            9 when codePoint == 0x0E3A => BelowRight,

            // Lao and Tibetan: vowel signs below, tone marks and vowel signs above.
            118 or 129 or 132 => Below,
            122 or 130 => Above,
            _ => combiningClass,
        };
    }

    /// <summary>Gets the box of the outline of a glyph, in whole font units; a glyph without outline has an empty box at the origin.</summary>
    private static void GetExtents(FontFace face, GlyphScratch scratch, int glyph, out int left, out int bottom, out int right, out int top)
    {
        var sink = new GlyphBoundsSink();
        var budget = new GlyphBudget(face.Data.Limits);
        face.DecodeOutline(glyph, sink, scratch, ref budget);
        if (sink.IsEmpty)
        {
            left = bottom = right = top = 0;
            return;
        }

        left = (int)Math.Floor(sink.MinX);
        bottom = (int)Math.Floor(sink.MinY);
        right = (int)Math.Ceiling(sink.MaxX);
        top = (int)Math.Ceiling(sink.MaxY);
    }
}
