using Meziantou.Framework.Language.Ini.Internals;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Ini.Syntax.InternalSyntax;

/// <summary>Builds the immutable tree for an INI document, keeping every character of it.</summary>
/// <remarks>
/// Every entry is one line: a section header, or a key with its separator and value, which may continue on the lines
/// below it. What follows an entry on its line, other than a comment, is kept as skipped text and reported once.
/// </remarks>
internal sealed class LanguageParser
{
    private readonly Lexer _lexer;
    private readonly IniParseOptions _options;
    private readonly List<PendingDiagnostic> _pending = [];
    private readonly DuplicateTracker? _duplicates;
    private GreenToken _current;
    private LexerMode _currentMode;
    private int _currentFullStart;
    private int _previousTokenTextEnd;
    private bool _previousTokenEndedTheLine = true;
    private int _valueIndentation;

    public LanguageParser(SourceText source, IniParseOptions options)
    {
        _options = options;
        _lexer = new Lexer(source, options);
        _duplicates = options.ReportDuplicates ? new DuplicateTracker(options.NameComparer) : null;
        _currentMode = LexerMode.LineStart;
        _current = _lexer.Lex(_currentMode);
        _currentFullStart = _lexer.Position - _current.FullWidth;
    }

    private SyntaxKind CurrentKind => (SyntaxKind)_current.RawKind;

    public IniDocumentSyntax ParseDocument()
    {
        var mark = _pending.Count;
        var entries = new List<GreenNode?>();
        while (true)
        {
            EnsureMode(LexerMode.LineStart);
            if (CurrentKind == SyntaxKind.EndOfFileToken)
                break;

            var entryStart = _currentFullStart;
            var entry = ParseEntry();
            entries.Add(entry);

            // Every entry reads at least one character; if one ever does not, the rest of the line is skipped rather than
            // read forever.
            if (_currentFullStart == entryStart)
            {
                _previousTokenEndedTheLine = false;
            }

            // Every entry ends its line; what follows it on the same line is not the start of another one.
            if (!_previousTokenEndedTheLine)
            {
                EnsureMode(LexerMode.RestOfLine);
                if (CurrentKind != SyntaxKind.EndOfFileToken)
                {
                    entries.Add(ParseRestOfLine(entry is IniSectionSyntax ? "a section header" : "a property"));
                }
            }
        }

        var node = new IniDocumentSyntax(SyntaxFactory.List(entries.ToArray()), EatToken(LexerMode.LineStart), _options);

        return (IniDocumentSyntax)Finish(node, nodeFullStart: 0, mark);
    }

    private IniEntrySyntax ParseEntry()
        => CurrentKind == SyntaxKind.OpenBracketToken ? ParseSection() : ParseProperty();

    private IniSectionSyntax ParseSection()
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var openBracket = EatToken(LexerMode.SectionName);

        GreenToken name;
        if (!_previousTokenEndedTheLine && CurrentKind == SyntaxKind.KeyToken)
        {
            var nameStart = CurrentTextStart;
            name = EatToken(LexerMode.SectionName);
            _duplicates?.EnterSection(name.Text, nameStart, _pending);
        }
        else
        {
            AddErrorForMissingToken(IniDiagnosticDescriptors.ExpectedSectionName);
            name = SyntaxFactory.MissingToken(SyntaxKind.KeyToken);
            _duplicates?.EnterSection(name: null, position: 0, _pending);
        }

        GreenToken closeBracket;
        if (!_previousTokenEndedTheLine && CurrentKind == SyntaxKind.CloseBracketToken)
        {
            closeBracket = EatToken(LexerMode.LineStart);
        }
        else
        {
            AddErrorForMissingToken(IniDiagnosticDescriptors.ExpectedClosingBracket);

            // The line ends with the header, missing bracket or not, so the trivia ending it goes to the last token.
            if (name.IsMissing)
            {
                closeBracket = SyntaxFactory.MissingToken(SyntaxKind.CloseBracketToken, openBracket.TrailingTrivia);
                openBracket = openBracket.WithTrivia(openBracket.LeadingTrivia, trailingTrivia: null);
            }
            else
            {
                closeBracket = SyntaxFactory.MissingToken(SyntaxKind.CloseBracketToken, name.TrailingTrivia);
                name = name.WithTrivia(name.LeadingTrivia, trailingTrivia: null);
            }
        }

        var node = new IniSectionSyntax(openBracket, name, closeBracket);

        return (IniSectionSyntax)Finish(node, start, mark);
    }

    private IniPropertySyntax ParseProperty()
    {
        var mark = _pending.Count;
        var start = _currentFullStart;

        GreenToken key;
        if (CurrentKind == SyntaxKind.KeyToken)
        {
            _valueIndentation = _lexer.GetIndentation(CurrentTextStart);
            _duplicates?.AddKey(_current.Text, CurrentTextStart, _pending);
            key = EatToken(LexerMode.LineStart);
        }
        else
        {
            // The line starts with a separator: the key is missing, but the value is still read.
            _valueIndentation = _lexer.GetIndentation(CurrentTextStart);
            _pending.Add(new PendingDiagnostic(CurrentTextStart, 0, IniDiagnosticDescriptors.ExpectedKey, []));
            key = SyntaxFactory.MissingToken(SyntaxKind.KeyToken);
        }

        GreenToken separator;
        GreenToken value;
        GreenNode? continuations = null;
        if ((key.IsMissing || !_previousTokenEndedTheLine) && CurrentKind is SyntaxKind.EqualsToken or SyntaxKind.ColonToken)
        {
            separator = EatToken(LexerMode.Value);
            value = EatToken(LexerMode.LineStart);
            if (_options.AllowMultilineValues)
            {
                continuations = ParseContinuationLines();
            }
        }
        else
        {
            if (!_options.AllowKeysWithoutValue)
            {
                AddErrorForMissingToken(IniDiagnosticDescriptors.ExpectedSeparator);
            }

            // A key on its own still ends its line, so the trivia ending it goes to the missing value.
            separator = SyntaxFactory.MissingToken(SyntaxKind.EqualsToken);
            value = SyntaxFactory.MissingToken(SyntaxKind.ValueToken, key.TrailingTrivia);
            key = key.WithTrivia(key.LeadingTrivia, trailingTrivia: null);
        }

        var node = new IniPropertySyntax(key, separator, value, continuations);

        return (IniPropertySyntax)Finish(node, start, mark);
    }

    /// <summary>Reads the lines a value continues on: the ones below it that are indented more than its key.</summary>
    private GreenNode? ParseContinuationLines()
    {
        List<GreenNode?>? lines = null;
        while (_previousTokenEndedTheLine && _lexer.IsContinuation(_currentFullStart, _valueIndentation))
        {
            EnsureMode(LexerMode.ValueContinuation);
            (lines ??= []).Add(EatToken(LexerMode.LineStart));
        }

        return lines is null ? null : SyntaxFactory.List(lines.ToArray());
    }

    /// <summary>Keeps what follows an entry on its line, where INI allows nothing but a comment.</summary>
    private IniSkippedTextSyntax ParseRestOfLine(string entryDescription)
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        AddErrorAtCurrentToken(IniDiagnosticDescriptors.ExpectedEndOfLine, entryDescription, _current.Text);

        var node = new IniSkippedTextSyntax(SyntaxFactory.ListNode([EatToken(LexerMode.LineStart)]));

        return (IniSkippedTextSyntax)Finish(node, start, mark);
    }

    private int CurrentTextStart => _currentFullStart + _current.GetLeadingTriviaWidth();

    private GreenToken EatToken(LexerMode nextMode)
    {
        var eaten = _current;
        _previousTokenTextEnd = _currentFullStart + eaten.GetLeadingTriviaWidth() + eaten.Width;
        _previousTokenEndedTheLine = EndsTheLine(eaten.TrailingTrivia);
        _lexer.Position = _currentFullStart + eaten.FullWidth;
        _current = _lexer.Lex(nextMode);
        _currentMode = nextMode;
        _currentFullStart = _lexer.Position - _current.FullWidth;

        return eaten;
    }

    /// <summary>Reads the current token again the way <paramref name="mode"/> reads it, if it was read another way.</summary>
    private void EnsureMode(LexerMode mode)
    {
        if (_currentMode == mode)
            return;

        _lexer.Position = _currentFullStart;
        _current = _lexer.Lex(mode);
        _currentMode = mode;
    }

    private static bool EndsTheLine(GreenNode? trailingTrivia)
    {
        var count = GreenNodeList.Count(trailingTrivia);
        for (var i = 0; i < count; i++)
        {
            if (GreenNodeList.ElementAt(trailingTrivia, i)?.RawKind == (int)SyntaxKind.EndOfLineTrivia)
                return true;
        }

        return false;
    }

    private void AddErrorAtCurrentToken(DiagnosticDescriptor descriptor, params object?[] arguments)
        => _pending.Add(new PendingDiagnostic(CurrentTextStart, Math.Max(_current.Width, 1), descriptor, arguments));

    /// <summary>Records an error for something the text does not have.</summary>
    /// <remarks>
    /// The missing thing belongs right after what comes before it. When that ended its line, reporting it in front of the
    /// current token would report it against whatever begins the next line instead.
    /// </remarks>
    private void AddErrorForMissingToken(DiagnosticDescriptor descriptor, params object?[] arguments)
    {
        var position = _previousTokenEndedTheLine || CurrentKind == SyntaxKind.EndOfFileToken ? _previousTokenTextEnd : CurrentTextStart;
        _pending.Add(new PendingDiagnostic(position, 0, descriptor, arguments));
    }

    private GreenNode Finish(GreenNode node, int nodeFullStart, int mark)
    {
        if (_pending.Count == mark)
            return node;

        var diagnostics = new SyntaxDiagnosticInfo[_pending.Count - mark];
        for (var i = 0; i < diagnostics.Length; i++)
        {
            var diagnostic = _pending[mark + i];
            diagnostics[i] = new SyntaxDiagnosticInfo(Math.Max(0, diagnostic.Position - nodeFullStart), diagnostic.Width, diagnostic.Descriptor, diagnostic.Arguments);
        }

        _pending.RemoveRange(mark, diagnostics.Length);

        return node.WithAdditionalDiagnostics(diagnostics);
    }

    private readonly record struct PendingDiagnostic(int Position, int Width, DiagnosticDescriptor Descriptor, object?[] Arguments);

    /// <summary>Finds the section names and keys a document uses more than once.</summary>
    /// <remarks>Sections that share a name are one section, so a key is a duplicate if any of them already has it.</remarks>
    private sealed class DuplicateTracker(StringComparer comparer)
    {
        private readonly HashSet<string> _sections = new(comparer);
        private readonly Dictionary<string, HashSet<string>> _keysBySection = new(comparer);
        private HashSet<string> _currentKeys = new(comparer);
        private string? _currentSection;

        public void EnterSection(string? name, int position, List<PendingDiagnostic> pending)
        {
            _currentSection = name;
            if (name is null)
            {
                // Keys under a header without a name belong to no section that can be named again.
                _currentKeys = new HashSet<string>(comparer);
                return;
            }

            if (!_sections.Add(name))
            {
                pending.Add(new PendingDiagnostic(position, name.Length, IniDiagnosticDescriptors.DuplicateSection, [name]));
            }

            if (!_keysBySection.TryGetValue(name, out var keys))
            {
                keys = new HashSet<string>(comparer);
                _keysBySection.Add(name, keys);
            }

            _currentKeys = keys;
        }

        public void AddKey(string key, int position, List<PendingDiagnostic> pending)
        {
            if (!_currentKeys.Add(key))
            {
                var section = _currentSection is null ? "the global section" : $"the section '{_currentSection}'";
                pending.Add(new PendingDiagnostic(position, key.Length, IniDiagnosticDescriptors.DuplicateKey, [key, section]));
            }
        }
    }
}
