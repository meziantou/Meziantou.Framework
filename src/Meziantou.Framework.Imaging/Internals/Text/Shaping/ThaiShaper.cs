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
    /// <exception cref="ImageResourceLimitException">The glyphs exceed <see cref="ImageResourceLimits.MaxTextGlyphs"/>.</exception>
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
}
