namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an input or output redirection attached to a command.</summary>
public sealed class ShellRedirectionSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellRedirectionSyntax(ShellSyntaxToken? ioNumberToken, ShellSyntaxToken operatorToken, ShellWordSyntax? target)
        : base(
            ShellSyntaxKind.Redirection,
            BuildText(ioNumberToken, operatorToken, target),
            (ioNumberToken ?? operatorToken)?.FullSpan.Start ?? 0,
            ioNumberToken is null ? [operatorToken!] : [ioNumberToken, operatorToken!])
    {
        IoNumberToken = ioNumberToken;
        OperatorToken = operatorToken!;
        Target = target;
        _childNodes = target is null ? [] : [target];
    }

    /// <summary>The optional file descriptor number that precedes the operator, as in <c>2&gt;</c>.</summary>
    public ShellSyntaxToken? IoNumberToken { get; }

    public ShellSyntaxToken OperatorToken { get; }

    /// <summary>The redirection target, or <see langword="null"/> when the source omitted it.</summary>
    public ShellWordSyntax? Target { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>
    /// The here-document body this redirection introduced, for <c>&lt;&lt;</c> and <c>&lt;&lt;-</c>. The body text
    /// lives after the command line, so it is not part of this node's own text.
    /// </summary>
    public PosixHereDocumentSyntax? HereDocument { get; internal set; }

    public ShellRedirectionSyntax WithTarget(ShellWordSyntax? target)
    {
        if (ReferenceEquals(target, Target))
            return this;

        return new ShellRedirectionSyntax(IoNumberToken, OperatorToken, target);
    }

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitRedirection(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitRedirection(this);

    private static string BuildText(ShellSyntaxToken? ioNumberToken, ShellSyntaxToken operatorToken, ShellWordSyntax? target)
    {
        ArgumentNullException.ThrowIfNull(operatorToken);

        return (ioNumberToken?.ToFullString() ?? string.Empty) + operatorToken.ToFullString() + (target?.ToFullString() ?? string.Empty);
    }
}
