using System.Runtime.InteropServices;

namespace Meziantou.Framework.Language;

/// <summary>Describes a region of old text that was replaced, and how long the replacement is.</summary>
/// <remarks>
/// Unlike <see cref="TextChange"/>, a range does not carry the replacement text. It is what an incremental
/// parser needs: enough to map a position in the new text back to the old one.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct TextChangeRange : IEquatable<TextChangeRange>
{
    public TextChangeRange(TextSpan span, int newLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newLength);

        Span = span;
        NewLength = newLength;
    }

    /// <summary>Gets the replaced range, in the old text.</summary>
    public TextSpan Span { get; }

    /// <summary>Gets the length the range occupies in the new text.</summary>
    public int NewLength { get; }

    /// <summary>Gets the number of characters the text grew by, which is negative when it shrank.</summary>
    public int Delta => NewLength - Span.Length;

    /// <summary>Combines <paramref name="changes"/> into the single range that spans all of them.</summary>
    /// <param name="changes">The ranges to combine. They are expected to be ordered by <see cref="Span"/> and not to overlap.</param>
    /// <returns>A range covering every change, or <see langword="default"/> when there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public static TextChangeRange Collapse(IEnumerable<TextChangeRange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var start = int.MaxValue;
        var end = 0;
        var delta = 0;
        foreach (var change in changes)
        {
            start = Math.Min(start, change.Span.Start);
            end = Math.Max(end, change.Span.End);
            delta += change.Delta;
        }

        if (start == int.MaxValue)
            return default;

        var span = TextSpan.FromBounds(start, end);

        return new TextChangeRange(span, Math.Max(0, span.Length + delta));
    }

    public bool Equals(TextChangeRange other) => Span == other.Span && NewLength == other.NewLength;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TextChangeRange other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Span, NewLength);
    public static bool operator ==(TextChangeRange left, TextChangeRange right) => left.Equals(right);
    public static bool operator !=(TextChangeRange left, TextChangeRange right) => !left.Equals(right);

    public override string ToString() => $"{Span} -> {NewLength}";
}
