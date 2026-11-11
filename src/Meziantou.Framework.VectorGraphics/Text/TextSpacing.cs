namespace Meziantou.Framework.VectorGraphics;

/// <summary>The letter spacing and the word spacing of the texts (<see cref="SceneText.LetterSpacing"/>, <see cref="SceneText.WordSpacing"/>).</summary>
internal static class TextSpacing
{
    // The largest magnitude of a spacing: the limit of the image renderer
    private const double MaxSpacing = 65536;

    /// <summary>Gets a spacing that can be drawn: 0 when it is not finite, clamped to the limit of the renderers.</summary>
    public static double Normalize(double spacing) => double.IsFinite(spacing) ? Math.Clamp(spacing, -MaxSpacing, MaxSpacing) : 0;

    /// <summary>Gets the width the spacing adds to a text: the letter spacing after each grapheme cluster, the word spacing after each space.</summary>
    public static double GetWidth(string text, double letterSpacing, double wordSpacing)
    {
        letterSpacing = Normalize(letterSpacing);
        wordSpacing = Normalize(wordSpacing);
        if (text.Length is 0 || (letterSpacing == 0 && wordSpacing == 0))
            return 0;

        var width = 0.0;
        foreach (var range in Unicode.EnumerateGraphemeClusters(text))
        {
            width += letterSpacing;
            if (IsWordSeparator(text[range.Start]))
            {
                width += wordSpacing;
            }
        }

        return width;
    }

    /// <summary>Indicates whether a character receives the word spacing.</summary>
    public static bool IsWordSeparator(char c) => c is ' ' or '\u00A0';
}
