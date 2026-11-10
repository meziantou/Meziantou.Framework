using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>A line of a <see cref="UnifiedDiffHunk"/>: an unchanged line of context, a deleted line or an inserted line.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct UnifiedDiffLine : IEquatable<UnifiedDiffLine>
{
    internal UnifiedDiffLine(TextDiffOperation operation, string text)
    {
        Operation = operation;
        Text = text;
    }

    /// <summary>
    /// Gets <see cref="TextDiffOperation.Equal"/> for a line of context (prefix <c>' '</c>),
    /// <see cref="TextDiffOperation.Delete"/> for a deleted line (<c>'-'</c>) or <see cref="TextDiffOperation.Insert"/>
    /// for an inserted line (<c>'+'</c>).
    /// </summary>
    public TextDiffOperation Operation { get; }

    /// <summary>
    /// Gets the line without its prefix. It ends with <c>"\n"</c>, unless it is the last line of a text that does not end
    /// with a line feed (<c>\ No newline at end of file</c>).
    /// </summary>
    public string Text { get; }

    public bool Equals(UnifiedDiffLine other) => Operation == other.Operation && string.Equals(Text, other.Text, StringComparison.Ordinal);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is UnifiedDiffLine other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Operation, Text);

    public static bool operator ==(UnifiedDiffLine left, UnifiedDiffLine right) => left.Equals(right);

    public static bool operator !=(UnifiedDiffLine left, UnifiedDiffLine right) => !left.Equals(right);

    public override string ToString() => UnifiedDiff.GetPrefix(Operation) + Text;
}
