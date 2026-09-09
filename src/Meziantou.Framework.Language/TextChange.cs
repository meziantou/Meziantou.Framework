using System.Runtime.InteropServices;

namespace Meziantou.Framework.Language;

/// <summary>Represents a text replacement applied to a <see cref="SourceText"/> or to a syntax tree.</summary>
/// <example>
/// <code>
/// var change = new TextChange(new TextSpan(10, 5), "2.0.0");
/// var updated = tree.WithChanges(change);
/// </code>
/// </example>
[StructLayout(LayoutKind.Auto)]
public readonly struct TextChange : IEquatable<TextChange>
{
    public TextChange(TextSpan span, string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);

        Span = span;
        NewText = newText;
    }

    public TextSpan Span { get; }
    public string NewText { get; }

    public bool Equals(TextChange other) => Span == other.Span && string.Equals(NewText, other.NewText, StringComparison.Ordinal);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TextChange other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Span, NewText);
    public static bool operator ==(TextChange left, TextChange right) => left.Equals(right);
    public static bool operator !=(TextChange left, TextChange right) => !left.Equals(right);

    public override string ToString() => $"{Span} -> '{NewText}'";
}
