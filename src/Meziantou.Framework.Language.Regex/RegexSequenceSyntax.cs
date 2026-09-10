namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents one branch of an alternation: the terms that must match one after another.</summary>
/// <remarks>A branch can be empty, as both branches of <c>(|)</c> are.</remarks>
public sealed partial class RegexSequenceSyntax : RegexSyntaxNode
{
    /// <summary>Returns this sequence with different terms, or itself when nothing changed.</summary>
    public RegexSequenceSyntax WithTerms(IEnumerable<RegexTermSyntax>? terms) => Update(new SyntaxList<RegexTermSyntax>(terms ?? []));
}
