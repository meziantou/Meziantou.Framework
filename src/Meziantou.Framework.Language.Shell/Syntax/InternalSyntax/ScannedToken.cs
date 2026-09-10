using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <summary>A token the parser has just read, together with the span it was read from.</summary>
/// <remarks>
/// A green token knows its width but not where it is, and the parsers need both: they report diagnostics against
/// absolute spans and decide what to do next from where a construct started. Carrying the two together is what lets
/// the parsers keep working the way they did while the tree underneath became position-independent.
/// </remarks>
internal readonly struct ScannedToken
{
    public ScannedToken(GreenToken green, int start, int end)
    {
        Green = green;
        Start = start;
        End = end;
    }

    /// <summary>Builds a token the parser is synthesising rather than reading straight out of the text.</summary>
    /// <remarks>
    /// <paramref name="fullStart"/> is where the token's trivia begins, so the token itself starts after it -- the
    /// same split the old red-only token drew between its span and its full span.
    /// </remarks>
    public ScannedToken(SyntaxKind kind, string text, string? valueText = null, bool isMissing = false, GreenNode? leadingTrivia = null, int fullStart = 0)
    {
        Green = isMissing
            ? SyntaxFactory.MissingToken(leadingTrivia, kind)
            : SyntaxFactory.TokenWithValue(leadingTrivia, kind, text, valueText ?? text, trailing: null);

        Start = fullStart + (leadingTrivia?.FullWidth ?? 0);
        End = Start + text.Length;
    }

    public GreenToken? Green { get; }

    /// <summary>Whether the parser actually read a token here. An absent one only ever reaches an optional slot.</summary>
    public bool IsPresent => Green is not null;

    public int Start { get; }
    public int End { get; }

    public TextSpan Span => TextSpan.FromBounds(Start, Math.Max(Start, End));

    public TextSpan FullSpan => TextSpan.FromBounds(Math.Max(0, Start - (Green?.GetLeadingTriviaWidth() ?? 0)), Math.Max(Start, End));

    public string Text => Green is { } green ? green.Text : "";
    public string ValueText => Green is { } green ? green.ValueText : "";
    public bool IsMissing => Green?.IsMissing ?? false;
    public SyntaxKind Kind => (SyntaxKind)(Green?.RawKind ?? 0);

    /// <summary>
    /// Unwraps the green token for a slot that requires one. An absent token is a parser bug rather than something a
    /// document can cause, because every optional slot takes the token itself rather than what it wraps.
    /// </summary>
    public static implicit operator GreenNode(ScannedToken token) => token.Green!;

    public override string ToString() => Text;
}
