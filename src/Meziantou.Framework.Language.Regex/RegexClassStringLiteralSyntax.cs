namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a JavaScript <c>v</c>-mode string disjunction inside a class, <c>\q{abc|def}</c>.</summary>
/// <remarks>
/// This is what lets a class match a sequence rather than a single character, which is why <c>v</c> mode calls its
/// members sets of strings rather than sets of characters.
/// </remarks>
public sealed partial class RegexClassStringLiteralSyntax : RegexSyntaxNode
{
    /// <summary>The text between the braces, alternatives and all.</summary>
    public string Value => TextToken.Text;

    /// <summary>The alternatives the disjunction lists.</summary>
    public IReadOnlyList<string> Alternatives => Value.Length == 0 ? [] : Value.Split('|');
}
