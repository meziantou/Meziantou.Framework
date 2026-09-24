using Meziantou.Framework.Language.Toml.Internals;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>Builds the immutable tree for an TOML document, keeping every character of it.</summary>
internal sealed class LanguageParser
{
    private readonly Lexer _lexer;
    private readonly List<PendingDiagnostic> _pending = [];
    private GreenToken _current;
    private int _currentFullStart;

    public LanguageParser(SourceText source)
    {
        _lexer = new Lexer(source);
        _current = _lexer.Lex();
        _currentFullStart = _lexer.Position - _current.FullWidth;
    }

    private SyntaxKind CurrentKind => (SyntaxKind)_current.RawKind;

    public TomlDocumentSyntax ParseDocument()
    {
        var mark = _pending.Count;
        var entries = new List<GreenNode?>();

        while (CurrentKind != SyntaxKind.EndOfFileToken)
        {
            entries.Add(ParseEntry());
        }

        var node = new TomlDocumentSyntax(SyntaxFactory.List(entries.ToArray()), EatToken());

        return (TomlDocumentSyntax)Finish(node, nodeFullStart: 0, mark);
    }

    private TomlEntrySyntax ParseEntry()
    {
        if (CurrentKind == SyntaxKind.OpenBracketToken)
            return ParseSection();

        if (CurrentKind == SyntaxKind.KeyToken)
            return ParsePropertyOrSkippedText();

        return ParseSkippedText(TomlDiagnosticDescriptors.UnexpectedToken, _current.Text);
    }

    private TomlTableSyntax ParseSection()
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var openBracket = EatToken(SyntaxKind.OpenBracketToken, TomlDiagnosticDescriptors.UnexpectedToken, _current.Text);

        GreenToken name;
        if (CurrentKind == SyntaxKind.KeyToken)
        {
            name = EatToken();
        }
        else
        {
            AddErrorForMissingToken(TomlDiagnosticDescriptors.ExpectedSectionName);
            name = SyntaxFactory.MissingToken(SyntaxKind.KeyToken);
        }

        var closeBracket = EatToken(SyntaxKind.CloseBracketToken, TomlDiagnosticDescriptors.ExpectedClosingBracket);
        var node = new TomlTableSyntax(openBracket, name, closeBracket);

        return (TomlTableSyntax)Finish(node, start, mark);
    }

    private TomlEntrySyntax ParsePropertyOrSkippedText()
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var key = EatToken(SyntaxKind.KeyToken, TomlDiagnosticDescriptors.ExpectedKey);
        if (CurrentKind is not SyntaxKind.EqualsToken)
        {
            AddErrorForMissingToken(TomlDiagnosticDescriptors.ExpectedSeparator);

            var tokens = new List<GreenNode?> { key };
            while (CurrentKind != SyntaxKind.EndOfFileToken && !CurrentStartsLine())
            {
                AddErrorAtCurrentToken(TomlDiagnosticDescriptors.UnexpectedToken, _current.Text);
                tokens.Add(EatToken());
            }

            var skipped = new TomlSkippedTextSyntax(SyntaxFactory.ListNode(tokens.ToArray()));

            return (TomlSkippedTextSyntax)Finish(skipped, start, mark);
        }

        var separator = EatSeparatorAndLexValue();
        var value = EatToken(SyntaxKind.ValueToken, TomlDiagnosticDescriptors.UnexpectedToken, _current.Text);
        var node = new TomlPropertySyntax(key, separator, value);

        return (TomlPropertySyntax)Finish(node, start, mark);
    }

    private TomlSkippedTextSyntax ParseSkippedText(DiagnosticDescriptor descriptor, params object?[] arguments)
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var tokens = new List<GreenNode?>();

        do
        {
            AddErrorAtCurrentToken(descriptor, arguments);
            tokens.Add(EatToken());
        }
        while (CurrentKind != SyntaxKind.EndOfFileToken && !CurrentStartsLine());

        var node = new TomlSkippedTextSyntax(SyntaxFactory.ListNode(tokens.ToArray()));

        return (TomlSkippedTextSyntax)Finish(node, start, mark);
    }

    private GreenToken EatToken()
    {
        var eaten = _current;
        _current = _lexer.Lex();
        _currentFullStart = _lexer.Position - _current.FullWidth;

        return eaten;
    }

    private GreenToken EatSeparatorAndLexValue()
    {
        var eaten = _current;
        _current = _lexer.LexValue();
        _currentFullStart = _lexer.Position - _current.FullWidth;

        return eaten;
    }

    private GreenToken EatToken(SyntaxKind kind, DiagnosticDescriptor descriptor, params object?[] arguments)
    {
        if (CurrentKind == kind)
            return EatToken();

        AddErrorForMissingToken(descriptor, arguments);

        return SyntaxFactory.MissingToken(kind);
    }

    private void AddErrorAtCurrentToken(DiagnosticDescriptor descriptor, params object?[] arguments)
        => _pending.Add(new PendingDiagnostic(_currentFullStart + _current.GetLeadingTriviaWidth(), Math.Max(_current.Width, 1), descriptor, arguments));

    private void AddErrorForMissingToken(DiagnosticDescriptor descriptor, params object?[] arguments)
        => _pending.Add(new PendingDiagnostic(_currentFullStart + _current.GetLeadingTriviaWidth(), 0, descriptor, arguments));

    private GreenNode Finish(GreenNode node, int nodeFullStart, int mark)
    {
        if (_pending.Count == mark)
            return node;

        var diagnostics = new SyntaxDiagnosticInfo[_pending.Count - mark];
        for (var i = 0; i < diagnostics.Length; i++)
        {
            var diagnostic = _pending[mark + i];
            diagnostics[i] = new SyntaxDiagnosticInfo(Math.Max(0, diagnostic.Start - nodeFullStart), diagnostic.Width, diagnostic.Descriptor, diagnostic.Arguments);
        }

        _pending.RemoveRange(mark, diagnostics.Length);

        return node.WithAdditionalDiagnostics(diagnostics);
    }

    private bool CurrentStartsLine() => ContainsEndOfLine(_current.LeadingTrivia);

    private static bool ContainsEndOfLine(GreenNode? node)
    {
        if (node is null)
            return false;

        if (node.IsTrivia)
            return node.RawKind == (int)SyntaxKind.EndOfLineTrivia;

        if (node.IsList)
        {
            for (var i = 0; i < node.SlotCount; i++)
            {
                if (ContainsEndOfLine(node.GetSlot(i)))
                    return true;
            }
        }

        return false;
    }

    private sealed record PendingDiagnostic(int Start, int Width, DiagnosticDescriptor Descriptor, object?[] Arguments);
}
