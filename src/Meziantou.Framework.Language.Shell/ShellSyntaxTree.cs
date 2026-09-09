using Meziantou.Framework.Language.Shell.Internals;

namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an immutable shell syntax tree with source text and diagnostics.</summary>
public sealed class ShellSyntaxTree
{
    private readonly List<Diagnostic> _diagnostics;

    private ShellSyntaxTree(SourceText sourceText, ShellParseOptions options, ShellScriptSyntax root, List<Diagnostic> diagnostics)
    {
        Text = sourceText.Text;
        SourceText = sourceText;
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
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public ShellScriptSyntax GetRoot() => Root;
    public IReadOnlyList<Diagnostic> GetDiagnostics() => Diagnostics;

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

        // Built once and handed to both the parser and the tree, so a diagnostic's location points at the same
        // source text instance the tree exposes.
        var source = SourceText.From(text);

        // The dialect family selects the parser; dialect features handle the differences within a family.
        ShellScriptSyntax root;
        IReadOnlyList<Diagnostic> diagnostics;
        switch (options.Dialect.Family)
        {
            case ShellDialectFamily.PowerShell:
                var powerShellParser = new PowerShellParser(source, options);
                root = powerShellParser.ParseScript();
                diagnostics = powerShellParser.Diagnostics;
                break;

            case ShellDialectFamily.Cmd:
                var cmdParser = new CmdParser(source, options);
                root = cmdParser.ParseScript();
                diagnostics = cmdParser.Diagnostics;
                break;

            default:
                var posixParser = new PosixParser(source, options);
                root = posixParser.ParseScript();
                diagnostics = posixParser.Diagnostics;
                break;
        }

        return new ShellSyntaxTree(source, options, root, [.. diagnostics]);
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

        for (var index = 1; index < statements.Count; index++)
        {
            if (BelongsTo(statements[index], statements[0]))
                continue;

            tree.AddTrailingContentDiagnostic(statements[index].FullSpan);
            break;
        }

        return statements[0];
    }

    /// <summary>
    /// Returns whether <paramref name="statement"/> is part of <paramref name="command"/> despite following it in the
    /// statement list.
    /// </summary>
    /// <remarks>
    /// A here-document body starts on the line after the operator that introduces it, so it is a statement of its own
    /// sitting after the command it belongs to. Reading it as content following the command would report every
    /// here-document as trailing content.
    /// </remarks>
    private static bool BelongsTo(ShellStatementSyntax statement, ShellStatementSyntax command)
    {
        if (statement is not PosixHereDocumentSyntax { Redirection: { } redirection })
            return false;

        foreach (var ancestor in redirection.AncestorsAndSelf())
        {
            if (ReferenceEquals(ancestor, command))
                return true;
        }

        return false;
    }

    private void AddTrailingContentDiagnostic(TextSpan span)
    {
        _diagnostics.Add(new Diagnostic("SHELL0101", "Unexpected content after the parsed statement.", DiagnosticSeverity.Error, new Location(span, SourceText)));
    }

    public ShellSyntaxTree WithChanges(params TextChange[] changes) => WithChanges((IEnumerable<TextChange>)changes);

    public ShellSyntaxTree WithChanges(IEnumerable<TextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return ParseText(SourceText.WithChanges(changes).Text, Options);
    }

    /// <summary>
    /// Returns the edit that turns <paramref name="oldTree"/>'s text into this tree's text. The common prefix and
    /// suffix are trimmed, so an edit in the middle of a script reports only the part that actually differs.
    /// </summary>
    public IReadOnlyList<TextChange> GetChanges(ShellSyntaxTree oldTree)
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

        return [new TextChange(
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
