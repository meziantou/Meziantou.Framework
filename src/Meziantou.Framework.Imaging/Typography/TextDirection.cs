namespace Meziantou.Framework.Imaging.Typography;

/// <summary>The base direction of the paragraphs of a text.</summary>
public enum TextDirection
{
    /// <summary>
    /// The direction of each paragraph is the one of its first character that has a direction (Unicode Bidirectional
    /// Algorithm, rules P2 and P3); left to right for a paragraph without such a character.
    /// </summary>
    Auto = 0,

    /// <summary>Left to right.</summary>
    LeftToRight = 1,

    /// <summary>Right to left.</summary>
    RightToLeft = 2,
}
