namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a single shell word: a command name, an argument, or a redirection target.</summary>
public sealed partial class ShellWordSyntax
{
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
                        var quotedValue = SyntaxFactory.ShellWord(quoted.Parts).Value;
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

    /// <summary>Returns this word rewritten as a single literal part holding <paramref name="text"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public ShellWordSyntax WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var replacement = SyntaxFactory.Literal(text);
        var leading = GetLeadingTrivia();

        return WithParts(new SyntaxList<ShellWordPartSyntax>(leading.Count == 0 ? replacement : replacement.WithLeadingTrivia(leading)));
    }
}
