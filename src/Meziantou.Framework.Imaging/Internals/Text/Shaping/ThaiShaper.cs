namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What Thai and Lao need besides the features of the font.</summary>
/// <remarks>
/// <para>
/// Microsoft, "Developing OpenType Fonts for Thai Script" and "Developing OpenType Fonts for Lao Script": the font
/// does the shaping (<c>ccmp</c>) and places the marks (<c>mark</c>, <c>mkmk</c>); the shaping engine prepares one
/// character for it, the sara am.
/// </para>
/// <para>
/// <b>Sara am</b> is a vowel written with two signs: a ring above the consonant (the nikhahit) and the sara aa after
/// it. It is decomposed into these two characters, so that the font places the ring like any mark above. When the
/// consonant already has marks above it (a tone mark is written before the sara am), the ring goes before them: it is
/// drawn on the consonant, and the tone mark on the ring.
/// </para>
/// </remarks>
internal static class ThaiShaper
{
    private const int ThaiSaraAm = 0x0E33;
    private const int LaoSaraAm = 0x0EB3;

    /// <summary>Decomposes each sara am and puts its ring before the marks above the consonant.</summary>
    /// <param name="buffer">The characters of the run, with their clusters.</param>
    /// <exception cref="FontResourceLimitException">The glyphs exceed <see cref="FontResourceLimits.MaxTextGlyphs"/>.</exception>
    public static void DecomposeSaraAm(GlyphBuffer buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var codePoint = buffer.Info[i].CodePoint;
            if (codePoint is not (ThaiSaraAm or LaoSaraAm))
                continue;

            // In both blocks, the nikhahit is 26 characters after the sara am and the sara aa is the character before.
            buffer.Resize(i, 1, 2);
            TextShaper.SetCharacterProperties(ref buffer.Info[i], codePoint + 0x1A);
            TextShaper.SetCharacterProperties(ref buffer.Info[i + 1], codePoint - 1);

            var start = i;
            while (start > 0 && IsAboveMark(buffer.Info[start - 1].CodePoint))
            {
                start--;
            }

            if (start < i)
            {
                var ring = buffer.Info[i];
                buffer.Info[start..i].CopyTo(buffer.Info[(start + 1)..]);
                buffer.Info[start] = ring;
                buffer.MergeClusters(start, i + 2);
            }
            else if (i > 0)
            {
                // The ring is a mark of what precedes it: the two parts belong to that cluster.
                buffer.MergeClusters(i - 1, i + 2);
            }

            i++;
        }
    }

    // The marks drawn above a consonant: the classes ABOVE1 to ABOVE4 of the Thai specification and ABOVE1 and ABOVE2
    // of the Lao one, with the yamakkan Unicode added to Lao since.
    private static bool IsAboveMark(int codePoint)
    {
        return codePoint is 0x0E31 or (>= 0x0E34 and <= 0x0E37) or (>= 0x0E47 and <= 0x0E4E)
            or 0x0EB1 or (>= 0x0EB4 and <= 0x0EB7) or 0x0EBB or (>= 0x0EC8 and <= 0x0ECE);
    }

    /// <summary>The first value of <see cref="GlyphInfo.ShaperData"/> that is a form of the private use area: it is none of the values the shaper gives to spaces and to mirrored characters.</summary>
    private const int FirstPrivateUseForm = 0x40;

    /// <summary>
    /// Finds the forms the letters and the marks of a run take in a Thai font that has no substitution for Thai: the
    /// marks at another place and the letters without their descender, which a font made before OpenType Layout has
    /// as characters of the private use area.
    /// </summary>
    /// <param name="buffer">The characters of the run, in the order of the text, the sara am decomposed.</param>
    /// <remarks>
    /// <para>
    /// Theppitak Karoonboonyanan, "Thai Script Shaping" (https://linux.thai.net/~thep/th-otf/shaping.html). A font draws
    /// each mark at one level, where it overlaps nothing on a letter of the usual shape. The rules, for the marks
    /// that follow a consonant:
    /// </para>
    /// <list type="bullet">
    /// <item><description>a tone mark, or the thanthakhat, is drawn lower when no mark above precedes it;</description></item>
    /// <item><description>a mark above a consonant that has an ascender on its right (po pla, fo fa, fo fan) is drawn further left: the first one, and the tone marks;</description></item>
    /// <item><description>a vowel below a consonant that has a descender it cannot lose (do chada, to patak) is drawn lower;</description></item>
    /// <item><description>a consonant that has a descender it can lose (yo ying, tho than) loses it before a vowel below.</description></item>
    /// </list>
    /// <para>
    /// What the document does not say is what the reference shaper does: a second mark below is drawn lower, like a
    /// mark below something that is no consonant; after a first mark above, only a tone mark that is the second mark
    /// above a consonant with an ascender has a form.
    /// </para>
    /// <para>
    /// The forms are found before the marks are put in the order they are shaped in, from the order of the text, and
    /// recorded in the glyphs: <see cref="ApplyPrivateUseForms"/> gives them their glyphs once the characters have
    /// theirs.
    /// </para>
    /// </remarks>
    public static void RecordPrivateUseForms(GlyphBuffer buffer)
    {
        var info = buffer.Info;
        var consonant = -1;
        var consonantClass = ConsonantClass.None;
        var marksAbove = 0;
        var hasMarkBelow = false;
        for (var i = 0; i < info.Length; i++)
        {
            var character = info[i].CodePoint;
            var form = PrivateUseForm.None;
            if (IsVowelAbove(character))
            {
                // The first mark above an ascender moves left.
                form = consonantClass == ConsonantClass.Ascender && marksAbove == 0 ? PrivateUseForm.Left : PrivateUseForm.None;
                marksAbove++;
            }
            else if (character is >= 0x0E48 and <= 0x0E4C)
            {
                // A tone mark and the thanthakhat: lower when nothing is above yet, and further left above an ascender,
                // as the first or the second mark above.
                form = (consonantClass, marksAbove) switch
                {
                    (ConsonantClass.None, _) => PrivateUseForm.None,
                    (ConsonantClass.Ascender, 0) => PrivateUseForm.LowLeft,
                    (ConsonantClass.Ascender, 1) => PrivateUseForm.Left,
                    (_, 0) => PrivateUseForm.Low,
                    _ => PrivateUseForm.None,
                };
                marksAbove++;
            }
            else if (character is >= 0x0E38 and <= 0x0E3A)
            {
                // A vowel below, and the phinthu: lower under a descender that stays, under what is no consonant and
                // under another mark below; a descender that can go is removed.
                if (hasMarkBelow || consonantClass is ConsonantClass.StrictDescender or ConsonantClass.None)
                {
                    form = PrivateUseForm.Low;
                }
                else if (consonantClass == ConsonantClass.RemovableDescender && consonant >= 0)
                {
                    info[consonant].ShaperData = (byte)(FirstPrivateUseForm + (int)PrivateUseForm.WithoutDescender);
                }

                hasMarkBelow = true;
            }
            else
            {
                // Another character starts over: a consonant with its class, or what is no consonant.
                consonant = i;
                consonantClass = character switch
                {
                    0x0E1B or 0x0E1D or 0x0E1F => ConsonantClass.Ascender,
                    0x0E0D or 0x0E10 => ConsonantClass.RemovableDescender,
                    0x0E0E or 0x0E0F => ConsonantClass.StrictDescender,
                    >= 0x0E01 and <= 0x0E2E => ConsonantClass.Normal,
                    _ => ConsonantClass.None,
                };
                marksAbove = 0;
                hasMarkBelow = false;
                continue;
            }

            if (form != PrivateUseForm.None)
            {
                info[i].ShaperData = (byte)(FirstPrivateUseForm + (int)form);
            }
        }
    }

    /// <summary>
    /// Gives the glyphs of the forms <see cref="RecordPrivateUseForms"/> found: the glyph of the character of the
    /// private use area the font has for the form, at the code point of the fonts of Windows or at the one of the fonts
    /// of Mac OS. A letter or a mark whose form the font does not have keeps its glyph.
    /// </summary>
    public static void ApplyPrivateUseForms(GlyphBuffer buffer, FontFace face)
    {
        foreach (ref var info in buffer.Info)
        {
            if (info.ShaperData is < FirstPrivateUseForm or > FirstPrivateUseForm + (int)PrivateUseForm.WithoutDescender)
                continue;

            var (windows, mac) = GetPrivateUseCharacters(info.CodePoint, (PrivateUseForm)(info.ShaperData - FirstPrivateUseForm));
            info.ShaperData = 0;
            var glyph = windows == 0 ? 0 : face.MapCodePoint(windows);
            if (glyph == 0 && mac != 0)
            {
                glyph = face.MapCodePoint(mac);
            }

            if (glyph != 0)
            {
                info.Glyph = glyph;
            }
        }
    }

    /// <summary>Determines whether a character is a vowel above its consonant, or one of the signs that count as one for the place of a tone mark.</summary>
    private static bool IsVowelAbove(int codePoint) => codePoint is 0x0E31 or (>= 0x0E34 and <= 0x0E37) or 0x0E47 or 0x0E4D or 0x0E4E;

    /// <summary>Gets the characters of the private use area that are a form of a character, in the fonts of Windows and of Mac OS; 0 when there is none.</summary>
    private static (int Windows, int Mac) GetPrivateUseCharacters(int codePoint, PrivateUseForm form)
    {
        return (codePoint, form) switch
        {
            // The tone marks and the thanthakhat: low, low and left, left.
            ( >= 0x0E48 and <= 0x0E4C, PrivateUseForm.Low) => (0xF70A + (codePoint - 0x0E48), 0xF88B + (3 * (codePoint - 0x0E48))),
            ( >= 0x0E48 and <= 0x0E4C, PrivateUseForm.LowLeft) => (0xF705 + (codePoint - 0x0E48), 0xF88C + (3 * (codePoint - 0x0E48))),
            ( >= 0x0E48 and <= 0x0E4C, PrivateUseForm.Left) => (0xF713 + (codePoint - 0x0E48), 0xF88A + (3 * (codePoint - 0x0E48))),

            // The vowels above and the signs that go with them, left.
            (0x0E31, PrivateUseForm.Left) => (0xF710, 0xF884),
            ( >= 0x0E34 and <= 0x0E37, PrivateUseForm.Left) => (0xF701 + (codePoint - 0x0E34), 0xF885 + (codePoint - 0x0E34)),
            (0x0E47, PrivateUseForm.Left) => (0xF712, 0xF889),
            (0x0E4D, PrivateUseForm.Left) => (0xF711, 0xF899),

            // The vowels below and the phinthu, low.
            ( >= 0x0E38 and <= 0x0E3A, PrivateUseForm.Low) => (0xF718 + (codePoint - 0x0E38), 0xF89B + (codePoint - 0x0E38)),

            // The consonants without their descender.
            (0x0E0D, PrivateUseForm.WithoutDescender) => (0xF70F, 0xF89A),
            (0x0E10, PrivateUseForm.WithoutDescender) => (0xF700, 0xF89E),
            _ => (0, 0),
        };
    }

    private enum ConsonantClass
    {
        /// <summary>No consonant: the marks keep their glyph.</summary>
        None,
        Normal,
        Ascender,
        RemovableDescender,
        StrictDescender,
    }

    private enum PrivateUseForm
    {
        None,
        Low,
        LowLeft,
        Left,
        WithoutDescender,
    }
}
