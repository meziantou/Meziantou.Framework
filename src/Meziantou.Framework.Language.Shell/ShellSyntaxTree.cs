using Meziantou.Framework.Language.Shell.Internals;

namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an immutable shell syntax tree with source text and diagnostics.</summary>
public sealed class ShellSyntaxTree
{
    private readonly List<ShellDiagnostic> _diagnostics;

    private ShellSyntaxTree(string text, ShellParseOptions options, ShellScriptSyntax root, List<ShellDiagnostic> diagnostics)
    {
        Text = text;
        SourceText = SourceText.From(text);
        Options = options;
        Root = root;
        _diagnostics = diagnostics;
        Root.SetParentAndTree(parent: null, this);
    }

    public string Text { get; }
    public SourceText SourceText { get; }
    public ShellParseOptions Options { get; }

    /// <summary>The dialect the text was parsed as.</summary>
    public ShellDialect Dialect => Options.Dialect;

    public ShellScriptSyntax Root { get; }
    public IReadOnlyList<ShellDiagnostic> Diagnostics => _diagnostics;

    public ShellScriptSyntax GetRoot() => Root;
    public IReadOnlyList<ShellDiagnostic> GetDiagnostics() => Diagnostics;

    /// <summary>Parses <paramref name="text"/> as a complete script. Never throws; problems are reported as diagnostics.</summary>
    public static ShellSyntaxTree ParseText(string text, ShellDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        return ParseText(text, new ShellParseOptions(dialect));
    }

    /// <inheritdoc cref="ParseText(string, ShellDialect)"/>
    public static ShellSyntaxTree ParseText(string text, ShellParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        text ??= string.Empty;

        // The dialect family selects the parser; dialect features handle the differences within a family.
        ShellScriptSyntax root;
        IReadOnlyList<ShellDiagnostic> diagnostics;
        switch (options.Dialect.Family)
        {
            case ShellDialectFamily.PowerShell:
                var powerShellParser = new PowerShellParser(text, options);
                root = powerShellParser.ParseScript();
                diagnostics = powerShellParser.Diagnostics;
                break;

            case ShellDialectFamily.Cmd:
                var cmdParser = new CmdParser(text, options);
                root = cmdParser.ParseScript();
                diagnostics = cmdParser.Diagnostics;
                break;

            default:
                var posixParser = new PosixParser(text, options);
                root = posixParser.ParseScript();
                diagnostics = posixParser.Diagnostics;
                break;
        }

        return new ShellSyntaxTree(text, options, root, [.. diagnostics]);
    }

    /// <summary>
    /// Parses <paramref name="text"/> as a single command, pipeline, or command list. Content after the first
    /// statement is reported as <c>SHELL0101</c> and kept as skipped text so the backing tree still round-trips.
    /// </summary>
    public static ShellStatementSyntax ParseCommand(string text, ShellDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        return ParseCommand(text, new ShellParseOptions(dialect));
    }

    /// <inheritdoc cref="ParseCommand(string, ShellDialect)"/>
    public static ShellStatementSyntax ParseCommand(string text, ShellParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var tree = ParseText(text, options);
        var statements = tree.Root.Statements.Statements;
        if (statements.Count == 0)
        {
            var empty = new ShellSkippedTextSyntax([], 0);
            empty.SetParentAndTree(tree.Root, tree);

            return empty;
        }

        if (statements.Count > 1)
        {
            tree.AddTrailingContentDiagnostic(statements[1].FullSpan);
        }

        return statements[0];
    }

    /// <summary>
    /// Parses <paramref name="text"/> as a single expression. Content after the first expression is reported as
    /// <c>SHELL0101</c>. Text that is not a valid expression yields a node holding the original text plus a diagnostic.
    /// </summary>
    public static ShellExpressionSyntax ParseExpression(string text, ShellDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        return ParseExpression(text, new ShellParseOptions(dialect));
    }

    /// <inheritdoc cref="ParseExpression(string, ShellDialect)"/>
    public static ShellExpressionSyntax ParseExpression(string text, ShellParseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        text ??= string.Empty;
        var tree = ParseText(text, options);

        ShellExpressionSyntax expression;
        bool hasTrailingContent;
        if (options.Dialect.Family == ShellDialectFamily.PowerShell)
        {
            expression = new PowerShellParser(text, options).ParseSingleExpression(out hasTrailingContent);
        }
        else
        {
            // The POSIX and cmd families have no standalone expression grammar: `$(( ))`, `[[ ]]`, and `set /a`
            // text is kept verbatim rather than being given a structure it does not have.
            expression = new ShellRawExpressionSyntax(new ShellSyntaxToken(ShellSyntaxKind.BareTextToken, text, text));
            hasTrailingContent = false;
        }

        expression.SetParentAndTree(tree.Root, tree);
        if (hasTrailingContent)
        {
            tree.AddTrailingContentDiagnostic(TextSpan.FromBounds(expression.FullSpan.End, text.Length));
        }

        return expression;
    }

    private void AddTrailingContentDiagnostic(TextSpan span)
    {
        _diagnostics.Add(new ShellDiagnostic("SHELL0101", "Unexpected content after the parsed statement.", ShellDiagnosticSeverity.Error, span));
    }

    public ShellSyntaxTree WithChanges(params ShellTextChange[] changes) => WithChanges((IEnumerable<ShellTextChange>)changes);

    public ShellSyntaxTree WithChanges(IEnumerable<ShellTextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return ParseText(SourceText.WithChanges(changes).Text, Options);
    }

    /// <summary>
    /// Returns the edit that turns <paramref name="oldTree"/>'s text into this tree's text. The common prefix and
    /// suffix are trimmed, so an edit in the middle of a script reports only the part that actually differs.
    /// </summary>
    public IReadOnlyList<ShellTextChange> GetChanges(ShellSyntaxTree oldTree)
    {
        ArgumentNullException.ThrowIfNull(oldTree);

        var oldText = oldTree.Text;
        var newText = Text;
        if (string.Equals(oldText, newText, StringComparison.Ordinal))
            return [];

        var prefix = 0;
        var maxPrefix = Math.Min(oldText.Length, newText.Length);
        while (prefix < maxPrefix && oldText[prefix] == newText[prefix])
        {
            prefix++;
        }

        // Never split a surrogate pair: the two halves are not text on their own.
        if (prefix > 0 && char.IsHighSurrogate(oldText[prefix - 1]))
        {
            prefix--;
        }

        var suffix = 0;
        var maxSuffix = Math.Min(oldText.Length, newText.Length) - prefix;
        while (suffix < maxSuffix && oldText[oldText.Length - suffix - 1] == newText[newText.Length - suffix - 1])
        {
            suffix++;
        }

        if (suffix > 0 && char.IsLowSurrogate(oldText[oldText.Length - suffix]))
        {
            suffix--;
        }

        return [new ShellTextChange(
            TextSpan.FromBounds(prefix, oldText.Length - suffix),
            newText[prefix..(newText.Length - suffix)])];
    }

    /// <summary>
    /// Compares this tree with <paramref name="other"/> structurally, ignoring whitespace and comments. Two scripts
    /// that differ only in formatting are equivalent; two scripts parsed as different dialects never are.
    /// </summary>
    public bool IsEquivalentTo(ShellSyntaxTree? other)
    {
        if (other is null)
            return false;

        if (other.Dialect != Dialect)
            return false;

        return string.Equals(Text, other.Text, StringComparison.Ordinal) || Root.IsEquivalentTo(other.Root);
    }
}
