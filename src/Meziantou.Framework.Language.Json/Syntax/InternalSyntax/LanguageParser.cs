using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Json.Internals;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Json.Syntax.InternalSyntax;

/// <summary>Builds the immutable tree for a JSON document, keeping every character of it.</summary>
/// <remarks>
/// <para>
/// Whatever the text says, the tree reproduces it exactly and parsing finishes. Both follow from one rule: a value
/// knows what can follow it. <see cref="ParseValue"/> is told which tokens end the construct it sits in, and skips
/// everything up to the first of them, so it always consumes something unless it is already looking at one. Each loop
/// then excludes exactly the tokens it passes down, which is why none of them can spin.
/// </para>
/// </remarks>
internal sealed class LanguageParser
{
    private readonly Lexer _lexer;
    private readonly Blender? _blender;
    private readonly List<PendingDiagnostic> _pending = [];
    private GreenToken _current;
    private int _currentFullStart;
    private int _previousTokenTextEnd;
    private bool _previousTokenEndedTheLine;

    public LanguageParser(SourceText source, Blender? blender = null)
    {
        _lexer = new Lexer(source);
        _blender = blender;
        _current = _lexer.Lex();
        _currentFullStart = _lexer.Position - _current.FullWidth;
    }

    /// <summary>What a construct can end with. A value stops at the first of these it meets.</summary>
    [Flags]
    private enum TerminatorState
    {
        None = 0,
        EndOfFile = 1,
        Comma = 2,
        CloseBrace = 4,
        CloseBracket = 8,

        Document = EndOfFile,
        Object = EndOfFile | Comma | CloseBrace,
        Array = EndOfFile | Comma | CloseBracket,
    }

    public JsonDocumentSyntax ParseDocument()
    {
        var mark = _pending.Count;
        var values = new List<GreenNode?>();
        var hasRootValue = false;

        while (CurrentKind != SyntaxKind.EndOfFileToken)
        {
            if (hasRootValue)
            {
                AddErrorAtCurrentToken(JsonDiagnosticDescriptors.UnexpectedDataAfterRootValue);
            }

            values.Add(ParseValue(TerminatorState.Document));
            hasRootValue = true;
        }

        var node = new JsonDocumentSyntax(SyntaxFactory.List(values.ToArray()), EatToken());

        return (JsonDocumentSyntax)Finish(node, nodeFullStart: 0, mark);
    }

    private SyntaxKind CurrentKind => (SyntaxKind)_current.RawKind;

    private GreenToken EatToken()
    {
        var eaten = _current;
        _previousTokenTextEnd = _currentFullStart + eaten.GetLeadingTriviaWidth() + eaten.Text.Length;
        _previousTokenEndedTheLine = EndsTheLine(eaten.TrailingTrivia);
        _current = _lexer.Lex();
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

    private GreenNode ParseValue(TerminatorState terminators)
    {
        if (TryReuse(Blender.NodeContext.Value) is { } reused)
            return reused;

        switch (CurrentKind)
        {
            case SyntaxKind.OpenBraceToken:
                return ParseObject();
            case SyntaxKind.OpenBracketToken:
                return ParseArray();
            case SyntaxKind.StringToken:
                return new JsonStringSyntax(EatToken());
            case SyntaxKind.NumberToken:
                return new JsonNumberSyntax(EatToken());
            case SyntaxKind.TrueKeyword:
            case SyntaxKind.FalseKeyword:
            case SyntaxKind.NullKeyword:
                return new JsonLiteralSyntax(SyntaxFacts.GetLiteralExpression(CurrentKind), EatToken());
            case SyntaxKind.BadToken:
                return ParseSkippedText(terminators);
            default:
                AddErrorForMissingToken(JsonDiagnosticDescriptors.ExpectedValue);

                return ParseSkippedText(terminators);
        }
    }

    private JsonSkippedTextSyntax ParseSkippedText(TerminatorState terminators)
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var tokens = new List<GreenNode?>();

        while (!IsTerminator(CurrentKind, terminators))
        {
            AddErrorAtCurrentToken(JsonDiagnosticDescriptors.UnexpectedToken, _current.Text);
            tokens.Add(EatToken());
        }

        var node = new JsonSkippedTextSyntax(SyntaxFactory.ListNode(tokens.ToArray()));

        return (JsonSkippedTextSyntax)Finish(node, start, mark);
    }

    private JsonObjectSyntax ParseObject()
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var openBrace = EatToken(SyntaxKind.OpenBraceToken, JsonDiagnosticDescriptors.ExpectedCharacter, "{");
        var members = new List<GreenNode?>();

        while (CurrentKind is not SyntaxKind.EndOfFileToken and not SyntaxKind.CloseBraceToken)
        {
            if (CurrentKind == SyntaxKind.CommaToken)
            {
                if (!EndsWithNode(members))
                {
                    AddErrorAtCurrentToken(JsonDiagnosticDescriptors.UnexpectedComma);
                    members.Add(MissingMember());
                }

                members.Add(EatToken());
                continue;
            }

            if (EndsWithNode(members))
            {
                AddErrorForMissingToken(JsonDiagnosticDescriptors.ExpectedCommaOrEndOfObject);
                members.Add(SyntaxFactory.MissingToken(SyntaxKind.CommaToken));
            }

            members.Add(ParseMember());
        }

        var closeBrace = EatToken(SyntaxKind.CloseBraceToken, JsonDiagnosticDescriptors.ExpectedCharacter, "}");
        var node = new JsonObjectSyntax(openBrace, SyntaxFactory.ListNode(members.ToArray()), closeBrace);

        return (JsonObjectSyntax)Finish(node, start, mark);
    }

    private JsonMemberSyntax ParseMember()
    {
        if (TryReuse(Blender.NodeContext.Member) is JsonMemberSyntax reused)
            return reused;

        var mark = _pending.Count;
        var start = _currentFullStart;

        GreenToken nameToken;
        if (CurrentKind == SyntaxKind.StringToken)
        {
            nameToken = EatToken();
        }
        else
        {
            AddErrorForMissingToken(JsonDiagnosticDescriptors.ExpectedPropertyName);
            nameToken = SyntaxFactory.MissingToken(SyntaxKind.StringToken);
        }

        var colonToken = EatToken(SyntaxKind.ColonToken, JsonDiagnosticDescriptors.ExpectedCharacter, ":");
        var value = ParseValue(TerminatorState.Object);
        var node = new JsonMemberSyntax(nameToken, colonToken, value);

        return (JsonMemberSyntax)Finish(node, start, mark);
    }

    private JsonArraySyntax ParseArray()
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var openBracket = EatToken(SyntaxKind.OpenBracketToken, JsonDiagnosticDescriptors.ExpectedCharacter, "[");
        var elements = new List<GreenNode?>();

        while (CurrentKind is not SyntaxKind.EndOfFileToken and not SyntaxKind.CloseBracketToken)
        {
            if (CurrentKind == SyntaxKind.CommaToken)
            {
                if (!EndsWithNode(elements))
                {
                    AddErrorForMissingToken(JsonDiagnosticDescriptors.ExpectedValue);
                    elements.Add(new JsonSkippedTextSyntax(tokens: null));
                }

                elements.Add(EatToken());
                continue;
            }

            if (EndsWithNode(elements))
            {
                AddErrorForMissingToken(JsonDiagnosticDescriptors.ExpectedCommaOrEndOfArray);
                elements.Add(SyntaxFactory.MissingToken(SyntaxKind.CommaToken));
            }

            elements.Add(ParseValue(TerminatorState.Array));
        }

        var closeBracket = EatToken(SyntaxKind.CloseBracketToken, JsonDiagnosticDescriptors.ExpectedCharacter, "]");
        var node = new JsonArraySyntax(openBracket, SyntaxFactory.ListNode(elements.ToArray()), closeBracket);

        return (JsonArraySyntax)Finish(node, start, mark);
    }

    /// <summary>Takes the node the previous tree already has here, and moves the lexer past its text.</summary>
    private GreenNode? TryReuse(Blender.NodeContext context)
    {
        if (_blender?.TryTakeNode(_currentFullStart, context) is not { } reused)
            return null;

        // The reused node stands in for tokens that were never read, so the state the diagnostics depend on has to be
        // brought up to date from the node itself.
        var lastToken = reused.GetLastTerminal();
        _previousTokenTextEnd = _currentFullStart + reused.FullWidth - (lastToken?.GetTrailingTriviaWidth() ?? 0);
        _previousTokenEndedTheLine = EndsTheLine((lastToken as GreenToken)?.TrailingTrivia);

        _lexer.Position = _currentFullStart + reused.FullWidth;
        _current = _lexer.Lex();
        _currentFullStart = _lexer.Position - _current.FullWidth;

        return reused;
    }

    private static JsonMemberSyntax MissingMember()
        => new(SyntaxFactory.MissingToken(SyntaxKind.StringToken), SyntaxFactory.MissingToken(SyntaxKind.ColonToken), new JsonSkippedTextSyntax(tokens: null));

    /// <summary>A separated list alternates, so the next thing after a node has to be a separator.</summary>
    private static bool EndsWithNode(List<GreenNode?> items) => items.Count % 2 == 1;

    private static bool IsTerminator(SyntaxKind kind, TerminatorState terminators) => kind switch
    {
        SyntaxKind.EndOfFileToken => true,
        SyntaxKind.CommaToken => (terminators & TerminatorState.Comma) != TerminatorState.None,
        SyntaxKind.CloseBraceToken => (terminators & TerminatorState.CloseBrace) != TerminatorState.None,
        SyntaxKind.CloseBracketToken => (terminators & TerminatorState.CloseBracket) != TerminatorState.None,
        _ => false,
    };

    private static bool EndsTheLine(GreenNode? trailingTrivia)
    {
        if (trailingTrivia is null)
            return false;

        var count = GreenNodeList.Count(trailingTrivia);
        for (var i = 0; i < count; i++)
        {
            if (GreenNodeList.ElementAt(trailingTrivia, i)?.RawKind == (int)SyntaxKind.EndOfLineTrivia)
                return true;
        }

        return false;
    }

    private void AddErrorAtCurrentToken(DiagnosticDescriptor descriptor, params object?[] arguments)
        => _pending.Add(new PendingDiagnostic(_currentFullStart + _current.GetLeadingTriviaWidth(), _current.Text.Length, descriptor, arguments));

    /// <summary>
    /// Records an error for something the text does not have.
    /// </summary>
    /// <remarks>
    /// When the previous token ended its line, the missing thing belongs at the end of that line rather than in front
    /// of whatever begins the next one -- otherwise a missing value is reported against the brace on the line below.
    /// </remarks>
    private void AddErrorForMissingToken(DiagnosticDescriptor descriptor, params object?[] arguments)
    {
        if (_previousTokenEndedTheLine)
        {
            _pending.Add(new PendingDiagnostic(_previousTokenTextEnd, 0, descriptor, arguments));
            return;
        }

        AddErrorAtCurrentToken(descriptor, arguments);
    }

    private GreenNode Finish(GreenNode node, int nodeFullStart, int mark)
    {
        if (_pending.Count == mark)
            return node;

        var infos = new SyntaxDiagnosticInfo[_pending.Count - mark];
        for (var i = 0; i < infos.Length; i++)
        {
            var pending = _pending[mark + i];
            infos[i] = new SyntaxDiagnosticInfo(Math.Max(0, pending.Position - nodeFullStart), pending.Width, pending.Descriptor, pending.Arguments);
        }

        _pending.RemoveRange(mark, infos.Length);

        return node.WithAdditionalDiagnostics(infos);
    }

    private readonly record struct PendingDiagnostic(int Position, int Width, DiagnosticDescriptor Descriptor, object?[] Arguments);
}
