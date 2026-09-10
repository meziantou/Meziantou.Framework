namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents an escape that stands for one character, such as <c>\n</c>, <c>\x41</c>, <c>\cA</c>, or <c>\052</c>.</summary>
public sealed partial class RegexCharacterEscapeSyntax : RegexAtomSyntax
{
    /// <summary>The character the escape stands for.</summary>
    public string Value => EscapeToken.ValueText;
}
