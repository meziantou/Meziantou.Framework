namespace Meziantou.Framework.Imaging.Typography;

/// <summary>How the places where the lines of a paragraph end are chosen among its break opportunities.</summary>
public enum TextLineBreakStrategy
{
    /// <summary>
    /// Each line takes as much of the text as fits, then the next line starts: fast, and the lines of a paragraph do
    /// not depend on the lines after them. The default.
    /// </summary>
    Greedy = 0,

    /// <summary>
    /// The breaks of the whole paragraph are chosen together (total fit, after Knuth and Plass), so that the lines but
    /// the last one are as evenly filled as possible: a line can end before the text that would fit if this leaves less
    /// room at the end of the lines after it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The breaks minimize the sum, over the lines but the last one, of the square of the room left at their end, in
    /// ems of the text at the start of the paragraph (for a justified line, of the room per justification opportunity,
    /// in thirds of an em). A hyphenated line costs as much as one em of room, and two hyphenated lines in a row three
    /// more. The break opportunities are those of greedy breaking: the Unicode line breaking algorithm, the soft
    /// hyphens and the hyphenation points when words are hyphenated; a word that is wider than the line is cut or
    /// overflows as with greedy breaking.
    /// </para>
    /// <para>
    /// The breaks are found from the advances of the characters when the paragraph is shaped as a whole: a line is shaped
    /// again once it is cut, and when what this changes (a kerning pair or a ligature across the break) makes it wider
    /// than its width, the breaks of the rest of the paragraph are chosen again from that line. Lines that do not fit
    /// <see cref="TextLayoutOptions.MaxLines"/> or <see cref="TextLayoutOptions.MaxHeight"/> are cut afterwards: the
    /// breaks are not chosen to fit them. Laying out a long paragraph takes about twice as long as with greedy breaking.
    /// </para>
    /// </remarks>
    Optimal = 1,
}
