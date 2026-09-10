namespace Meziantou.Framework.Language.Regex;

/// <summary>
/// Represents a JavaScript <c>v</c>-mode class set operation: an intersection such as <c>[\w&amp;&amp;\p{L}]</c> or a
/// difference such as <c>[[a-z]--[aeiou]]</c>.
/// </summary>
/// <remarks>
/// The operation is n-ary because the grammar is: <c>[a--b--c]</c> is one difference of three operands rather than
/// two nested ones. Mixing operators at the same level is not allowed, so every operator here is the same.
/// </remarks>
public sealed partial class RegexClassSetOperationSyntax : RegexSyntaxNode
{
    /// <summary>Gets the operator between each pair of operands. The one at <c>i</c> follows the operand at <c>i</c>.</summary>
    public IEnumerable<SyntaxToken> OperatorTokens => Operands.GetSeparators();

    /// <summary>Gets the operator text, <c>&amp;&amp;</c> or <c>--</c>, or an empty string when there is none.</summary>
    public string OperatorText => Operands.SeparatorCount > 0 ? Operands.GetSeparator(0).Text : string.Empty;

    /// <summary>Gets a value indicating whether the operator is <c>&amp;&amp;</c>.</summary>
    public bool IsIntersection => string.Equals(OperatorText, "&&", StringComparison.Ordinal);
}
