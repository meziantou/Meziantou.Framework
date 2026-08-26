// Portions of this file are derived from dotnet/runtime, licensed to the .NET Foundation under the MIT license.
// See THIRD-PARTY-NOTICES.TXT in the project root.
//
// Source: src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
// Commit: 5ec6efc171b19c0e2d591fbd451920e8f43a1552
// Permalink: https://github.com/dotnet/runtime/blob/5ec6efc171b19c0e2d591fbd451920e8f43a1552/src/libraries/System.Text.RegularExpressions/src/System/Text/RegularExpressions/RegexParser.cs
//
// Changes: ScanBlank becomes ScanTrivia, which produces trivia and diagnostics instead of only advancing a position.

namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>Reads a pattern one character at a time and turns spans of it into tokens and trivia.</summary>
/// <remarks>
/// <para>
/// The scanner holds the one invariant the whole library rests on: <see cref="Position"/> is the first character that
/// no token and no trivia has claimed yet, and a token is always built by slicing the text between a remembered start
/// and the current position. A span can therefore never disagree with the text it came from.
/// </para>
/// <para>
/// Trivia is peeked before it is claimed, because whitespace in front of a construct belongs to whatever comes next,
/// which may be the caller's <c>)</c> rather than an atom of the current sequence.
/// </para>
/// </remarks>
internal sealed class RegexScanner
{
    private readonly List<RegexDiagnostic> _diagnostics;

    // A peek is cached so that asking twice costs nothing and, more importantly, does not report the diagnostics of an
    // unterminated comment twice. The cache is keyed on the options as well as the position: an inline option setter
    // changes whether whitespace is trivia without the position moving.
    private int _triviaStart = -1;
    private int _triviaEnd;
    private RegexPatternOptions _triviaOptions;
    private List<RegexSyntaxTrivia>? _triviaCache;
    private List<RegexDiagnostic>? _triviaDiagnostics;

    public RegexScanner(string text, List<RegexDiagnostic> diagnostics)
    {
        Text = text;
        _diagnostics = diagnostics;
    }

    public string Text { get; }

    /// <summary>The first character not yet claimed by a token or by trivia.</summary>
    public int Position { get; set; }

    public bool IsAtEnd => Position >= Text.Length;

    /// <summary>The character at the reading position, or <c>'\0'</c> at the end of the pattern.</summary>
    public char Current => Position < Text.Length ? Text[Position] : '\0';

    public char Peek(int offset = 1)
    {
        var index = Position + offset;

        return index >= 0 && index < Text.Length ? Text[index] : '\0';
    }

    public char CharAt(int index) => index >= 0 && index < Text.Length ? Text[index] : '\0';

    public void AddDiagnostic(TextSpan span, string id, string message) =>
        _diagnostics.Add(new RegexDiagnostic(id, message, RegexDiagnosticSeverity.Error, span));

    /// <summary>Builds a token covering the text from <paramref name="start"/> to the reading position.</summary>
    public RegexSyntaxToken Token(RegexSyntaxKind kind, int start, IReadOnlyList<RegexSyntaxTrivia>? leadingTrivia = null, string? valueText = null)
    {
        var text = Text[start..Position];
        var fullStart = leadingTrivia is { Count: > 0 } ? leadingTrivia[0].Span.Start : start;

        return new RegexSyntaxToken(kind, text, valueText, isMissing: false, leadingTrivia, trailingTrivia: null, fullStart);
    }

    /// <summary>Builds a zero-width token standing in for one the source did not contain.</summary>
    public RegexSyntaxToken MissingToken(RegexSyntaxKind kind, IReadOnlyList<RegexSyntaxTrivia>? leadingTrivia = null)
    {
        var fullStart = leadingTrivia is { Count: > 0 } ? leadingTrivia[0].Span.Start : Position;

        return new RegexSyntaxToken(kind, string.Empty, string.Empty, isMissing: true, leadingTrivia, trailingTrivia: null, fullStart);
    }

    /// <summary>Reports where the trivia at the reading position ends, without claiming it.</summary>
    public int PeekTriviaEnd(RegexPatternOptions options, RegexFlavor flavor)
    {
        EnsureTrivia(options, flavor);

        return _triviaEnd;
    }

    /// <summary>Claims the trivia at the reading position and reports whatever went wrong inside it.</summary>
    public IReadOnlyList<RegexSyntaxTrivia> TakeTrivia(RegexPatternOptions options, RegexFlavor flavor)
    {
        EnsureTrivia(options, flavor);
        Position = _triviaEnd;

        if (_triviaDiagnostics is not null)
        {
            _diagnostics.AddRange(_triviaDiagnostics);
        }

        var trivia = (IReadOnlyList<RegexSyntaxTrivia>?)_triviaCache ?? [];
        _triviaStart = -1;
        _triviaCache = null;
        _triviaDiagnostics = null;

        return trivia;
    }

    private void EnsureTrivia(RegexPatternOptions options, RegexFlavor flavor)
    {
        if (_triviaStart == Position && _triviaOptions == options)
            return;

        _triviaStart = Position;
        _triviaOptions = options;
        _triviaCache = null;
        _triviaDiagnostics = null;
        _triviaEnd = ScanTrivia(Position, options, flavor);
    }

    /// <summary>Scans the trivia starting at <paramref name="start"/> and returns where it ends.</summary>
    /// <remarks>
    /// Ported from the engine's <c>ScanBlank</c>, with its two conditions kept as they are. Whitespace and <c>#</c>
    /// comments are trivia only in extended mode, but a <c>(?#…)</c> comment is trivia in every mode, which is why its
    /// test is the <c>else</c> of the extended-mode one rather than nested inside it. A <c>#</c> comment ends at a line
    /// feed and does not include it, so the line feed is picked up by the whitespace pass on the next turn.
    /// </remarks>
    private int ScanTrivia(int start, RegexPatternOptions options, RegexFlavor flavor)
    {
        var extended = (options & RegexPatternOptions.IgnorePatternWhitespace) != RegexPatternOptions.None &&
            flavor.HasFeature(RegexFlavorFeatures.IgnorePatternWhitespace);
        var comments = flavor.HasFeature(RegexFlavorFeatures.CommentGroups);
        var position = start;

        while (true)
        {
            if (extended)
            {
                var whitespaceStart = position;
                while (position < Text.Length && RegexCharacterTables.IsSpace(Text[position]))
                {
                    position++;
                }

                if (position > whitespaceStart)
                {
                    Add(new RegexSyntaxTrivia(RegexSyntaxKind.WhitespaceTrivia, Text[whitespaceStart..position], whitespaceStart));
                }
            }

            if (extended && position < Text.Length && Text[position] == '#')
            {
                var commentStart = position;
                var lineFeed = Text.AsSpan(position).IndexOf('\n');
                position = lineFeed < 0 ? Text.Length : position + lineFeed;

                Add(new RegexSyntaxTrivia(RegexSyntaxKind.PatternCommentTrivia, Text[commentStart..position], commentStart));
            }
            else if (comments && position + 2 < Text.Length && Text[position + 2] == '#' && Text[position + 1] == '?' && Text[position] == '(')
            {
                var commentStart = position;
                var closeParen = Text.AsSpan(position).IndexOf(')');
                if (closeParen < 0)
                {
                    position = Text.Length;
                    AddDiagnosticToPeek(TextSpan.FromBounds(commentStart, position), RegexDiagnosticIds.UnterminatedComment, "Unterminated '(?#' comment: expected ')'.");
                }
                else
                {
                    position += closeParen + 1;
                }

                Add(new RegexSyntaxTrivia(RegexSyntaxKind.InlineCommentTrivia, Text[commentStart..position], commentStart));
            }
            else
            {
                break;
            }
        }

        return position;

        void Add(RegexSyntaxTrivia trivia)
        {
            _triviaCache ??= [];
            _triviaCache.Add(trivia);
        }
    }

    private void AddDiagnosticToPeek(TextSpan span, string id, string message)
    {
        _triviaDiagnostics ??= [];
        _triviaDiagnostics.Add(new RegexDiagnostic(id, message, RegexDiagnosticSeverity.Error, span));
    }
}
