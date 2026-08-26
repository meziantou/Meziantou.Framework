namespace Meziantou.Framework.Language.Regex;

/// <summary>
/// Represents a JavaScript <c>v</c>-mode class set operation: an intersection such as <c>[\w&amp;&amp;\p{L}]</c> or a
/// difference such as <c>[[a-z]--[aeiou]]</c>.
/// </summary>
/// <remarks>
/// The operation is n-ary because the grammar is: <c>[a--b--c]</c> is one difference of three operands rather than
/// two nested ones. Mixing operators at the same level is not allowed, so every operator here is the same.
/// </remarks>
public sealed class RegexClassSetOperationSyntax : RegexSyntaxNode
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexClassSetOperationSyntax(IReadOnlyList<RegexSyntaxNode>? operands, IReadOnlyList<RegexSyntaxToken>? operatorTokens, int fullStart = 0)
        : base(
            RegexSyntaxKind.ClassSetOperation,
            SeparatedNodes.BuildText(operands, operatorTokens),
            operands is { Count: > 0 } ? operands[0].FullSpan.Start : fullStart,
            operatorTokens ?? [])
    {
        Operands = Snapshot(operands);
        OperatorTokens = Snapshot(operatorTokens);
        _childNodes = [.. Operands];
    }

    /// <summary>The sets being combined.</summary>
    public IReadOnlyList<RegexSyntaxNode> Operands { get; }

    /// <summary>The operator between each pair of operands. <c>OperatorTokens[i]</c> follows <c>Operands[i]</c>.</summary>
    public IReadOnlyList<RegexSyntaxToken> OperatorTokens { get; }

    /// <summary>The operator text, <c>&amp;&amp;</c> or <c>--</c>, or an empty string when there is none.</summary>
    public string OperatorText => OperatorTokens.Count > 0 ? OperatorTokens[0].Text : string.Empty;

    /// <summary>Returns <see langword="true"/> for <c>&amp;&amp;</c>.</summary>
    public bool IsIntersection => OperatorText == "&&";

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitClassSetOperation(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitClassSetOperation(this);
}
