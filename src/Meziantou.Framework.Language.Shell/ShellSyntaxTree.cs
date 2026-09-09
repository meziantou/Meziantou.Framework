using Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Shell;

/// <summary>A shell script read from source text, together with what is wrong with it.</summary>
/// <example>
/// <code>
/// var tree = ShellSyntaxTree.ParseText("ls -l | wc -l", ShellDialect.Bash);
/// var updated = tree.WithChanges(new TextChange(new TextSpan(0, 2), "dir"));
/// </code>
/// </example>
public sealed class ShellSyntaxTree : SyntaxTree
{
    private readonly SourceText _text;
    private readonly ShellScriptSyntax _root;
    private readonly List<Diagnostic> _diagnostics;

    private ShellSyntaxTree(SourceText text, ShellParseOptions options, Syntax.InternalSyntax.ShellScriptSyntax green, List<Diagnostic> diagnostics)
    {
        _text = text;
        Options = options;
        _diagnostics = diagnostics;
        _root = (ShellScriptSyntax)green.CreateRed();
        _root.AttachToTree(this);
    }

    public ShellParseOptions Options { get; }

    /// <summary>Gets the dialect the text was read as.</summary>
    public ShellDialect Dialect => Options.Dialect;

    public override string? FilePath => null;

    public override SourceText GetText() => _text;

    /// <summary>Gets the script this tree holds.</summary>
    public new ShellScriptSyntax GetRoot() => _root;

    /// <summary>Gets everything wrong with the script, in the order the parser found it.</summary>
    public new IReadOnlyList<Diagnostic> GetDiagnostics() => _diagnostics;

    /// <summary>Parses <paramref name="text"/> as a complete script. Never throws; problems are reported as diagnostics.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="dialect"/> is <see langword="null"/>.</exception>
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
        Syntax.InternalSyntax.ShellScriptSyntax root;
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
    /// <exception cref="ArgumentNullException"><paramref name="dialect"/> is <see langword="null"/>.</exception>
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
        var statements = tree.GetRoot().Statements.Statements;
        if (statements.Count == 0)
            return SyntaxFactory.ShellSkippedText(default);

        for (var index = 1; index < statements.Count; index++)
        {
            if (BelongsTo(statements[index], statements[0]))
                continue;

            tree.AddTrailingContentDiagnostic(statements[index].FullSpan);
            break;
        }

        return statements[0];
    }

    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public ShellSyntaxTree WithChanges(params TextChange[] changes) => WithChanges((IEnumerable<TextChange>)changes);

    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public ShellSyntaxTree WithChanges(IEnumerable<TextChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return ParseText(_text.WithChanges(changes).Text, Options);
    }

    /// <summary>
    /// Returns the edit that turns <paramref name="oldTree"/>'s text into this tree's text. The common prefix and
    /// suffix are trimmed, so an edit in the middle of a script reports only the part that actually differs.
    /// </summary>
    /// <remarks>
    /// This compares the two texts rather than the two trees. Editing by text reparses, so the trees never share
    /// nodes and a structural comparison would report the whole script as replaced.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="oldTree"/> is <see langword="null"/>.</exception>
    public override IReadOnlyList<TextChange> GetChanges(SyntaxTree oldTree)
    {
        ArgumentNullException.ThrowIfNull(oldTree);

        var newText = GetText();

        return [.. newText.GetChangeRanges(oldTree.GetText()).Select(range =>
            new TextChange(range.Span, newText.ToString(new TextSpan(range.Span.Start, range.NewLength))))];
    }

    /// <summary>
    /// Compares this tree with <paramref name="other"/> structurally, ignoring whitespace and comments. Two scripts
    /// that differ only in formatting are equivalent; two scripts parsed as different dialects never are.
    /// </summary>
    public bool IsEquivalentTo(ShellSyntaxTree? other)
    {
        if (other is null || other.Dialect != Dialect)
            return false;

        return string.Equals(_text.Text, other._text.Text, StringComparison.Ordinal) || _root.IsEquivalentTo(other._root);
    }

    protected override SyntaxNode GetRootCore() => _root;

    protected override SyntaxTree WithChangedTextCore(SourceText newText) => ParseText(newText.Text, Options);

    protected override SyntaxTree WithRootCore(SyntaxNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return ParseText(root.ToFullString(), Options);
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
        => _diagnostics.Add(new Diagnostic("SHELL0101", "Unexpected content after the parsed statement.", DiagnosticSeverity.Error, new Location(span, _text)));
}
