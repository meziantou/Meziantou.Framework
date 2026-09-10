namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a single ordinary character that matches itself.</summary>
public sealed partial class RegexLiteralSyntax : RegexAtomSyntax
{
    /// <summary>The first code unit of the character this literal matches.</summary>
    public char Value => LiteralToken.Text.Length > 0 ? LiteralToken.Text[0] : '\0';

    /// <summary>
    /// The code point this literal matches. In Unicode mode a surrogate pair is one atom, so this is the pair's code
    /// point rather than the first half of it.
    /// </summary>
    public int CodePoint => LiteralToken.Text.Length switch
    {
        0 => 0,
        1 => LiteralToken.Text[0],
        _ when char.IsSurrogatePair(LiteralToken.Text[0], LiteralToken.Text[1]) => char.ConvertToUtf32(LiteralToken.Text[0], LiteralToken.Text[1]),
        _ => LiteralToken.Text[0],
    };
}
