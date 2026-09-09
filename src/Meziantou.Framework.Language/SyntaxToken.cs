using System.Diagnostics;
using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language;

/// <summary>A terminal of the grammar: the smallest piece of a tree that carries text.</summary>
/// <remarks>
/// A token is a view rather than an object of its own, so <see langword="default"/> is a valid, empty token rather
/// than something that throws. Compare against <see cref="None"/>, or test <see cref="RawKind"/>, to detect it.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public readonly struct SyntaxToken : IEquatable<SyntaxToken>
{
    private readonly GreenNode? _token;
    private readonly int _position;
    private readonly int _index;

    internal SyntaxToken(SyntaxNode? parent, GreenNode? token, int position, int index)
    {
        Debug.Assert(token is null || token.IsToken, "Only a token can back a SyntaxToken.");

        Parent = parent;
        _token = token;
        _position = position;
        _index = index;
    }

    /// <summary>Gets the token that is not there.</summary>
    public static SyntaxToken None => default;

    internal GreenNode? Node => _token;
    internal int Index => _index;
    internal int Position => _position;

    /// <summary>Gets the language-specific kind of this token as a plain integer.</summary>
    public int RawKind => _token?.RawKind ?? 0;

    /// <summary>Gets the node this token is a child of.</summary>
    public SyntaxNode? Parent { get; }

    /// <summary>Gets the range this token covers, including the trivia on both sides.</summary>
    public TextSpan FullSpan => new(_position, _token?.FullWidth ?? 0);

    /// <summary>Gets the range the token's own text covers.</summary>
    public TextSpan Span => new(SpanStart, _token?.Width ?? 0);

    public int SpanStart => _position + (_token?.GetLeadingTriviaWidth() ?? 0);

    /// <summary>Gets the token exactly as it was spelled in the source.</summary>
    public string Text => (_token as GreenToken)?.Text ?? string.Empty;

    /// <summary>Gets the value the text denotes, which is the text itself unless the language decoded something else.</summary>
    public string ValueText => (_token as GreenToken)?.ValueText ?? string.Empty;

    /// <summary>Gets the decoded value of the token, which may be a number or another type the language chose.</summary>
    public object? Value => _token?.GetValue();

    /// <summary>Gets a value indicating whether the parser inserted this token rather than reading it from the text.</summary>
    public bool IsMissing => _token?.IsMissing ?? false;

    public bool ContainsDiagnostics => _token?.ContainsDiagnostics ?? false;
    public bool ContainsAnnotations => _token?.ContainsAnnotations ?? false;
    public bool ContainsSkippedText => _token?.ContainsSkippedText ?? false;

    public bool HasLeadingTrivia => (_token as GreenToken)?.LeadingTrivia is not null;
    public bool HasTrailingTrivia => (_token as GreenToken)?.TrailingTrivia is not null;

    /// <summary>Gets the trivia that comes before this token's text.</summary>
    public SyntaxTriviaList LeadingTrivia
        => _token is GreenToken { LeadingTrivia: { } leading } ? new SyntaxTriviaList(this, leading, _position, index: 0) : default;

    /// <summary>Gets the trivia that comes after this token's text.</summary>
    public SyntaxTriviaList TrailingTrivia
    {
        get
        {
            if (_token is not GreenToken { TrailingTrivia: { } trailing } token)
                return default;

            var leadingWidth = token.GetLeadingTriviaWidth();
            var index = token.LeadingTrivia is { } leading ? (leading.IsList ? leading.SlotCount : 1) : 0;

            return new SyntaxTriviaList(this, trailing, _position + leadingWidth + token.Text.Length, index);
        }
    }

    /// <summary>Returns this token with <paramref name="trivia"/> in front of its text.</summary>
    public SyntaxToken WithLeadingTrivia(params SyntaxTrivia[] trivia) => WithLeadingTrivia((IEnumerable<SyntaxTrivia>?)trivia);

    /// <summary>Returns this token with <paramref name="trivia"/> in front of its text.</summary>
    public SyntaxToken WithLeadingTrivia(IEnumerable<SyntaxTrivia>? trivia)
    {
        if (_token is not GreenToken token)
            return this;

        return new SyntaxToken(parent: null, token.WithTrivia(SyntaxTriviaList.ToGreen(trivia), token.TrailingTrivia), position: 0, index: 0);
    }

    /// <summary>Returns this token with <paramref name="trivia"/> after its text.</summary>
    public SyntaxToken WithTrailingTrivia(params SyntaxTrivia[] trivia) => WithTrailingTrivia((IEnumerable<SyntaxTrivia>?)trivia);

    /// <summary>Returns this token with <paramref name="trivia"/> after its text.</summary>
    public SyntaxToken WithTrailingTrivia(IEnumerable<SyntaxTrivia>? trivia)
    {
        if (_token is not GreenToken token)
            return this;

        return new SyntaxToken(parent: null, token.WithTrivia(token.LeadingTrivia, SyntaxTriviaList.ToGreen(trivia)), position: 0, index: 0);
    }

    /// <summary>Returns this token carrying the trivia of <paramref name="token"/> on both sides.</summary>
    public SyntaxToken WithTriviaFrom(SyntaxToken token)
    {
        if (_token is not GreenToken green)
            return this;

        return new SyntaxToken(parent: null, green.WithTrivia((token._token as GreenToken)?.LeadingTrivia, (token._token as GreenToken)?.TrailingTrivia), position: 0, index: 0);
    }

    /// <summary>Returns the text of this token, excluding its trivia.</summary>
    public override string ToString() => _token?.ToString() ?? string.Empty;

    /// <summary>Returns the text of this token, including its trivia.</summary>
    public string ToFullString() => _token?.ToFullString() ?? string.Empty;

    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public void WriteTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        _token?.WriteTo(writer);
    }

    /// <summary>Determines whether the two tokens have the same kind, text, and trivia.</summary>
    public bool IsEquivalentTo(SyntaxToken token)
    {
        if (_token is null || token._token is null)
            return _token is null && token._token is null;

        return _token.IsEquivalentTo(token._token);
    }

    /// <summary>
    /// Determines whether the two tokens are backed by the very same immutable token, which happens when one tree was
    /// derived from the other and this token was left untouched.
    /// </summary>
    public bool IsIncrementallyIdenticalTo(SyntaxToken token) => _token is not null && ReferenceEquals(_token, token._token);

    public bool Equals(SyntaxToken other) => ReferenceEquals(Parent, other.Parent) && ReferenceEquals(_token, other._token) && _position == other._position && _index == other._index;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SyntaxToken other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Parent, _token, _position, _index);
    public static bool operator ==(SyntaxToken left, SyntaxToken right) => left.Equals(right);
    public static bool operator !=(SyntaxToken left, SyntaxToken right) => !left.Equals(right);

    private string GetDebuggerDisplay() => $"{_token?.KindText} {Span}: '{ToString()}'";
}
