namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a POSIX bracket expression such as <c>[:alpha:]</c>, which is only meaningful inside a character class.</summary>
public sealed partial class RegexPosixCharacterClassSyntax : RegexSyntaxNode
{
    /// <summary>Returns <see langword="true"/> for the negated form, <c>[:^alpha:]</c>.</summary>
    public bool IsNegated => NameToken.Text is ['^', ..];

    /// <summary>The class name, without the negation marker.</summary>
    public string Name => NameToken.Text.TrimStart('^');
}
