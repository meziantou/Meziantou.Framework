namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a list of branches separated by <c>|</c>.</summary>
/// <remarks>
/// Every pattern body is an alternation, even one with a single branch and no <c>|</c> at all. Keeping the shape
/// uniform means a consumer never has to handle two spellings of the same thing.
/// </remarks>
public sealed partial class RegexAlternationSyntax : RegexSyntaxNode
{
    /// <summary>Gets the <c>|</c> that follows each branch. The one at <c>i</c> follows the branch at <c>i</c>.</summary>
    public IEnumerable<SyntaxToken> BarTokens => Branches.GetSeparators();

    /// <summary>Gets a value indicating whether the alternation has more than one branch.</summary>
    public bool HasAlternatives => Branches.Count > 1 || Branches.SeparatorCount > 0;
}
