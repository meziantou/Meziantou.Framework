using System.Runtime.InteropServices;

namespace Meziantou.Framework;

[StructLayout(LayoutKind.Auto)]
public readonly struct TextDiffEntry : IEquatable<TextDiffEntry>
{
    public TextDiffEntry(TextDiffOperation operation, string text)
    {
        Operation = operation;
        OldText = operation is TextDiffOperation.Insert ? null : text;
        NewText = operation is TextDiffOperation.Delete ? null : text;
    }

    public TextDiffEntry(TextDiffOperation operation, string? oldText, string? newText)
    {
        Operation = operation;
        OldText = oldText;
        NewText = newText;
    }

    public TextDiffOperation Operation { get; }

    /// <summary>
    /// Gets the chunk as it appears in the old text, or <see langword="null"/> when <see cref="Operation"/> is
    /// <see cref="TextDiffOperation.Insert"/>.
    /// </summary>
    public string? OldText { get; }

    /// <summary>
    /// Gets the chunk as it appears in the new text, or <see langword="null"/> when <see cref="Operation"/> is
    /// <see cref="TextDiffOperation.Delete"/>.
    /// </summary>
    /// <remarks>
    /// When an option such as <see cref="TextDiffOptions.IgnoreCase"/> or <see cref="TextDiffOptions.IgnoreWhitespace"/>
    /// makes two different chunks compare equal, an <see cref="TextDiffOperation.Equal"/> entry keeps both sides:
    /// <see cref="OldText"/> and <see cref="NewText"/> then differ. Append <see cref="NewText"/> for
    /// <see cref="TextDiffOperation.Equal"/> and <see cref="TextDiffOperation.Insert"/> entries to rebuild the new text.
    /// </remarks>
    public string? NewText { get; }

    /// <summary>
    /// Gets <see cref="OldText"/>, or <see cref="NewText"/> when <see cref="Operation"/> is
    /// <see cref="TextDiffOperation.Insert"/>.
    /// </summary>
    /// <remarks>
    /// For an <see cref="TextDiffOperation.Equal"/> entry this is the chunk from the <em>old</em> text, which is not
    /// necessarily the chunk from the new text. Use <see cref="OldText"/> and <see cref="NewText"/> to rebuild either
    /// side of the diff.
    /// </remarks>
    public string Text => (OldText ?? NewText)!;

    public bool Equals(TextDiffEntry other)
        => Operation == other.Operation && OldText == other.OldText && NewText == other.NewText;

    public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is TextDiffEntry other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Operation, OldText, NewText);

    public static bool operator ==(TextDiffEntry left, TextDiffEntry right) => left.Equals(right);

    public static bool operator !=(TextDiffEntry left, TextDiffEntry right) => !left.Equals(right);

    public override string ToString() => $"{Operation}: {Text}";
}
