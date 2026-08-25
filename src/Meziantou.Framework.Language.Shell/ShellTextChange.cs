using System.Runtime.InteropServices;

namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a text replacement operation applied to a <see cref="SourceText"/> or <see cref="ShellSyntaxTree"/>.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct ShellTextChange : IEquatable<ShellTextChange>
{
    public ShellTextChange(TextSpan span, string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        Span = span;
        NewText = newText;
    }

    public TextSpan Span { get; }
    public string NewText { get; }

    public bool Equals(ShellTextChange other) => Span == other.Span && string.Equals(NewText, other.NewText, StringComparison.Ordinal);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ShellTextChange other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Span, NewText);
    public static bool operator ==(ShellTextChange left, ShellTextChange right) => left.Equals(right);
    public static bool operator !=(ShellTextChange left, ShellTextChange right) => !left.Equals(right);
}
