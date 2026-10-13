namespace Meziantou.Framework.Imaging.Typography;

/// <summary>Whether words are hyphenated at the end of a line.</summary>
public enum TextHyphenation
{
    /// <summary>A word is broken where the text has a soft hyphen (U+00AD), and a hyphen is drawn there.</summary>
    Manual = 0,

    /// <summary>Words are not broken, not even at soft hyphens.</summary>
    None = 1,

    /// <summary>
    /// A word is also broken where the hyphenation patterns of its style allow it
    /// (<see cref="TextStyle.Hyphenator"/>); a word of a style without patterns is hyphenated as with <see cref="Manual"/>.
    /// </summary>
    Automatic = 2,
}
