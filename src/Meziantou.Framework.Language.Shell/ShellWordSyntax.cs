namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a single shell word: a command name, an argument, or a redirection target.</summary>
public sealed class ShellWordSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellWordSyntax(IReadOnlyList<ShellWordPartSyntax> parts)
        : base(ShellSyntaxKind.Word, BuildFullText(parts ?? []), GetFullStart(parts))
    {
        Parts = parts ?? [];
        _childNodes = [.. Parts];
    }

    public IReadOnlyList<ShellWordPartSyntax> Parts { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>
    /// Returns the word text with quotes and escapes resolved, or <see langword="null"/> when the value depends on
    /// runtime expansion (a variable reference or command substitution).
    /// </summary>
    public string? Value
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var part in Parts)
            {
                switch (part)
                {
                    case ShellLiteralWordPartSyntax literal:
                        builder.Append(literal.Value);
                        break;
                    case ShellEscapeSequenceSyntax escape:
                        builder.Append(escape.Value);
                        break;
                    case ShellGlobSyntax glob:
                        builder.Append(glob.GlobToken.Text);
                        break;
                    case ShellQuotedStringSyntax quoted:
                        var quotedValue = new ShellWordSyntax(quoted.Parts).Value;
                        if (quotedValue is null)
                            return null;

                        builder.Append(quotedValue);
                        break;

                    // PowerShell keeps strings as expressions, so unwrap the ones that need no expansion.
                    case ShellEmbeddedExpressionSyntax { Expression: PowerShellLiteralExpressionSyntax literalExpression }:
                        builder.Append(literalExpression.Value);
                        break;

                    case ShellEmbeddedExpressionSyntax { Expression: PowerShellExpandableStringSyntax expandable }:
                        var expandableValue = GetExpandableStringValue(expandable);
                        if (expandableValue is null)
                            return null;

                        builder.Append(expandableValue);
                        break;

                    default:
                        return null;
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Replaces the whole word with a single literal <paramref name="text"/>, keeping the leading trivia of the
    /// original word so surrounding whitespace and comments survive the edit.
    /// </summary>
    public ShellWordSyntax WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var leadingTrivia = DescendantTokens().FirstOrDefault()?.LeadingTrivia;
        var token = new ShellSyntaxToken(ShellSyntaxKind.BareTextToken, text, text, leadingTrivia: leadingTrivia, fullStart: FullSpan.Start);

        return new ShellWordSyntax([new ShellLiteralWordPartSyntax(token)]);
    }

    /// <summary>
    /// Returns the text of an expandable string when every part is literal, or <see langword="null"/> when it
    /// embeds a variable or subexpression whose value is only known at runtime.
    /// </summary>
    private static string? GetExpandableStringValue(PowerShellExpandableStringSyntax expandable)
    {
        var builder = new StringBuilder();
        foreach (var part in expandable.Parts)
        {
            switch (part)
            {
                case ShellLiteralWordPartSyntax literal:
                    builder.Append(literal.Value);
                    break;
                case ShellEscapeSequenceSyntax escape:
                    builder.Append(escape.Value);
                    break;
                default:
                    return null;
            }
        }

        return builder.ToString();
    }

    public ShellWordSyntax WithParts(IEnumerable<ShellWordPartSyntax>? parts)
    {
        var updated = parts?.ToArray() ?? [];
        if (updated.SequenceEqual(Parts))
            return this;

        return new ShellWordSyntax(updated);
    }

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitWord(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitWord(this);

    private static int GetFullStart(IReadOnlyList<ShellWordPartSyntax>? parts) => parts is { Count: > 0 } ? parts[0].FullSpan.Start : 0;
}
