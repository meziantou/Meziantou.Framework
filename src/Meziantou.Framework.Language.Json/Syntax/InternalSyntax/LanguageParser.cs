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
/// <para>
/// The closers of every enclosing construct are among those tokens, not only the construct's own. That is what lets
/// <c>{"a": [1, 2}</c> end the array at the brace and report the missing bracket, rather than skip the brace as
/// garbage and then report the object as unterminated too.
/// </para>
/// </remarks>
internal sealed class LanguageParser
{
    /// <summary>
    /// How deeply objects and arrays may nest before the rest is kept as skipped text.
    /// </summary>
    /// <remarks>
    /// The parser follows the shape of the document with its own call stack, so without a limit a deeply enough
    /// nested document would end the process instead of reporting anything. The limit is far above anything a
    /// document written to be read reaches, and well below the depth at which the stack runs out.
    /// </remarks>
    private const int MaxDepth = 256;

    private readonly Lexer _lexer;
    private readonly Blender? _blender;
    private readonly List<PendingDiagnostic> _pending = [];
    private GreenToken _current;
    private int _currentFullStart;
    private int _previousTokenTextEnd;
    private bool _previousTokenEndedTheLine;
    private int _depth;

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
        Closers = CloseBrace | CloseBracket,
    }

    public JsonDocumentSyntax ParseDocument()
    {
        var mark = _pending.Count;
        var values = new List<GreenNode?>();
        var hasRootValue = false;

        while (CurrentKind != SyntaxKind.EndOfFileToken)
        {
            // Nothing can start with these, so each is reported on its own and parsing picks up after it: a stray
            // closing brace does not take the rest of the document with it, nor stand in for the root value.
            if (!CanStartValue(CurrentKind))
            {
                values.Add(ParseStrayTokens());
                continue;
            }

            if (hasRootValue)
            {
                AddErrorAtCurrentToken(JsonDiagnosticDescriptors.UnexpectedDataAfterRootValue);
            }

            values.Add(ParseValue(TerminatorState.Document));
            hasRootValue = true;
        }

        // A document is exactly one value; whitespace and comments alone are not one.
        if (values.Count == 0)
        {
            AddErrorForMissingToken(JsonDiagnosticDescriptors.ExpectedValue);
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

    /// <summary>Gets the kind of the token after the current one, without moving past anything.</summary>
    private SyntaxKind PeekKind()
    {
        var position = _lexer.Position;
        var next = _lexer.Lex();
        _lexer.Position = position;

        return (SyntaxKind)next.RawKind;
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
            case SyntaxKind.OpenBracketToken:
                return ParseObjectOrArray(terminators);
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

    /// <summary>Parses an object or an array, and refuses to descend past <see cref="MaxDepth"/>.</summary>
    /// <remarks>
    /// Past the limit the rest of the construct is kept as skipped text, so the promise the whole parser is built on
    /// still holds: nothing is thrown, every character is reproduced, and what went wrong is a diagnostic.
    /// </remarks>
    private GreenNode ParseObjectOrArray(TerminatorState terminators)
    {
        if (_depth >= MaxDepth)
        {
            AddErrorAtCurrentToken(JsonDiagnosticDescriptors.NestingTooDeep, MaxDepth);

            // The depth is already said to be the problem, so the tokens below it are not each reported as a surprise.
            return ParseSkippedText(terminators, reportEachToken: false);
        }

        _depth++;
        try
        {
            return CurrentKind == SyntaxKind.OpenBraceToken ? ParseObject(terminators) : ParseArray(terminators);
        }
        finally
        {
            _depth--;
        }
    }

    private JsonSkippedTextSyntax ParseSkippedText(TerminatorState terminators, bool reportEachToken = true)
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var tokens = new List<GreenNode?>();

        while (!IsTerminator(CurrentKind, terminators))
        {
            if (reportEachToken)
            {
                AddErrorAtCurrentToken(JsonDiagnosticDescriptors.UnexpectedToken, _current.Text);
            }

            tokens.Add(EatToken());
        }

        var node = new JsonSkippedTextSyntax(SyntaxFactory.ListNode(tokens.ToArray()));

        return (JsonSkippedTextSyntax)Finish(node, start, mark);
    }

    /// <summary>Parses the tokens at the document level that cannot start a value, reporting each of them.</summary>
    private JsonSkippedTextSyntax ParseStrayTokens()
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var tokens = new List<GreenNode?>();

        while (CurrentKind != SyntaxKind.EndOfFileToken && !CanStartValue(CurrentKind))
        {
            AddErrorAtCurrentToken(JsonDiagnosticDescriptors.UnexpectedToken, _current.Text);
            tokens.Add(EatToken());
        }

        var node = new JsonSkippedTextSyntax(SyntaxFactory.ListNode(tokens.ToArray()));

        return (JsonSkippedTextSyntax)Finish(node, start, mark);
    }

    private JsonObjectSyntax ParseObject(TerminatorState outerTerminators)
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var openBrace = EatToken(SyntaxKind.OpenBraceToken, JsonDiagnosticDescriptors.ExpectedCharacter, "{");
        var members = new List<GreenNode?>();
        var closers = TerminatorState.EndOfFile | TerminatorState.CloseBrace | (outerTerminators & TerminatorState.Closers);

        while (!IsTerminator(CurrentKind, closers))
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

            members.Add(ParseMember(closers | TerminatorState.Comma));
        }

        ReportDuplicateNames(start + openBrace.FullWidth, members);

        var closeBrace = EatToken(SyntaxKind.CloseBraceToken, JsonDiagnosticDescriptors.ExpectedCharacter, "}");
        var node = new JsonObjectSyntax(openBrace, SyntaxFactory.ListNode(members.ToArray()), closeBrace);

        return (JsonObjectSyntax)Finish(node, start, mark);
    }

    private JsonMemberSyntax ParseMember(TerminatorState terminators)
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
        else if (CurrentKind is SyntaxKind.BadToken or SyntaxKind.NumberToken or SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword or SyntaxKind.NullKeyword
            && PeekKind() == SyntaxKind.ColonToken)
        {
            // A bare word before a colon is a name someone forgot to quote. Reading it as the name keeps the member
            // whole, where skipping it would lose the name, the colon, and the value together.
            AddErrorAtCurrentToken(JsonDiagnosticDescriptors.UnquotedPropertyName);
            nameToken = AsStringToken(EatToken());
        }
        else
        {
            AddErrorForMissingToken(JsonDiagnosticDescriptors.ExpectedPropertyName);
            nameToken = SyntaxFactory.MissingToken(SyntaxKind.StringToken);
        }

        var colonToken = EatToken(SyntaxKind.ColonToken, JsonDiagnosticDescriptors.ExpectedCharacter, ":");
        var value = ParseValue(terminators);
        var node = new JsonMemberSyntax(nameToken, colonToken, value);

        return (JsonMemberSyntax)Finish(node, start, mark);
    }

    private JsonArraySyntax ParseArray(TerminatorState outerTerminators)
    {
        var mark = _pending.Count;
        var start = _currentFullStart;
        var openBracket = EatToken(SyntaxKind.OpenBracketToken, JsonDiagnosticDescriptors.ExpectedCharacter, "[");
        var elements = new List<GreenNode?>();
        var closers = TerminatorState.EndOfFile | TerminatorState.CloseBracket | (outerTerminators & TerminatorState.Closers);

        while (!IsTerminator(CurrentKind, closers))
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

            elements.Add(ParseValue(closers | TerminatorState.Comma));
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

    /// <summary>Reports every member whose name an earlier member of the same object already has.</summary>
    /// <remarks>
    /// RFC 8259 only says names should be unique, and readers disagree on which of two values they keep, so this is a
    /// warning rather than an error. The positions are worked out from the widths of the members, which is why a
    /// member taken from the previous tree is checked like any other.
    /// </remarks>
    private void ReportDuplicateNames(int membersStart, List<GreenNode?> members)
    {
        var memberCount = (members.Count + 1) / 2;
        if (memberCount < 2)
            return;

        // Comparing each name with the ones before it costs nothing for the small objects most documents are made of.
        HashSet<string>? seen = memberCount > 8 ? new(StringComparer.Ordinal) : null;
        var position = membersStart;
        for (var i = 0; i < members.Count; i++)
        {
            var item = members[i];
            if (i % 2 == 0 && GetNameToken(item) is { } nameToken)
            {
                var name = nameToken.ValueText;
                if (seen is null ? IsNameUsedBefore(members, i, name) : !seen.Add(name))
                {
                    _pending.Add(new PendingDiagnostic(position + nameToken.GetLeadingTriviaWidth(), nameToken.Text.Length, JsonDiagnosticDescriptors.DuplicatePropertyName, [name]));
                }
            }

            position += item?.FullWidth ?? 0;
        }

        static bool IsNameUsedBefore(List<GreenNode?> members, int index, string name)
        {
            for (var i = 0; i < index; i += 2)
            {
                if (GetNameToken(members[i]) is { } other && string.Equals(other.ValueText, name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        // A name that is missing, or too broken to have a reliable value, is already reported for that.
        static GreenToken? GetNameToken(GreenNode? member)
            => member is JsonMemberSyntax && member.GetSlot(0) is GreenToken { IsMissing: false } token && token.GetDiagnostics().Length == 0 ? token : null;
    }

    private static GreenToken AsStringToken(GreenToken token)
    {
        var converted = SyntaxFactory.TokenWithValue(token.LeadingTrivia, SyntaxKind.StringToken, token.Text, token.Text, token.TrailingTrivia);
        var diagnostics = token.GetDiagnostics();

        return diagnostics.Length == 0 ? converted : (GreenToken)converted.WithAdditionalDiagnostics(diagnostics);
    }

    private static bool CanStartValue(SyntaxKind kind)
        => kind is SyntaxKind.OpenBraceToken or SyntaxKind.OpenBracketToken or SyntaxKind.StringToken or SyntaxKind.NumberToken
            or SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword or SyntaxKind.NullKeyword;

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
