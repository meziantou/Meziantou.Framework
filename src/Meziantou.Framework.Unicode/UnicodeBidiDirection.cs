namespace Meziantou.Framework;

/// <summary>Specifies the direction of a paragraph for the Unicode Bidirectional Algorithm.</summary>
public enum UnicodeBidiDirection
{
    /// <summary>The direction of the first strong character of the paragraph, and left-to-right when it has none.</summary>
    Auto = 0,

    /// <summary>Left-to-right: the embedding level of the paragraph is 0.</summary>
    LeftToRight = 1,

    /// <summary>Right-to-left: the embedding level of the paragraph is 1.</summary>
    RightToLeft = 2,
}
