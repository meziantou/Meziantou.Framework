using System.Diagnostics;
using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenTrivia = Meziantou.Framework.Language.InternalSyntax.SyntaxTrivia;

namespace Meziantou.Framework.Language;

/// <summary>A run of text a parser keeps but does not give meaning to, such as whitespace or a comment.</summary>
/// <remarks>Trivia belongs to a token: it is either the leading or the trailing trivia of the token it sits beside.</remarks>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public readonly struct SyntaxTrivia : IEquatable<SyntaxTrivia>
{
    private readonly GreenNode? _trivia;
    private readonly int _position;
    private readonly int _index;

    internal SyntaxTrivia(SyntaxToken token, GreenNode? trivia, int position, int index)
    {
        Token = token;
        _trivia = trivia;
        _position = position;
        _index = index;
    }

    internal GreenNode? UnderlyingNode => _trivia;
    internal int Index => _index;

    /// <summary>Gets the token this trivia belongs to.</summary>
    public SyntaxToken Token { get; }

    /// <summary>Gets the language-specific kind of this trivia as a plain integer.</summary>
    public int RawKind => _trivia?.RawKind ?? 0;

    public SyntaxNode? Parent => Token.Parent;

    /// <summary>Gets the range this trivia covers. Trivia has no trivia of its own, so this is also its full span.</summary>
    public TextSpan Span => new(_position, _trivia?.FullWidth ?? 0);

    public TextSpan FullSpan => Span;
    public int SpanStart => _position;

    public bool ContainsDiagnostics => _trivia?.ContainsDiagnostics ?? false;
    public bool ContainsAnnotations => _trivia?.ContainsAnnotations ?? false;

    /// <summary>Returns this trivia carrying <paramref name="annotations"/> as well as the ones it already has.</summary>
    public SyntaxTrivia WithAdditionalAnnotations(params SyntaxAnnotation[] annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        return _trivia is null ? this : new SyntaxTrivia(default, _trivia.WithAdditionalAnnotations(annotations), position: 0, index: 0);
    }

    /// <summary>Returns this trivia without <paramref name="annotations"/>.</summary>
    public SyntaxTrivia WithoutAnnotations(params SyntaxAnnotation[] annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        return _trivia is null ? this : new SyntaxTrivia(default, _trivia.WithoutAnnotations(annotations), position: 0, index: 0);
    }

    /// <summary>Gets all the annotations of this trivia.</summary>
    public IEnumerable<SyntaxAnnotation> GetAnnotations() => _trivia?.GetAnnotations() ?? [];

    /// <summary>Gets the annotations of this trivia of the given kind.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="annotationKind"/> is <see langword="null"/>.</exception>
    public IEnumerable<SyntaxAnnotation> GetAnnotations(string annotationKind)
    {
        ArgumentNullException.ThrowIfNull(annotationKind);

        return GetAnnotations().Where(annotation => string.Equals(annotation.Kind, annotationKind, StringComparison.Ordinal));
    }

    /// <summary>Determines whether this trivia carries <paramref name="annotation"/>.</summary>
    public bool HasAnnotation(SyntaxAnnotation? annotation) => annotation is not null && _trivia is not null && Array.IndexOf(_trivia.GetAnnotations(), annotation) >= 0;

    /// <summary>Determines whether this trivia carries an annotation of the given kind.</summary>
    public bool HasAnnotations(string annotationKind) => GetAnnotations(annotationKind).Any();

    /// <summary>Returns the text of this trivia.</summary>
    public override string ToString() => (_trivia as GreenTrivia)?.Text ?? string.Empty;

    /// <summary>Returns the text of this trivia, which is the same as <see cref="ToString"/> because trivia has no trivia.</summary>
    public string ToFullString() => ToString();

    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public void WriteTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        _trivia?.WriteTo(writer);
    }

    /// <summary>Determines whether the two trivia have the same kind and text.</summary>
    public bool IsEquivalentTo(SyntaxTrivia trivia)
    {
        if (_trivia is null || trivia._trivia is null)
            return _trivia is null && trivia._trivia is null;

        return _trivia.IsEquivalentTo(trivia._trivia);
    }

    public bool Equals(SyntaxTrivia other) => Token == other.Token && ReferenceEquals(_trivia, other._trivia) && _position == other._position && _index == other._index;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SyntaxTrivia other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Token, _trivia, _position, _index);
    public static bool operator ==(SyntaxTrivia left, SyntaxTrivia right) => left.Equals(right);
    public static bool operator !=(SyntaxTrivia left, SyntaxTrivia right) => !left.Equals(right);

    private string GetDebuggerDisplay() => $"{_trivia?.KindText} {Span}: '{ToString()}'";
}
