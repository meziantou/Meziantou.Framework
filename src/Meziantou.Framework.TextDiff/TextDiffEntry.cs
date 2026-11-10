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

    /// <summary>Initializes a new entry that belongs to a moved block.</summary>
    /// <param name="operation">What happened to the chunk.</param>
    /// <param name="oldText">The chunk in the old text, or <see langword="null"/> for an insertion.</param>
    /// <param name="newText">The chunk in the new text, or <see langword="null"/> for a deletion.</param>
    /// <param name="moveId">The identifier shared by the entries of a moved block, or <see langword="null"/>.</param>
    public TextDiffEntry(TextDiffOperation operation, string? oldText, string? newText, int? moveId)
    {
        Operation = operation;
        OldText = oldText;
        NewText = newText;
        MoveId = moveId;
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

    /// <summary>
    /// Gets the identifier of the moved block this entry belongs to, or <see langword="null"/> when the chunk is not
    /// part of a move.
    /// </summary>
    /// <remarks>
    /// Moves are only detected when <see cref="TextDiffOptions.DetectMoves"/> is set. The <see cref="TextDiffOperation.Delete"/>
    /// entries where the block was and the <see cref="TextDiffOperation.Insert"/> entries where it is now share the same
    /// identifier. Identifiers start at 0 and follow the order of the deleted blocks.
    /// </remarks>
    public int? MoveId { get; }

    public bool Equals(TextDiffEntry other)
        => Operation == other.Operation && OldText == other.OldText && NewText == other.NewText && MoveId == other.MoveId;

    public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is TextDiffEntry other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Operation, OldText, NewText, MoveId);

    public static bool operator ==(TextDiffEntry left, TextDiffEntry right) => left.Equals(right);

    public static bool operator !=(TextDiffEntry left, TextDiffEntry right) => !left.Equals(right);

    public override string ToString() => MoveId is { } moveId ? $"{Operation} (move {moveId}): {Text}" : $"{Operation}: {Text}";
}
