using System.Runtime.InteropServices;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a text replacement operation applied to a <see cref="SourceText"/> or <see cref="RegexSyntaxTree"/>.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct RegexTextChange : IEquatable<RegexTextChange>
{
    public RegexTextChange(TextSpan span, string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        Span = span;
        NewText = newText;
    }

    public TextSpan Span { get; }
    public string NewText { get; }

    public bool Equals(RegexTextChange other) => Span == other.Span && string.Equals(NewText, other.NewText, StringComparison.Ordinal);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is RegexTextChange other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Span, NewText);
    public static bool operator ==(RegexTextChange left, RegexTextChange right) => left.Equals(right);
    public static bool operator !=(RegexTextChange left, RegexTextChange right) => !left.Equals(right);
}
