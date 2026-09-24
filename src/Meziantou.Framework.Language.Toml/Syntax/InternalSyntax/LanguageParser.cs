using Meziantou.Framework.Language.Toml.Internals;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>Builds the immutable tree for a TOML document, keeping every character of it.</summary>
internal sealed class LanguageParser
{
    private readonly Lexer _lexer;
    private readonly List<PendingDiagnostic> _pending = [];
    private GreenNode _current;
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

        return ParseSkippedText(TomlDiagnosticDescriptors.UnexpectedToken, CurrentText);
    }

    private TomlTableSyntax ParseSection()
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var openBracket = EatToken(SyntaxKind.OpenBracketToken, TomlDiagnosticDescriptors.UnexpectedToken, CurrentText);

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
                AddErrorAtCurrentToken(TomlDiagnosticDescriptors.UnexpectedToken, CurrentText);
                tokens.Add(EatToken());
            }

            var skipped = new TomlSkippedTextSyntax(SyntaxFactory.ListNode(tokens.ToArray()));

            return (TomlSkippedTextSyntax)Finish(skipped, start, mark);
        }

        var separator = EatSeparatorAndLexValue();
        var valueStart = _currentFullStart;
        var value = EatValue();
        var valueText = value.ToFullString();
        if (value.RawKind != (int)SyntaxKind.TomlArray && !IsValidValue(valueText))
            _pending.Add(new PendingDiagnostic(valueStart + value.GetLeadingTriviaWidth(), Math.Max(value.Width, 1), TomlDiagnosticDescriptors.InvalidValue, [valueText.Trim()]));
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
        var eaten = (GreenToken)_current;
        _current = _lexer.Lex();
        _currentFullStart = _lexer.Position - _current.FullWidth;

        return eaten;
    }

    private GreenNode EatValue()
    {
        var eaten = _current;
        _current = _lexer.Lex();
        _currentFullStart = _lexer.Position - _current.FullWidth;
        return eaten;
    }

    private GreenToken EatSeparatorAndLexValue()
    {
        var eaten = (GreenToken)_current;
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

    private static bool IsValidValue(string text)
    {
        var value = text.Trim();
        if (value.Length == 0)
            return false;

        if (value is "true" or "false")
            return true;

        if ((value[0] is '"' or '\'') && IsCompleteString(value))
            return true;

        if (value[0] is '[' or '{')
            return IsBalancedContainer(value) && (value[0] == '[' ? IsValidContainerContents(value) : IsValidInlineTable(value));

        return Regex.IsMatch(value, """^[+-]?(?:0|[1-9](?:_?[0-9])*)(?:\.[0-9](?:_?[0-9])*)?(?:[eE][+-]?[0-9](?:_?[0-9])*)?$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)
            || DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)
            || Regex.IsMatch(value, """^[+-]?[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?$""", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }

    private static bool IsCompleteString(string value)
    {
        var quote = value[0];
        if (value.Length < 2 || value[^1] != quote)
            return false;

        if (quote == '\'')
            return true;

        var slashCount = 0;
        for (var i = value.Length - 2; i >= 0 && value[i] == '\\'; i--)
            slashCount++;

        return (slashCount & 1) == 0;
    }

    private static bool IsBalancedContainer(string value)
    {
        var depth = 0;
        char quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (quote is not '\0')
            {
                if (current == quote && (quote == '\'' || !IsEscaped(value, i)))
                    quote = '\0';
                continue;
            }

            if (current is '"' or '\'')
                quote = current;
            else if (current is '[' or '{')
                depth++;
            else if (current is ']' or '}')
                depth--;

            if (depth < 0)
                return false;
        }

        return quote is '\0' && depth == 0;
    }

    private static bool IsValidContainerContents(string value)
    {
        var inner = value[1..^1].Trim();
        if (inner.Length == 0)
            return true;

        foreach (var item in SplitTopLevel(inner))
        {
            var candidate = item.Trim();
            if (candidate.Length == 0 || !IsValidValue(candidate))
                return false;
        }

        return true;
    }

    private static bool IsValidInlineTable(string value)
    {
        var inner = value[1..^1].Trim();
        if (inner.Length == 0)
            return true;

        foreach (var item in SplitTopLevel(inner))
        {
            var separator = item.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || !IsValidValue(item[(separator + 1)..].Trim()))
                return false;
        }

        return true;
    }

    private static IEnumerable<string> SplitTopLevel(string value)
    {
        var start = 0;
        var depth = 0;
        char quote = '\0';
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (quote is not '\0')
            {
                if (current == quote && (quote == '\'' || !IsEscaped(value, i)))
                    quote = '\0';
            }
            else if (current is '"' or '\'')
                quote = current;
            else if (current is '[' or '{')
                depth++;
            else if (current is ']' or '}')
                depth--;
            else if (current == ',' && depth == 0)
            {
                yield return value[start..i];
                start = i + 1;
            }
        }

        yield return value[start..];
    }

    private static bool IsEscaped(string value, int position)
    {
        var count = 0;
        for (var i = position - 1; i >= 0 && value[i] == '\\'; i--)
            count++;

        return (count & 1) != 0;
    }

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

    private string CurrentText => _current is GreenToken token ? token.Text : _current.ToString();

    private GreenNode? CurrentLeadingTrivia => _current is GreenToken token ? token.LeadingTrivia : null;

    private bool CurrentStartsLine() => ContainsEndOfLine(CurrentLeadingTrivia);

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
