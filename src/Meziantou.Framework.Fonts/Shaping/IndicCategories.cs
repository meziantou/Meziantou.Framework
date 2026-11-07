namespace Meziantou.Framework.Fonts;

/// <summary>
/// What a character is in a syllable of an Indic script, and where it goes in it: the static properties of the
/// Microsoft specifications ("Developing OpenType Fonts for Devanagari Script" and the eight others).
/// </summary>
/// <remarks>
/// The categories come from the Indic_Syllabic_Category property of Unicode and the places of the vowel signs from the
/// Indic_Positional_Category property, turned into the reordering classes of the specification of each script (the
/// table "Character reordering classes"). The exceptions are listed where they are made.
/// </remarks>
internal static class IndicCategories
{
    // The categories: the letters of the syllable patterns.
    public const byte Other = 0;
    public const byte Consonant = 1;
    public const byte Vowel = 2;
    public const byte Nukta = 3;
    public const byte Halant = 4;
    public const byte NonJoiner = 5;
    public const byte Joiner = 6;
    public const byte Matra = 7;
    public const byte SyllableModifier = 8;
    public const byte VedicSign = 9;
    public const byte Placeholder = 10;
    public const byte DottedCircle = 11;
    public const byte RegisterShifter = 12;
    public const byte MatraPost = 13;
    public const byte Repha = 14;
    public const byte Ra = 15;
    public const byte ConsonantMedial = 16;
    public const byte Symbol = 17;
    public const byte ConsonantWithStacker = 18;

    // The places of a syllable, in the order they are in once the syllable is reordered.
    public const byte Start = 0;
    public const byte RaToBecomeReph = 1;
    public const byte PreMatra = 2;
    public const byte PreConsonant = 3;
    public const byte Base = 4;
    public const byte AfterMain = 5;
    public const byte AboveConsonant = 6;
    public const byte BeforeSubjoined = 7;
    public const byte BelowConsonant = 8;
    public const byte AfterSubjoined = 9;
    public const byte BeforePost = 10;
    public const byte PostConsonant = 11;
    public const byte AfterPost = 12;
    public const byte ModifiersAndVedic = 13;
    public const byte End = 14;

    /// <summary>The categories that can be the base of a syllable.</summary>
    public const uint ConsonantCategories = (1u << Consonant) | (1u << ConsonantWithStacker) | (1u << Ra) | (1u << ConsonantMedial) | (1u << Vowel) | (1u << Placeholder) | (1u << DottedCircle);

    /// <summary>The joiners.</summary>
    public const uint JoinerCategories = (1u << Joiner) | (1u << NonJoiner);

    /// <summary>Gets the category and the place of a character.</summary>
    public static void Get(int codePoint, out byte category, out byte position)
    {
        category = GetCategory(codePoint);
        position = category switch
        {
            Consonant or Ra or Vowel or Placeholder or DottedCircle or ConsonantWithStacker => Base,
            Matra => GetMatraPosition(codePoint, ref category),
            // The candrabindu of Oriya is "before subscript" in the reordering classes of its specification.
            SyllableModifier when codePoint == 0x0B01 => BeforeSubjoined,
            SyllableModifier or VedicSign or Symbol => ModifiersAndVedic,
            Repha => RaToBecomeReph,
            _ => End,
        };
    }

    private static byte GetCategory(int codePoint)
    {
        // The letter ra of each script: it becomes a reph, or a form before the base.
        if (codePoint is 0x0930 or 0x09B0 or 0x09F0 or 0x0A30 or 0x0AB0 or 0x0B30 or 0x0BB0 or 0x0C30 or 0x0CB0 or 0x0D30)
            return Ra;

        switch (codePoint)
        {
            case 0x25CC:
                return DottedCircle;

            // The generic bases of the specifications: a mark is shown on them without a letter.
            case 0x00A0 or 0x00D7 or (>= 0x2010 and <= 0x2014) or 0x2022 or (>= 0x25FB and <= 0x25FE):
                return Placeholder;

            // Signs that take the marks of a consonant: the Bengali vedic anusvara, the Kannada spacing candrabindu
            // and the Malayalam vedic anusvara.
            case 0x09FC or 0x0C80 or 0x0D04:
                return Placeholder;

            // The Gujarati shadda and the Oriya overline are written like a nukta, after the character they mark.
            case 0x0AFB or 0x0B55:
                return Nukta;

            // The accents of Devanagari and the Vedic tone marks are written after everything else of a syllable.
            case 0x0953 or 0x0954:
                return SyllableModifier;

            case (>= 0x1CE2 and <= 0x1CE8) or 0x1CED:
                return VedicSign;

            // The Vedic forms of the anusvara and the signs of the Devanagari Extended block stand by themselves, and
            // can have a modifier.
            case (>= 0x1CE9 and <= 0x1CEC) or (>= 0x1CEE and <= 0x1CF1) or (>= 0xA8F2 and <= 0xA8F7):
                return Symbol;

            // The dot reph of Malayalam is written before the consonant it is drawn on.
            case 0x0D4E:
                return Repha;

            // The vowel carriers of Gurmukhi take vowel signs as consonants do.
            case 0x0A72 or 0x0A73:
                return Consonant;

            // The udaat of Gurmukhi is written with the vowel signs.
            case 0x0A51:
                return Matra;
        }

        return UnicodeProperties.GetIndicSyllabicCategory(codePoint) switch
        {
            UnicodeIndicSyllabicCategory.Avagraha => Symbol,
            UnicodeIndicSyllabicCategory.Bindu or UnicodeIndicSyllabicCategory.GeminationMark or UnicodeIndicSyllabicCategory.SyllableModifier or UnicodeIndicSyllabicCategory.Visarga => SyllableModifier,
            UnicodeIndicSyllabicCategory.CantillationMark => VedicSign,
            UnicodeIndicSyllabicCategory.Consonant or UnicodeIndicSyllabicCategory.ConsonantDead or UnicodeIndicSyllabicCategory.ConsonantHeadLetter or UnicodeIndicSyllabicCategory.ConsonantInitialPostfixed => Consonant,
            UnicodeIndicSyllabicCategory.ConsonantFinal or UnicodeIndicSyllabicCategory.ConsonantMedial or UnicodeIndicSyllabicCategory.ConsonantSubjoined or UnicodeIndicSyllabicCategory.ConsonantSucceedingRepha => ConsonantMedial,
            UnicodeIndicSyllabicCategory.ConsonantKiller or UnicodeIndicSyllabicCategory.PureKiller or UnicodeIndicSyllabicCategory.ReorderingKiller or UnicodeIndicSyllabicCategory.VowelDependent => Matra,
            UnicodeIndicSyllabicCategory.ConsonantPlaceholder or UnicodeIndicSyllabicCategory.BrahmiJoiningNumber or UnicodeIndicSyllabicCategory.Number or UnicodeIndicSyllabicCategory.NumberJoiner => Placeholder,
            UnicodeIndicSyllabicCategory.ConsonantPrecedingRepha => Repha,
            UnicodeIndicSyllabicCategory.ConsonantWithStacker => ConsonantWithStacker,
            UnicodeIndicSyllabicCategory.InvisibleStacker or UnicodeIndicSyllabicCategory.Virama => Halant,
            UnicodeIndicSyllabicCategory.Joiner => Joiner,
            UnicodeIndicSyllabicCategory.NonJoiner => NonJoiner,
            UnicodeIndicSyllabicCategory.Nukta or UnicodeIndicSyllabicCategory.ToneMark => Nukta,
            UnicodeIndicSyllabicCategory.RegisterShifter => RegisterShifter,
            UnicodeIndicSyllabicCategory.Vowel or UnicodeIndicSyllabicCategory.VowelIndependent => Vowel,
            _ => Other,
        };
    }

    /// <summary>
    /// Gets the place a vowel sign is moved to: the reordering classes of the specification of its script. The vowel
    /// sign ii of Gurmukhi is also told apart: it is the one sign that is written after a modifier (the bindi or the
    /// tippi it carries).
    /// </summary>
    private static byte GetMatraPosition(int codePoint, ref byte category)
    {
        // The udaat of Gurmukhi is drawn under the consonant, before the vowel signs.
        if (codePoint == 0x0A51)
            return BelowConsonant;

        var block = codePoint >> 7;
        switch (UnicodeProperties.GetIndicPositionalCategory(codePoint))
        {
            case UnicodeIndicPositionalCategory.Left or UnicodeIndicPositionalCategory.VisualOrderLeft:
                return PreMatra;

            case UnicodeIndicPositionalCategory.Top:
                return block switch
                {
                    Gurmukhi => AfterPost,
                    Oriya => AfterMain,
                    Telugu or Kannada => BeforeSubjoined,
                    _ => AfterSubjoined,
                };

            case UnicodeIndicPositionalCategory.Bottom:
                return block switch
                {
                    Gurmukhi or Gujarati or Tamil or Malayalam => AfterPost,
                    Telugu or Kannada => BeforeSubjoined,
                    _ => AfterSubjoined,
                };

            case UnicodeIndicPositionalCategory.Overstruck:
                return AfterMain;

            case UnicodeIndicPositionalCategory.NotApplicable:
                return End;

            default:
                // On the right, alone or with another part.
                if (codePoint == 0x0A40)
                {
                    category = MatraPost;
                }

                return block switch
                {
                    Devanagari => AfterSubjoined,
                    Telugu => codePoint <= 0x0C42 ? BeforeSubjoined : AfterSubjoined,
                    Kannada => codePoint is < 0x0CC3 or > 0x0CD6 ? BeforeSubjoined : AfterSubjoined,
                    Bengali or Gurmukhi or Gujarati or Oriya or Tamil or Malayalam => AfterPost,
                    _ => AfterSubjoined,
                };
        }
    }

    // The blocks of the scripts: the code points divided by 128.
    private const int Devanagari = 0x0900 >> 7;
    private const int Bengali = 0x0980 >> 7;
    private const int Gurmukhi = 0x0A00 >> 7;
    private const int Gujarati = 0x0A80 >> 7;
    private const int Oriya = 0x0B00 >> 7;
    private const int Tamil = 0x0B80 >> 7;
    private const int Telugu = 0x0C00 >> 7;
    private const int Kannada = 0x0C80 >> 7;
    private const int Malayalam = 0x0D00 >> 7;
}
